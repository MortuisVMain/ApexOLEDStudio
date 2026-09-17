using System.IO;
using ApexOLEDStudio.Core.Drawing;
using ApexOLEDStudio.Core.Drivers;
using ApexOLEDStudio.Core.Models;
using ApexOLEDStudio.Core.Services;
using Xunit;

namespace ApexOLEDStudio.Tests;

public class LayoutTests
{
    [Fact]
    public void FormatTemplate_ReplacesCpuAndGpuTokens()
    {
        var metrics = new HardwareMetrics
        {
            CpuLoad    = 45.2f,
            CpuTemp    = 58.7f,
            GpuLoad    = 98.1f,
            GpuTemp    = 67.4f,
            GpuHotspot = 79.9f
        };

        string result = metrics.FormatTemplate("CPU: {cpu_load}% GPU: {gpu_load}% HS: {gpu_hotspot}C");

        Assert.Equal("CPU: 45% GPU: 98% HS: 80C", result);
    }

    [Fact]
    public void FormatTemplate_ReplacesExtendedTokens()
    {
        var metrics = new HardwareMetrics
        {
            CpuPower    = 95.3f,
            GpuVramTemp = 84.6f,
            GpuFanRpm   = 1850f
        };

        string result = metrics.FormatTemplate("PWR:{cpu_power}W VRAM:{gpu_vram_temp}C FAN:{gpu_fan}RPM");

        Assert.Equal("PWR:95W VRAM:85C FAN:1850RPM", result);
    }

    [Fact]
    public void FormatTemplate_ReplacesTotalPowerToken()
    {
        var metrics = new HardwareMetrics
        {
            CpuPower = 65.4f,
            GpuPower = 220.6f
        };

        string result = metrics.FormatTemplate("Total: {total_power}W");

        // 65.4 + 220.6 = 286.0 -> 286
        Assert.Equal("Total: 286W", result);
    }

    [Fact]
    public void GetNumericValue_ReturnsCorrectPowerValues()
    {
        var metrics = new HardwareMetrics
        {
            CpuPower = 70.0f,
            GpuPower = 250.0f
        };

        Assert.Equal(70.0f, metrics.GetNumericValue("cpu_power"));
        Assert.Equal(250.0f, metrics.GetNumericValue("gpu_power"));
        Assert.Equal(320.0f, metrics.GetNumericValue("total_power"));
    }

    [Fact]
    public void FormatTemplate_ReplacesNetworkApmAndMediaTokens()
    {
        var metrics = new HardwareMetrics
        {
            PingMs            = 24,
            DownloadSpeedKBs  = 5200f,
            UploadSpeedKBs    = 1100f,
            Apm               = 235,
            KeystrokeCount    = 1420,
            MediaTitle        = "Blinding Lights",
            MediaArtist       = "The Weeknd",
            MediaStatus       = "▶"
        };

        string result = metrics.FormatTemplate("{ping}ms D:{net_down} APM:{apm} {media_status} {media_artist} - {media_title}");

        Assert.Equal("24ms D:5.1MB/s APM:235 ▶ The Weeknd - Blinding Lights", result);
    }

