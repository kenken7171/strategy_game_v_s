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
using System.Text;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
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

    /// <summary>Raised when 家系図 を開く意思表示。購読側（GameDirector）が PedigreeOverlay をマウントする。</summary>
    public event Action<Guid>? PedigreeRequested;

    private ChronicleGlobal? _chronicleGlobal;

    /// <summary>操作ボタン行（家系図／戦力外通告）。解雇の 2 段階確認で中身を組み直すため保持する。</summary>
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

        // ── 操作（家系図 / 戦力外通告）。人事の per-unit アクションをここへ集約。 ──
        BuildActions(body);

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
    }

    /// <summary>
    /// 操作ボタン行（🌳 家系図 ／ 🛡 戦力外通告）を組む器を置き、<see cref="RenderActions"/> で中身を描く。
    /// 解雇は不可逆なため行内 2 段階確認（［戦力外通告］→［解雇する］／［やめる］）にする。
    /// </summary>
    private void BuildActions(VBoxContainer body)
    {
        _actionsRow = new HBoxContainer();
        _actionsRow.AddThemeConstantOverride("separation", 10);
        _actionsRow.SetMeta(TestIdMetaKey, "unit-detail-actions");
        body.AddChild(_actionsRow);
        RenderActions();
    }

    /// <summary>操作ボタン行を現在の武装状態から組み直す（家系図は常設、解雇は確認待ちで切替）。</summary>
    private void RenderActions()
    {
        if (_actionsRow is null) return;
        foreach (var c in _actionsRow.GetChildren()) c.QueueFree();

        var pedigreeBtn = new Button { Text = "🌳 家系図" };
        pedigreeBtn.SetMeta(TestIdMetaKey, "unit-detail-pedigree-button");
        pedigreeBtn.Pressed += () => PedigreeRequested?.Invoke(TargetUnitId);
        _actionsRow.AddChild(pedigreeBtn);

        if (!_dismissArmed)
        {
            var dismissBtn = new Button { Text = "🛡 戦力外通告" };
            dismissBtn.SetMeta(TestIdMetaKey, "unit-detail-dismiss-button");
            dismissBtn.Pressed += () => { _dismissArmed = true; RenderActions(); };
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
            cancelBtn.Pressed += () => { _dismissArmed = false; RenderActions(); };
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
