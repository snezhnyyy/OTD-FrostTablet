using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;

namespace TabletDriver.UI;

/// <summary>
/// The only motion in the app: sections slide open / closed (height + fade), one easing curve.
/// </summary>
internal static class Motion
{
    private static readonly Easing Ease = new SplineEasing(0.22, 1, 0.36, 1);
    private static readonly ConditionalWeakTable<Control, CancellationTokenSource> Running = new();

    public static async Task Reveal(Control control, bool show, int durationMs = 240)
    {
        if (Running.TryGetValue(control, out var previous))
        {
            previous.Cancel();
            Running.Remove(control);
        }
        var cts = new CancellationTokenSource();
        Running.Add(control, cts);

        if (show == control.IsVisible && control.MaxHeight is double.PositiveInfinity && control.Opacity >= 1)
            return;

        double from, to;
        if (show)
        {
            var startHeight = control.IsVisible ? control.Bounds.Height : 0;
            control.IsVisible = true;
            var width = (control.Parent as Layoutable)?.Bounds.Width ?? control.Bounds.Width;
            control.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
            from = startHeight;
            to = control.DesiredSize.Height;
            control.MaxHeight = double.PositiveInfinity;
            control.Opacity = 1;
        }
        else
        {
            from = control.Bounds.Height;
            to = 0;
            control.MaxHeight = 0;
            control.Opacity = 0;
        }

        control.ClipToBounds = true;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Easing = Ease,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Layoutable.MaxHeightProperty, from), new Setter(Visual.OpacityProperty, show ? 0d : 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Layoutable.MaxHeightProperty, to), new Setter(Visual.OpacityProperty, show ? 1d : 0d) } },
            }
        };

        try
        {
            await animation.RunAsync(control, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested) return;
        Running.Remove(control);
        if (!show)
        {
            control.IsVisible = false;
            control.MaxHeight = double.PositiveInfinity;
            control.Opacity = 1;
        }
    }
}
