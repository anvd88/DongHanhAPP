using WpfMedia = System.Windows.Media;
using WpfImaging = System.Windows.Media.Imaging;

namespace KetoanMini;

/// <summary>
/// File-based avatar storage. Avatars are saved as square 128×128 PNGs under
/// %AppData%/KetoanMini/avatars/{username}.png so no database migration is needed.
/// </summary>
public static class AvatarStore
{
    private const int AvatarSize = 128;

    private static string Dir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KetoanMini", "avatars");

    private static string Sanitize(string username)
    {
        var name = (username ?? "").Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "user" : name;
    }

    private static string PathFor(string username) =>
        Path.Combine(Dir, Sanitize(username) + ".png");

    public static bool Has(string username) => File.Exists(PathFor(username));

    /// <summary>Loads the avatar as a WPF image source with no file lock, or null.</summary>
    public static WpfMedia.ImageSource? Load(string username)
    {
        try
        {
            var path = PathFor(username);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            var image = new WpfImaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = WpfImaging.BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Square-crops and resizes the source image, then saves it as the user's avatar.</summary>
    public static void Save(string username, string sourcePath)
    {
        Directory.CreateDirectory(Dir);
        using var input = File.OpenRead(sourcePath);
        var decoder = WpfImaging.BitmapDecoder.Create(input, WpfImaging.BitmapCreateOptions.PreservePixelFormat, WpfImaging.BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        var side = Math.Min(source.PixelWidth, source.PixelHeight);
        var crop = new WpfImaging.CroppedBitmap(source, new System.Windows.Int32Rect(
            Math.Max(0, (source.PixelWidth - side) / 2),
            Math.Max(0, (source.PixelHeight - side) / 2),
            side,
            side));
        var scale = AvatarSize / (double)side;
        var resized = new WpfImaging.TransformedBitmap(crop, new WpfMedia.ScaleTransform(scale, scale));
        var encoder = new WpfImaging.PngBitmapEncoder();
        encoder.Frames.Add(WpfImaging.BitmapFrame.Create(resized));
        using var output = File.Create(PathFor(username));
        encoder.Save(output);
    }

    public static void Delete(string username)
    {
        try
        {
            var path = PathFor(username);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

}
