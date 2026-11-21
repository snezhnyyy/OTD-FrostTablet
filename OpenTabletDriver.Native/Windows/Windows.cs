using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using OpenTabletDriver.Native.Windows.Input;
using OpenTabletDriver.Native.Windows.Timers;

namespace OpenTabletDriver.Native.Windows
{
    public static class Windows
    {
        #region Display

        public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hmon, ref MonitorInfoEx mi);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

        [DllImport("Shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmon, DpiType dpiType, out uint dpiX, out uint dpiY);

        [DllImport("Shcore.dll")]
        public static extern int SetProcessDpiAwareness(int awareness);

        #endregion

        #region Input

        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        #endregion

        #region Timers

        public delegate void TimerCallback(uint uTimerID, uint uMsg, UIntPtr dwUser, UIntPtr dw1, UIntPtr dw2);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeGetDevCaps(ref TimeCaps timeCaps, uint sizeTimeCaps);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeEndPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeSetEvent(uint msDelay, uint msResolution, TimerCallback handler,
            IntPtr data, EventType eventType);

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern uint timeKillEvent(uint timerEventId);

        public const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
        public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        public const uint TIMER_ALL_ACCESS = 0x001F0003;
        public const uint INFINITE = 0xFFFFFFFF;
        public const uint WAIT_OBJECT_0 = 0x00000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWaitableTimerExW")]
        public static extern IntPtr CreateWaitableTimerEx(IntPtr lpTimerAttributes, string? lpTimerName, uint dwFlags, uint dwDesiredAccess);

        /// <param name="dueTime">Negative = relative time in 100ns units.</param>
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetWaitableTimer(IntPtr hTimer, in long dueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        #endregion

        #region MMCSS

        [DllImport("avrt.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "AvSetMmThreadCharacteristicsW")]
        public static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        public static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        #endregion

        #region Keyboard

        public const uint MAPVK_VK_TO_VSC_EX = 4;

        [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        #endregion

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(
            [MarshalAs(UnmanagedType.LPWStr)] string filename,
            FileAccess access,
            FileShare share,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            FileAttributes flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool SetProcessInformation(IntPtr hProcess, ProcessInformationClass processInformationClass, IntPtr processInformation, int processInformationLength);

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public enum ProcessInformationClass
        {
            ProcessMemoryPriority,
            ProcessMemoryExhaustionInfo,
            ProcessAppMemoryInfo,
            ProcessInPrivateInfo,
            ProcessPowerThrottling,
            ProcessReservedValue1,
            ProcessTelemetryCoverageInfo,
            ProcessProtectionLevelInfo,
            ProcessLeapSecondInfo,
            ProcessMachineTypeInfo,
            ProcessInformationClassMax
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;

            public static PowerThrottlingState Create()
            {
                return new PowerThrottlingState
                {
                    Version = 1,
                };
            }
        }

        public enum PowerThrottlingStateMask
        {
            ExecutionSpeed = 0x1,
            IgnoreTimerResolution = 0x4,
        }
    }
}
