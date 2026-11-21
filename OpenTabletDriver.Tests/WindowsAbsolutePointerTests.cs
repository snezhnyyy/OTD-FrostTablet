using System;
using OpenTabletDriver.Desktop.Interop.Input.Absolute;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class WindowsAbsolutePointerTests
    {
        // Windows converts normalized absolute coordinates back to pixels as floor(n * size / 65536)
        private static long ToPixel(int normalized, int size) => (long)normalized * size / 65536;

        [Theory]
        [InlineData(1920)]
        [InlineData(1080)]
        [InlineData(2560)]
        [InlineData(3000)]
        [InlineData(3840)]
        [InlineData(5760)]
        [InlineData(7680)]
        [InlineData(1366)]
        public void ToNormalized_LandsOnExactPixel(int size)
        {
            var rnd = new Random(size);
            for (int i = 0; i < 20000; i++)
            {
                float pos = (float)(rnd.NextDouble() * size);
                int n = WindowsAbsolutePointer.ToNormalized(pos, size);
                Assert.InRange(n, 0, 65535);
                Assert.Equal((long)MathF.Floor(pos), ToPixel(n, size));
            }

            for (int px = 0; px < size; px++)
                Assert.Equal(px, ToPixel(WindowsAbsolutePointer.ToNormalized(px, size), size));
        }

        [Fact]
        public void ToNormalized_IsMonotonicAndKeepsSubPixel()
        {
            const int size = 1920;
            int prev = -1;
            int distinct = 0;
            for (float pos = 100f; pos < 101f; pos += 1f / 64)
            {
                int n = WindowsAbsolutePointer.ToNormalized(pos, size);
                Assert.True(n >= prev);
                if (n != prev) distinct++;
                prev = n;
            }

            // ~34 normalized units per pixel at 1920px, sub-pixel movement must not be quantized away
            Assert.True(distinct > 20);
        }

        [Theory]
        [InlineData(-50f, 0)]
        [InlineData(-0.5f, 0)]
        [InlineData(1920f, 65535)]
        [InlineData(99999f, 65535)]
        [InlineData(float.NaN, 0)]
        public void ToNormalized_ClampsOutOfRange(float pos, int expected)
        {
            Assert.Equal(expected, WindowsAbsolutePointer.ToNormalized(pos, 1920));
        }
    }
}
