using System;
using System.IO;
using System.Text.Json;
using ApexOLEDStudio.Core.Models;

namespace ApexOLEDStudio.Core.Services;

public static class LayoutSerializer
{
    public static string? LastError { get; private set; }
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(OledLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        layout.ValidateOrThrow();
        return JsonSerializer.Serialize(layout, Options);
    }

    public static OledLayout Deserialize(string json)
    {
        var layout = JsonSerializer.Deserialize<OledLayout>(json, Options)
            ?? throw new JsonException("Layout JSON contains no layout object.");
        layout.ValidateOrThrow();
        return layout;
    }

    public static void SaveToFile(OledLayout layout, string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = Serialize(layout);
        string tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(filePath))
                File.Replace(tempPath, filePath, null);
            else
                File.Move(tempPath, filePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static OledLayout LoadFromFile(string filePath)
    {
        LastError = null;
        if (!File.Exists(filePath)) return OledLayout.CreateDefaultApexProSplit();

        try
        {
            string json = File.ReadAllText(filePath);
            return Deserialize(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        {
            LastError = "Profile was invalid or corrupted; the default layout was loaded.";
            System.Diagnostics.Debug.WriteLine($"[Layout] Invalid profile '{filePath}': {ex}");
            try
            {
                string backupPath = $"{filePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                File.Move(filePath, backupPath);
            }
            catch (Exception backupException)
            {
                System.Diagnostics.Debug.WriteLine($"[Layout] Could not back up invalid profile: {backupException}");
            }
            return OledLayout.CreateDefaultApexProSplit();
        }
    }
}
