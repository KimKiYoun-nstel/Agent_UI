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

            var cfgDir = ConfigLocator.Resolve(Environment.GetCommandLineArgs());
            var vm = new MainViewModel(cfgDir, new ClockService());

            var win = new Views.MainWindow { DataContext = vm };
            win.Show();
        }
    }
}
