using System;
using System.Runtime.InteropServices;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Queries system lock key states (Caps Lock, Num Lock, Scroll Lock) via Win32 GetKeyState.
/// Negligible CPU overhead (< 0.001 ms).
/// </summary>
public static class LockKeysService
{
    private const int VK_CAPITAL = 0x14; // Caps Lock
    private const int VK_NUMLOCK = 0x90; // Num Lock
    private const int VK_SCROLL  = 0x91; // Scroll Lock

    [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    private static extern short GetKeyState(int keyCode);

    /// <summary>Returns true if Caps Lock is currently toggled on.</summary>
    public static bool IsCapsLockOn => (GetKeyState(VK_CAPITAL) & 0x0001) != 0;

    /// <summary>Returns true if Num Lock is currently toggled on.</summary>
    public static bool IsNumLockOn => (GetKeyState(VK_NUMLOCK) & 0x0001) != 0;

    /// <summary>Returns true if Scroll Lock is currently toggled on.</summary>
    public static bool IsScrollLockOn => (GetKeyState(VK_SCROLL) & 0x0001) != 0;
}
