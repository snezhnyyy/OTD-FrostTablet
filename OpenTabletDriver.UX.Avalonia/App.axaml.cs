using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TabletDriver.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            var cancel = new CancellationTokenSource();
            _ = Program.Listen(window.Restore, cancel.Token);
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _ = window.ShowError(e.Exception);
            };
            desktop.Exit += (_, _) => cancel.Cancel();
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static void SetTheme(bool light) =>
        Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
}
