using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace KcpTransport
{
    internal static class StopwatchPolyfiil
    {
        internal static TimeSpan GetElapsedTime(long startingTimestamp)
        {
#if NET7_0_OR_GREATER
            return Stopwatch.GetElapsedTime(startingTimestamp);
#else
            return GetElapsedTime(startingTimestamp, Stopwatch.GetTimestamp());
#endif
        }
        internal static TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
        {
#if NET7_0_OR_GREATER
            return Stopwatch.GetElapsedTime(startingTimestamp, endingTimestamp);
#else
            return new TimeSpan((long)(endingTimestamp - startingTimestamp));
#endif
        }
    }
}
