using ApexOLEDStudio.Core.Drawing;
using ApexOLEDStudio.Core.Models;

namespace ApexOLEDStudio.Core.Drivers;

public sealed class ApexProProtocolProfile : IProtocolProfile
{
    public string Id => "steelseries.apex-oled.v1";
    public string DisplayName => "SteelSeries Apex OLED";

    public DeviceValidationResult Validate(DeviceDescriptor device)
    {
        if (device.VendorId != ApexProHidDriver.SteelSeriesVendorId)
            return DeviceValidationResult.Unsupported("Vendor is not SteelSeries.");
        if (!ApexProHidDriver.SupportedProductIds.Contains(device.ProductId))
            return DeviceValidationResult.Unsupported($"Product 0x{device.ProductId:X4} is not in the supported Apex OLED list.");
        if ((device.Capabilities & (DeviceCapabilities.OledDisplay | DeviceCapabilities.FeatureReports)) !=
            (DeviceCapabilities.OledDisplay | DeviceCapabilities.FeatureReports))
            return DeviceValidationResult.Unsupported("Device does not expose the required OLED feature-report capability.");
        if (device.DisplayWidth != OledFrameBuffer.Width || device.DisplayHeight != OledFrameBuffer.Height)
            return DeviceValidationResult.Unsupported("Device display dimensions do not match the 128x40 Apex OLED protocol.");
        return DeviceValidationResult.Supported($"Validated {device.Model} for {DisplayName}.");
    }

    public byte[] CreateFrameReport(OledFrameBuffer frame) => frame.ToApexHidPacket();
}
