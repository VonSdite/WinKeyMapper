using System;
using System.Linq;
using System.Windows;

namespace KeyMapper;

public partial class App : Application
{
    private const string TrayArgument = "--tray";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;

        bool startInTray = e.Args.Any(a =>
            string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
            window.Show();
    }
}
