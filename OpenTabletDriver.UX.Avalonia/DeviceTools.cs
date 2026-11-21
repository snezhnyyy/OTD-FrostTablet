using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.ViewModels;
using OpenTabletDriver.Desktop.ViewModels.Utility;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace TabletDriver.UI;

public partial class MainWindow
{
    private async Task DeviceTool(bool send)
    {
        var devices = (await Driver.GetDevices()).ToArray();
        var dialog = ToolWindow(send ? "InitSender" : "StringReader");
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 9 };
        var names = devices.Select(d => $"{d.FriendlyName ?? d.ProductName}   {d.VendorID:X4}:{d.ProductID:X4}").ToArray();
        var select = new ComboBox { ItemsSource = names };
        var vendor = Number(0, _ => {}, 0, 65535, 1);
        var product = Number(0, _ => {}, 0, 65535, 1);
        var index = Number(1, _ => {}, 1, 255, 1);
        var port = new ComboBox { ItemsSource = Enumerable.Range(0, 10).Select(i => i.ToString()).ToArray(), SelectedIndex = 0 };
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedIndex < 0) return;
            var device = devices[select.SelectedIndex];
            vendor.Value = device.VendorID;
            product.Value = device.ProductID;
            port.ItemsSource = devices.Where(d => d.VendorID == device.VendorID && d.ProductID == device.ProductID)
                .Select(d => d.DeviceAttributes.TryGetValue("USB_INTERFACE_NUMBER", out var value) ? value : "0").Distinct().ToArray();
            port.SelectedIndex = 0;
        };
        if (devices.Length > 0) select.SelectedIndex = 0;
        body.Children.Add(Field(T("Device"), select));
        body.Children.Add(Field(T("VendorID"), vendor));
        body.Children.Add(Field(T("ProductID"), product));
        var result = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 70 };
        var closing = new CancellationTokenSource();
        dialog.Closed += (_, _) => closing.Cancel();
        async Task Run(Func<Task> operation)
        {
            body.IsEnabled = false;
            try { await operation(); }
            catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
            catch (Exception error) { result.Text = error.Message; }
            finally { body.IsEnabled = true; }
        }
        if (send)
        {
            var kind = new ComboBox { ItemsSource = new[] { T("FeatureReport"), T("OutputReport") } };
            var format = new ComboBox { ItemsSource = new[] { T("Base64"), T("Hex") }, SelectedIndex = 0 };
            var data = new TextBox();
            body.Children.Add(Field(T("Interface"), port));
            body.Children.Add(Field(T("ReportType"), kind));
            body.Children.Add(Field(T("Encoding"), format));
            body.Children.Add(Field(T("Data"), data));
            body.Children.Add(ButtonFor("Send", async () => await Run(async () =>
            {
                if (kind.SelectedIndex < 0 || port.SelectedItem is not string usb || string.IsNullOrWhiteSpace(data.Text))
                    throw new InvalidDataException(T("MissingReport"));
                var bytes = format.SelectedIndex == 0 ? Convert.FromBase64String(data.Text) : Convert.FromHexString(data.Text);
                var vid = (int)(vendor.Value ?? 0);
                var pid = (int)(product.Value ?? 0);
                await (kind.SelectedIndex == 0 ? Driver.SendFeatureInit(vid, pid, usb, bytes) :
                    Driver.SendOutputInit(vid, pid, usb, bytes)).WaitAsync(TimeSpan.FromSeconds(5), closing.Token);
                result.Text = T("Sent");
            })));
        }
        else
        {
            Task<string> Read(int i) => Driver.RequestDeviceString((int)(vendor.Value ?? 0), (int)(product.Value ?? 0), i)
                .WaitAsync(TimeSpan.FromSeconds(5), closing.Token);
            body.Children.Add(Field(T("StringIndex"), index));
            body.Children.Add(ButtonFor("Read", async () => await Run(async () => result.Text = await Read((int)(index.Value ?? 1)))));
            var includeDfu = Toggle("ReadDFU", false, _ => {});
            body.Children.Add(includeDfu);
            body.Children.Add(ButtonFor("DumpStrings", async () => await Run(async () =>
            {
                if (!devices.Any(d => d.VendorID == vendor.Value && d.ProductID == product.Value))
                    throw new InvalidDataException(T("NoDevice"));
                var file = await dialog.StorageProvider.SaveFilePickerAsync(new() { Title = T("DumpStrings"), SuggestedFileName = "device-strings.txt" });
                if (file?.TryGetLocalPath() is not string path) return;
                await using var writer = File.CreateText(path);
                for (var i = 1; i <= 255; i++)
                {
                    closing.Token.ThrowIfCancellationRequested();
                    string value;
                    if (vendor.Value == 10429 && i == 200 && (includeDfu.Tag as ToggleSwitch)?.IsChecked != true) value = T("SkippedDFU");
                    else
                    {
                        try { value = await Read(i); }
                        catch (Exception ex) when (!closing.IsCancellationRequested) { value = ex.Message; }
                    }
                    await writer.WriteLineAsync($"{i}: {value}");
                    result.Text = $"{i}: {value}";
                }
            })));
        }
        body.Children.Add(result);
        dialog.Content = new ScrollViewer { Content = body };
        await dialog.ShowDialog(this);
    }

    private async Task Debugger()
    {
        using var vm = new TabletDebuggerViewModel();
        var proxy = Driver;
        var dialog = ToolWindow("Debugger");
        dialog.Width = 1040;
        dialog.Height = 720;

        // ---- stat tiles: numbers first (rate in Hz, interval, pressure, position) ----
        TextBlock Figure() { var t = new TextBlock { FontSize = 28, FontWeight = Avalonia.Media.FontWeight.Medium }; t.Classes.Add("figure"); return t; }
        TextBlock Caption(string key) { var t = new TextBlock { Text = T(key) }; t.Classes.Add("muted"); t.Classes.Add("caption"); return t; }
        TextBlock Sub() { var t = new TextBlock(); t.Classes.Add("muted"); t.Classes.Add("meta"); t.Classes.Add("mono"); return t; }
        var rate = Figure(); var rateSub = Sub();
        var interval = Figure(); var intervalSub = Sub();
        var pressure = Figure(); var pressureSub = Sub();
        var position = Figure(); var positionSub = Sub();
        Border Tile(string key, TextBlock figure, TextBlock sub)
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(Caption(key));
            body.Children.Add(figure);
            body.Children.Add(sub);
            var tile = new Border { Child = body, Padding = new Thickness(16, 14) };
            tile.Classes.Add("stage");
            return tile;
        }
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 12 };
        var tileList = new[] { Tile("DbgRate", rate, rateSub), Tile("DbgInterval", interval, intervalSub), Tile("DbgPressure", pressure, pressureSub), Tile("DbgPosition", position, positionSub) };
        for (var i = 0; i < tileList.Length; i++) { Grid.SetColumn(tileList[i], i); tiles.Children.Add(tileList[i]); }

        // ---- visualizer + data ----
        var visual = new ReportPreview { Height = 300 };
        var visualBox = new Border { Child = visual, Padding = new Thickness(16) };
        visualBox.Classes.Add("stage");

        SelectableTextBlock Mono() { var t = new SelectableTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap }; t.Classes.Add("mono"); return t; }
        var raw = Mono();
        var decoded = Mono();
        var format = new ComboBox { ItemsSource = new[] { T("Hex"), T("Binary") }, SelectedIndex = 0, Width = 120 };
        format.SelectionChanged += (_, _) => vm.DecodingMode = format.SelectedIndex == 0 ? DecodingMode.Hex : DecodingMode.Binary;
        StackPanel Block(string key, Control content, Control? aside = null)
        {
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var title = Caption(key);
            title.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            head.Children.Add(title);
            if (aside != null) { Grid.SetColumn(aside, 1); head.Children.Add(aside); }
            var box = new Border { Child = content, Padding = new Thickness(12, 10), MinHeight = 40 };
            box.Classes.Add("field-block");
            var panel = new StackPanel { Spacing = 6 };
            panel.Children.Add(head);
            panel.Children.Add(box);
            return panel;
        }
        var data = new StackPanel { Spacing = 16 };
        data.Children.Add(Block("Raw", raw, format));
        data.Children.Add(Block("Decoded", decoded));

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), ColumnSpacing = 16 };
        main.Children.Add(visualBox);
        Grid.SetColumn(data, 1);
        main.Children.Add(data);

        // ---- options & extra statistics ----
        var options = new StackPanel();
        options.Children.Add(SwitchRow(T("Visualize"), true, v => visualBox.IsVisible = v));
        options.Children.Add(SwitchRow(T("Record"), false, v => vm.DataRecordingEnabled = v, T("RecordHint")));
        var optionsActions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 8) };
        var resetStats = ButtonFor("ResetStats", vm.ResetStatistics);
        var folder = ButtonFor("RecordingFolder", () => DesktopInterop.OpenFolder(AppInfo.Current.RecordingDirectory));
        optionsActions.Children.Add(resetStats);
        optionsActions.Children.Add(folder);
        options.Children.Add(optionsActions);

        var stats = Mono();
        var filters = new StackPanel();
        var more = new StackPanel { Spacing = 8 };
        more.Children.Add(new Expander { Header = T("Statistics"), Content = stats, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch });
        more.Children.Add(new Expander { Header = T("ReportFilters"), Content = filters, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch });

        var page = new StackPanel { Spacing = 16, Margin = new Thickness(24) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var deviceName = new TextBlock { Text = T("DbgWaiting") };
        deviceName.Classes.Add("section");
        header.Children.Add(deviceName);
        var recorded = Sub();
        recorded.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        Grid.SetColumn(recorded, 1);
        header.Children.Add(recorded);
        page.Children.Add(header);
        page.Children.Add(tiles);
        page.Children.Add(main);
        page.Children.Add(Card(T("DbgOptions"), options));
        page.Children.Add(more);

        var seen = new HashSet<string>();
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timer.Tick += (_, _) =>
        {
            if (vm.ReportData is not { } reportData) return;
            deviceName.Text = vm.DeviceName;
            recorded.Text = vm.DataRecordingEnabled ? string.Format(T("DbgRecorded"), vm.ReportsRecorded) : "";
            if (double.TryParse(vm.ReportRateString, System.Globalization.NumberStyles.Float, culture, out var hz) && double.IsFinite(hz) && hz < 1)
            {
                rate.Text = "—";
                interval.Text = "—";
                rateSub.Text = T("DbgIdle");
                intervalSub.Text = "";
            }
            else if (double.IsFinite(hz) && hz > 0)
            {
                rate.Text = $"{hz:0} {T("UnitHz")}";
                rateSub.Text = T("DbgRateHint");
                interval.Text = $"{1000 / hz:0.00} {T("UnitMs")}";
                intervalSub.Text = T("DbgIntervalHint");
            }
            var report = reportData.ToObject();
            if (report is ITabletReport tablet && reportData.Tablet.Properties.Specifications.Pen is { } pen && pen.MaxPressure > 0)
            {
                pressure.Text = $"{tablet.Pressure * 100.0 / pen.MaxPressure:0}%";
                pressureSub.Text = $"{tablet.Pressure} / {pen.MaxPressure}";
            }
            if (report is IAbsolutePositionReport abs)
            {
                position.Text = $"{abs.Position.X:0}, {abs.Position.Y:0}";
                var d = reportData.Tablet.Properties.Specifications.Digitizer;
                positionSub.Text = d.MaxX > 0 ? $"{abs.Position.X / d.MaxX * d.Width:0.0} × {abs.Position.Y / d.MaxY * d.Height:0.0} {T("UnitMm")}" : "";
            }
            raw.Text = vm.RawTabletData;
            decoded.Text = vm.DecodedTabletData;
            if (stats.IsEffectivelyVisible) stats.Text = string.Join(Environment.NewLine, vm.AdditionalStatistics.DumpTreeAsStrings());
            if (visualBox.IsVisible) { visual.Report = reportData; visual.InvalidateVisual(); }
            foreach (var id in vm.SeenReports.Where(seen.Add))
            {
                var key = id;
                filters.Children.Add(SwitchRow(key, true, on => { if (on) vm.IgnoredReports.Remove(key); else vm.IgnoredReports.Add(key); }));
            }
        };
        dialog.Content = new ScrollViewer { Content = page };
        var active = true;
        void Report(object? sender, DebugReportData data) => Dispatcher.UIThread.Post(() =>
        {
            if (active) vm.HandleReport(sender, data);
        });
        void Disconnected(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(); });
        proxy.DeviceReport += Report;
        client.Disconnected += Disconnected;
        try
        {
            await proxy.SetTabletDebug(true);
            timer.Start();
            await dialog.ShowDialog(this);
        }
        finally
        {
            active = false;
            timer.Stop();
            client.Disconnected -= Disconnected;
            proxy.DeviceReport -= Report;
            if (client.IsConnected && ReferenceEquals(client.Instance, proxy)) await proxy.SetTabletDebug(false);
        }
    }
}

