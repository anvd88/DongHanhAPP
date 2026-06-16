using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

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

    /// <summary>Loads the avatar as an independent Bitmap (no file lock), or null.</summary>
    public static Image? Load(string username)
    {
        try
        {
            var path = PathFor(username);
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
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
        byte[] bytes = File.ReadAllBytes(sourcePath);
        using var ms = new MemoryStream(bytes);
        using var src = Image.FromStream(ms);
        using var square = CropSquare(src, AvatarSize);
        square.Save(PathFor(username), ImageFormat.Png);
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

    private static Bitmap CropSquare(Image src, int size)
    {
        int side = Math.Min(src.Width, src.Height);
        int sx = (src.Width - side) / 2;
        int sy = (src.Height - side) / 2;

        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
        return bmp;
    }

    /// <summary>Draws an image clipped to a circle inside the given rectangle.</summary>
    public static void DrawCircular(Graphics g, Image img, Rectangle rect)
    {
        var saved = g.Save();
        try
        {
            using var clip = new GraphicsPath();
            clip.AddEllipse(rect);
            g.SetClip(clip);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, rect);
        }
        finally
        {
            g.Restore(saved);
        }
    }
}
