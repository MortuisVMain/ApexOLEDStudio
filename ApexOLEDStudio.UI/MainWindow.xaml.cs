using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ApexOLEDStudio.Core.Models;
using ApexOLEDStudio.UI.ViewModels;

namespace ApexOLEDStudio.UI;

public partial class MainWindow : Window
{
    private const double OledScale = 6.0; // 128x40 → 768x240

    // Drag state
    private bool _isDragging;
    private OledWidget? _draggedWidget;
    private double _dragOffsetX;
    private double _dragOffsetY;

    // Resize state
    private bool _isResizing;
    private double _resizeStartMouseX;
    private double _resizeStartMouseY;
    private int _resizeStartWidth;
    private int _resizeStartHeight;
    private OledWidget? _highlightTrackedWidget;

    // Tray & Background state
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _balloonShown;
    private bool _hotkeyRegistered;

    // Global Hotkey (Ctrl + Alt + O)
    private const int HOTKEY_ID = 9001;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_O = 0x4F;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();

        Loaded += (s, e) =>
        {
            string[] args = Environment.GetCommandLineArgs();
            bool startMin = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                                          a.Equals("-silent", StringComparison.OrdinalIgnoreCase))
                            || (ViewModel?.Settings.StartMinimized == true);

            if (startMin)
            {
                Hide();
            }

            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += (vs, ve) =>
                {
                    if (ve.PropertyName == nameof(MainViewModel.SelectedWidget))
                    {
                        AttachWidgetTracking(ViewModel.SelectedWidget);
                        UpdateSelectionHighlight(ViewModel.SelectedWidget);
                    }
                };

