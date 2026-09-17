using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Loads/saves AppSettings JSON and manages the Windows autostart registry entry.
/// </summary>
public static class SettingsService
{
    private const string AppName   = "ApexOLEDStudio";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppName, "settings.json");

    public static string? LastError { get; private set; }

    // ── Load / Save ───────────────────────────────────────────────────

    public static AppSettings Load()
    {
        LastError = null;
        string path = SettingsPath;
        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            LastError = "Settings file is corrupted; defaults were loaded.";
            BackupCorruptFile(path, ex);
            return new AppSettings();
        }
        catch (IOException ex)
        {
            LastError = "Settings file could not be read; defaults were loaded.";
            Debug.WriteLine($"[Settings] Read error: {ex}");
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string dir  = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        WriteAtomically(SettingsPath, json);

        // Apply autostart immediately when saving
        ApplyAutostart(settings.AutostartWithWindows);
    }

    // ── Autostart (HKCU Registry) ─────────────────────────────────────

    /// <summary>
    /// Writes or removes the HKCU\...\Run registry entry for ApexOLED Studio.
    /// Uses the path of the currently running process executable.
    /// </summary>
    public static void ApplyAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enable)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                                 ?? Environment.ProcessPath
                                 ?? string.Empty;

                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                // Remove key if present
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName);
            }
        }
        catch (Exception ex)
        {
            LastError = "Autostart could not be updated.";
            Debug.WriteLine($"[Settings] Autostart error: {ex}");
        }
    }

    /// <summary>Returns true if the autostart registry entry currently exists.</summary>
    public static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Autostart query error: {ex}");
            return false;
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void BackupCorruptFile(string path, Exception exception)
    {
        Debug.WriteLine($"[Settings] Corrupt settings file: {exception}");
        try
        {
            string backupPath = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            File.Move(path, backupPath);
            LastError += $" Backup: {Path.GetFileName(backupPath)}";
        }
        catch (Exception backupException)
        {
            Debug.WriteLine($"[Settings] Could not back up corrupt settings: {backupException}");
        }
    }
}