/// <summary>Live view of the pen on the tablet surface: same object styling as the area editor, pressure as a ring.</summary>
internal sealed class ReportPreview : Control
{
    public DebugReportData? Report { get; set; }

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Report is not { } data) return;
        var spec = data.Tablet.Properties.Specifications;
        var digitizer = spec.Digitizer;
        if (digitizer.Width <= 0 || digitizer.Height <= 0) return;
        var frameW = digitizer.Width * 1.28;
        var frameH = digitizer.Height * 1.4;
        var scale = Math.Min(Bounds.Width / frameW, Bounds.Height / frameH);
        var body = new Rect((Bounds.Width - frameW * scale) / 2, (Bounds.Height - frameH * scale) / 2, frameW * scale, frameH * scale);
        var surface = new Rect(body.Center.X - digitizer.Width * scale / 2, body.Center.Y - digitizer.Height * scale / 2, digitizer.Width * scale, digitizer.Height * scale);
        context.DrawRectangle(Brush("TabletBody"), new Pen(Brush("Hairline"), 1), body, 14, 14);
        context.DrawRectangle(Brush("TabletSurface"), null, surface, 4, 4);

        var report = data.ToObject();
        void Dot(System.Numerics.Vector2 point, float maxX, float maxY, double ring)
        {
            if (maxX <= 0 || maxY <= 0) return;
            var p = new Point(surface.X + surface.Width * point.X / maxX, surface.Y + surface.Height * point.Y / maxY);
            if (ring > 0) context.DrawEllipse(Brush("AccentFill"), new Pen(Brush("Accent"), 1), p, 6 + ring * 18, 6 + ring * 18);
            context.DrawEllipse(Brush("Accent"), null, p, 4, 4);
        }
        var pressure = report is ITabletReport t && spec.Pen is { MaxPressure: > 0 } pen ? (double)t.Pressure / pen.MaxPressure : 0;
        if (report is IAbsolutePositionReport position) Dot(position.Position, digitizer.MaxX, digitizer.MaxY, pressure);
        if (report is ITouchReport touch && spec.Touch is { } sensor)
            foreach (var point in touch.Touches)
                if (point != null) Dot(point.Position, sensor.MaxX, sensor.MaxY, 0);
    }
}
