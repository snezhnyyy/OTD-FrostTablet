using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Native.Windows;
using OpenTabletDriver.Native.Windows.Timers;
using OpenTabletDriver.Plugin;
using ITimer = OpenTabletDriver.Plugin.Timers.ITimer;

namespace OpenTabletDriver.Desktop.Interop.Timer
{
    using static OpenTabletDriver.Native.Windows.Windows;

    /// <summary>
    /// Windows timer.
    /// </summary>
    /// <remarks>
    /// Preferred implementation: a dedicated MMCSS-boosted thread sleeping on a high resolution waitable timer
    /// (Windows 10 1803+), finishing the last fraction of the interval with a short adaptive spin.
    /// Deadlines are computed in absolute <see cref="Stopwatch"/> ticks, so fractional intervals
    /// (e.g. 2000hz, 333hz, monitor refresh rates) are exact and never drift.
    /// Falls back to the legacy multimedia timer / <see cref="FallbackTimer"/> when unavailable.
    /// </remarks>
    internal class WindowsTimer : ITimer, IDisposable
    {
        public WindowsTimer()
        {
            callbackDelegate = Callback;
            callbackHandle = GCHandle.Alloc(callbackDelegate);
        }

        private readonly object stateLock = new object();

        // high resolution path
        private Thread? timerThread;
        private IntPtr waitableTimer;
        private volatile bool running;

        // legacy path
        private uint timerId;
        private uint timerPeriod;
        private FallbackTimer? fallbackTimer;
        private readonly TimerCallback callbackDelegate;
        private readonly GCHandle callbackHandle;

        public bool Enabled { private set; get; }
        public float Interval { set; get; } = 1;

        public event Action? Elapsed;

        public void Start()
        {
            lock (stateLock)
            {
                if (Enabled)
                    return;

                if (Interval <= 0)
                    throw new ArgumentOutOfRangeException(nameof(Interval), Interval, "Timer interval must be positive");

                if (TryStartHighResolution() || TryStartMultimedia())
                {
                    Enabled = true;
                    return;
                }

                Log.WriteNotify("Timer", "High resolution timer is unavailable, will use fallback timer. Expect high CPU usage.", LogLevel.Warning);
                fallbackTimer = new FallbackTimer
                {
                    Interval = Interval
                };
                fallbackTimer.Elapsed += () => Elapsed?.Invoke();
                fallbackTimer.Start();
                Enabled = true;
            }
        }

        public void Stop()
        {
            lock (stateLock)
            {
                if (!Enabled)
                    return;

                if (timerThread != null)
                {
                    running = false;
                    if (Thread.CurrentThread != timerThread)
                        timerThread.Join();
                    timerThread = null;

                    CloseHandle(waitableTimer);
                    waitableTimer = IntPtr.Zero;
                }
                else if (fallbackTimer != null)
                {
                    fallbackTimer.Stop();
                    fallbackTimer.Dispose();
                    fallbackTimer = null;
                }
                else
                {
                    _ = timeKillEvent(timerId);
                    _ = timeEndPeriod(timerPeriod);
                }

                Enabled = false;
            }
        }

        #region High resolution waitable timer

        private bool TryStartHighResolution()
        {
            IntPtr handle;
            try
            {
                handle = CreateWaitableTimerEx(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }

            if (handle == IntPtr.Zero)
                return false;

            waitableTimer = handle;
            running = true;
            timerThread = new Thread(HighResolutionLoop)
            {
                Name = "OpenTabletDriver High Resolution Timer",
                IsBackground = true,
                Priority = ThreadPriority.Highest
            };
            timerThread.Start((handle, (double)Interval));
            return true;
        }

        private void HighResolutionLoop(object? args)
        {
            var (handle, intervalMs) = ((IntPtr, double))args!;
            using var mmcss = MmcssThread.Register();

            long freq = Stopwatch.Frequency;
            double intervalTicks = intervalMs * freq / 1000.0;
            long minSpin = freq / 50_000;  // 20us
            long maxSpin = Math.Min(freq / 2_000, (long)(intervalTicks / 4)); // 500us, at most a quarter of an interval
            long spinMargin = Math.Min(freq / 5_000, maxSpin); // initial guess: 200us of wake-up latency, adapted below
            long minSleep = freq / 3_333;  // ~300us: shorter waits can't be met by the OS timer, spin instead
            long lateLimit = (long)(intervalTicks * 2);
            bool exceptionLogged = false;

            long origin = Stopwatch.GetTimestamp();
            long tick = 0;

            while (running)
            {
                tick++;
                long deadline = origin + (long)(tick * intervalTicks);
                long now = Stopwatch.GetTimestamp();

                if (now - deadline > lateLimit)
                {
                    // We fell far behind (system stall, debugger, sleep). Don't burst-fire missed ticks; resync.
                    origin = now;
                    tick = 0;
                    deadline = now;
                }

                long sleepTicks = deadline - now - spinMargin;
                if (sleepTicks >= minSleep)
                {
                    long due = -(sleepTicks * 10_000_000 / freq); // relative, 100ns units
                    if (due < 0 && SetWaitableTimer(handle, in due, 0, IntPtr.Zero, IntPtr.Zero, false))
                    {
                        _ = WaitForSingleObject(handle, INFINITE);

                        // Track the wake-up overshoot peak: grow immediately on a late wake-up, decay slowly,
                        // so ticks are rarely late while spinning as little as possible.
                        long overshoot = Stopwatch.GetTimestamp() - (now + sleepTicks) + minSpin;
                        spinMargin = overshoot > spinMargin
                            ? Math.Min(overshoot, maxSpin)
                            : Math.Max(spinMargin - (spinMargin >> 7), minSpin);
                    }
                }

                while (Stopwatch.GetTimestamp() < deadline)
                    Thread.SpinWait(16);

                if (!running)
                    break;

                try
                {
                    Elapsed?.Invoke();
                }
                catch (Exception ex)
                {
                    if (!exceptionLogged)
                    {
                        Log.Exception(ex);
                        exceptionLogged = true;
                    }
                }
            }
        }

        #endregion

        #region Legacy multimedia timer

        private unsafe bool TryStartMultimedia()
        {
            if (Interval != (int)Interval)
                return false;

            var caps = new TimeCaps();
            _ = timeGetDevCaps(ref caps, (uint)sizeof(TimeCaps));
            var clampedInterval = Math.Clamp((uint)Interval, caps.wPeriodMin, caps.wPeriodMax);
            timerPeriod = Math.Max(1, caps.wPeriodMin);
            _ = timeBeginPeriod(timerPeriod);
            timerId = timeSetEvent(clampedInterval, 1, callbackDelegate, IntPtr.Zero, EventType.TIME_PERIODIC | EventType.TIME_KILL_SYNCHRONOUS);
            if (timerId == 0)
            {
                _ = timeEndPeriod(timerPeriod);
                return false;
            }

            return true;
        }

        private void Callback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2)
        {
            Elapsed?.Invoke();
        }

        #endregion

        public void Dispose()
        {
            if (Enabled)
                Stop();
            callbackHandle.Free();
            GC.SuppressFinalize(this);
        }
    }
}
