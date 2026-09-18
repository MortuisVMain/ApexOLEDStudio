using System;
using ApexOLEDStudio.Core.Models;
using ApexOLEDStudio.Core.Services;
using Xunit;

namespace ApexOLEDStudio.Tests;

public class AsusAcpiAndHardwareTests
{
    [Theory]
    [InlineData(0x00010039u, 57)] // 57°C (Armoury Crate test vector)
    [InlineData(0x00010031u, 49)] // 49°C (Armoury Crate GPU test vector)
    [InlineData(0x00010046u, 70)] // 70°C
    public void AsusAcpiProvider_DecodeTemperature_ValidStatus_ReturnsCelsius(uint rawStatus, int expectedTemp)
    {
        var temp = AsusAcpiProvider.DecodeTemperature(rawStatus);
        Assert.NotNull(temp);
        Assert.Equal(expectedTemp, temp.Value);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x000100FAu)] // 250°C (out of bounds)
    public void AsusAcpiProvider_DecodeTemperature_InvalidStatus_ReturnsNull(uint rawStatus)
    {
        var temp = AsusAcpiProvider.DecodeTemperature(rawStatus);
        Assert.Null(temp);
    }

    [Theory]
    [InlineData(0x00010030u, 4800)] // 48 * 100 = 4800 RPM (Armoury Crate CPU Fan)
    [InlineData(0x00010033u, 5100)] // 51 * 100 = 5100 RPM (Armoury Crate GPU Fan)
    [InlineData(0x00010032u, 5000)] // 50 * 100 = 5000 RPM (Armoury Crate Mid Fan)
    public void AsusAcpiProvider_DecodeFanRpm_ValidStatus_ReturnsRpm(uint rawStatus, int expectedRpm)
    {
        var rpm = AsusAcpiProvider.DecodeFanRpm(rawStatus);
        Assert.NotNull(rpm);
        Assert.Equal(expectedRpm, rpm.Value);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x000100FFu)] // > 120 (out of bounds)
    public void AsusAcpiProvider_DecodeFanRpm_InvalidStatus_ReturnsNull(uint rawStatus)
    {
        var rpm = AsusAcpiProvider.DecodeFanRpm(rawStatus);
        Assert.Null(rpm);
    }

    [Fact]
    public void HardwareMetrics_FormatTemplate_ReplacesNewTokens()
    {
        var metrics = new HardwareMetrics
        {
            CpuLoad = 15f,
            CpuTemp = 57f,
            CpuPower = 45f,
            CpuClock = 4849f,
            CpuVoltage = 1130f,
            CpuFanRpm = 4800f,
            MidFanRpm = 5000f,
            GpuTemp = 49f,
            GpuClock = 1695f,
            GpuMemoryClock = 14001f,
            GpuVoltage = 785f,
            GpuFanRpm = 5100f,
            RamClock = 5600f
        };

        string template = "CPU {cpu_temp}°C {cpu_clock}MHz ({cpu_clock_ghz}GHz, {cpu_volt}mV) | GPU {gpu_clock}MHz VRAM: {gpu_mem_clock}MHz ({gpu_volt}mV) | RAM: {ram_clock}MT/s";
        string formatted = metrics.FormatTemplate(template);

        Assert.Equal("CPU 57°C 4849MHz (4.8GHz, 1130mV) | GPU 1695MHz VRAM: 14001MHz (785mV) | RAM: 5600MT/s", formatted);
    }

    [Fact]
    public void HardwareMetrics_GetNumericValue_SupportsNewKeys()
    {
        var metrics = new HardwareMetrics
        {
            CpuFanRpm = 4800f,
            CpuClock = 4849f,
            CpuVoltage = 1130f,
            GpuClock = 1695f,
            GpuMemoryClock = 14001f,
            GpuVoltage = 785f,
            RamClock = 5600f,
            MidFanRpm = 5000f
        };

        Assert.Equal(4800f, metrics.GetNumericValue("cpu_fan"));
        Assert.Equal(4849f, metrics.GetNumericValue("cpu_clock"));
        Assert.Equal(1130f, metrics.GetNumericValue("cpu_volt"));
        Assert.Equal(1695f, metrics.GetNumericValue("gpu_clock"));
        Assert.Equal(14001f, metrics.GetNumericValue("gpu_mem_clock"));
        Assert.Equal(785f, metrics.GetNumericValue("gpu_volt"));
        Assert.Equal(5600f, metrics.GetNumericValue("ram_clock"));
        Assert.Equal(5000f, metrics.GetNumericValue("mid_fan"));
    }

    [Fact]
    public void HardwareMetrics_FormatTemplate_CorrectsUnitConflict_AndAliases()
    {
        var metrics = new HardwareMetrics
        {
            CpuClock = 5200f,
            GpuClock = 1905f,
            GpuMemoryClock = 14226f,
            RamClock = 5600f
        };

        // If template accidentally has {cpu_clock_ghz}MHz or {cpu_clock}GHz
        Assert.Equal("5200MHz", metrics.FormatTemplate("{cpu_clock_ghz}MHz"));
        Assert.Equal("5200 MHz", metrics.FormatTemplate("{cpu_clock_ghz} MHz"));
        Assert.Equal("5.2GHz", metrics.FormatTemplate("{cpu_clock}GHz"));
        Assert.Equal("5.2 GHz", metrics.FormatTemplate("{cpu_clock} GHz"));

        // Aliases
        Assert.Equal("5200MHz", metrics.FormatTemplate("{cpu_clock_mhz}MHz"));
        Assert.Equal("5200MHz", metrics.FormatTemplate("{cpu_freq}MHz"));
        Assert.Equal("5200MHz", metrics.FormatTemplate("{cpu_mhz}MHz"));
        Assert.Equal("5.2GHz", metrics.FormatTemplate("{cpu_ghz}GHz"));
        Assert.Equal("1905MHz", metrics.FormatTemplate("{gpu_freq}MHz"));
        Assert.Equal("14226MHz", metrics.FormatTemplate("{gpu_mem_clock_mhz}MHz"));
        Assert.Equal("5600MT/s", metrics.FormatTemplate("{ram_freq}MT/s"));
    }
}

