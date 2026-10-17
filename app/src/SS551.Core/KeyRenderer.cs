using SkiaSharp;
using SS551.Core.Model;

namespace SS551.Core;

/// <summary>Draws key faces: background colour, optional icon and a title.</summary>
public static class KeyRenderer
{
    public const int Size = 64;

    /// <summary>Renders the key as seen by the user (upright).</summary>
    public static SKBitmap Render(KeyAppearance key, int size = Size)
    {
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Scale(size / (float)Size);
        Draw(canvas, key);
        return bmp;
    }

    /// <summary>
    /// Renders the 64x64 JPEG the device expects: the panel shows images
    /// rotated 90° counter-clockwise, so the image is rotated clockwise here.
    /// </summary>
    public static byte[] RenderForDevice(KeyAppearance key)
    {
        using var bmp = new SKBitmap(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Translate(Size, 0);
            canvas.RotateDegrees(90);
            Draw(canvas, key);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>PNG of the upright key, for UI previews.</summary>
    public static byte[] RenderPng(KeyAppearance key, int size)
    {
        using var bmp = Render(key, size);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Draw(SKCanvas canvas, KeyAppearance key)
    {
        canvas.Clear(ParseColor(key.BackgroundColor, SKColors.Black));

        var hasTitle = !string.IsNullOrWhiteSpace(key.Title);
        var iconRect = hasTitle ? SKRect.Create(10, 4, 44, 40) : SKRect.Create(4, 4, 56, 56);
        if (LoadIcon(key.IconPath) is { } icon)
        {
            using (icon)
            {
                var fit = Fit(icon.Width, icon.Height, iconRect);
                using var img = SKImage.FromBitmap(icon);
                canvas.DrawImage(img, fit, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
        }
        else if (hasTitle)
        {
            // No icon: centre the title in the whole key.
            DrawTitle(canvas, key, SKRect.Create(3, 3, 58, 58), 20);
            return;
        }

        if (hasTitle)
            DrawTitle(canvas, key, SKRect.Create(2, 44, 60, 18), 13);
    }

    private static void DrawTitle(SKCanvas canvas, KeyAppearance key, SKRect area, float maxSize)
    {
        using var paint = new SKPaint { Color = ParseColor(key.TextColor, SKColors.White), IsAntialias = true };
        using var font = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), maxSize) { Edging = SKFontEdging.Antialias };

        var lines = key.Title.Split('\n');
        // Shrink until the widest line and all lines fit the area.
        while (font.Size > 7)
        {
            var widest = lines.Max(l => font.MeasureText(l));
            if (widest <= area.Width && font.Spacing * lines.Length <= area.Height)
                break;
            font.Size -= 1;
        }

        // Still too wide at the smallest size: cut with an ellipsis.
        for (var i = 0; i < lines.Length; i++)
        {
            if (font.MeasureText(lines[i]) <= area.Width)
                continue;
            var text = lines[i];
            while (text.Length > 1 && font.MeasureText(text + "…") > area.Width)
                text = text[..^1];
            lines[i] = text.TrimEnd() + "…";
        }

        var lineHeight = font.Spacing;
        var y = area.MidY - lineHeight * lines.Length / 2 - font.Metrics.Ascent;
        foreach (var line in lines)
        {
            canvas.DrawText(line, area.MidX, y, SKTextAlign.Center, font, paint);
            y += lineHeight;
        }
    }

    /// <summary>Loads a PNG/JPEG/… or SVG icon; SVGs are rasterised at <paramref name="svgSize"/>.</summary>
    private static SKBitmap? LoadIcon(string? path, int svgSize = 128)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = ExpandHome(path);
        if (!File.Exists(path))
            return null;
        try
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? RasterizeSvg(path, svgSize) : SKBitmap.Decode(path);
        }
        catch (Exception)
        {
            // Broken or unsupported image: draw the key without it.
            return null;
        }
    }

    private static SKBitmap? RasterizeSvg(string path, int size)
    {
        using var svg = new Svg.Skia.SKSvg();
        if (svg.Load(path) is not { } picture || picture.CullRect.Width <= 0 || picture.CullRect.Height <= 0)
            return null;
        var bounds = picture.CullRect;
        var scale = size / Math.Max(bounds.Width, bounds.Height);
        var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        canvas.Translate((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        return bmp;
    }

    /// <summary>PNG of just an icon file, scaled to fit <paramref name="size"/> (for app lists).</summary>
    public static byte[]? RenderIconPng(string path, int size)
    {
        using var icon = LoadIcon(path, size * 2);
        if (icon is null)
            return null;
        using var bmp = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var img = SKImage.FromBitmap(icon);
            canvas.DrawImage(img, Fit(icon.Width, icon.Height, SKRect.Create(0, 0, size, size)),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKRect Fit(int w, int h, SKRect area)
    {
        var scale = Math.Min(area.Width / w, area.Height / h);
        var fw = w * scale;
        var fh = h * scale;
        return SKRect.Create(area.MidX - fw / 2, area.MidY - fh / 2, fw, fh);
    }

    public static SKColor ParseColor(string? text, SKColor fallback) =>
        SKColor.TryParse(text, out var c) ? c : fallback;

    public static string ExpandHome(string path) =>
        path == "~" || path.StartsWith("~/", StringComparison.Ordinal)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..]
            : path;
}
