// =============================================================================
//  ChronicleKnights — UserInterface/TextureDiskLoader.cs
// -----------------------------------------------------------------------------
//  Shared two-stage, import-independent, never-crashing texture resolver used by
//  the Job / Background / Enemy texture libraries. Centralizes the load policy so
//  the three libraries do not each duplicate it (single source of truth).
//
//  Resolution:
//    1. ResourceLoader (the imported .ctex, available in editor/exported builds
//       once Godot has imported the asset).
//    2. Raw-disk decode whose format is detected from the file *content* (magic
//       bytes), NOT the file extension. This matters because tools like Scenario
//       often export JPEG even when the file is saved with a ".png" name; Godot's
//       extension-based Image.LoadFromFile would then fail to decode it (PNG
//       decoder on JPEG bytes -> empty image -> blank slot). Sniffing the content
//       lets a JPEG/WebP saved as ".png" still load. Unknown content falls back to
//       the extension-based loader (covers bmp/tga/svg/etc.).
//
//  A genuinely absent or undecodable asset returns null -> the caller leaves the
//  TextureRect empty (graceful; the screen looks as it did before any art existed).
//
//  Constitution I: identifiers / paths / comments stay ASCII; no Japanese here.
// =============================================================================

using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>Resolves a res:// texture path to a Texture2D, decoding by content (or null if absent).</summary>
public static class TextureDiskLoader
{
    /// <summary>
    /// Load a Texture2D for a res:// path. Tries the imported resource first, then a
    /// content-sniffing raw-disk decode (extension-agnostic). Null only when the asset
    /// is missing or cannot be decoded.
    /// </summary>
    public static Texture2D? Resolve(string resPath)
    {
        if (string.IsNullOrEmpty(resPath)) return null;

        // 1. Imported resource (preferred when present).
        if (ResourceLoader.Exists(resPath))
        {
            var loaded = ResourceLoader.Load<Texture2D>(resPath);
            if (loaded is not null) return loaded;
        }

        // 2. Raw-disk decode by content (works from source before Godot imports).
        var diskPath = ProjectSettings.GlobalizePath(resPath);
        if (!System.IO.File.Exists(diskPath)) return null;

        var image = DecodeByContent(diskPath);
        return image is not null && !image.IsEmpty()
            ? ImageTexture.CreateFromImage(image)
            : null;
    }

    /// <summary>Decode an image file by sniffing its magic bytes (falls back to extension-based load).</summary>
    private static Image? DecodeByContent(string diskPath)
    {
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(diskPath); }
        catch { return null; }
        if (bytes.Length < 4) return null;

        var image = new Image();
        Error err;
        if (IsPng(bytes)) err = image.LoadPngFromBuffer(bytes);
        else if (IsJpeg(bytes)) err = image.LoadJpgFromBuffer(bytes);
        else if (IsWebp(bytes)) err = image.LoadWebpFromBuffer(bytes);
        else return Image.LoadFromFile(diskPath); // bmp/tga/svg/etc.: extension-based fallback

        return err == Error.Ok ? image : null;
    }

    // ─── content magic-byte sniffers ─────────────────────────────────────────

    private static bool IsPng(byte[] b)
        => b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;

    private static bool IsJpeg(byte[] b)
        => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    private static bool IsWebp(byte[] b)
        => b.Length >= 12
           && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
           && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';
}
