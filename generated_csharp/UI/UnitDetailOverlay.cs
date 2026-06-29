// =============================================================================
//  ChronicleKnights — UnitDetailOverlay.cs
// -----------------------------------------------------------------------------
//  Modal shown when a roster unit is inspected: a gendered job illustration plus
//  the full profile (stats / gender / lineage / current formation slot) and the
//  shared job description block. Faithful trace of the TS UnitDetailModal.
//
//  Stateless, self-collapsing modal: TargetUnitId is injected before AddChild;
//  CLOSE raises CloseRequested and the GameDirector frees it. All data is read
//  fresh from ChronicleGlobal on _Ready; numbers come from JobMaster + BattleManager.
//
//  ★ Language exception (this command only): UI display text is Japanese (UTF-8).
//    Identifiers / comments stay ASCII. Emphasis is BBCode (bold / gold numbers).
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Pedigree;        // PedigreeBuilder / PedigreeGraph（家系図のインライン表示）
using ChronicleKnights.Core.Units;
using ChronicleKnights.UserInterface; // JobTextureLibrary
using Godot;

namespace ChronicleKnights.UI;

/// <summary>Front-most modal: one unit's full profile (Japanese / BBCode + art).</summary>
public partial class UnitDetailOverlay : Godot.Control
{
    private const string TestIdMetaKey = "data_testid";
    private const string HeaderColor = "#7fd0ff";
    private const string GoldColor   = "#ffd24a";

    /// <summary>Unit to profile. Injected before AddChild (the modal is stateless).</summary>
    public Guid TargetUnitId { get; set; }

    /// <summary>Raised when the player presses the close button.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when 戦力外通告（解雇）が確定したとき。購読側（GameDirector）が ExecuteDismiss を呼ぶ。</summary>
    public event Action<Guid>? DismissRequested;

    private ChronicleGlobal? _chronicleGlobal;

    /// <summary>戦力外通告ボタン行。解雇の 2 段階確認で中身を組み直すため保持する。</summary>
    private HBoxContainer? _actionsRow;

