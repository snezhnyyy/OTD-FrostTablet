using System;
using System.Diagnostics;
using System.Threading;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.Devices
{
    /// <summary>
    /// Allocation-free, lock-free (single writer) measurement of the input pipeline:
    /// time from a report being read to the end of its processing (filters, bindings, OS injection),
    /// and the real report rate / interval jitter of the device.
    /// A summary is logged once a minute from a thread pool thread.
    /// </summary>
    internal sealed class PipelineStats
    {
        private const int LatencyBuckets = 4096;   // 1 µs per bucket, 0..4 ms
        private const int IntervalBuckets = 4096;  // 10 µs per bucket, 0..41 ms
        private static readonly double TicksPerMicrosecond = Stopwatch.Frequency / 1_000_000.0;
        private static readonly long LogPeriod = Stopwatch.Frequency * 60;

        private readonly string name;
        private int[] latency = new int[LatencyBuckets];
        private int[] interval = new int[IntervalBuckets];
        private long count, maxLatencyTicks, lastRead, windowStart;

        public PipelineStats(string name)
        {
            this.name = name;
        }

        /// <param name="readTimestamp">Stopwatch timestamp taken right after the report was read.</param>
        /// <param name="doneTimestamp">Stopwatch timestamp taken after the report was fully processed.</param>
        public void Record(long readTimestamp, long doneTimestamp)
        {
            if (windowStart == 0)
                windowStart = readTimestamp;

            var processing = doneTimestamp - readTimestamp;
            latency[Math.Clamp((int)(processing / TicksPerMicrosecond), 0, LatencyBuckets - 1)]++;
            if (processing > maxLatencyTicks)
                maxLatencyTicks = processing;

            if (lastRead != 0)
                interval[Math.Clamp((int)((readTimestamp - lastRead) / (TicksPerMicrosecond * 10)), 0, IntervalBuckets - 1)]++;
            lastRead = readTimestamp;
            count++;

            if (doneTimestamp - windowStart >= LogPeriod)
                Flush(doneTimestamp);
        }

        private void Flush(long now)
        {
            var latencySnapshot = latency;
            var intervalSnapshot = interval;
            var reports = count;
            var seconds = (now - windowStart) / (double)Stopwatch.Frequency;
            var max = maxLatencyTicks / TicksPerMicrosecond;

            // swap in fresh buckets; the old ones are only read by the logger from here on
            latency = new int[LatencyBuckets];
            interval = new int[IntervalBuckets];
            count = 0;
            maxLatencyTicks = 0;
            windowStart = now;

            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                if (reports < 2)
                    return;
                var p50 = Percentile(latencySnapshot, reports, 0.50);
                var p99 = Percentile(latencySnapshot, reports, 0.99);
                var iP50 = Percentile(intervalSnapshot, reports - 1, 0.50) * 10;
                var iP99 = Percentile(intervalSnapshot, reports - 1, 0.99) * 10;
                Log.Write("Performance",
                    $"{name}: {reports / seconds:0} reports/s, interval p50 {iP50} µs / p99 {iP99} µs, " +
                    $"pipeline p50 {p50} µs / p99 {p99} µs / max {max:0} µs");
            }, null);
        }

        private static int Percentile(int[] histogram, long total, double q)
        {
            var target = (long)Math.Ceiling(total * q);
            long seen = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                seen += histogram[i];
                if (seen >= target)
                    return i;
            }
            return histogram.Length - 1;
        }
    }
}
