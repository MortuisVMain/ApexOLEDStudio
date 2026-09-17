using System;
using System.Collections.Generic;

namespace ApexOLEDStudio.Core.Drawing;

/// <summary>
/// High-performance monochrome framebuffer for 128x40 OLED screens (SteelSeries Apex Pro / 7 / 5).
/// Supports drawing primitives, graphs, progress bars, analog gauges, and text.
/// Encodes directly to row-major MSB 640-byte buffer and SteelSeries HID packets.
/// </summary>
public sealed class OledFrameBuffer
{
    public const int Width = 128;
    public const int Height = 40;
    public const int RawByteCount = (Width * Height) / 8; // 640 bytes

    private readonly bool[,] _pixels = new bool[Width, Height];

    public void Clear(bool on = false)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _pixels[x, y] = on;
            }
        }
    }

    public void Invert()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _pixels[x, y] = !_pixels[x, y];
            }
        }
    }

    public void SetPixel(int x, int y, bool on)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            _pixels[x, y] = on;
        }
    }

    public bool GetPixel(int x, int y)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            return _pixels[x, y];
        }
        return false;
    }

    public void DrawLine(int x0, int y0, int x1, int y1, bool on = true)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            SetPixel(x0, y0, on);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    public void DrawRectangle(int x, int y, int w, int h, bool fill = false, bool on = true)
    {
        if (w <= 0 || h <= 0) return;

        if (fill)
        {
            for (int r = y; r < y + h; r++)
            {
                for (int c = x; c < x + w; c++)
                {
                    SetPixel(c, r, on);
                }
            }
        }
        else
        {
            for (int c = x; c < x + w; c++)
            {
                SetPixel(c, y, on);
                SetPixel(c, y + h - 1, on);
            }
            for (int r = y; r < y + h; r++)
            {
                SetPixel(x, r, on);
                SetPixel(x + w - 1, r, on);
            }
        }
    }

    public void DrawProgressBar(int x, int y, int w, int h, float percent, bool bordered = true, bool on = true)
    {
        if (w <= 2 || h <= 2) return;

        float clamped = Math.Clamp(percent, 0f, 100f);

        if (bordered)
        {
            DrawRectangle(x, y, w, h, fill: false, on: on);
            int innerW = w - 2;
            int innerH = h - 2;
            int fillW = (int)Math.Round((clamped / 100f) * innerW);

            for (int r = y + 1; r < y + 1 + innerH; r++)
            {
                for (int c = x + 1; c < x + 1 + fillW; c++)
                {
                    SetPixel(c, r, on);
                }
            }
        }
        else
        {
            int fillW = (int)Math.Round((clamped / 100f) * w);
            DrawRectangle(x, y, fillW, h, fill: true, on: on);
        }
    }

    public void DrawGraph(int x, int y, int w, int h, IReadOnlyList<float> history, float min = 0, float max = 100, bool on = true)
    {
        if (w <= 0 || h <= 0 || history == null || history.Count == 0) return;

        // Draw baseline
        DrawLine(x, y + h - 1, x + w - 1, y + h - 1, on);

        float range = max - min;
        if (range <= 0) range = 1;

        int count = Math.Min(w, history.Count);
        int startX = x + w - count;

        for (int i = 0; i < count; i++)
        {
            float val = Math.Clamp(history[history.Count - count + i], min, max);
            float normalized = (val - min) / range;
            int barHeight = (int)Math.Round(normalized * (h - 1));

            int curX = startX + i;
            for (int dy = 0; dy < barHeight; dy++)
            {
                SetPixel(curX, y + h - 1 - dy, on);
            }
        }
    }

    public void DrawGauge(int centerX, int centerY, int radius, float percent, bool on = true)
    {
        if (radius <= 2) return;

        // Draw semicircular top arc (180 degrees from PI to 0)
        for (int a = 180; a <= 360; a += 4)
        {
            double rad = a * Math.PI / 180.0;
            int px = (int)Math.Round(centerX + radius * Math.Cos(rad));
            int py = (int)Math.Round(centerY + radius * Math.Sin(rad));
            SetPixel(px, py, on);
        }

        // Needle angle: 0% = 180 deg (left), 100% = 360 deg (right)
        float clamped = Math.Clamp(percent, 0f, 100f);
        double needleAngle = (180.0 + (clamped / 100.0) * 180.0) * Math.PI / 180.0;
        int needleLength = radius - 1;
        int tipX = (int)Math.Round(centerX + needleLength * Math.Cos(needleAngle));
        int tipY = (int)Math.Round(centerY + needleLength * Math.Sin(needleAngle));
        DrawLine(centerX, centerY, tipX, tipY, on);
    }

    public int DrawText(int x, int y, string text, bool compact = false, int scale = 1, bool on = true)
    {
        if (string.IsNullOrEmpty(text)) return x;

        int curX = x;
        var font = compact ? OledFonts.Font3x5 : OledFonts.Font5x7;
        int charW = compact ? 3 : 5;
        int spacing = 1;

        foreach (char ch in text)
        {
            if (ch == ' ')
            {
                curX += (charW + spacing) * scale;
                continue;
            }

            if (!font.TryGetValue(ch, out var columns))
            {
                // Fallback to uppercase or question mark
                if (!font.TryGetValue(char.ToUpperInvariant(ch), out columns))
                {
                    columns = font.TryGetValue('?', out var qm) ? qm : [0x00, 0x00, 0x00, 0x00, 0x00];
                }
            }

            for (int col = 0; col < columns.Length; col++)
            {
                byte colByte = columns[col];
                for (int row = 0; row < (compact ? 5 : 7); row++)
                {
                    bool bitOn = (colByte & (1 << row)) != 0;
                    if (bitOn)
                    {
                        if (scale == 1)
                        {
                            SetPixel(curX + col, y + row, on);
                        }
                        else
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                for (int sy = 0; sy < scale; sy++)
                                {
                                    SetPixel(curX + col * scale + sx, y + row * scale + sy, on);
                                }
                            }
                        }
                    }
                }
            }

            curX += (charW + spacing) * scale;
        }

        return curX;
    }

    /// <summary>
    /// Encodes the 128x40 display into 640 raw row-major MSB bytes.
    /// Format: row by row (y=0..39), 16 bytes per row, MSB first (bit 7 = leftmost pixel).
    /// </summary>
    public byte[] ToRawApexBytes()
    {
        byte[] buffer = new byte[RawByteCount];

        for (int y = 0; y < Height; y++)
        {
            int rowOffset = y * (Width / 8);
            for (int x = 0; x < Width; x++)
            {
                if (_pixels[x, y])
                {
                    int byteIdx = rowOffset + (x / 8);
                    int bitIdx = 7 - (x % 8); // MSB
                    buffer[byteIdx] |= (byte)(1 << bitIdx);
                }
            }
        }

        return buffer;
    }

    /// <summary>
    /// Builds the exact 643-byte HID Feature Report packet for SteelSeries Apex Pro / 7 / 5 on Windows.
    /// Format: [0x00 ReportID] + [0x61 CMD] + [640 bytes pixelData] + [0x00 padding].
    /// </summary>
    public byte[] ToApexHidPacket()
    {
        byte[] packet = new byte[643];
        packet[0] = 0x00; // Windows HID Report ID
        packet[1] = 0x61; // SteelSeries Apex OLED Frame Command

        byte[] raw = ToRawApexBytes();
        Buffer.BlockCopy(raw, 0, packet, 2, raw.Length);
        packet[642] = 0x00; // 1-byte padding

        return packet;
    }

    /// <summary>
    /// Exports as 32-bit BGRA pixel array (128x40x4 bytes) for WPF WriteableBitmap display in the UI.
    /// </summary>
    public byte[] ToBgra32(uint onColor = 0xFF00E5FF, uint offColor = 0xFF0B0E14)
    {
        byte[] bgra = new byte[Width * Height * 4];
        byte onB = (byte)(onColor & 0xFF);
        byte onG = (byte)((onColor >> 8) & 0xFF);
        byte onR = (byte)((onColor >> 16) & 0xFF);
        byte onA = (byte)((onColor >> 24) & 0xFF);

        byte offB = (byte)(offColor & 0xFF);
        byte offG = (byte)((offColor >> 8) & 0xFF);
        byte offR = (byte)((offColor >> 16) & 0xFF);
        byte offA = (byte)((offColor >> 24) & 0xFF);

        int idx = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool isPixelOn = _pixels[x, y];
                bgra[idx++] = isPixelOn ? onB : offB;
                bgra[idx++] = isPixelOn ? onG : offG;
                bgra[idx++] = isPixelOn ? onR : offR;
                bgra[idx++] = isPixelOn ? onA : offA;
            }
        }

        return bgra;
    }
}
