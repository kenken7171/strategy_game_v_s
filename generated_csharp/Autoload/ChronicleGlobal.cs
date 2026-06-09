// =============================================================================
//  ChronicleKnights — ChronicleGlobal.cs
// -----------------------------------------------------------------------------
//  Godot 4.x の Autoload (Singleton) として常駐し、ゲーム全体の唯一の真実
//  (SoT: Single Source of Truth) として 3 つの不変な状態を保持する常駐ノード。
//
//  保持する状態:
//    1. CurrentEconomy   : PointsEconomy        — ポイント一元経済の財布
//    2. CurrentTimeline  : TimelineEngine       — 予言タイムラインの現在状態
//    3. BattalionRoster  : ImmutableList<Unit>  — 大隊の全旅団員リスト
//
//  設計思想 (single-direction data flow):
//    UI ノード (各シーン)
//         │
//         │ ① ChronicleGlobal の API を呼ぶ (例: ResolveLastHit)
//         ▼
//    ChronicleGlobal (本クラス)
//         │
//         │ ② 内部で純粋な core ロジック (BattleManager 等) を叩く
//         ▼
//    新しい不変レコードを受け取り「丸ごと差し替える」
//         │
//         │ ③ Signal 発火 (RosterChanged 等)
//         ▼
//    UI ノードが Signal を受け、状態を読み直して再描画
//
//  ★ UI は本クラスの状態を直接書き換えない（すべて private setter）。
//  ★ 状態更新は必ず API メソッド経由でロジック層の純粋関数を呼ぶ。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md):
//    - 略称 (BDF/SDF/AB/HL) 完全廃止
//    - すべての変数・プロパティは正式名称 (CurrentEconomy, BattalionRoster 等)
//    - イミュータブル更新を徹底 (record + with 式の世界を Godot まで貫通)
//
//  テスト容易性 (Godot 非依存):
//    - 状態遷移ロジックは Godot ランタイムに依存しない純粋な C# 操作のみ
//    - 初期状態と Random は Initialize() で外部から注入可能
//    - EmitSignal は IsInsideTree() ガード + try/catch で完全隔離
//      → xUnit/NUnit で「new ChronicleGlobal(); Initialize(); ResolveLastHit();
//         CurrentEconomy.CurrentBalance を assert」が Godot なしで動作可能
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Persistence;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Godot;

namespace ChronicleKnights.Autoload;

/// <summary>
/// Godot 4.x の autoload として常駐し、ゲーム全体の唯一の SoT 状態を保持し
/// API 経由でのみ更新を受け付ける常駐ノード。
/// </summary>
public partial class ChronicleGlobal : Godot.Node
{
    // ════════════════════════════════════════════════════════════════════════
    //  Signal 名称定数（Godot ソースジェネレータ非依存）
    // ════════════════════════════════════════════════════════════════════════
    //  Godot 4 では [Signal] 属性付き delegate を宣言すると、ソースジェネレータ
    //  が SignalName.Xxx という const クラスを生成する。しかし xUnit テスト等の
    //  Godot ランタイム非依存環境ではジェネレータが走らない場合があるため、
    //  本クラスでは安全のため明示的な const string を保持し、EmitSignal も
    //  string ベースで行う。Godot 側の挙動には影響なし。

    public const string SignalStateInitialized  = "StateInitialized";
    public const string SignalEconomyChanged    = "EconomyChanged";
    public const string SignalTimelineChanged   = "TimelineChanged";
    public const string SignalRosterChanged     = "RosterChanged";
    public const string SignalPhaseChanged      = "PhaseChanged";

    // ════════════════════════════════════════════════════════════════════════
    //  Signal 宣言
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>初期化完了時に 1 回だけ発火するシグナル。UI が初期描画を行う契機。</summary>
    [Signal] public delegate void StateInitializedEventHandler();

    /// <summary>CurrentEconomy が更新された時に発火するシグナル。</summary>
    [Signal] public delegate void EconomyChangedEventHandler();