                AttachWidgetTracking(ViewModel.SelectedWidget);
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            source?.AddHook(HwndHook);
            _hotkeyRegistered = RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_O);
            if (!_hotkeyRegistered)
            {
                System.Diagnostics.Debug.WriteLine("[Hotkey] Registration failed; Ctrl+Alt+O may be used by another application.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Hotkey] Failed to register: {ex.Message}");
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            if (ViewModel?.Settings.EnableHotkeyProfileSwitch == true)
            {
                ViewModel.CycleNextPreset();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void InitializeTrayIcon()
    {
        try
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open ApexOLED Studio", null, (s, e) => Dispatcher.Invoke(ShowWindow));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var presetsMenu = new System.Windows.Forms.ToolStripMenuItem("Presets");
            presetsMenu.DropDownItems.Add("Dual CPU/GPU", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Dual CPU/GPU")));
            presetsMenu.DropDownItems.Add("Power Station (Watts)", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Power Station (Watts)")));
            presetsMenu.DropDownItems.Add("Gamer Pro (Ping/Net)", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Gamer Pro (Ping/Net)")));
            presetsMenu.DropDownItems.Add("Media Station", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Media Station")));
            presetsMenu.DropDownItems.Add("Gamer Minimal", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Gamer Minimal")));
            presetsMenu.DropDownItems.Add("Dev Mode", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Dev Mode")));
            presetsMenu.DropDownItems.Add("Clock & Time", null, (s, e) => Dispatcher.Invoke(() => ViewModel?.ApplyPreset("Clock & Time")));
            menu.Items.Add(presetsMenu);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Dispatcher.Invoke(ExitApplication));

            System.Drawing.Icon appIcon;
            try
            {
                string? exePath = Environment.ProcessPath;
                appIcon = (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath)
                    ? System.Drawing.Icon.ExtractAssociatedIcon(exePath)
                    : null) ?? System.Drawing.SystemIcons.Application;
            }
            catch
            {
                appIcon = System.Drawing.SystemIcons.Application;
            }

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "ApexOLED Studio",
                Icon = appIcon,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => Dispatcher.Invoke(ShowWindow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Tray] Initialization error: {ex.Message}");
        }
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ExitApplication()
    {
        _isExiting = true;
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (_hotkeyRegistered)
                UnregisterHotKey(handle, HOTKEY_ID);
        }
        catch { }
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        ViewModel?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting && (ViewModel?.Settings.MinimizeToTrayOnClose ?? true))
        {
            e.Cancel = true;
            Hide();
            if (_notifyIcon != null && !_balloonShown)
            {
                _balloonShown = true;
                _notifyIcon.ShowBalloonTip(2000, "ApexOLED Studio", "Running in background. Double-click tray icon to restore.", System.Windows.Forms.ToolTipIcon.Info);
            }
            return;
        }

        base.OnClosing(e);
    }

    // ── Selection + Drag Start ────────────────────────────────────────
    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.CurrentLayout == null) return;

        var pos   = e.GetPosition(InteractiveCanvas);
        int oledX = (int)(pos.X / OledScale);
        int oledY = (int)(pos.Y / OledScale);

        var hit = ViewModel.CurrentLayout.Widgets
            .FirstOrDefault(w => oledX >= w.X && oledX <= w.X + Math.Max(w.Width, 20) &&
                                 oledY >= w.Y && oledY <= w.Y + Math.Max(w.Height, 8));

        if (hit != null)
        {
            ViewModel.SelectedWidget = hit;
            AttachWidgetTracking(hit);
            UpdateSelectionHighlight(hit);

            if (hit.IsLocked)
                return;

            // Begin drag
            _isDragging     = true;
            _draggedWidget  = hit;
            _dragOffsetX    = pos.X - hit.X * OledScale;
            _dragOffsetY    = pos.Y - hit.Y * OledScale;

            // Capture mouse so we keep getting events even outside the canvas
            Mouse.Capture(InteractiveCanvas);
        }
        else
        {
            ViewModel.SelectedWidget = null;
            AttachWidgetTracking(null);
            UpdateSelectionHighlight(null);
        }
    }

    // ── Resize Grip Start ─────────────────────────────────────────────
    private void OnResizeGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.SelectedWidget == null || ViewModel.SelectedWidget.IsLocked) return;

        if (ViewModel.SelectedWidget.Type == WidgetType.Text)
        {
            ViewModel.SelectedWidget.StretchText = true;
        }

        _isResizing = true;
        var pos = e.GetPosition(InteractiveCanvas);
        _resizeStartMouseX = pos.X;
        _resizeStartMouseY = pos.Y;
        _resizeStartWidth  = ViewModel.SelectedWidget.Width;
        _resizeStartHeight = ViewModel.SelectedWidget.Height;

        Mouse.Capture(InteractiveCanvas);
        e.Handled = true;
    }

    // ── Drag & Resize Move ────────────────────────────────────────────
    private void OnCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var pos = e.GetPosition(InteractiveCanvas);

        if (_isResizing && ViewModel?.SelectedWidget != null)
        {
            double deltaPixelX = (pos.X - _resizeStartMouseX) / OledScale;
            double deltaPixelY = (pos.Y - _resizeStartMouseY) / OledScale;

            int newW = (int)Math.Round(_resizeStartWidth + deltaPixelX);
            int newH = (int)Math.Round(_resizeStartHeight + deltaPixelY);

            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                int step = Math.Max(1, ViewModel.Settings.EditorGridStep);
                newW = WidgetEditorMath.Snap(newW, step);
                newH = WidgetEditorMath.Snap(newH, step);
            }

            int maxW = 128 - ViewModel.SelectedWidget.X;
            int maxH = 40 - ViewModel.SelectedWidget.Y;
            ViewModel.SelectedWidget.Width = Math.Clamp(newW, 4, Math.Max(4, maxW));
            ViewModel.SelectedWidget.Height = Math.Clamp(newH, 4, Math.Max(4, maxH));
            ResizeGripHandle.ToolTip = $"Размер: {ViewModel.SelectedWidget.Width} × {ViewModel.SelectedWidget.Height} px";

            UpdateSelectionHighlight(ViewModel.SelectedWidget);
            return;
        }

        if (!_isDragging || _draggedWidget == null) return;

        int newX  = (int)Math.Round((pos.X - _dragOffsetX) / OledScale);
        int newY  = (int)Math.Round((pos.Y - _dragOffsetY) / OledScale);

        // Clamp to OLED screen bounds
        _draggedWidget.X = newX;
        _draggedWidget.Y = newY;
        ViewModel?.NormalizeSelectedWidget(snap: Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));

        // Move the selection highlight in real time
        UpdateSelectionHighlight(_draggedWidget);
    }

    // ── Drag & Resize End ─────────────────────────────────────────────
    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            Mouse.Capture(null);
            if (ViewModel?.SelectedWidget != null)
            {
                ViewModel.NormalizeSelectedWidget();
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
            }
            return;
        }

        if (!_isDragging) return;

        _isDragging    = false;
        _draggedWidget = null;
        Mouse.Capture(null); // Release mouse capture
    }

    // ── Helpers & Selection Highlight ─────────────────────────────────
    private void AttachWidgetTracking(OledWidget? widget)
    {
        if (_highlightTrackedWidget != null)
            _highlightTrackedWidget.PropertyChanged -= OnWidgetPropertyChanged;

        _highlightTrackedWidget = widget;
        if (_highlightTrackedWidget != null)
            _highlightTrackedWidget.PropertyChanged += OnWidgetPropertyChanged;
    }

    private void OnWidgetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is OledWidget w && w == ViewModel?.SelectedWidget)
        {
            if (e.PropertyName is nameof(OledWidget.X) or nameof(OledWidget.Y) or nameof(OledWidget.Width) or nameof(OledWidget.Height) or nameof(OledWidget.IsLocked))
            {
                UpdateSelectionHighlight(w);
            }
        }
    }

    private void UpdateSelectionHighlight(OledWidget? widget)
    {
        if (widget == null)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            ResizeGripHandle.Visibility = Visibility.Collapsed;
            return;
        }

        double left = widget.X * OledScale;
        double top = widget.Y * OledScale;
        double w = Math.Max(widget.Width, 4) * OledScale;
        double h = Math.Max(widget.Height, 4) * OledScale;

        Canvas.SetLeft(SelectionBorder, left - 2);
        Canvas.SetTop(SelectionBorder,  top - 2);
        SelectionBorder.Width  = w + 4;
        SelectionBorder.Height = h + 4;
        SelectionBorder.Visibility = Visibility.Visible;

        // Position resize grip handle at bottom-right corner (centered on 14x14 handle)
        Canvas.SetLeft(ResizeGripHandle, left + w - 7);
        Canvas.SetTop(ResizeGripHandle,  top + h - 7);
        ResizeGripHandle.ToolTip = $"Размер: {widget.Width} × {widget.Height} px (Тяните для изменения размера)";
        ResizeGripHandle.Visibility = widget.IsLocked ? Visibility.Collapsed : Visibility.Visible;
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.OriginalSource is System.Windows.Controls.TextBox)
            return;

        if (ViewModel?.SelectedWidget == null || ViewModel.SelectedWidget.IsLocked)
            return;

        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        switch (e.Key)
        {
            case Key.Left:
                if (isShift)
                    ViewModel.NudgeWidth(-1);
                else
                    ViewModel.NudgeX(-1);
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
                e.Handled = true;
                break;

            case Key.Right:
                if (isShift)
                    ViewModel.NudgeWidth(1);
                else
                    ViewModel.NudgeX(1);
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
                e.Handled = true;
                break;

            case Key.Up:
                if (isShift)
                    ViewModel.NudgeHeight(-1);
                else
                    ViewModel.NudgeY(-1);
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
                e.Handled = true;
                break;

            case Key.Down:
                if (isShift)
                    ViewModel.NudgeHeight(1);
                else
                    ViewModel.NudgeY(1);
                UpdateSelectionHighlight(ViewModel.SelectedWidget);
                e.Handled = true;
                break;
        }
    }

    private void OnInsertTokenClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is string token && ViewModel?.SelectedWidget != null)
        {
            ViewModel.SelectedWidget.FormatTemplate += token;
            // Force inspector refresh
            var current = ViewModel.SelectedWidget;
            ViewModel.SelectedWidget = null;
            ViewModel.SelectedWidget = current;
        }
    }

    private void OnToggleSettingsPanel(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}