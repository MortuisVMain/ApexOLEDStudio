using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Monitors the active foreground application process in Windows.
/// Used for auto-switching OLED presets based on games or media apps.
/// </summary>
public sealed class AppWatcherService : IDisposable
{
    private CancellationTokenSource? _cts;
    private string _lastProcessName = string.Empty;

    public string CurrentProcessName { get; private set; } = string.Empty;

    /// <summary>Fired when the user switches to a different foreground application.</summary>
    public event Action<string>? ForegroundProcessChanged;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollForegroundProcess();
                    await Task.Delay(1000, token); // 1s check is plenty fast and zero CPU
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Foreground window may be transitioning
                }
            }
        }, token);
    }

    private void PollForegroundProcess()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName.ToLowerInvariant();

            if (name != _lastProcessName)
            {
                _lastProcessName = name;
                CurrentProcessName = name;
                ForegroundProcessChanged?.Invoke(name);
            }
        }
        catch
        {
            // Process may have exited or is privileged system process
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
