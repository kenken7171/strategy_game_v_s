// =============================================================================
//  ChronicleKnights — JobDescriptionView.cs
// -----------------------------------------------------------------------------
//  Faithful C# trace of the TS JobDescriptionView (packages/frontend/src/
//  components/JobDescriptionView.tsx). Builds one job's description block:
//
//    1. Role
//    2. Signature passives (summary + structured BDF/SDF/AB/HL/special list)
//    3. Recommended placement (mini wedge with recommended rows highlighted)
//       + headline + effect-range note
//    4. Tactical usage
//    5. Flavor (quote)
//
//  Pure static builder: returns a detached Control so the same block can be
//  embedded in both the Job Manual overlay (catalog) and the Unit Detail modal.
//  Reads display text from JobCodex (English) and structured data from JobMaster
//  (single SoT). Constitution I: ASCII only.
// =============================================================================

using ChronicleKnights.Core.Job;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Static builder for a single job's description block (English / ASCII).</summary>
public static class JobDescriptionView
{
    private const string TestIdMetaKey = "data_testid";

    /// <summary>Canonical wedge row order (top of the V to the rear corners).</summary>
    private static readonly SquadRow[] RowOrder =
        { SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight };

    private static string RowLabel(SquadRow row) => row switch
    {
        SquadRow.Front     => "FRONT",
        SquadRow.RearLeft  => "REAR-L",
        SquadRow.RearRight => "REAR-R",
        _ => row.ToString(),
    };

    /// <summary>ASCII glyph per effect kind (TS used emoji; ASCII here per Constitution I).</summary>
    private static string EffectGlyph(EffectKind kind) => kind switch
    {
        EffectKind.Buff   => "BUFF",
        EffectKind.Heal   => "HEAL",
        EffectKind.Defend => "DEF",
        EffectKind.Attack => "ATK",
        _ => "*",
    };

    /// <summary>
    /// Build the full description block for a job. Returns null for null/unknown jobs
    /// (the caller decides what to render in that case).
    /// </summary>
    public static Control? Build(JobId? jobId, string testIdPrefix = "job-description")
    {
        var text = JobCodex.TextFor(jobId);
        if (text is null || jobId is not { } id) return null;

        var def = JobMaster.Find(id);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 6);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.SetMeta(TestIdMetaKey, $"{testIdPrefix}-body-{id}");

        // ── Title row: display name + enum id ───────────────────────────────
        var title = new Label { Text = $"== {JobCodex.DisplayName(id)} ({id}) ==" };
        title.SetMeta(TestIdMetaKey, $"{testIdPrefix}-title-{id}");
        body.AddChild(title);

        // ── Role ────────────────────────────────────────────────────────────
        AddLabeled(body, "ROLE", text.Role, $"{testIdPrefix}-role-{id}");

        // ── Signature passives (summary + structured list) ──────────────────
        var passiveHeader = new Label { Text = "SIGNATURE PASSIVES" };
        passiveHeader.SetMeta(TestIdMetaKey, $"{testIdPrefix}-passives-header-{id}");
        body.AddChild(passiveHeader);

        var summary = new Label { Text = text.AbilitySummary, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summary.SetMeta(TestIdMetaKey, $"{testIdPrefix}-ability-summary-{id}");
        body.AddChild(summary);

        var passives = JobCodex.Passives(id);
        if (passives.Length == 0)
        {
            var none = new Label { Text = "- No numeric passive (works on pure combat stats)" };
            none.SetMeta(TestIdMetaKey, $"{testIdPrefix}-passives-empty-{id}");
            body.AddChild(none);
        }
        else
        {
            foreach (var p in passives)
            {
                var valueText = p.Value is { } v ? $"  (value: {v})" : string.Empty;
                var line = new Label
                {
                    Text = $"- {p.Label}{valueText}\n    target: {p.Scope}\n    {p.Description}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                line.SetMeta(TestIdMetaKey, $"{testIdPrefix}-passive-{p.Key}-{id}");
                body.AddChild(line);
            }
        }

        // ── Recommended placement (mini wedge) ──────────────────────────────
        if (def is not null)
        {
            var guide = def.FormationGuide;

            var guideHeader = new Label { Text = "RECOMMENDED PLACEMENT" };
            guideHeader.SetMeta(TestIdMetaKey, $"{testIdPrefix}-guide-header-{id}");
            body.AddChild(guideHeader);

            var glyph = EffectGlyph(guide.EffectKind);
            foreach (var row in RowOrder)
            {
                var isRecommended = guide.RecommendedRows.Contains(row);
                var inEffect = guide.EffectRange.Contains(row);
                var cells = isRecommended
                    ? $"[{glyph}] [{glyph}] [{glyph}]"
                    : "[ ] [ ] [ ]";
                var recTag = isRecommended ? "  <= recommended" : (inEffect ? "  (in range)" : string.Empty);
                var rowLabel = new Label { Text = $"{RowLabel(row),-7} {cells}{recTag}" };
                rowLabel.SetMeta(TestIdMetaKey, $"{testIdPrefix}-guide-row-{row}-{id}");
                body.AddChild(rowLabel);
            }

            var headline = new Label { Text = $"[{text.FormationHeadline}]", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            headline.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            headline.SetMeta(TestIdMetaKey, $"{testIdPrefix}-guide-headline-{id}");
            body.AddChild(headline);

            var rangeNote = new Label { Text = $"Effect range: {text.EffectRangeNote}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            rangeNote.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rangeNote.SetMeta(TestIdMetaKey, $"{testIdPrefix}-guide-range-{id}");
            body.AddChild(rangeNote);
        }

        // ── Tactical usage ──────────────────────────────────────────────────
        AddLabeled(body, "USAGE", text.Usage, $"{testIdPrefix}-usage-{id}");

        // ── Flavor ──────────────────────────────────────────────────────────
        var flavor = new Label
        {
            Text = $"\"{text.Flavor}\"",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        flavor.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        flavor.AddThemeColorOverride("font_color", Colors.LightGoldenrod);
        flavor.SetMeta(TestIdMetaKey, $"{testIdPrefix}-flavor-{id}");
        body.AddChild(flavor);

        return body;
    }

    private static void AddLabeled(VBoxContainer parent, string label, string value, string testId)
    {
        var l = new Label { Text = $"{label}: {value}", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.SetMeta(TestIdMetaKey, testId);
        parent.AddChild(l);
    }
}
