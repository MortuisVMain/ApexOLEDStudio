using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ApexOLEDStudio.Core.Drawing;
using ApexOLEDStudio.Core.Drivers;
using ApexOLEDStudio.Core.Models;
using ApexOLEDStudio.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace ApexOLEDStudio.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IHardwareMonitorService _hardwareService;
    private readonly ApexProHidDriver _hidDriver;
    private readonly BurnInGuard _burnInGuard;
    private readonly DispatcherTimer _renderTimer;

    private readonly NetworkMonitorService _networkService;
    private readonly KeyboardTrackerService _keyboardTracker;
    private readonly MediaSessionService _mediaService;
    private readonly AudioService _audioService;
    private readonly AppWatcherService _appWatcher;
    private readonly WeatherService _weatherService;
    private DateTime _volumeOverlayUntil = DateTime.MinValue;

    private readonly OledFrameBuffer _frameBuffer = new();
    private readonly WriteableBitmap _oledBitmap;

    // BUG FIX: Single stable ObservableCollection — never recreated on property access
    private readonly ObservableCollection<OledWidget> _widgets = new();

    [ObservableProperty]
    private OledLayout _currentLayout;

    [ObservableProperty]
    private OledWidget? _selectedWidget;
    private OledWidget? _observedWidget;

    [ObservableProperty]
    private bool _isApexConnected;

    [ObservableProperty]
    private bool _isLiveSyncEnabled = true;

    [ObservableProperty]
    private bool _isSimulatorMode = false;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _burnInStatus = "Shift: (0, 0)";

    [ObservableProperty]
    private bool _isEditorGridVisible = true;

    // Live Telemetry Readout
    [ObservableProperty]
    private HardwareMetrics _liveMetrics = new();

    // ── Simulator Sliders ──────────────────────────────────────────────
    [ObservableProperty] private float _simCpuLoad    = 65f;
    [ObservableProperty] private float _simCpuTemp    = 58f;
    [ObservableProperty] private float _simCpuPower   = 75f;
    [ObservableProperty] private float _simGpuLoad    = 88f;
    [ObservableProperty] private float _simGpuTemp    = 67f;
    [ObservableProperty] private float _simGpuHotspot = 78f;
    [ObservableProperty] private float _simGpuPower   = 220f;
    [ObservableProperty] private float _simVramTemp   = 82f;
    [ObservableProperty] private float _simRamPercent = 54f;
    [ObservableProperty] private float _simPing       = 24f;
    [ObservableProperty] private float _simApm        = 185f;

    // ── Presets System ────────────────────────────────────────────────
    public ObservableCollection<string> AvailablePresets { get; } = new()
    {
        "Dual CPU/GPU",
        "Power Station (Watts)",
        "Gamer Pro (Ping/Net)",
        "Media Station",
        "Gamer Minimal",
        "Dev Mode",
        "Clock & Time"
    };

    [ObservableProperty]
    private string _selectedPresetName = "Dual CPU/GPU";

    // ── Settings & WidgetType index (ComboBox enum bug fix) ──────────
    [ObservableProperty]
    private AppSettings _settings = new();

    /// <summary>
    /// Int index bound to WidgetType ComboBox via SelectedIndex.
    /// Avoids WPF enum-vs-string binding bug entirely.
    /// 0=Text, 1=ProgressBar, 2=Graph, 3=Line, 4=Gauge
    /// </summary>
    private int _selectedWidgetTypeIndex;
    public int SelectedWidgetTypeIndex
    {
        get => _selectedWidgetTypeIndex;
        set
        {
            if (SetProperty(ref _selectedWidgetTypeIndex, value))
                OnSelectedWidgetTypeIndexChanged(value);
        }
    }

    public WriteableBitmap OledBitmap => _oledBitmap;

    /// <summary>
    /// Stable ObservableCollection bound to the ListBox. Use SyncWidgets() after layout swap.
    /// </summary>
    public ObservableCollection<OledWidget> Widgets => _widgets;

    public MainViewModel()
    {
        // Load persisted user settings
        Settings = SettingsService.Load();
        IsEditorGridVisible = Settings.EditorGridEnabled;
        Settings.AutostartWithWindows = SettingsService.IsAutostartEnabled();
        if (SettingsService.LastError != null)
            StatusMessage = SettingsService.LastError;

        _currentLayout = OledLayout.CreateDefaultApexProSplit();
        SyncWidgets();
        _selectedWidget = _widgets.Count > 0 ? _widgets[0] : null;
        SyncWidgetTypeIndex();

        // Initialize 128x40 32-bit BGRA WriteableBitmap for instant UI preview
        _oledBitmap = new WriteableBitmap(
            OledFrameBuffer.Width,
            OledFrameBuffer.Height,
            96, 96,
            PixelFormats.Bgra32,
            null);

        _hardwareService = new HardwareMonitorService();
        _hidDriver = new ApexProHidDriver();
        _burnInGuard = new BurnInGuard(TimeSpan.FromMinutes(Settings.BurnInShiftMinutes));
        _networkService = new NetworkMonitorService();
        _keyboardTracker = new KeyboardTrackerService();
        _mediaService = new MediaSessionService();
        _audioService = new AudioService();
        _appWatcher = new AppWatcherService();
        _weatherService = new WeatherService();
        _hardwareService.ConfigureSensors(Settings);

        _audioService.VolumeChanged += (vol, isMuted) =>
        {
            if (Settings.EnableVolumeOverlay)
            {
                _volumeOverlayUntil = DateTime.UtcNow.AddMilliseconds(Settings.VolumeOverlayDurationMs);
            }
        };

        _appWatcher.ForegroundProcessChanged += procName =>
        {
            if (Settings.EnableAutoProfileSwitch && Settings.ProcessProfileRules.TryGetValue(procName, out var targetPreset))
            {
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ApplyPreset(targetPreset));
            }
        };

        _hardwareService.MetricsUpdated += (s, m) =>
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                LiveMetrics = m;
            });
        };

        // Win+L / console disconnect — auto-dim physical OLED
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Start internal hardware monitoring using configured interval
        _hardwareService.Start(TimeSpan.FromMilliseconds(Settings.PollingIntervalMs));
        _networkService.Start();
        _keyboardTracker.Start();
        _mediaService.Start();
        _audioService.Start(Settings.EnableMicMonitor);
        _appWatcher.Start();
        if (Settings.EnableWeather)
        {
            _weatherService.Start(Settings.WeatherCity, Settings.WeatherUpdateIntervalMinutes);
        }

        // Connect to Apex Pro
        TryConnectApex();

        // UI & Frame Rendering Loop (20 FPS for smooth preview)
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();
    }

    // ── Property change callbacks ─────────────────────────────────────

    /// <summary>When preset is changed from the dropdown, swap the layout.</summary>
    partial void OnSelectedPresetNameChanged(string value) => ApplyPreset(value);

    /// <summary>When the selected widget changes, sync the type-index ComboBox.</summary>
    partial void OnSelectedWidgetChanged(OledWidget? value)
    {
        if (_observedWidget != null)
            _observedWidget.PropertyChanged -= OnSelectedWidgetPropertyChanged;
        _observedWidget = value;
        if (_observedWidget != null)
        {
            _observedWidget.PropertyChanged += OnSelectedWidgetPropertyChanged;
            NormalizeSelectedWidget();
        }
        SyncWidgetTypeIndex();
    }

    private void OnSelectedWidgetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OledWidget.X) or nameof(OledWidget.Y) or nameof(OledWidget.Width) or nameof(OledWidget.Height))
            NormalizeSelectedWidget();
        else if (e.PropertyName == nameof(OledWidget.MinValue) && SelectedWidget != null && SelectedWidget.MinValue >= SelectedWidget.MaxValue)
            SelectedWidget.MaxValue = SelectedWidget.MinValue + 1;
        else if (e.PropertyName == nameof(OledWidget.MaxValue) && SelectedWidget != null && SelectedWidget.MaxValue <= SelectedWidget.MinValue)
            SelectedWidget.MinValue = SelectedWidget.MaxValue - 1;
    }

    /// <summary>When type-index ComboBox changes, write back to the widget enum.</summary>
    private void OnSelectedWidgetTypeIndexChanged(int value)
    {
        if (SelectedWidget == null) return;
        SelectedWidget.Type = value switch
        {
            1 => WidgetType.ProgressBar,
            2 => WidgetType.Graph,
            3 => WidgetType.Line,
            4 => WidgetType.Gauge,
            5 => WidgetType.Gif,
            _ => WidgetType.Text
        };
    }

    private void SyncWidgetTypeIndex()
    {
        int newIndex = SelectedWidget?.Type switch
        {
            WidgetType.ProgressBar => 1,
            WidgetType.Graph       => 2,
            WidgetType.Line        => 3,
            WidgetType.Gauge       => 4,
            WidgetType.Gif         => 5,
            _                      => 0
        };
        // Use SetProperty to go through the generated property properly
        SetProperty(ref _selectedWidgetTypeIndex, newIndex);
    }

    // ── Win+L / Session Lock Auto-Dim ─────────────────────────────────
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (!Settings.BlankOnWindowsLock) return;

        bool blanked = e.Reason is SessionSwitchReason.SessionLock
                                 or SessionSwitchReason.ConsoleDisconnect;
        _burnInGuard.SetBlanked(blanked);

        if (blanked && IsApexConnected)
        {
            // Send an all-black frame so the physical display goes dark
            var blank = new OledFrameBuffer();
            blank.Clear(false);
            _hidDriver.SendFrame(blank);
        }

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            BurnInStatus = blanked
                ? "⏸ Screen blanked (session locked)"
                : $"Pixel Shift: ({_burnInGuard.CurrentShift.dx}, {_burnInGuard.CurrentShift.dy})";
        });
    }

    private int _tickCount = 0;

    private void OnRenderTick(object? sender, EventArgs e)
    {
        // Skip expensive rendering while workstation is locked
        if (_burnInGuard.IsScreenBlanked) return;

        _tickCount++;

        // 1. Determine active metrics (Live vs Simulator)
        HardwareMetrics activeMetrics;
        if (IsSimulatorMode)
        {
            activeMetrics = new HardwareMetrics
            {
                Timestamp      = DateTime.Now,
                CpuLoad        = SimCpuLoad,
                CpuTemp        = SimCpuTemp,
                CpuPackageTemp = SimCpuTemp,
                CpuPower       = SimCpuPower,
                GpuLoad        = SimGpuLoad,
                GpuTemp        = SimGpuTemp,
                GpuHotspot     = SimGpuHotspot,
                GpuPower       = SimGpuPower,
                GpuVramTemp    = SimVramTemp,
                RamPercent     = SimRamPercent,
                RamUsedGb      = (SimRamPercent / 100f) * 32f,
                RamTotalGb     = 32f,
                PingMs           = (int)SimPing,
                DownloadSpeedKBs = 2450f,
                UploadSpeedKBs   = 420f,
                Apm              = (int)SimApm,
                KeystrokeCount   = 1450,
                MediaTitle       = "Master of Puppets",
                MediaArtist      = "Metallica",
                MediaStatus      = "▶",
                VolumeLevel      = 54f,
                IsVolumeMuted    = false,
                IsMicMuted       = false,
                IsCapsLock       = false,
                IsNumLock        = true,
                WeatherTemp      = "+19°C",
                WeatherCondition = "Clear"
            };
        }

        else
        {
            activeMetrics = LiveMetrics;
            activeMetrics.PingMs = _networkService.CurrentPingMs;
            activeMetrics.DownloadSpeedKBs = _networkService.CurrentDownloadKBs;
            activeMetrics.UploadSpeedKBs = _networkService.CurrentUploadKBs;
            activeMetrics.Apm = _keyboardTracker.CurrentApm;
            activeMetrics.KeystrokeCount = _keyboardTracker.TotalKeystrokes;
            activeMetrics.MediaTitle = _mediaService.TrackTitle;
            activeMetrics.MediaArtist = _mediaService.Artist;
            activeMetrics.MediaStatus = _mediaService.StatusSymbol;
            activeMetrics.VolumeLevel = _audioService.CurrentMasterVolume;
            activeMetrics.IsVolumeMuted = _audioService.IsMasterMuted;
            activeMetrics.IsMicMuted = _audioService.IsMicMuted;
            if (Settings.EnableLockKeys)
            {
                activeMetrics.IsCapsLock = LockKeysService.IsCapsLockOn;
                activeMetrics.IsNumLock = LockKeysService.IsNumLockOn;
                activeMetrics.IsScrollLock = LockKeysService.IsScrollLockOn;
            }
            if (Settings.EnableWeather)
            {
                activeMetrics.WeatherTemp = _weatherService.CurrentTemperature;
                activeMetrics.WeatherCondition = _weatherService.CurrentCondition;
            }
        }

        // 2. Check burn-in orbital shift
        var (shiftX, shiftY) = _burnInGuard.Tick(CurrentLayout.EnableBurnInProtection);
        BurnInStatus = $"Pixel Shift: ({shiftX}, {shiftY})";

        // 3. Render layout to internal framebuffer (supporting Volume HUD popup overlay & display inversion)
        bool volActive = Settings.EnableVolumeOverlay && DateTime.UtcNow < _volumeOverlayUntil;
        CurrentLayout.Render(_frameBuffer, activeMetrics, shiftX, shiftY, volActive, Settings.InvertDisplay);

        // 4. Update WPF WriteableBitmap — Electric Cyan on Deep Obsidian
        byte[] bgra = _frameBuffer.ToBgra32(onColor: 0xFF00E5FF, offColor: 0xFF0B0F17);
        _oledBitmap.WritePixels(
            new Int32Rect(0, 0, OledFrameBuffer.Width, OledFrameBuffer.Height),
            bgra,
            OledFrameBuffer.Width * 4,
            0);

        // 5. Send to physical keyboard dynamically based on configured HidSyncIntervalMs
        int hidSyncTicks = Math.Max(1, Settings.HidSyncIntervalMs / 50);
        if (_tickCount % hidSyncTicks == 0)
        {
            if (!IsApexConnected)
                TryConnectApex();

            if (IsApexConnected && IsLiveSyncEnabled)
            {
                bool sent = _hidDriver.SendFrame(_frameBuffer);
                if (!sent)
                {
                    IsApexConnected = false;
                    StatusMessage = $"Apex Pro connection lost: {_hidDriver.LastError}";
                }
                else
                {
                    StatusMessage = "Streaming to Apex Pro (Direct HID)";
                }
            }
        }
    }

    public void NormalizeSelectedWidget(bool snap = false)
    {
        if (SelectedWidget == null) return;
        WidgetEditorMath.ClampToCanvas(SelectedWidget);
        if (snap && Settings.EditorGridEnabled)
            WidgetEditorMath.SnapToCanvas(SelectedWidget, Settings.EditorGridStep);
    }

    [RelayCommand]
    public void ToggleEditorGrid()
    {
        IsEditorGridVisible = !IsEditorGridVisible;
        Settings.EditorGridEnabled = IsEditorGridVisible;
    }

    [RelayCommand]
    public void BringSelectedWidgetFront()
    {
        if (SelectedWidget == null) return;
        CurrentLayout.Widgets.Remove(SelectedWidget);
        CurrentLayout.Widgets.Add(SelectedWidget);
        ReindexWidgetOrder();
    }

    [RelayCommand]
    public void SendSelectedWidgetBack()
    {
        if (SelectedWidget == null) return;
        CurrentLayout.Widgets.Remove(SelectedWidget);
        CurrentLayout.Widgets.Insert(0, SelectedWidget);
        ReindexWidgetOrder();
    }

    [RelayCommand]
    public void ToggleSelectedWidgetLock()
    {
        if (SelectedWidget != null)
            SelectedWidget.IsLocked = !SelectedWidget.IsLocked;
    }

    private void ReindexWidgetOrder()
    {
        for (int index = 0; index < CurrentLayout.Widgets.Count; index++)
            CurrentLayout.Widgets[index].ZIndex = index;
        SyncWidgets();
    }

    [RelayCommand]
    public void ApplyPreset(string name)
    {
        CurrentLayout = name switch
        {
            "Power Station (Watts)" => OledLayout.CreatePowerStation(),
            "Gamer Pro (Ping/Net)"  => OledLayout.CreateGamerPro(),
            "Media Station"         => OledLayout.CreateMediaStation(),
            "Gamer Minimal"         => OledLayout.CreateGamerMinimal(),
            "Dev Mode"              => OledLayout.CreateDevMode(),
            "Clock & Time"          => OledLayout.CreateClockMedia(),
            _                       => OledLayout.CreateDefaultApexProSplit()
        };
        if (SelectedPresetName != name)
        {
            SelectedPresetName = name;
        }
        SyncWidgets();
        SelectedWidget = _widgets.Count > 0 ? _widgets[0] : null;
        StatusMessage = $"Preset loaded: {name}";
    }

    // ── Commands ──────────────────────────────────────────────────────

    [RelayCommand]
    public void TryConnectApex()
    {
        IsApexConnected = _hidDriver.Connect();
        StatusMessage = IsApexConnected
            ? "Connected to SteelSeries Apex Pro"
            : $"No Apex Pro detected: {_hidDriver.LastError}";
    }

    [RelayCommand]
    public void SendToKeyboard()
    {
        if (!IsApexConnected)
            TryConnectApex();

        if (IsApexConnected)
        {
            bool success = _hidDriver.SendFrame(_frameBuffer);
            StatusMessage = success ? "Frame sent to Apex Pro!" : "Failed to send frame";
        }
    }

    [RelayCommand]
    public void AddWidget()
    {
        AddPresetWidget("default");
    }

    [RelayCommand]
    public void AddPresetWidget(string presetKey)
    {
        int count = CurrentLayout.Widgets.Count + 1;
        (int w, int h, WidgetType type, string key, string template, string name, bool font, bool border, bool marquee) config = presetKey?.ToLowerInvariant() switch
        {
            "watts_total" or "watts" => (46, 10, WidgetType.Text, "total_power", "{total_power}W", $"Total Watts #{count}", false, false, false),
            "cpu_watts"   => (52, 10, WidgetType.Text, "cpu_power", "CPU:{cpu_power}W", $"CPU Watts #{count}", false, false, false),
            "gpu_watts"   => (52, 10, WidgetType.Text, "gpu_power", "GPU:{gpu_power}W", $"GPU Watts #{count}", false, false, false),
            "dual_temp"   => (62, 10, WidgetType.Text, "cpu_temp", "C:{cpu_temp}° G:{gpu_temp}°", $"Dual Temp #{count}", false, false, false),
            "cpu_bar"     => (52, 6,  WidgetType.ProgressBar, "cpu_load", "{cpu_load}%", $"CPU Bar #{count}", false, false, false),
            "gpu_bar"     => (52, 6,  WidgetType.ProgressBar, "gpu_load", "{gpu_load}%", $"GPU Bar #{count}", false, false, false),
            "power_gauge" => (22, 22, WidgetType.Gauge, "total_power", "{total_power}", $"Power Dial #{count}", false, false, false),
            "cpu_graph"   => (34, 16, WidgetType.Graph, "cpu_load", "{cpu_load}%", $"CPU Graph #{count}", false, false, false),
            "clock"       => (32, 10, WidgetType.Text, "time", "{time_short}", $"Clock #{count}", false, false, false),
            "now_playing" => (74, 10, WidgetType.Text, "media_title", "{media_track}", $"Now Playing #{count}", true, false, true),
            "bongo_cat"   => (36, 24, WidgetType.Gif, "apm", "", $"Bongo Cat #{count}", false, false, false),
            "net_speed"   => (60, 10, WidgetType.Text, "ping", "{ping}ms {net_down}", $"Network #{count}", false, false, false),
            "lock_keys"   => (46, 10, WidgetType.Text, "caps", "{caps} {num}", $"Lock Keys #{count}", false, false, false),
            "divider_vert"  => (1, 40, WidgetType.Line, "", "", $"Split Line #{count}", false, false, false),
            "divider_horiz" => (128, 1, WidgetType.Line, "", "", $"Divider #{count}", false, false, false),
            _ => (40, 10, WidgetType.Text, "cpu_load", "{cpu_load}%", $"Widget #{count}", false, false, false)
        };

        var (w, h, type, key, template, name, font, border, marquee) = config;
        var (posX, posY) = CalculateSmartPlacement(w, h);

        var newWidget = new OledWidget
        {
            Name = name,
            Type = type,
            X = posX,
            Y = posY,
            Width = w,
            Height = h,
            MetricKey = key,
            FormatTemplate = template,
            CompactFont = font,
            Bordered = border,
            EnableMarquee = marquee
        };

        CurrentLayout.Widgets.Add(newWidget);
        _widgets.Add(newWidget);
        SelectedWidget = newWidget;
        StatusMessage = $"Added {name} at ({posX}, {posY})";
    }

    private (int x, int y) CalculateSmartPlacement(int reqWidth, int reqHeight)
    {
        if (CurrentLayout.Widgets.Count == 0)
            return (2, 2);

        // Scan 3-4px grid across 128x40 display canvas for an unoccupied rectangle
        for (int y = 2; y <= 40 - reqHeight; y += 3)
        {
            for (int x = 2; x <= 128 - reqWidth; x += 4)
            {
                bool collides = false;
                foreach (var w in CurrentLayout.Widgets)
                {
                    if (!w.Enabled) continue;
                    // Check AABB collision
                    if (x < w.X + Math.Max(1, w.Width) && x + reqWidth > w.X &&
                        y < w.Y + Math.Max(1, w.Height) && y + reqHeight > w.Y)
                    {
                        collides = true;
                        break;
                    }
                }
                if (!collides)
                    return (x, y);
            }
        }

        // Fallback: place below or next to the last widget
        var last = CurrentLayout.Widgets[^1];
        int nextX = Math.Min(128 - reqWidth, last.X + last.Width + 2);
        int nextY = Math.Min(40 - reqHeight, last.Y);
        return (Math.Max(0, nextX), Math.Max(0, nextY));
    }

    [RelayCommand]
    public void DeleteSelectedWidget()
    {
        if (SelectedWidget != null)
        {
            CurrentLayout.Widgets.Remove(SelectedWidget);
            _widgets.Remove(SelectedWidget);
            SelectedWidget = _widgets.Count > 0 ? _widgets[0] : null;
        }
    }

    [RelayCommand]
    public void ResetDefaultLayout()
    {
        CurrentLayout = OledLayout.CreateDefaultApexProSplit();
        SyncWidgets();
        SelectedWidget = _widgets.Count > 0 ? _widgets[0] : null;
        StatusMessage = "Reset to default dual CPU/GPU split";
    }

    [RelayCommand]
    public void SaveProfile()
    {
        try
        {
            string dir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexOLEDStudio");
            string file = Path.Combine(dir, "default_profile.json");
            LayoutSerializer.SaveToFile(CurrentLayout, file);
            StatusMessage = $"Profile saved to {file}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving: {ex.Message}";
        }
    }

    [RelayCommand]
    public void LoadProfile()
    {
        try
        {
            string dir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexOLEDStudio");
            string file = Path.Combine(dir, "default_profile.json");
            if (File.Exists(file))
            {
                CurrentLayout = LayoutSerializer.LoadFromFile(file);
                SyncWidgets();
                SelectedWidget = _widgets.Count > 0 ? _widgets[0] : null;
                StatusMessage = LayoutSerializer.LastError ?? "Profile loaded";
            }
            else
            {
                StatusMessage = "No saved profile found";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SaveFrameScreenshot()
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ApexOLED");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"oled_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            using var fileStream = new FileStream(file, FileMode.Create);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_oledBitmap));
            encoder.Save(fileStream);
            StatusMessage = $"Saved: {Path.GetFileName(file)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Screenshot error: {ex.Message}";
        }
    }

    /// <summary>
    /// Rebuilds the stable _widgets collection from CurrentLayout.Widgets.
    /// Must be called whenever CurrentLayout is replaced.
    /// </summary>
    public void SyncWidgets()
    {
        _widgets.Clear();
        foreach (var w in CurrentLayout.Widgets)
            _widgets.Add(w);
    }

    [RelayCommand]
    public void SaveSettings()
    {
        try
        {
            SettingsService.Save(Settings);
            _hardwareService.ConfigureSensors(Settings);
            _hardwareService.UpdateInterval(TimeSpan.FromMilliseconds(Settings.PollingIntervalMs));
            _burnInGuard.ShiftInterval = TimeSpan.FromMinutes(Settings.BurnInShiftMinutes);
            _audioService.ConfigureMicMonitor(Settings.EnableMicMonitor);
            _weatherService.Stop();
            if (Settings.EnableWeather)
                _weatherService.Start(Settings.WeatherCity, Settings.WeatherUpdateIntervalMinutes);
            StatusMessage = Settings.AutostartWithWindows
                ? "Settings applied & saved — autostart enabled"
                : "Settings applied & saved";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
    }

    [RelayCommand]
    public void EnableAllWidgets()
    {
        foreach (var w in CurrentLayout.Widgets)
            w.Enabled = true;
        StatusMessage = "All widgets enabled";
    }

    [RelayCommand]
    public void DisableAllWidgets()
    {
        foreach (var w in CurrentLayout.Widgets)
            w.Enabled = false;
        StatusMessage = "All widgets disabled";
    }

    [RelayCommand]
    public void DuplicateSelectedWidget()
    {
        if (SelectedWidget == null) return;
        var clone = new OledWidget
        {
            Name = SelectedWidget.Name + " (Copy)",
            Type = SelectedWidget.Type,
            Enabled = SelectedWidget.Enabled,
            IsLocked = SelectedWidget.IsLocked,
            ZIndex = SelectedWidget.ZIndex,
            X = Math.Clamp(SelectedWidget.X + 2, 0, 120),
            Y = Math.Clamp(SelectedWidget.Y + 2, 0, 32),
            Width = SelectedWidget.Width,
            Height = SelectedWidget.Height,
            MetricKey = SelectedWidget.MetricKey,
            FormatTemplate = SelectedWidget.FormatTemplate,
            CompactFont = SelectedWidget.CompactFont,
            FontScale = SelectedWidget.FontScale,
            Bordered = SelectedWidget.Bordered,
            MinValue = SelectedWidget.MinValue,
            MaxValue = SelectedWidget.MaxValue,
            EnableMarquee = SelectedWidget.EnableMarquee,
            MarqueeSpeed = SelectedWidget.MarqueeSpeed,
            GifPath = SelectedWidget.GifPath
        };
        CurrentLayout.Widgets.Add(clone);
        _widgets.Add(clone);
        SelectedWidget = clone;
        StatusMessage = $"Duplicated widget: {clone.Name}";
    }

    [RelayCommand]
    public void CycleNextPreset()
    {
        if (AvailablePresets.Count == 0) return;
        int idx = AvailablePresets.IndexOf(SelectedPresetName);
        int nextIdx = (idx + 1) % AvailablePresets.Count;
        ApplyPreset(AvailablePresets[nextIdx]);
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _renderTimer.Stop();
        _hardwareService.Dispose();
        _hidDriver.Dispose();
        _networkService.Dispose();
        _keyboardTracker.Dispose();
        _mediaService.Dispose();
        _audioService.Dispose();
        _appWatcher.Dispose();
        _weatherService.Dispose();
    }
}
