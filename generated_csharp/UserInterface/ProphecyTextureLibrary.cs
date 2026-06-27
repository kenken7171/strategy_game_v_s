// =============================================================================
//  ChronicleKnights — UserInterface/ProphecyTextureLibrary.cs
// -----------------------------------------------------------------------------
//  Stateless static factory that resolves a prophecy (selection) card illustration
//  Texture2D for a given ProphecyKind. Assets live at
//    res://Assets/Textures/Prophecies/{kind_slug}.png
//  (slug = AssetSlugs.ForProphecyKind, the snake_case kind id; see docs/ASSET_MANIFEST.md).
//
//  ★ Same proven two-stage, import-independent, content-sniffing resolve as the
//    Job / Background / Enemy libraries (via TextureDiskLoader). A missing asset
//    returns null -> the card keeps its text/emoji presentation (graceful).
//
//  Constitution I: identifiers / paths / comments stay ASCII; no Japanese here.
// =============================================================================

using ChronicleKnights.Core.Assets;
using ChronicleKnights.Core.Timeline;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>Resolves a prophecy kind to its selection-card illustration Texture2D (or null if absent).</summary>
public static class ProphecyTextureLibrary
{
    private const string ProphecyTextureRoot = "res://Assets/Textures/Prophecies/";

    /// <summary>
    /// Load the illustration for a prophecy kind. Returns null when the slug is unknown
    /// or the asset is genuinely absent (the caller leaves the card art empty).
    /// </summary>
    public static Texture2D? TryLoad(ProphecyKind kind)
    {
        var slug = AssetSlugs.ForProphecyKind(kind);
        if (slug.Length == 0) return null;

        // Two-stage, content-sniffing resolve (a JPEG/WebP saved as .png still loads).
        return TextureDiskLoader.Resolve($"{ProphecyTextureRoot}{slug}.png");
    }
}