    /// <summary>CurrentTimeline が更新された時に発火するシグナル。</summary>
    [Signal] public delegate void TimelineChangedEventHandler();

    /// <summary>BattalionRoster が更新された時に発火するシグナル。</summary>
    [Signal] public delegate void RosterChangedEventHandler();

    /// <summary>
    /// CurrentPhase が遷移した時に発火するシグナル。UI 層がこれを受けて画面を
    /// 切り替える契機にする（新フェーズは CurrentPhase を読み直して判定）。
    /// </summary>
    [Signal] public delegate void PhaseChangedEventHandler();

    // ════════════════════════════════════════════════════════════════════════
    //  状態保持プロパティ（SoT、外部からは読み取り専用）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ポイント一元経済の現在状態。外部からは読み取りのみ。
    /// 更新は ResolveLastHit / SelectProphecyAndAdvance / ExecuteMarriage 経由のみ。
    /// </summary>
    public PointsEconomy CurrentEconomy { get; private set; } = PointsEconomy.CreateInitial();

    /// <summary>
    /// 予言タイムラインの現在状態。Initialize 前は null。
    /// 更新は SelectProphecyAndAdvance 経由のみ。
    /// </summary>
    public TimelineEngine? CurrentTimeline { get; private set; }

    /// <summary>
    /// 大隊の全旅団員（ImmutableList）。外部からは読み取りのみ。
    /// 更新は ResolveLastHit / SelectProphecyAndAdvance / ExecuteMarriage 経由のみ。
    /// </summary>
    public ImmutableList<Unit> BattalionRoster { get; private set; } = ImmutableList<Unit>.Empty;

    /// <summary>Initialize が呼ばれて状態が有効化されているか。</summary>
    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// ゲーム進行の現在フェーズ（状態マシン）。外部からは読み取りのみ。
    /// 遷移は AdvancePhase / TryAdvanceTo 経由でのみ行い、合法性は
    /// 純粋ロジック GamePhaseFlow が判定する（不正な飛び越し・後退は不可）。
    /// </summary>
    public GamePhase CurrentPhase { get; private set; } = GamePhaseFlow.InitialPhase;

    // ════════════════════════════════════════════════════════════════════════
    //  注入可能フィールド + スレッド安全用ロック
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 戦闘・予言生成・婚姻継承等で使う乱数発生器。Initialize で注入される。
    /// テストでは seeded Random を渡すことで再現性を確保できる。
    /// </summary>
    private Random _rng = new();

    /// <summary>
    /// 状態 (3 プロパティ) を一括差し替えする際の排他ロック。Godot のゲーム
    /// ロジックは主に単一スレッドだが、念のためマルチスレッドからの並列呼び出しに
    /// 備える（ユーザー仕様: 「スレッド安全やヌル安全を考慮した堅牢なガード句」）。
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// 名前キー → 表示用日本語文字列のリゾルバ。LoadLocalization で
    /// res://Config/localization_ja.json から構築される。未ロード時は null で、
    /// その場合 ResolveDisplayName は生のキーをフォールバック表示する。
    /// </summary>
    private NameResolver? _nameResolver;

