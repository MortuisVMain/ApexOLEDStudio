namespace ApexOLEDStudio.Core.Models;

public static class WidgetEditorMath
{
    public static int ClampCoordinate(int value, int minimum, int maximum)
        => Math.Clamp(value, minimum, Math.Max(minimum, maximum));

    public static int ClampSize(int value, int minimum, int available)
        => Math.Clamp(value, Math.Max(1, minimum), Math.Max(1, available));

    public static int Snap(int value, int step)
    {
        step = Math.Max(1, step);
        return Math.Max(0, (int)Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step);
    }

    public static void ClampToCanvas(OledWidget widget, int canvasWidth = 128, int canvasHeight = 40)
    {
        ArgumentNullException.ThrowIfNull(widget);
        widget.Width = ClampSize(widget.Width, 1, canvasWidth);
        widget.Height = ClampSize(widget.Height, 1, canvasHeight);
        widget.X = ClampCoordinate(widget.X, 0, canvasWidth - widget.Width);
        widget.Y = ClampCoordinate(widget.Y, 0, canvasHeight - widget.Height);
    }

    public static void SnapToCanvas(OledWidget widget, int step, int canvasWidth = 128, int canvasHeight = 40)
    {
        ArgumentNullException.ThrowIfNull(widget);
        widget.X = Snap(widget.X, step);
        widget.Y = Snap(widget.Y, step);
        ClampToCanvas(widget, canvasWidth, canvasHeight);
    }
}
