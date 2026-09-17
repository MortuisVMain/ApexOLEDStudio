using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using ApexOLEDStudio.Core.Drawing;

namespace ApexOLEDStudio.Core.Models;

public enum WidgetType
{
    Text,
    ProgressBar,
    Graph,
    Gauge,
    Line,
    Gif
}

public enum TextSizeMode
{
    FreeStretch,
    Proportional,
    FixedScale
}

public static class WidgetTypography
{
    public const int BaseFontScale = 1;
    public const int MinFontScale = 1;
    public const int MaxFontScale = 4;

    public static int ClampFontScale(int scale)
        => Math.Clamp(scale, MinFontScale, MaxFontScale);

    public static int RecommendedHeight(WidgetType type, bool compactFont, int scale = BaseFontScale)
    {
        int normalizedScale = ClampFontScale(scale);
        return type switch
        {
            WidgetType.Text => (compactFont ? 5 : 7) * normalizedScale,
            WidgetType.ProgressBar => 6 + normalizedScale,
            WidgetType.Graph => 8 + normalizedScale,
            WidgetType.Gauge => 12 + normalizedScale * 2,
            _ => 1
        };
    }
}

public sealed class OledWidget : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private string _id = Guid.NewGuid().ToString("N");
    public string Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(); } } }

    private string _name = "New Widget";
    public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }

    private WidgetType _type = WidgetType.Text;
    public WidgetType Type { get => _type; set { if (_type != value) { _type = value; OnPropertyChanged(); } } }

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; OnPropertyChanged(); } } }

    private bool _isLocked;
    public bool IsLocked { get => _isLocked; set { if (_isLocked != value) { _isLocked = value; OnPropertyChanged(); } } }

    private int _zIndex;
    public int ZIndex { get => _zIndex; set { if (_zIndex != value) { _zIndex = value; OnPropertyChanged(); } } }

    // Bounds in 128x40 coordinates
    private int _x;
    public int X { get => _x; set { if (_x != value) { _x = value; OnPropertyChanged(); } } }

    private int _y;
    public int Y { get => _y; set { if (_y != value) { _y = value; OnPropertyChanged(); } } }

    private int _width = 30;
    public int Width
    {
        get => _width;
        set
        {
            if (_width != value)
            {
                _width = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DimensionsSummary));
                OnPropertyChanged(nameof(StretchModeDisplayName));
                OnPropertyChanged(nameof(IsFreeStretchActive));
                OnPropertyChanged(nameof(IsProportionalActive));
                OnPropertyChanged(nameof(IsFixedScaleActive));
            }
        }
    }

    private int _height = 10;
    public int Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                _height = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DimensionsSummary));
                OnPropertyChanged(nameof(StretchModeDisplayName));
                OnPropertyChanged(nameof(IsFreeStretchActive));
                OnPropertyChanged(nameof(IsProportionalActive));
                OnPropertyChanged(nameof(IsFixedScaleActive));
                if (Type == WidgetType.Text)
                {
                    int baseCharHeight = CompactFont ? 5 : 7;
                    int calculatedScale = Math.Clamp(_height / baseCharHeight, WidgetTypography.MinFontScale, WidgetTypography.MaxFontScale);
                    if (_fontScale != calculatedScale && _height >= baseCharHeight)
                    {
                        _fontScale = calculatedScale;
                        OnPropertyChanged(nameof(FontScale));
                        OnPropertyChanged(nameof(FontScaleDisplayName));
                    }
                }
            }
        }
    }

    // Data Binding
    private string _metricKey = "cpu_load";
    public string MetricKey { get => _metricKey; set { if (_metricKey != value) { _metricKey = value; OnPropertyChanged(); } } }

    private string _formatTemplate = "{cpu_load}%";
    public string FormatTemplate { get => _formatTemplate; set { if (_formatTemplate != value) { _formatTemplate = value; OnPropertyChanged(); } } }

    private bool _compactFont = false;
    public bool CompactFont
    {
        get => _compactFont;
        set
        {
            if (_compactFont != value)
            {
                _compactFont = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontScaleDisplayName));
            }
        }
    }

    private int _fontScale = WidgetTypography.BaseFontScale;
    public int FontScale
    {
        get => _fontScale;
        set
        {
            int normalized = WidgetTypography.ClampFontScale(value);
            if (_fontScale != normalized)
            {
                _fontScale = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FontScaleDisplayName));
                if (Type == WidgetType.Text)
                {
                    int recH = WidgetTypography.RecommendedHeight(Type, CompactFont, normalized);
                    if (_height < recH)
                    {
                        _height = recH;
                        OnPropertyChanged(nameof(Height));
                        OnPropertyChanged(nameof(DimensionsSummary));
                        OnPropertyChanged(nameof(StretchModeDisplayName));
                    }
                }
            }
        }
    }

    private bool _stretchText = true;
    public bool StretchText
    {
        get => _stretchText;
        set
        {
            if (_stretchText != value)
            {
                _stretchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StretchModeDisplayName));
                OnPropertyChanged(nameof(SizeMode));
                OnPropertyChanged(nameof(IsFreeStretchActive));
                OnPropertyChanged(nameof(IsProportionalActive));
                OnPropertyChanged(nameof(IsFixedScaleActive));
            }
        }
    }

    private bool _keepAspectRatio = false;
    public bool KeepAspectRatio
    {
        get => _keepAspectRatio;
        set
        {
            if (_keepAspectRatio != value)
            {
                _keepAspectRatio = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StretchModeDisplayName));
                OnPropertyChanged(nameof(SizeMode));
                OnPropertyChanged(nameof(IsFreeStretchActive));
                OnPropertyChanged(nameof(IsProportionalActive));
                OnPropertyChanged(nameof(IsFixedScaleActive));
            }
        }
    }

    [JsonIgnore]
    public TextSizeMode SizeMode
    {
        get => !StretchText ? TextSizeMode.FixedScale : (KeepAspectRatio ? TextSizeMode.Proportional : TextSizeMode.FreeStretch);
        set
        {
            switch (value)
            {
                case TextSizeMode.FreeStretch:
                    StretchText = true;
                    KeepAspectRatio = false;
                    break;
                case TextSizeMode.Proportional:
                    StretchText = true;
                    KeepAspectRatio = true;
                    break;
                case TextSizeMode.FixedScale:
                    StretchText = false;
                    KeepAspectRatio = false;
                    break;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(StretchText));
            OnPropertyChanged(nameof(KeepAspectRatio));
            OnPropertyChanged(nameof(StretchModeDisplayName));
            OnPropertyChanged(nameof(IsFreeStretchActive));
            OnPropertyChanged(nameof(IsProportionalActive));
            OnPropertyChanged(nameof(IsFixedScaleActive));
        }
    }

    [JsonIgnore]
    public bool IsFreeStretchActive => StretchText && !KeepAspectRatio;

    [JsonIgnore]
    public bool IsProportionalActive => StretchText && KeepAspectRatio;

    [JsonIgnore]
    public bool IsFixedScaleActive => !StretchText;

    [JsonIgnore]
    public string DimensionsSummary => $"{Width} × {Height} px";

    [JsonIgnore]
    public string StretchModeDisplayName =>
        !StretchText ? "Шрифт 5x7" :
        KeepAspectRatio ? $"{Width}×{Height} (Пропорции)" :
        $"{Width}×{Height} px (Растянут)";

    [JsonIgnore]
    public string FontScaleDisplayName => FontScale switch
    {
        1 => CompactFont ? "5 px (XS Micro)" : "7 px (1x Standard)",
        2 => "14 px (2x Large)",
        3 => "21 px (3x Huge)",
        4 => "28 px (4x Giant)",
        _ => $"{FontScale}x"
    };

    public void AutoFitToContent(int targetFontScale = 1)
    {
        if (Type != WidgetType.Text) return;
        int scale = targetFontScale > 0 ? targetFontScale : Math.Max(1, FontScale);
        int charW = (CompactFont ? 3 : 5) * scale;
        int charH = (CompactFont ? 5 : 7) * scale;
        int spacing = 1 * scale;
        string sample = string.IsNullOrEmpty(FormatTemplate) ? (Name ?? "Text") : FormatTemplate;
        string estimated = System.Text.RegularExpressions.Regex.Replace(sample, @"\{[^\}]+\}", "000");
        int len = Math.Max(1, estimated.Length);
        int neededW = len * charW + Math.Max(0, len - 1) * spacing;
        int neededH = charH;

        Width = Math.Clamp(neededW, 4, 128 - X);
        Height = Math.Clamp(neededH, 4, 40 - Y);
    }

    public void SetTextScale(int scale)
    {
        StretchText = false;
        FontScale = scale;
        int recH = WidgetTypography.RecommendedHeight(Type, CompactFont, FontScale);
        Height = Math.Max(Height, recH);
        int minCharWidth = (CompactFont ? 4 : 6) * FontScale;
        int approxLen = string.IsNullOrEmpty(FormatTemplate) ? 6 : Math.Max(4, FormatTemplate.Length);
        Width = Math.Clamp(Math.Max(Width, minCharWidth * approxLen), 10, 128 - X);
    }

    // Visual options
    private bool _bordered = true;
    public bool Bordered { get => _bordered; set { if (_bordered != value) { _bordered = value; OnPropertyChanged(); } } }

    private float _minValue = 0f;
    public float MinValue { get => _minValue; set { if (_minValue != value) { _minValue = value; OnPropertyChanged(); } } }

    private float _maxValue = 100f;
    public float MaxValue { get => _maxValue; set { if (_maxValue != value) { _maxValue = value; OnPropertyChanged(); } } }

    private bool _enableMarquee = false;
    public bool EnableMarquee { get => _enableMarquee; set { if (_enableMarquee != value) { _enableMarquee = value; OnPropertyChanged(); } } }

    private int _marqueeSpeed = 1;
    public int MarqueeSpeed { get => _marqueeSpeed; set { if (_marqueeSpeed != value) { _marqueeSpeed = value; OnPropertyChanged(); } } }

    private string? _gifPath = null;
    public string? GifPath { get => _gifPath; set { if (_gifPath != value) { _gifPath = value; OnPropertyChanged(); } } }

    [JsonIgnore]
    public List<float> History { get; } = new(128);

    [JsonIgnore]
    private int _marqueeOffset = 0;

    [JsonIgnore]
    private int _animFrameTick = 0;

    public void Render(OledFrameBuffer buffer, HardwareMetrics metrics)
    {
        if (!Enabled) return;

        float val = metrics.GetNumericValue(MetricKey);

        switch (Type)
        {
            case WidgetType.Text:
                string text = metrics.FormatTemplate(FormatTemplate);
                if (EnableMarquee)
                {
                    int charWidth = (CompactFont ? 4 : 6) * FontScale;
                    int textPixelWidth = text.Length * charWidth;
                    if (textPixelWidth > Width)
                    {
                        _marqueeOffset = (_marqueeOffset + Math.Max(1, MarqueeSpeed)) % (textPixelWidth + 12);
                        buffer.DrawText(X - _marqueeOffset, Y, text, CompactFont, FontScale, on: true);
                        if (_marqueeOffset > 0)
                        {
                            buffer.DrawText(X - _marqueeOffset + textPixelWidth + 12, Y, text, CompactFont, FontScale, on: true);
                        }
                        break;
                    }
                }

                if (StretchText)
                {
                    buffer.DrawTextStretched(X, Y, Width, Height, text, CompactFont, on: true, keepAspectRatio: KeepAspectRatio);
                }
                else
                {
                    buffer.DrawText(X, Y, text, CompactFont, FontScale, on: true);
                }
                break;

            case WidgetType.ProgressBar:
                buffer.DrawProgressBar(X, Y, Width, Height, val, Bordered, on: true, scale: FontScale);
                break;

            case WidgetType.Graph:
                History.Add(val);
                if (History.Count > Math.Max(Width, 128)) History.RemoveAt(0);
                buffer.DrawGraph(X, Y, Width, Height, History, MinValue, MaxValue, on: true, scale: FontScale);
                break;

            case WidgetType.Gauge:
                int radius = Math.Min(Width, Height) / 2;
                buffer.DrawGauge(X + radius, Y + radius, radius, val, on: true, scale: FontScale);
                break;

            case WidgetType.Line:
                buffer.DrawLine(X, Y, X + Width - 1, Y + Height - 1, on: true);
                break;

            case WidgetType.Gif:
                var frames = Services.GifAnimationService.LoadGifFrames(GifPath, Width, Height);
                if (frames.Count > 0)
                {
                    _animFrameTick++;
                    int frameIndex = (_animFrameTick / 4) % frames.Count;
                    var currentFrame = frames[frameIndex];
                    int fw = currentFrame.GetLength(0);
                    int fh = currentFrame.GetLength(1);
                    for (int gy = 0; gy < fh && (Y + gy) < OledFrameBuffer.Height; gy++)
                    {
                        for (int gx = 0; gx < fw && (X + gx) < OledFrameBuffer.Width; gx++)
                        {
                            if (currentFrame[gx, gy])
                            {
                                buffer.SetPixel(X + gx, Y + gy, true);
                            }
                        }

                    }
                }
                break;
        }
    }

    public void ResetEditorDefaults()
    {
        CompactFont = false;
        FontScale = WidgetTypography.BaseFontScale;
        Width = Type == WidgetType.Line ? Width : Math.Max(Width, Type == WidgetType.Text ? 40 : 30);
        Height = Math.Max(Height, WidgetTypography.RecommendedHeight(Type, CompactFont, FontScale));
        MinValue = 0f;
        MaxValue = 100f;
        Bordered = Type == WidgetType.ProgressBar;
        EnableMarquee = false;
        MarqueeSpeed = 1;
    }
}

