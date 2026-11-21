using System;
using System.Diagnostics;
using System.Threading;

using ITimer = OpenTabletDriver.Plugin.Timers.ITimer;

namespace OpenTabletDriver.Desktop.Interop.Timer
{
    internal class FallbackTimer : ITimer, IDisposable
    {
        private Thread? threadTimer;
        private bool runTimer = true;

        public float Interval { get; set; }
        public float IgnoreEventIfLateBy { get; set; } = float.MaxValue;
        public bool Enabled => this.threadTimer != null && this.threadTimer.IsAlive;

        public event Action? Elapsed;

        public void Start()
        {
            if (Enabled || Interval <= 0)
                return;

            this.runTimer = true;

            this.IgnoreEventIfLateBy = Interval * 2;

            this.threadTimer = new Thread(ThreadMain)
            {
                Priority = ThreadPriority.AboveNormal,
                IsBackground = true,
            };
            this.threadTimer.Start();
        }

        public void Stop()
        {
            this.runTimer = false;
            this.threadTimer?.Join();
        }

        private void ThreadMain()
        {
            // Absolute deadlines in Stopwatch ticks (64-bit): a float millisecond accumulator loses precision
            // after a few hours of uptime, making the timer drift or fire continuously.
            long freq = Stopwatch.Frequency;
            double intervalTicks = Interval * freq / 1000.0;
            double lateLimitTicks = IgnoreEventIfLateBy * freq / 1000.0;
            long origin = Stopwatch.GetTimestamp();
            long tick = 0;

            while (this.runTimer)
            {
                tick++;
                long deadline = origin + (long)(tick * intervalTicks);

                long now;
                while ((now = Stopwatch.GetTimestamp()) < deadline)
                {
                    Thread.Yield();
                }

                if (now - deadline >= lateLimitTicks)
                    continue;
                Elapsed?.Invoke();
            }
        }

        public void Dispose()
        {
            if (Enabled)
                Stop();
        }
    }
}
