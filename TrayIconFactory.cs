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

        using var bezelBrush = new LinearGradientBrush(
            new Rectangle(7, 8, 50, 38),
            Color.FromArgb(66, 66, 70),
            Color.FromArgb(30, 30, 32),
            90f);
        using var innerShadowBrush = new SolidBrush(Color.FromArgb(35, 35, 38));
        using var screenBrush = new LinearGradientBrush(
            new Rectangle(10, 11, 44, 31),
            Color.FromArgb(63, 141, 255),
            Color.FromArgb(14, 74, 199),
            90f);
        using var standBrush = new LinearGradientBrush(
            new Rectangle(24, 46, 16, 14),
            Color.FromArgb(87, 87, 91),
            Color.FromArgb(34, 34, 36),
            90f);
        using var baseBrush = new LinearGradientBrush(
            new Rectangle(18, 56, 28, 6),
            Color.FromArgb(84, 84, 88),
            Color.FromArgb(32, 32, 34),
            90f);
        using var arrowBrush = new SolidBrush(Color.WhiteSmoke);
        using var outlinePen = new Pen(Color.FromArgb(20, 20, 22), 2f);

        FillRoundedRectangle(graphics, bezelBrush, new Rectangle(7, 8, 50, 38), 6);
        graphics.DrawPath(outlinePen, CreateRoundedRectanglePath(new Rectangle(7, 8, 50, 38), 6));
        FillRoundedRectangle(graphics, innerShadowBrush, new Rectangle(10, 11, 44, 31), 4);
        FillRoundedRectangle(graphics, screenBrush, new Rectangle(11, 12, 42, 29), 3);

        var standPoints = new[]
        {
            new PointF(26, 46),
            new PointF(38, 46),
            new PointF(40, 56),
            new PointF(24, 56)
        };
        graphics.FillPolygon(standBrush, standPoints);
        FillRoundedRectangle(graphics, baseBrush, new Rectangle(17, 56, 30, 5), 2);

        using var topArrow = CreateTopArrowPath();
        using var bottomArrow = CreateBottomArrowPath();
        graphics.FillPath(arrowBrush, topArrow);
        graphics.FillPath(arrowBrush, bottomArrow);

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

    private static GraphicsPath CreateTopArrowPath()
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddLines(new Point[]
        {
            new Point(22, 23),
            new Point(34, 23),
            new Point(34, 18),
            new Point(42, 26),
            new Point(34, 34),
            new Point(34, 29),
            new Point(22, 29)
        });
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath CreateBottomArrowPath()
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddLines(new Point[]
        {
            new Point(42, 31),
            new Point(30, 31),
            new Point(30, 36),
            new Point(22, 28),
            new Point(30, 20),
            new Point(30, 25),
            new Point(42, 25)
        });
        path.CloseFigure();
        return path;
    }
}
