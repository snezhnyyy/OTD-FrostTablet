using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;

namespace TabletDriver.UI;

public partial class MainWindow
{
    private readonly Dictionary<KeyGesture, Func<Task>> shortcuts = new();
    private ObservableCollection<string>? logItems;
    private LogLevel minimum = LogLevel.Info;

    private void BuildMenus()
    {
        moreMenu.Items.Clear();
        shortcuts.Clear();
        MenuItem Item(string key, Func<Task> run, string? shortcut = null)
        {
            var item = new MenuItem { Header = T(key) };
            item.Click += async (_, _) => await run();
            if (shortcut != null)
            {
                item.InputGesture = KeyGesture.Parse(shortcut);
                shortcuts[item.InputGesture] = run;
            }
            return item;
        }
        static MenuItem Group(string key, params MenuItem[] items) =>
            new() { Header = T(key), ItemsSource = items };
        static Func<Task> Run(Action action) => () => { action(); return Task.CompletedTask; };
        var exit = Item("Exit", Run(Close), "Ctrl+Q");
        var native = new NativeMenu();
        void TrayAction(string label, Func<Task> run)
        {
            var item = new NativeMenuItem(label);
            item.Click += async (_, _) => await run();
            native.Items.Add(item);
        }
        TrayAction(T("Show"), Run(Restore));
        TrayAction(T("Hide"), Run(Hide));
        if (settings != null)
        {
            var presets = new MenuItem { Header = T("Presets") };
            foreach (var preset in AppInfo.PresetManager.GetPresets())
            {
                async Task Select()
                {
                    await Driver.SetSettings(preset.Settings);
                    settings = preset.Settings;
                    ResetBaseline(saved: false);
                    SetTablets(connected);
                }
                var item = new MenuItem { Header = preset.Name };
                item.Click += async (_, _) => await Select();
                presets.Items.Add(item);
                TrayAction(preset.Name, Select);
            }
            if (presets.Items.Count == 0)
                presets.Items.Add(new MenuItem { Header = T("EmptyPresets"), IsEnabled = false });
            presets.Items.Add(new Separator());
            presets.Items.Add(Item("SavePreset", () => SaveFile(true)));
            presets.Items.Add(Item("Refresh", Run(RefreshPresets)));
            presets.Items.Add(Item("PresetFolder", Run(() => DesktopInterop.OpenFolder(AppInfo.Current.PresetDirectory))));
            moreMenu.Items.Add(Group("File", Item("Load", LoadFile, "Ctrl+O"),
                Item("Save", () => ApplySettings(true), "Ctrl+S"), Item("SaveAs", () => SaveFile(false), "Ctrl+Shift+S"),
                Item("Apply", () => ApplySettings(false), "Ctrl+Enter"), Item("Reset", Reset)));
            moreMenu.Items.Add(presets);
            moreMenu.Items.Add(Item("ManagePlugins", ManagePlugins));
            moreMenu.Items.Add(Group("TabletsMenu", Item("Detect", DetectTablets, "Ctrl+D"),
                Item("Debugger", Debugger), Item("StringReader", () => DeviceTool(false)), Item("InitSender", () => DeviceTool(true))));
        }
        var help = new MenuItem { Header = T("Help") };
        help.Items.Add(Item("Wiki", Run(() => DesktopInterop.Open(Program.RepositoryUrl + "#readme"))));
        help.Items.Add(Item("Guide", Guide));
        help.Items.Add(Item("About", About, "F1"));
        if (settings != null)
        {
            help.Items.Add(Item("Export", () => Diagnostics(false)));
            help.Items.Add(Item("CopyDiagnostics", () => Diagnostics(true)));
            help.Items.Add(Item("Updates", () => Updates(true)));
        }
        moreMenu.Items.Add(help);
        moreMenu.Items.Add(new Separator());
        moreMenu.Items.Add(exit);
        TrayAction(T("Exit"), Run(Close));
        if (tray != null) tray.Menu = native;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        var command = shortcuts.FirstOrDefault(pair => pair.Key.Matches(e)).Value;
        if (command == null) { base.OnKeyDown(e); return; }
        e.Handled = true;
        await command();
    }

    private async Task Reset()
    {
        if (!await Dialog("Reset", T("ResetConfirm"), true)) return;
        await Driver.ResetSettings();
        settings = await Driver.GetSettings();
        ResetBaseline(saved: false);
        SetTablets(connected);
    }

