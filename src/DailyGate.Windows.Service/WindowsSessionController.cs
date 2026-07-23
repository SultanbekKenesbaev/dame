using System.Runtime.InteropServices;

namespace DailyGate.Windows.Service;

public sealed class WindowsSessionController(ILogger<WindowsSessionController> logger)
{
    private const uint NoSession = 0xffffffff;

    public bool ForceLogoffActiveSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == NoSession) return false;
        var result = WTSLogoffSession(IntPtr.Zero, sessionId, false);
        if (!result) logger.LogError("WTSLogoffSession failed with Win32 error {Error}.", Marshal.GetLastWin32Error());
        return result;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(IntPtr serverHandle, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);
}
