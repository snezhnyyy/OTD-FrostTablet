using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenTabletDriver.Desktop.Profiles;

namespace TabletDriver.UI;

/// <summary>
/// The area map: the tablet drawn as an object (body, active surface, corner marks) or the monitor layout,
/// with the mapped area in the accent color and drafting-style dimension lines.
/// </summary>
internal sealed class AreaPreview : Control
{
    private readonly AreaSettings area;
    private readonly Rect[] devices;
    private readonly Rect full;
    private readonly bool isTablet;
    private Point? last;
    public Action<double, double>? MoveArea { get; set; }

    private static readonly Typeface Mono = new("Cascadia Mono, Consolas, monospace");

    internal AreaPreview(AreaSettings area, Rect[] devices, bool isTablet)
    {
        this.area = area;
        this.devices = devices;
        this.isTablet = isTablet;
        full = devices.Aggregate((a, b) => a.Union(b));
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        AttachedToVisualTree += (_, _) => area.PropertyChanged += Changed;
        DetachedFromVisualTree += (_, _) => area.PropertyChanged -= Changed;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => InvalidateVisual();

    private IBrush Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    // the drawn object around the mapped space: tablets get a body margin, monitors none
    private const double SideRoom = 64;

    private Rect Frame => isTablet ? full.Inflate(new Thickness(full.Width * 0.14, full.Height * 0.2)) : full;

    private double Scale
    {
        get
        {
            var frame = Frame;
            // leave room on the right and top for the dimension labels
            return Math.Max(.0001, Math.Min((Bounds.Width - 2 * SideRoom) / Math.Max(1, frame.Width), (Bounds.Height - 56) / Math.Max(1, frame.Height)));
        }
    }

    private Point Origin
    {
        get
        {
            var frame = Frame;
            var scale = Scale;
            return new Point((Bounds.Width - frame.Width * scale) / 2 - frame.X * scale, (Bounds.Height - frame.Height * scale) / 2 - frame.Y * scale);
        }
    }

    private Rect ToView(Rect r, Point origin, double scale) =>
        new(origin.X + r.X * scale, origin.Y + r.Y * scale, r.Width * scale, r.Height * scale);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        var scale = Scale;
        var origin = Origin;
        var hairline = new Pen(Brush("Hairline"), 1);
        var muted = Brush("TextMuted");

        if (isTablet)
        {
            var body = ToView(Frame, origin, scale);
            context.DrawRectangle(Brush("TabletBody"), new Pen(Brush("Hairline"), 1), body, 14, 14);
            // a single soft top highlight gives the body some material without a glow
            context.DrawLine(new Pen(new SolidColorBrush(Colors.White, 0.06), 1), body.TopLeft + new Vector(14, 0.5), body.TopRight + new Vector(-14, 0.5));
            var surface = ToView(full, origin, scale);
            context.DrawRectangle(Brush("TabletSurface"), null, surface, 4, 4);
            var mark = new Pen(new SolidColorBrush(Colors.White, 0.28), 1);
            const double m = 8;
            foreach (var (corner, dx, dy) in new[] { (surface.TopLeft, 1, 1), (surface.TopRight, -1, 1), (surface.BottomLeft, 1, -1), (surface.BottomRight, -1, -1) })
            {
                context.DrawLine(mark, corner, corner + new Vector(m * dx, 0));
                context.DrawLine(mark, corner, corner + new Vector(0, m * dy));
            }
        }
        else
        {
            for (var i = 0; i < devices.Length; i++)
            {
                var rect = ToView(devices[i], origin, scale).Deflate(2);
                context.DrawRectangle(Brush("Raised"), hairline, rect, 6, 6);
                var label = Text((i + 1).ToString(CultureInfo.InvariantCulture), muted);
                context.DrawText(label, rect.TopLeft + new Vector(8, 6));
            }
        }

        var center = new Point(origin.X + area.X * scale, origin.Y + area.Y * scale);
        var w = Math.Max(0, area.Width * scale);
        var h = Math.Max(0, area.Height * scale);
        var areaRect = new Rect(center.X - w / 2, center.Y - h / 2, w, h);
        using (context.PushTransform(Matrix.CreateRotation(area.Rotation * Math.PI / 180, center)))
        {
            context.DrawRectangle(Brush("AccentFill"), new Pen(Brush("Accent"), 1.5), areaRect, 3, 3);
        }

        // dimension lines (only when the area is axis-aligned, otherwise a centered size label)
        var unit = isTablet ? Loc.Instance["UnitMm"] : Loc.Instance["UnitPx"];
        var fmt = isTablet ? "0.#" : "0";
        var widthText = area.Width.ToString(fmt, CultureInfo.InvariantCulture);
        var heightText = area.Height.ToString(fmt, CultureInfo.InvariantCulture);
        if (Math.Abs(area.Rotation % 360) < 0.01 && w > 24 && h > 16)
        {
            var dim = new Pen(Brush("TextSecondary"), 1);
            var top = areaRect.Top - 10;
            DimensionH(context, dim, areaRect.Left, areaRect.Right, top, $"{widthText} {unit}");
            var right = areaRect.Right + 10;
            DimensionV(context, dim, areaRect.Top, areaRect.Bottom, right, $"{heightText} {unit}");
        }
        else if (w > 40)
        {
            var label = Text($"{widthText}×{heightText}", Brush("TextMain"));
            context.DrawText(label, center - new Vector(label.Width / 2, label.Height / 2));
        }
    }

    private FormattedText Text(string text, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 11, brush);

    private void DimensionH(DrawingContext context, Pen pen, double x1, double x2, double y, string label)
    {
        var text = Text(label, Brush("TextSecondary"));
        var mid = (x1 + x2) / 2;
        var gap = text.Width / 2 + 6;
        context.DrawLine(pen, new Point(x1, y - 4), new Point(x1, y + 4));
        context.DrawLine(pen, new Point(x2, y - 4), new Point(x2, y + 4));
        if (x2 - x1 > text.Width + 20)
        {
            context.DrawLine(pen, new Point(x1, y), new Point(mid - gap, y));
            context.DrawLine(pen, new Point(mid + gap, y), new Point(x2, y));
            context.DrawText(text, new Point(mid - text.Width / 2, y - text.Height / 2));
        }
        else
        {
            context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            context.DrawText(text, new Point(mid - text.Width / 2, y - text.Height - 2));
        }
    }

    private void DimensionV(DrawingContext context, Pen pen, double y1, double y2, double x, string label)
    {
        var text = Text(label, Brush("TextSecondary"));
        context.DrawLine(pen, new Point(x - 4, y1), new Point(x + 4, y1));
        context.DrawLine(pen, new Point(x - 4, y2), new Point(x + 4, y2));
        context.DrawLine(pen, new Point(x, y1), new Point(x, y2));
        context.DrawText(text, new Point(x + 6, (y1 + y2) / 2 - text.Height / 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        last = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (last is not Point previous) return;
        var point = e.GetPosition(this);
        var delta = point - previous;
        last = point;
        MoveArea?.Invoke(delta.X / Scale, delta.Y / Scale);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        last = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) => last = null;
}
