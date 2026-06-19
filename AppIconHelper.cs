using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenSwitch;

internal static class AppIconHelper
{
    public static Image GetAppIcon(MovableWindowInfo window, IDictionary<string, Image> cache)
    {
        var key = GetIconKey(window);
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        Image image;
        try
        {
            image = !string.IsNullOrWhiteSpace(window.ProcessPath) && File.Exists(window.ProcessPath)
                ? Icon.ExtractAssociatedIcon(window.ProcessPath)?.ToBitmap() ?? SystemIcons.Application.ToBitmap()
                : SystemIcons.Application.ToBitmap();
        }
        catch
        {
            image = SystemIcons.Application.ToBitmap();
        }

        cache[key] = image;
        return image;
    }

    public static Image GetTileIcon(MovableWindowInfo window, IDictionary<string, Image> cache)
    {
        if (!window.IsMinimizedOrOffscreen)
        {
            return GetAppIcon(window, cache);
        }

        var dimmedKey = GetIconKey(window) + "|dim";
        if (cache.TryGetValue(dimmedKey, out var cached))
        {
            return cached;
        }

        var dimmed = CreateDimmedImage(GetAppIcon(window, cache));
        cache[dimmedKey] = dimmed;
        return dimmed;
    }

    private static string GetIconKey(MovableWindowInfo window)
    {
        return string.IsNullOrWhiteSpace(window.ProcessPath) ? window.AppName : window.ProcessPath;
    }

    private static Image CreateDimmedImage(Image source)
    {
        var image = new Bitmap(source.Width, source.Height);
        using var graphics = Graphics.FromImage(image);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix
        {
            Matrix00 = 0.58f,
            Matrix11 = 0.58f,
            Matrix22 = 0.58f,
            Matrix33 = 0.36f,
            Matrix44 = 1f
        });
        graphics.DrawImage(
            source,
            new Rectangle(0, 0, source.Width, source.Height),
            0,
            0,
            source.Width,
            source.Height,
            GraphicsUnit.Pixel,
            attributes);
        return image;
    }
}
