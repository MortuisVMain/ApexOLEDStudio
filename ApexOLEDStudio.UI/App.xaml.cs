using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ApexOLEDStudio.UI;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                File.AppendAllText("crash.log", $"[AppDomain] {DateTime.Now}: {args.ExceptionObject}\n");
            }
            catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                File.AppendAllText("crash.log", $"[Dispatcher] {DateTime.Now}: {args.Exception}\n");
            }
            catch { }
        };
    }
}

