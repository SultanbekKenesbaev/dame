using System.Windows;

namespace DailyGate.Windows.Client;

public partial class App : System.Windows.Application
{
    private Mutex? instanceMutex;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        instanceMutex = new Mutex(initiallyOwned: true, "Local\\DailyGate.Client", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        if (instanceMutex is null) return;
        try { instanceMutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        instanceMutex.Dispose();
    }
}
