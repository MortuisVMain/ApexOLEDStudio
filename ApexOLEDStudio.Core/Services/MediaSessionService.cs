using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApexOLEDStudio.Core.Services;

public interface IMediaSessionService : IDisposable
{
    string TrackTitle { get; }
    string Artist { get; }
    string StatusSymbol { get; } // "▶" or "⏸" or ""
    bool IsPlaying { get; }
    void Start();
    void Stop();
}

public sealed class MediaSessionService : IMediaSessionService
{
    private CancellationTokenSource? _cts;

    public string TrackTitle { get; private set; } = string.Empty;
    public string Artist { get; private set; } = string.Empty;
    public string StatusSymbol { get; private set; } = string.Empty;
    public bool IsPlaying { get; private set; } = false;

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
                    PollActiveMedia();
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MediaSession] Poll error: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        }, token);
    }

    private void PollActiveMedia()
    {
        string? foundTitle = null;
        string? foundArtist = null;
        bool playing = false;

        // 1. Inspect Spotify window title
        // When playing: "Artist - Song Name"
        // When paused: "Spotify" or "Spotify Premium" / "Spotify Free"
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            GetWindowThreadProcessId(hWnd, out uint pid);
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string procName = proc.ProcessName.ToLowerInvariant();

                if (procName == "spotify")
                {
                    if (!title.Equals("Spotify", StringComparison.OrdinalIgnoreCase) &&
                        !title.StartsWith("Spotify ", StringComparison.OrdinalIgnoreCase) &&
                        title.Contains(" - "))
                    {
                        int dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
                        foundArtist = title.Substring(0, dashIdx).Trim();
                        foundTitle  = title.Substring(dashIdx + 3).Trim();
                        playing = true;
                        return false; // Stop enumeration
                    }
                }
                else if (procName is "chrome" or "msedge" or "firefox")
                {
                    if (title.Contains(" - YouTube", StringComparison.OrdinalIgnoreCase))
                    {
                        string clean = title.Replace(" - YouTube", "", StringComparison.OrdinalIgnoreCase);
                        clean = clean.Replace(" - Google Chrome", "", StringComparison.OrdinalIgnoreCase);
                        clean = clean.Replace(" - Microsoft​ Edge", "", StringComparison.OrdinalIgnoreCase);
                        clean = clean.Trim();

                        if (clean.Contains(" - "))
                        {
                            int dashIdx = clean.IndexOf(" - ", StringComparison.Ordinal);
                            foundArtist = clean.Substring(0, dashIdx).Trim();
                            foundTitle  = clean.Substring(dashIdx + 3).Trim();
                        }
                        else
                        {
                            foundTitle  = clean;
                            foundArtist = "YouTube";
                        }
                        playing = true;
                        return false;
                    }
                }
                else if (procName == "vlc")
                {
                    if (title.Contains(" - VLC media player", StringComparison.OrdinalIgnoreCase))
                    {
                        string clean = title.Replace(" - VLC media player", "", StringComparison.OrdinalIgnoreCase).Trim();
                        foundTitle = clean;
                        foundArtist = "VLC";
                        playing = true;
                        return false;
                    }
                }
            }
            catch
            {
                // Process may have exited or access denied
            }

            return true;
        }, IntPtr.Zero);

        if (playing && !string.IsNullOrEmpty(foundTitle))
        {
            TrackTitle   = foundTitle;
            Artist       = foundArtist ?? string.Empty;
            IsPlaying    = true;
            StatusSymbol = "▶";
        }
        else
        {
            TrackTitle   = string.Empty;
            Artist       = string.Empty;
            IsPlaying    = false;
            StatusSymbol = string.Empty;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
