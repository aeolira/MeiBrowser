using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Dark.Net;

namespace GUI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Unhandled error:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                try { File.AppendAllText("meibrowser_crash.log",
                    $"[{DateTime.Now}] {args.Exception}\n"); } catch { }
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                try { File.AppendAllText("meibrowser_crash.log",
                    $"[{DateTime.Now}] Task: {args.Exception}\n"); } catch { }
                args.SetObserved();
            };

            try
            {
                DarkNet.Instance.SetCurrentProcessTheme(Theme.Dark);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to set dark theme: {ex.Message}", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

}
