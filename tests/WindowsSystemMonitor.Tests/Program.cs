using System.Runtime.InteropServices;
using WindowsSystemMonitor.Core;

var tests = new (string Name, Func<Task> Run, bool Windows)[]
{
    ("Only stale temporary extensions are eligible", Selection, false),
    ("Exactly 24 hours old is excluded", AgeBoundary, false),
    ("Preview reports exact logical candidate bytes", PreviewBytes, false),
    ("Read-only files are skipped", ReadOnly, false),
    ("Missing root is an empty plan", MissingRoot, false),
    ("Drive root is rejected", DriveRoot, false),
    ("Canceled scan propagates cancellation", CancelScan, false),
    ("Scan entry cap is explicit", EntryCap, false),
    ("Directory links are not traversed", DirectoryLink, false),
    ("File links are not selected", FileLink, false),
    ("Linked root is rejected", RootLink, false),
    ("Plans cannot cross cleanup sessions", SessionOwnership, false),
    ("Deletion refuses unsupported platforms", UnsupportedPlatform, false),
    ("Confirmed deletion removes files but retains directories", Delete, true),
    ("Changed files are skipped", Changed, true),
    ("Replaced file with matching size and time is skipped", Replaced, true),
    ("Locked files are skipped", Locked, true),
    ("Hard-linked files are excluded", HardLinked, true),
    ("Parent replaced by link cannot redirect deletion", RedirectedParent, true),
    ("Canceled deletion preserves candidates", CancelDelete, true),
    ("Already deleted preview is safely skipped", RepeatDelete, true),
};

int passed = 0, failed = 0, skipped = 0;
foreach (var test in tests)
{
    if (test.Windows && !OperatingSystem.IsWindows()) { Console.WriteLine($"SKIP {test.Name} (Windows required)"); skipped++; continue; }
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); passed++; }
    catch (SkipException ex) { Console.WriteLine($"SKIP {test.Name}: {ex.Message}"); skipped++; }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {test.Name}: {ex}"); failed++; }
}
Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped; {tests.Length} total.");
return failed == 0 ? 0 : 1;

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task Expect<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static async Task Selection()
{
    using var fixture = new Fixture();
    fixture.File("old.tmp", 3); fixture.File("nested/old.TEMP", 5);
    fixture.File("recent.tmp", 7, fresh: true); fixture.File("notes.txt", 9);
    fixture.File("database.sqlite", 9); fixture.File("important.log", 9); fixture.File("session.json", 9);
    var plan = await fixture.Service.ScanAsync();
    Assert(plan.Candidates.Count == 2 && plan.EstimatedBytes == 8, "Only two stale temp files should be selected.");
    Assert(plan.SkippedCount == 5, "Excluded files must be counted.");
    Assert(File.Exists(Path.Combine(fixture.Root, "old.tmp")), "Scanning cannot delete.");
}

static async Task AgeBoundary()
{
    using var fixture = new Fixture();
    string path = fixture.File("boundary.tmp", 1);
    File.SetLastWriteTimeUtc(path, fixture.Now.AddHours(-24));
    Assert((await fixture.Service.ScanAsync()).Candidates.Count == 0, "Age boundary must be strict.");
}

static async Task PreviewBytes()
{
    using var fixture = new Fixture(); fixture.File("one.tmp", 17); fixture.File("two.temp", 31);
    var plan = await fixture.Service.ScanAsync();
    Assert(plan.EstimatedBytes == 48 && !plan.IsTruncated, "Preview byte count is incorrect.");
}

static async Task ReadOnly()
{
    using var fixture = new Fixture(); string path = fixture.File("protected.tmp", 10);
    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
    try { Assert((await fixture.Service.ScanAsync()).Candidates.Count == 0, "Read-only file was selected."); }
    finally { File.SetAttributes(path, FileAttributes.Normal); }
}

