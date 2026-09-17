using System;
using System.IO;
using System.Text.Json;
using ApexOLEDStudio.Core.Models;

namespace ApexOLEDStudio.Core.Services;

public sealed class ProfileConfiguration
{
    public AppSettings Settings { get; set; } = new();
    public OledLayout Layout { get; set; } = OledLayout.CreateDefaultApexProSplit();
}

public static class ProfileConfigurationService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string Export(ProfileConfiguration profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Layout.ValidateOrThrow();
        return JsonSerializer.Serialize(profile, Options);
    }

    public static ProfileConfiguration Import(string json)
    {
        var profile = JsonSerializer.Deserialize<ProfileConfiguration>(json, Options)
            ?? throw new JsonException("Profile configuration is empty.");
        profile.Settings ??= new AppSettings();
        profile.Layout ??= OledLayout.CreateDefaultApexProSplit();
        profile.Layout.ValidateOrThrow();
        return profile;
    }

    public static void ExportToFile(ProfileConfiguration profile, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(filePath, Export(profile));
    }

    public static ProfileConfiguration ImportFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Import(File.ReadAllText(filePath));
    }
}