    // ════════════════════════════════════════════════════════════════════════
    //  初期化 API
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ゲーム開始時、または新規 1 周開始時に呼ぶ初期化メソッド。
    /// 全引数 null 許容で、null の場合はデフォルトの初期状態を作る。
    ///
    /// テスト容易性:
    ///   - initialRoster: 任意ロスタを直接注入可（例: テスト用の固定 3 名）
    ///   - initialEconomy: 任意残高で開始可（例: 50 pt から開始してテスト）
    ///   - initialTimeline: 任意の予言セットで開始可
    ///   - rng: seeded Random でテストの完全再現性を確保
    ///
    /// 本メソッドは何度でも呼べる（「新しい旅団でやり直し」の挙動）。
    /// </summary>
    /// <param name="initialRoster">初期旅団員リスト（null なら空）</param>
    /// <param name="initialEconomy">初期経済状態（null なら残高 0）</param>
    /// <param name="initialTimeline">初期タイムライン（null ならデフォルト生成）</param>
    /// <param name="rng">乱数発生器（null なら new Random()）</param>
    public void Initialize(
        IReadOnlyList<Unit>? initialRoster = null,
        PointsEconomy? initialEconomy = null,
        TimelineEngine? initialTimeline = null,
        Random? rng = null)
    {
        lock (_stateLock)
        {
            _rng = rng ?? new Random();
            CurrentEconomy = initialEconomy ?? PointsEconomy.CreateInitial();
            BattalionRoster = initialRoster?.ToImmutableList() ?? ImmutableList<Unit>.Empty;
            CurrentTimeline = initialTimeline
                ?? TimelineEngine.CreateInitial(TimelineEngine.DefaultGenerator, _rng);
            CurrentPhase = GamePhaseFlow.InitialPhase; // 新規 1 周は常に Chronicle から
            IsInitialized = true;
        }

        // ロック解放後にシグナル発火（lock 内 EmitSignal はデッドロックリスク）
        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalPhaseChanged);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ラストヒット解決の受領
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 戦闘画面から「どのユニットがラストヒットを取ったか」のユニット ID を受領し、
    /// BattleManager.ExecuteLastHit を実行して、結果をロスタと経済に一括反映する。
    ///
    /// 反映内容:
    ///   1. result.NewUnit でロスタの該当ユニットを差し替え
    ///   2. result.GreedPointsStolen &gt; 0 なら CurrentEconomy.EarnDirect で
    ///      経済に直接加算（CoinGreed Lv5 の特殊効果）
    ///
    /// 戻り値:
    ///   - LastHitResult: 成長・破壊・強奪の詳細（UI 演出に使用）
    ///   - null: ユニットが見つからない、または未初期化
    /// </summary>
    public LastHitResult? ResolveLastHit(Guid unitId)
    {
        bool rosterChanged = false;
        bool economyChanged = false;
        LastHitResult? result;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // ロスタから対象ユニットを探す
            var index = BattalionRoster.FindIndex(u => u.Id == unitId);
            if (index < 0) return null;

            var unit = BattalionRoster[index];

            // 純粋ロジック呼び出し
            result = BattleManager.ExecuteLastHit(unit, _rng);

            // ロスタ差し替え（不変更新）
            BattalionRoster = BattalionRoster.SetItem(index, result.NewUnit);
            rosterChanged = true;

            // 強欲効果による直接ポイント加算
            if (result.GreedPointsStolen > 0)
            {
                CurrentEconomy = CurrentEconomy.EarnDirect(result.GreedPointsStolen);
                economyChanged = true;
            }
        }

        if (rosterChanged)  SafeEmit(SignalRosterChanged);
        if (economyChanged) SafeEmit(SignalEconomyChanged);

        return result;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  予言の選択 + タイムスキップ
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// プレイヤーが選択した予言の ID を受領し、以下を一挙に実行する:
    ///   1. TimelineEngine.ApplyTimeSkipToRoster で全生存ユニットを一斉加齢
    ///   2. PointsEconomy.EarnFromTimeSkip で定期収入を加算 (SoT #1)
    ///   3. TimelineEngine.AdvanceToNextTurn で次ターンの予言 3 つを再生成
    ///
    /// 戻り値:
    ///   - Prophecy: 選択された予言（UI が Kind に応じて次の演出を起動するため）
    ///   - null: 不正な予言 ID、または未初期化
    ///
    /// 注: 予言の Kind ごとの効果（戦闘発生・スカウト等）は本メソッドでは
    ///     実行しない。戻り値の Prophecy を見て、UI 側で対応するシーンを呼ぶ。
    /// </summary>
    public Prophecy? SelectProphecyAndAdvance(Guid prophecyId)
    {
        Prophecy? selected;

        lock (_stateLock)
        {
            if (!IsInitialized || CurrentTimeline is null) return null;
            if (!CurrentTimeline.IsValidSelection(prophecyId)) return null;

            // 1. 選択された予言を取り出し
            selected = CurrentTimeline.GetSelectionOrThrow(prophecyId);

            // 2. 全旅団員を一斉加齢（生存ユニットのみ）
            var newRoster = TimelineEngine.ApplyTimeSkipToRoster(BattalionRoster, selected.SkipYears);
            BattalionRoster = newRoster.ToImmutableList();

            // 3. 定期収入を加算 (SoT #1)
            CurrentEconomy = CurrentEconomy.EarnFromTimeSkip(selected.SkipYears);

            // 4. 次ターンの予言 3 つを生成（過去予言は完全破棄）
            CurrentTimeline = CurrentTimeline.AdvanceToNextTurn(
                TimelineEngine.DefaultGenerator, _rng);
        }

        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);

