using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Attributes;

namespace TabletDriver.UI;

public partial class MainWindow
{
    /// <summary>Empty state: a headline naming the space, one line of explanation, one action.</summary>
    private static Control EmptyState(string key, string icon, string actionKey, Action action)
    {
        var box = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 96, 0, 0), MaxWidth = 420 };
        var mark = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new ContentControl { Content = Icons.Create(icon, 18), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable("Raised"));
        box.Children.Add(mark);
        box.Children.Add(new TextBlock { Text = T(key + "Title"), Classes = { "section" }, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
        box.Children.Add(new TextBlock { Text = T(key + "Body"), Classes = { "secondary" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, TextAlignment = Avalonia.Media.TextAlignment.Center });
        var button = ButtonFor(actionKey, action);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        button.Margin = new Thickness(0, 8, 0, 0);
        box.Children.Add(button);
        return box;
    }

    private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private static Button ButtonFor(string key, Action action)
    {
        var button = new Button { Content = T(key) };
        button.Click += (_, _) => action();
        return button;
    }
    private static Control Toggle(string key, bool initial, Action<bool> change) => SwitchRow(T(key), initial, change);

    private static Border Row(Control left, Control? right)
    {
        // inputs get a flexible column so narrow windows shrink them instead of pushing past the edge
        var flexible = right is ComboBox or TextBox or NumericUpDown or Slider or Button;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = flexible ? 24 : 16 };
        left.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(left);
        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }
        var row = new Border { Child = grid };
        row.Classes.Add("row");
        return row;
    }

    private static Control SwitchRow(string label, bool initial, Action<bool> change, string? hint = null)
    {
        var toggle = new ToggleSwitch { IsChecked = initial, Classes = { "compact" } };
        toggle.IsCheckedChanged += (_, _) => change(toggle.IsChecked == true);
        var row = Row(LabelBlock(label, hint), toggle);
        row.Tag = toggle;
        return row;
    }

    private static Control LabelBlock(string label, string? hint)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(hint))
            text.Children.Add(new TextBlock { Text = hint, Classes = { "muted", "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left });
        return text;
    }

    private static Control Field(string label, Control input, string? hint = null)
    {
        if (input is ComboBox or TextBox or NumericUpDown or Slider or Button)
        {
            input.Width = 260;
            input.HorizontalAlignment = HorizontalAlignment.Right;
        }
        return Row(LabelBlock(label, hint), input);
    }

    /// <summary>A titled group: section title above one panel whose rows are separated by hairlines.</summary>
    private static StackPanel Card(string title, StackPanel body, string? subtitle = null)
    {
        var rows = body.Children.OfType<Border>().Where(b => b.Classes.Contains("row")).ToList();
        if (rows.Count > 0) rows[^1].Classes.Add("last");
        body.Spacing = 0;
        var group = new Border { Child = body };
        group.Classes.Add("group");
        var section = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(4, 0) };
        header.Children.Add(new TextBlock { Text = title, Classes = { "section" } });
        if (subtitle != null)
            header.Children.Add(new TextBlock { Text = subtitle, Classes = { "muted", "caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        section.Children.Add(header);
        section.Children.Add(group);
        return section;
    }

    private static NumericUpDown Number(double value, Action<double> change, decimal min = -10000000,
        decimal max = 10000000, decimal step = 0.1m)
    {
        var input = new NumericUpDown { Minimum = min, Maximum = max, Increment = step, FormatString = "0.####", ShowButtonSpinner = false,
            Value = double.IsFinite(value) ? (decimal)Math.Clamp(value, (double)min, (double)max) : null };
        input.ValueChanged += (_, e) => { if (e.NewValue is decimal n) change((double)n); };
        return input;
    }
    private sealed record PluginChoice(TypeInfo Type)
    {
        public override string ToString() => PluginText(Type, Type.GetCustomAttribute<PluginNameAttribute>()?.Name ?? Type.FullName!);
    }
    private static PluginChoice[] Choices<T>() => AppInfo.PluginManager.GetChildTypes<T>()
        .Select(type => new PluginChoice(type)).OrderBy(choice => choice.ToString()).ToArray();

    private static string? PluginOwner(Type type)
    {
        if (type.Assembly == typeof(Settings).Assembly) return null;
        var owner = AppInfo.PluginManager.GetLoadedPlugins().FirstOrDefault(p => p.Assemblies.Contains(type.Assembly))?.FriendlyName;
        var title = new PluginChoice(type.GetTypeInfo()).ToString();
        return owner == null || title.StartsWith(owner, StringComparison.OrdinalIgnoreCase) ? null : owner;
    }

    private static string PluginText(Type type, string text) => type.Assembly == typeof(Settings).Assembly
        ? Loc.Instance.Known(text) : text;

    private void PluginCollection<TPlugin>(PluginSettingStoreCollection stores) where TPlugin : class
    {
        var choices = Choices<TPlugin>();
        if (choices.Length == 0)
        {
            Editor.Children.Add(EmptyState(typeof(TPlugin) == typeof(OpenTabletDriver.Plugin.ITool) ? "EmptyTools" : "EmptyFilters",
                Icons.Plug, "ManagePlugins", async () => await ManagePlugins()));
        }
        foreach (var choice in choices)
        {
            var store = stores.FromType(choice.Type);
            var body = new StackPanel();
            var properties = new StackPanel();
            Properties(properties, store);
            if (properties.Children.OfType<Border>().LastOrDefault(b => b.Classes.Contains("row")) is { } lastRow)
                lastRow.Classes.Add("last");
            // settings slide out under the switch; a hairline separates them from it
            var reveal = new Border { Child = properties, IsVisible = store.Enable && properties.Children.Count > 0, BorderThickness = new Thickness(0, 1, 0, 0) };
            reveal.Bind(Border.BorderBrushProperty, reveal.GetResourceObservable("Hairline"));
            body.Children.Add(Toggle("Enabled", store.Enable, value =>
            {
                store.Enable = value;
                if (properties.Children.Count > 0) _ = Motion.Reveal(reveal, value);
            }));
            body.Children.Add(reveal);
            Editor.Children.Add(Card(choice.ToString(), body, PluginOwner(choice.Type)));
        }
    }

    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is { Length: > 140 } ? line[..137] + "…" : line;
    }

    private static void Properties(StackPanel panel, PluginSettingStore store)
    {
        if (store.GetTypeInfo() is not { } type) return;
        foreach (var property in type.GetProperties())
        {
            if (property.GetCustomAttribute<PropertyAttribute>() is not { } meta) continue;
            var setting = store[property];
            var slider = property.GetCustomAttribute<SliderPropertyAttribute>();
            if (!setting.HasValue)
                setting.SetValue(property.GetCustomAttribute<DefaultPropertyValueAttribute>()?.Value ?? slider?.DefaultValue);
            var kind = property.PropertyType;
            var value = setting.GetValue(kind);
            Control control;
            if (kind == typeof(bool))
            {
                var description = property.GetCustomAttribute<BooleanPropertyAttribute>()?.Description;
                var box = new ToggleSwitch { IsChecked = value is true, Classes = { "compact" } };
                box.IsCheckedChanged += (_, _) => setting.SetValue(box.IsChecked == true);
                if (description != null) ToolTip.SetTip(box, PluginText(type, description));
                control = box;
            }
            else if (kind.IsEnum || property.GetCustomAttribute<PropertyValidatedAttribute>() is not null)
            {
                var options = kind.IsEnum ? Enum.GetNames(kind) :
                    property.GetCustomAttribute<PropertyValidatedAttribute>()!.GetValue<IEnumerable<string>>(property)?.ToArray() ?? [];
                var combo = new ComboBox { ItemsSource = options.Select(o => PluginText(type, o)).ToArray(),
                    SelectedIndex = Array.IndexOf(options, value?.ToString()), HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedIndex < 0) return;
                    var selected = options[combo.SelectedIndex];
                    setting.SetValue(kind.IsEnum ? Enum.Parse(kind, selected) : selected);
                };
                control = combo;
            }
            else if (slider != null)
            {
                var input = new Slider { Minimum = slider.Min, Maximum = slider.Max, Value = Convert.ToDouble(value) };
                input.ValueChanged += (_, e) => setting.SetValue((float)e.NewValue);
                control = input;
            }
            else
            {
                var converter = TypeDescriptor.GetConverter(kind);
                var input = new TextBox { Text = value == null ? "" : converter.ConvertToString(value),
                    Watermark = property.GetCustomAttribute<PropertyUnvalidatedStringPlaceholderAttribute>()?.Placeholder };
                input.TextChanged += (_, _) =>
                {
                    var text = input.Text ?? "";
                    if (kind == typeof(string)) setting.SetValue(text);
                    else if (converter.IsValid(text)) setting.SetValue(converter.ConvertFromString(null, CultureInfo.CurrentCulture, text));
                };
                control = input;
            }
            var hint = property.GetCustomAttribute<ToolTipAttribute>() is { } tip ? PluginText(type, tip.ToolTip) : null;
            if (hint != null) ToolTip.SetTip(control, hint);
            var name = PluginText(type, meta.DisplayName ?? property.Name);
            if (property.GetCustomAttribute<UnitAttribute>() is { } unit) name += $", {unit.Unit}";
            panel.Children.Add(Field(name, control, FirstLine(hint)));
        }
    }
}