public sealed class OledLayout
{
    public string Name { get; set; } = "Default Profile";
    public int ScreenWidth { get; set; } = 128;
    public int ScreenHeight { get; set; } = 40;
    public bool EnableBurnInProtection { get; set; } = true;
    public bool EnableThermalAlert { get; set; } = false;
    public float ThermalAlertCpuThreshold { get; set; } = 85f;
    public float ThermalAlertGpuThreshold { get; set; } = 82f;
    public bool EnableGpuDeltaAlert { get; set; } = true;
    public float GpuDeltaThreshold { get; set; } = 20f;
    public List<OledWidget> Widgets { get; set; } = new();

    public void NormalizeTypography(int baseFontScale = WidgetTypography.BaseFontScale, bool unifyToStandardFont = true)
    {
        int normalizedScale = WidgetTypography.ClampFontScale(baseFontScale);
        foreach (var widget in Widgets ?? Enumerable.Empty<OledWidget>())
        {
            widget.FontScale = normalizedScale;
            if (unifyToStandardFont)
            {
                widget.CompactFont = false;
            }
            int recommendedHeight = WidgetTypography.RecommendedHeight(widget.Type, widget.CompactFont, normalizedScale);
            if (widget.Type is not WidgetType.Line and not WidgetType.Gif)
                widget.Height = Math.Max(widget.Height, recommendedHeight);
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ScreenWidth != OledFrameBuffer.Width || ScreenHeight != OledFrameBuffer.Height)
            errors.Add($"Screen must be {OledFrameBuffer.Width}x{OledFrameBuffer.Height}.");
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Layout name is required.");
        if (Widgets == null)
            errors.Add("Widget collection is missing.");
        else
        {
            foreach (var widget in Widgets.Where(w => w != null))
            {
                if (widget.X < 0 || widget.Y < 0 || widget.Width <= 0 || widget.Height <= 0 ||
                    widget.X + widget.Width > ScreenWidth || widget.Y + widget.Height > ScreenHeight)
                    errors.Add($"Widget '{widget.Name}' is outside the {ScreenWidth}x{ScreenHeight} display.");
                if (widget.FontScale < 1 || widget.FontScale > 4)
                    errors.Add($"Widget '{widget.Name}' has an invalid font scale.");
            }
        }
        return errors;
    }