    private async Task LoadFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = T("Load"), FileTypeFilter = [JsonType()] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not string path) return;
        if (!Settings.TryDeserialize(new FileInfo(path), out var loaded))
        {
            await Dialog("Error", T("InvalidFile"));
            return;
        }
        await Driver.SetSettings(loaded);
        settings = loaded;
        ResetBaseline(saved: false);
        SetTablets(connected);
    }

    private static FilePickerFileType JsonType() => new(T("JsonFile")) { Patterns = ["*.json"] };

    private async Task SaveFile(bool preset)
    {
        var folder = preset ? await StorageProvider.TryGetFolderFromPathAsync(AppInfo.Current.PresetDirectory) : null;
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = T(preset ? "SavePreset" : "SaveAs"), SuggestedStartLocation = folder,
            FileTypeChoices = [JsonType()], DefaultExtension = "json", SuggestedFileName = preset ? "preset.json" : "settings.json"
        });
        if (settings == null || file?.TryGetLocalPath() is not string path) return;
        settings.Serialize(new FileInfo(path));
        if (preset) RefreshPresets();
        else await ApplySettings(false);
    }

    private void RefreshPresets()
    {
        AppInfo.PresetManager.Refresh();
        BuildMenus();
    }

    private async Task Diagnostics(bool copy)
    {
        var dump = (await Driver.GetDiagnosticInfo()).ToString();
        if (copy && Clipboard != null) await Clipboard.SetTextAsync(dump);
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new()
            {
                Title = T("Export"), SuggestedFileName = "Diagnostics.json", DefaultExtension = "json", FileTypeChoices = [JsonType()]
            });
            if (file?.TryGetLocalPath() is string path) await File.WriteAllTextAsync(path, dump);
        }
    }

    private Task Guide() => Dialog("Guide", T("GuideText"));

    private async Task About()
    {
        using var reader = new StreamReader(typeof(App).Assembly.GetManifestResourceStream("LICENSE")!);
        await Dialog("About", $"FrostTablet {Program.FrostVersion} — by tgk: snezhnyyy and claude ^^\n" +
            $"Based on OpenTabletDriver {typeof(OpenTabletDriver.Desktop.Settings).Assembly.GetName().Version?.ToString(3)} (LGPL-3.0)\n" +
            $"{Program.RepositoryUrl}\n\n{await reader.ReadToEndAsync()}");
    }

    /// <summary>
    /// Checks this fork's GitHub releases. The daemon's built-in updater targets upstream OpenTabletDriver
    /// and would replace FrostTablet with it, so it is never used here.
    /// </summary>
    private async Task Updates(bool manual)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FrostTablet/" + Program.FrostVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(
                "https://api.github.com/repos/snezhnyyy/OTD-FrostTablet/releases/latest"));
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? Program.RepositoryUrl + "/releases";
            var latest = Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var v) ? v : null;
            if (latest != null && latest > Version.Parse(Program.FrostVersion))
            {
                if (await Dialog("Updates", string.Format(T("UpdateAvailable"), latest), true))
                    DesktopInterop.Open(url);
            }
            else if (manual)
                await Dialog("Updates", T("UpToDate"));
        }
        catch (Exception) when (!manual)
        {
            // offline or rate limited: stay quiet on the automatic check
        }
        catch (Exception ex)
        {
            await Dialog("Updates", T("UpdateCheckFailed") + Environment.NewLine + ex.Message);
        }
    }

    private void RenderLog()
    {
        var levels = Enum.GetValues<LogLevel>();
        var filter = new ComboBox { ItemsSource = levels.Select(l => T(l.ToString())).ToArray(), SelectedIndex = Array.IndexOf(levels, minimum) };
        filter.SelectionChanged += (_, _) =>
        {
            if (filter.SelectedIndex < 0) return;
            minimum = levels[filter.SelectedIndex];
            RenderPage();
        };
        logItems = new ObservableCollection<string>(messages.Where(m => m.Level >= minimum).Select(FormatLog));
        var list = new ListBox { ItemsSource = logItems, Height = Math.Max(220, Bounds.Height - 260), Classes = { "log" },
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((line, _) =>
                new TextBlock { Text = line, Classes = { "mono" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap }),
            SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle };
        var bar = Row();
        bar.Children.Add(filter);
        bar.Children.Add(ButtonFor("Copy", async () =>
        {
            if (Clipboard != null && list.SelectedItems != null)
                await Clipboard.SetTextAsync(string.Join(Environment.NewLine, list.SelectedItems.Cast<string>()));
        }));
        bar.Children.Add(ButtonFor("Export", async () => await Diagnostics(false)));
        Editor.Children.Add(bar);
        Editor.Children.Add(list);
    }

    private static string FormatLog(LogMessage m) =>
        $"{m.Time:T}  [{T(m.Level.ToString())}]  {m.Group}  {m.Message}{(m.StackTrace == null ? "" : Environment.NewLine + m.StackTrace)}";

    private void AppendLog(LogMessage message)
    {
        if (message.Level >= minimum) logItems?.Add(FormatLog(message));
    }
}
