using System.Diagnostics;
using Avalonia.Threading;
using OpenTabletDriver;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;

namespace TabletDriver.UI;

internal sealed class DaemonClient() : RpcClient<IDriverDaemon>("OpenTabletDriver.Daemon"), IDisposable
{
    private Process? ownedProcess;
    public event Action<LogMessage>? Logged;
    public event Action<IEnumerable<TabletReference>>? Tablets;
    public event Action? Sync;

    protected override void OnConnected()
    {
        var proxy = Instance!;
        void Deliver(Action action) => Dispatcher.UIThread.Post(() =>
        {
            if (IsConnected && ReferenceEquals(proxy, Instance)) action();
        });
        proxy.Message += (_, value) => Deliver(() => Logged?.Invoke(value));
        proxy.TabletsChanged += (_, value) => Deliver(() => Tablets?.Invoke(value));
        proxy.Resynchronize += (_, _) => Deliver(() => Sync?.Invoke());
        base.OnConnected();
    }

    public void StartLocalDaemon()
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) ||
            ownedProcess is { HasExited: false } || OpenTabletDriver.Instance.Exists("OpenTabletDriver.Daemon")) return;
        // Prefer a separate daemon executable next to us (development layout), else run ourselves as the daemon.
        var separate = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows()
            ? "OpenTabletDriver.Daemon.exe" : "OpenTabletDriver.Daemon");
        var self = Environment.ProcessPath;
        var start = File.Exists(separate) ? new ProcessStartInfo(separate)
            : self != null ? new ProcessStartInfo(self, "--daemon")
            : null;
        if (start == null) return;
        start.WorkingDirectory = AppContext.BaseDirectory;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        ownedProcess?.Dispose();
        ownedProcess = Process.Start(start);
    }

    public void Dispose()
    {
        try { if (ownedProcess is { HasExited: false }) ownedProcess.Kill(); }
        catch (InvalidOperationException) { }
        finally { ownedProcess?.Dispose(); }
    }
}
