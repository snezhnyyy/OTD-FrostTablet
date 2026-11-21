using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace TabletDriver.UI;

public partial class MainWindow : Window
{
    private readonly DaemonClient client = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<LogMessage> messages = [];
    private readonly string[] sections = ["Output", "Filters", "Bindings", "Tools", "Console"];
    private readonly string[] navIcons = [Icons.Monitor, Icons.Activity, Icons.Keyboard, Icons.Wrench, Icons.Terminal];
    private readonly MenuFlyout moreMenu = new() { Placement = PlacementMode.BottomEdgeAlignedRight };
    private Preferences preferences = Preferences.Read();
    private Settings? settings;
    private TabletReference[] connected = [];
    private string section = "Output";
    private string? selectedTablet;
    private TrayIcon? tray;
    private bool refreshing;
    private bool started;
    private IDriverDaemon Driver => client.Instance ?? throw new InvalidOperationException(T("Connecting"));
    private static string T(string key) => Loc.Instance[key];

    public MainWindow()
    {
        InitializeComponent();
        string? Arg(string name) => Array.IndexOf(Program.Options, name) is var i and >= 0 && i + 1 < Program.Options.Length ? Program.Options[i + 1] : null;
        langOverride = Arg("--lang");
        themeOverride = Arg("--theme");
        ApplyPreferences();
        if (!Program.Headless && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
        {
            tray = new TrayIcon { Icon = Icon, ToolTipText = Title, IsVisible = true };
            tray.Clicked += (_, _) => Restore();
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
        }
        Translate();
        void Language(string language)
        {
            if (refreshing) return;
            preferences.Language = language;
            Loc.Instance.Select(language);
            Translate();
            preferences.Save();
        }
        void Theme(bool light)
        {
            if (refreshing) return;
            preferences.Light = light;
            App.SetTheme(light);
            Translate();
            preferences.Save();
        }
        LanguageButton.Click += (_, _) =>
        {
            // a list exactly as wide as the language cell, opening upwards from it
            var list = new StackPanel { Spacing = 2, Width = LanguageButton.Bounds.Width + 44 };
            var flyout = new Flyout { Placement = PlacementMode.TopEdgeAlignedLeft, Content = list, FlyoutPresenterClasses = { "picker" } };
            foreach (var (code, name) in Loc.Languages)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,20"), ColumnSpacing = 8 };
                row.Children.Add(new TextBlock { Text = name, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                var english = Loc.EnglishName(code);
                if (english != name)
                {
                    var hint = new TextBlock { Text = english, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                    hint.Classes.Add("muted");
                    hint.Classes.Add("meta");
                    Grid.SetColumn(hint, 1);
                    row.Children.Add(hint);
                }
                if (Loc.Instance.Language == code)
                {
                    var check = Icons.Create(Icons.Check, 16);
                    check.Bind(TextElement.ForegroundProperty, check.GetResourceObservable("Accent"));
                    Grid.SetColumn(check, 2);
                    row.Children.Add(check);
                }
                var item = new Button { Content = row, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
                item.Classes.Add("menurow");
                if (Loc.Instance.Language == code) item.Classes.Add("current");
                item.Click += (_, _) => { flyout.Hide(); Language(code); };
                list.Children.Add(item);
            }
            flyout.ShowAt(LanguageButton);
        };
        ThemeButton.Click += (_, _) => Theme(!preferences.Light);
        LanguageIcon.Content = Icons.Create(Icons.Globe);
        LanguageChevron.Content = Icons.Create(Icons.ChevronDown, 14);
        MoreButton.Content = Icons.Create(Icons.More);
        MoreButton.Click += (_, _) => moreMenu.ShowAt(MoreButton);
        VersionText.Text = typeof(OpenTabletDriver.Desktop.Settings).Assembly.GetName().Version?.ToString(3) + " custom";
        Navigation.SelectionChanged += (_, _) =>
        {
            if (!refreshing && Navigation.SelectedIndex >= 0)
            {
                section = sections[Navigation.SelectedIndex];
                RenderPage();
            }
        };
        Tablets.SelectionChanged += (_, _) =>
        {
            if (refreshing) return;
            selectedTablet = Tablets.SelectedItem as string;
            RenderPage();
        };
        Detect.Click += async (_, _) => await DetectTablets();
        Apply.Click += async (_, _) => await ApplySettings(false);
        Save.Click += async (_, _) => await ApplySettings(true);
        ResetChanges.Click += async (_, _) => await RevertChanges();
        // content fills the whole width, aligned with the header
        EditorScroll.SizeChanged += (_, e) => Editor.Width = Math.Max(0, e.NewSize.Width - 64);
        changeWatch.Tick += (_, _) => UpdateChangesBar();
        changeWatch.Start();
        client.Logged += message =>
        {
            messages.Add(message);
            AppendLog(message);
        };
        client.Tablets += SetTablets;
        client.Sync += async () =>
        {
            if (settings != null)
            {
                settings = await Driver.GetSettings();
                ResetBaseline();
                SetTablets(connected);
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized && tray != null) Hide();
            if (e.Property == ClientSizeProperty && WindowState == WindowState.Normal && IsVisible)
            {
                preferences.Width = ClientSize.Width;
                preferences.Height = ClientSize.Height;
            }
        };
        Opened += (_, _) =>
        {
            if (started) return;
            started = true;
            var pageArg = Array.IndexOf(Program.Options, "--page");
            if (pageArg >= 0 && pageArg + 1 < Program.Options.Length && sections.Contains(Program.Options[pageArg + 1]))
            {
                section = Program.Options[pageArg + 1];
                Translate();
            }
            if (Program.Options.Contains("--minimized") || Program.Options.Contains("-m"))
                Dispatcher.UIThread.Post(() => WindowState = WindowState.Minimized);
            _ = ConnectLoop();
        };
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            tray?.Dispose();
            client.Dispose();
            preferences.Save();
        };
    }

    private string? langOverride, themeOverride;

    private void ApplyPreferences()
    {
        if (langOverride != null) preferences.Language = langOverride;
        if (themeOverride != null) preferences.Light = themeOverride == "light";
        if (Program.Headless)
        {
            Loc.Instance.Select(preferences.Language);
            App.SetTheme(preferences.Light);
            return;
        }
        Width = Math.Clamp(preferences.Width, MinWidth, 8192);
        Height = Math.Clamp(preferences.Height, MinHeight, 4320);
        Loc.Instance.Select(preferences.Language);
        App.SetTheme(preferences.Light);
    }

    internal void Restore()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ConnectLoop()
    {
        var token = lifetime.Token;
        while (!token.IsCancellationRequested)
        {
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDisconnect(object? s, EventArgs e) => disconnected.TrySetResult();
            client.Disconnected += OnDisconnect;
            try
            {
                client.StartLocalDaemon();
                if (!client.IsConnected)
                {
                    var connecting = client.Connect();
                    while (!connecting.IsCompleted)
                    {
                        var delay = Task.Delay(TimeSpan.FromSeconds(15), token);
                        if (await Task.WhenAny(connecting, delay) == connecting) break;
                        token.ThrowIfCancellationRequested();
                        client.StartLocalDaemon();
                        if (!client.IsConnected) Editor.Children.Clear();
                        if (!client.IsConnected) Editor.Children.Add(Label(T("ConnectionTimeout")));
                    }
                    await connecting;
                }
                foreignDaemon = FindForeignDaemon();
                var previousPath = Preferences.FilePath;
                AppInfo.Current = await Driver.GetApplicationInfo();
                if (Preferences.FilePath != previousPath)
                {
                    preferences = Preferences.Read();
                    ApplyPreferences();
                }
                AppInfo.PluginManager = new DesktopPluginManager();
                AppInfo.PluginManager.Load();
                AppInfo.PresetManager = new PresetManager();
                AppInfo.PresetManager.Refresh();
                messages.Clear();
                messages.AddRange(await Driver.GetCurrentLog());
                settings = await Driver.GetSettings();
                ResetBaseline();
                SetTablets(await Driver.GetTablets());
                Translate();
                if (!File.Exists(AppInfo.Current.SettingsFile) && IsVisible && WindowState != WindowState.Minimized)
                    await Guide();
                if (!Program.Options.Contains("--skipupdate") && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
                    await Updates(false);
                await disconnected.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested) await ShowError(error);
            }
            finally { client.Disconnected -= OnDisconnect; }
            if (token.IsCancellationRequested) return;
            settings = null;
            ResetBaseline();
            SetTablets([]);
            Translate();
            await Task.Delay(1000);
        }
    }

    private void Translate()
    {
        refreshing = true;
        LanguageText.Text = Loc.NameOf(Loc.Instance.Language);
        ThemeButton.Content = Icons.Create(preferences.Light ? Icons.Moon : Icons.Sun);
        ToolTip.SetTip(ThemeButton, T(preferences.Light ? "SwitchDark" : "SwitchLight"));
        ToolTip.SetTip(MoreButton, T("MoreActions"));
        Detect.Content = Icons.WithText(Icons.Refresh, T("Detect"));
        Apply.Content = T("Apply");
        Save.Content = T("Save");
        ResetChanges.Content = T("ResetChanges");
        ToolTip.SetTip(Apply, T("ApplyTip"));
        ToolTip.SetTip(Save, T("SaveTip"));
        ToolTip.SetTip(ResetChanges, T("ResetChangesTip"));
        lastBarState = null;
        UpdateChangesBar();
        UpdateStatus();
        Navigation.ItemsSource = sections.Select((key, i) => Icons.WithText(navIcons[i], T(key))).ToArray();
        Navigation.SelectedIndex = Array.IndexOf(sections, section);
        refreshing = false;
        BuildMenus();
        RenderPage();
    }

    private IDisposable? statusDotBinding;
    private string? foreignDaemon;

    /// <summary>Path of a running daemon that is not the one shipped next to this UI (e.g. the stock 0.6.7).</summary>
    private static string? FindForeignDaemon()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("OpenTabletDriver.Daemon"))
            {
                using (process)
                {
                    var file = process.MainModule?.FileName;
                    var ownDaemon = Path.Combine(AppContext.BaseDirectory, "OpenTabletDriver.Daemon.exe");
                    if (file != null && !string.Equals(Path.GetFullPath(file), Path.GetFullPath(ownDaemon), StringComparison.OrdinalIgnoreCase))
                        return file;
                }
            }
        }
        catch (Exception) { }
        return null;
    }

