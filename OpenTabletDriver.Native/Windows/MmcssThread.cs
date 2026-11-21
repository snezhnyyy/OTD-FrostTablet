using System;

namespace OpenTabletDriver.Native.Windows
{
    /// <summary>
    /// Registers the calling thread with the Multimedia Class Scheduler Service (MMCSS),
    /// which gives latency-sensitive threads a scheduling boost over normal desktop work.
    /// </summary>
    public readonly struct MmcssThread : IDisposable
    {
        private readonly IntPtr handle;

        private MmcssThread(IntPtr handle) => this.handle = handle;

        public bool IsRegistered => handle != IntPtr.Zero;

        /// <summary>
        /// Registers the current thread. Never throws; returns an unregistered scope on failure or non-Windows.
        /// </summary>
        public static MmcssThread Register(string taskName = "Games")
        {
            if (!OperatingSystem.IsWindows())
                return default;

            try
            {
                uint taskIndex = 0;
                return new MmcssThread(Windows.AvSetMmThreadCharacteristics(taskName, ref taskIndex));
            }
            catch (Exception) // DllNotFoundException / EntryPointNotFoundException on stripped-down systems
            {
                return default;
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
                Windows.AvRevertMmThreadCharacteristics(handle);
        }
    }
}
