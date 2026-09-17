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

    public static void Resize(OledWidget widget, int deltaWidth, int deltaHeight, int minW = 4, int minH = 4, int canvasWidth = 128, int canvasHeight = 40)
    {
        ArgumentNullException.ThrowIfNull(widget);
        int availableW = canvasWidth - widget.X;
        int availableH = canvasHeight - widget.Y;
        widget.Width = Math.Clamp(widget.Width + deltaWidth, Math.Min(minW, availableW), availableW);
        widget.Height = Math.Clamp(widget.Height + deltaHeight, Math.Min(minH, availableH), availableH);
    }

    public static void Nudge(OledWidget widget, int deltaX, int deltaY, int canvasWidth = 128, int canvasHeight = 40)
    {
        ArgumentNullException.ThrowIfNull(widget);
        widget.X = ClampCoordinate(widget.X + deltaX, 0, canvasWidth - widget.Width);
        widget.Y = ClampCoordinate(widget.Y + deltaY, 0, canvasHeight - widget.Height);
    }

    public static (int width, int height) CalculateTextBounds(string text, bool compact = false, int scale = 1)
    {
        if (string.IsNullOrEmpty(text)) return (0, 0);
        int charW = (compact ? 3 : 5) * Math.Max(1, scale);
        int charH = (compact ? 5 : 7) * Math.Max(1, scale);
        int spacing = 1 * Math.Max(1, scale);
        int width = text.Length * charW + Math.Max(0, text.Length - 1) * spacing;
        return (width, charH);
    }
}
