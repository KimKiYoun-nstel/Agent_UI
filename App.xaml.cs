using System;
using System.Windows;
using Agent.UI.Wpf.Services;
using Agent.UI.Wpf.ViewModels;

namespace Agent.UI.Wpf
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var args = Environment.GetCommandLineArgs();
            var cfgDir = ConfigLocator.Resolve(args);
            var autoArg = args.Length > 1 ? args[1] : null;
            var vm = new MainViewModel(cfgDir, new ClockService(), autoArg);

            var win = new Views.MainWindow { DataContext = vm };
            win.Show();

            // If caller requested auto-connect, trigger it after window shows
            if (string.Equals(autoArg, "--autoconnect", StringComparison.OrdinalIgnoreCase))
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    // brief delay to let UI initialize
                    await System.Threading.Tasks.Task.Delay(200);
                    try
                    {
                        // invoke on UI thread
                        win.Dispatcher.Invoke(() =>
                        {
                            if (vm.ConnectCommand.CanExecute(null)) vm.ConnectCommand.Execute(null);
                        });
                    }
                    catch { }
                });
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            if (Current.MainWindow?.DataContext is IAsyncDisposable d)
            {
                try { await d.DisposeAsync(); } catch { }
            }
        }
    }
}
