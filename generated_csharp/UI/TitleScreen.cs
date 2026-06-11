// =============================================================================
//  ChronicleKnights — TitleScreen.cs
// -----------------------------------------------------------------------------
//  起動直後にプレイヤーが最初に触れる「門（フロントドア）」。新規ゲーム開始と
//  セーブ継続の二択だけを提示する、ゲーム状態を一切持たない無状態タイトルゲート。
//
//  ★ 完全な無状態 UI（設計憲法 ③・単方向データフロー）:
//    本画面は SoT（ChronicleGlobal）への参照すら持たない。ゲーム状態を 1 ミリも
//    キャッシュせず、ボタン押下を 2 つの「意思表示イベント」
//    （<see cref="NewChronicleRequested"/> / <see cref="ContinueChronicleRequested"/>）
//    として外へ発火するだけ。実際の Initialize / LoadGame という SoT トリガーは、
//    本イベントを購読する GameDirector が一手に引く（起動エントリの単一窓口）。
//    「つづきから」を活性化してよいか（セーブの有無）も、GameDirector が
//    <see cref="ContinueAvailable"/> へ注入する。本画面は HasSaveData を自分で
//    問い合わせない（SoT 非依存を貫く）。
//
//  ★ overlay 方式:
//    GameDirector が IsInitialized == false の間だけ、本画面を画面の最前面（全 UI の
//    上）に被せる。背後の拠点・戦闘画面への入力は MouseFilter = Stop で遮断する。
//    いずれかのボタンで世界が初期化（StateInitialized）された瞬間、GameDirector が
//    本画面を QueueFree して静かに退場させる（自己崩壊型ライフサイクル）。
//
//  ★ 篝火（かがりび）の演出と Tween 規律:
//    暗幕（背景 ColorRect）の色を、危険赤（<see cref="DangerFrameColor"/>）と
//    深紺（<see cref="DeepNavyColor"/>）の間で極低速に往復させ、旅団の野営の篝火が
//    ゆらめくような明滅を焚く。生成したループ Tween は <c>_titlePulseTweens</c> 台帳に
//    保持し、退場（_ExitTree）時に必ず <c>.Kill()</c> してゾンビ化・リークを根絶する
//    （BattleUI の危険脈動と同一の規律）。
//
//  ★ 日本語ハードコード方針（設計憲法 ①）:
//    ここで直書きするのはゲーム題字・ボタン文言というクローム文字列のみ。ジョブ名・
//    アイテム名・ユニット名といった「データ名」は一切出さないため、localization 解決は
//    不要（憲法はクローム日本語・絵文字を許容）。
//
//  ★ ロゴについて:
//    現状は専用のドット絵ロゴ画像（res://image 配下）が未配置のため、題字を大きな
//    Label として描く（testid は logo を踏襲）。将来ロゴ PNG を追加した際は、本 Label を
//    TextureRect へ差し替えるだけで意匠を差し込める（testid は据え置き）。
//
//  略称（BDF/SDF/AB/HL）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using Godot;

namespace ChronicleKnights.UI;

