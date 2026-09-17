using System.Text.Json.Serialization;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// All user-configurable settings for ApexOLED Studio.
/// Serialised to %AppData%\ApexOLEDStudio\settings.json.
/// </summary>
public sealed class AppSettings
{
    // ── Startup & Window ─────────────────────────────────────────────
    public bool AutostartWithWindows   { get; set; } = false;
    public bool StartMinimized         { get; set; } = false;
    public bool MinimizeToTrayOnClose  { get; set; } = true;
    public bool EditorGridEnabled     { get; set; } = true;
    public int EditorGridStep         { get; set; } = 2;

    // ── Hardware Polling & Sensors ───────────────────────────────────
    /// <summary>How often LibreHardwareMonitor polls sensors (ms).</summary>
    public int PollingIntervalMs  { get; set; } = 1000;

    /// <summary>How often the HID frame is pushed to the keyboard (ms).</summary>
    public int HidSyncIntervalMs  { get; set; } = 1000;

    public bool EnableCpuMonitoring     { get; set; } = true;
    public bool EnableGpuMonitoring     { get; set; } = true;
    public bool EnableRamMonitoring     { get; set; } = true;
    public bool EnableNetworkMonitoring { get; set; } = true;
    public bool EnableStorageMonitoring { get; set; } = false;
    public bool EnablePowerMonitoring   { get; set; } = true;

    // ── Display & Burn-In Protection ─────────────────────────────────
    public bool InvertDisplay              { get; set; } = false;
    public bool BurnInProtectionEnabled    { get; set; } = true;

    /// <summary>Minutes between orbital pixel-shift steps.</summary>
    public int  BurnInShiftMinutes         { get; set; } = 3;

    /// <summary>Send a blank frame on Win+L session lock.</summary>
    public bool BlankOnWindowsLock         { get; set; } = true;

    // ── Audio & Volume HUD ───────────────────────────────────────────
    public bool EnableVolumeOverlay        { get; set; } = true;
    public int  VolumeOverlayDurationMs    { get; set; } = 1500;
    public bool EnableMicMonitor           { get; set; } = true;

    // ── Lock Keys & Hardware Alerts ──────────────────────────────────
    public bool  EnableLockKeys            { get; set; } = true;
    public bool  EnableGpuDeltaAlert       { get; set; } = true;
    public float GpuDeltaThreshold         { get; set; } = 20f; // Hotspot - Core temp (°C)
    public bool  EnableInvertOnThermalAlert{ get; set; } = true;

    // ── Hotkey & Automation ──────────────────────────────────────────
    public bool EnableHotkeyProfileSwitch { get; set; } = true;
    public bool EnableAutoProfileSwitch   { get; set; } = true;

    /// <summary>Mapping of executable name (lowercase without .exe) to Preset Name.</summary>
    public System.Collections.Generic.Dictionary<string, string> ProcessProfileRules { get; set; } = new()
    {
        { "cs2", "Gamer Pro (Ping/Net)" },
        { "dota2", "Gamer Pro (Ping/Net)" },
        { "valorant", "Gamer Pro (Ping/Net)" },
        { "cyberpunk2077", "Gamer Pro (Ping/Net)" },
        { "spotify", "Media Station" },
        { "vlc", "Media Station" }
    };

    // ── Weather Telemetry ────────────────────────────────────────────
    public bool   EnableWeather                { get; set; } = false;
    public string WeatherCity                  { get; set; } = "auto";
    public int    WeatherUpdateIntervalMinutes { get; set; } = 30;
}