    /// <summary>解雇の確認待ち（武装）状態。true なら ［解雇する］／［やめる］を出す（不可逆操作の誤爆防止）。</summary>
    private bool _dismissArmed;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta(TestIdMetaKey, "unit-detail-overlay-root");
        BuildUI();
    }

    private void BuildUI()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.85f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.SetMeta(TestIdMetaKey, "unit-detail-overlay-backdrop");
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer();
        panel.CustomMinimumSize = new Vector2(620, 0);
        panel.SetMeta(TestIdMetaKey, "unit-detail-modal-root");
        center.AddChild(panel);

        var scroll = new ScrollContainer();
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scroll.CustomMinimumSize = new Vector2(620, 600);
        scroll.SetMeta(TestIdMetaKey, "unit-detail-scroll");
        panel.AddChild(scroll);

        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(body);

        BuildHeader(body);

        var unit = _chronicleGlobal?.FindUnit(TargetUnitId);
        if (_chronicleGlobal is null || unit is null)
        {
            var missing = new Label { Text = "ユニットが見つかりません。" };
            missing.SetMeta(TestIdMetaKey, "unit-detail-missing");
            body.AddChild(missing);
            return;
        }

        // ── Identity row: illustration + name/job/gender/origin/age ──────────
        var idRow = new HBoxContainer();
        idRow.AddThemeConstantOverride("separation", 14);
        idRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.AddChild(idRow);

        var texture = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (texture is not null)
        {
            var art = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(128, 128),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = SizeFlags.ShrinkBegin,
            };
            art.SetMeta(TestIdMetaKey, "unit-detail-art");
            idRow.AddChild(art);
        }

        var identity = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        identity.SetMeta(TestIdMetaKey, "unit-detail-identity");
        identity.Text = BuildIdentityBbcode(unit);
        idRow.AddChild(identity);

        // ── Current formation slot ───────────────────────────────────────────
        var coord = _chronicleGlobal.CurrentFormation.CoordinateOf(TargetUnitId);
        var slotText = coord is { } c
            ? $"配置スロット: {RowLabel(c.Row)} {c.Column + 1}番"
            : "配置スロット: （控え・未配置）";
        var slot = new Label { Text = slotText };
        slot.AddThemeColorOverride("font_color", Colors.LightSkyBlue);
        slot.SetMeta(TestIdMetaKey, "unit-detail-formation-slot");
        body.AddChild(slot);

        // ── Stats + lineage (BBCode) ─────────────────────────────────────────
        var stats = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        stats.SetMeta(TestIdMetaKey, "unit-detail-stats-section");
        stats.Text = BuildStatsBbcode(unit);
        body.AddChild(stats);

        // ── Job description (shared with the Job Manual) ─────────────────────
        var jobDescHeader = new Label { Text = "― ジョブ説明 ―" };
        jobDescHeader.SetMeta(TestIdMetaKey, "unit-detail-job-description-header");
        body.AddChild(jobDescHeader);

        var jobBlock = JobDescriptionView.Build(unit.Job, unit.Gender, "unit-detail-job");
        if (jobBlock is not null)
        {
            body.AddChild(jobBlock);
        }

        // ── 家系図（インライン・常時表示。ボタン不要） ＋ その下に戦力外通告 ──
        BuildPedigreeInline(body);
        BuildDismissAction(body);
    }

    // ─── 家系図（インライン・常時表示） ───────────────────────────────────

    /// <summary>
    /// 本人を根とする家系図を、ボタンを介さず詳細モーダル最下部へ直接描く。世代帯（祖父母／父母／
    /// 本人・配偶者・兄弟／子／孫）ごとに小カードを横並びにする。データは血統宇宙
    /// （<see cref="ChronicleGlobal.GetPedigreeUniverse"/>）＋現役 Id 集合から純粋層 <see cref="PedigreeBuilder"/> が構築する。
    /// </summary>
    private void BuildPedigreeInline(VBoxContainer body)
    {
        if (_chronicleGlobal is null) return;

        var header = new Label { Text = "― 家系図 ―" };
        header.SetMeta(TestIdMetaKey, "unit-detail-pedigree-header");
        body.AddChild(header);

        var universe = _chronicleGlobal.GetPedigreeUniverse();
        var currentIds = _chronicleGlobal.BattalionRoster.Select(u => u.Id).ToHashSet();
        var graph = PedigreeBuilder.Build(universe, currentIds, TargetUnitId);

        // 本人 1 ノードしか無い＝血縁リンクなし（創設メンバー）。
        if (!graph.HasNodes || graph.Nodes.Length <= 1)
        {
            var none = new Label { Text = "血統情報なし（創設メンバー）" };
            none.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            none.SetMeta(TestIdMetaKey, "unit-detail-pedigree-empty");
            body.AddChild(none);
            return;
        }

        // 祖先（上）→ 子孫（下）の順に世代帯を縦に積む。
        foreach (var gen in new[] { -2, -1, 0, 1, 2 })
        {
            var nodes = graph.Nodes.Where(n => n.Generation == gen).ToArray();
            if (nodes.Length == 0) continue;

            var band = new VBoxContainer();
            band.AddThemeConstantOverride("separation", 2);
            band.SetMeta(TestIdMetaKey, $"unit-detail-pedigree-gen-{gen}");

            var genLabel = new Label { Text = GenerationLabel(gen) };
            genLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.82f, 1.0f));
            band.AddChild(genLabel);

            var iconsRow = new HBoxContainer();
            iconsRow.AddThemeConstantOverride("separation", 12);
            foreach (var node in nodes) iconsRow.AddChild(BuildPedigreeCard(node));
            band.AddChild(iconsRow);

            body.AddChild(band);
        }
    }

    /// <summary>家系図 1 ノードの小カード（立ち絵 ＋ 氏名 ＋ 関係/職）。去った者は遺影風に淡色、本人は金字。</summary>
    private Control BuildPedigreeCard(PedigreeNode node)
    {
        var unit = node.Unit;

        var card = new VBoxContainer();
        card.AddThemeConstantOverride("separation", 2);
        card.SetMeta(TestIdMetaKey, $"unit-detail-pedigree-card-{unit.Id}");

        var tex = JobTextureLibrary.TryLoad(unit.Job, unit.Gender);
        if (tex is not null)
        {
            var icon = new TextureRect
            {
                Texture = tex,
                CustomMinimumSize = new Vector2(56, 56),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            };
            // 去った祖先（非現役）は遺影風に淡いセピアで沈める。
            if (!node.IsCurrentMember) icon.Modulate = new Color(0.62f, 0.56f, 0.50f);
            card.AddChild(icon);
        }

        var name = new Label
        {
            Text = _chronicleGlobal?.ResolveDisplayName(unit) ?? unit.Id.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (node.Relation == PedigreeRelation.Self)
        {
            name.AddThemeColorOverride("font_color", new Color(1.0f, 0.82f, 0.29f)); // 本人＝金
        }
        name.SetMeta(TestIdMetaKey, $"unit-detail-pedigree-name-{unit.Id}");
        card.AddChild(name);

        var sub = new Label
        {
            Text = $"{RelationLabel(node.Relation)}・{(_chronicleGlobal?.ResolveJobName(unit.Job) ?? unit.Job.ToString())}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        sub.AddThemeFontSizeOverride("font_size", 12);
        sub.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        card.AddChild(sub);

        return card;
    }

    private static string GenerationLabel(int generation) => generation switch
    {
        -2 => "祖父母",
        -1 => "父母",
        0  => "本人・配偶者・兄弟",
        1  => "子",
        2  => "孫",
        _  => $"世代 {generation}",
    };

    private static string RelationLabel(PedigreeRelation relation) => relation switch
    {
        PedigreeRelation.Grandparent => "祖父母",
        PedigreeRelation.Parent      => "親",
        PedigreeRelation.Self        => "本人",
        PedigreeRelation.Spouse      => "配偶者",
        PedigreeRelation.Sibling     => "兄弟姉妹",
        PedigreeRelation.Child       => "子",
        PedigreeRelation.Grandchild  => "孫",
        _ => relation.ToString(),
    };

    // ─── 戦力外通告（家系図の下に置く・2 段階確認） ───────────────────────

    /// <summary>戦力外通告ボタン行を家系図の下に置き、<see cref="RenderDismissAction"/> で中身を描く。</summary>
    private void BuildDismissAction(VBoxContainer body)
    {
        _actionsRow = new HBoxContainer();
        _actionsRow.AddThemeConstantOverride("separation", 10);
        _actionsRow.SetMeta(TestIdMetaKey, "unit-detail-actions");
        body.AddChild(_actionsRow);
        RenderDismissAction();
    }

    /// <summary>戦力外通告（解雇）の 2 段階確認（［戦力外通告］→［解雇する］／［やめる］）を組み直す。</summary>
    private void RenderDismissAction()
    {
        if (_actionsRow is null) return;
        foreach (var c in _actionsRow.GetChildren()) c.QueueFree();

        if (!_dismissArmed)
        {
            var dismissBtn = new Button { Text = "🛡 戦力外通告" };
            dismissBtn.SetMeta(TestIdMetaKey, "unit-detail-dismiss-button");
            dismissBtn.Pressed += () => { _dismissArmed = true; RenderDismissAction(); };
            _actionsRow.AddChild(dismissBtn);
        }
        else
        {
            var confirmLabel = new Label { Text = "⚠ 本当に解雇？" };
            confirmLabel.SetMeta(TestIdMetaKey, "unit-detail-dismiss-confirm-label");
            _actionsRow.AddChild(confirmLabel);

            var confirmBtn = new Button { Text = "解雇する" };
            confirmBtn.SetMeta(TestIdMetaKey, "unit-detail-dismiss-confirm-button");
            confirmBtn.Pressed += () => DismissRequested?.Invoke(TargetUnitId);
            _actionsRow.AddChild(confirmBtn);

            var cancelBtn = new Button { Text = "やめる" };
            cancelBtn.SetMeta(TestIdMetaKey, "unit-detail-dismiss-cancel-button");
            cancelBtn.Pressed += () => { _dismissArmed = false; RenderDismissAction(); };
            _actionsRow.AddChild(cancelBtn);
        }
    }

    private void BuildHeader(VBoxContainer body)
    {
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.SetMeta(TestIdMetaKey, "unit-detail-header");
        body.AddChild(header);

        var title = new Label { Text = "ユニット詳細" };
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.SetMeta(TestIdMetaKey, "unit-detail-title");
        header.AddChild(title);

        var closeButton = new Button { Text = "閉じる" };
        closeButton.SetMeta(TestIdMetaKey, "unit-detail-modal-close-button");
        closeButton.Pressed += () => CloseRequested?.Invoke();
        header.AddChild(closeButton);
    }

    private string BuildIdentityBbcode(Unit unit)
    {
        var name = _chronicleGlobal?.ResolveDisplayName(unit) ?? unit.Id.ToString();
        var jobName = _chronicleGlobal?.ResolveJobName(unit.Job) ?? unit.Job.ToString();
        var gender = unit.Gender == Gender.Male ? "男" : "女";

        var sb = new StringBuilder();
        sb.Append("[b][font_size=24]").Append(name).Append("[/font_size][/b]\n");
        sb.Append(Field("職")).Append("[b]").Append(jobName).Append("[/b]　");
        sb.Append(Field("性別")).Append(gender).Append("　");
        sb.Append(Field("出自")).Append(OriginLabel(unit.Origin)).Append('\n');
        sb.Append(Field("年齢")).Append(unit.Age).Append(" 歳　");
        sb.Append(Field("階級")).Append("Lv").Append(unit.Level);
        return sb.ToString();
    }

    private string BuildStatsBbcode(Unit unit)
    {
        var sb = new StringBuilder();

        sb.Append("[color=").Append(HeaderColor).Append("][b]■ ステータス[/b][/color]\n");
        var def = JobMaster.Find(unit.Job);
        if (def is not null)
        {
            var s = def.Stats;
            var eff = s.FrontAttack >= s.RearAttack ? s.FrontAttack : s.RearAttack;
            sb.Append("体力(HP): [b]").Append(s.MaxHp).Append("[/b]\n");
            sb.Append("攻撃力: 前衛 [b]").Append(s.FrontAttack).Append("[/b] / 後衛 [b]").Append(s.RearAttack)
              .Append("[/b]　(実効 ").Append(eff).Append(")\n");
            sb.Append("俊敏(SPD): [b]").Append(s.Speed).Append("[/b]\n");
        }

        var equip = unit.MainEquipment;
        var equipText = equip is null ? "なし" : $"{ItemName(equip.ItemId)} Lv{equip.Level}";
        sb.Append("兵装: ").Append(equipText).Append('\n');

        if (equip is not null && equip.HasAnyAffix)
        {
            sb.Append("付加効果: [color=").Append(GoldColor).Append("][b]");
            for (var i = 0; i < equip.AffixKeys.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(AffixName(equip.AffixKeys[i]));
            }
            sb.Append("[/b][/color]\n");
        }

        var atk = BattleManager.EquipmentAttackBonus(unit);
        var dfn = BattleManager.EquipmentDefenseBonus(unit);
        var spd = BattleManager.EquipmentSpeedBonus(unit);
        sb.Append("装備補正: 攻撃 [color=").Append(GoldColor).Append("][b]+").Append(atk).Append("[/b][/color]")
          .Append("  防御 [color=").Append(GoldColor).Append("][b]+").Append(dfn).Append("[/b][/color]")
          .Append("  俊敏 [color=").Append(GoldColor).Append("][b]+").Append(spd).Append("[/b][/color]\n\n");

        // Lineage.
        sb.Append("[color=").Append(HeaderColor).Append("][b]■ 血統[/b][/color]\n");
        var hasLine = false;
        if (unit.IsMarried && unit.SpouseId is { } spouseId)
        {
            var spouse = _chronicleGlobal?.FindUnit(spouseId);
            var spouseName = spouse is null ? "（失われた）" : _chronicleGlobal!.ResolveDisplayName(spouse);
            sb.Append("配偶者: [b]").Append(spouseName).Append("[/b]\n");
            hasLine = true;
        }
        if (unit.HasParentage && unit.Parentage is { } parentage)
        {
            var father = _chronicleGlobal?.FindUnit(parentage.FatherId);
            var mother = _chronicleGlobal?.FindUnit(parentage.MotherId);
            var fatherName = father is null ? "（失われた）" : _chronicleGlobal!.ResolveDisplayName(father);
            var motherName = mother is null ? "（失われた）" : _chronicleGlobal!.ResolveDisplayName(mother);
            sb.Append("両親: [b]").Append(fatherName).Append("[/b] ／ [b]").Append(motherName).Append("[/b]\n");
            hasLine = true;
        }
        if (!hasLine)
        {
            sb.Append("[color=#999999]血統情報なし（創設メンバー）[/color]");
        }

        return sb.ToString();
    }

    private static string Field(string label)
        => $"[color={HeaderColor}]{label}:[/color] ";

    private static string RowLabel(SquadRow row) => row switch
    {
        SquadRow.Front     => "前衛",
        SquadRow.RearLeft  => "後衛-左",
        SquadRow.RearRight => "後衛-右",
        _ => row.ToString(),
    };

    private static string OriginLabel(Origin origin) => origin switch
    {
        Origin.Japanese  => "和風",
        Origin.European  => "欧州",
        Origin.Classical => "古典",
        _ => origin.ToString(),
    };

    private string ItemName(ItemId item)
        => _chronicleGlobal?.ResolveItemName(item) ?? item.ToString();

    private string AffixName(string affixKey)
        => _chronicleGlobal?.ResolveAffixName(affixKey) ?? affixKey;
}
