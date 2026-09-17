using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ApexOLEDStudio.Core.Drawing;
using ApexOLEDStudio.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace ApexOLEDStudio.Core.Drivers;

/// <summary>
/// Direct USB HID driver for SteelSeries Apex Pro / 7 / 5 keyboards.
/// Communicates directly with the keyboard controller without needing SteelSeries GG.
/// </summary>
public sealed class ApexProHidDriver : IDisplayDeviceBackend
{
    public const ushort SteelSeriesVendorId = 0x1038;

    // Supported SteelSeries OLED Product IDs
    public static readonly HashSet<ushort> SupportedProductIds = new()
    {
        0x1610, // Apex Pro
        0x1612, // Apex 7
        0x1614, // Apex Pro TKL
        0x1618, // Apex 7 TKL
        0x161C, // Apex 5
        0x161E, // Apex Pro Wireless
        0x12C2, // GameDAC Gen 2
        0x12D0  // Arctis Nova Pro Base Station
    };

    private SafeFileHandle? _deviceHandle;
    private string? _activeDevicePath;
    private bool _disposed;
    private DateTime _nextConnectAttemptUtc = DateTime.MinValue;
    private int _connectFailures;
    private readonly ApexProProtocolProfile _protocol = new();

    public string LastError { get; private set; } = string.Empty;
    public IProtocolProfile Protocol => _protocol;
    public DeviceDescriptor? ConnectedDevice { get; private set; }

    public bool IsConnected => _deviceHandle != null && !_deviceHandle.IsInvalid && !_deviceHandle.IsClosed;
    public string? ActiveDevicePath => _activeDevicePath;

    public bool Connect()
    {
        if (IsConnected) return true;
        if (DateTime.UtcNow < _nextConnectAttemptUtc) return false;

        var devices = EnumerateDevices();
        foreach (var dev in devices)
        {
            var descriptor = dev.ToDescriptor();
            var validation = _protocol.Validate(descriptor);
            if (validation.IsSupported)
            {
                var handle = Win32Hid.CreateFile(
                    dev.DevicePath,
                    Win32Hid.GENERIC_READ | Win32Hid.GENERIC_WRITE,
                    Win32Hid.FILE_SHARE_READ | Win32Hid.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32Hid.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    _deviceHandle = handle;
                    _activeDevicePath = dev.DevicePath;
                    ConnectedDevice = descriptor;
                    _connectFailures = 0;
                    LastError = string.Empty;
                    Debug.WriteLine($"[ApexProHidDriver] Successfully connected to Apex Pro at {dev.DevicePath}");
                    return true;
                }
            }
        }

        _connectFailures = Math.Min(_connectFailures + 1, 6);
        _nextConnectAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, _connectFailures - 1)));
        LastError = "No supported SteelSeries HID device is available.";
        return false;
    }

    public bool SendFrame(OledFrameBuffer buffer)
    {
        if (!IsConnected && !Connect()) return false;

        try
        {
            byte[] packet = buffer.ToApexHidPacket();
            bool success = Win32Hid.HidD_SetFeature(_deviceHandle!, packet, (uint)packet.Length);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[ApexProHidDriver] HidD_SetFeature failed with error code {error}");
                LastError = $"HID feature write failed (Win32 {error}).";
                Disconnect();
            }
            return success;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ApexProHidDriver] Send error: {ex.Message}");
            LastError = $"HID send failed: {ex.Message}";
            Disconnect();
            return false;
        }
    }

    public void Disconnect()
    {
        _deviceHandle?.Dispose();
        _deviceHandle = null;
        _activeDevicePath = null;
        ConnectedDevice = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }

    public record DeviceInfo(string DevicePath, ushort VendorId, ushort ProductId)
    {
        public DeviceDescriptor ToDescriptor() => new(
            DevicePath,
            VendorId,
            ProductId,
            $"SteelSeries 0x{ProductId:X4}",
            OledFrameBuffer.Width,
            OledFrameBuffer.Height,
            DeviceCapabilities.OledDisplay | DeviceCapabilities.FeatureReports);
    }

    public static List<DeviceInfo> EnumerateDevices()
    {
        var list = new List<DeviceInfo>();
        Guid hidGuid = Win32Hid.HidGuid;

        IntPtr devInfo = Win32Hid.SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            Win32Hid.DIGCF_PRESENT | Win32Hid.DIGCF_DEVICEINTERFACE);

        if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return list;

        try
        {
            var ifData = new Win32Hid.SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = (uint)Marshal.SizeOf(ifData);

            for (uint i = 0; Win32Hid.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
            {
                uint reqSize = 0;
                Win32Hid.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, ref reqSize, IntPtr.Zero);

                if (reqSize == 0) continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6); // cbSize depends on architecture

                    if (Win32Hid.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, ref reqSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string? path = Marshal.PtrToStringAuto(pDevicePath);

                        if (!string.IsNullOrEmpty(path))
                        {
                            var (vid, pid) = ExtractVidPid(path);
                            list.Add(new DeviceInfo(path, vid, pid));
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            Win32Hid.SetupDiDestroyDeviceInfoList(devInfo);
        }

        return list;
    }

    private static (ushort vid, ushort pid) ExtractVidPid(string path)
    {
        ushort vid = 0;
        ushort pid = 0;
        string lower = path.ToLowerInvariant();

        int vidIdx = lower.IndexOf("vid_");
        if (vidIdx != -1 && vidIdx + 8 <= lower.Length)
        {
            string vidStr = lower.Substring(vidIdx + 4, 4);
            ushort.TryParse(vidStr, System.Globalization.NumberStyles.HexNumber, null, out vid);
        }

        int pidIdx = lower.IndexOf("pid_");
        if (pidIdx != -1 && pidIdx + 8 <= lower.Length)
        {
            string pidStr = lower.Substring(pidIdx + 4, 4);
            ushort.TryParse(pidStr, System.Globalization.NumberStyles.HexNumber, null, out pid);
        }

        return (vid, pid);
    }
}

internal static class Win32Hid
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    public static readonly Guid HidGuid = new(0x4d1e55b2, 0xf16f, 0x11cf, 0x88, 0xcb, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);
}
