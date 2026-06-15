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
    /// Load the illustration for a job + gender. Returns null when the slug is
    /// unknown or the asset is not present (sentinel guard; caller renders empty).
    /// </summary>
    public static Texture2D? TryLoad(JobId job, Gender gender = Gender.Male)
    {
        var slug = Slug(job);
        if (slug.Length == 0) return null;

        var file = gender == Gender.Female ? "female" : "male";
        var path = $"{JobTextureRoot}{slug}/{file}.png";
        if (!ResourceLoader.Exists(path))
        {
            return null;
        }
        return ResourceLoader.Load<Texture2D>(path);
    }
}