    private void UpdateStatus()
    {
        var online = settings != null && client.IsConnected;
        statusDotBinding?.Dispose();
        statusDotBinding = StatusDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable(online ? "Good" : "Bad"));
        StatusText.Text = !online ? T("StatusOffline")
            : connected.Length == 0 ? T("StatusNoTablet")
            : string.Join(", ", connected.Select(t => t.Properties.Name).Distinct());

        if (online && foreignDaemon != null)
        {
            statusDotBinding?.Dispose();
            statusDotBinding = StatusDot.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, this.GetResourceObservable("Bad"));
            StatusText.Text = T("ForeignDaemon");
            ToolTip.SetTip(StatusText, string.Format(T("ForeignDaemonHint"), foreignDaemon));
        }
    }

    private void SetTablets(IEnumerable<TabletReference> devices)
    {
        connected = devices.ToArray();
        var names = connected.Select(t => t.Properties.Name).Distinct().ToArray();
        if (settings != null)
            foreach (var tablet in connected) settings.Profiles.GetProfile(tablet);
        if (!names.Contains(selectedTablet)) selectedTablet = names.FirstOrDefault();
        refreshing = true;
        Tablets.ItemsSource = names;
        Tablets.SelectedItem = selectedTablet;
        Tablets.IsVisible = names.Length > 0;
        refreshing = false;
        UpdateStatus();
        RenderPage();
    }

    private async Task DetectTablets()
    {
        AppInfo.Current.ConfigurationDirectory = null;
        var devices = await Driver.DetectTablets();
        settings = await Driver.GetSettings();
        await Driver.SetSettings(settings);
        ResetBaseline();
        SetTablets(devices);
    }

    private void RenderPage()
    {
        logItems = null;
        Editor.Children.Clear();
        Detect.IsEnabled = settings != null;
        PageTitle.Text = T(section);
        if (settings == null) { Editor.Children.Add(Label(T("Connecting"))); return; }
        var tablet = connected.FirstOrDefault(t => t.Properties.Name == selectedTablet);
        if (section == "Console") RenderLog();
        else if (section == "Tools") PluginCollection<ITool>(settings.Tools);
        else if (tablet == null) Editor.Children.Add(Label(T("NoTablet")));
        else
        {
            var profile = settings.Profiles.GetProfile(tablet);
            switch (section)
            {
                case "Output": Output(profile, tablet); break;
                case "Filters": PluginCollection<IPositionedPipelineElement<IDeviceReport>>(profile.Filters); break;
                case "Bindings": Bindings(profile.BindingSettings, tablet); break;
            }
        }
    }

    private async Task ApplySettings(bool persist)
    {
        if (settings == null) return;
        if (persist)
        {
            if (settings.Profiles.Any(p => p.AbsoluteModeSettings?.Tablet is { Width: 0, Height: 0 }) &&
                !await Dialog("Save", T("InvalidArea"), true)) return;
            settings.Serialize(new FileInfo(AppInfo.Current.SettingsFile));
        }
        await Driver.SetSettings(settings);
        appliedJson = Snapshot(settings);
        if (persist) savedJson = appliedJson;
        UpdateChangesBar();
    }

    // ---- change tracking: what the user edited vs. what the driver runs vs. what is on disk ----
    private readonly Avalonia.Threading.DispatcherTimer changeWatch = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private string? savedJson, appliedJson;
    private string? lastBarState;

    private static string Snapshot(Settings value)
    {
        using var stream = new MemoryStream();
        Serialization.Serialize(stream, value);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Call whenever settings are (re)loaded from the driver: that state is both applied and saved.</summary>
    private void ResetBaseline(bool saved = true)
    {
        if (settings == null) { savedJson = appliedJson = null; return; }
        appliedJson = Snapshot(settings);
        if (saved || savedJson == null) savedJson = appliedJson;
        UpdateChangesBar();
    }

    private void UpdateChangesBar()
    {
        string state;
        if (settings == null || appliedJson == null || savedJson == null)
            state = "none";
        else
        {
            var current = Snapshot(settings);
            state = current != appliedJson ? "edited" : appliedJson != savedJson ? "applied" : "none";
        }
        if (state == lastBarState) return;
        lastBarState = state;
        Apply.IsVisible = state == "edited";
        ChangesTitle.Text = state == "applied" ? T("ChangesApplied") : T("ChangesEdited");
        ChangesHint.Text = state == "applied" ? T("ChangesAppliedHint") : T("ChangesEditedHint");
        _ = Motion.Reveal(ChangesBar, state != "none", 200);
    }

    private async Task RevertChanges()
    {
        if (savedJson == null) return;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(savedJson));
        if (Serialization.Deserialize<Settings>(stream) is not { } restored) return;
        settings = restored;
        if (appliedJson != savedJson) await Driver.SetSettings(restored);
        appliedJson = savedJson;
        SetTablets(connected);
        UpdateChangesBar();
    }

    internal Task ShowError(Exception exception) => Dialog("Error", exception.ToString());

    private static SelectableTextBlock MessageText(string message, bool mono)
    {
        var text = new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        if (mono) text.Classes.Add("mono");
        else text.Classes.Add("secondary");
        return text;
    }

    private async Task<bool> Dialog(string title, string message, bool confirm = false)
    {
        var dialog = new Window { Title = T(title), Icon = Icon, Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 560,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        if (confirm) actions.Children.Add(ButtonFor("Cancel", () => dialog.Close(false)));
        var ok = ButtonFor("OK", () => dialog.Close(true));
        ok.Classes.Add("primary");
        ok.IsDefault = true;
        actions.Children.Add(ok);
        DockPanel.SetDock(actions, Dock.Bottom);
        var head = new TextBlock { Text = T(title), Classes = { "section" }, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(head, Dock.Top);
        dialog.Content = new DockPanel { Margin = new Thickness(24), Children =
        {
            head, actions, new ScrollViewer { MaxHeight = 400, Content = MessageText(message, title == "Error") }
        }};
        return await dialog.ShowDialog<bool>(this);
    }
}
