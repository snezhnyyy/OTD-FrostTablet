using System;
using HidSharp;
using OpenTabletDriver.Plugin.Devices;

namespace OpenTabletDriver.Devices.HidSharpBackend
{
    public sealed class HidSharpEndpointStream : IDeviceEndpointStream
    {
        internal HidSharpEndpointStream(HidStream stream)
        {
            this.stream = stream;
            stream.ReadTimeout = int.MaxValue;

            int length = 0;
            try { length = stream.Device.GetMaxInputReportLength(); }
            catch { /* fall back to allocating reads */ }

            if (length > 0)
            {
                ring = new byte[RingSize][];
                for (int i = 0; i < RingSize; i++)
                    ring[i] = new byte[length];
            }
        }

        // Reports are parsed into objects that keep a reference to their raw buffer (Raw). Rotating through a
        // ring of buffers avoids a heap allocation per report while keeping each buffer untouched for the next
        // RingSize - 1 reads, so anything that briefly holds on to a report still sees its own bytes.
        private const int RingSize = 64;
        private readonly byte[][]? ring;
        private int ringIndex;

        private HidStream stream;

        public byte[] Read()
        {
            if (ring == null)
                return stream.Read();

            var buffer = ring[ringIndex];
            ringIndex = (ringIndex + 1) & (RingSize - 1);
            int count = stream.Read(buffer, 0, buffer.Length);
            return count == buffer.Length ? buffer : buffer.AsSpan(0, count).ToArray();
        }
        public void Write(byte[] buffer) => stream.Write(buffer);

        public void GetFeature(byte[] buffer) => stream.GetFeature(buffer);
        public void SetFeature(byte[] buffer) => stream.SetFeature(buffer);

        public void Dispose() => stream.Dispose();
    }
}
