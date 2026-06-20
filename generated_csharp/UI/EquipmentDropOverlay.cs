// =============================================================================
//  ChronicleKnights — EquipmentDropOverlay.cs
// -----------------------------------------------------------------------------
//  装備ドロップの「3 択」提示オーバーレイ（ハクスラの脳汁ポイント）。
//
//  ★ 完全な無状態 UI（設計憲法 ③・単方向データフロー）:
//    自前の状態を持たず、ChronicleGlobal.PendingDropCandidates（純粋層
//    EquipmentDropService が生成した不変候補列）を「読むだけ」で 3 つのカードを描く。
//    プレイヤーが 1 つ押すと ChronicleGlobal.ChooseDroppedEquipment(id) を呼び、
//    選んだ装備は旅団の持ち物（BrigadeInventory）へ入り、残りは破棄される。
//    取得後は Chosen を 1 度だけ発火して自身を退場（QueueFree）する。
//
//  ★ クローム日本語は直書き可（憲法①）。識別子・testid・ログは ASCII のみ。
// =============================================================================

using System;
using ChronicleKnights.Autoload;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// ChronicleGlobal.PendingDropCandidates を読み取り 3 択で提示する無状態モーダル。
/// 1 つ選ぶと持ち物へ取り込み、<see cref="Chosen"/> を発火して退場する。
/// </summary>
public partial class EquipmentDropOverlay : Godot.Control
{
    private const string TestIdMetaKey = "data_testid";
    private static readonly Vector2 BodyMinimumSize = new(520, 240);
    private static readonly Color BackdropColor = new(0f, 0f, 0f, 0.78f);
    private static readonly Color TitleColor = new(1.0f, 0.86f, 0.32f);   // 戦利品の金
    private static readonly Color AffixColor = new(0.36f, 0.80f, 0.42f);  // Affix の緑

    private ChronicleGlobal? _chronicleGlobal;

    /// <summary>候補を 1 つ取得し終えたときに 1 度だけ発火（GameDirector が後始末に使う）。</summary>
    public event Action? Chosen;

    private bool _chosen;

    public override void _Ready()
    {
        _chronicleGlobal = GetNodeOrNull<ChronicleGlobal>("/root/ChronicleGlobal");

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // 背後への入力を遮断（モーダル）
        SetMeta(TestIdMetaKey, "equipment-drop-root");

        BuildUI();
    }

    private void BuildUI()
    {
        var candidates = _chronicleGlobal?.PendingDropCandidates
                         ?? System.Collections.Immutable.ImmutableArray<Equipment>.Empty;

        var backdrop = new ColorRect { Color = BackdropColor };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.MouseFilter = MouseFilterEnum.Stop;
        backdrop.SetMeta(TestIdMetaKey, "equipment-drop-backdrop");
        AddChild(backdrop);

        var centerWrap = new CenterContainer();
        centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerWrap);

        var panel = new PanelContainer();
        panel.SetMeta(TestIdMetaKey, "equipment-drop-panel");
        centerWrap.AddChild(panel);

        var outer = new VBoxContainer { CustomMinimumSize = BodyMinimumSize };
        outer.AddThemeConstantOverride("separation", 12);
        panel.AddChild(outer);

        var title = new Label { Text = "🎁 戦利品 — ひとつ選んで持ち帰れ" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", TitleColor);
        title.SetMeta(TestIdMetaKey, "equipment-drop-title");
        outer.AddChild(title);

        var hint = new Label
        {
            Text = "選んだ 1 つは持ち物に入り、残りは置いていく。装備は拠点でいつでも付け替えられる。",
        };
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        hint.SetMeta(TestIdMetaKey, "equipment-drop-hint");
        outer.AddChild(hint);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.SetMeta(TestIdMetaKey, "equipment-drop-choices");
        outer.AddChild(row);

        for (var i = 0; i < candidates.Length; i++)
        {
            row.AddChild(BuildChoiceCard(candidates[i], i));
        }
    }

    /// <summary>1 候補ぶんの選択カード（種別名・Lv・Affix を表示し、押下で取得する）。</summary>
    private Control BuildChoiceCard(Equipment candidate, int index)
    {
        var itemName = _chronicleGlobal?.ResolveItemName(candidate.ItemId) ?? candidate.ItemId.ToString();

        var button = new Button { ToggleMode = false };
        button.CustomMinimumSize = new Vector2(160, 120);
        button.SetMeta(TestIdMetaKey, $"equipment-drop-choice-{index}");

        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 4);
        box.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var name = new Label
        {
            Text = $"{itemName}\nLv{candidate.Level}",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddChild(name);

        for (var a = 0; a < candidate.AffixKeys.Count; a++)
        {
            var affixName = _chronicleGlobal?.ResolveAffixName(candidate.AffixKeys[a])
                            ?? candidate.AffixKeys[a];
            var affix = new Label
            {
                Text = affixName,
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            affix.AddThemeColorOverride("font_color", AffixColor);
            box.AddChild(affix);
        }

        button.AddChild(box);

        var capturedId = candidate.Id;
        button.Pressed += () => OnChoicePressed(capturedId);
        return button;
    }

    private void OnChoicePressed(Guid equipmentId)
    {
        if (_chosen) return;
        _chosen = true;

        _chronicleGlobal?.ChooseDroppedEquipment(equipmentId);

        Chosen?.Invoke();
        QueueFree();
    }
}
