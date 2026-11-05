using WindowsSystemMonitor.UI;

namespace WindowsSystemMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool smokeTest = args.Contains("--smoke-test", StringComparer.Ordinal);
        bool smokeFailed = false;
        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            if (smokeTest) { smokeFailed = true; Application.Exit(); }
            else DarkDialog.ShowError(Form.ActiveForm, "Something went wrong", e.Exception.Message);
        };
        using var form = new MainForm(smokeTest);
        Application.Run(form);
        return smokeFailed ? 2 : form.ExitCode;
    }
}
