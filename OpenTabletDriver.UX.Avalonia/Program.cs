using System.IO.Pipes;
using Avalonia;
using Avalonia.Threading;

namespace TabletDriver.UI;

internal static class Program
{
    private const string Pipe = "OpenTabletDriver.UX.Avalonia.Singleton";
    internal static string[] Options { get; set; } = [];
    /// <summary>Rendering off-screen (UI snapshots): no tray icon, no window activation.</summary>
    internal static bool Headless { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // One executable for everything: "--daemon" runs the driver itself (no UI), otherwise the app.
        if (args.Length > 0 && args[0] == "--daemon")
            return RunDaemon(args[1..]);

        using var mutex = new Mutex(true, Pipe + ".Mutex", out var first);
        if (!first)
        {
            using var client = new NamedPipeClientStream(".", Pipe, PipeDirection.Out);
            try { client.Connect(5000); }
            catch (TimeoutException) { return 1; }
            return 0;
        }
        Options = args;
        return AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }

    private static int RunDaemon(string[] args)
    {
        var entry = typeof(OpenTabletDriver.Daemon.DriverDaemon).Assembly.EntryPoint
            ?? throw new InvalidOperationException("Daemon entry point not found");
        var result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { args });
        if (result is Task task) task.GetAwaiter().GetResult();
        return result is int code ? code : 0;
    }

    internal static async Task Listen(Action activate, CancellationToken token)
    {
        using var server = new NamedPipeServerStream(Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            while (!token.IsCancellationRequested)
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                Dispatcher.UIThread.Post(activate);
                server.Disconnect();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
