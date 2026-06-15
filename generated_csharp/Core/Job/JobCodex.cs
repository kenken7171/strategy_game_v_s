// =============================================================================
//  ChronicleKnights — JobCodex.cs
// -----------------------------------------------------------------------------
//  Faithful C# trace of the TypeScript (web) job manual content
//  (packages/core/src/data/jobs.ts JOB_ABILITY / JOB_FORMATION_GUIDE and
//   packages/frontend/src/utils/job.ts explainJobPassives).
//
//  This is the SINGLE SOURCE OF TRUTH for the *display text* of the Job Manual
//  and the per-unit Job Description section. It holds ONLY English text
//  (Constitution I: ASCII only; Japanese localization is deferred to a future
//  localization layer). All NUMERIC values (BDF/SDF/AB/HL) and the structured
//  formation guide (recommended rows / effect range / effect kind) are read from
//  JobMaster at call time, so numbers stay single-sourced and never drift.
//
//  Mirrors the TS structure exactly:
//    - JobCodexText : role / ability summary / usage / flavor / headline / range note
//    - JobPassiveLine : the "structured passive" rows (bdf/sdf/ab/hl/special-*)
//                       with label, value, scope and description (English).
// =============================================================================

using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Job;

/// <summary>
/// The prose block for one job: role, ability summary, tactical usage, flavor,
/// and the formation headline / effect-range note. English only (Constitution I).
/// </summary>
public sealed record JobCodexText
{
    /// <summary>One-line role summary (the player's first impression).</summary>
    public required string Role { get; init; }

    /// <summary>Mechanical summary of the signature ability (numeric spec).</summary>
    public required string AbilitySummary { get; init; }

    /// <summary>Tactical usage / recommended placement.</summary>
    public required string Usage { get; init; }

    /// <summary>World-flavor text.</summary>
    public required string Flavor { get; init; }

    /// <summary>Formation-guide headline (e.g. "Recommended: front line").</summary>
    public required string FormationHeadline { get; init; }

    /// <summary>Supplementary note on the effect range.</summary>
    public required string EffectRangeNote { get; init; }
}