/// <summary>
/// 新規開始 / セーブ継続の二択だけを提示する無状態タイトルゲート。ボタン押下を
/// イベントとして外へ発火するのみで、実際の初期化は購読側（GameDirector）が担う。
/// </summary>
public partial class TitleScreen : Godot.Control
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>data-testid を載せる Godot メタデータのキー（テスト自動化の足場）。</summary>
    private const string TestIdMetaKey = "data_testid";

    /// <summary>篝火の明滅の片道時間（秒）。極低速にして荘厳な「ゆらめき」を出す。</summary>
    private const double TitlePulseHalfPeriodSeconds = 2.8;

    /// <summary>暗幕の安息色（深紺）。夜営の闇を表す明滅の片端。</summary>
    private static readonly Color DeepNavyColor = new(0.05f, 0.07f, 0.14f);

    /// <summary>
    /// 危険赤。戦場の篝火・血脈を象徴する明滅のもう片端で、戦闘 UI の予告赤枠
    /// （BattleUI.DangerFrameColor）と同一の色価をタイトルでも用い、世界観の色を統一する。
    /// </summary>
    private static readonly Color DangerFrameColor = new(0.86f, 0.20f, 0.20f);

    /// <summary>ゲーム題字（クローム・憲法①は data 名でないクローム日本語を許容）。</summary>
    private const string GameTitleText = "⚔ 年代記の騎士団";

    /// <summary>副題（クローム）。原題を併記して荘厳さを添える。</summary>
    private const string GameSubtitleText = "― Chronicle Knights ―";

    /// <summary>「新たな年代記を始める」ボタン文言（クローム）。</summary>
    private const string NewChronicleButtonText = "⚔ 新たな年代記を始める";

    /// <summary>「つづきから」ボタン文言（クローム）。</summary>
    private const string ContinueButtonText = "📜 つづきから";

    // ─── 外部からの注入（GameDirector が AddChild 前に設定する） ────────────

    /// <summary>
    /// 「つづきから」を活性化してよいか（セーブデータが存在するか）。GameDirector が
    /// ChronicleGlobal.HasSaveData() の結果を AddChild 前に注入する。false ならボタンを
    /// Disabled でグレーアウトする。本画面は SoT を自分で問い合わせない（無状態の徹底）。
    /// </summary>
    public bool ContinueAvailable { get; set; }

    // ─── 意思表示イベント（SoT トリガーは購読側 = GameDirector が引く） ─────

    /// <summary>「新たな年代記を始める」押下時に 1 度だけ発火する。</summary>
    public event Action? NewChronicleRequested;

    /// <summary>「つづきから」押下時に 1 度だけ発火する。</summary>
    public event Action? ContinueChronicleRequested;

    // ─── UI 要素 / Tween 台帳 ─────────────────────────────────────────────

    /// <summary>篝火の明滅対象となる暗幕（背景 ColorRect）。</summary>
    private ColorRect? _backdrop;

    /// <summary>
    /// 生存中のループ Tween 台帳。退場（_ExitTree）時に全 Kill して更地化し、
    /// 無効ノードを掴んだ Tween が残留（ゾンビ化・リーク）するのを構造的に根絶する。
    /// </summary>
    private readonly List<Tween> _titlePulseTweens = new();

    // ─── ライフサイクル ───────────────────────────────────────────────────

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;   // 背後の拠点・戦闘画面への入力を遮断（overlay）
        SetMeta(TestIdMetaKey, "title-screen-root");

        BuildUI();

        // Tween 生成はノードが SceneTree 内（= AddChild 済み）である必要があるため、
        // 暗幕を tree に組み込んだ後の _Ready で起動する。
        StartBackgroundPulse();
    }

    public override void _ExitTree()
    {
        // 退場時にループ Tween を必ず明示 Kill（ゾンビ化・リーク防止規律）。
        KillTitlePulses();
    }

    // ─── UI 構築（暗幕 + 中央の題字 + 二択ボタン） ─────────────────────────

    private void BuildUI()
    {
        // ── 暗幕（フルレクト・入力遮断・篝火の明滅対象） ───────────────
        _backdrop = new ColorRect { Color = DeepNavyColor };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _backdrop.MouseFilter = MouseFilterEnum.Stop;
        _backdrop.SetMeta(TestIdMetaKey, "title-screen-backdrop");
        AddChild(_backdrop);

        // ── 中央寄せラッパ → 縦積みの題字＋ボタン列 ───────────────────
        var centerWrap = new CenterContainer();
        centerWrap.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        centerWrap.MouseFilter = MouseFilterEnum.Ignore; // クリックは下のボタンへ素通り
        AddChild(centerWrap);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 24);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        centerWrap.AddChild(column);

        // ── 題字（ロゴ）。将来 res://image にロゴ PNG を置く際は TextureRect へ差替可。
        var logo = new Label
        {
            Text                = GameTitleText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        logo.AddThemeFontSizeOverride("font_size", 52);
        logo.AddThemeColorOverride("font_color", DangerFrameColor);
        logo.SetMeta(TestIdMetaKey, "title-screen-logo");
        column.AddChild(logo);

        // ── 副題（クローム） ───────────────────────────────────────
        var subtitle = new Label
        {
            Text                = GameSubtitleText,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        subtitle.SetMeta(TestIdMetaKey, "title-screen-subtitle");
        column.AddChild(subtitle);

        // ── 新たな年代記を始める ───────────────────────────────────
        var newGameButton = new Button
        {
            Text              = NewChronicleButtonText,
            CustomMinimumSize = new Vector2(320, 56),
        };
        newGameButton.Pressed += OnNewChroniclePressed;
        newGameButton.SetMeta(TestIdMetaKey, "title-screen-new-game-button");
        column.AddChild(newGameButton);

        // ── つづきから（セーブが無ければグレーアウト） ────────────────
        var continueButton = new Button
        {
            Text              = ContinueButtonText,
            CustomMinimumSize = new Vector2(320, 56),
            Disabled          = !ContinueAvailable, // セーブ不在なら不活性（誤押下を構造的に封じる）
        };
        continueButton.Pressed += OnContinueChroniclePressed;
        continueButton.SetMeta(TestIdMetaKey, "title-screen-continue-button");
        column.AddChild(continueButton);
    }

    // ─── 篝火の明滅（暗幕の色を赤↔深紺で往復・ループ） ────────────────────

    /// <summary>
    /// 暗幕の <c>color</c> を <see cref="DangerFrameColor"/> ↔ <see cref="DeepNavyColor"/> で
    /// 極低速に往復させるループ Tween を起動し、台帳に登録する。BattleUI の危険脈動と
    /// 同型の規律（SetLoops + 台帳 + 退場時 Kill）でゾンビ化を防ぐ。
    /// </summary>
    private void StartBackgroundPulse()
    {
        if (_backdrop is null) return;

        var tween = _backdrop.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(_backdrop, "color", DangerFrameColor, TitlePulseHalfPeriodSeconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(_backdrop, "color", DeepNavyColor, TitlePulseHalfPeriodSeconds)
             .SetTrans(Tween.TransitionType.Sine)
             .SetEase(Tween.EaseType.InOut);

        _titlePulseTweens.Add(tween);
    }

    /// <summary>
    /// 生存中のループ Tween をすべて明示的に Kill して台帳を更地化する。_ExitTree で
    /// 必ず呼び、退場後に無効ノードを掴んだ Tween が残留（リーク）するのを根絶する。
    /// </summary>
    private void KillTitlePulses()
    {
        foreach (var tween in _titlePulseTweens)
        {
            if (tween is not null && tween.IsValid())
            {
                tween.Kill();
            }
        }
        _titlePulseTweens.Clear();
    }

    // ─── ボタンハンドラ（意思表示イベントを発火するだけ） ────────────────

    /// <summary>
    /// 「新たな年代記」押下。SoT には触れず <see cref="NewChronicleRequested"/> を発火する。
    ///
    /// ★ 二重発火の構造的防止（一過性フラグを持たない理由）:
    ///   購読側 GameDirector は Initialize 成功で同期発火する StateInitialized を受け、
    ///   その場で本イベントの購読を解除して本画面を QueueFree する。よって最初の押下処理が
    ///   返る時点で購読は既に外れており、（同一フレーム内の追い撃ち押下を含め）以後の
    ///   Invoke は購読者ゼロで no-op になる。逆に初期化が成立しなかった場合（後述の
    ///   「つづきから」失敗時）は購読が残るため、本画面は生きたまま再選択を受け付ける。
    /// </summary>
    private void OnNewChroniclePressed()
    {
        NewChronicleRequested?.Invoke();
    }

    /// <summary>
    /// 「つづきから」押下。SoT には触れず <see cref="ContinueChronicleRequested"/> を発火する。
    /// ロードが失敗（ファイル消失・破損）した場合は世界が初期化されず購読も残るため、
    /// 本画面はそのまま生存し、プレイヤーは「新たな年代記」を含め再選択できる。
    /// </summary>
    private void OnContinueChroniclePressed()
    {
        ContinueChronicleRequested?.Invoke();
    }
}
