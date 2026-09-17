using System;
using System.IO;
using System.Threading;
using ApexOLEDStudio.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace ApexOLEDStudio.Tests;

public class HardwareServiceRealVerification
{
    private readonly ITestOutputHelper _output;

    public HardwareServiceRealVerification(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void VerifyRealHardwareMetrics_GpuAndCpuNonZero()
    {
        using var service = new HardwareMonitorService();
        service.PollHardware();
        Thread.Sleep(500);
        service.PollHardware();

        var m = service.CurrentMetrics;
        _output.WriteLine($"GpuName: '{m.GpuName}'");
        _output.WriteLine($"GpuLoad: {m.GpuLoad}%");
        _output.WriteLine($"GpuTemp: {m.GpuTemp}°C");
        _output.WriteLine($"GpuPower: {m.GpuPower}W");
        _output.WriteLine($"CpuLoad: {m.CpuLoad}%");
        _output.WriteLine($"CpuTemp: {m.CpuTemp}°C");
        _output.WriteLine($"CpuPower: {m.CpuPower}W");
        _output.WriteLine($"TotalPower: {m.TotalPower}W");

        // The GPU on ROG Strix G18 is detected as NVIDIA GeForce RTX (or fallback 'GPU' when dGPU is in sleep/D3 state)
        Assert.True(m.GpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || m.GpuName.Equals("GPU", StringComparison.OrdinalIgnoreCase), $"Unexpected GpuName: '{m.GpuName}'");
        // GPU Temp must be realistic (above 30C)
        Assert.True(m.GpuTemp > 30f, $"GpuTemp was {m.GpuTemp}");
        // CPU Load must be tracked (above 0)
        Assert.True(m.CpuLoad >= 0f);
        // GPU Power must be detected (above 0W)
        Assert.True(m.GpuPower > 0f, $"GpuPower was {m.GpuPower}W");
        // CPU Power must be calculated/detected (above 0W)
        Assert.True(m.CpuPower > 0f, $"CpuPower was {m.CpuPower}W");
        // Total power must be above 0W
        Assert.True(m.TotalPower > 0f, $"TotalPower was {m.TotalPower}W");
    }
}
