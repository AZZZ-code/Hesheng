using System;
using System.Threading;
using System.Windows;
namespace Hesheng;
public partial class App : Application {
    private Mutex? singleton;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? wait;
    protected override void OnStartup(StartupEventArgs e) {
        singleton = new Mutex(true, @"Local\Hesheng.Audio.Desktop", out bool first);
        activate = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Hesheng.Audio.Activate");
        if (!first) { activate.Set(); Shutdown(); return; }
        wait=ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>Dispatcher.BeginInvoke(new Action(()=>{if(MainWindow is Hesheng.MainWindow w)w.RestoreWindow();})),null,-1,false);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { wait?.Unregister(null); activate?.Dispose(); singleton?.Dispose(); base.OnExit(e); }
}
