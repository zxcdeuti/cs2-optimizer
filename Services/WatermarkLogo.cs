using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CS2Optimizer.Services;

/// <summary>
/// The user's own watermark icon. Their picture is converted once into a small PNG in the app's data folder, and the
/// settings keep only its file name: the watermark usually runs elevated, so it must never decode an arbitrary file.
/// </summary>
public static class WatermarkLogo
{
    const int MaxSide = 256;
    const long MaxFileBytes = 30 * 1024 * 1024;
    const long MaxPixels = 64_000_000;

    public const string FileFilter = "Картинки|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.webp;*.tif;*.tiff|Все файлы|*.*";

    static readonly string Dir = Path.Combine(AppSettings.DataDir, "logos");

    /// <summary>Returns the stored file's name.</summary>
    /// <exception cref="InvalidDataException">Not a picture this PC can read; the message is ready to show.</exception>
    public static string Import(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("Файл больше 30 МБ, возьми картинку поменьше.");
        byte[] png;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.None);
            // An .ico holds several sizes; the largest one looks best after scaling.
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels) throw new InvalidDataException("Картинка слишком большая, возьми поменьше.");
            png = Encode(frame);
        }
        catch (Exception e) when (e is NotSupportedException or FileFormatException or ArgumentException or COMException or OverflowException or InvalidOperationException)
        {
            throw new InvalidDataException("Не получилось прочитать картинку: файл повреждён или такой формат Windows не открывает.", e);
        }

        var name = $"logo-{Convert.ToHexString(SHA256.HashData(png))[..16].ToLowerInvariant()}.png";
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(Path.Combine(Dir, name), png);
        return name;
    }

    /// <summary>Scaled down to fit and turned the way a phone photo should stand; the proportions stay, shapes are cut when shown.</summary>
    static byte[] Encode(BitmapFrame frame)
    {
        int turn = Orientation(frame) switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        bool sideways = turn is 90 or 270;
        double sourceW = frame.PixelWidth, sourceH = frame.PixelHeight;
        double k = Math.Min(1, MaxSide / Math.Max(sourceW, sourceH));
        int width = Math.Max(1, (int)Math.Round((sideways ? sourceH : sourceW) * k));
        int height = Math.Max(1, (int)Math.Round((sideways ? sourceW : sourceH) * k));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new RotateTransform(turn, width / 2.0, height / 2.0));
            double drawW = sourceW * k, drawH = sourceH * k;
            dc.DrawImage(frame, new Rect((width - drawW) / 2, (height - drawH) / 2, drawW, drawH));
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>The EXIF orientation of a photo; other formats simply have none.</summary>
    static ushort? Orientation(BitmapFrame frame)
    {
        try { return (frame.Metadata as BitmapMetadata)?.GetQuery("/app1/ifd/{ushort=274}") as ushort?; }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or COMException) { return null; }
    }

    /// <summary>Null when there is no custom icon or its file can't be used; the watermark then falls back to the app's mark.</summary>
    public static BitmapSource? Load(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        try
        {
            using var stream = File.OpenRead(Path.Combine(Dir, Path.GetFileName(name)));
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Removes icons that are no longer in use.</summary>
    public static void RemoveExcept(string? keep)
    {
        if (!Directory.Exists(Dir)) return;
        foreach (var file in Directory.GetFiles(Dir, "logo-*.png"))
        {
            if (Path.GetFileName(file) == keep) continue;
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // stays until the next change
        }
    }
}
