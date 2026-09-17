using System;
using System.Diagnostics;
using System.Management;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Interacts with ASUS System Control Interface via WMI (root\wmi:AsusAtkWmi_WMNB)
/// to read CPU/GPU hardware temperatures and fan speeds directly from the Embedded Controller (EC).
/// Standard interface used by ASUS Armoury Crate and G-Helper.
/// </summary>
public sealed class AsusAcpiProvider
{
    public const uint DevCpuFan  = 0x00110013;
    public const uint DevGpuFan  = 0x00110014;
    public const uint DevMidFan  = 0x00110031;
    public const uint DevTempCpu = 0x00120094;
    public const uint DevTempGpu = 0x00120097;

    private bool _wmiAvailable = true;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Decodes raw DSTS status returned by ASUS WMI into temperature (°C).
    /// Format: bit 16 = status flag, bits 0..15 = temperature in °C.
    /// </summary>
    public static int? DecodeTemperature(uint status)
    {
        if (status == 0 || status == uint.MaxValue) return null;
        int val = (int)(status & 0xFFFF);
        return (val > 0 && val < 130) ? val : null;
    }

    /// <summary>
    /// Decodes raw DSTS status returned by ASUS WMI into Fan RPM.
    /// Format: bit 16 = status flag, bits 0..15 = RPM / 100.
    /// </summary>
    public static int? DecodeFanRpm(uint status)
    {
        if (status == 0 || status == uint.MaxValue) return null;
        int raw = (int)(status & 0xFFFF);
        if (raw > 0 && raw <= 120)
        {
            return raw * 100;
        }
        return null;
    }

    /// <summary>
    /// Queries the DSTS method on AsusAtkWmi_WMNB for a given Device ID.
    /// Returns raw device_status uint or null if unavailable.
    /// </summary>
    public uint? QueryDeviceStatus(uint deviceId)
    {
        if (!_wmiAvailable && DateTime.UtcNow - _lastCheckTime < CheckCooldown)
        {
            return null;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                using (obj)
                {
                    var inParams = obj.GetMethodParameters("DSTS");
                    inParams["Device_ID"] = deviceId;
                    var outParams = obj.InvokeMethod("DSTS", inParams, null);
                    if (outParams?["device_status"] is uint status)
                    {
                        _wmiAvailable = true;
                        return status;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Access denied, non-ASUS board, or WMI service not ready
            _wmiAvailable = false;
            _lastCheckTime = DateTime.UtcNow;
            Debug.WriteLine($"[AsusAcpiProvider] DSTS 0x{deviceId:X8} failed: {ex.Message}");
        }

        return null;
    }

    public int? GetCpuTemperature()
    {
        var raw = QueryDeviceStatus(DevTempCpu);
        return raw.HasValue ? DecodeTemperature(raw.Value) : null;
    }

    public int? GetGpuTemperature()
    {
        var raw = QueryDeviceStatus(DevTempGpu);
        return raw.HasValue ? DecodeTemperature(raw.Value) : null;
    }

    public int? GetCpuFanRpm()
    {
        var raw = QueryDeviceStatus(DevCpuFan);
        return raw.HasValue ? DecodeFanRpm(raw.Value) : null;
    }

    public int? GetGpuFanRpm()
    {
        var raw = QueryDeviceStatus(DevGpuFan);
        return raw.HasValue ? DecodeFanRpm(raw.Value) : null;
    }

    public int? GetMidFanRpm()
    {
        var raw = QueryDeviceStatus(DevMidFan);
        return raw.HasValue ? DecodeFanRpm(raw.Value) : null;
    }
}
