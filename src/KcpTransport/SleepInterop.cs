// https://github.com/Cysharp/LogicLooper/blob/master/src/LogicLooper/Internal/SleepInterop.cs
// needs for the high-resolution timer: https://github.com/Cysharp/LogicLooper/issues/11
// https://github.com/dotnet/runtime/issues/67088

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace KcpTransport;

internal static partial class SleepInterop
{
    private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Sleep(int millisecondsTimeout)
    {
#if NET5_0_OR_GREATER
        if (OperatingSystem.IsWindows())
#else
        if (_isWindows)
#endif
        {
            Win32WaitableTimerSleep.Sleep(millisecondsTimeout);
        }
        else
        {
            Thread.Sleep(millisecondsTimeout);
        }
    }

    private static partial class Win32WaitableTimerSleep
    {
        private const uint CREATE_WAITABLE_TIMER_MANUAL_RESET = 0x00000001;
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;

        [ThreadStatic]
        private static SafeHandle? _timerHandle;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Sleep(int milliseconds)
        {
#if NET5_0_OR_GREATER
            // https://learn.microsoft.com/en-us/dotnet/standard/analyzers/platform-compat-analyzer#assert-the-call-site-with-platform-check
            Debug.Assert(OperatingSystem.IsWindows());
            Debug.Assert(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134)); // Windows 10 version 1803 or newer
#endif
            _timerHandle ??= PInvoke.CreateWaitableTimerEx(null, default(string?), CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, 0x1F0003 /* TIMER_ALL_ACCESS */);
            var result = PInvoke.SetWaitableTimer(_timerHandle, milliseconds * -10000, 0, null, null, false);
            var resultWait = PInvoke.WaitForSingleObject(_timerHandle, 0xffffffff /* Infinite */);
        }
    }
}
