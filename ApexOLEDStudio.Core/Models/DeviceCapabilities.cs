using System;

namespace ApexOLEDStudio.Core.Models;

[Flags]
public enum DeviceCapabilities
{
    None = 0,
    OledDisplay = 1,
    FeatureReports = 2,
    TemperatureTelemetry = 4,
    PowerTelemetry = 8
}

public sealed record DeviceDescriptor(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    string Model,
    int DisplayWidth,
    int DisplayHeight,
    DeviceCapabilities Capabilities);

public sealed record DeviceValidationResult(bool IsSupported, string Message)
{
    public static DeviceValidationResult Supported(string message = "Device is supported.")
        => new(true, message);

    public static DeviceValidationResult Unsupported(string message)
        => new(false, message);
}
