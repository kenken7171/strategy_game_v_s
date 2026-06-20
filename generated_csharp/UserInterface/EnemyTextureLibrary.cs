// =============================================================================
//  ChronicleKnights — UserInterface/EnemyTextureLibrary.cs
// -----------------------------------------------------------------------------
//  Stateless static factory that resolves an enemy illustration Texture2D for a
//  given enemy archetype. Assets live at
//    res://Assets/Textures/Enemies/{archetype_slug}.png
//  (slug = AssetSlugs.ForEnemy, the snake_case archetype id; see docs/ASSET_MANIFEST.md).
//
//  ★ Mirrors the proven JobTextureLibrary loader exactly: a two-stage,
//    import-independent, never-crashing resolve (ResourceLoader -> raw-disk PNG).
//    A missing asset returns null -> the enemy portrait TextureRect stays empty and
//    the battle screen falls back to the text/number presentation (graceful).
//
//  Constitution I: identifiers / paths / comments stay ASCII; no Japanese here.
// =============================================================================

using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Battle;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>Resolves an enemy archetype to its illustration Texture2D (or null if absent).</summary>
public static class EnemyTextureLibrary
{
    private const string EnemyTextureRoot = "res://Assets/Textures/Enemies/";

    /// <summary>
    /// Load the illustration for an enemy archetype. Returns null only when the slug is
    /// unknown or the asset is genuinely absent (the caller leaves the portrait empty).
    /// </summary>
    public static Texture2D? TryLoad(EnemyArchetype archetype)
    {
        var slug = AssetSlugs.ForEnemy(archetype);
        if (slug.Length == 0) return null;

        var resPath = $"{EnemyTextureRoot}{slug}.png";

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