        // 予言を選択したら自動的に次フェーズ（Guild）へ。Chronicle 以外で呼ばれた
        // 場合は状態マシンのガードにより no-op となる（一方通行の安全性）。
        TryAdvanceTo(GamePhase.Guild);

        return selected;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  手動婚姻の実行
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 両親のユニット ID と子ユニットのスペック (NewbornSpec) を受領し、
    /// MarriageService.ExecuteManualMarriage を実行する。
    ///
    /// 実行内容:
    ///   1. 自然婚姻判定 (BattleAffinity 双方向 ≥ 150 ならコスト 0)
    ///   2. コスト算出と PointsEconomy.SpendPoints での消費
    ///   3. 子ユニットの生成と BattalionRoster への追加
    ///
    /// 戻り値:
    ///   - MarriageResult: コスト・タダ結婚フラグ・生成された子の詳細
    ///   - null: 親が見つからない、ポイント不足、未初期化、その他失敗
    /// </summary>
    public MarriageResult? ExecuteMarriage(
        Guid fatherId,
        Guid motherId,
        NewbornSpec newborn)
    {
        if (newborn is null) return null;

        MarriageResult? result;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            var father = BattalionRoster.FirstOrDefault(u => u.Id == fatherId);
            var mother = BattalionRoster.FirstOrDefault(u => u.Id == motherId);
            if (father is null || mother is null) return null;

            // 名前自動生成の重複回避: 現在のロスタ全員のファーストネームキーを渡す。
            var usedFirstNameKeys = BattalionRoster
                .Select(u => u.FirstNameKey)
                .ToHashSet(StringComparer.Ordinal);

            try
            {
                result = MarriageService.ExecuteManualMarriage(
                    CurrentEconomy, father, mother, newborn, _rng, usedFirstNameKeys);
            }
            catch (InvalidOperationException)
            {
                // ポイント不足、両親が同一等の論理エラー → UI に null を返して
                // 例外を伝播させない (ヌル安全)
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            // 経済・ロスタを一括差し替え
            CurrentEconomy = result.NewEconomy;
            BattalionRoster = BattalionRoster.Add(result.Child);
        }

        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalRosterChanged);

        return result;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  外様スカウト（血縁なしユニットの有償採用）
    // ════════════════════════════════════════════════════════════════════════
    //  設計憲法: 「ポイントを消費して雇うのは血縁なしの外様のみ」。
    //  子供 (婚姻で生まれたユニット) は 0 pt でロスタに既に含まれるのに対し、
    //  外様はこの API でポイントを支払って即戦力 (成人) を 1 名加える。
    //
    //  ★ 残高検証・ポイント消費・ユニット生成・ロスター追加の純粋ロジックは
    //    Godot 非依存の ScoutService (Core/Managers) に分離済み。本メソッドは
    //    「状態の差し替えとシグナル発火」だけに専念する（責務分離 + テスト容易性）。

