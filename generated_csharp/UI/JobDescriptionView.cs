// =============================================================================
//  ChronicleKnights — JobDescriptionView.cs
// -----------------------------------------------------------------------------
//  Builds one job's description block: a job illustration (TextureRect) beside a
//  BBCode RichTextLabel (role / signature passives / recommended placement /
//  tactical usage / flavor). Shared by the Job Manual catalog and the Unit
//  Detail modal.
//
//  ★ Language exception (this command only): UI display text is Japanese (UTF-8).
//    Identifiers / comments stay ASCII. Numbers come from JobMaster (single SoT).
//  ★ Emphasis is BBCode (bold / color / gold passive values), not emoji, so the
//    Godot default font renders cleanly (no tofu glyphs).
// =============================================================================

using System.Text;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.UserInterface; // JobTextureLibrary
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Static builder for a single job's description block (Japanese / BBCode + art).</summary>
public static class JobDescriptionView
{
    private const string TestIdMetaKey = "data_testid";

    private static readonly SquadRow[] RowOrder =
        { SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight };

    // BBCode color palette (visibility: section headers, gold numbers, kind hues).
    private const string HeaderColor = "#7fd0ff";
    private const string GoldColor   = "#ffd24a";
    private const string FlavorColor = "#d8b86a";

    private static string RowLabel(SquadRow row) => row switch
    {
        SquadRow.Front     => "前衛",
        SquadRow.RearLeft  => "後衛-左",
        SquadRow.RearRight => "後衛-右",
        _ => row.ToString(),
    };

    private static string EffectColor(EffectKind kind) => kind switch
    {
        EffectKind.Defend => "#6c9cff",
        EffectKind.Buff   => "#ffd24a",
        EffectKind.Heal   => "#5fd97a",
        EffectKind.Attack => "#ff7a5a",
        _ => "#cccccc",
    };

    /// <summary>
    /// Build the full description block for a job. <paramref name="gender"/> selects
    /// the illustration (null -> Male). Returns null for null/unknown jobs.
    /// </summary>
    public static Control? Build(JobId? jobId, Gender? gender = null, string testIdPrefix = "job-description")
    {
        var text = JobCodex.TextFor(jobId);
        if (text is null || jobId is not { } id) return null;

        var def = JobMaster.Find(id);

        var root = new HBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        root.SetMeta(TestIdMetaKey, $"{testIdPrefix}-body-{id}");

        // ── Illustration (left) ─────────────────────────────────────────────
        var texture = JobTextureLibrary.TryLoad(id, gender ?? Gender.Male);
        if (texture is not null)
        {
            var art = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(128, 128),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            art.SetMeta(TestIdMetaKey, $"{testIdPrefix}-art-{id}");
            root.AddChild(art);
        }
        else
        {
            var placeholder = new Label
            {
                Text = "［画像準備中］",
                CustomMinimumSize = new Vector2(128, 128),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            placeholder.SetMeta(TestIdMetaKey, $"{testIdPrefix}-art-placeholder-{id}");
            root.AddChild(placeholder);
        }

        // ── Description text (right, BBCode) ────────────────────────────────
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(360, 0),
        };
        label.SetMeta(TestIdMetaKey, $"{testIdPrefix}-text-{id}");
        label.Text = BuildBbcode(id, text, def);
        root.AddChild(label);

        return root;
    }

    private static string BuildBbcode(JobId id, JobCodexText text, JobDefinition? def)
    {
        var sb = new StringBuilder();

        // Title: job display name + ascii id.
        sb.Append("[b][font_size=22]").Append(JobCodex.DisplayName(id)).Append("[/font_size][/b]");
        sb.Append("  [color=#888888](").Append(id).Append(")[/color]\n\n");

        // Role.
        Header(sb, "役割");
        sb.Append(text.Role).Append("\n\n");

        // Signature passives.
        Header(sb, "署名パッシブ");
        sb.Append(text.AbilitySummary).Append('\n');
        var passives = JobCodex.Passives(id);
        if (passives.Length == 0)
        {
            sb.Append("[color=#999999]・数値プロパティ上のパッシブはなし（純戦闘ステータスで機能）[/color]\n");
        }
        else
        {
            foreach (var p in passives)
            {
                sb.Append("・[b]").Append(p.Label).Append("[/b]");
                if (p.Value is { } v)
                {
                    sb.Append("　値: [color=").Append(GoldColor).Append("][b]").Append(v).Append("[/b][/color]");
                }
                sb.Append('\n');
                sb.Append("　　[color=#aaaaaa]対象: ").Append(p.Scope).Append("[/color]\n");
                sb.Append("　　").Append(p.Description).Append('\n');
            }
        }
        sb.Append('\n');

        // Recommended placement (text wedge).
        if (def is not null)
        {
            var guide = def.FormationGuide;
            var kindColor = EffectColor(guide.EffectKind);
            Header(sb, "推奨配置");
            foreach (var row in RowOrder)
            {
                var isRecommended = guide.RecommendedRows.Contains(row);
                var inEffect = guide.EffectRange.Contains(row);
                var cells = isRecommended ? "■ ■ ■" : "□ □ □";
                var tag = isRecommended ? "　★推奨" : (inEffect ? "　（効果圏内）" : string.Empty);
                var line = $"{RowLabel(row)}　{cells}{tag}";
                if (isRecommended)
                {
                    sb.Append("[color=").Append(kindColor).Append("][b]").Append(line).Append("[/b][/color]\n");
                }
                else
                {
                    sb.Append("[color=#888888]").Append(line).Append("[/color]\n");
                }
            }
            sb.Append("【").Append(text.FormationHeadline).Append("】\n");
            sb.Append("[color=#aaaaaa]効果範囲: ").Append(text.EffectRangeNote).Append("[/color]\n\n");
        }

        // Tactical usage.
        Header(sb, "戦術的な使い方");
        sb.Append(text.Usage).Append("\n\n");

        // Flavor.
        sb.Append("[color=").Append(FlavorColor).Append("][i]「").Append(text.Flavor).Append("」[/i][/color]");

        return sb.ToString();
    }

    private static void Header(StringBuilder sb, string title)
        => sb.Append("[color=").Append(HeaderColor).Append("][b]■ ").Append(title).Append("[/b][/color]\n");
}
