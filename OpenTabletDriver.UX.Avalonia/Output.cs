using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace TabletDriver.UI;

public partial class MainWindow
{
    private void Output(Profile profile, TabletReference tablet)
    {
        var choices = Choices<IOutputMode>();
        var selected = choices.FirstOrDefault(c => c.Type.FullName == profile.OutputMode.Path);
        var mode = new ComboBox { ItemsSource = choices, SelectedItem = selected };
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedItem is not PluginChoice choice) return;
            profile.OutputMode = new PluginSettingStore(choice.Type);
            RenderPage();
        };

        if (selected?.Type.IsSubclassOf(typeof(AbsoluteOutputMode)) == true)
            AbsoluteEditor(profile, tablet);

        var modeGroup = new StackPanel();
        modeGroup.Children.Add(Field(T("Mode"), mode, T("ModeHint")));
        Properties(modeGroup, profile.OutputMode);
        if (selected?.Type.IsSubclassOf(typeof(RelativeOutputMode)) == true)
        {
            var r = profile.RelativeModeSettings ??= RelativeModeSettings.GetDefaults();
            modeGroup.Children.Add(Field(T("XSensitivity"), Number(r.XSensitivity, v => r.XSensitivity = (float)v)));
            modeGroup.Children.Add(Field(T("YSensitivity"), Number(r.YSensitivity, v => r.YSensitivity = (float)v)));
            modeGroup.Children.Add(Field(T("Rotation"), Number(r.RelativeRotation, v => r.RelativeRotation = (float)v)));
            modeGroup.Children.Add(Field(T("ResetTime"), Number(r.ResetTime.TotalMilliseconds, v => r.ResetTime = TimeSpan.FromMilliseconds(v), 0)));
        }
        Editor.Children.Add(Card(T("OutputModeCard"), modeGroup));
    }

    private void AbsoluteEditor(Profile profile, TabletReference tablet)
    {
        var digitizer = tablet.Properties.Specifications.Digitizer;
        var config = profile.AbsoluteModeSettings ??= AbsoluteModeSettings.GetDefaults(digitizer);
        var displays = DesktopInterop.VirtualScreen?.Displays.Where(d => d is not IVirtualScreen).ToArray() ?? [];
        var monitorRects = displays.Select(d => new Rect(d.Position.X, d.Position.Y, d.Width, d.Height)).ToArray();
        if (monitorRects.Length > 0)
        {
            var origin = new Vector(-monitorRects.Min(r => r.X), -monitorRects.Min(r => r.Y));
            monitorRects = monitorRects.Select(r => r.Translate(origin)).ToArray();
        }
        var tabletRect = new Rect(0, 0, digitizer.Width, digitizer.Height);

        var tabletSide = AreaSide(config, true, [tabletRect], out var tabletActions);
        AreaActions? screenActions = null;
        Control? screenSide = null;
        if (monitorRects.Length > 0)
        {
            screenSide = AreaSide(config, false, monitorRects, out var actions);
            screenActions = actions;
        }

        var stage = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 32 };
        stage.Children.Add(tabletSide);
        if (screenSide != null)
        {
            Grid.SetColumn(screenSide, 1);
            stage.Children.Add(screenSide);
        }
        var stageBorder = new Border { Classes = { "stage" }, Padding = new Thickness(24, 20), Child = stage };
        void Layout(double width)
        {
            var narrow = width < 820 && screenSide != null;
            stage.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            stage.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            stage.RowSpacing = 28;
            if (screenSide != null)
            {
                Grid.SetColumn(screenSide, narrow ? 0 : 1);
                Grid.SetRow(screenSide, narrow ? 1 : 0);
            }
        }
        stageBorder.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Layout(e.NewSize.Width); };
        Editor.Children.Add(stageBorder);

        var options = new StackPanel();
        options.Children.Add(Toggle("Aspect", config.LockAspectRatio, v => { config.LockAspectRatio = v; tabletActions.Aspect(false); }));
        options.Children.Add(SwitchRow(T("Clip"), config.EnableClipping, v => config.EnableClipping = v, T("ClipHint")));
        options.Children.Add(SwitchRow(T("Limit"), config.EnableAreaLimiting, v => config.EnableAreaLimiting = v, T("LimitHint")));
        options.Children.Add(SwitchRow(T("UsableTablet"), settings!.LockUsableAreaTablet, v => { settings!.LockUsableAreaTablet = v; tabletActions.Fit(); }));
        options.Children.Add(SwitchRow(T("UsableDisplay"), settings!.LockUsableAreaDisplay, v => { settings!.LockUsableAreaDisplay = v; screenActions?.Fit(); }));
        Editor.Children.Add(Card(T("AreaOptions"), options));
    }

    private sealed class AreaActions
    {
        public required Action Fit { get; init; }
        public required Action<bool> Aspect { get; init; }
    }

    private Control AreaSide(AbsoluteModeSettings config, bool isTablet, Rect[] bounds, out AreaActions actions)
    {
        var area = isTablet ? config.Tablet : config.Display;
        var full = bounds.Aggregate((a, b) => a.Union(b));
        var unit = isTablet ? T("UnitMm") : T("UnitPx");

        bool Locked() => isTablet ? settings!.LockUsableAreaTablet : settings!.LockUsableAreaDisplay;
        double Ratio() => config.Display.Height > 0 ? config.Display.Width / config.Display.Height : 1;
        (double Cos, double Sin) Rotation()
        {
            var angle = area.Rotation * Math.PI / 180;
            return (Math.Abs(Math.Cos(angle)), Math.Abs(Math.Sin(angle)));
        }
        void Fit()
        {
            if (!Locked()) return;
            var (c, s) = Rotation();
            var halfW = (area.Width * c + area.Height * s) / 2;
            var halfH = (area.Width * s + area.Height * c) / 2;
            var scale = Math.Min(1, Math.Min(full.Width / (2 * halfW), full.Height / (2 * halfH)));
            if (scale < 1)
            {
                area.Width *= (float)scale;
                area.Height *= (float)scale;
                halfW *= scale;
                halfH *= scale;
            }
            area.X = (float)Math.Clamp(area.X, Math.Min(halfW, full.Width / 2), Math.Max(full.Width - halfW, full.Width / 2));
            area.Y = (float)Math.Clamp(area.Y, Math.Min(halfH, full.Height / 2), Math.Max(full.Height - halfH, full.Height / 2));
        }
        void Aspect(bool fromHeight)
        {
            if (!config.LockAspectRatio || Ratio() <= 0) return;
            if (fromHeight && isTablet) config.Tablet.Width = (float)(config.Tablet.Height * Ratio());
            else config.Tablet.Height = (float)(config.Tablet.Width / Ratio());
        }
        actions = new AreaActions { Fit = Fit, Aspect = Aspect };

        var side = new StackPanel { Spacing = 12 };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new TextBlock { Text = T(isTablet ? "TabletArea" : "DisplayArea"), Classes = { "section" } });
        var sizeText = isTablet
            ? string.Format(T("ActiveSurface"), $"{full.Width:0.#} × {full.Height:0.#}")
            : string.Format(T("MonitorCount"), bounds.Length);
        var meta = new TextBlock { Text = sizeText, Classes = { "muted", "mono" }, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(meta, 1);
        header.Children.Add(meta);
        side.Children.Add(header);

        var canvas = new AreaPreview(area, bounds, isTablet) { Height = 220 };
        canvas.MoveArea = (dx, dy) =>
        {
            area.X += (float)dx;
            area.Y += (float)dy;
            Fit();
        };
        side.Children.Add(canvas);

        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, RowSpacing = 8 };
        void Place(Control field, int index, int span = 1)
        {
            while (fields.RowDefinitions.Count <= index / 2) fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(field, index / 2);
            Grid.SetColumn(field, index % 2);
            Grid.SetColumnSpan(field, span);
            fields.Children.Add(field);
        }
        var unitSuffix = isTablet ? T("UnitMm") : T("UnitPx");
        Place(InlineNumber(T("Width"), () => area.Width, v => { area.Width = v; Aspect(false); Fit(); }, unitSuffix, 0, area), 0);
        Place(InlineNumber(T("Height"), () => area.Height, v => { area.Height = v; Aspect(true); Fit(); }, unitSuffix, 0, area), 1);
        Place(InlineNumber("X", () => area.X, v => { area.X = v; Fit(); }, unitSuffix, null, area), 2);
        Place(InlineNumber("Y", () => area.Y, v => { area.Y = v; Fit(); }, unitSuffix, null, area), 3);
        if (isTablet)
            Place(InlineNumber(T("Rotation"), () => area.Rotation, v => { area.Rotation = Math.Clamp(v, -360, 360); Fit(); }, "°", null, area), 4, 2);
        side.Children.Add(fields);

        var chips = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        void Chip(string label, Action run)
        {
            var b = new Button { Content = label, Classes = { "chip" } };
            b.Click += (_, _) => run();
            chips.Children.Add(b);
        }
        void Resize(Rect rect, double fraction)
        {
            var (c, s) = Rotation();
            var ratio = config.LockAspectRatio && isTablet ? Ratio() : rect.Width / rect.Height;
            if (ratio <= 0) return;
            var height = Math.Min(rect.Width / (c * ratio + s), rect.Height / (s * ratio + c)) * fraction;
            area.Width = (float)(height * ratio);
            area.Height = (float)height;
            area.X = (float)rect.Center.X;
            area.Y = (float)rect.Center.Y;
            Aspect(false);
            Fit();
        }
        void Align(string key)
        {
            var (c, s) = Rotation();
            var halfW = (area.Width * c + area.Height * s) / 2;
            var halfH = (area.Width * s + area.Height * c) / 2;
            switch (key)
            {
                case "Left": area.X = (float)halfW; break;
                case "Right": area.X = (float)(full.Width - halfW); break;
                case "Top": area.Y = (float)halfH; break;
                case "Bottom": area.Y = (float)(full.Height - halfH); break;
                case "Center": area.X = (float)full.Center.X; area.Y = (float)full.Center.Y; break;
                case "FlipX": area.X = (float)full.Width - area.X; break;
                case "FlipY": area.Y = (float)full.Height - area.Y; break;
            }
            Fit();
        }

        Chip(T("Full"), () => Resize(full, 1));
        Chip(T("Center"), () => Align("Center"));
        if (!isTablet && bounds.Length > 1)
        {
            for (var i = 0; i < bounds.Length; i++)
            {
                var monitor = bounds[i];
                Chip(string.Format(T("Monitor"), i + 1), () => Resize(monitor, 1));
            }
        }

        var more = new MenuFlyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedLeft };
        void Command(string label, Action run)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => run();
            more.Items.Add(item);
        }
        foreach (var key in new[] { "Left", "Right", "Top", "Bottom", "FlipX", "FlipY" })
            Command(T(key), () => Align(key));
        if (isTablet)
        {
            more.Items.Add(new Separator());
            Command(T("Quarter"), () => Resize(full, .5));
            Command(T("Convert"), async () => await ConvertArea(config, area));
        }
        var moreButton = new Button { Content = Icons.Create(Icons.More), Classes = { "chip" } };
        ToolTip.SetTip(moreButton, T("AreaActions"));
        moreButton.Click += (_, _) => more.ShowAt(moreButton);
        chips.Children.Add(moreButton);
        side.Children.Add(chips);

        return side;
    }

    /// <summary>
    /// Compact number field: label inside on the left, value on the right, unit as a quiet suffix.
    /// Arrow up/down nudge the value (Shift = x10).
    /// </summary>
    private static Control InlineNumber(string label, Func<float> read, Action<float> write, string suffix, float? min, System.ComponentModel.INotifyPropertyChanged source)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        string Format(float v) => v.ToString("0.###", culture);
        var box = new TextBox { Text = Format(read()), HorizontalContentAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        box.Classes.Add("bare");
        box.Classes.Add("mono");
        var updating = false;
        bool TryParse(string? text, out float value) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float, culture, out value) ||
            float.TryParse(text?.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        void Commit(float value)
        {
            if (min is { } m) value = Math.Max(m, value);
            if (!float.IsFinite(value)) return;
            updating = true;
            try { write(value); }
            finally { updating = false; }
        }
        box.TextChanged += (_, _) =>
        {
            if (!updating && box.IsFocused && TryParse(box.Text, out var v)) Commit(v);
        };
        box.LostFocus += (_, _) => box.Text = Format(read());
        box.PointerWheelChanged += (_, e) =>
        {
            if (!box.IsFocused) return; // only the focused field reacts, so page scrolling is never hijacked
            var step = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? 10f : 1f;
            Commit(read() + (e.Delta.Y > 0 ? step : -step));
            box.Text = Format(read());
            e.Handled = true;
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Avalonia.Input.Key.Up or Avalonia.Input.Key.Down)
            {
                var step = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift) ? 10f : 1f;
                Commit(read() + (e.Key == Avalonia.Input.Key.Up ? step : -step));
                box.Text = Format(read());
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Enter)
            {
                box.Text = Format(read());
                e.Handled = true;
            }
        };
        void Sync(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (updating || box.IsFocused) return;
            box.Text = Format(read());
        }
        box.AttachedToVisualTree += (_, _) => source.PropertyChanged += Sync;
        box.DetachedFromVisualTree += (_, _) => source.PropertyChanged -= Sync;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        caption.Classes.Add("muted");
        caption.Classes.Add("caption");
        grid.Children.Add(caption);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        var unit = new TextBlock { Text = suffix, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        unit.Classes.Add("muted");
        unit.Classes.Add("meta");
        Grid.SetColumn(unit, 2);
        grid.Children.Add(unit);
        var field = new Border { Child = grid, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Ibeam) };
        field.Classes.Add("field");
        field.PointerPressed += (_, _) => box.Focus();
        return field;
    }

    private async Task ConvertArea(AbsoluteModeSettings config, AreaSettings area)
    {
        var tablet = connected.FirstOrDefault(t => t.Properties.Name == selectedTablet);
        if (tablet == null) return;
        var dialog = ToolWindow("Convert");
        dialog.Width = 480;
        dialog.Height = 440;
        var types = Choices<IAreaConverter>();
        var select = new ComboBox { ItemsSource = types };
        var fields = new StackPanel();
        var body = new StackPanel { Spacing = 0 };
        body.Children.Add(Field(T("ConvertFrom"), select));
        body.Children.Add(fields);
        var apply = new Button { Content = T("Apply"), Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Right };
        select.SelectionChanged += (_, _) =>
        {
            fields.Children.Clear();
            if (select.SelectedItem is not PluginChoice choice ||
                new PluginSettingStore(choice.Type).Construct<IAreaConverter>() is not { } converter) return;
            var values = new double[4];
            var labels = new[] { converter.Top, converter.Left, converter.Bottom, converter.Right };
            for (var i = 0; i < labels.Length; i++)
            {
                var index = i;
                fields.Children.Add(Field(PluginText(choice.Type, labels[i]), Number(0, v => values[index] = v)));
            }
            apply.Tag = (Action)(() =>
            {
                area.Area = converter.Convert(tablet, values[0], values[1], values[2], values[3]);
                dialog.Close();
            });
        };
        apply.Click += (_, _) => (apply.Tag as Action)?.Invoke();
        if (types.Length > 0) select.SelectedIndex = 0;
        var page = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        page.Children.Add(Card(T("Convert"), body, T("ConvertHint")));
        page.Children.Add(apply);
        dialog.Content = new ScrollViewer { Content = page };
        await dialog.ShowDialog(this);
    }
}
