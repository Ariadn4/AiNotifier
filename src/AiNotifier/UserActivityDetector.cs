using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AiNotifier;

public class UserActivityDetector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private readonly DispatcherTimer _pollTimer;
    private uint _baselineTick;

    public event Action? UserActivityDetected;

    public UserActivityDetector()
    {
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pollTimer.Tick += OnPollTick;
    }

    public static uint GetLastInputTick()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        GetLastInputInfo(ref info);
        return info.dwTime;
    }

    public void Start(uint baselineTick)
    {
        _baselineTick = baselineTick;
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        var currentTick = GetLastInputTick();
        if (currentTick != _baselineTick)
        {
            Stop();
            UserActivityDetected?.Invoke();
        }
    }
}
