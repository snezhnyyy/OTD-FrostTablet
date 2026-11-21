using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Reflection.Metadata;

namespace TabletDriver.UI;

public partial class MainWindow
{
    private sealed record PluginRow(PluginMetadata Metadata, DesktopPluginContext? Installed, PluginMetadata? Available)
    {
        public override string ToString() => (Installed == null ? "" : "✓ ") + Metadata.Name;
    }

    private async Task ManagePlugins()
    {
        var dialog = ToolWindow("Plugins");
        var list = new ListBox { Height = 220 };
        var detail = Label("");
        var status = Label("");
        var source = new TextBox { Watermark = T("DefaultRepository") };
        var catalog = PluginMetadataCollection.Empty;
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        var busy = false;

        void Populate()
        {
            var selected = (list.SelectedItem as PluginRow)?.Metadata;
            var remote = catalog.Where(m => m.IsSupportedBy(AppInfo.Current.Version))
                .OrderByDescending(m => m.PluginVersion).DistinctBy(m => (m.Owner, m.Name, m.RepositoryUrl)).ToArray();
            var rows = AppInfo.PluginManager.GetLoadedPlugins().Select(context =>
            {
                var metadata = context.GetMetadata();
                return new PluginRow(metadata, context, remote.FirstOrDefault(m => PluginMetadata.Match(m, metadata)));
            }).OrderBy(row => row.Metadata.Name).ToList();
            rows.AddRange(remote.Where(m => !rows.Any(row => PluginMetadata.Match(m, row.Metadata)))
                .OrderBy(m => m.Name).Select(m => new PluginRow(m, null, m)).ToArray());
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(row => PluginMetadata.Match(row.Metadata, selected)) ?? rows.FirstOrDefault();
        }

        async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            body.IsEnabled = false;
            status.Text = T("Working");
            try
            {
                await action();
                Populate();
                RenderPage();
                status.Text = "";
            }
            catch (Exception ex) { status.Text = T("Error") + Environment.NewLine + ex.Message; }
            finally { busy = false; body.IsEnabled = true; }
        }
        Task Refresh() => Run(async () =>
        {
            var url = source.Text?.Trim();
            catalog = await (string.IsNullOrEmpty(url) ? PluginMetadataCollection.DownloadAsync() :
                PluginMetadataCollection.DownloadAsync(url)).WaitAsync(TimeSpan.FromSeconds(12));
        });
        async Task InstallZip(string path)
        {
            if (!await Driver.InstallPlugin(path)) throw new IOException(T("InstallFailed"));
            AppInfo.PluginManager.Load();
        }
        var install = ButtonFor("Install", async () => await Run(async () =>
        {
            if (list.SelectedItem is not PluginRow { Available: { } metadata } row) return;
            if (!await Driver.DownloadPlugin(metadata)) throw new IOException(T("InstallFailed"));
            if (row.Installed != null) AppInfo.PluginManager.UnloadPlugin(row.Installed);
            AppInfo.PluginManager.Load();
        }));
        var uninstall = ButtonFor("Uninstall", async () => await Run(async () =>
        {
            if (list.SelectedItem is not PluginRow { Installed: { } context }) return;
            if (context.Directory.Exists && !await Driver.UninstallPlugin(context.Directory.FullName))
                throw new IOException(T("UninstallFailed"));
            AppInfo.PluginManager.UnloadPlugin(context);
        }));
        list.SelectionChanged += (_, _) =>
        {
            var row = list.SelectedItem as PluginRow;
            var meta = row?.Metadata;
            detail.Text = meta == null ? "" : $"{meta.Name}  {meta.PluginVersion}   {meta.Creator ?? meta.Owner}\n{meta.Description}";
            install.Content = T(row?.Installed != null ? "Update" : "Install");
            install.IsEnabled = row?.Available != null && (row.Installed == null || row.Available.PluginVersion > meta?.PluginVersion);
            uninstall.IsEnabled = row?.Installed != null;
        };
        var bar = new WrapPanel();
        bar.Children.Add(install);
        bar.Children.Add(uninstall);
        bar.Children.Add(ButtonFor("InstallZip", async () =>
        {
            var files = await dialog.StorageProvider.OpenFilePickerAsync(new() { Title = T("InstallZip"),
                FileTypeFilter = [new FilePickerFileType(T("ZipFile")) { Patterns = ["*.zip"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is string path) await Run(() => InstallZip(path));
        }));
        bar.Children.Add(ButtonFor("PluginFolder", () => DesktopInterop.OpenFolder(AppInfo.Current.PluginDirectory)));
        body.Children.Add(Field(T("Repository"), source));
        body.Children.Add(ButtonFor("Refresh", async () => await Refresh()));
        body.Children.Add(list);
        body.Children.Add(detail);
        body.Children.Add(bar);
        body.Children.Add(Label(T("DropZip")));
        body.Children.Add(status);
        DragDrop.SetAllowDrop(dialog, true);
        dialog.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.DataTransfer.TryGetFiles() != null ? DragDropEffects.Copy : DragDropEffects.None);
        dialog.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>()
                .Where(p => string.Equals(Path.GetExtension(p), ".zip", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            await Run(async () => { foreach (var path in paths) await InstallZip(path); });
        });
        dialog.Content = new ScrollViewer { Content = body };
        Populate();
        dialog.Opened += async (_, _) => await Refresh();
        await dialog.ShowDialog(this);
    }

    private Window ToolWindow(string key) => new() { Title = T(key), Icon = Icon, Width = 760, Height = 600,
        MinWidth = 420, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
}
