using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ApexOLEDStudio.Core.Services;

public interface IKeyboardTrackerService : IDisposable
{
    int CurrentApm { get; }
    int TotalKeystrokes { get; }
    void Start();
    void Stop();
    void RecordKeystroke(); // For testing or simulated inputs
}

public sealed class KeyboardTrackerService : IKeyboardTrackerService
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private readonly int[] _secondBuckets = new int[60];
    private int _currentBucketIndex = 0;
    private int _totalKeystrokes = 0;
    private int _currentApm = 0;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private Timer? _rollingTimer;
    private bool _disposed;

    public int CurrentApm => _currentApm;
    public int TotalKeystrokes => _totalKeystrokes;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr modHandle = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;

        try
        {
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, modHandle, 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KeyboardTracker] Failed to install hook: {ex.Message}");
        }

        // Rolling 1-second timer to advance buckets and compute APM
        _rollingTimer = new Timer(OnRollingTick, null, 1000, 1000);
    }

    public void RecordKeystroke()
    {
        Interlocked.Increment(ref _totalKeystrokes);
        int idx = _currentBucketIndex % 60;
        Interlocked.Increment(ref _secondBuckets[idx]);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            RecordKeystroke();
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void OnRollingTick(object? state)
    {
        // 1. Calculate sum across all 60 second buckets = APM
        int sum = 0;
        for (int i = 0; i < 60; i++)
        {
            sum += Volatile.Read(ref _secondBuckets[i]);
        }
        _currentApm = sum;

        // 2. Advance to next second bucket and clear it
        _currentBucketIndex = (_currentBucketIndex + 1) % 60;
        Volatile.Write(ref _secondBuckets[_currentBucketIndex], 0);
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        _rollingTimer?.Dispose();
        _rollingTimer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
