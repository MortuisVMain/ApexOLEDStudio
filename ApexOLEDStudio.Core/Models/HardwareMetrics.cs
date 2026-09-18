using System;
using System.Collections.Generic;

namespace ApexOLEDStudio.Core.Models;

/// <summary>
/// Snapshot of real-time hardware telemetry collected by LibreHardwareMonitorLib.
/// </summary>
public sealed class HardwareMetrics
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // CPU Metrics
    public float CpuLoad { get; set; } // %
    public float CpuTemp { get; set; } // °C
    public float CpuPackageTemp { get; set; } // °C
    public float CpuPower { get; set; } // Watts
    public float CpuClock { get; set; } // MHz
    public float CpuVoltage { get; set; } // mV
    public float CpuFanRpm { get; set; } // RPM
    public float MidFanRpm { get; set; } // RPM

    // GPU Metrics
    public string GpuName { get; set; } = "GPU";
    public float GpuLoad { get; set; } // %
    public float GpuTemp { get; set; } // Core °C
    public float GpuHotspot { get; set; } // Hotspot °C
    public float GpuVramTemp { get; set; } // VRAM °C
    public float GpuPower { get; set; } // Watts
    public float GpuClock { get; set; } // Core MHz
    public float GpuMemoryClock { get; set; } // VRAM MHz
    public float GpuVoltage { get; set; } // mV
    public string PowerSource { get; set; } = "Unavailable";
    public bool PowerIsEstimated { get; set; }
    public float GpuFanRpm { get; set; } // RPM or %
    public float GpuVramUsedMb { get; set; } // MB
    public float GpuVramTotalMb { get; set; } // MB

    // RAM Metrics
    public float RamUsedGb { get; set; } // GB
    public float RamTotalGb { get; set; } // GB
    public float RamPercent { get; set; } // %
    public float RamClock { get; set; } = 5600f; // MHz or MT/s

    // ── Network Metrics ────────────────────────────────────────────────
    public int PingMs { get; set; } = 0;
    public float DownloadSpeedKBs { get; set; } = 0f;
    public float UploadSpeedKBs { get; set; } = 0f;

    // ── APM & Keystrokes ──────────────────────────────────────────────
    public int Apm { get; set; } = 0;
    public int KeystrokeCount { get; set; } = 0;

    // ── Windows Media / Now Playing ───────────────────────────────────
    public string MediaTitle { get; set; } = string.Empty;
    public string MediaArtist { get; set; } = string.Empty;
    public string MediaStatus { get; set; } = string.Empty;
    public string MediaTrack => string.IsNullOrWhiteSpace(MediaArtist)
        ? (string.IsNullOrWhiteSpace(MediaTitle) ? "No media" : MediaTitle)
        : $"{MediaArtist} - {MediaTitle}";

    // ── Audio & Volume ────────────────────────────────────────────────
    public float VolumeLevel { get; set; } = 50f; // 0..100%
    public bool  IsVolumeMuted { get; set; } = false;
    public bool  IsMicMuted { get; set; } = false;

    // ── Lock Keys ─────────────────────────────────────────────────────
    public bool IsCapsLock { get; set; } = false;
    public bool IsNumLock { get; set; } = false;
    public bool IsScrollLock { get; set; } = false;

    // ── Weather ───────────────────────────────────────────────────────
    public string WeatherTemp { get; set; } = "";
    public string WeatherCondition { get; set; } = "";

    /// <summary>
    /// Difference between GPU Hotspot and Core temperature (indicates thermal paste health).
    /// </summary>
    public float GpuDelta => (GpuHotspot > 0 && GpuTemp > 0) ? Math.Max(0f, GpuHotspot - GpuTemp) : 0f;

    /// <summary>
    /// Combined total system power (CPU + GPU in Watts).
    /// </summary>
    public float TotalPower => Math.Max(0f, CpuPower) + Math.Max(0f, GpuPower);

    /// <summary>
    /// Evaluates dynamic format tokens such as {cpu_load}, {gpu_temp}, {ping}, {media_title}.
    /// </summary>
    public string FormatTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string netDown = DownloadSpeedKBs >= 1024f
            ? (DownloadSpeedKBs / 1024f).ToString("0.0", inv) + "MB/s"
            : Math.Round(DownloadSpeedKBs).ToString() + "KB/s";

        string netUp = UploadSpeedKBs >= 1024f
            ? (UploadSpeedKBs / 1024f).ToString("0.0", inv) + "MB/s"
            : Math.Round(UploadSpeedKBs).ToString() + "KB/s";

        string keyCount = KeystrokeCount >= 1000
            ? (KeystrokeCount / 1000f).ToString("0.0", inv) + "k"
            : KeystrokeCount.ToString();

        string mediaTrack = string.IsNullOrWhiteSpace(MediaArtist)
            ? MediaTitle
            : $"{MediaArtist} - {MediaTitle}";

        string volText = IsVolumeMuted ? "MUTED" : $"{Math.Round(VolumeLevel)}%";

        string cpuMhzStr = Math.Round(CpuClock).ToString();
        string cpuGhzStr = (CpuClock / 1000f).ToString("0.0", inv);
        string gpuMhzStr = Math.Round(GpuClock).ToString();
        string gpuGhzStr = (GpuClock / 1000f).ToString("0.0", inv);
        string gpuMemMhzStr = Math.Round(GpuMemoryClock).ToString();
        string ramMhzStr = Math.Round(RamClock).ToString();

        // 1. Intelligent unit conflict resolution:
        // If a user types {cpu_clock_ghz}MHz or {cpu_clock}GHz, resolve to the requested physical unit.
        template = template
            .Replace("{cpu_clock_ghz}MHz",  cpuMhzStr + "MHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock_ghz} MHz", cpuMhzStr + " MHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq_ghz}MHz",   cpuMhzStr + "MHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq_ghz} MHz",  cpuMhzStr + " MHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_ghz}MHz",        cpuMhzStr + "MHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_ghz} MHz",       cpuMhzStr + " MHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock}GHz",      cpuGhzStr + "GHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock} GHz",     cpuGhzStr + " GHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq}GHz",       cpuGhzStr + "GHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq} GHz",      cpuGhzStr + " GHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock_ghz}MHz",  gpuMhzStr + "MHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock_ghz} MHz", gpuMhzStr + " MHz", StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock}GHz",      gpuGhzStr + "GHz",  StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock} GHz",     gpuGhzStr + " GHz", StringComparison.OrdinalIgnoreCase);

        return template
            .Replace("{time}",          Timestamp.ToString("HH:mm:ss"),                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{time_short}",     Timestamp.ToString("HH:mm"),                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_load}",       Math.Round(CpuLoad).ToString(),                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_temp}",       Math.Round(CpuTemp > 0 ? CpuTemp : CpuPackageTemp).ToString(),        StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_power}",      Math.Round(CpuPower).ToString(),                                       StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock}",      cpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock_mhz}",  cpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq}",       cpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq_mhz}",   cpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_mhz}",        cpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_clock_ghz}",  cpuGhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_freq_ghz}",   cpuGhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_ghz}",        cpuGhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_volt}",       Math.Round(CpuVoltage).ToString(),                                     StringComparison.OrdinalIgnoreCase)
            .Replace("{cpu_fan}",        Math.Round(CpuFanRpm).ToString(),                                      StringComparison.OrdinalIgnoreCase)
            .Replace("{mid_fan}",        Math.Round(MidFanRpm).ToString(),                                      StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_load}",       Math.Round(GpuLoad).ToString(),                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_temp}",       Math.Round(GpuTemp).ToString(),                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_hotspot}",    Math.Round(GpuHotspot > 0 ? GpuHotspot : GpuTemp).ToString(),         StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_delta}",      Math.Round(GpuDelta).ToString() + "°C",                                StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_power}",      Math.Round(GpuPower).ToString(),                                       StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock}",      gpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock_mhz}",  gpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_freq}",       gpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_freq_mhz}",   gpuMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_clock_ghz}",  gpuGhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_ghz}",        gpuGhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_mem_clock}",  gpuMemMhzStr,                                                          StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_mem_clock_mhz}", gpuMemMhzStr,                                                       StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_volt}",       Math.Round(GpuVoltage).ToString(),                                     StringComparison.OrdinalIgnoreCase)
            .Replace("{total_power}",    Math.Round(TotalPower).ToString(),                                      StringComparison.OrdinalIgnoreCase)
            .Replace("{power_source}",   PowerSource,                                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_vram_temp}",  Math.Round(GpuVramTemp).ToString(),                                    StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_fan}",        Math.Round(GpuFanRpm).ToString(),                                      StringComparison.OrdinalIgnoreCase)
            .Replace("{gpu_vram_used}",  (GpuVramUsedMb / 1024f).ToString("0.0", inv),                           StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_percent}",    Math.Round(RamPercent).ToString(),                                     StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_used}",       RamUsedGb.ToString("0.0", inv),                                         StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_total}",      Math.Round(RamTotalGb).ToString(),                                     StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_clock}",      ramMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_freq}",       ramMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{ram_mhz}",        ramMhzStr,                                                             StringComparison.OrdinalIgnoreCase)
            .Replace("{ping}",           PingMs >= 0 ? PingMs.ToString() : "OFF",                               StringComparison.OrdinalIgnoreCase)
            .Replace("{net_down}",       netDown,                                                               StringComparison.OrdinalIgnoreCase)
            .Replace("{net_up}",         netUp,                                                                 StringComparison.OrdinalIgnoreCase)
            .Replace("{apm}",            Apm.ToString(),                                                        StringComparison.OrdinalIgnoreCase)
            .Replace("{key_count}",      keyCount,                                                              StringComparison.OrdinalIgnoreCase)
            .Replace("{media_title}",    MediaTitle,                                                            StringComparison.OrdinalIgnoreCase)
            .Replace("{media_artist}",   MediaArtist,                                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{media_status}",   MediaStatus,                                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{media_track}",    mediaTrack,                                                            StringComparison.OrdinalIgnoreCase)
            .Replace("{vol}",            volText,                                                               StringComparison.OrdinalIgnoreCase)
            .Replace("{caps}",           IsCapsLock ? "CAPS" : "",                                              StringComparison.OrdinalIgnoreCase)
            .Replace("{num}",            IsNumLock ? "NUM" : "",                                                StringComparison.OrdinalIgnoreCase)
            .Replace("{scroll}",         IsScrollLock ? "SCRL" : "",                                            StringComparison.OrdinalIgnoreCase)
            .Replace("{mic}",            IsMicMuted ? "MUTED" : "ON",                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{weather_temp}",   WeatherTemp,                                                           StringComparison.OrdinalIgnoreCase)
            .Replace("{weather_cond}",   WeatherCondition,                                                      StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns numeric value for graphical gauges/bars (0..100).
    /// </summary>
    public float GetNumericValue(string metricKey) => metricKey?.ToLowerInvariant() switch
    {
        "cpu_load"                                                 => CpuLoad,
        "cpu_temp"                                                 => CpuTemp > 0 ? CpuTemp : CpuPackageTemp,
        "cpu_power"                                                => CpuPower,
        "cpu_clock" or "cpu_clock_mhz" or "cpu_freq" or "cpu_mhz"  => CpuClock,
        "cpu_clock_ghz" or "cpu_ghz"                               => CpuClock / 1000f,
        "cpu_volt"                                                 => CpuVoltage,
        "cpu_fan"                                                  => CpuFanRpm,
        "mid_fan"                                                  => MidFanRpm,
        "gpu_load"                                                 => GpuLoad,
        "gpu_temp"                                                 => GpuTemp,
        "gpu_hotspot"                                              => GpuHotspot > 0 ? GpuHotspot : GpuTemp,
        "gpu_delta"                                                => GpuDelta,
        "gpu_power"                                                => GpuPower,
        "gpu_clock" or "gpu_clock_mhz" or "gpu_freq"               => GpuClock,
        "gpu_clock_ghz" or "gpu_ghz"                               => GpuClock / 1000f,
        "gpu_mem_clock" or "gpu_mem_clock_mhz"                     => GpuMemoryClock,
        "gpu_volt"                                                 => GpuVoltage,
        "total_power"                                              => TotalPower,
        "gpu_vram_temp"                                            => GpuVramTemp,
        "gpu_fan"                                                  => GpuFanRpm,
        "ram_percent"                                              => RamPercent,
        "ram_clock" or "ram_freq" or "ram_mhz"                     => RamClock,
        "ping"                                                     => PingMs >= 0 ? PingMs : 0,
        "net_down"                                                 => DownloadSpeedKBs,
        "net_up"        => UploadSpeedKBs,
        "apm"           => Apm,
        "key_count"     => KeystrokeCount,
        "vol"           => VolumeLevel,
        _ => 0f
    };
}
