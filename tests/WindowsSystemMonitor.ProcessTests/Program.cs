using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WindowsSystemMonitor.Services;

if (args.Length > 0 && args[0] == "--fixture")
{
    switch (args[1])
    {
        case "echo":
            Console.Write(JsonSerializer.Serialize(args.Skip(2).ToArray()));
            return 0;
        case "flood":
            await Console.Out.WriteAsync(new string('O', 200_000));
            await Console.Error.WriteAsync(new string('E', 200_000));
            return 0;
        case "exit":
            Console.Error.Write("expected test failure");
            return 19;
        case "wait":
            File.WriteAllText(args[2], Environment.ProcessId.ToString());
            await Task.Delay(Timeout.Infinite);
            return 0;
    }
    return 99;
}

var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test host path missing.");
var entryAssembly = Assembly.GetExecutingAssembly().Location;
string[] Fixture(params string[] fixtureArgs) =>
    Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? [entryAssembly, "--fixture", .. fixtureArgs]
        : ["--fixture", .. fixtureArgs];

var passed = 0;
var failed = 0;
async Task Check(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

await Check("arguments arrive literally without shell interpretation", async () =>
{
    using var runner = new ProcessRunner();
    string[] values = ["space separated", "quoted\"argument", "$HOME;$(echo injected)", "& echo injected | echo changed", "line\nbreak", "trailing\\"];
    var result = await runner.RunAsync(executable, Fixture(["echo", .. values]), CancellationToken.None);
    Assert(result.Succeeded, result.Summary);
    Assert(result.Output == JsonSerializer.Serialize(values), "An argument changed during invocation.");
});

await Check("stdout and stderr drain without deadlock and remain bounded", async () =>
{
    using var runner = new ProcessRunner();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var result = await runner.RunAsync(executable, Fixture("flood"), timeout.Token);
    Assert(result.Succeeded, result.Summary);
    Assert(result.Output.Contains(new string('O', 100)) && result.Output.Contains(new string('E', 100)), "Both streams were not captured.");
    Assert(result.Output.Contains("Output truncated") && result.Output.Length < 200_000, "Output bound was not enforced.");
});

await Check("real exit codes and stderr are preserved", async () =>
{
    using var runner = new ProcessRunner();
    var result = await runner.RunAsync(executable, Fixture("exit"), CancellationToken.None);
    Assert(result.ExitCode == 19 && !result.Succeeded, "Nonzero exit code was hidden.");
    Assert(result.Output.Contains("expected test failure"), "stderr was lost.");
});

async Task<int> WaitForPid(string path)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    while (true)
    {
        timeout.Token.ThrowIfCancellationRequested();
        try
        {
            if (File.Exists(path) && int.TryParse(await File.ReadAllTextAsync(path), out var pid)) return pid;
        }
        catch (IOException) { }
        await Task.Delay(30, timeout.Token);
    }
}
bool IsRunning(int pid)
{
    try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
    catch (ArgumentException) { return false; }
}

await Check("cancel terminates the owned child before returning", async () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"wsm-process-test-{Guid.NewGuid():N}");
    try
    {
        using var runner = new ProcessRunner();
        using var cancel = new CancellationTokenSource();
        var task = runner.RunAsync(executable, Fixture("wait", path), cancel.Token);
        var pid = await WaitForPid(path);
        cancel.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert(result.Canceled && !result.Succeeded, "Canceled command was reported as successful.");
        Assert(!IsRunning(pid), "Owned child remains running after cancellation.");
    }
    finally { File.Delete(path); }
});

await Check("dispose terminates the owned child", async () =>
{
    var path = Path.Combine(Path.GetTempPath(), $"wsm-process-test-{Guid.NewGuid():N}");
    try
    {
        using var runner = new ProcessRunner();
        var task = runner.RunAsync(executable, Fixture("wait", path), CancellationToken.None);
        var pid = await WaitForPid(path);
        runner.Dispose();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert(result.Canceled, "Dispose did not cancel the command.");
        Assert(!IsRunning(pid), "Owned child remains running after disposal.");
    }
    finally { File.Delete(path); }
});

await Check("unsafe or ambiguous package identifiers are rejected before launch", async () =>
{
    using var operations = new WindowsOperations();
    foreach (var id in new[] { "", "--all", "Publisher.App --silent", "a;calc.exe", "a\nb", "truncated…", new string('a', 201) })
    {
        var rejected = false;
        try { await operations.PreviewApplicationUpdateAsync(id, CancellationToken.None); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, $"Invalid ID was accepted: {id}");
    }
});

Console.WriteLine($"Process tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