static async Task MissingRoot()
{
    using var fixture = new Fixture();
    var plan = await new CleanupService(Path.Combine(fixture.Root, "absent")).ScanAsync();
    Assert(plan.Candidates.Count == 0 && plan.EstimatedBytes == 0, "Absent Temp folder should be empty.");
}

static Task DriveRoot()
{
    return Expect<ArgumentException>(() => { _ = new CleanupService(Path.GetPathRoot(Path.GetTempPath())!); return Task.CompletedTask; });
}

static async Task CancelScan()
{
    using var fixture = new Fixture(); fixture.File("one.tmp", 1);
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await Expect<OperationCanceledException>(() => fixture.Service.ScanAsync(cancellation.Token));
}

static async Task EntryCap()
{
    using var fixture = new Fixture(); for (int i = 0; i < 5; i++) fixture.File($"{i}.tmp", 1);
    var plan = await new CleanupService(fixture.Root, () => fixture.Now, entryLimit: 2).ScanAsync();
    Assert(plan.IsTruncated && plan.Candidates.Count == 2, "Capped scans must show incomplete status.");
}

static async Task DirectoryLink()
{
    using var fixture = new Fixture(); using var outside = new Fixture(); outside.File("external.tmp", 10);
    MakeDirectoryLink(Path.Combine(fixture.Root, "linked"), outside.Root);
    Assert((await fixture.Service.ScanAsync()).Candidates.Count == 0, "Linked directory was traversed.");
}

static async Task FileLink()
{
    using var fixture = new Fixture(); using var outside = new Fixture(); string target = outside.File("external.tmp", 10);
    try { File.CreateSymbolicLink(Path.Combine(fixture.Root, "linked.tmp"), target); }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { throw new SkipException("File symlinks unavailable: " + ex.Message); }
    Assert((await fixture.Service.ScanAsync()).Candidates.Count == 0, "Linked file was selected.");
}

static async Task RootLink()
{
    using var fixture = new Fixture(); using var outside = new Fixture(); outside.File("external.tmp", 10);
    string link = Path.Combine(fixture.Root, "linked"); MakeDirectoryLink(link, outside.Root);
    await Expect<IOException>(() => new CleanupService(link).ScanAsync());
}

static async Task SessionOwnership()
{
    using var fixture = new Fixture(); fixture.File("one.tmp", 1);
    var plan = await fixture.Service.ScanAsync();
    await Expect<ArgumentException>(() => new CleanupService(fixture.Root).DeleteAsync(plan));
}

static async Task UnsupportedPlatform()
{
    if (OperatingSystem.IsWindows()) throw new SkipException("Non-Windows guard only.");
    using var fixture = new Fixture(); fixture.File("one.tmp", 1);
    var plan = await fixture.Service.ScanAsync();
    await Expect<PlatformNotSupportedException>(() => fixture.Service.DeleteAsync(plan));
}

static async Task Delete()
{
    using var fixture = new Fixture(); string a = fixture.File("one.tmp", 11), b = fixture.File("nested/two.tmp", 22);
    string preserved = fixture.File("document.txt", 33);
    var result = await fixture.Service.DeleteAsync(await fixture.Service.ScanAsync());
    Assert(result.DeletedCount == 2 && result.DeletedBytes == 33 && result.SkippedCount == 0, $"Incorrect deletion result: {result}");
    Assert(!File.Exists(a) && !File.Exists(b) && File.Exists(preserved), "Deletion did not match preview.");
    Assert(Directory.Exists(fixture.Root) && Directory.Exists(Path.GetDirectoryName(b)), "Cleanup removed a directory.");
}

static async Task Changed()
{
    using var fixture = new Fixture(); string path = fixture.File("one.tmp", 11);
    var plan = await fixture.Service.ScanAsync(); File.AppendAllText(path, "changed");
    var result = await fixture.Service.DeleteAsync(plan);
    Assert(result.DeletedCount == 0 && result.SkippedCount == 1 && File.Exists(path), "Changed file was deleted.");
}

