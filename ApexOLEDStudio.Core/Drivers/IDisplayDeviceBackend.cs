using ApexOLEDStudio.Core.Drawing;
using ApexOLEDStudio.Core.Models;

namespace ApexOLEDStudio.Core.Drivers;

public interface IProtocolProfile
{
    string Id { get; }
    string DisplayName { get; }
    DeviceValidationResult Validate(DeviceDescriptor device);
    byte[] CreateFrameReport(OledFrameBuffer frame);
}

public interface IDisplayDeviceBackend : IDisposable
{
    DeviceDescriptor? ConnectedDevice { get; }
    IProtocolProfile Protocol { get; }
    bool IsConnected { get; }
    string LastError { get; }
    bool Connect();
    bool SendFrame(OledFrameBuffer frame);
    void Disconnect();
}
