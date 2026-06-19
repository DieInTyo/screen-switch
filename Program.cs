namespace ScreenSwitch;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\ScreenSwitch.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: false, SingleInstanceMutexName);
        var ownsSingleInstance = false;
        try
        {
            ownsSingleInstance = singleInstance.WaitOne(0, false);
        }
        catch (AbandonedMutexException)
        {
            ownsSingleInstance = true;
        }

        if (!ownsSingleInstance)
        {
            DiagnosticLog.Info($"duplicate instance exit pid={Environment.ProcessId}");
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, args) => DiagnosticLog.Exception("thread exception", args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    DiagnosticLog.Exception("unhandled exception", exception);
                }
                else
                {
                    DiagnosticLog.Info($"unhandled exception object={args.ExceptionObject}");
                }
            };
            Application.Run(new TrayAppContext());
        }
        finally
        {
            singleInstance.ReleaseMutex();
        }
    }
}
