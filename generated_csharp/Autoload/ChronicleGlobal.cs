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
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Localization;
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
    public const string SignalFormationChanged  = "FormationChanged";
    public const string SignalBattleChanged     = "BattleChanged";

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

    /// <summary>
    /// CurrentFormation（V字3×3編成盤面）が更新された時に発火するシグナル。
    /// 編成画面 UI がこれを受け、盤面を丸ごと読み直して 9 マスを再描画する。
    /// </summary>
    [Signal] public delegate void FormationChangedEventHandler();

    /// <summary>
    /// CurrentBattle（1 ターン戦闘解決リゾルバの現在スナップショット）が更新された時に
    /// 発火するシグナル。戦闘開始・各ターン解決・戦闘終了（null 化）のいずれでも流れる。
    /// 戦闘画面 UI がこれを受け、CurrentBattle を丸ごと読み直して 9 マスの生存・HP・敵カードを
    /// 再描画する。非戦闘状態（CurrentBattle == null）への遷移もこのシグナルで通知される。
    /// </summary>
    [Signal] public delegate void BattleChangedEventHandler();

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

    /// <summary>
    /// V字 3×3 編成盤面の現在状態。常にちょうど 9 スロットを内包する完全不変レコード。
    /// 盤面は Unit 実体を持たず OccupantId(Guid?) のみを保持する薄い参照レイヤであり、
    /// 正本は <see cref="BattalionRoster"/> 側にある（単一 SoT・設計憲法 ③）。
    ///
    /// 外部からは読み取りのみ。不変レコードゆえ、ゲッタは現在のスナップショット参照を
    /// そのまま安全に返せる（参照読み取りはアトミックで、内容は変更不能）。更新は
    /// PlaceUnitOnFormation / ClearFormationSlot / SwapFormationSlots / RotateFormation
    /// 経由のみ。ロスタから完全ロストしたユニットは世代交代の中で自動的に掃き出される。
    /// </summary>
    public FormationBoard CurrentFormation { get; private set; } = FormationBoard.Empty();

    /// <summary>
    /// 現在進行中の戦闘の不変スナップショット。非戦闘時および初期状態は null。
    /// 戦闘の唯一の真実（SoT）であり、外部からは読み取りのみ。更新は
    /// <see cref="StartBattle"/> / <see cref="ResolveBattleTurn"/> / <see cref="EndBattle"/>
    /// 経由のみ。不変レコードゆえ参照読み取りはアトミックで安全（内容は変更不能）。
    /// </summary>
    public BattleSnapshot? CurrentBattle { get; private set; }

    /// <summary>
    /// 直近の戦闘の戦果決算（開戦時と終了時の参加者静止画を Guid 突合した不変差分）。
    /// 非戦闘時・初期状態は <see cref="BattleSpoils.Empty"/>。<see cref="EndBattle"/> の
    /// ロック内で確定し、以後は読み取り専用。次段の戦果決算スクリーン（無状態 UI）が
    /// これを読み取るだけの公開スナップショットになる（戦果のロスタ正本化とは関心分離）。
    /// </summary>
    public BattleSpoils LastBattleSpoils { get; private set; } = BattleSpoils.Empty;

    // ════════════════════════════════════════════════════════════════════════
    //  注入可能フィールド + スレッド安全用ロック
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 戦闘・予言生成・婚姻継承等で使う乱数発生器。Initialize で注入される。
    /// テストでは seeded Random を渡すことで再現性を確保できる。
    /// </summary>
    private Random _rng = new();

    /// <summary>
    /// 戦闘セッション専用の乱数発生器。世代進行用の <see cref="_rng"/> とは独立した
    /// ストリームを持ち、1 つの戦闘ごとに <see cref="StartBattle"/> で改めてシードされる。
    /// これにより「同一の開始局面＋同一シードなら、何ターン解決しても全環境で同一結果」
    /// という戦闘の決定論チェーンを 1 戦闘の幕開けから幕引きまで貫通させる。常に
    /// <see cref="_stateLock"/> 内でのみ読み書きする（ResolveBattleTurn でリゾルバへ注入）。
    /// ★ セーブには含めない（Random は永続化しない設計に準拠）。
    /// </summary>
    private Random _battleRng = new();

    /// <summary>
    /// 開戦時の参加者静止画（Id → Unit）。<see cref="StartBattle"/> で捕捉し、
    /// <see cref="EndBattle"/> で終了時の <see cref="BattleSnapshot.Combatants"/> と
    /// Guid 突合して <see cref="LastBattleSpoils"/> を算出するための「開戦時の基準点」。
    /// 戦果決算は「開戦時 → 終了時」の差分なので、開戦の瞬間を別途保持する必要がある
    /// （CurrentBattle は最新ターンへ毎回差し替わり、開戦時の値を失うため）。常に
    /// <see cref="_stateLock"/> 内でのみ読み書きする。★ セーブには含めない（一過性）。
    /// </summary>
    private ImmutableDictionary<Guid, Unit> _battleOpeningCombatants =
        ImmutableDictionary<Guid, Unit>.Empty;

    /// <summary>
    /// 状態 (3 プロパティ) を一括差し替えする際の排他ロック。Godot のゲーム
    /// ロジックは主に単一スレッドだが、念のためマルチスレッドからの並列呼び出しに
    /// 備える（ユーザー仕様: 「スレッド安全やヌル安全を考慮した堅牢なガード句」）。
    /// </summary>
    private readonly object _stateLock = new();

    /// <summary>
    /// Chronicle で選択された予言の SkipYears を、Battle→Chronicle のループ閉幕まで
    /// 保留しておくための値。これにより「予言で選んだ年数 = この世代の長さ」となり、
    /// 1 周ぶんの年送り（加齢・完全ロスト・収入・予言再生成）をループの幕引きで
    /// 一括適用できる（「1 世代 = 時間軸 1 周」の構造的保証）。
    ///
    /// ★ Random と同様、セーブには含めない（LoadGame は常に Chronicle から再開する
    ///   ため、保留年数は世代内の一過性の状態として 0 から始まれば足りる）。
    ///   常に _stateLock 内でのみ読み書きする。
    /// </summary>
    private int _pendingGenerationSkipYears;

    /// <summary>
    /// 名前キー → 表示用日本語文字列のリゾルバ。LoadLocalization で
    /// res://Config/localization_ja.json から構築される。未ロード時は null で、
    /// その場合 ResolveDisplayName は生のキーをフォールバック表示する。
    /// </summary>
    private NameResolver? _nameResolver;

    /// <summary>
    /// フェーズスラッグ → 表示用日本語フェーズ名のリゾルバ。LoadLocalization で
    /// 名前リゾルバと同じ JSON から構築される。未ロード時は null で、その場合
    /// ResolvePhaseName は生のスラッグをフォールバック表示する。
    /// </summary>
    private PhaseNameResolver? _phaseNameResolver;

    /// <summary>
    /// マスターデータ識別子（ジョブ / アイテム / 予言の種類）→ 表示用日本語テキストの
    /// リゾルバ。LoadLocalization で他リゾルバと同じ JSON から構築される。未ロード時は
    /// null で、その場合 ResolveJobName 等は enum 名をフォールバック表示する。
    /// </summary>
    private MasterDataNameResolver? _masterDataNameResolver;

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
            CurrentFormation = FormationBoard.Empty();  // 新規開始は空盤面から
            CurrentBattle = null;                       // 新規開始時は非戦闘状態
            _battleRng = new Random();                  // 戦闘乱数は StartBattle で再シード
            _pendingGenerationSkipYears = 0;            // 新規開始時は保留年数なし
            _battleOpeningCombatants = ImmutableDictionary<Guid, Unit>.Empty; // 戦果基準点も更地
            LastBattleSpoils = BattleSpoils.Empty;      // 新規開始時は戦果なし
            IsInitialized = true;
        }

        // ロック解放後にシグナル発火（lock 内 EmitSignal はデッドロックリスク）
        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalFormationChanged);
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
    /// プレイヤーが選択した予言の ID を受領し、「この世代の長さ」として確定する。
    ///
    /// ★ 重要（「1 世代 = 時間軸 1 周」の構造）:
    ///   本メソッドは加齢・収入・予言再生成を **ここでは行わない**。選択された予言の
    ///   SkipYears を <see cref="_pendingGenerationSkipYears"/> に保留するだけに留め、
    ///   実際の年送り（加齢 → 完全ロスト → 定期収入 → 次予言生成）はループの幕引き、
    ///   すなわち Battle → Chronicle の遷移時 (<see cref="AdvancePhase"/> →
    ///   <see cref="AdvanceGenerationLocked"/>) に一括適用する。
    ///   これにより「Chronicle で選んだ年数ぶんを 1 周かけて戦い抜き、幕引きで一気に
    ///   時が流れる」という時間軸の周回構造が保証される。
    ///
    /// 実行内容:
    ///   1. 選択予言の妥当性を検証し取り出す
    ///   2. その SkipYears を保留年数として記録する
    ///   3. 状態マシンを Guild フェーズへ進める
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

            // 選択された予言を取り出し、その SkipYears を「この世代の長さ」として保留。
            // 実際の年送りはループ幕引き（Battle→Chronicle）でまとめて適用する。
            selected = CurrentTimeline.GetSelectionOrThrow(prophecyId);
            _pendingGenerationSkipYears = selected.SkipYears;
        }

        // 予言を選択したら自動的に次フェーズ（Guild）へ。Chronicle 以外で呼ばれた
        // 場合は状態マシンのガードにより no-op となる（一方通行の安全性）。
        // ※ 年送りは行わないため Roster/Economy/Timeline は変化せず、ここで発火する
        //   のは PhaseChanged（TryAdvanceTo 内）のみ。
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
    ///
    /// ★ ループ幕引きの年送り（「1 世代 = 時間軸 1 周」）:
    ///   Battle → Chronicle の遷移はゲームループ 1 周の閉幕にあたる。この遷移のときだけ
    ///   <see cref="AdvanceGenerationLocked"/> を呼び、保留しておいた SkipYears ぶんの
    ///   年送り（全旅団員の加齢 → 寿命到達/戦闘死の完全ロスト仕分け → 定期収入 →
    ///   次世代の予言 3 つの再生成）を一括適用する。年送りが起きた場合は、UI が確実に
    ///   追従できるよう「データ系シグナル（Roster/Economy/Timeline）→ PhaseChanged」
    ///   の順で発火する（PhaseChanged を最後にすることで、画面切り替え前に新データが
    ///   確定している）。
    /// </summary>
    /// <returns>遷移後の新しいフェーズ。未初期化時は現状維持で何もしない。</returns>
    public GamePhase AdvancePhase()
    {
        GamePhase next;
        bool generationAdvanced = false;
        bool formationChanged = false;

        lock (_stateLock)
        {
            if (!IsInitialized) return CurrentPhase;

            var from = CurrentPhase;
            next = GamePhaseFlow.Next(from);

            // Battle → Chronicle はループ 1 周の幕引き。ここで世代交代（年送り）を行う。
            if (from == GamePhase.Battle && next == GamePhase.Chronicle)
            {
                formationChanged = AdvanceGenerationLocked();
                generationAdvanced = true;
            }

            CurrentPhase = next;
        }

        // ロック解放後にシグナル発火。年送りがあった場合はデータ系を先に流し、
        // 画面切り替え契機の PhaseChanged を最後に発火する（順序保証）。
        if (generationAdvanced)
        {
            SafeEmit(SignalRosterChanged);
            SafeEmit(SignalEconomyChanged);
            SafeEmit(SignalTimelineChanged);
            // 完全ロストで盤面から掃き出しが発生した時だけ FormationChanged を流す。
            if (formationChanged) SafeEmit(SignalFormationChanged);
        }

        SafeEmit(SignalPhaseChanged);
        return next;
    }

    /// <summary>
    /// ゲームループ 1 周の幕引き（Battle→Chronicle）に伴う年送り（世代交代）を、
    /// 既に取得済みの <see cref="_stateLock"/> 内で適用する純粋な内部処理。
    /// シグナルは発火しない（呼び出し側 <see cref="AdvancePhase"/> がロック解放後に
    /// まとめて発火する責務を持つ）。
    ///
    /// 適用順序（「1 世代 = 時間軸 1 周」）:
    ///   1. 保留年数 <see cref="_pendingGenerationSkipYears"/> ぶん全旅団員を加齢し、
    ///      寿命到達・戦闘死のユニットを完全ロストとして現役ロスタから外す
    ///      （RosterLifecycle.AdvanceGeneration による加齢→仕分け）。
    ///   2. 同じ年数ぶんの定期収入を経済へ加算（PointsEconomy.EarnFromTimeSkip）。
    ///   3. 次世代の予言 3 つを再生成（TimelineEngine.AdvanceToNextTurn）。
    ///   4. 保留年数を 0 にリセット（次の Chronicle 選択まで持ち越さない）。
    ///
    /// ★ 保留年数が 0（防御的初期値）でも安全に動作する: 加齢 0 でも戦闘死ユニットの
    ///   仕分けは行われ（戦闘後クリーンアップを兼ねる）、EarnFromTimeSkip(0) は例外なく
    ///   据え置きを返す。
    /// </summary>
    private bool AdvanceGenerationLocked()
    {
        var years = _pendingGenerationSkipYears;

        // 1. 加齢 → 完全ロストの仕分け（純粋層 RosterLifecycle に委譲）。
        //    現役のみを次世代ロスタへ持ち越す（離脱者は不可逆に外れる）。
        var advance = RosterLifecycle.AdvanceGeneration(BattalionRoster, years);
        BattalionRoster = advance.SurvivingRoster.ToImmutableList();

        // 2. ロスタ整合フック: 完全ロストしたユニットの ID を盤面からも掃き出す。
        //    盤面が常にロスタ実在 ID のみを参照する不変条件をここで回復する。
        bool formationChanged = ReconcileFormationWithRosterLocked();

        // 3. 定期収入を加算（SoT #1）。
        CurrentEconomy = CurrentEconomy.EarnFromTimeSkip(years);

        // 4. 次世代の予言 3 つを再生成（過去予言は完全破棄）。
        if (CurrentTimeline is not null)
        {
            CurrentTimeline = CurrentTimeline.AdvanceToNextTurn(
                TimelineEngine.DefaultGenerator, _rng);
        }

        // 5. 保留年数をリセット（次の Chronicle 選択で改めて設定される）。
        _pendingGenerationSkipYears = 0;

        // 盤面に掃き出しが起きたかを呼び出し側へ返す（FormationChanged 発火の判断材料）。
        return formationChanged;
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
    //  V字編成盤面（FormationBoard）の操作 API
    // ════════════════════════════════════════════════════════════════════════
    //  盤面は Unit 実体を持たず OccupantId(Guid?) だけを保持する薄い参照レイヤ
    //  （正本は BattalionRoster 側・単一 SoT・設計憲法 ③）。以下 4 操作はいずれも
    //  「① 純粋な不変メソッドで次盤面を生成 → ② _stateLock 内で原子的に差し替え →
    //  ③【ロックを解放してから】FormationChanged を SafeEmit」という単方向フローを
    //  厳守する。ロック保持中に EmitSignal を呼ばないため、シグナル受信側 UI が
    //  CurrentFormation を読み直しても再入ロックは発生せず、デッドロックの余地がない。
    //
    //  変化が無い操作（同一座標スワップ・空席クリア等、純粋層が this を返すケース）は
    //  参照同一性で検出して発火を抑止し、余計な再描画を起こさない。

    /// <summary>
    /// 指定座標へユニットを配置する。同一 ID が別マスに居れば自動退去する
    /// （盤面上で同一ユニットが二重に現れない不変条件を純粋層が保証）。ロスタに
    /// 実在しない ID は無視する（盤面が常に実在 ID のみを参照する整合の即時保証）。
    /// </summary>
    public void PlaceUnitOnFormation(SlotCoordinate coordinate, Guid unitId)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!IsInitialized) return;
            if (!BattalionRoster.Exists(u => u.Id == unitId)) return;

            var next = CurrentFormation.WithUnitAt(coordinate, unitId);
            if (!ReferenceEquals(next, CurrentFormation))
            {
                CurrentFormation = next;
                changed = true;
            }
        }

        if (changed) SafeEmit(SignalFormationChanged);
    }

    /// <summary>指定座標の占有者を取り除く。元から空席なら何もしない（発火もしない）。</summary>
    public void ClearFormationSlot(SlotCoordinate coordinate)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!IsInitialized) return;

            var next = CurrentFormation.ClearedAt(coordinate);
            if (!ReferenceEquals(next, CurrentFormation))
            {
                CurrentFormation = next;
                changed = true;
            }
        }

        if (changed) SafeEmit(SignalFormationChanged);
    }

    /// <summary>2 座標の占有者を入れ替える。同一座標なら何もしない（発火もしない）。</summary>
    public void SwapFormationSlots(SlotCoordinate first, SlotCoordinate second)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!IsInitialized) return;

            var next = CurrentFormation.SwapSlots(first, second);
            if (!ReferenceEquals(next, CurrentFormation))
            {
                CurrentFormation = next;
                changed = true;
            }
        }

        if (changed) SafeEmit(SignalFormationChanged);
    }

    /// <summary>
    /// 分隊（行）単位で占有者の三つ組をローテーションする（列順 0/1/2 は完全保持）。
    /// </summary>
    public void RotateFormation(RotationDirection direction)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!IsInitialized) return;

            var next = CurrentFormation.Rotated(direction);
            if (!ReferenceEquals(next, CurrentFormation))
            {
                CurrentFormation = next;
                changed = true;
            }
        }

        if (changed) SafeEmit(SignalFormationChanged);
    }

    /// <summary>
    /// 現在のロスタに実在しない占有者を盤面から掃き出す整合フック。既に取得済みの
    /// <see cref="_stateLock"/> 内から呼ぶこと（シグナルはここでは発火せず、呼び出し側が
    /// ロック解放後に発火する責務を持つ）。掃き出しが起きたら true を返す。
    /// </summary>
    private bool ReconcileFormationWithRosterLocked()
    {
        var validIds = BattalionRoster.Select(u => u.Id).ToHashSet();
        var next = CurrentFormation.RetainingUnits(validIds);
        if (ReferenceEquals(next, CurrentFormation)) return false;

        CurrentFormation = next;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  戦闘ライフサイクル（BattleResolver の常駐統合）
    // ════════════════════════════════════════════════════════════════════════
    //  純粋層 BattleResolver（盤面・攻防純関数・敵を接着する 1 ターン解決器）を
    //  常駐 SoT へ昇格させ、非戦闘 ⇄ 戦闘のライフサイクルを統治する 3 つの薄い API を
    //  公開する。いずれも編成 API と同じ単方向フロー規律を厳守する:
    //    ① 純粋な BattleResolver を呼んで次スナップショットを生成
    //    ② _stateLock 内で CurrentBattle を原子的に差し替え
    //    ③【ロックを解放してから】BattleChanged を SafeEmit
    //  ロック保持中に EmitSignal を呼ばないため、シグナル受信側 UI が CurrentBattle を
    //  読み直しても再入ロックは発生せず、デッドロックの余地がない。非戦闘時
    //  （CurrentBattle == null）でも全 API がヌル安全に振る舞う（戦闘外呼び出しは no-op）。

    /// <summary>
    /// 現在の編成盤面（<see cref="CurrentFormation"/>）とロスタ（<see cref="BattalionRoster"/>）、
    /// および与えられた敵から初期戦闘スナップショットを生成し、戦闘を開始する。
    ///
    /// 戦闘専用乱数 <see cref="_battleRng"/> をここで改めてシードし、この戦闘の決定論
    /// チェーンの起点を確定する。再現性のため <paramref name="battleSeed"/> を明示できる
    /// （省略時は世代用 <see cref="_rng"/> から 1 つ引いた値を種にして、ゲーム全体の
    /// 単一シード再現性を保ったまま戦闘ストリームを独立させる）。
    ///
    /// 戻り値:
    ///   - BattleSnapshot: 開始直後の初期スナップショット（TurnNumber 0 / Outcome Ongoing）
    ///   - null: 未初期化、または <paramref name="enemy"/> が null（ヌル安全に弾く）
    /// </summary>
    /// <param name="enemy">対戦相手の敵スナップショット（null 不可）。</param>
    /// <param name="battleSeed">戦闘乱数の種（null なら世代用乱数から決定論的に導出）。</param>
    public BattleSnapshot? StartBattle(EnemyState enemy, int? battleSeed = null)
    {
        if (enemy is null) return null;

        BattleSnapshot snapshot;
        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // この戦闘専用の独立した乱数ストリームを（再）シードする。
            _battleRng = battleSeed is { } seed ? new Random(seed) : new Random(_rng.Next());

            snapshot = BattleResolver.CreateInitial(CurrentFormation, BattalionRoster, enemy, _battleRng);
            CurrentBattle = snapshot;

            // 戦果決算の基準点として、開戦の瞬間の参加者静止画を別途捕捉しておく
            // （CurrentBattle は以後のターンで毎回差し替わるため、開戦時の値はここで確保）。
            _battleOpeningCombatants = snapshot.Combatants;
        }

        SafeEmit(SignalBattleChanged);
        return snapshot;
    }

    /// <summary>
    /// 選択された陣形回転（無作戦なら null）を適用して 1 ターンを解決し、CurrentBattle を
    /// 次のスナップショットへ原子的に差し替える。確率要素には戦闘専用乱数
    /// <see cref="_battleRng"/> を注入する（グローバル乱数は一切使わない・決定論保証）。
    ///
    /// UI のアニメーション・ログ再生の受け皿とするため、リゾルバが返した不変イベント
    /// ログ配列（<see cref="ImmutableArray{BattleEvent}"/>）をそのまま呼び出し元へ返す。
    ///
    /// 戻り値:
    ///   - 発生順のイベントログ（このターンに起きた出来事）
    ///   - 空配列: 未初期化、非戦闘時（CurrentBattle == null）、または既に決着済み
    ///     （いずれもヌル安全な no-op として扱い、状態は変えない）
    /// </summary>
    /// <param name="rotation">ターン冒頭に適用する回転作戦（無作戦なら null）。</param>
    public ImmutableArray<BattleEvent> ResolveBattleTurn(RotationDirection? rotation)
    {
        BattleTurnResult result;
        lock (_stateLock)
        {
            if (!IsInitialized || CurrentBattle is null)
            {
                return ImmutableArray<BattleEvent>.Empty;
            }

            result = BattleResolver.ResolveTurn(CurrentBattle, rotation, _battleRng);
            CurrentBattle = result.Snapshot;
        }

        SafeEmit(SignalBattleChanged);
        return result.Events;
    }

    /// <summary>
    /// 戦闘を終了し、その結末（勝敗）をロスタへ反映してから CurrentBattle を null へ
    /// 安全にクリアし、非戦闘状態へ遷移する。
    ///
    /// 戦闘は <see cref="BattleSnapshot.Combatants"/> 上の Unit 複製に対して進行する
    /// （完全ロストや、とどめによるラストヒット成長は複製側へ記録される）。本メソッドは
    /// その戦闘後の複製を ID で突き合わせて <see cref="BattalionRoster"/> 本体へ書き戻し、
    /// 戦闘の結果（戦死・成長・装備変化）を世代の正本へ確定させる。書き戻しが起きた場合
    /// のみ RosterChanged を流し、最後に必ず BattleChanged を流して UI を非戦闘描画へ導く。
    ///
    /// 戻り値:
    ///   - 終了時点の決着状態（BattalionVictory / BattalionDefeat / Ongoing）
    ///   - Ongoing: 未初期化、または非戦闘時に呼ばれた場合（ヌル安全な no-op）
    /// </summary>
    public BattleOutcome EndBattle()
    {
        bool rosterChanged = false;
        BattleOutcome outcome;

        lock (_stateLock)
        {
            if (!IsInitialized || CurrentBattle is null) return BattleOutcome.Ongoing;

            outcome = CurrentBattle.Outcome;

            // 戦果決算（開戦時 vs 終了時の Guid 突合）を、ロスタ書き戻しの直前に確定する。
            //   - ここで両静止画はまだ手元にある（_battleOpeningCombatants と CurrentBattle）。
            //   - 純粋ファクトリ BattleSpoils.FromBattle に委譲し、本クラスは保持だけ担う。
            //   - 書き戻し（ロスタ正本化）とは関心分離: あちらは状態確定、こちらは差分提示。
            LastBattleSpoils = BattleSpoils.FromBattle(
                _battleOpeningCombatants, CurrentBattle.Combatants, outcome);

            // 戦闘後の参加者複製（戦死・成長・装備変化込み）を正本ロスタへ書き戻す。
            var combatants = CurrentBattle.Combatants;
            var mergedRoster = BattalionRoster;
            for (int index = 0; index < mergedRoster.Count; index++)
            {
                var rosterUnit = mergedRoster[index];
                if (combatants.TryGetValue(rosterUnit.Id, out var afterBattle)
                    && !ReferenceEquals(afterBattle, rosterUnit))
                {
                    mergedRoster = mergedRoster.SetItem(index, afterBattle);
                    rosterChanged = true;
                }
            }

            BattalionRoster = mergedRoster;
            CurrentBattle = null; // 非戦闘状態へ
        }

        if (rosterChanged) SafeEmit(SignalRosterChanged);
        SafeEmit(SignalBattleChanged);
        return outcome;
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
    /// 名前リゾルバとフェーズ名リゾルバを同じ JSON から一度に構築する。読み込み・
    /// 解析に失敗しても例外は投げず false を返す（その場合は生のキー／スラッグを
    /// フォールバック表示する）。
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

            // 同一の localization 本文から名前・フェーズ名・マスターデータ名の
            // 各リゾルバを構築する（res:// の読み取りは一度だけ。純粋層へ文字列を
            // 渡す層別を保つ）。
            _nameResolver = NameResolver.FromLocalizationJson(json);
            _phaseNameResolver = PhaseNameResolver.FromLocalizationJson(json);
            _masterDataNameResolver = MasterDataNameResolver.FromLocalizationJson(json);
            return true;
        }
        catch
        {
            // 設定欠落・破損時もゲームを止めない（生キー／スラッグへフォールバック）。
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

    /// <summary>
    /// テスト・CLI 環境用に、構築済みのフェーズ名リゾルバを直接注入する。
    /// （Godot 非依存の純粋経路でフェーズ表示名解決を差し込みたい場合に使う。）
    /// </summary>
    public void ConfigurePhaseNameResolver(PhaseNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _phaseNameResolver = resolver;
    }

    /// <summary>
    /// 指定フェーズの表示用日本語名を解決する。リゾルバ未ロード時は生のスラッグを
    /// フォールバック表示する（画面を止めず、未登録フェーズも判別できる）。
    /// </summary>
    public string ResolvePhaseName(GamePhase phase)
    {
        return _phaseNameResolver is not null
            ? _phaseNameResolver.Resolve(phase)
            : phase.Slug();
    }

    /// <summary>
    /// 現在フェーズ（CurrentPhase）の表示用日本語名を解決する便宜メソッド。
    /// 画面上部のフェーズインジケータ更新で使う。
    /// </summary>
    public string ResolveCurrentPhaseName() => ResolvePhaseName(CurrentPhase);

    // ════════════════════════════════════════════════════════════════════════
    //  マスターデータ名解決（ジョブ / アイテム / 予言の種類）
    // ════════════════════════════════════════════════════════════════════════
    //  純粋層 MasterDataNameResolver（Core/Localization）に解決ロジックを委ね、
    //  本クラスは res:// 読込（Godot I/O）と「未ロード時は enum 名フォールバック」の
    //  ガードだけを担う。各 UI（TimelineUI / FormationUI / MarriageUI / BattleResultUI）は
    //  これらのメソッド経由で表示名を引き、コード側に日本語・絵文字を一切持たない。

    /// <summary>
    /// テスト・CLI 環境用に、構築済みのマスターデータ名リゾルバを直接注入する。
    /// （Godot 非依存の純粋経路で表示名解決を差し込みたい場合に使う。）
    /// </summary>
    public void ConfigureMasterDataNameResolver(MasterDataNameResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _masterDataNameResolver = resolver;
    }

    /// <summary>
    /// ジョブの表示用日本語名を解決する。リゾルバ未ロード時は enum 名へフォールバック。
    /// </summary>
    public string ResolveJobName(JobId job)
        => _masterDataNameResolver?.ResolveJobName(job) ?? job.ToString();

    /// <summary>
    /// アイテムの表示用日本語名（絵文字込み）を解決する。未ロード時は enum 名へフォールバック。
    /// </summary>
    public string ResolveItemName(ItemId item)
        => _masterDataNameResolver?.ResolveItemName(item) ?? item.ToString();

    /// <summary>
    /// 予言種別の表示用日本語名を解決する。未ロード時は enum 名へフォールバック。
    /// </summary>
    public string ResolveProphecyKindName(ProphecyKind kind)
        => _masterDataNameResolver?.ResolveProphecyKindName(kind) ?? kind.ToString();

    /// <summary>
    /// 予言種別のアイコン（絵文字）を解決する。未ロード時は enum 名へフォールバック。
    /// </summary>
    public string ResolveProphecyKindIcon(ProphecyKind kind)
        => _masterDataNameResolver?.ResolveProphecyKindIcon(kind) ?? kind.ToString();

    /// <summary>
    /// 敵スキル（攻撃予告）の表示用日本語名を、AttackIntent.SkillNameKey（ASCII キー）
    /// から解決する。未ロード・未登録時は生キーへフォールバック（① 準拠で日本語は持たない）。
    /// </summary>
    public string ResolveSkillName(string skillNameKey)
        => _masterDataNameResolver?.ResolveSkillName(skillNameKey) ?? skillNameKey;

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
            CurrentFormation = FormationBoard.Empty();     // 盤面は永続化せずロードは空盤面から
            CurrentBattle   = null;              // 戦闘は永続化しない（ロード再開は非戦闘状態）
            _battleRng      = new Random();       // 戦闘乱数は次の StartBattle で再シード
            _pendingGenerationSkipYears = 0;     // 保留年数は保存しない（Chronicle 再開で再設定）
            IsInitialized   = true;
        }

        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalFormationChanged);
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
