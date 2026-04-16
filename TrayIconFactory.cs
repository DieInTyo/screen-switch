using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenSwitch;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var backgroundBrush = new LinearGradientBrush(
            new Rectangle(0, 0, 64, 64),
            Color.FromArgb(24, 75, 140),
            Color.FromArgb(0, 168, 181),
            35f);
        graphics.FillEllipse(backgroundBrush, 2, 2, 60, 60);

        using var screenBrush = new SolidBrush(Color.FromArgb(245, 249, 252));
        using var standBrush = new SolidBrush(Color.FromArgb(196, 224, 234));
        using var arrowPen = new Pen(Color.FromArgb(255, 210, 77), 4)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.ArrowAnchor
        };

        FillRoundedRectangle(graphics, screenBrush, new Rectangle(10, 16, 18, 14), 3);
        FillRoundedRectangle(graphics, screenBrush, new Rectangle(36, 16, 18, 14), 3);
        graphics.FillRectangle(standBrush, 16, 31, 6, 4);
        graphics.FillRectangle(standBrush, 42, 31, 6, 4);
        graphics.FillRectangle(standBrush, 12, 35, 14, 3);
        graphics.FillRectangle(standBrush, 38, 35, 14, 3);
        graphics.DrawLine(arrowPen, 24, 46, 38, 46);
        graphics.DrawLine(arrowPen, 40, 50, 26, 50);

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
    {
        using var path = CreateRoundedRectanglePath(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rectangle, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
