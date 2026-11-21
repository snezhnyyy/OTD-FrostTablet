using System;
using System.Numerics;
using OpenTabletDriver.Native.Windows.Input;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace OpenTabletDriver.Desktop.Interop.Input.Absolute
{
    public class WindowsAbsolutePointer : WindowsVirtualMouse, IAbsolutePointer
    {
        public WindowsAbsolutePointer()
        {
            var virtualScreen = DesktopInterop.VirtualScreen ??
                                throw new InvalidOperationException("Could not get virtual screen");

            width = Math.Max(1, (int)virtualScreen.Width);
            height = Math.Max(1, (int)virtualScreen.Height);
            lastScreenCheck = Environment.TickCount64;
        }

        private int width, height;
        private long lastScreenCheck;

        // Re-read the virtual desktop size about once a second (cheap user32 call, no allocation), so a game
        // switching resolution or a monitor being added doesn't leave the cursor mapped to stale dimensions.
        private void RefreshScreenSize()
        {
            var now = Environment.TickCount64;
            if (now - lastScreenCheck < 1000)
                return;
            lastScreenCheck = now;
            int w = Native.Windows.Windows.GetSystemMetrics(Native.Windows.Windows.SM_CXVIRTUALSCREEN);
            int h = Native.Windows.Windows.GetSystemMetrics(Native.Windows.Windows.SM_CYVIRTUALSCREEN);
            if (w > 0 && h > 0)
            {
                width = w;
                height = h;
            }
        }

        public void SetPosition(Vector2 pos)
        {
            SetDirty();
            RefreshScreenSize();

            inputs[0].U.mi.dwFlags |= MOUSEEVENTF.ABSOLUTE | MOUSEEVENTF.MOVE | MOUSEEVENTF.VIRTUALDESK;
            inputs[0].U.mi.dx = ToNormalized(pos.X, width);
            inputs[0].U.mi.dy = ToNormalized(pos.Y, height);
        }

        /// <summary>
        /// Converts a virtual desktop pixel coordinate to Windows' normalized 0..65535 absolute coordinate.
        /// </summary>
        /// <remarks>
        /// Windows maps a normalized value back to pixel <c>floor(n * size / 65536)</c>. The previous
        /// <c>(int)(pos * 65535 / size)</c> conversion landed one pixel left/up of the target for ~8% of positions.
        /// Here the integer pixel is always exact, and the sub-pixel fraction is spread across the normalized
        /// range belonging to that pixel, so raw-input consumers (e.g. osu!lazer) keep sub-pixel precision.
        /// </remarks>
        public static int ToNormalized(float pos, int size)
        {
            if (float.IsNaN(pos))
                return 0;

            float floor = MathF.Floor(Math.Clamp(pos, -1f, size));
            long pixel = (long)floor;
            float frac = pos - floor;
            if (frac is < 0 or >= 1)
                frac = 0;

            long lo = CeilDiv(pixel * 65536, size);
            long hi = CeilDiv((pixel + 1) * 65536, size) - 1;
            long n = lo + (long)(frac * (hi - lo + 1));
            return (int)Math.Clamp(Math.Min(n, hi), 0, 65535);
        }

        private static long CeilDiv(long a, long b) => a >= 0 ? (a + b - 1) / b : -(-a / b);
    }
}
