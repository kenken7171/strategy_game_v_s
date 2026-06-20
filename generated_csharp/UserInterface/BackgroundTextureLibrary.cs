// =============================================================================
//  ChronicleKnights — UserInterface/BackgroundTextureLibrary.cs
// -----------------------------------------------------------------------------
//  Stateless static factory that resolves a battlefield background Texture2D for
//  a given chronicle epoch. Assets live at
//    res://Assets/Textures/Backgrounds/{epoch_slug}.png
//  (slug = AssetSlugs.ForEpoch, the snake_case epoch id; see docs/ASSET_MANIFEST.md).
//
//  ★ Mirrors the proven JobTextureLibrary loader exactly (the 16 job portraits use
//    the same path): a two-stage, import-independent, never-crashing resolve.
//      1. ResourceLoader (imported .ctex, present in editor/exported builds).
//      2. Raw-disk fallback via Image.LoadFromFile (works from source before Godot
//         has generated import files).
//    A missing asset returns null -> the background TextureRect stays empty and the
//    battle screen looks exactly as it did before any art existed (graceful).
//
//  Constitution I: identifiers / paths / comments stay ASCII; no Japanese here.
// =============================================================================

using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Chronicle;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>Resolves a chronicle epoch to its battlefield background Texture2D (or null if absent).</summary>
public static class BackgroundTextureLibrary
{
    private const string BackgroundTextureRoot = "res://Assets/Textures/Backgrounds/";

    /// <summary>
    /// Load the battlefield background for an epoch. Returns null only when the slug is
    /// unknown or the asset is genuinely absent (the caller leaves the background empty).
    /// </summary>
    public static Texture2D? TryLoad(EpochId epoch)
    {
        var slug = AssetSlugs.ForEpoch(epoch);
        if (slug.Length == 0) return null;

        var resPath = $"{BackgroundTextureRoot}{slug}.png";

        // 1. Imported resource (preferred when present).
        if (ResourceLoader.Exists(resPath))
        {
            var loaded = ResourceLoader.Load<Texture2D>(resPath);
            if (loaded is not null)
            {
                return loaded;
            }
        }

        // 2. Decode the raw PNG from disk (no Godot import required).
        var diskPath = ProjectSettings.GlobalizePath(resPath);
        if (System.IO.File.Exists(diskPath))
        {
            var image = Image.LoadFromFile(diskPath);
            if (image is not null && !image.IsEmpty())
            {
                return ImageTexture.CreateFromImage(image);
            }
        }

        return null;
    }
}
