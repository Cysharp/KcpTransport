using System.Diagnostics;

namespace ConsoleApp1;

internal class TimerResolution
{
    internal async Task TestPeriodicTimerAsync()
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        var prev = Stopwatch.GetTimestamp();
        while (await timer.WaitForNextTickAsync())
        {
            var current = Stopwatch.GetTimestamp();
            var timeSpan = Stopwatch.GetElapsedTime(prev, current);
            Console.WriteLine(timeSpan.TotalMilliseconds);
            prev = current;
        }
    }
}