    public void ValidateOrThrow()
    {
        var errors = Validate();
        if (errors.Count > 0)
            throw new ArgumentException($"Invalid layout: {string.Join(" ", errors)}");
    }

    public bool IsThermalAlertActive(HardwareMetrics metrics)
    {
        if (!EnableThermalAlert) return false;
        float cpu = metrics.CpuTemp > 0 ? metrics.CpuTemp : metrics.CpuPackageTemp;
        float gpu = metrics.GpuTemp > 0 ? metrics.GpuTemp : metrics.GpuHotspot;
        if (cpu >= ThermalAlertCpuThreshold || gpu >= ThermalAlertGpuThreshold)
            return true;
        if (EnableGpuDeltaAlert && metrics.GpuDelta >= GpuDeltaThreshold)
            return true;
        return false;
    }

    public static OledLayout CreateDefaultApexProSplit()
    {
        var layout = new OledLayout
        {
            Name = "Apex Pro Dual Monitor (CPU & GPU)"
        };

        // Left side: CPU
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Label",
            Type = WidgetType.Text,
            X = 2,
            Y = 2,
            Width = 58,
            Height = 8,
            MetricKey = "cpu_load",
            FormatTemplate = "CPU {cpu_load}%",
            CompactFont = false,
            StretchText = true,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Temp",
            Type = WidgetType.Text,
            X = 2,
            Y = 11,
            Width = 58,
            Height = 9,
            MetricKey = "cpu_temp",
            FormatTemplate = "{cpu_temp}°C",
            CompactFont = false,
            StretchText = true,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Bar",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "cpu_load",
            Bordered = true
        });

        // Center Divider
        layout.Widgets.Add(new OledWidget
        {
            Name = "Center Divider",
            Type = WidgetType.Line,
            X = 63,
            Y = 0,
            Width = 1,
            Height = 40
        });

        // Right side: GPU
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Label",
            Type = WidgetType.Text,
            X = 68,
            Y = 2,
            Width = 58,
            Height = 8,
            MetricKey = "gpu_load",
            FormatTemplate = "GPU {gpu_load}%",
            CompactFont = false,
            StretchText = true,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Temp",
            Type = WidgetType.Text,
            X = 68,
            Y = 11,
            Width = 58,
            Height = 9,
            MetricKey = "gpu_temp",
            FormatTemplate = "{gpu_temp}°C",
            CompactFont = false,
            StretchText = true,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "gpu_load",
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreatePowerStation()
    {
        var layout = new OledLayout
        {
            Name = "Power Station (Watts)"
        };

        // Header: Total System Power
        layout.Widgets.Add(new OledWidget
        {
            Name = "Total Power",
            Type = WidgetType.Text,
            X = 24,
            Y = 1,
            MetricKey = "total_power",
            FormatTemplate = "TOTAL: {total_power}W",
            CompactFont = false,
            FontScale = 1
        });

        // Left: CPU Power Text
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Power",
            Type = WidgetType.Text,
            X = 2,
            Y = 12,
            MetricKey = "cpu_power",
            FormatTemplate = "CPU {cpu_power}W",
            CompactFont = false,
            FontScale = 1
        });

        // Left: CPU Power Bar (0..200W)
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Bar",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "cpu_power",
            MaxValue = 200f,
            Bordered = true
        });

        // Center Divider
        layout.Widgets.Add(new OledWidget
        {
            Name = "Divider",
            Type = WidgetType.Line,
            X = 63,
            Y = 12,
            Width = 1,
            Height = 26
        });

        // Right: GPU Power Text
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Power",
            Type = WidgetType.Text,
            X = 68,
            Y = 12,
            MetricKey = "gpu_power",
            FormatTemplate = "GPU {gpu_power}W",
            CompactFont = false,
            FontScale = 1
        });

        // Right: GPU Power Bar (0..450W)
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "gpu_power",
            MaxValue = 450f,
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreateGamerMinimal()
    {
        var layout = new OledLayout
        {
            Name = "Gamer Minimal"
        };

        // Left: GPU Temp
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Temp",
            Type = WidgetType.Text,
            X = 2,
            Y = 2,
            MetricKey = "gpu_temp",
            FormatTemplate = "GPU {gpu_temp}°C",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Hotspot",
            Type = WidgetType.Text,
            X = 2,
            Y = 12,
            MetricKey = "gpu_hotspot",
            FormatTemplate = "HS:{gpu_hotspot}°C",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Load Bar",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "gpu_load",
            Bordered = true
        });

        // Divider
        layout.Widgets.Add(new OledWidget
        {
            Name = "Divider",
            Type = WidgetType.Line,
            X = 63,
            Y = 0,
            Width = 1,
            Height = 40
        });

        // Right: GPU Fan & Power
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Fan",
            Type = WidgetType.Text,
            X = 68,
            Y = 2,
            MetricKey = "gpu_fan",
            FormatTemplate = "{gpu_fan} RPM",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Power",
            Type = WidgetType.Text,
            X = 68,
            Y = 12,
            MetricKey = "gpu_power",
            FormatTemplate = "{gpu_power}W",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "Power Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "gpu_power",
            MaxValue = 450f,
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreateDevMode()
    {
        var layout = new OledLayout
        {
            Name = "Dev Mode"
        };

        // Left: CPU
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Load",
            Type = WidgetType.Text,
            X = 2,
            Y = 2,
            MetricKey = "cpu_load",
            FormatTemplate = "CPU {cpu_load}%",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "RAM Used",
            Type = WidgetType.Text,
            X = 2,
            Y = 12,
            MetricKey = "ram_percent",
            FormatTemplate = "RAM {ram_used}G",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Bar",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "cpu_load",
            Bordered = true
        });

        // Divider
        layout.Widgets.Add(new OledWidget
        {
            Name = "Divider",
            Type = WidgetType.Line,
            X = 63,
            Y = 0,
            Width = 1,
            Height = 40
        });

        // Right: VRAM & Short Time
        layout.Widgets.Add(new OledWidget
        {
            Name = "VRAM Used",
            Type = WidgetType.Text,
            X = 68,
            Y = 2,
            MetricKey = "gpu_load",
            FormatTemplate = "VRM {gpu_vram_used}G",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "Time Short",
            Type = WidgetType.Text,
            X = 68,
            Y = 12,
            FormatTemplate = "{time_short}",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "RAM Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 22,
            Width = 58,
            Height = 14,
            MetricKey = "ram_percent",
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreateClockMedia()
    {
        var layout = new OledLayout
        {
            Name = "Clock & Time"
        };

        // Big Time in center
        layout.Widgets.Add(new OledWidget
        {
            Name = "Digital Clock",
            Type = WidgetType.Text,
            X = 32,
            Y = 4,
            FormatTemplate = "{time}",
            CompactFont = false,
            FontScale = 1
        });

        // Status row
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU/GPU Status",
            Type = WidgetType.Text,
            X = 14,
            Y = 17,
            FormatTemplate = "CPU:{cpu_load}%  GPU:{gpu_load}%",
            CompactFont = false,
            FontScale = 1
        });

        // Dual mini bars at bottom
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Mini Bar",
            Type = WidgetType.ProgressBar,
            X = 4,
            Y = 28,
            Width = 56,
            Height = 9,
            MetricKey = "cpu_load",
            Bordered = true
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Mini Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 28,
            Width = 56,
            Height = 9,
            MetricKey = "gpu_load",
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreateGamerPro()
    {
        var layout = new OledLayout
        {
            Name = "Gamer Pro (Ping/Net)"
        };

        // Left: Ping & Net
        layout.Widgets.Add(new OledWidget
        {
            Name = "Ping Text",
            Type = WidgetType.Text,
            X = 2,
            Y = 2,
            MetricKey = "ping",
            FormatTemplate = "PING:{ping}ms",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "Net Down",
            Type = WidgetType.Text,
            X = 2,
            Y = 12,
            MetricKey = "net_down",
            FormatTemplate = "D:{net_down}",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "Net Up",
            Type = WidgetType.Text,
            X = 2,
            Y = 22,
            MetricKey = "net_up",
            FormatTemplate = "U:{net_up}",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "Ping Bar",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 32,
            Width = 58,
            Height = 6,
            MetricKey = "ping",
            MaxValue = 100f,
            Bordered = true
        });

        // Center Divider
        layout.Widgets.Add(new OledWidget
        {
            Name = "Divider",
            Type = WidgetType.Line,
            X = 63,
            Y = 0,
            Width = 1,
            Height = 40
        });

        // Right: GPU & APM
        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Temp",
            Type = WidgetType.Text,
            X = 68,
            Y = 2,
            MetricKey = "gpu_temp",
            FormatTemplate = "GPU {gpu_temp}°C",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "APM Text",
            Type = WidgetType.Text,
            X = 68,
            Y = 12,
            MetricKey = "apm",
            FormatTemplate = "APM {apm}",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Power",
            Type = WidgetType.Text,
            X = 68,
            Y = 22,
            MetricKey = "gpu_power",
            FormatTemplate = "{gpu_power}W",
            CompactFont = false,
            FontScale = 1
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Bar",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 32,
            Width = 58,
            Height = 6,
            MetricKey = "gpu_load",
            Bordered = true
        });

        return layout;
    }

    public static OledLayout CreateMediaStation()
    {
        var layout = new OledLayout
        {
            Name = "Media Station"
        };

        // Top: Track Name
        layout.Widgets.Add(new OledWidget
        {
            Name = "Track Name",
            Type = WidgetType.Text,
            X = 2,
            Y = 2,
            FormatTemplate = "{media_status} {media_track}",
            CompactFont = false,
            FontScale = 1
        });

        // Subtitle: APM & Time
        layout.Widgets.Add(new OledWidget
        {
            Name = "Status Row",
            Type = WidgetType.Text,
            X = 2,
            Y = 14,
            FormatTemplate = "APM:{apm} · {time_short}",
            CompactFont = false,
            FontScale = 1
        });

        // Bottom Dual Meters
        layout.Widgets.Add(new OledWidget
        {
            Name = "CPU Meter",
            Type = WidgetType.ProgressBar,
            X = 2,
            Y = 24,
            Width = 58,
            Height = 12,
            MetricKey = "cpu_load",
            Bordered = true
        });

        layout.Widgets.Add(new OledWidget
        {
            Name = "GPU Meter",
            Type = WidgetType.ProgressBar,
            X = 68,
            Y = 24,
            Width = 58,
            Height = 12,
            MetricKey = "gpu_load",
            Bordered = true
        });

        return layout;
    }

    public void Render(OledFrameBuffer buffer, HardwareMetrics metrics, int pixelShiftX = 0, int pixelShiftY = 0, bool volumeOverlayActive = false, bool invertDisplay = false)
    {
        buffer.Clear(false);

        if (volumeOverlayActive)
        {
            string volHeader = metrics.IsVolumeMuted ? "VOLUME: [MUTED]" : $"VOLUME: {Math.Round(metrics.VolumeLevel)}%";
            buffer.DrawText(6, 4, volHeader, compact: false, scale: 1, on: true);
            buffer.DrawProgressBar(6, 20, 116, 14, metrics.VolumeLevel, bordered: true, on: true);
            if (invertDisplay) buffer.Invert();
            return;
        }

        // If pixel shift is active for burn-in protection, offset all widgets slightly
        foreach (var widget in Widgets)
        {
            int origX = widget.X;
            int origY = widget.Y;

            if (pixelShiftX != 0 || pixelShiftY != 0)
            {
                widget.X = Math.Clamp(widget.X + pixelShiftX, 0, ScreenWidth - 1);
                widget.Y = Math.Clamp(widget.Y + pixelShiftY, 0, ScreenHeight - 1);
            }

            widget.Render(buffer, metrics);

            widget.X = origX;
            widget.Y = origY;
        }

        // Thermal alert: pulse inversion at 1 Hz when temperature exceeds safety threshold
        if (IsThermalAlertActive(metrics) && DateTime.UtcNow.Millisecond < 500)
        {
            buffer.Invert();
        }
        else if (invertDisplay)
        {
            buffer.Invert();
        }
    }
}
