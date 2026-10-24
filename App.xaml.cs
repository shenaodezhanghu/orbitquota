using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodexIsland;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            System.IO.File.Delete("self-test-error.txt");
            try { SelfTest.Run(); Shutdown(0); }
            catch (Exception ex) { System.IO.File.WriteAllText("self-test-error.txt", ex.ToString()); Shutdown(1); }
            return;
        }
        bool demo=e.Args.Contains("--demo");
        instance = new Mutex(true, @"Local\CodexIsland-" + (demo?"demo-":"") + Environment.UserName, out bool first);
        if (!first) { Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("运行遇到问题：" + args.Exception.Message, "OrbitQuota");
            args.Handled = true;
        };
        MainWindow = new IslandWindow(e.Args.Contains("--demo"));
        if(e.Args.Contains("--capture"))
        {
            MainWindow.Loaded += async (_,_) =>
            {
                try { await ((IslandWindow)MainWindow).Capture(); Shutdown(0); }
                catch(Exception ex) { System.IO.File.WriteAllText("capture-error.txt",ex.ToString()); Shutdown(1); }
            };
        }
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
