using System;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Protects the delicate 128x40 monochrome OLED display from permanent burn-in.
/// Implements micro pixel-shifting (1-pixel orbital cycle every N minutes)
/// and handles display blanking on workstation lock.
/// </summary>
public sealed class BurnInGuard
{
    private DateTime _lastShiftTime = DateTime.Now;
    private int _shiftIndex = 0;
    public TimeSpan ShiftInterval { get; set; }

    // 4-phase micro-orbital pixel shift coordinates
    private static readonly (int dx, int dy)[] ShiftPattern =
    [
        (0, 0),
        (1, 0),
        (1, 1),
        (0, 1)
    ];

    public bool IsScreenBlanked { get; private set; }
    public (int dx, int dy) CurrentShift { get; private set; } = (0, 0);

    public BurnInGuard(TimeSpan? shiftInterval = null)
    {
        ShiftInterval = shiftInterval ?? TimeSpan.FromMinutes(5);
    }

    public (int dx, int dy) Tick(bool enabled)
    {
        if (!enabled)
        {
            CurrentShift = (0, 0);
            return CurrentShift;
        }

        if (DateTime.Now - _lastShiftTime >= ShiftInterval)
        {
            _lastShiftTime = DateTime.Now;
            _shiftIndex = (_shiftIndex + 1) % ShiftPattern.Length;
            CurrentShift = ShiftPattern[_shiftIndex];
        }

        return CurrentShift;
    }

    public void SetBlanked(bool blanked)
    {
        IsScreenBlanked = blanked;
    }
}
