using System;
using System.Diagnostics;

namespace KcpTransport.Fallbacks
{
    public static class StopwatchFallback
    {
        private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;

        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        public static TimeSpan GetElapsedTime(long startingTimestamp)
        {
#if NET7_0_OR_GREATER
            return Stopwatch.GetElapsedTime(startingTimestamp);
#else
            return GetElapsedTime(startingTimestamp, GetTimestamp());
#endif
        }

        public static TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
#if NET7_0_OR_GREATER
            return Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
#else
            long delta = endingTimestamp - startingTimestamp;
            long ticks = (long)(delta * s_tickFrequency);
            return new TimeSpan(ticks);
#endif
        }
    }
}