    /// <summary>
    /// 指定コストのポイントを消費して、血縁関係のないランダムな外様ユニットを
    /// 1 名生成し、大隊ロスタへ追加する。
    ///
    /// 実行内容:
    ///   1. ScoutService.TryScout で残高検証 → 消費 → 外様生成 → 追加を一括試行
    ///   2. 成功時のみ CurrentEconomy と BattalionRoster を一括差し替え
    ///   3. EconomyChanged / RosterChanged を発火
    ///
    /// 戻り値:
    ///   - Unit: 採用された外様ユニット（UI が「○○が加入！」演出に使う）
    ///   - null: 残高不足・未初期化・cost が負、等の失敗
    /// </summary>
    /// <param name="cost">スカウトに支払うポイント数（非負）</param>
    public Unit? ExecuteScout(int cost)
    {
        if (cost < 0) return null;

        Unit? recruited;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 純粋ロジックへ委譲。失敗（残高不足・不正入力）は null で返る。
            var result = ScoutService.TryScout(CurrentEconomy, BattalionRoster, cost, _rng);
            if (result is null) return null;

            // 成功 → 状態を一括差し替え
            CurrentEconomy  = result.NewEconomy;
            BattalionRoster = result.NewRoster;
            recruited       = result.Recruit;
        }

        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalRosterChanged);

        return recruited;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  ゲームフェーズ状態マシン（一方通行の遷移）
    // ════════════════════════════════════════════════════════════════════════
    //  遷移可否の判断は純粋ロジック GamePhaseFlow に集約する。本クラスは
    //  「現在フェーズの保持」と「PhaseChanged シグナルの発火」だけを担う。
    //
    //    Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──▶（次世代の）Chronicle
    //
    //  後退・飛び越し（例 Chronicle → Battle）はすべて拒否される（false 返却）。

    /// <summary>
    /// 現在フェーズを循環順序で 1 つ進める（Chronicle→Guild→Formation→Battle→Chronicle）。
    /// 常に合法（構造上、各フェーズの次はただ 1 つ）。遷移後 PhaseChanged を発火する。
    /// </summary>
    /// <returns>遷移後の新しいフェーズ。未初期化時は現状維持で何もしない。</returns>
    public GamePhase AdvancePhase()
    {
        GamePhase next;
        lock (_stateLock)
        {
            if (!IsInitialized) return CurrentPhase;
            next = GamePhaseFlow.Next(CurrentPhase);
            CurrentPhase = next;
        }

        SafeEmit(SignalPhaseChanged);
        return next;
    }

    /// <summary>
    /// 指定フェーズへの遷移を試みる。合法（現在フェーズのちょうど次）な場合のみ
    /// 遷移して PhaseChanged を発火し true を返す。不正な飛び越し・後退・自己遷移、
    /// および未初期化の場合は状態を変えず false を返す（一方通行のガード）。
    /// </summary>
    /// <param name="target">遷移先として要求するフェーズ。</param>
    public bool TryAdvanceTo(GamePhase target)
    {
        lock (_stateLock)
        {
            if (!IsInitialized) return false;
            if (!GamePhaseFlow.CanTransition(CurrentPhase, target)) return false;
            CurrentPhase = target;
        }

        SafeEmit(SignalPhaseChanged);
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  名前解決（ローカライズ）
    // ════════════════════════════════════════════════════════════════════════
    //  純粋層 NameResolver（Core/Naming）に解決ロジックを委ね、本クラスは
    //  res:// からの localization テキスト読み込み（Godot I/O）だけを担う。
    //  SaveManager（I/O）と SaveSerializer（純粋）の層別と同じ思想。

    /// <summary>名前テキストを引く既定のローカライズ設定リソースパス。</summary>
    public const string LocalizationResourcePath = "res://Config/localization_ja.json";

    /// <summary>
    /// localization 設定（既定 res://Config/localization_ja.json）を読み込み、
    /// 名前リゾルバを構築する。読み込み・解析に失敗しても例外は投げず false を返す
    /// （その場合 ResolveDisplayName は生のキーをフォールバック表示する）。
    /// </summary>
    /// <param name="path">読込元パス。null/空なら既定パス。</param>
    public bool LoadLocalization(string? path = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(path)
            ? LocalizationResourcePath
            : path!;

        try
        {
            if (!Godot.FileAccess.FileExists(targetPath)) return false;
            using var file = Godot.FileAccess.Open(targetPath, Godot.FileAccess.ModeFlags.Read);
            if (file is null) return false;

            var json = file.GetAsText();
            if (string.IsNullOrWhiteSpace(json)) return false;

            _nameResolver = NameResolver.FromLocalizationJson(json);
            return true;
        }
        catch
        {
            // 設定欠落・破損時もゲームを止めない（生キーフォールバックで継続）。
            return false;
        }
    }

    /// <summary>
    /// テスト・CLI 環境用に、構築済みの名前リゾルバを直接注入する。
    /// （Godot 非依存の純粋経路で表示名解決を差し込みたい場合に使う。）
    /// </summary>
    public void ConfigureNameResolver(NameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _nameResolver = resolver;
    }

    /// <summary>
    /// ファーストネームキー・ファミリーネームキーから表示用日本語氏名を解決する。
    /// 複合キー（'@' 連結）は「称号 ＋ 名前 ＋（あれば）姓」へ自動連結される。
    /// リゾルバ未ロード時は生のキーをそのまま連結して返す（フォールバック）。
    /// </summary>
    public string ResolveDisplayName(string firstNameKey, string? lastNameKey = null)
    {
        if (_nameResolver is not null)
        {
            return _nameResolver.ResolveFullName(firstNameKey, lastNameKey);
        }

        // フォールバック: リゾルバ未ロードでも落とさず、キーをそのまま見せる。
        return string.IsNullOrEmpty(lastNameKey)
            ? firstNameKey
            : $"{firstNameKey}{lastNameKey}";
    }

    /// <summary>
    /// ユニットの表示用日本語氏名を解決する便宜オーバーロード。
    /// </summary>
    public string ResolveDisplayName(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        return ResolveDisplayName(unit.FirstNameKey, unit.LastNameKey);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  読み取り専用クエリヘルパー（UI 利便用）
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 指定 ID の旅団員を取得する。存在しない場合 null。
    /// UI から「クリックされたカードに対応するユニットを取得」等で使う。
    /// </summary>
    public Unit? FindUnit(Guid unitId)
    {
        if (!IsInitialized) return null;
        return BattalionRoster.FirstOrDefault(u => u.Id == unitId);
    }

    /// <summary>
    /// 現在提示されている予言 3 つを取得する。未初期化 / null なら空配列。
    /// </summary>
    public IReadOnlyList<Prophecy> GetCurrentProphecies()
        => CurrentTimeline?.CurrentOptions
           ?? (IReadOnlyList<Prophecy>)Array.Empty<Prophecy>();

    /// <summary>
    /// 現在の旅団員数（生存者数ではなく登録上の人数）。
    /// </summary>
    public int RosterSize => BattalionRoster.Count;

    /// <summary>
    /// 生存中の旅団員のみを返す純粋クエリ。
    /// </summary>
    public IReadOnlyList<Unit> GetAliveUnits()
        => BattalionRoster.Where(u => u.IsAlive).ToList();

    // ════════════════════════════════════════════════════════════════════════
    //  セーブ＆ロード（永続化）
    // ════════════════════════════════════════════════════════════════════════
    //  3 つの不変レコード状態 (CurrentEconomy / CurrentTimeline / BattalionRoster)
    //  を SaveManager 経由で user://save_data.json に保存・復元する。
    //
    //  ★ Random は保存しない。LoadGame では新しい Random を再注入する
    //    （ユーザー仕様: 「Random インスタンスは保存せず、ロード時に再注入」）。

    /// <summary>
    /// 現在の 3 状態を指定パス（既定 SaveManager.DefaultSavePath）へ保存する。
    ///
    /// 戻り値:
    ///   - true : 保存成功
    ///   - false: 未初期化、タイムライン null、書き込み失敗のいずれか
    ///
    /// スレッド安全のため、状態のスナップショットを lock 内で取得してから、
    /// ファイル I/O はロック外で行う（I/O 中の lock 占有を避ける）。
    /// </summary>
    /// <param name="path">保存先パス。null/空なら既定の user://save_data.json。</param>
    public bool SaveGame(string? path = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(path)
            ? SaveManager.DefaultSavePath
            : path!;

        PointsEconomy economy;
        TimelineEngine timeline;
        ImmutableList<Unit> roster;

        lock (_stateLock)
        {
            if (!IsInitialized || CurrentTimeline is null) return false;
            economy  = CurrentEconomy;
            timeline = CurrentTimeline;
            roster   = BattalionRoster;
        }

        return SaveManager.SaveToFile(targetPath, economy, timeline, roster);
    }

    /// <summary>
    /// 指定パスのセーブデータを読み込み、3 状態を一括で復元する。
    ///
    /// 復元後の処理:
    ///   - _rng に新しい Random（または引数 rng）を再注入（★ Random は保存しない設計）
    ///   - IsInitialized を true に
    ///   - StateInitialized / Economy / Timeline / Roster の全シグナルを発火し
    ///     UI を初期化と同じ経路で完全再描画させる
    ///
    /// 戻り値:
    ///   - true : ロード成功（状態を差し替え済み）
    ///   - false: ファイルなし・パース失敗（既存の状態は一切変更しない）
    /// </summary>
    /// <param name="path">読込元パス。null/空なら既定の user://save_data.json。</param>
    /// <param name="rng">ロード後に再注入する乱数発生器。null なら new Random()。</param>
    public bool LoadGame(string? path = null, Random? rng = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(path)
            ? SaveManager.DefaultSavePath
            : path!;

        // ファイル I/O とパースはロック外（ここで失敗しても既存状態は無傷）
        var loaded = SaveManager.LoadFromFile(targetPath);
        if (loaded is null) return false;

        lock (_stateLock)
        {
            _rng = rng ?? new Random();          // ★ Random はロード時に新規注入
            CurrentEconomy  = loaded.Economy;
            CurrentTimeline = loaded.Timeline;
            BattalionRoster = loaded.Roster;
            CurrentPhase    = GamePhaseFlow.InitialPhase; // ロード再開は Chronicle から
            IsInitialized   = true;
        }

        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalPhaseChanged);

        return true;
    }

    /// <summary>
    /// 既定パス（または指定パス）にセーブデータが存在するか。
    /// タイトル画面の「つづきから」ボタン活性制御等に使う想定。
    /// </summary>
    public bool HasSaveData(string? path = null)
    {
        var targetPath = string.IsNullOrWhiteSpace(path)
            ? SaveManager.DefaultSavePath
            : path!;
        return SaveManager.HasSaveFile(targetPath);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  安全なシグナル発火
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// シグナル発火を Godot 環境と CLI 環境の両方で安全に行うラッパー。
    ///
    /// - Godot 環境 (シーンツリーにアタッチ済み): 通常通り EmitSignal
    /// - Godot 環境だが未アタッチ: スキップ (IsInsideTree() == false)
    /// - 完全な CLI 環境 (Godot ランタイムなし): 例外を吐かず無視 (catch)
    ///
    /// これにより、xUnit/NUnit から
    ///   var g = new ChronicleGlobal();
    ///   g.Initialize(...);
    ///   g.ResolveLastHit(...);
    ///   Assert.AreEqual(..., g.CurrentEconomy.CurrentBalance);
    /// が動作する（状態遷移ロジックを 100% テスト可能）。
    /// </summary>
    private void SafeEmit(string signalName)
    {
        try
        {
            if (IsInsideTree())
            {
                EmitSignal(signalName);
            }
        }
        catch
        {
            // テスト環境や、シグナル登録ミス等の異常時にもロジックを止めない。
            // 本来の Godot 環境では IsInsideTree() == true なら EmitSignal は成功する。
        }
    }
}
