// =============================================================================
//  ChronicleKnights — UserInterface/JobTextureLibrary.cs
// -----------------------------------------------------------------------------
//  Stateless static factory that safely resolves a job illustration Texture2D
//  via ResourceLoader. Assets live at res://Assets/Jobs/{slug}/{male|female}.png
//  (pixel art mirrored from the TS web public/image tree).
//
//  ★ Safe load (never crashes on missing asset):
//    ResourceLoader.Exists is checked before Load; a missing asset returns null
//    (the TextureRect renders empty). The game boots healthy even before Godot
//    has imported the PNGs.
//
//  ★ Leak-free: the returned Texture2D is a ref-counted Godot resource; the
//    caller only assigns it to a TextureRect, freed when that node is QueueFree'd.
//
//  Constitution I: identifiers / paths / comments stay ASCII (the asset slugs
//  are the TS snake_case job ids; no Japanese here).
// =============================================================================

using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Naming;
using Godot;

namespace ChronicleKnights.UserInterface;

/// <summary>Resolves a job + gender to its illustration Texture2D (or null if absent).</summary>
public static class JobTextureLibrary
{
    private const string JobTextureRoot = "res://Assets/Jobs/";

    /// <summary>ASCII asset slug per job (matches the res://Assets/Jobs/{slug} folders).</summary>
    private static string Slug(JobId job) => job switch
    {
        JobId.IronWallKnight => "iron_wall_knight",
        JobId.HeavyInfantry  => "heavy_infantry",
        JobId.StandardBearer => "standard_bearer",
        JobId.Tactician      => "tactician",
        JobId.Medic          => "medic",
        JobId.Sniper         => "sniper",
        JobId.Sorcerer       => "sorcerer",
        JobId.Scout          => "scout",
        _ => string.Empty,
    };

    /// <summary>
    /// Load the illustration for a job + gender. The gender read is preserved
    /// (female -> female.png, otherwise male.png). Returns null only when the
    /// slug is unknown or the asset is genuinely absent.
    ///
    /// Two-stage resolution (import-independent):
    ///   1. ResourceLoader (the imported .ctex; available once Godot has imported
    ///      the PNG, e.g. in an exported build or after an editor import).
    ///   2. Raw-disk fallback: globalize res:// to a filesystem path and decode the
    ///      PNG directly via Image.LoadFromFile. This makes the illustration appear
    ///      even when running from source before Godot has generated import files
    ///      (which is exactly why the placeholder was showing).
    /// </summary>
    public static Texture2D? TryLoad(JobId job, Gender gender = Gender.Male)
    {
        var slug = Slug(job);
        if (slug.Length == 0) return null;

        var file = gender == Gender.Female ? "female" : "male";
        var resPath = $"{JobTextureRoot}{slug}/{file}.png";

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