/// <summary>
/// One structured passive row. Mirrors the TS PassiveExplanation:
/// key is stable for testids ("bdf"|"sdf"|"ab"|"hl"|"special-double-strike").
/// </summary>
public sealed record JobPassiveLine
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public int? Value { get; init; }
    public required string Scope { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Pure, Godot-independent catalog of job manual text + a passive explainer that
/// derives the structured passive list from JobMaster numeric stats.
/// </summary>
public static class JobCodex
{
    // ─── English display names (ASCII; JP deferred to localization) ──────────

    private static readonly IReadOnlyDictionary<JobId, string> DisplayNames =
        new Dictionary<JobId, string>
        {
            [JobId.IronWallKnight] = "Iron Wall Knight",
            [JobId.HeavyInfantry]  = "Heavy Infantry",
            [JobId.StandardBearer] = "Standard Bearer",
            [JobId.Tactician]      = "Tactician",
            [JobId.Medic]          = "Medic",
            [JobId.Sniper]         = "Sniper",
            [JobId.Sorcerer]       = "Sorcerer",
            [JobId.Scout]          = "Scout",
        };

    /// <summary>Human-readable English job name. Falls back to the enum name.</summary>
    public static string DisplayName(JobId id)
        => DisplayNames.TryGetValue(id, out var name) ? name : id.ToString();

    // ─── Manual prose (faithful English trace of TS JOB_ABILITY + guide text) ─

    private static readonly IReadOnlyDictionary<JobId, JobCodexText> Texts =
        new Dictionary<JobId, JobCodexText>
        {
            [JobId.IronWallKnight] = new JobCodexText
            {
                Role           = "Front-line shield. A heavy knight who guards the whole battalion.",
                AbilitySummary = "Battalion Guard (in FRONT, every ally takes -10 damage) + Squad Guard (own squad takes -15 damage). Stacks when you field several.",
                Usage          = "Place at the front center to minimize harm to your rear attackers. Fielding several doubles your sturdiness.",
                Flavor         = "Bearer of an ancient oath. I make a shield of my body so those behind me may live.",
                FormationHeadline = "Recommended: front line (Battalion Guard activation)",
                EffectRangeNote   = "Battalion Guard reaches the whole battalion only when in FRONT / Squad Guard is own squad only.",
            },
            [JobId.HeavyInfantry] = new JobCodexText
            {
                Role           = "Self-sufficient front-line attacker.",
                AbilitySummary = "Highest HP of all jobs (300) + high front attack (FA=70) + Squad Guard (SDF=10).",
                Usage          = "Place beside the Iron Wall Knight as an unbreakable shield that also deals damage. Strong in long battles.",
                Flavor         = "Unbreaking armor, unyielding will. Holds the line until the last man stands.",
                FormationHeadline = "Recommended: front line (the unbreaking shield)",
                EffectRangeNote   = "Squad Guard=10 mitigates own squad / high HP + high FA holds the front.",
            },
            [JobId.StandardBearer] = new JobCodexText
            {
                Role           = "Spiritual pillar that raises the whole battalion's firepower.",
                AbilitySummary = "Rally Order=40 (at turn start, grants every other ally SPD+40 / ATK+40). Stackable.",
                Usage          = "Works in front or rear. Field several to make the entire battalion monstrous.",
                Flavor         = "The waving banner rekindles morale. One who lights a fire in every heart.",
                FormationHeadline = "Recommended: anywhere (empowers the whole battalion)",
                EffectRangeNote   = "Rally Order=40 grants SPD+40 / ATK+40 to every other ally in the battalion.",
            },
            [JobId.Tactician] = new JobCodexText
            {
                Role           = "Speed-leaning lightweight buffer.",
                AbilitySummary = "Rally Order=20 (SPD+20 / ATK+20) + a moderate own Speed (SPD=35).",
                Usage          = "More modest than the Standard Bearer, but carries a speed value, so combined use boosts initiative.",
                Flavor         = "A cold intellect that reads the pieces on the board. One command can turn the tide.",
                FormationHeadline = "Recommended: anywhere (lightweight buffer)",
                EffectRangeNote   = "Rally Order=20 grants SPD+20 / ATK+20 to every other ally in the battalion.",
            },
            [JobId.Medic] = new JobCodexText
            {
                Role           = "The only sustained healer.",
                AbilitySummary = "Squad Heal=30 (at turn end, heals every living member of own squad +30 HP). Up to max HP.",
                Usage          = "Always place in a fragile rear squad; a necessity even up front. The lifeline of long battles.",
                Flavor         = "The angel of the battlefield, or the last hope. Beyond the healing lies a new future.",
                FormationHeadline = "Recommended: fragile rear (sustained squad healing)",
                EffectRangeNote   = "Squad Heal=30 is own squad only. Heals every survivor in the placed squad.",
            },
            [JobId.Sniper] = new JobCodexText
            {
                Role           = "Rear super-firepower and a double-shot artillery piece.",
                AbilitySummary = "RA=90 (rear attack 90). If first in initiative and fastest in the squad, fires a Second Arrow (double strike).",
                Usage          = "Must be placed in REAR-L / REAR-R. Raise the double-shot chance with Tactician / Standard Bearer speed buffs.",
                Flavor         = "Listens to the wind, holds the breath. A single arrow that changes the battle.",
                FormationHeadline = "Recommended: rear (the double-shot artillery)",
                EffectRangeNote   = "RA=90 gives full power only in the rear / Second Arrow (1st initiative, front slot) explodes when it lands.",
            },
            [JobId.Sorcerer] = new JobCodexText
            {
                Role           = "The strongest firepower of all jobs, yet the most fragile artillery.",
                AbilitySummary = "RA=120 (rear attack 120, strongest of all). HP=40, weakest of all jobs.",
                Usage          = "Must be placed in the rear. A high-risk high-reward card to settle the fight before the front collapses.",
                Flavor         = "A wielder of ancient forbidden spells. Pours life into a single magic bullet, or has life taken in return.",
                FormationHeadline = "Recommended: rear (high risk, high reward)",
                EffectRangeNote   = "RA=120 is the strongest artillery of all / HP=40 makes the front line impossible.",
            },
            [JobId.Scout] = new JobCodexText
            {
                Role           = "The fastest pre-emptive chipper.",
                AbilitySummary = "SPD=60 (fastest of all jobs) + even front/rear FA=RA=40 for high placement freedom.",
                Usage          = "Place in the rear to seize initiative and blunt the enemy's opening. Its top speed can also support the Tactician.",
                Flavor         = "Moves faster than anyone, sees the enemy before anyone. Races across the field like the wind.",
                FormationHeadline = "Recommended: anywhere (the fastest pre-emptive chipper)",
                EffectRangeNote   = "FA=RA=40 means free placement / SPD=60 always seizes initiative.",
            },
        };

    /// <summary>Manual prose for a job (never null for the 8 defined jobs).</summary>
    public static JobCodexText? TextFor(JobId? id)
        => id is { } jid && Texts.TryGetValue(jid, out var t) ? t : null;

    // ─── Structured passive explainer (numbers sourced from JobMaster) ───────
    //
    //  Mirrors the TS explainJobPassives: emit a row only for non-zero numeric
    //  passives (BDF/SDF/AB/HL), then append the special passive (sniper's
    //  Second Arrow) if the job declares it.

    /// <summary>
    /// Build the structured passive list for a job, reading numeric values from
    /// JobMaster (single SoT for numbers) and English labels/scope/desc from here.
    /// Returns an empty list for null/unknown jobs.
    /// </summary>
    public static ImmutableArray<JobPassiveLine> Passives(JobId? id)
    {
        var def = JobMaster.Find(id);
        if (def is null) return ImmutableArray<JobPassiveLine>.Empty;

        var stats = def.Stats;
        var builder = ImmutableArray.CreateBuilder<JobPassiveLine>();

        if (stats.BattalionDefense > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "bdf",
                Label       = "Battalion Guard",
                Value       = stats.BattalionDefense,
                Scope       = "Activates only when in FRONT / effect reaches the whole battalion",
                Description = "Reduces damage taken by every ally (reaches other squads too).",
            });
        }
        if (stats.SquadDefense > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "sdf",
                Label       = "Squad Guard",
                Value       = stats.SquadDefense,
                Scope       = "Always active / effect is own squad only",
                Description = "Reduces damage taken by the squad you belong to.",
            });
        }
        if (stats.InitiativeBuff > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "ab",
                Label       = "Rally Order",
                Value       = stats.InitiativeBuff,
                Scope       = "Every turn start / every other ally in the battalion",
                Description = "Spreads a turn-start speed and attack buff (stackable).",
            });
        }
        if (stats.TurnEndSquadHeal > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "hl",
                Label       = "Squad Heal",
                Value       = stats.TurnEndSquadHeal,
                Scope       = "Every turn end / every survivor of own squad",
                Description = "Heals the squad's HP at turn end (capped at max HP).",
            });
        }

        if (def.SpecialPassives.Contains(PassiveKind.ConsecutiveStrike))
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "special-double-strike",
                Label       = "Second Arrow (double strike)",
                Value       = null,
                Scope       = "Only when 1st in initiative AND in the squad's front slot",
                Description = "The normal attack is performed twice (a follow-up within one turn).",
            });
        }

        return builder.ToImmutable();
    }
}
