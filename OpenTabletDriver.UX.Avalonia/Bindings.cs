using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;

namespace TabletDriver.UI;

public partial class MainWindow
{
    private void Bindings(BindingSettings model, TabletReference tablet)
    {
        Control Slot(string label, PluginSettingStore? value, Action<PluginSettingStore?> set, string? hint = null)
        {
            var text = new TextBlock
            {
                Text = value == null ? T("Unbound") : BindingLabel(value),
                Classes = { value == null ? "muted" : "secondary" }, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            content.Children.Add(text);
            var chevron = Icons.Create(Icons.Edit, 14);
            Grid.SetColumn(chevron, 1);
            content.Children.Add(chevron);
            var button = new Button { Content = content, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            button.Click += async (_, _) => await EditBinding(label, value, set);
            return Field(label, button, hint);
        }
        void Slots(StackPanel body, string key, PluginSettingStoreCollection stores, List<Control>? collect = null)
        {
            for (int i = 0; i < stores.Count; i++)
            {
                var index = i;
                var row = Slot(string.Format(T(key), i + 1), stores[i], value => stores[index] = value);
                collect?.Add(row);
                body.Children.Add(row);
            }
        }

        var pen = new StackPanel();
        pen.Children.Add(Slot(T("Tip"), model.TipButton, v => model.TipButton = v, T("TipHint")));
        pen.Children.Add(Field(T("Threshold"), Number(model.TipActivationThreshold, v => model.TipActivationThreshold = (float)v, 0, 100), T("ThresholdHint")));
        pen.Children.Add(Slot(T("Eraser"), model.EraserButton, v => model.EraserButton = v));
        pen.Children.Add(Field(T("EraserThreshold"), Number(model.EraserActivationThreshold, v => model.EraserActivationThreshold = (float)v, 0, 100)));
        Editor.Children.Add(Card(T("Pen"), pen));

        var barrel = new StackPanel();
        var penRows = new List<Control>();
        barrel.Children.Add(SwitchRow(T("DisablePenButtons"), model.DisablePenButtons, v =>
        {
            model.DisablePenButtons = v;
            foreach (var row in penRows) row.IsEnabled = !v;
        }, T("DisablePenButtonsHint")));
        Slots(barrel, "PenButton", model.PenButtons, penRows);
        foreach (var row in penRows) row.IsEnabled = !model.DisablePenButtons;
        barrel.Children.Add(SwitchRow(T("DragBindings"), model.EnableDragBindings, v => model.EnableDragBindings = v, T("DragBindingsHint")));
        Editor.Children.Add(Card(T("PenButtons"), barrel));

        var buttons = new StackPanel();
        Slots(buttons, "AuxButton", model.AuxButtons);
        Slots(buttons, "MouseButton", model.MouseButtons);
        if (tablet.Properties.Specifications.MouseButtons != null)
        {
            buttons.Children.Add(Slot(T("ScrollUp"), model.MouseScrollUp, v => model.MouseScrollUp = v));
            buttons.Children.Add(Slot(T("ScrollDown"), model.MouseScrollDown, v => model.MouseScrollDown = v));
        }
        if (buttons.Children.Count != 0) Editor.Children.Add(Card(T("Buttons"), buttons));

        for (var i = 0; i < model.WheelBindings.Count; i++)
        {
            var wheel = model.WheelBindings[i];
            var body = new StackPanel();
            var step = (decimal)(wheel.StepSize ?? 1);
            body.Children.Add(Slot(T("CW"), wheel.ClockwiseRotation, v => wheel.ClockwiseRotation = v));
            body.Children.Add(Field(T("AngleThreshold"), Number(wheel.ClockwiseActivationThreshold, v => wheel.ClockwiseActivationThreshold = (float)v, step, 360, step)));
            body.Children.Add(Slot(T("CCW"), wheel.CounterClockwiseRotation, v => wheel.CounterClockwiseRotation = v));
            body.Children.Add(Field(T("AngleThreshold"), Number(wheel.CounterClockwiseActivationThreshold, v => wheel.CounterClockwiseActivationThreshold = (float)v, step, 360, step)));
            Slots(body, "WheelButton", wheel.WheelButtons);
            Editor.Children.Add(Card(string.Format(T("Wheel"), i + 1), body));
        }

        var input = new StackPanel();
        input.Children.Add(SwitchRow(T("DisablePressure"), model.DisablePressure, v => model.DisablePressure = v));
        input.Children.Add(SwitchRow(T("DisableTilt"), model.DisableTilt, v => model.DisableTilt = v));
        input.Children.Add(SwitchRow(T("DisableRotation"), model.DisableRotation, v => model.DisableRotation = v));
        Editor.Children.Add(Card(T("PenData"), input, T("PenDataHint")));
    }

    /// <summary>Human label for a binding, e.g. "Клавиша · Z" instead of "Key Binding: (Key: Z)".</summary>
    private static string BindingLabel(PluginSettingStore store)
    {
        var type = store.GetTypeInfo();
        if (type == null) return store.Path ?? T("Unbound");
        var name = PluginText(type, type.GetCustomAttribute<OpenTabletDriver.Plugin.Attributes.PluginNameAttribute>()?.Name ?? type.Name);
        var values = store.Settings.Where(s => s.HasValue).Select(s => s.Value!.Type == Newtonsoft.Json.Linq.JTokenType.Object
                ? s.Value.ToObject<PluginSettingStore>() is { } inner ? BindingLabel(inner) : s.Value.ToString()
                : s.Value!.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return values.Length == 0 ? name : $"{name} · {string.Join(", ", values)}";
    }

    private async Task EditBinding(string title, PluginSettingStore? original, Action<PluginSettingStore?> save)
    {
        var draft = original?.Clone();
        var dialog = new Window { Title = title, Icon = Icon, Width = 560, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = title, Classes = { "section" } });
        var types = Choices<IBinding>();
        var properties = new StackPanel { Spacing = 8 };
        var select = new ComboBox { ItemsSource = types, SelectedItem = types.FirstOrDefault(t => t.Type.FullName == draft?.Path) };
        void SetDraft(PluginSettingStore? store)
        {
            draft = store;
            select.SelectedItem = types.FirstOrDefault(t => t.Type.FullName == draft?.Path);
            properties.Children.Clear();
            if (draft != null) Properties(properties, draft);
        }
        select.SelectionChanged += (_, _) =>
        {
            if (select.SelectedItem is PluginChoice choice && choice.Type.FullName != draft?.Path)
                SetDraft(choice.Type.FullName == original?.Path ? original.Clone() : new PluginSettingStore(choice.Type));
        };
        SetDraft(draft);
        var capture = new Button { Content = T("Capture") };
        capture.Click += async (_, _) =>
        {
            var captured = await Capture(dialog);
            if (captured != null) SetDraft(captured);
        };
        body.Children.Add(Field(T("BindingType"), select));
        body.Children.Add(capture);
        body.Children.Add(properties);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var clear = ButtonFor("Clear", () => SetDraft(null));
        clear.Classes.Add("quiet");
        actions.Children.Add(clear);
        var cancel = ButtonFor("Cancel", () => dialog.Close(false));
        Grid.SetColumn(cancel, 1);
        actions.Children.Add(cancel);
        var ok = ButtonFor("OK", () => dialog.Close(true));
        ok.Classes.Add("primary");
        ok.IsDefault = true;
        Grid.SetColumn(ok, 2);
        actions.Children.Add(ok);
        body.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = body };
        if (await dialog.ShowDialog<bool>(this)) { save(draft); RenderPage(); }
    }

    private static Task<PluginSettingStore?> Capture(Window owner)
    {
        var dialog = new Window { Title = T("Capture"), Icon = owner.Icon, Content = Label(T("CapturePrompt")),
            Padding = new Thickness(20), Width = 400, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.KeyDown += (_, e) =>
        {
            var key = e.Key.ToString();
            if (KeyBindingKeys(key) is not string valid) return;
            var keys = new List<string>();
            if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) keys.Add("Application");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) keys.Add("Control");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
            keys.Add(valid);
            var store = new PluginSettingStore(keys.Count == 1 ? typeof(OpenTabletDriver.Desktop.Binding.KeyBinding) : typeof(MultiKeyBinding));
            store[keys.Count == 1 ? "Key" : "Keys"].SetValue(string.Join("+", keys));
            dialog.Close(store);
        };
        dialog.AddHandler(PointerPressedEvent, (_, e) =>
        {
            var p = e.GetCurrentPoint(dialog).Properties;
            var button = p.IsLeftButtonPressed ? "Left" : p.IsRightButtonPressed ? "Right" : p.IsMiddleButtonPressed ? "Middle" : null;
            if (button == null) return;
            var store = new PluginSettingStore(typeof(MouseBinding));
            store["Button"].SetValue(button);
            dialog.Close(store);
        }, RoutingStrategies.Tunnel);
        return dialog.ShowDialog<PluginSettingStore?>(owner);
    }

    private static string? KeyBindingKeys(string key) =>
        OpenTabletDriver.Desktop.Binding.KeyBinding.ValidKeys?.FirstOrDefault(name =>
            string.Equals(name, key, StringComparison.OrdinalIgnoreCase));
}
