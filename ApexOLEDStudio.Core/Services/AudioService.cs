using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ApexOLEDStudio.Core.Services;

/// <summary>
/// Native Windows CoreAudio master volume & microphone monitor.
/// Zero external dependencies — direct COM Interop with IAudioEndpointVolume.
/// </summary>
public sealed class AudioService : IDisposable
{
    private CancellationTokenSource? _cts;
    private float _lastVolume = -1f;
    private bool _lastMute = false;
    private volatile bool _micMonitoringEnabled = true;

    public float CurrentMasterVolume { get; private set; } = 50f; // 0..100
    public bool  IsMasterMuted { get; private set; } = false;
    public bool  IsMicMuted { get; private set; } = false;

    /// <summary>Fired whenever volume level or mute state changes (e.g. Apex volume wheel rotation).</summary>
    public event Action<float, bool>? VolumeChanged;

    public void Start(bool enableMicMonitor = true)
    {
        _micMonitoringEnabled = enableMicMonitor;
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    PollAudioState();
                    await Task.Delay(100, token); // 10 Hz poll for instant volume wheel response
                }

                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AudioService] Error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }, token);
    }

    public void ConfigureMicMonitor(bool enabled) => _micMonitoringEnabled = enabled;

    private void PollAudioState()
    {
        // 1. Master Output (Speakers / Headphones)
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
            if (device != null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out var endpointObj);
                if (endpointObj is IAudioEndpointVolume volume)
                {
                    volume.GetMasterVolumeLevelScalar(out float level);
                    volume.GetMute(out bool isMuted);

                    float volPercent = Math.Clamp(level * 100f, 0f, 100f);
                    CurrentMasterVolume = volPercent;
                    IsMasterMuted = isMuted;

                    if (Math.Abs(volPercent - _lastVolume) > 0.5f || isMuted != _lastMute)
                    {
                        bool wasInitial = _lastVolume < 0f;
                        _lastVolume = volPercent;
                        _lastMute = isMuted;

                        if (!wasInitial)
                        {
                            VolumeChanged?.Invoke(volPercent, isMuted);
                        }
                    }
                    Marshal.ReleaseComObject(volume);
                }
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(enumerator);
        }
        catch
        {
            // Audio service may be temporarily unavailable or sleeping
        }

        if (!_micMonitoringEnabled)
        {
            IsMicMuted = false;
            return;
        }

        // 2. Default Input (Microphone)
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eCommunications, out var micDevice);
            if (micDevice != null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                micDevice.Activate(ref iid, CLSCTX.ALL, IntPtr.Zero, out var endpointObj);
                if (endpointObj is IAudioEndpointVolume micVolume)
                {
                    micVolume.GetMute(out bool isMicMuted);
                    IsMicMuted = isMicMuted;
                    Marshal.ReleaseComObject(micVolume);
                }
                Marshal.ReleaseComObject(micDevice);
            }
            Marshal.ReleaseComObject(enumerator);
        }
        catch
        {
            // Microphone not connected or permission restricted
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    // ── COM Declarations ──────────────────────────────────────────────────
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }

    [Flags]
    private enum CLSCTX { INPROC_SERVER = 0x1, INPROC_HANDLER = 0x2, LOCAL_SERVER = 0x4, ALL = 0x17 }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, CLSCTX clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr client);
        int UnregisterControlChangeNotify(IntPtr client);
        int GetChannelCount(out int channelCount);
        int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint channelNumber, float levelDb, ref Guid eventContext);
        int SetChannelVolumeLevelScalar(uint channelNumber, float level, ref Guid eventContext);
        int GetChannelVolumeLevel(uint channelNumber, out float levelDb);
        int GetChannelVolumeLevelScalar(uint channelNumber, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
    }
}
