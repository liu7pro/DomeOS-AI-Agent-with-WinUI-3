using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace DomeOSAgentDev;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log("startup", ex);
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log("unhandled", e.Exception);
    }

    /// <summary>Writes a crash report next to the executable, so a startup failure is diagnosable.</summary>
    private static void Log(string stage, Exception? exception)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "crash.txt");
            File.AppendAllText(path,
                               DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + stage + "]\r\n"
                               + (exception?.ToString() ?? "(no exception object)") + "\r\n\r\n");
        }
        catch
        {
            // a crash log that cannot be written is not worth crashing over
        }
    }
}
