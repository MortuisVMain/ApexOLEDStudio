using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Decodes animated .gif files into 1-bit monochome frames for OLED rendering.
/// Includes built-in retro pixel-art animations (Bongo Cat / Audio Waves) when no file is chosen.
/// </summary>
public sealed class GifAnimationService
{
    private static readonly ConcurrentDictionary<string, List<bool[,]>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads a GIF file and returns a list of 1-bit frames dithered/scaled to target dimensions.
    /// </summary>
    public static List<bool[,]> LoadGifFrames(string? filePath, int width, int height)
    {
        width = Math.Clamp(width, 4, 128);
        height = Math.Clamp(height, 4, 40);

        long lastWriteTicks = !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)
            ? File.GetLastWriteTimeUtc(filePath).Ticks
            : 0;
        string cacheKey = $"{filePath}_{lastWriteTicks}_{width}_{height}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            var defaultFrames = GenerateDefaultMascotFrames(width, height);
            _cache[cacheKey] = defaultFrames;
            return defaultFrames;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frames = new List<bool[,]>();

            foreach (var rawFrame in decoder.Frames)
            {
                var converted = new FormatConvertedBitmap(rawFrame, PixelFormats.Bgra32, null, 0);
                var scaled = new TransformedBitmap(converted, new ScaleTransform(
                    (double)width / converted.PixelWidth,
                    (double)height / converted.PixelHeight));
                scaled.Freeze();

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                scaled.CopyPixels(pixels, stride, 0);

                var matrix = new bool[width, height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (y * stride) + (x * 4);
                        byte b = pixels[idx];
                        byte g = pixels[idx + 1];
                        byte r = pixels[idx + 2];
                        byte a = pixels[idx + 3];

                        if (a > 32)
                        {
                            int luminance = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                            matrix[x, y] = luminance > 120; // 1-bit threshold
                        }
                    }
                }
                frames.Add(matrix);
            }

            if (frames.Count > 0)
            {
                _cache[cacheKey] = frames;
                return frames;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GIF] Could not decode '{filePath}': {ex.Message}");
        }

        var fallback = GenerateDefaultMascotFrames(width, height);
        _cache[cacheKey] = fallback;
        return fallback;
    }

    public static void ClearCache() => _cache.Clear();

    /// <summary>
    /// Generates a cute 4-frame retro Bongo Cat / Wave animation.
    /// </summary>
    public static List<bool[,]> GenerateDefaultMascotFrames(int width, int height)
    {
        var list = new List<bool[,]>();

        // 4 frames of alternating paws/pulse
        for (int frame = 0; frame < 4; frame++)
        {
            var m = new bool[width, height];
            int cx = width / 2;
            int cy = height / 2;

            // Cat / Mascot Head
            int headW = Math.Min(width - 2, 20);
            int headH = Math.Min(height - 2, 14);
            int startX = Math.Max(0, cx - headW / 2);
            int startY = Math.Max(0, cy - headH / 2);

            // Draw head outline
            for (int x = 0; x < headW; x++)
            {
                if (startX + x < width && startY < height) m[startX + x, startY] = true;
                if (startX + x < width && startY + headH - 1 < height) m[startX + x, startY + headH - 1] = true;
            }
            for (int y = 0; y < headH; y++)
            {
                if (startX < width && startY + y < height) m[startX, startY + y] = true;
                if (startX + headW - 1 < width && startY + y < height) m[startX + headW - 1, startY + y] = true;
            }

            // Ears
            if (startX + 2 < width && startY - 2 >= 0) m[startX + 2, startY - 1] = true;
            if (startX + headW - 3 < width && startY - 2 >= 0) m[startX + headW - 3, startY - 1] = true;

            // Eyes (^ _ ^)
            if (startX + 5 < width && startY + 4 < height) m[startX + 5, startY + 4] = true;
            if (startX + headW - 6 < width && startY + 4 < height) m[startX + headW - 6, startY + 4] = true;

            // Animated Bongo Paws (Frame 0: Left paw down, Frame 2: Right paw down)
            if (frame % 2 == 0)
            {
                // Left paw tapping
                for (int px = 0; px < 4; px++)
                    if (startX + 2 + px < width && startY + headH < height)
                        m[startX + 2 + px, startY + headH] = true;
            }
            else
            {
                // Right paw tapping
                for (int px = 0; px < 4; px++)
                    if (startX + headW - 6 + px < width && startY + headH < height)
                        m[startX + headW - 6 + px, startY + headH] = true;
            }

            list.Add(m);
        }

        return list;
    }
}
