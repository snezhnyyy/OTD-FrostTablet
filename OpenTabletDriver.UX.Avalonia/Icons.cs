using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace TabletDriver.UI;

/// <summary>
/// Line icons (lucide, ISC license) drawn as 24x24 strokes, 1.5 stroke, colored by the text role.
/// </summary>
internal static class Icons
{
    public const string Monitor = "M4 3h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M8 21h8 M12 17v4";
    public const string Activity = "M22 12h-4l-3 9L9 3l-3 9H2";
    public const string Keyboard = "M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z M6 8h.01 M10 8h.01 M14 8h.01 M18 8h.01 M8 12h.01 M12 12h.01 M16 12h.01 M7 16h10";
    public const string Wrench = "M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z";
    public const string Terminal = "M4 17l6-6-6-6 M12 19h8";
    public const string Moon = "M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9z";
    public const string Sun = "M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41";
    public const string Globe = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z";
    public const string ChevronDown = "M6 9l6 6 6-6";
    public const string Refresh = "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5";
    public const string Plug = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8z";
    public const string More = "M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M19 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z M5 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2z";
    public const string Check = "M20 6L9 17l-5-5";
    public const string Pen = "M12 20h9 M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z";
    public const string Copy = "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z M4 16a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2";
    public const string Download = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3";
    public const string Maximize = "M8 3H5a2 2 0 0 0-2 2v3 M21 8V5a2 2 0 0 0-2-2h-3 M3 16v3a2 2 0 0 0 2 2h3 M16 21h3a2 2 0 0 0 2-2v-3";
    public const string Crosshair = "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20z M22 12h-4 M6 12H2 M12 6V2 M12 22v-4";
    public const string Edit = "M12 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7 M18.4 2.6a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4z";

    /// <summary>Creates a 16px (default) line icon that inherits the foreground of its parent.</summary>
    public static Control Create(string data, double size = 16, IBrush? brush = null)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        if (brush != null) path.Stroke = brush;
        else path.Bind(Shape.StrokeProperty, path.GetObservable(Avalonia.Controls.Documents.TextElement.ForegroundProperty));
        var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>Icon followed by a label, 8px gap.</summary>
    public static StackPanel WithText(string data, string text)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Create(data));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return row;
    }
}