static async Task Replaced()
{
    using var fixture = new Fixture(); string path = fixture.File("one.tmp", 11);
    var plan = await fixture.Service.ScanAsync(); DateTime modified = File.GetLastWriteTimeUtc(path);
    // Keep original alive under a new name so NTFS cannot recycle its ID for the replacement.
    File.Move(path, path + ".original"); File.WriteAllBytes(path, new byte[11]); File.SetLastWriteTimeUtc(path, modified);
    var result = await fixture.Service.DeleteAsync(plan);
    Assert(result.DeletedCount == 0 && result.SkippedCount == 1 && File.Exists(path), "Replacement file was deleted.");
}

static async Task Locked()
{
    using var fixture = new Fixture(); string path = fixture.File("one.tmp", 11);
    var plan = await fixture.Service.ScanAsync();
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    var result = await fixture.Service.DeleteAsync(plan);
    Assert(result.DeletedCount == 0 && result.SkippedCount == 1 && File.Exists(path), "Locked file was deleted.");
}

static async Task HardLinked()
{
    using var fixture = new Fixture(); string path = fixture.File("one.tmp", 11);
    if (!NativeTests.CreateHardLinkW(Path.Combine(fixture.Root, "two.tmp"), path, IntPtr.Zero))
        throw new SkipException($"Filesystem cannot create hard links (error {Marshal.GetLastWin32Error()}).");
    Assert((await fixture.Service.ScanAsync()).Candidates.Count == 0, "Hard links must not be selected.");
}

static async Task RedirectedParent()
{
    using var fixture = new Fixture(); using var outside = new Fixture(); fixture.File("nested/one.tmp", 11);
    string target = outside.File("one.tmp", 11); var plan = await fixture.Service.ScanAsync();
    Directory.Move(Path.Combine(fixture.Root, "nested"), Path.Combine(fixture.Root, "original"));
    MakeDirectoryLink(Path.Combine(fixture.Root, "nested"), outside.Root);
    var result = await fixture.Service.DeleteAsync(plan);
    Assert(result.DeletedCount == 0 && result.SkippedCount == 1 && File.Exists(target), "Parent-link replacement redirected cleanup.");
}

static async Task CancelDelete()
{
    using var fixture = new Fixture(); string path = fixture.File("one.tmp", 11); var plan = await fixture.Service.ScanAsync();
    using var source = new CancellationTokenSource(); source.Cancel();
    var result = await fixture.Service.DeleteAsync(plan, source.Token);
    Assert(result.Canceled && result.DeletedCount == 0 && result.UnprocessedCount == 1 && File.Exists(path), "Canceled cleanup removed a file.");
}

static async Task RepeatDelete()
{
    using var fixture = new Fixture(); fixture.File("one.tmp", 11); var plan = await fixture.Service.ScanAsync();
    await fixture.Service.DeleteAsync(plan); var second = await fixture.Service.DeleteAsync(plan);
    Assert(second.DeletedCount == 0 && second.DeletedBytes == 0 && second.SkippedCount == 1, "Repeated preview claimed duplicate recovery.");
}

static void MakeDirectoryLink(string link, string target)
{
    try { Directory.CreateSymbolicLink(link, target); }
    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { throw new SkipException("Directory symlinks unavailable: " + ex.Message); }
}

sealed class SkipException(string message) : Exception(message);

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "wsm-cleanup-tests-" + Guid.NewGuid().ToString("N"));
    public DateTime Now { get; } = DateTime.UtcNow;
    public CleanupService Service { get; }
    public Fixture() { Directory.CreateDirectory(Root); Service = new CleanupService(Root, () => Now); }
    public string File(string name, int length, bool fresh = false)
    {
        string path = Path.Combine(Root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[length]); System.IO.File.SetLastWriteTimeUtc(path, fresh ? Now : Now.AddDays(-2)); return path;
    }
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
}

static class NativeTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
}
