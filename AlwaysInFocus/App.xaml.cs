using System.Windows;
using WPFApp = System.Windows.Application;

namespace AlwaysInFocus
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : WPFApp
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Prevent automatic shutdown when main window is hidden; we'll call Shutdown() from tray exit.
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mw = new MainWindow();

            // Ensure window is created and tray icon initialized, but kept hidden and removed from taskbar.
            mw.WindowState = WindowState.Minimized;
            mw.ShowInTaskbar = false;
            mw.Hide();

            this.MainWindow = mw;
        }
    }
}
