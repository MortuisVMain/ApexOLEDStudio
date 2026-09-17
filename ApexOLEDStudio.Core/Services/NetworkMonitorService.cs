using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ApexOLEDStudio.Core.Services;

public interface INetworkMonitorService : IDisposable
{
    int CurrentPingMs { get; }
    float CurrentDownloadKBs { get; }
    float CurrentUploadKBs { get; }
    void Start();
    void Stop();
}

public sealed class NetworkMonitorService : INetworkMonitorService
{
    private CancellationTokenSource? _cts;
    private long _prevBytesRecv = -1;
    private long _prevBytesSent = -1;
    private DateTime _prevTime = DateTime.UtcNow;

    public int CurrentPingMs { get; private set; } = 0;
    public float CurrentDownloadKBs { get; private set; } = 0f;
    public float CurrentUploadKBs { get; private set; } = 0f;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            var pingSender = new Ping();
            int tick = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    tick++;

                    // 1. Traffic calculation every 1s
                    CalculateBandwidth();

                    // 2. Ping check every 2s
                    if (tick % 2 == 0)
                    {
                        try
                        {
                            var reply = await pingSender.SendPingAsync("8.8.8.8", 600);
                            if (reply.Status == IPStatus.Success)
                                CurrentPingMs = (int)reply.RoundtripTime;
                            else
                                CurrentPingMs = -1;
                        }
                        catch
                        {
                            CurrentPingMs = -1;
                        }
                    }

                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NetworkMonitor] Error: {ex.Message}");
                    await Task.Delay(2000, token);
                }
            }
        }, token);
    }

    private void CalculateBandwidth()
    {
        try
        {
            long totalRecv = 0;
            long totalSent = 0;

            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var stats = ni.GetIPStatistics();
                totalRecv += stats.BytesReceived;
                totalSent += stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            var elapsed = (now - _prevTime).TotalSeconds;

            if (_prevBytesRecv >= 0 && elapsed > 0)
            {
                long deltaRecv = Math.Max(0, totalRecv - _prevBytesRecv);
                long deltaSent = Math.Max(0, totalSent - _prevBytesSent);

                CurrentDownloadKBs = (float)((deltaRecv / 1024.0) / elapsed);
                CurrentUploadKBs   = (float)((deltaSent / 1024.0) / elapsed);
            }

            _prevBytesRecv = totalRecv;
            _prevBytesSent = totalSent;
            _prevTime = now;
        }
        catch
        {
            // Non-critical network query failure
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
    }
}
