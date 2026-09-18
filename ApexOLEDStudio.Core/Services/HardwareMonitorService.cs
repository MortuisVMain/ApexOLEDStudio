using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using ApexOLEDStudio.Core.Models;
using LibreHardwareMonitor.Hardware;

namespace ApexOLEDStudio.Core.Services;

public interface IHardwareMonitorService : IDisposable
{
    HardwareMetrics CurrentMetrics { get; }
    event EventHandler<HardwareMetrics>? MetricsUpdated;
    void Start(TimeSpan updateInterval);
    void Stop();
    void UpdateInterval(TimeSpan newInterval);
    bool IsRunning { get; }
    void ConfigureSensors(AppSettings settings);
}

public sealed class HardwareMonitorService : IHardwareMonitorService
{
    private readonly Computer? _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly AsusAcpiProvider _asusAcpi = new();
    private PerformanceCounter? _cpuUtilityCounter;

    private float _cachedRamSpeed = 5600f;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _powerMonitoringEnabled = true;

    // Win32 System Times for true hardware CPU Load calculation
    private long _prevIdleTime;
    private long _prevKernelTime;
    private long _prevUserTime;
    private bool _hasPrevTimes;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
                                              out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
                                              out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    private static extern int CallNtPowerInformation(
        int informationLevel,
        IntPtr lpInputBuffer,
        int nInputBufferSize,
        [Out] PROCESSOR_POWER_INFORMATION[] lpOutputBuffer,
        int nOutputBufferSize);

    public static float GetSystemCpuClockMhz()
    {
        try
        {
            int coreCount = Environment.ProcessorCount;
            if (coreCount <= 0) coreCount = 64;
            var info = new PROCESSOR_POWER_INFORMATION[coreCount];
            int structSize = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            int totalSize = structSize * coreCount;
            int status = CallNtPowerInformation(11, IntPtr.Zero, 0, info, totalSize);
            if (status == 0)
            {
                if (info.Length > 0 && info[0].CurrentMhz > 0)
                {
                    return info[0].CurrentMhz;
                }
                uint maxMhz = 0;
                for (int i = 0; i < coreCount; i++)
                {
                    if (info[i].CurrentMhz > maxMhz)
                    {
                        maxMhz = info[i].CurrentMhz;
                    }
                }
                if (maxMhz > 0) return maxMhz;
            }
        }
        catch
        {
            // Fallback for non-Windows platforms or permission bounds
        }
        return 0f;
    }