    [Fact]
    public void AllPresetLayouts_GenerateValidWidgetsWithinBounds()
    {
        var presets = new List<OledLayout>
        {
            OledLayout.CreateDefaultApexProSplit(),
            OledLayout.CreatePowerStation(),
            OledLayout.CreateGamerMinimal(),
            OledLayout.CreateDevMode(),
            OledLayout.CreateClockMedia(),
            OledLayout.CreateGamerPro(),
            OledLayout.CreateMediaStation()
        };

        foreach (var layout in presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(layout.Name));
            Assert.NotEmpty(layout.Widgets);

            foreach (var w in layout.Widgets)
            {
                Assert.True(w.X >= 0 && w.X < 128, $"Widget '{w.Name}' in preset '{layout.Name}' has invalid X={w.X}");
                Assert.True(w.Y >= 0 && w.Y < 40, $"Widget '{w.Name}' in preset '{layout.Name}' has invalid Y={w.Y}");
                Assert.True(w.Width > 0, $"Widget '{w.Name}' in preset '{layout.Name}' has non-positive Width={w.Width}");
                Assert.True(w.Height > 0, $"Widget '{w.Name}' in preset '{layout.Name}' has non-positive Height={w.Height}");
            }
        }
    }

    [Fact]
    public void ThermalAlert_TriggersWhenTempExceedsThreshold()
    {
        var layout = new OledLayout
        {
            EnableThermalAlert = true,
            ThermalAlertCpuThreshold = 85f,
            ThermalAlertGpuThreshold = 80f
        };

        var normalMetrics = new HardwareMetrics { CpuTemp = 60f, GpuTemp = 65f };
        Assert.False(layout.IsThermalAlertActive(normalMetrics));

        var hotCpuMetrics = new HardwareMetrics { CpuTemp = 88f, GpuTemp = 65f };
        Assert.True(layout.IsThermalAlertActive(hotCpuMetrics));

        var hotGpuMetrics = new HardwareMetrics { CpuTemp = 60f, GpuTemp = 82f };
        Assert.True(layout.IsThermalAlertActive(hotGpuMetrics));
    }

    [Fact]
    public void Render_GamerProAndMediaStation_ExecutesWithoutError()
    {
        var metrics = new HardwareMetrics
        {
            PingMs = 18,
            DownloadSpeedKBs = 2400f,
            UploadSpeedKBs = 350f,
            Apm = 210,
            KeystrokeCount = 950,
            MediaTitle = "Starboy",
            MediaArtist = "The Weeknd",
            MediaStatus = "▶",
            CpuLoad = 55f,
            GpuLoad = 70f
        };

        var fb = new OledFrameBuffer();
        var gamerPro = OledLayout.CreateGamerPro();
        gamerPro.Render(fb, metrics);

        byte[] raw = fb.ToRawApexBytes();
        Assert.Equal(OledFrameBuffer.Width * OledFrameBuffer.Height / 8, raw.Length);

        var mediaStation = OledLayout.CreateMediaStation();
        mediaStation.Render(fb, metrics);

        byte[] hid = fb.ToApexHidPacket();
        Assert.Equal(643, hid.Length);
    }

    [Fact]
    public void GpuDelta_CalculatesAccuratelyAndTriggersAlert()
    {
        var metrics = new HardwareMetrics
        {
            GpuTemp = 65f,
            GpuHotspot = 88f // Delta = 23°C
        };

        Assert.Equal(23f, metrics.GpuDelta);
        Assert.Equal("23°C", metrics.FormatTemplate("{gpu_delta}"));

        var layout = new OledLayout
        {
            EnableThermalAlert = true,
            ThermalAlertCpuThreshold = 95f,
            ThermalAlertGpuThreshold = 90f,
            EnableGpuDeltaAlert = true,
            GpuDeltaThreshold = 20f
        };

        Assert.True(layout.IsThermalAlertActive(metrics));

        // When delta alert disabled, should not trigger
        layout.EnableGpuDeltaAlert = false;
        Assert.False(layout.IsThermalAlertActive(metrics));
    }

    [Fact]
    public void LockKeys_And_Audio_TokenReplacement_Works()
    {
        var metrics = new HardwareMetrics
        {
            VolumeLevel = 75f,
            IsVolumeMuted = false,
            IsMicMuted = false,
            IsCapsLock = true,
            IsNumLock = true,
            IsScrollLock = false,
            WeatherTemp = "+21°C",
            WeatherCondition = "Sunny"
        };

        string formatted = metrics.FormatTemplate("V:{vol} C:{caps} N:{num} S:{scroll} M:{mic} W:{weather_temp} {weather_cond}");
        Assert.Equal("V:75% C:CAPS N:NUM S: M:ON W:+21°C Sunny", formatted);

        // When muted
        metrics.IsVolumeMuted = true;
        metrics.IsMicMuted = true;
        metrics.IsCapsLock = false;
        string mutedFormatted = metrics.FormatTemplate("V:{vol} C:{caps} M:{mic}");
        Assert.Equal("V:MUTED C: M:MUTED", mutedFormatted);
    }

    [Fact]
    public void VolumeOverlay_DrawsProminentHudWhenActive()
    {
        var metrics = new HardwareMetrics
        {
            VolumeLevel = 60f,
            IsVolumeMuted = false
        };

        var fb = new OledFrameBuffer();
        var layout = OledLayout.CreateDefaultApexProSplit();

        layout.Render(fb, metrics, volumeOverlayActive: true);

        // Buffer must have pixels set for the Volume HUD
        byte[] raw = fb.ToRawApexBytes();
        bool anySet = false;
        foreach (byte b in raw)
        {
            if (b != 0) { anySet = true; break; }
        }
        Assert.True(anySet, "Volume HUD should set pixels in the frame buffer");
    }

    [Fact]
    public void GifAnimationService_GeneratesValidMascotFrames()
    {
        var frames = ApexOLEDStudio.Core.Services.GifAnimationService.GenerateDefaultMascotFrames(32, 20);
        Assert.Equal(4, frames.Count);
        Assert.Equal(32, frames[0].GetLength(0));
        Assert.Equal(20, frames[0].GetLength(1));

        var widget = new OledWidget
        {
            Name = "Mascot",
            Type = WidgetType.Gif,
            X = 0,
            Y = 0,
            Width = 32,
            Height = 20
        };

        var fb = new OledFrameBuffer();
        var metrics = new HardwareMetrics();
        widget.Render(fb, metrics);

        byte[] raw = fb.ToRawApexBytes();
        bool hasMascotPixels = false;
        foreach (byte b in raw)
        {
            if (b != 0) { hasMascotPixels = true; break; }
        }
        Assert.True(hasMascotPixels, "Gif widget should render mascot pixels");
    }

    [Fact]
    public void Marquee_TextWidget_DoesNotThrowOnRender()
    {
        var widget = new OledWidget
        {
            Name = "Long Track",
            Type = WidgetType.Text,
            X = 0,
            Y = 0,
            Width = 20, // narrow width
            EnableMarquee = true,
            MarqueeSpeed = 2,
            FormatTemplate = "Extremely Long Track Title That Exceeds Width"
        };

        var fb = new OledFrameBuffer();
        var metrics = new HardwareMetrics();

        // Render multiple frames to advance marquee
        for (int i = 0; i < 10; i++)
        {
            widget.Render(fb, metrics);
        }

        byte[] raw = fb.ToRawApexBytes();
        Assert.Equal(640, raw.Length);
    }

    [Fact]
    public void LayoutSerializer_RoundTripsDefaultProfile()
    {
        var layout = OledLayout.CreateDefaultApexProSplit();
        string json = LayoutSerializer.Serialize(layout);
        var restored = LayoutSerializer.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal(layout.Name, restored.Name);
        Assert.Equal(layout.Widgets.Count, restored.Widgets.Count);
        Assert.Equal(layout.Widgets[0].Name, restored.Widgets[0].Name);
    }

    [Fact]
    public void LayoutValidation_RejectsWidgetsOutsideDisplay()
    {
        var layout = new OledLayout
        {
            Widgets = new List<OledWidget>
            {
                new() { Name = "Invalid", X = 120, Y = 0, Width = 16, Height = 8 }
            }
        };

        Assert.Contains(layout.Validate(), error => error.Contains("outside"));
    }

    [Fact]
    public void ApexProtocolProfile_ValidatesCapabilitiesAndDimensions()
    {
        var profile = new ApexProProtocolProfile();
        var valid = new DeviceDescriptor(
            "hid://test", ApexProHidDriver.SteelSeriesVendorId, 0x1610, "Apex Pro",
            128, 40, DeviceCapabilities.OledDisplay | DeviceCapabilities.FeatureReports);

        Assert.True(profile.Validate(valid).IsSupported);
        Assert.False(profile.Validate(valid with { DisplayWidth = 64 }).IsSupported);
        Assert.False(profile.Validate(valid with { Capabilities = DeviceCapabilities.OledDisplay }).IsSupported);
    }

    [Fact]
    public void WidgetEditorMath_ClampsAndSnapsToCanvas()
    {
        var widget = new OledWidget { X = 127, Y = 39, Width = 80, Height = 30 };

        WidgetEditorMath.ClampToCanvas(widget);
        Assert.Equal(48, widget.X);
        Assert.Equal(10, widget.Y);

        widget.X = 7;
        widget.Y = 5;
        WidgetEditorMath.SnapToCanvas(widget, 4);
        Assert.Equal(8, widget.X);
        Assert.Equal(4, widget.Y);
    }

    [Fact]
    public void WidgetModel_EditorPropertiesRaiseChangesAndRemainJsonCompatible()
    {
        var widget = new OledWidget();
        var changed = new List<string>();
        widget.PropertyChanged += (_, args) => changed.Add(args.PropertyName ?? string.Empty);

        widget.IsLocked = true;
        widget.ZIndex = 4;

        Assert.Contains(nameof(OledWidget.IsLocked), changed);
        Assert.Contains(nameof(OledWidget.ZIndex), changed);
        var json = System.Text.Json.JsonSerializer.Serialize(widget);
        Assert.Contains("IsLocked", json);
        Assert.Contains("ZIndex", json);
    }

    [Fact]
    public void ProfileConfiguration_RoundTripsSettingsAndLayout()
    {
        var source = new ProfileConfiguration
        {
            Settings = new AppSettings { WeatherCity = "Tokyo", EnablePowerMonitoring = false },
            Layout = OledLayout.CreatePowerStation()
        };

        var restored = ProfileConfigurationService.Import(ProfileConfigurationService.Export(source));

        Assert.Equal("Tokyo", restored.Settings.WeatherCity);
        Assert.False(restored.Settings.EnablePowerMonitoring);
        Assert.Equal(source.Layout.Name, restored.Layout.Name);
    }

    [Fact]
    public void HardwareMonitor_DisablingPowerMonitoringReportsDisabledSource()
    {
        using var service = new HardwareMonitorService();
        service.ConfigureSensors(new AppSettings { EnablePowerMonitoring = false });
        service.PollHardware();

        Assert.Equal("Disabled", service.CurrentMetrics.PowerSource);
        Assert.False(service.CurrentMetrics.PowerIsEstimated);
    }

    [Fact]
    public void BurnInGuard_ShiftsPixelsPeriodically()
    {
        var guard = new BurnInGuard(TimeSpan.FromMilliseconds(10));
        var initialShift = guard.Tick(enabled: true);

        System.Threading.Thread.Sleep(20);
        var nextShift = guard.Tick(enabled: true);

        Assert.NotEqual(initialShift, nextShift);
    }

    [Fact]
    public void BurnInGuard_CompletesFullFourPhaseCycle()
    {
        var guard = new BurnInGuard(TimeSpan.FromMilliseconds(5));
        var shifts = new HashSet<(int dx, int dy)>();

        for (int i = 0; i < 8; i++)
        {
            System.Threading.Thread.Sleep(10);
            shifts.Add(guard.Tick(enabled: true));
        }

        // All 4 orbital positions must have been visited: (0,0),(1,0),(1,1),(0,1)
        Assert.True(shifts.Count == 4,
            $"Expected 4 unique shift positions, got {shifts.Count}: {string.Join(", ", shifts)}");
    }

    [Fact]
    public void BurnInGuard_DisabledAlwaysReturnsZeroShift()
    {
        var guard = new BurnInGuard(TimeSpan.FromMilliseconds(1));
        System.Threading.Thread.Sleep(10);

        var shift = guard.Tick(enabled: false);

        Assert.Equal((0, 0), shift);
    }

    [Fact]
    public void BurnInGuard_SetBlanked_ControlsIsScreenBlanked()
    {
        var guard = new BurnInGuard();
        Assert.False(guard.IsScreenBlanked);

        guard.SetBlanked(true);
        Assert.True(guard.IsScreenBlanked);

        guard.SetBlanked(false);
        Assert.False(guard.IsScreenBlanked);
    }

    [Fact]
    public void DrawProgressBar_AtZeroPercent_OnlyBorderPixelsSet()
    {
        var buffer = new OledFrameBuffer();
        buffer.DrawProgressBar(0, 0, 20, 8, percent: 0f, bordered: true);

        // Inner fill should be empty — pixel at (1,1) should be OFF
        Assert.False(buffer.GetPixel(1, 1));
        // Border corners must be ON
        Assert.True(buffer.GetPixel(0, 0));
        Assert.True(buffer.GetPixel(19, 0));
    }

    [Fact]
    public void DrawProgressBar_AtHundredPercent_InnerAreaFilled()
    {
        var buffer = new OledFrameBuffer();
        buffer.DrawProgressBar(0, 0, 20, 8, percent: 100f, bordered: true);

        // Inner pixels should be ON at 100%
        Assert.True(buffer.GetPixel(1, 1));
        Assert.True(buffer.GetPixel(18, 6));
    }

    [Fact]
    public void DrawGraph_AccumulatesHistoryAndRendersBaseline()
    {
        var buffer = new OledFrameBuffer();
        var history = new List<float> { 0f, 50f, 100f, 50f, 0f };

        buffer.DrawGraph(0, 0, 30, 20, history, min: 0, max: 100);

        // Baseline (y=19) must be set
        Assert.True(buffer.GetPixel(0, 19));
        Assert.True(buffer.GetPixel(29, 19));
    }

    [Fact]
    public void HardwareMonitorService_UpdateInterval_ChangesIntervalSafely()
    {
        using var service = new HardwareMonitorService();
        service.Start(TimeSpan.FromMilliseconds(500));
        Assert.True(service.IsRunning);

        // Update interval to 250ms
        service.UpdateInterval(TimeSpan.FromMilliseconds(250));
        Assert.True(service.IsRunning);

        service.Stop();
        Assert.False(service.IsRunning);
    }
    [Fact]
    public void OledWidget_Enabled_False_DoesNotRender_ToBuffer()
    {
        var buffer = new OledFrameBuffer();
        var metrics = new HardwareMetrics { CpuLoad = 50f };
        var widget = new OledWidget
        {
            Name = "Disabled Bar",
            Type = WidgetType.ProgressBar,
            X = 0,
            Y = 0,
            Width = 20,
            Height = 8,
            Enabled = false
        };

        widget.Render(buffer, metrics);

        // Every pixel must remain OFF (false)
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                Assert.False(buffer.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void OledLayout_Render_InvertDisplay_InvertsAllPixels()
    {
        var buffer = new OledFrameBuffer();
        var metrics = new HardwareMetrics();
        var layout = new OledLayout(); // Empty layout

        // Render with invertDisplay: true
        layout.Render(buffer, metrics, invertDisplay: true);

        // All pixels on screen should be ON (true)
        Assert.True(buffer.GetPixel(0, 0));
        Assert.True(buffer.GetPixel(127, 39));
    }

    [Fact]
    public void AppSettings_ModularToggles_SerializeAndDeserialize()
    {
        var original = new AppSettings
        {
            AutostartWithWindows = true,
            StartMinimized = true,
            MinimizeToTrayOnClose = true,
            InvertDisplay = true,
            EnableCpuMonitoring = true,
            EnableGpuMonitoring = false,
            EnablePowerMonitoring = true,
            EnableVolumeOverlay = true,
            EnableMicMonitor = true,
            EnableLockKeys = true,
            EnableGpuDeltaAlert = true,
            GpuDeltaThreshold = 25f,
            EnableInvertOnThermalAlert = false,
            EnableHotkeyProfileSwitch = true,
            EnableAutoProfileSwitch = true,
            EnableWeather = true,
            WeatherCity = "Tokyo",
            WeatherUpdateIntervalMinutes = 15
        };

        string json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.True(restored.InvertDisplay);
        Assert.True(restored.MinimizeToTrayOnClose);
        Assert.False(restored.EnableGpuMonitoring);
        Assert.True(restored.EnablePowerMonitoring);
        Assert.Equal(25f, restored.GpuDeltaThreshold);
        Assert.False(restored.EnableInvertOnThermalAlert);
        Assert.Equal("Tokyo", restored.WeatherCity);
        Assert.Equal(15, restored.WeatherUpdateIntervalMinutes);
    }

    [Fact]
    public void OledWidget_INotifyPropertyChanged_FiresOnPropertyChange()
    {
        var widget = new OledWidget();
        var changedProps = new List<string>();
        widget.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
                changedProps.Add(e.PropertyName);
        };

        widget.Enabled = false;
        widget.Name = "Updated Name";
        widget.X = 15;
        widget.MetricKey = "total_power";

        Assert.Contains("Enabled", changedProps);
        Assert.Contains("Name", changedProps);
        Assert.Contains("X", changedProps);
        Assert.Contains("MetricKey", changedProps);
    }

    [Fact]
    public void HardwareMonitor_ConfigureSensors_DoesNotThrow()
    {
        using var service = new HardwareMonitorService();
        var settings = new AppSettings
        {
            EnableCpuMonitoring = true,
            EnableGpuMonitoring = false,
            EnableRamMonitoring = true,
            EnableNetworkMonitoring = false,
            EnableStorageMonitoring = false
        };

        // Must run smoothly without exceptions
        service.ConfigureSensors(settings);
    }

    [Fact]
    public void HardwareMonitor_PollHardware_GeneratesValidPowerFallback()
    {
        using var service = new HardwareMonitorService();
        service.PollHardware();
        var metrics = service.CurrentMetrics;

        Assert.NotNull(metrics);
        Assert.True(metrics.CpuPower > 0f, "CPU power must be greater than 0W even without admin rights");
        Assert.True(metrics.GpuPower > 0f, "GPU power must be greater than 0W even without admin rights");
        Assert.True(metrics.TotalPower >= metrics.CpuPower + metrics.GpuPower - 0.1f, "Total power must be sum of CPU and GPU");
        Assert.True(metrics.CpuTemp > 0f, "CPU temperature must be greater than 0C");
    }

    [Fact]
    public void MainViewModel_AddPresetWidget_AddsWattsAndPreservesProperties()
    {
        var t = new Thread(() =>
        {
            using var vm = new ApexOLEDStudio.UI.ViewModels.MainViewModel();
            vm.CurrentLayout.Widgets.Clear();

            vm.AddPresetWidget("watts_total");
            Assert.Single(vm.CurrentLayout.Widgets);
            var w = vm.CurrentLayout.Widgets[0];
            Assert.Equal("total_power", w.MetricKey);
            Assert.Equal("{total_power}W", w.FormatTemplate);

            vm.AddPresetWidget("dual_temp");
            Assert.Equal(2, vm.CurrentLayout.Widgets.Count);
            var w2 = vm.CurrentLayout.Widgets[1];
            Assert.Equal("cpu_temp", w2.MetricKey);

            // Verify smart placement prevents exact coordinate collision
            Assert.False(w.X == w2.X && w.Y == w2.Y, "Widgets must not overlap at identical coordinates");
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
    }

    [Fact]
    public void MainWindow_Xaml_HasNoOverlappingRows_AndThreeColumnStudioArchitecture()
    {
        string xamlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "ApexOLEDStudio.UI", "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), $"MainWindow.xaml must exist at {xamlPath}");
        string xaml = File.ReadAllText(xamlPath);

        // 1. Root grid must have 3 rows (Header, MainTabs, Footer)
        Assert.Contains("<RowDefinition Height=\"*\" />    <!-- Main Tactical Tabs (Full Workspace Height) -->", xaml);

        // 2. Tab 1 must have 3 columns: Widget Layers (290), Center Studio Canvas (*), Inspector (350)
        Assert.Contains("<ColumnDefinition Width=\"290\" /> <!-- Column 0: Full-Height WIDGET LAYERS List -->", xaml);
        Assert.Contains("<ColumnDefinition Width=\"*\" />   <!-- Column 1: Center Studio Canvas & Quick Dock -->", xaml);
        Assert.Contains("<ColumnDefinition Width=\"350\" /> <!-- Column 2: Widget Inspector & Sensor Telemetry -->", xaml);

        // 3. Left column must NOT have row collisions (ListBox in Row 2, Batch controls in Row 3)
        Assert.Contains("<ListBox Grid.Row=\"2\" ItemsSource=\"{Binding Widgets}\"", xaml);
        Assert.Contains("<Grid Grid.Row=\"3\" Margin=\"0,10,0,0\">", xaml);

        // 4. Center column must contain the 1-Click Widget Shelf directly under canvas
        Assert.Contains("1-CLICK WIDGET SHELF", xaml);
        Assert.Contains("InteractiveCanvas", xaml);
    }

    [Fact]
    public void Typography_NormalizeLayoutUsesOneScaleAndKeepsPositions()
    {
        var layout = new OledLayout();
        layout.Widgets.Add(new OledWidget
        {
            Name = "Label",
            Type = WidgetType.Text,
            X = 17,
            Y = 9,
            Width = 30,
            Height = 2,
            FontScale = 4
        });
        layout.Widgets.Add(new OledWidget
        {
            Name = "Graph",
            Type = WidgetType.Graph,
            X = 63,
            Y = 12,
            Width = 40,
            Height = 3,
            FontScale = 3
        });

        layout.NormalizeTypography();

        Assert.All(layout.Widgets, widget => Assert.Equal(WidgetTypography.BaseFontScale, widget.FontScale));
        Assert.Equal(17, layout.Widgets[0].X);
        Assert.Equal(9, layout.Widgets[0].Y);
        Assert.True(layout.Widgets[0].Height >= WidgetTypography.RecommendedHeight(WidgetType.Text, false));
        Assert.True(layout.Widgets[1].Height >= WidgetTypography.RecommendedHeight(WidgetType.Graph, false));
    }

    [Fact]
    public void Typography_FontScaleIsClampedAndIndividualOverrideSurvives()
    {
        var widget = new OledWidget { FontScale = 99 };
        Assert.Equal(WidgetTypography.MaxFontScale, widget.FontScale);

        widget.FontScale = 3;
        var layout = new OledLayout();
        layout.Widgets.Add(widget);
        layout.NormalizeTypography();
        Assert.Equal(WidgetTypography.BaseFontScale, widget.FontScale);

        widget.FontScale = 4;
        Assert.Equal(4, widget.FontScale);
        widget.ResetEditorDefaults();
        Assert.Equal(WidgetTypography.BaseFontScale, widget.FontScale);
    }

    [Fact]
    public void Typography_PrimitivesAcceptScaleWithoutChangingLegacyDefaults()
    {
        var buffer = new OledFrameBuffer();
        buffer.DrawProgressBar(1, 1, 30, 8, 50, scale: 2);
        buffer.DrawGraph(1, 12, 30, 10, new[] { 10f, 50f, 90f }, scale: 2);
        buffer.DrawGauge(50, 25, 8, 50, scale: 2);

        Assert.True(buffer.GetPixel(1, 1) || buffer.GetPixel(50, 25));
    }
}
