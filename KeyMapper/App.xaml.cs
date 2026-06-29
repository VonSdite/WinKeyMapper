using System;
using System.Linq;
using System.Windows;
using KeyMapper.Core;

namespace KeyMapper;

public partial class App : Application
{
    private const string TrayArgument = "--tray";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (SystemKeyMapper.TryRunStartupCommand(e.Args, out int exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;

        bool startInTray = e.Args.Any(a =>
            string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
        if (!startInTray)
            window.Show();
    }
}