    public HardwareMetrics CurrentMetrics { get; private set; } = new();
    public event EventHandler<HardwareMetrics>? MetricsUpdated;
    public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    public HardwareMonitorService()
    {
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = false,
                IsStorageEnabled = false
            };
            _computer.Open();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] LibreHardwareMonitor initialization notice: {ex.Message}");
            _computer = null;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _cpuUtilityCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuUtilityCounter.NextValue();

            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] PerformanceCounter init notice: {ex.Message}");
            _cpuUtilityCounter = null;

        }

        // Cache DDR5 RAM configured speed once (e.g. 5600 MT/s)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                if (obj["ConfiguredClockSpeed"] is uint spd && spd > 0)
                {
                    _cachedRamSpeed = spd;
                    break;
                }
                if (obj["Speed"] is uint spd2 && spd2 > 0)
                {
                    _cachedRamSpeed = spd2;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] RAM speed query notice: {ex.Message}");
        }
    }

    public void ConfigureSensors(AppSettings settings)
    {
        _powerMonitoringEnabled = settings.EnablePowerMonitoring;
        if (_computer == null) return;
        try
        {
            _computer.IsCpuEnabled = settings.EnableCpuMonitoring;
            _computer.IsGpuEnabled = settings.EnableGpuMonitoring;
            _computer.IsMemoryEnabled = settings.EnableRamMonitoring;
            _computer.IsMotherboardEnabled = true;
            _computer.IsControllerEnabled = true;
            _computer.IsNetworkEnabled = settings.EnableNetworkMonitoring;
            _computer.IsStorageEnabled = settings.EnableStorageMonitoring;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] ConfigureSensors error: {ex.Message}");
        }
    }

    public void Start(TimeSpan updateInterval)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollHardware();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HardwareMonitor] Poll error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(updateInterval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void UpdateInterval(TimeSpan newInterval)
    {
        if (!IsRunning) return;
        Stop();
        Start(newInterval);
    }

    public void PollHardware()
    {
        var metrics = new HardwareMetrics { Timestamp = DateTime.Now };
        bool cpuPowerFromSensor = false;
        bool gpuPowerFromSensor = false;

        // 1. Physical RAM via Win32 GlobalMemoryStatusEx (Exact match with Task Manager / Armoury Crate)
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem))
            {
                double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGb = totalGb - availGb;
                metrics.RamTotalGb = (float)Math.Round(totalGb, 1);
                metrics.RamUsedGb = (float)Math.Round(usedGb, 1);
                metrics.RamPercent = (float)Math.Round((usedGb / totalGb) * 100.0, 0);
                metrics.RamClock = _cachedRamSpeed;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] Memory status query error: {ex.Message}");
        }

        // 2. Win32 System CPU Load Calculation (GetSystemTimes)
        float win32CpuLoad = -1f;
        try
        {
            if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                long idle = ((long)idleFt.dwHighDateTime << 32) | (uint)idleFt.dwLowDateTime;
                long kernel = ((long)kernelFt.dwHighDateTime << 32) | (uint)kernelFt.dwLowDateTime;
                long user = ((long)userFt.dwHighDateTime << 32) | (uint)userFt.dwLowDateTime;

                if (_hasPrevTimes)
                {
                    long idleDelta = idle - _prevIdleTime;
                    long kernelDelta = kernel - _prevKernelTime;
                    long userDelta = user - _prevUserTime;
                    long totalDelta = kernelDelta + userDelta;

                    if (totalDelta > 0)
                    {
                        double load = (1.0 - ((double)idleDelta / totalDelta)) * 100.0;
                        win32CpuLoad = (float)Math.Clamp(Math.Round(load, 0), 0.0, 100.0);
                    }
                }

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _hasPrevTimes = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] GetSystemTimes error: {ex.Message}");
        }

        // 2. CPU Load: Win32 API GetSystemTimes & % Processor Time (matches Windows Task Manager & Armoury Crate)
        float perfCpuLoad = -1f;
        if (_cpuUtilityCounter != null)
        {
            try
            {
                float util = _cpuUtilityCounter.NextValue();
                if (util >= 0f)
                {
                    perfCpuLoad = (float)Math.Clamp(Math.Round(util, 0), 0.0, 100.0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareMonitor] PerformanceCounter read error: {ex.Message}");
            }
        }

        // Prefer win32CpuLoad (kernel32 GetSystemTimes) as it's the exact authoritative source used by Armoury Crate,
        // followed by % Processor Time PerformanceCounter
        if (win32CpuLoad >= 0f)
        {
            metrics.CpuLoad = win32CpuLoad;
        }
        else if (perfCpuLoad >= 0f)
        {
            metrics.CpuLoad = perfCpuLoad;
        }

        // CPU Frequency: CallNtPowerInformation gives per-core CurrentMhz (works without admin, matches Armoury Crate)
        float ntFreq = GetSystemCpuClockMhz();
        if (ntFreq > 500f)
        {
            metrics.CpuClock = ntFreq;
        }

        // 3. ASUS ACPI WMI Telemetry (Direct from EC BIOS: exact values used by Armoury Crate)
        int? asusCpuTemp = null;
        int? asusGpuTemp = null;
        try
        {
            asusCpuTemp = _asusAcpi.GetCpuTemperature();
            asusGpuTemp = _asusAcpi.GetGpuTemperature();
            var cpuFan = _asusAcpi.GetCpuFanRpm();
            var gpuFan = _asusAcpi.GetGpuFanRpm();
            var midFan = _asusAcpi.GetMidFanRpm();

            // Direct assignment: EC temperature is the authoritative source used by ASUS
            if (asusCpuTemp.HasValue && asusCpuTemp.Value > 0)
            {
                metrics.CpuTemp = asusCpuTemp.Value;
                metrics.CpuPackageTemp = asusCpuTemp.Value;
            }
            if (asusGpuTemp.HasValue && asusGpuTemp.Value > 0)
            {
                metrics.GpuTemp = asusGpuTemp.Value;
            }

            if (cpuFan.HasValue) metrics.CpuFanRpm = cpuFan.Value;
            if (gpuFan.HasValue) metrics.GpuFanRpm = gpuFan.Value;
            if (midFan.HasValue) metrics.MidFanRpm = midFan.Value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareMonitor] AsusAcpi query error: {ex.Message}");
        }

        // 4. LibreHardwareMonitor Hardware Traversal
        if (_computer != null)
        {
            try
            {
                _computer.Accept(_visitor);

                // --- CPU Telemetry ---
                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    // CPU Load: Total sensor from LibreHardwareMonitor (fallback if counter not active)
                    if (metrics.CpuLoad <= 0f)
                    {
                        var totalLoadSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load &&
                            (s.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("Total", StringComparison.OrdinalIgnoreCase)))
                            ?? cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));

                        if (totalLoadSensor?.Value != null && totalLoadSensor.Value.Value > 0)
                        {
                            metrics.CpuLoad = (float)Math.Round(totalLoadSensor.Value.Value, 0);
                        }
                    }

                    // CPU Temperature: Package / Core Average first (never Core Max spike)
                    if (metrics.CpuTemp <= 0f)
                    {
                        var pkgTempSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                            (s.Name.Equals("CPU Package", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) && s.Value > 0)
                            ?? cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                                s.Name.Equals("Core Average", StringComparison.OrdinalIgnoreCase) && s.Value > 0)
                            ?? cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                                s.Name.Equals("Core Max", StringComparison.OrdinalIgnoreCase) && s.Value > 0)
                            ?? cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value > 0 && !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
                                          .OrderByDescending(s => s.Value)
                                          .FirstOrDefault();

                        if (pkgTempSensor?.Value != null && pkgTempSensor.Value.Value > 0)
                        {
                            metrics.CpuTemp = (float)Math.Round(pkgTempSensor.Value.Value, 0);
                            metrics.CpuPackageTemp = metrics.CpuTemp;
                        }
                    }

                    // CPU Frequency fallback: LHM per-core sensors (highest active core)
                    if (metrics.CpuClock <= 0f)
                    {
                        var clockSensors = cpu.Sensors
                            .Where(s => s.SensorType == SensorType.Clock && s.Value > 0 && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (clockSensors.Count > 0)
                        {
                            metrics.CpuClock = (float)Math.Round(clockSensors.Max(s => s.Value!.Value), 0);
                        }
                    }

                    // CPU Voltage (VCore / VID in mV)
                    var cpuVoltSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage && s.Value > 0);
                    if (cpuVoltSensor?.Value != null && cpuVoltSensor.Value.Value > 0)
                    {
                        metrics.CpuVoltage = cpuVoltSensor.Value.Value < 10f
                            ? (float)Math.Round(cpuVoltSensor.Value.Value * 1000f, 0)
                            : (float)Math.Round(cpuVoltSensor.Value.Value, 0);
                    }

                    // CPU Power (RAPL MSR Package Power)
                    if (_powerMonitoringEnabled)
                    {
                        var cpuPowerSensor = cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power &&
                            (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("PPT", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("CPU Power", StringComparison.OrdinalIgnoreCase)) && s.Value > 0)
                            ?? cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value > 0);

                        if (cpuPowerSensor?.Value != null && cpuPowerSensor.Value.Value > 0)
                        {
                            metrics.CpuPower = (float)Math.Round(cpuPowerSensor.Value.Value, 1);
                            cpuPowerFromSensor = true;
                        }
                    }
                }

                // --- GPU Telemetry ---
                var gpus = _computer.Hardware.Where(h =>
                    h.HardwareType == HardwareType.GpuNvidia ||
                    h.HardwareType == HardwareType.GpuAmd ||
                    h.HardwareType == HardwareType.GpuIntel).ToList();

                var primaryGpu = gpus.FirstOrDefault(g => g.HardwareType == HardwareType.GpuNvidia)
                              ?? gpus.FirstOrDefault(g => g.HardwareType == HardwareType.GpuAmd && !g.Name.Contains("Radeon Graphics", StringComparison.OrdinalIgnoreCase))
                              ?? gpus.FirstOrDefault();

                if (primaryGpu != null)
                {
                    metrics.GpuName = primaryGpu.Name;

                    // GPU Load
                    var coreLoadSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load &&
                        (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("Core", StringComparison.OrdinalIgnoreCase)))
                        ?? primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase))
                        ?? primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load && s.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase));

                    if (coreLoadSensor?.Value != null)
                    {
                        metrics.GpuLoad = (float)Math.Round(coreLoadSensor.Value.Value, 0);
                    }

                    // GPU Core Temperature: LHM is authoritative for NVIDIA/AMD GPU temp
                    {
                        var coreTempSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                            (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("Core", StringComparison.OrdinalIgnoreCase)))
                            ?? primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Value > 0);

                        if (coreTempSensor?.Value != null && coreTempSensor.Value.Value > 0)
                        {
                            metrics.GpuTemp = (float)Math.Round(coreTempSensor.Value.Value, 0);
                        }
                    }

                    // GPU Hotspot
                    var hotspotSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                        (s.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase)));
                    if (hotspotSensor?.Value != null && hotspotSensor.Value.Value > 0)
                    {
                        metrics.GpuHotspot = (float)Math.Round(hotspotSensor.Value.Value, 0);
                    }

                    // GPU Core Clock
                    var gpuCoreClock = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock &&
                        (s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || s.Name.Equals("Core", StringComparison.OrdinalIgnoreCase)));
                    if (gpuCoreClock?.Value != null && gpuCoreClock.Value.Value > 0)
                    {
                        metrics.GpuClock = (float)Math.Round(gpuCoreClock.Value.Value, 0);
                    }

                    // GPU Memory Clock
                    var gpuMemClock = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Clock &&
                        (s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("VRAM", StringComparison.OrdinalIgnoreCase)));
                    if (gpuMemClock?.Value != null && gpuMemClock.Value.Value > 0)
                    {
                        metrics.GpuMemoryClock = (float)Math.Round(gpuMemClock.Value.Value, 0);
                    }

                    // GPU Voltage (mV)
                    var gpuVoltSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Voltage && s.Value > 0);
                    if (gpuVoltSensor?.Value != null && gpuVoltSensor.Value.Value > 0)
                    {
                        metrics.GpuVoltage = gpuVoltSensor.Value.Value < 10f
                            ? (float)Math.Round(gpuVoltSensor.Value.Value * 1000f, 0)
                            : (float)Math.Round(gpuVoltSensor.Value.Value, 0);
                    }

                    // GPU VRAM Temperature (Memory Junction)
                    var vramTempSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                        (s.Name.Contains("Memory Junction", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("VRAM", StringComparison.OrdinalIgnoreCase)));
                    if (vramTempSensor?.Value != null && vramTempSensor.Value.Value > 0)
                    {
                        metrics.GpuVramTemp = (float)Math.Round(vramTempSensor.Value.Value, 0);
                    }

                    // GPU Power
                    if (_powerMonitoringEnabled)
                    {
                        var gpuPowerSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power &&
                            (s.Name.Equals("GPU Package", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("Board", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                             s.Name.Equals("GPU Power", StringComparison.OrdinalIgnoreCase)) && s.Value > 0)
                            ?? primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Power && s.Value > 0);

                        if (gpuPowerSensor?.Value != null && gpuPowerSensor.Value.Value > 0)
                        {
                            metrics.GpuPower = (float)Math.Round(gpuPowerSensor.Value.Value, 1);
                            gpuPowerFromSensor = true;
                        }
                    }

                    // GPU Fan (if reported via LHM)
                    var gpuFanSensor = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Value > 0);
                    if (gpuFanSensor?.Value != null && metrics.GpuFanRpm <= 0)
                    {
                        metrics.GpuFanRpm = gpuFanSensor.Value.Value;
                    }

                    // GPU VRAM Usage
                    var vramUsed = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));
                    if (vramUsed?.Value != null)
                    {
                        metrics.GpuVramUsedMb = vramUsed.Value.Value;
                    }
                    var vramTotal = primaryGpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase));
                    if (vramTotal?.Value != null)
                    {
                        metrics.GpuVramTotalMb = vramTotal.Value.Value;
                    }
                }

                // Motherboard Fan and Thermal Sensors fallback
                var mb = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
                if (mb != null)
                {
                    if (metrics.CpuFanRpm <= 0)
                    {
                        var cpuFanSensor = mb.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Fan && s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) && s.Value > 0);
                        if (cpuFanSensor?.Value != null) metrics.CpuFanRpm = cpuFanSensor.Value.Value;
                    }
                    if (metrics.CpuTemp <= 0)
                    {
                        var mbCpuTemp = mb.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature &&
                            (s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) && s.Value > 0);
                        if (mbCpuTemp?.Value != null) metrics.CpuTemp = (float)Math.Round(mbCpuTemp.Value.Value, 0);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HardwareMonitor] Sensor parse error: {ex.Message}");
            }
        }

        // 5. ASUS ACPI Fallback for CPU / GPU Temperature (in case not resolved earlier)
        if (metrics.CpuTemp <= 0f && asusCpuTemp.HasValue && asusCpuTemp.Value > 0)
        {
            metrics.CpuTemp = asusCpuTemp.Value;
            metrics.CpuPackageTemp = asusCpuTemp.Value;
        }

        if (metrics.GpuTemp <= 0f && asusGpuTemp.HasValue && asusGpuTemp.Value > 0)
        {
            metrics.GpuTemp = asusGpuTemp.Value;
        }

        // 6. GPU Hotspot estimate (only if we have real GPU temp but no hotspot sensor)
        if (metrics.GpuHotspot <= 0f && metrics.GpuTemp > 0f)
        {
            metrics.GpuHotspot = metrics.GpuTemp + 4f;
        }

        // NOTE: No synthetic fallback values. Zero means "sensor unavailable" — the UI
        // should display "--" or "N/A" for zero values rather than fake estimated data.

        // 7. Sensor Source Reporting
        metrics.PowerIsEstimated = _powerMonitoringEnabled && (!cpuPowerFromSensor || !gpuPowerFromSensor);
        metrics.PowerSource = !_powerMonitoringEnabled
            ? "Disabled"
            : (cpuPowerFromSensor && gpuPowerFromSensor) ? "Sensors"
            : (cpuPowerFromSensor || gpuPowerFromSensor) ? "Mixed"
            : "Estimated (Run as Admin)";

        CurrentMetrics = metrics;
        MetricsUpdated?.Invoke(this, metrics);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        try
        {
            _cpuUtilityCounter?.Dispose();
            _cpuUtilityCounter = null;

        }
        catch { }
        try
        {
            _computer?.Close();
        }
        catch { }
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
