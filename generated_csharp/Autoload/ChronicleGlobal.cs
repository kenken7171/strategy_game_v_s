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
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Localization;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Persistence;
using ChronicleKnights.Core.Shop;
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
    public const string SignalInventoryChanged  = "InventoryChanged";
    public const string SignalDropChoicePending = "DropChoicePending";

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

    /// <summary>
    /// BrigadeInventory（旅団共有の持ち物＝未装着の装備個体）が更新された時に発火するシグナル。
    /// 装備の付け外し（持ち物⇄ユニット）やドロップの取得で流れ、拠点の持ち物 UI が読み直して再描画する。
    /// </summary>
    [Signal] public delegate void InventoryChangedEventHandler();

    /// <summary>
    /// EquipmentDrop の 3 択候補（PendingDropCandidates）が発生した時に発火するシグナル。
    /// GameDirector がこれを受け、現フェーズに関わらず 3 択ドロップ overlay を最前面へ立ち上げる。
    /// </summary>
    [Signal] public delegate void DropChoicePendingEventHandler();

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

    /// <summary>
    /// 旅団共有の持ち物（未装着の装備個体）。外部からは読み取りのみ。更新は
    /// EquipFromInventory / UnequipToInventory / ChooseDroppedEquipment 経由のみ。
    /// 装備は持ち物⇄ユニットを個体保持のまま往復し、複製も消滅もしない（保存則）。
    /// </summary>
    public ImmutableList<Equipment> BrigadeInventory { get; private set; } = ImmutableList<Equipment>.Empty;

    /// <summary>
    /// 取得待ちの 3 択ドロップ候補（EquipmentDrop 予言で生成）。空＝選択待ちなし。プレイヤーが
    /// <see cref="ChooseDroppedEquipment"/> で 1 つ選ぶと、それが持ち物へ入り本配列は空へ戻る（残りは破棄）。
    /// 永続化しない（claim 前にセーブ・終了した場合は失われる＝その年の運）。
    /// </summary>
    public ImmutableArray<Equipment> PendingDropCandidates { get; private set; } = ImmutableArray<Equipment>.Empty;

    /// <summary>Initialize が呼ばれて状態が有効化されているか。</summary>
    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// ゲーム進行の現在フェーズ（状態マシン）。外部からは読み取りのみ。
    /// 遷移は AdvancePhase / TryAdvanceTo 経由でのみ行い、合法性は
    /// 純粋ロジック GamePhaseFlow が判定する（不正な飛び越し・後退は不可）。
    /// </summary>
    public GamePhase CurrentPhase { get; private set; } = GamePhaseFlow.InitialPhase;

    /// <summary>
    /// この年の行動（戦闘＝March / 休息＝Rest）。編成フェーズの「次へ」がこの値で分岐する：
    /// March は 編成 → 戦闘、Rest は 戦闘フェーズを丸ごと回避して 編成 → 年代記（安全な年送り）。
    /// 既定は March。設定は <see cref="SetPlannedAction"/> 経由のみ。1 周の幕引き（年代記復帰）で既定へ戻る。
    /// </summary>
    public PlannedAction CurrentAction { get; private set; } = PlannedAction.March;

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
    /// 直近の戦闘の戦果決算（統合台帳: 開戦時と「とどめ完了後のロスタ」を Guid 突合した
    /// 不変差分）。非戦闘時・初期状態は <see cref="BattleSpoils.Empty"/>。
    /// <see cref="FinalizeBattleSpoils"/> のロック内で確定し、以後は読み取り専用。
    /// ターン制戦闘の成長と、とどめ（<see cref="ResolveLastHit"/>）の成長/喪失を 1 枚へ
    /// 合算するため、確定は <see cref="EndBattle"/> ではなくとどめ完了後まで遅延する。
    /// 次段の戦果決算スクリーン（無状態 UI）がこれを読み取るだけの公開スナップショットに
    /// なる（戦果のロスタ正本化とは関心分離）。
    /// </summary>
    public BattleSpoils LastBattleSpoils { get; private set; } = BattleSpoils.Empty;

    /// <summary>
    /// 直近の「休息」年の決算スナップショット（休息で大隊を立て直したときの成果）。
    /// <see cref="ExecuteRest"/> が純粋層 <see cref="RestService"/> で確定して公開し、
    /// 休息報酬通知 UI（無状態オーバーレイ）がこれを読み取るだけ。戦闘を経由しない年は
    /// <see cref="LastBattleSpoils"/> ではなく本値が「この年の成果」を表す。
    /// </summary>
    public RestOutcome LastRestOutcome { get; private set; } = RestOutcome.None;

    /// <summary>
    /// 旅団史（年代記ナレーション）の累積ログ。世代交代（<see cref="AdvanceGenerationLocked"/>）の
    /// たびに、その世代の損失（引退 / 戦死）と成長（昇級）を不変イベントとして追記していく。
    /// 完全不変（ImmutableArray）ゆえ参照読み取りはアトミックで安全。書き込みは
    /// <see cref="RecordGenerationChronicleLocked"/> 経由のみ（常に <see cref="_stateLock"/> 内）。
    /// 外部へは <see cref="GetChronicleLog"/> がロック下のスナップショットとして開放する。
    /// ★ セーブには含めない（ロード再開時は空から再蓄積する）。
    /// </summary>
    private ImmutableArray<ChronicleLogEntry> _chronicleLog = ImmutableArray<ChronicleLogEntry>.Empty;

    /// <summary>
    /// 英霊アーカイブ（Id → 旅団を去った当時の静止画 Unit）。世代交代の自然離脱
    /// （<see cref="AdvanceGenerationLocked"/> の DepartedRoster）と手動解雇
    /// （<see cref="ExecuteDismiss"/> の dismissed）の瞬間に、外れる直前のユニットを
    /// その場で写し取って蓄積する。家系図オーバーレイ (PedigreeOverlay) が
    /// 「現役ロスタから既に消えた祖先（遺影）」を遺影として描けるよう、Unit に刻まれた
    /// Parentage / SpouseId ごと保存する唯一の保管庫。
    ///
    /// ★ <see cref="GetPedigreeUniverse"/> が「アーカイブ ∪ 現役ロスタ」を合成する際、
    ///   同一 Id は現役側を優先採用する（生きている者の最新状態が常に正本）。
    /// ★ <see cref="_chronicleLog"/> と同様、セーブには含めない（ロード再開時は空から再蓄積）。
    ///   現役ロスタ側の Parentage / SpouseId はセーブ対象なので、ロード直後でも生者同士の
    ///   血統・婚姻リンクは復元される（失われるのは「既に去った祖先の遺影」だけ）。
    ///   常に <see cref="_stateLock"/> 内でのみ読み書きする。
    /// </summary>
    private ImmutableDictionary<Guid, Unit> _ancestralArchive =
        ImmutableDictionary<Guid, Unit>.Empty;

    /// <summary>
    /// 家系図オーバーレイ向けに「血統宇宙」を合成して返す純粋な読み出し API。
    /// 現役ロスタ（<see cref="BattalionRoster"/>）と英霊アーカイブ
    /// （<see cref="_ancestralArchive"/>）を Id で和集合し、同一 Id は常に現役側を採用する
    /// （生者の最新状態が正本、去った者は遺影として補完）。
    ///
    /// PedigreeOverlay はこの辞書だけを頼りに、TargetUnitId を根として Parentage（父母）・
    /// SpouseId（配偶者）の鎖を TryGetValue で手繰り、縦軸の家系ネットを再構築する。
    /// 戻り値は完全不変のスナップショット（ロック下で確定）ゆえ参照読みは安全。
    /// 未初期化でも空辞書を返すヌル安全 API。
    /// </summary>
    public ImmutableDictionary<Guid, Unit> GetPedigreeUniverse()
    {
        lock (_stateLock)
        {
            var builder = _ancestralArchive.ToBuilder();
            // 現役を後勝ちで上書き（同一 Id は生者の最新状態を正本に）。
            foreach (var unit in BattalionRoster)
            {
                builder[unit.Id] = unit;
            }
            return builder.ToImmutable();
        }
    }

    /// <summary>
    /// 旅団を去ろうとしているユニット群を英霊アーカイブへ写し取る（<see cref="_stateLock"/> 保持中専用）。
    /// 既に同一 Id が存在する場合は「去る直前の最新状態」で上書きする。null 要素・空列は安全に無視する。
    /// </summary>
    /// <param name="departed">これからロスタを外れるユニット群（外れる前の静止画）。</param>
    private void ArchiveDepartedLocked(IEnumerable<Unit>? departed)
    {
        if (departed is null) return;
        var builder = _ancestralArchive.ToBuilder();
        foreach (var unit in departed)
        {
            if (unit is null) continue;
            builder[unit.Id] = unit;
        }
        _ancestralArchive = builder.ToImmutable();
    }

    /// <summary>
    /// 旅団史（年代記ナレーション）の現在スナップショットを <see cref="_stateLock"/> 下で安全に
    /// 読み出す。返すのは完全不変の <see cref="ImmutableArray{T}"/>（古い→新しいの時系列順）で、
    /// UI 層はこれを丸ごと読んでナレーション領域を無状態に再描画する（単方向データフロー）。
    /// 未初期化・無履歴でも空配列を返すヌル安全 API。
    /// </summary>
    public ImmutableArray<ChronicleLogEntry> GetChronicleLog()
    {
        lock (_stateLock)
        {
            return _chronicleLog;
        }
    }

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
    /// <see cref="FinalizeBattleSpoils"/> で「とどめ完了後の正本ロスタ」と Guid 突合して
    /// <see cref="LastBattleSpoils"/>（統合台帳）を算出するための「開戦時の基準点」。
    /// 戦果決算は「開戦時 → とどめ完了後」の差分なので、開戦の瞬間を別途保持する必要が
    /// ある（CurrentBattle は最新ターンへ毎回差し替わり、開戦時の値を失うため）。
    /// <see cref="EndBattle"/> を越えて生かし、<see cref="FinalizeBattleSpoils"/> 確定時に
    /// 解放する。常に <see cref="_stateLock"/> 内でのみ読み書きする。
    /// ★ セーブには含めない（一過性）。
    /// </summary>
    private ImmutableDictionary<Guid, Unit> _battleOpeningCombatants =
        ImmutableDictionary<Guid, Unit>.Empty;

    /// <summary>
    /// <see cref="EndBattle"/> 時点で確定した決着状態を、<see cref="FinalizeBattleSpoils"/>
    /// まで保留しておくための値。戦果決算（統合台帳）は「ターン制戦闘 → とどめ
    /// （<see cref="ResolveLastHit"/>）」の両成長を 1 枚に合算するため、決算の確定を
    /// とどめ完了まで遅延させる。その際 Outcome は戦闘終了の瞬間に凍結しておく必要が
    /// あるので、ここで EndBattle 時の結末を退避する。常に <see cref="_stateLock"/> 内
    /// でのみ読み書きする。★ セーブには含めない（一過性）。
    /// </summary>
    private BattleOutcome _lastBattleOutcome = BattleOutcome.Ongoing;

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
    /// Chronicle で選択された予言そのもの（Kind / Value）を、その年の決算まで保留しておく値。
    /// 非戦闘の予言（RewardPoints / ScoutReward / EquipmentDrop / Rest）は休息に流れるため、
    /// <see cref="ExecuteRest"/> がこれを読んで予言の報酬（ポイント加算 / 新人加入 / 装備ドロップ）を
    /// 確定する。消費後・年送り（<see cref="AdvanceGenerationLocked"/>）でクリアする。
    /// ★ Random / 保留年数と同様セーブには含めない。常に _stateLock 内でのみ読み書きする。
    /// </summary>
    private Prophecy? _pendingProphecy;

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
            BrigadeInventory = ImmutableList<Equipment>.Empty; // 新規開始は持ち物なし
            PendingDropCandidates = ImmutableArray<Equipment>.Empty; // 選択待ちドロップなし
            CurrentTimeline = initialTimeline
                ?? TimelineEngine.CreateInitial(TimelineEngine.DefaultGenerator, _rng);
            CurrentPhase = GamePhaseFlow.InitialPhase; // 新規 1 周は常に Chronicle から
            CurrentAction = PlannedAction.March;        // 新規開始時の既定行動は出撃
            CurrentFormation = FormationBoard.Empty();  // 新規開始は空盤面から
            CurrentBattle = null;                       // 新規開始時は非戦闘状態
            _battleRng = new Random();                  // 戦闘乱数は StartBattle で再シード
            _pendingGenerationSkipYears = 0;            // 新規開始時は保留年数なし
            _pendingProphecy = null;                    // 保留予言も更地
            _battleOpeningCombatants = ImmutableDictionary<Guid, Unit>.Empty; // 戦果基準点も更地
            _lastBattleOutcome = BattleOutcome.Ongoing; // 退避中の決着状態も更地
            LastBattleSpoils = BattleSpoils.Empty;      // 新規開始時は戦果なし
            LastRestOutcome  = RestOutcome.None;        // 新規開始時は休息成果なし
            _chronicleLog = ImmutableArray<ChronicleLogEntry>.Empty; // 旅団史も白紙から
            _ancestralArchive = ImmutableDictionary<Guid, Unit>.Empty; // 英霊アーカイブも更地から
            IsInitialized = true;
        }

        // ロック解放後にシグナル発火（lock 内 EmitSignal はデッドロックリスク）
        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalInventoryChanged);
        SafeEmit(SignalFormationChanged);
        SafeEmit(SignalPhaseChanged);
    }

    /// <summary>
    /// 「前世の完全粛清」: 全 SoT（経済・タイムライン・旅団ロスタ・英霊アーカイブ・旅団史・盤面・
    /// 戦闘・戦果）を未初期化の更地へ戻す。ゲームオーバーや完走後にタイトルへ送還された際、前世の
    /// メモリ（PedigreeGraph の素となる血統・TimelineEngine・ChronicleLog・PointsEconomy）を 1 バイトの
    /// リークもなく解放する。SoT が握るのはすべて不変レコード／不変コレクションなので、フレッシュな
    /// 空値へ再代入することで旧参照は解放（GC 回収）され、前世のデータは一切残らない。
    ///
    /// <see cref="Initialize"/> と異なり、新しい 1 周は始めない（<see cref="IsInitialized"/> = false へ
    /// 戻すだけ）。新規開始はタイトルの「Start」が <see cref="StartNewGame"/> で行う。状態消滅を
    /// 観測側へ知らせるため、データ変更シグナルのみ発火する（StateInitialized は発火しない）。
    /// </summary>
    public void Reset()
    {
        lock (_stateLock)
        {
            _rng                     = new Random();
            CurrentEconomy           = PointsEconomy.CreateInitial();
            BattalionRoster          = ImmutableList<Unit>.Empty;
            BrigadeInventory         = ImmutableList<Equipment>.Empty;
            PendingDropCandidates    = ImmutableArray<Equipment>.Empty;
            CurrentTimeline          = null;
            CurrentPhase             = GamePhaseFlow.InitialPhase;
            CurrentAction            = PlannedAction.March;
            CurrentFormation         = FormationBoard.Empty();
            CurrentBattle            = null;
            _battleRng               = new Random();
            _pendingGenerationSkipYears = 0;
            _pendingProphecy         = null;
            _battleOpeningCombatants = ImmutableDictionary<Guid, Unit>.Empty;
            _lastBattleOutcome       = BattleOutcome.Ongoing;
            LastBattleSpoils         = BattleSpoils.Empty;
            LastRestOutcome          = RestOutcome.None;
            _chronicleLog            = ImmutableArray<ChronicleLogEntry>.Empty;
            _ancestralArchive        = ImmutableDictionary<Guid, Unit>.Empty;
            IsInitialized            = false; // ★ 未初期化へ回帰（前世の完全消滅）
        }

        // ロック解放後に「状態が更地化された」ことを観測側へ通知（StateInitialized は出さない）。
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalInventoryChanged);
        SafeEmit(SignalFormationChanged);
        SafeEmit(SignalPhaseChanged);
    }

    /// <summary>
    /// タイトルの「Start」から呼ぶ新規開始の正本。与えられた決定論シードで PRNG を織り、その 1 本の
    /// 乱数ストリームがこの 100 年史の運命を支配する（同一シードからは同一の歴史）。<see cref="Initialize"/>
    /// は全 SoT を新規生成するため、それ自体が前世を完全に上書きする（粛清済みの状態から始めても二重に安全）。
    /// </summary>
    /// <param name="seed">この 100 年史を決定づける決定論 PRNG シード。</param>
    public void StartNewGame(int seed)
    {
        Initialize(rng: new Random(seed));
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
            _pendingProphecy = selected; // その年の報酬決算（ExecuteRest）まで保留

            // ★ 予言の種別がこの年の行動を決める（結線の要）: Battle のみ出撃(March)、それ以外
            //   （Rest / RewardPoints / ScoutReward / EquipmentDrop）は休息(Rest)。これにより
            //   「年代記で戦闘以外の予言を選んだのに編成・戦闘へ進んでしまう」事故を根絶する。
            //   ★ ただし章ボス年（25/50/75/100）は出撃必至（強制戦闘）: 休息予言でも March に上書きし、
            //     章ボスを休息で素通りできないようにする（ボススナップで暦は必ずボス年へ着地するため、
            //     その年に戦闘を強制すれば 4 体の章ボスは構造的に取りこぼせない）。
            CurrentAction = ActionPhaseRouter.ActionForProphecyAtYear(
                selected.Kind, ChronicleTimelineConfig.IsEpochBossYear(CurrentTimeline.Turn));
        }

        // 予言を選択したら自動的に次フェーズ（Guild）へ。Chronicle 以外で呼ばれた
        // 場合は状態マシンのガードにより no-op となる（一方通行の安全性）。
        // ※ 年送りは行わないため Roster/Economy/Timeline は変化せず、ここで発火するのは
        //   PhaseChanged（TryAdvanceTo 内）のみ。CurrentAction は上のロック内で予言種別から
        //   確定済みで、PhaseChanged を受けて新たにマウントされる旅団組合画面がそれを読み直す。
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

            // 経済・ロスタを一括差し替え。婚姻の横リンク（配偶者 SpouseId）を SoT へ定着
            // させるため、旧父・旧母を相互リンク済みの版（UpdatedFather/UpdatedMother）で
            // 置換してから、血統リンク（Parentage）を刻んだ子を加える。これにより
            // GetPedigreeUniverse() が「夫婦」と「親子」の縦横両軸を矛盾なく開放できる。
            CurrentEconomy = result.NewEconomy;
            BattalionRoster = BattalionRoster
                .Replace(father, result.UpdatedFather)
                .Replace(mother, result.UpdatedMother)
                .Add(result.Child);
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

    // ────────────────────────────────────────────────────────────────────────
    //  人事: 戦力外通告（手動解雇）
    // ────────────────────────────────────────────────────────────────────────
    //  設計憲法「旅団の新陳代謝はプレイヤーの手に」。世代交代が寿命到達・戦闘死を
    //  *自動* で外すのに対し、本 API はプレイヤーの意思で現役 1 名を *任意* に外す
    //  （席を空ける・戦力外を切る）。TS 正史 HumanDecisionService.dismissUnit 相当。
    //
    //  ★ 純粋ロジック（対象探索・除去）は Godot 非依存の RosterAdminService に分離済み。
    //    本メソッドは「ロスタの差し替え → 盤面整合 → シグナル発火」だけに専念する。
    //
    //  ★ 解雇はポイント経済に一切触れない（払い戻しなし）。動かすのはロスタと、その
    //    結果として掃き出しが起きた場合のみ編成盤面（FormationChanged）の 2 つだけ。

    /// <summary>
    /// 指定 Id の旅団員に戦力外通告を行い、現役ロスタから即時に外す。
    ///
    /// 実行内容:
    ///   1. RosterAdminService.TryDismiss で対象探索 → 除去を一括試行（不在なら null）。
    ///   2. 成功時のみ BattalionRoster を差し替え、盤面整合フックで占有を掃き出す。
    ///   3. RosterChanged を発火（盤面に掃き出しが起きた時のみ FormationChanged も）。
    ///
    /// 戻り値:
    ///   - Unit: 解雇されたユニット（UI が「○○に戦力外を通告」演出に使う）。
    ///   - null: 未初期化、または指定 Id がロスタに存在しない（no-op）。
    /// </summary>
    /// <param name="unitId">戦力外通告の対象ユニット Id。</param>
    public Unit? ExecuteDismiss(Guid unitId)
    {
        Unit? dismissed;
        bool formationChanged;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 純粋ロジックへ委譲。対象不在は null で返る（no-op）。
            var result = RosterAdminService.TryDismiss(BattalionRoster, unitId);
            if (result is null) return null;

            // 成功 → ロスタを差し替え、盤面からも当該占有を掃き出す。
            BattalionRoster = result.NewRoster;
            formationChanged = ReconcileFormationWithRosterLocked();
            dismissed = result.Dismissed;

            // 解雇の事実を旅団史へ刻む（ロスタから外れる前の静止画から名前キー等を確定捕捉）。
            RecordDismissalChronicleLocked(dismissed);

            // 英霊アーカイブ: 戦力外通告で去る 1 名も、外れた直後に保管庫へ写し取る。
            // これにより、解雇された祖先を根に持つ子孫の家系図も縦軸が途切れない。
            ArchiveDepartedLocked(new[] { dismissed });
        }

        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalTimelineChanged); // 旅団史が 1 行伸びたので年代記ナレーションを再描画させる。
        if (formationChanged) SafeEmit(SignalFormationChanged);

        return dismissed;
    }

    /// <summary>
    /// 手動解雇（戦力外通告）1 件を純粋層 <see cref="ChronicleLog.BuildDismissalEntry"/> で不変エントリへ
    /// 写し取り、旅団史 <see cref="_chronicleLog"/> へ追記する。<see cref="ExecuteDismiss"/> の内部
    /// （<see cref="_stateLock"/> 保持中・対象がロスタから外れる直前）からのみ呼ばれる。
    ///
    /// ★ 世代見出しは現ターン（<see cref="CurrentTimeline"/>.Turn）を用いる。解雇は Guild フェーズで
    ///   起きるため、その時点の世代番号がそのまま見出しになる（世代交代の年送りとは独立した即時記録）。
    /// </summary>
    /// <param name="dismissed">戦力外通告で外れる旅団員（外れる前の静止画）。</param>
    private void RecordDismissalChronicleLocked(Unit dismissed)
    {
        var generation = CurrentTimeline?.Turn ?? 0;
        var entry = ChronicleLog.BuildDismissalEntry(generation, dismissed);
        _chronicleLog = _chronicleLog.Add(entry);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  旅団兵器廠: 装備の購入・強化（共通サイフ PointsEconomy を消費）
    // ────────────────────────────────────────────────────────────────────────
    //  設計憲法③「単一 SoT」。装備の購入・強化はスカウト・婚姻と同じ共通サイフ
    //  （PointsEconomy）を消費する。専用通貨は設けず、EconomyChanged を再利用する。
    //
    //  ★ 純粋ロジック（残高検証・装備生成/差し替え・上限ガード）は Godot 非依存の
    //    ShopService (Core/Shop) に分離済み。本メソッドは「状態の差し替えとシグナル
    //    発火」だけに専念する（責務分離 + テスト容易性）。
    //
    //  ★ コストの SoT は ShopService（BuyCost / UpgradeCostFor）に一元化する。
    //    UI と ChronicleGlobal はともにこの公開 API を参照し、コスト式を二重定義しない。

    /// <summary>
    /// 共通サイフから <see cref="ShopService.BuyCost"/> を消費して、指定ユニットへ新品 Lv1 装備を
    /// 1 個装着する。既存装備があった場合、旧装備は差し替えで完全ロストする。
    ///
    /// 実行内容:
    ///   1. ShopService.TryBuyEquipment で残高検証 → 消費 → 新装備生成 → 装着を一括試行。
    ///   2. 成功時のみ CurrentEconomy と BattalionRoster を一括差し替え。
    ///   3. EconomyChanged / RosterChanged を発火。
    ///
    /// 戻り値:
    ///   - ShopBuyResult: 購入結果（UI が「○○が装備した」「旧装備を失った」演出に使う）。
    ///   - null: 残高不足・未初期化・対象不在、等の失敗（no-op）。
    /// </summary>
    /// <param name="unitId">装備を購入して装着する対象ユニット Id。</param>
    /// <param name="itemId">購入する装備種別（5 大マスターのいずれか）。</param>
    public ShopBuyResult? ExecuteBuyEquipment(Guid unitId, ItemId itemId)
    {
        ShopBuyResult? result;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 純粋ロジックへ委譲。失敗（残高不足・対象不在・不正入力）は null で返る。
            result = ShopService.TryBuyEquipment(
                CurrentEconomy, BattalionRoster, unitId, itemId, ShopService.BuyCost);
            if (result is null) return null;

            // 成功 → 状態を一括差し替え。
            CurrentEconomy  = result.NewEconomy;
            BattalionRoster = result.NewRoster;
        }

        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalRosterChanged);

        return result;
    }

    /// <summary>
    /// 共通サイフから <see cref="ShopService.UpgradeCostFor"/>（現レベル比例）を消費して、指定ユニットの
    /// 現装備を 1 段階レベルアップする。上限 (Lv5) では失敗（ポイント浪費を防ぐ）。
    ///
    /// 実行内容:
    ///   1. ロスタから対象を覗き、現装備レベルから今回の強化コストを算出（SoT は ShopService）。
    ///   2. ShopService.TryUpgradeEquipment で残高検証 → 消費 → レベルアップ → 装着し直しを一括試行。
    ///   3. 成功時のみ CurrentEconomy と BattalionRoster を一括差し替え。
    ///   4. EconomyChanged / RosterChanged を発火。
    ///
    /// 戻り値:
    ///   - ShopUpgradeResult: 強化結果（UI が「○○の装備が強化された」演出に使う）。
    ///   - null: 残高不足・未初期化・対象不在・装備なし・上限到達、等の失敗（no-op）。
    /// </summary>
    /// <param name="unitId">装備を強化する対象ユニット Id。</param>
    public ShopUpgradeResult? ExecuteUpgradeEquipment(Guid unitId)
    {
        ShopUpgradeResult? result;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 現装備レベルから今回の強化コストを確定（対象不在・装備なしは後段の純粋層が null 化）。
            var target = BattalionRoster.Find(u => u.Id == unitId);
            var current = target?.MainEquipment;
            if (current is null) return null;

            var cost = ShopService.UpgradeCostFor(current.Level);

            // 純粋ロジックへ委譲。失敗（残高不足・上限到達・対象不在）は null で返る。
            result = ShopService.TryUpgradeEquipment(
                CurrentEconomy, BattalionRoster, unitId, cost);
            if (result is null) return null;

            // 成功 → 状態を一括差し替え。
            CurrentEconomy  = result.NewEconomy;
            BattalionRoster = result.NewRoster;
        }

        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalRosterChanged);

        return result;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  編成: 装備の直接脱着（無償・ポイント経済を消費しない）
    // ────────────────────────────────────────────────────────────────────────
    //  設計憲法③「単一 SoT・単方向データフロー」。兵器廠（ExecuteBuyEquipment /
    //  ExecuteUpgradeEquipment）が「共通サイフを消費して新品を購入・強化する」のに対し、
    //  本 2 API は「すでに旅団が所有する装備種別を、編成段階でユニットへ素手で着け外しする」
    //  無償の直接脱着を担う。FormationUI / RosterUI の装備スロット（第2段）が直接叩く窓口。
    //
    //  ★ 純粋ロジック（対象探索・WithEquipment 差し替え・番兵）は Godot 非依存の
    //    EquipmentService (Core/Units) に分離済み。本メソッドは「ロスタの差し替えと
    //    シグナル発火」だけに専念する（責務分離・テスト容易性）。
    //
    //  ★ 脱着は経済・盤面占有を一切動かさない（払い戻しなし・席は不動）。よって発火するのは
    //    RosterChanged ただ 1 つ（装備の有無で 9 マスの占有は変わらないため FormationChanged 不要）。

    /// <summary>
    /// 指定 Id の旅団員へ、指定種別の新品 Lv1 装備を無償で装着する（既存装備は差し替えで手放す）。
    ///
    /// 実行内容:
    ///   1. EquipmentService.TryEquip で対象探索 → 新装備生成 → 装着を一括試行（不在なら null）。
    ///   2. 成功時のみ BattalionRoster を差し替える。
    ///   3. RosterChanged を発火する。
    ///
    /// 戻り値:
    ///   - Unit: 装着後のユニット（UI が「○○が装備した」演出に使う）。
    ///   - null: 未初期化、または指定 Id がロスタに存在しない（no-op）。
    /// </summary>
    /// <param name="unitId">装備を装着する対象ユニット Id。</param>
    /// <param name="itemId">装着する装備種別（5 大マスターのいずれか・型安全 enum）。</param>
    public Unit? EquipItem(Guid unitId, ItemId itemId)
    {
        Unit? affected;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 純粋ロジックへ委譲。対象不在は null で返る（no-op・例外なし）。
            var result = EquipmentService.TryEquip(BattalionRoster, unitId, itemId);
            if (result is null) return null;

            // 成功 → ロスタを差し替え（脱着はロスタのみを動かす）。
            BattalionRoster = result.NewRoster;
            affected = result.AffectedUnit;
        }

        SafeEmit(SignalRosterChanged);

        return affected;
    }

    /// <summary>
    /// 指定 Id の旅団員から現装備を取り外す（無償・手放した装備は現状ロスト）。
    ///
    /// 実行内容:
    ///   1. EquipmentService.TryUnequip で対象探索 → 取り外しを一括試行（不在なら null）。
    ///   2. 実際に装備が外れた場合（RosterMutated=true）のみ BattalionRoster を差し替え、
    ///      RosterChanged を発火する。元から未装備なら no-op（不要な再描画を抑止）。
    ///
    /// 戻り値:
    ///   - Unit: 取り外し後（または元から未装備）のユニット（UI 反映に使う）。
    ///   - null: 未初期化、または指定 Id がロスタに存在しない（no-op）。
    /// </summary>
    /// <param name="unitId">装備を取り外す対象ユニット Id。</param>
    public Unit? UnequipItem(Guid unitId)
    {
        Unit? affected;
        bool rosterMutated;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            // 純粋ロジックへ委譲。対象不在は null で返る（no-op・例外なし）。
            var result = EquipmentService.TryUnequip(BattalionRoster, unitId);
            if (result is null) return null;

            rosterMutated = result.RosterMutated;
            if (rosterMutated)
            {
                // 実際に外れた時だけロスタを差し替える（元から未装備なら据え置き）。
                BattalionRoster = result.NewRoster;
            }
            affected = result.AffectedUnit;
        }

        if (rosterMutated)
        {
            SafeEmit(SignalRosterChanged);
        }

        return affected;
    }

    /// <summary>
    /// 持ち物（<see cref="BrigadeInventory"/>）の中の指定装備を、指定ユニットへ装着する。
    /// ユニットが既に装備していれば旧装備は持ち物へ戻る（個体は消えない＝保存則）。純粋層
    /// <see cref="InventoryService.EquipFromInventory"/> へ委譲し、成功時のみ SoT を差し替える。
    ///
    /// 戻り値:
    ///   - Unit: 装着後のユニット（UI 演出に使う）。
    ///   - null: 未初期化 / 対象ユニット不在 / 指定装備が持ち物に無い（no-op）。
    /// </summary>
    /// <param name="unitId">装備を装着する対象ユニット Id。</param>
    /// <param name="equipmentId">持ち物から装着する装備個体の Id。</param>
    public Unit? EquipFromInventory(Guid unitId, Guid equipmentId)
    {
        Unit? affected;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            var result = InventoryService.EquipFromInventory(
                BattalionRoster, BrigadeInventory, unitId, equipmentId);
            if (result is null) return null;

            BattalionRoster  = result.NewRoster;
            BrigadeInventory = result.NewInventory;
            affected = result.AffectedUnit;
        }

        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalInventoryChanged);

        return affected;
    }

    /// <summary>
    /// 指定ユニットの現装備を取り外し、持ち物（<see cref="BrigadeInventory"/>）へ戻す
    /// （外しても消えない＝保存則）。純粋層 <see cref="InventoryService.UnequipToInventory"/> へ
    /// 委譲し、実際に外れた場合（Mutated=true）のみ SoT を差し替えてシグナルを発火する。
    ///
    /// 戻り値:
    ///   - Unit: 取り外し後（または元から未装備）のユニット。
    ///   - null: 未初期化 / 対象ユニット不在（no-op）。
    /// </summary>
    /// <param name="unitId">装備を取り外す対象ユニット Id。</param>
    public Unit? UnequipToInventory(Guid unitId)
    {
        Unit? affected;
        bool mutated;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;

            var result = InventoryService.UnequipToInventory(
                BattalionRoster, BrigadeInventory, unitId);
            if (result is null) return null;

            mutated = result.Mutated;
            if (mutated)
            {
                BattalionRoster  = result.NewRoster;
                BrigadeInventory = result.NewInventory;
            }
            affected = result.AffectedUnit;
        }

        if (mutated)
        {
            SafeEmit(SignalRosterChanged);
            SafeEmit(SignalInventoryChanged);
        }

        return affected;
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
    /// 今年の行動（出撃 March / 休息 Rest）を選択する。編成フェーズで「次へ」を押す前に
    /// 確定させる意思表示で、<see cref="AdvancePhase"/> の編成離脱分岐（ActionPhaseRouter）の
    /// 入力になる。同値なら何もしない。変更時は FormationChanged を発火し、UI のボタン表示
    /// （出撃／休息）と整合させる。未初期化時は何もしない。
    /// </summary>
    public void SetPlannedAction(PlannedAction action)
    {
        bool changed = false;
        lock (_stateLock)
        {
            if (!IsInitialized) return;

            if (CurrentAction != action)
            {
                CurrentAction = action;
                changed = true;
            }
        }

        if (changed)
        {
            SafeEmit(SignalFormationChanged);
        }
    }

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
        bool battleConcluded = false;

        lock (_stateLock)
        {
            if (!IsInitialized) return CurrentPhase;

            var from = CurrentPhase;

            // ★ 行動分岐: 拠点（Guild）からの離脱は選択行動で行き先が変わる（純粋ルータ ActionPhaseRouter）。
            //   March → 編成（その後 戦闘）/ Rest → 年代記（編成・戦闘の両フェーズを完全バイパス）。
            //   それ以外は通常の循環。決定を編成より上流（拠点）へ置くことで、休息時は編成画面・
            //   戦闘画面が一度も描かれない（Visible が false のまま）。
            next = from == GamePhase.Guild
                ? ActionPhaseRouter.PhaseAfterGuild(CurrentAction)
                : GamePhaseFlow.Next(from);

            // 無人出撃の絶対封鎖: 編成 → 戦闘 の前進は、盤面に最低 1 名が配置されている時のみ許す。
            // 編成へ入るのは March のみ（Rest は Guild→Chronicle ゆえここに到達しない）＝出撃時のみ発火。
            if (from == GamePhase.Formation && next == GamePhase.Battle
                && !DeploymentGate.CanMarch(CurrentFormation))
            {
                return CurrentPhase;
            }

            // 戦闘スキップの絶対封鎖: 戦闘が進行中（未決着）のあいだは 戦闘 → 年代記 へ前進しない。
            // 「次へ」でフェーズごと飛ばして戦闘を無効化する事故を構造的に断つ（純粋ガード BattleProgressGate）。
            if (from == GamePhase.Battle && next == GamePhase.Chronicle
                && !BattleProgressGate.CanLeaveBattlePhase(CurrentBattle))
            {
                return CurrentPhase;
            }

            // ループ 1 周の幕引き（年代記へ復帰）で世代交代（年送り）を行う。
            //   - Battle → Chronicle: 出撃の幕引き（戦果還流あり）。
            //   - Guild  → Chronicle: 休息（編成・戦闘を回避した安全な年。戦果還流なし）。
            if (next == GamePhase.Chronicle
                && (from == GamePhase.Battle || from == GamePhase.Guild))
            {
                formationChanged = AdvanceGenerationLocked();
                generationAdvanced = true;
                battleConcluded = from == GamePhase.Battle; // 戦果還流は出撃の幕引き時のみ
            }

            // 年代記へ復帰したら選択行動を既定（戦闘）へ戻す（次の周回は明示選択があるまで March）。
            if (next == GamePhase.Chronicle)
            {
                CurrentAction = PlannedAction.March;
            }

            CurrentPhase = next;
        }

        // ★ 資源ループの閉鎖（戦闘 → 経済の入口）:
        //   Battle → Chronicle の幕引きでのみ、直近戦闘の戦果（LastBattleSpoils）から
        //   婚姻ポイントを純粋算出して経済へ非破壊加算する。ApplyBattleSpoils は内部で
        //   独自にロック → EarnDirect → EconomyChanged を流す単方向 API。勝利以外・戦果なしは
        //   獲得 0 で安全に据え置かれ、画面切り替え（PhaseChanged）より前に経済が決算される。
        //   戦果と世代交代は 1 : 1 対応のため二重加算は起こらない（LastBattleSpoils は
        //   次戦闘の FinalizeBattleSpoils まで上書きされない）。
        if (battleConcluded)
        {
            ApplyBattleSpoils(LastBattleSpoils);
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

        // 出撃経路で EquipmentDrop 報酬が残っていた場合の 3 択ドロップ overlay を立ち上げる。
        if (!PendingDropCandidates.IsDefaultOrEmpty) SafeEmit(SignalDropChoicePending);

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
        // 出撃経路で未消費の予言報酬を、年送り（戦闘後）にここで確定する。休息経路では ExecuteRest が
        // 既に消費し _pendingProphecy=null のため no-op。Battle 予言は RestService 側で報酬 0（戦果で
        // 報いる）ゆえ通常の戦闘年も実質 no-op。発火するのは「章ボス年で非戦闘予言を選び強制出撃に
        // なった」周など、出撃したのにカード報酬が残っているケース（「戦闘後にも加算」を満たす）。
        // 加齢より前に適用するため、加入した新人は休息経路と同様にこの周の年送りで加齢される（整合）。
        if (_pendingProphecy is { } pendingReward)
        {
            var rewardRes = RestService.Resolve(BattalionRoster, CurrentEconomy, pendingReward, _rng);
            CurrentEconomy  = rewardRes.NextEconomy;
            BattalionRoster = rewardRes.NextRoster;
            // 章ボス年で EquipmentDrop 予言を選び強制出撃になった等、出撃後に残ったドロップ報酬。
            // 3 択候補を選択待ちへ（年代記復帰後に overlay が立ち上がる。呼び出し側がシグナル発火）。
            if (!rewardRes.Outcome.DropCandidates.IsDefaultOrEmpty)
            {
                PendingDropCandidates = rewardRes.Outcome.DropCandidates;
            }
        }

        // 予言が告げた飛ばし年数。ただし章ボス年（25/50/75/100）を踏み越さないよう、現在の暦年から
        // 見て次のボス年でクランプ（スナップ）する。これにより加齢・収入・暦の前進が同一年数で完全に
        // 整合し（「○年経過」が暦にも効く）、かつボス戦を取りこぼさない（接近周はボス年へちょうど着地し、
        // 次の周回でその年の戦闘がボス戦になる）。
        var currentYear = CurrentTimeline?.Turn ?? 0;
        var years = ChronicleTimelineConfig.ClampSkipToNextBossYear(
            currentYear, _pendingGenerationSkipYears);

        // 1. 加齢 → 完全ロストの仕分け（純粋層 RosterLifecycle に委譲）。
        //    現役のみを次世代ロスタへ持ち越す（離脱者は不可逆に外れる）。
        var advance = RosterLifecycle.AdvanceGeneration(BattalionRoster, years);

        // 1.5 年代記ナレーション: この世代の損失（引退 / 戦死）と成長（昇級）を不変イベントへ
        //     写し取り旅団史へ追記する。⚠️ 損失ユニットはこの直後に BattalionRoster から
        //     不可逆に外れるため、名前キー等を「外れる前」のこのタイミングで確定捕捉する。
        RecordGenerationChronicleLocked(advance);

        // 1.6 英霊アーカイブ: この世代で旅団を去る者（自然死・寿命）の静止画を、外れる直前の
        //     このタイミングで保管庫へ写し取る。これにより家系図は、現役から消えた祖先も
        //     Parentage / SpouseId ごと遺影として描き続けられる（縦軸の連続性を保証）。
        ArchiveDepartedLocked(advance.DepartedRoster);

        BattalionRoster = advance.SurvivingRoster.ToImmutableList();

        // 2. ロスタ整合フック: 完全ロストしたユニットの ID を盤面からも掃き出す。
        //    盤面が常にロスタ実在 ID のみを参照する不変条件をここで回復する。
        bool formationChanged = ReconcileFormationWithRosterLocked();

        // 3. 定期収入を加算（SoT #1）。
        CurrentEconomy = CurrentEconomy.EarnFromTimeSkip(years);

        // 4. 暦を years ぶん進めて次世代の予言 3 つを再生成（過去予言は完全破棄）。
        //    暦の前進は加齢・収入と同一の years を用いる（「○年経過」の整合）。
        if (CurrentTimeline is not null)
        {
            CurrentTimeline = CurrentTimeline.AdvanceToNextTurn(
                TimelineEngine.DefaultGenerator, _rng, years);
        }

        // 5. 保留年数・保留予言をリセット（次の Chronicle 選択で改めて設定される）。
        //    予言報酬は休息決算（ExecuteRest）で既に消費済みのため、ここでは取りこぼし防止の
        //    防御的クリア（出撃＝Battle 予言の経路では報酬がないため、そのまま破棄してよい）。
        _pendingGenerationSkipYears = 0;
        _pendingProphecy = null;

        // 盤面に掃き出しが起きたかを呼び出し側へ返す（FormationChanged 発火の判断材料）。
        return formationChanged;
    }

    /// <summary>
    /// この世代の世代交代で起きた損失（引退 / 戦死）と成長（昇級）を、純粋層 <see cref="ChronicleLog"/>
    /// で不変イベント列へ写し取り、旅団史 <see cref="_chronicleLog"/> へ追記する。
    /// <see cref="AdvanceGenerationLocked"/> の内部（<see cref="_stateLock"/> 保持中）からのみ呼ばれる。
    ///
    /// ★ 呼び出しタイミングの規律:
    ///   損失ユニットはこの直後に <see cref="BattalionRoster"/> から不可逆に外れるため、
    ///   <paramref name="advance"/>.DepartedRoster（外れる前の静止画）から名前キー・ジョブ・年齢を
    ///   その場で確定捕捉する。これにより UI はロスタ実在に依存せず過去の喪失も描ける。
    ///
    /// ★ 世代見出しと成長の出所:
    ///   世代番号はタイムライン前進前の現ターン（<see cref="CurrentTimeline"/>.Turn）を用いる。
    ///   成長（昇級）は直近の戦闘で確定済みの <see cref="LastBattleSpoils"/>（統合台帳）から拾う。
    ///   世代交代はちょうど 1 戦闘の決着・決算確定の直後に 1 度だけ走るため、同じ戦果が二重に
    ///   記録されることはない（戦果と世代交代は 1 : 1 に対応する）。
    ///
    /// ★ 昇級者の名前解決:
    ///   <see cref="LastBattleSpoils"/> は突合キー(UnitId)と前後レベルしか持たないため、生存者と
    ///   離脱者を併せた Id→Unit の辞書を組んで名前・ジョブを引き当てる（戦闘で育った後に斃れた
    ///   者の名も解決できるよう両者を含める）。
    /// </summary>
    /// <param name="advance">この世代の加齢・仕分け結果（離脱者と生存者を内包）。</param>
    private void RecordGenerationChronicleLocked(GenerationAdvanceResult advance)
    {
        // 世代見出し: タイムライン前進前の現ターン（無ければ防御的に 0）。
        var generation = CurrentTimeline?.Turn ?? 0;

        // 昇級者の名前解決用に、生存者＋離脱者を Id で引ける辞書を組む。
        var lookupBuilder = ImmutableDictionary.CreateBuilder<Guid, Unit>();
        foreach (var unit in advance.SurvivingRoster) lookupBuilder[unit.Id] = unit;
        foreach (var unit in advance.DepartedRoster)  lookupBuilder[unit.Id] = unit;

        var entries = ChronicleLog.BuildGenerationEntries(
            generation,
            advance.DepartedRoster,
            LastBattleSpoils,
            lookupBuilder.ToImmutable());

        if (!entries.IsEmpty)
        {
            _chronicleLog = _chronicleLog.AddRange(entries);
        }
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

            // ★ 戦闘以外の行動では敵・戦闘インスタンスを構造的に生成しない（完全隔離・永久封鎖）。
            //   CurrentBattle を起こす唯一の窓口が本メソッドであるため、ここで出撃(March)以外を
            //   no-op で弾けば、休息など戦闘以外の行動では裏に敵データが 1 ビットも作られない。
            //   フェーズ遷移（拠点→年代記の休息直行）でも構造的に Battle へ入らないが、万一どの
            //   呼び出し元が誤って到達しても、ここが最終結界となる。
            if (!ActionPhaseRouter.MayGenerateEnemy(CurrentAction)) return null;

            // この戦闘専用の独立した乱数ストリームを（再）シードする。
            _battleRng = battleSeed is { } seed ? new Random(seed) : new Random(_rng.Next());

            snapshot = BattleResolver.CreateInitial(CurrentFormation, BattalionRoster, enemy, _battleRng);
            CurrentBattle = snapshot;

            // 戦果決算の基準点として、開戦の瞬間の参加者静止画を別途捕捉しておく
            // （CurrentBattle は以後のターンで毎回差し替わるため、開戦時の値はここで確保）。
            _battleOpeningCombatants = snapshot.Combatants;
        }

        SafeEmit(SignalBattleChanged);

        // 実走ログ（一過性ダンプ・SoT 不変・リーク無し）: 年・出現原型・敵素ステータスを
        // 機械可読な ASCII 構造化ログとして標準出力へ。整形は純粋層 MetricsLogFormatter に委譲。
        GD.Print(MetricsLogFormatter.FormatBattleStart(
            CurrentTimeline?.Turn ?? 0, enemy.Archetype, enemy.MaxHp, enemy.Attack, enemy.Speed));

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

            // ★ 戦果決算（統合台帳）はここでは確定しない。決算は「ターン制戦闘の成長」と
            //   「とどめ（ResolveLastHit）の成長/喪失」を 1 枚に合算する統合台帳であり、
            //   とどめは EndBattle の後に解決されるため、決算の確定は FinalizeBattleSpoils
            //   へ遅延させる。ここでは決着状態だけを凍結して退避し、開戦時の基準点
            //   （_battleOpeningCombatants）はとどめ完了まで生かす。
            _lastBattleOutcome = outcome;

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

    /// <summary>
    /// 戦果決算（統合台帳）を確定する。開戦時の参加者静止画
    /// （<see cref="_battleOpeningCombatants"/>）と「現在の正本ロスタ」を Guid 突合し、
    /// この 1 戦闘で起きた全変化を 1 枚へ集約して <see cref="LastBattleSpoils"/> へ確定する。
    ///
    /// ★ 呼ぶタイミング（統合台帳の要）:
    ///   <see cref="EndBattle"/>（ターン制戦闘の成長/戦死をロスタへ書き戻し）→
    ///   <see cref="ResolveLastHit"/>（とどめの昇級/装備進化/Lv5 破壊/強奪をロスタへ反映）
    ///   の後に呼ぶ。こうすると現在のロスタには「戦闘中の成長」も「とどめの成長/喪失」も
    ///   両方が刻まれており、開戦時との差分が両者を合算した 1 枚の台帳になる。
    ///
    /// 副作用と冪等性:
    ///   - 確定後、開戦時基準点を解放する（次戦闘の <see cref="StartBattle"/> が再捕捉）。
    ///   - 基準点が既に空（未戦闘・確定済みの二重呼び出し）なら現在値を保ったまま no-op で
    ///     返す（ヌル安全フォールバック・画面が落ちない方針）。
    ///
    /// 戻り値:
    ///   - 確定した戦果決算（呼び出し元の無状態 UI がそのまま提示に使える）。
    ///   - 未初期化・基準点なしの場合は現在の <see cref="LastBattleSpoils"/> をそのまま返す。
    /// </summary>
    public BattleSpoils FinalizeBattleSpoils()
    {
        lock (_stateLock)
        {
            if (!IsInitialized) return LastBattleSpoils;

            // 基準点が無い（未戦闘 or 既に確定済み）なら現在値を維持して no-op。
            if (_battleOpeningCombatants.IsEmpty) return LastBattleSpoils;

            LastBattleSpoils = BattleSpoils.FromBattle(
                _battleOpeningCombatants,
                BattalionRoster.ToImmutableDictionary(unit => unit.Id),
                _lastBattleOutcome);

            // 統合台帳を確定したので開戦時基準点を解放（次戦闘の StartBattle が再捕捉する）。
            _battleOpeningCombatants = ImmutableDictionary<Guid, Unit>.Empty;

            return LastBattleSpoils;
        }
    }

    /// <summary>
    /// 戦果決算（<see cref="BattleSpoils"/>）から獲得婚姻ポイントを純粋算出し、その分を
    /// 経済 SoT（<see cref="CurrentEconomy"/>）へ非破壊的に合算して最新総点を確定する
    /// 単方向の決算 API。戦闘 → 経済の資源ループを 1 本の環へ閉じる入口。
    ///
    /// 規律:
    ///   - 算出は <see cref="BattleSpoils.CalculateEarnedMarriagePoints"/> に委ね、本メソッドは
    ///     「ロック内で <see cref="PointsEconomy.EarnDirect"/> による不変加算」だけを担う
    ///     （<see cref="ResolveLastHit"/> の CoinGreed 強奪加算と同じ単方向の作法）。
    ///   - 加算が起きたときだけロック解放後に <see cref="SignalEconomyChanged"/> を流す。
    ///   - ヌル / 戦果なし / 勝利以外（獲得 0）/ 未初期化はすべて安全に no-op で素通りし、
    ///     例外を投げない（番兵ガード・画面が落ちない方針）。獲得は常に 0 以上で底打ち
    ///     済みのため、経済のアンダーフロー・不正な負加算は構造的に起こり得ない。
    /// </summary>
    /// <param name="spoils">決算対象の戦果（null は安全にスキップ）。</param>
    /// <returns>実際に経済へ加算された婚姻ポイント（加算が起きなければ 0）。</returns>
    public int ApplyBattleSpoils(BattleSpoils? spoils)
    {
        if (spoils is null) return 0;

        // 純粋算出（副作用ゼロ・常に 0 以上）。内訳ごと弾いておき、合計を獲得量として使う
        // （breakdown.Total == CalculateEarnedMarriagePoints()。実走ログにも内訳を流す）。
        var breakdown = spoils.DescribeMarriagePoints();
        var earned = breakdown.Total;

        bool applied = false;
        if (earned > 0)
        {
            lock (_stateLock)
            {
                if (IsInitialized)
                {
                    CurrentEconomy = CurrentEconomy.EarnDirect(earned);
                    applied = true;
                }
            }
        }

        // 実走ログ（一過性ダンプ・SoT 不変・リーク無し）: 年・内訳・適用後の総残高を機械可読な
        // ASCII 構造化ログとして標準出力へ。勝敗を問わず決算 1 件を 1 行に映す（敗北＝獲得 0 も計上）。
        GD.Print(MetricsLogFormatter.FormatSettlement(
            CurrentTimeline?.Turn ?? 0, breakdown, CurrentEconomy.CurrentBalance));

        if (applied) SafeEmit(SignalEconomyChanged);
        return applied ? earned : 0;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  休息（戦闘回避）の決算
    // ════════════════════════════════════════════════════════════════════════
    //  編成で「休息」を選んで前進したとき、戦闘フェーズを完全にバイパスする代わりに
    //  ここで「大隊を立て直す」決算を 1 度だけ確定する。ExecuteScout / ApplyBattleSpoils と
    //  同じく、純粋層（RestService）が算術を担い、本メソッドは「状態の差し替えとシグナル
    //  発火」だけに専念する（責務分離）。年送り（加齢・年次収入・予言再生成）は本メソッドでは
    //  行わない——休息報酬通知 UI の「確認」後に GameDirector が AdvancePhase を駆動して初めて
    //  起きる（提示が消滅副作用に先行する。BattleSpoilsScreen と同じ案A）。

    /// <summary>
    /// 「休息」年の決算を確定する。純粋層 <see cref="RestService.Resolve"/> で次経済と
    /// 成果サマリ（休息頭数・報酬ポイント・適用後残高）を算出し、経済 SoT を原子的に差し替え、
    /// <see cref="LastRestOutcome"/> を公開する。ロック解放後に EconomyChanged を発火する。
    /// 未初期化なら状態を変えず <see cref="RestOutcome.None"/> を返す（no-op 契約）。
    /// </summary>
    /// <returns>確定した休息成果サマリ（休息報酬通知 UI がこれを読むだけ）。</returns>
    public RestOutcome ExecuteRest()
    {
        RestOutcome outcome;
        bool rosterChanged;
        bool dropPending;
        lock (_stateLock)
        {
            if (!IsInitialized) return RestOutcome.None;

            // 保留中の予言（この年に選んだカード）の報酬を、休息決算と同時に確定する。
            //   RewardPoints → ポイント加算 / ScoutReward → 新人加入 / EquipmentDrop → 3 択ドロップ候補 /
            //   Rest（または予言なし）→ 固定の休息ボーナス。純粋層 RestService が算術・生成を担う。
            var resolution = RestService.Resolve(
                BattalionRoster, CurrentEconomy, _pendingProphecy, _rng);

            CurrentEconomy  = resolution.NextEconomy;
            rosterChanged   = !ReferenceEquals(resolution.NextRoster, BattalionRoster);
            BattalionRoster = resolution.NextRoster;
            LastRestOutcome = resolution.Outcome;
            outcome = resolution.Outcome;

            // EquipmentDrop の 3 択候補を選択待ちへ。プレイヤーが ChooseDroppedEquipment で 1 つ選ぶ。
            PendingDropCandidates = resolution.Outcome.DropCandidates;
            dropPending = !PendingDropCandidates.IsDefaultOrEmpty;

            // 予言報酬はここで消費済み。年送り（AdvanceGenerationLocked）で二重適用しないようクリア。
            _pendingProphecy = null;
        }

        SafeEmit(SignalEconomyChanged);
        if (rosterChanged) SafeEmit(SignalRosterChanged); // 新人加入で名簿が動いた時
        if (dropPending) SafeEmit(SignalDropChoicePending); // 3 択ドロップ overlay を立ち上げる
        return outcome;
    }

    /// <summary>
    /// 選択待ちの 3 択ドロップ（<see cref="PendingDropCandidates"/>）から 1 つを選び、持ち物
    /// （<see cref="BrigadeInventory"/>）へ加える。残りの候補は破棄され、選択待ちは空へ戻る。
    ///
    /// 戻り値:
    ///   - Equipment: 取得して持ち物へ入った装備。
    ///   - null: 未初期化 / 指定 Id が候補に無い（no-op）。
    /// </summary>
    /// <param name="equipmentId">取得する候補装備の Id。</param>
    public Equipment? ChooseDroppedEquipment(Guid equipmentId)
    {
        Equipment? chosen;

        lock (_stateLock)
        {
            if (!IsInitialized) return null;
            if (PendingDropCandidates.IsDefaultOrEmpty) return null;

            chosen = null;
            foreach (var candidate in PendingDropCandidates)
            {
                if (candidate.Id == equipmentId) { chosen = candidate; break; }
            }
            if (chosen is null) return null; // 候補に無い Id は no-op。

            BrigadeInventory = BrigadeInventory.Add(chosen);
            PendingDropCandidates = ImmutableArray<Equipment>.Empty; // 残りは破棄・選択完了。
        }

        SafeEmit(SignalInventoryChanged);
        return chosen;
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
    //  ガードだけを担う。各 UI（TimelineUI / FormationUI / MarriageUI / BattleUI）は
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

    /// <summary>
    /// 章（Epoch）の表示用日本語名を localization 経由で解決する。Autoload 未初期化や
    /// 未登録キーのときは生キー（"epoch-*"）へフォールバックして画面を落とさない。
    /// </summary>
    public string ResolveEpochName(string epochNameKey)
        => _masterDataNameResolver?.ResolveEpochName(epochNameKey) ?? epochNameKey;

    /// <summary>
    /// Affix（装備の付加効果）の表示用日本語名を localization 経由で解決する。Autoload
    /// 未初期化や未登録キーのときは生キー（"affix-*"）へフォールバックして画面を落とさない。
    /// </summary>
    public string ResolveAffixName(string affixKey)
        => _masterDataNameResolver?.ResolveAffixName(affixKey) ?? affixKey;

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
    /// 現在の暦年（<see cref="CurrentTimeline"/>.Turn）が章ボス出現年（25/50/75/100）か。
    /// 未初期化・タイムライン null のときは false。UI が「この年は出撃必至（章ボス）」表示に使う。
    /// </summary>
    public bool IsCurrentYearEpochBossYear()
        => CurrentTimeline is not null
           && ChronicleTimelineConfig.IsEpochBossYear(CurrentTimeline.Turn);

    /// <summary>
    /// 生存中の旅団員のみを返す純粋クエリ。
    /// </summary>
    public IReadOnlyList<Unit> GetAliveUnits()
        => BattalionRoster.Where(u => u.IsAlive).ToList();

    /// <summary>
    /// 現在戦闘中の敵が、この先 <paramref name="lookaheadTurns"/> ターンに放つ予定の攻撃予告
    /// （運命の帯）を、現在の戦況スナップショットから決定論的に先読みして返す読み取り専用クエリ。
    /// 非戦闘時（<see cref="CurrentBattle"/> == null）は空配列を返す。SoT は 1 ミリも変更しない。
    ///
    /// ★ 決定論シードの導出（SoT 不可侵・カプセル化の堅持）:
    ///   実戦の乱数器 <c>_battleRng</c> の内部状態は外部へ晒さない。代わりに、現在スナップショットの
    ///   不変な観測量（敵攻撃力・敏捷・最大 HP・現ターン番号）から大きな素数で決定論的にシードを
    ///   織り、純粋関数 <see cref="AttackIntentRoller.Forecast"/> へ渡す。同じ局面からは常に同じ帯が
    ///   返り、ターンが進めば（TurnNumber が変われば）帯も前進する。
    ///
    /// ★ 「確定した予言」ではなく「決定論的な予測投影」:
    ///   これは未来を 1 ビットの狂いなく当てる予言ではなく、現局面から再現可能に投影した予測である。
    ///   遠い未来ほど実戦（ローテーション・とどめの乱数前進）とのズレが増す不確実性は、UI 層が
    ///   confidence-fade（遠いカードほど褪色）として正直に表現する。
    /// </summary>
    /// <param name="lookaheadTurns">何ターン先まで読むか（0 以下は空・上限は Forecast 側でクランプ）。</param>
    /// <returns>T+1 から順に並ぶ攻撃予告の帯（非戦闘時は空）。</returns>
    public IReadOnlyList<AttackIntent> ForecastEnemyIntents(int lookaheadTurns)
    {
        var battle = CurrentBattle;
        if (battle is null)
        {
            return Array.Empty<AttackIntent>();
        }

        var seed = DeriveForecastSeed(battle);
        return AttackIntentRoller.Forecast(battle.Enemy, seed, lookaheadTurns);
    }

    /// <summary>
    /// <see cref="ForecastEnemyIntents"/> に「現在年の章ボス接近前兆」を重ねた運命の帯を返す
    /// 読み取り専用クエリ。各ターンの通常脅威は <see cref="ForecastEnemyIntents"/> と 1 ビットの
    /// 狂いもなく一致し、章ボス出現年が近づくと該当ターンへ前兆（<see cref="EpochBossOmen"/>）が
    /// 重なる。非戦闘時（<see cref="CurrentBattle"/> == null）は空配列。SoT は 1 ミリも変更しない。
    ///
    /// ★ 現在年コンテキストの導出（単一 SoT）:
    ///   現在年は予言タイムラインの現ターン（<see cref="CurrentTimeline"/>.Turn）を唯一の真実とし、
    ///   純粋関数 <see cref="ChronicleTimelineConfig.BuildOmenScheduleForYear"/> が次の章ボスまでの
    ///   残り年から前兆スケジュールを決定論的に組む。先読みシードは通常 Forecast と同一の導出
    ///   （<see cref="DeriveForecastSeed"/>）を用いるため、脅威列は完全に一致する。
    /// </summary>
    /// <param name="lookaheadTurns">何ターン先まで読むか（0 以下は空・上限は Forecast 側でクランプ）。</param>
    /// <returns>通常脅威に章ボス前兆を重ねた帯（非戦闘時は空）。</returns>
    public IReadOnlyList<ForecastEntry> ForecastEnemyIntentsWithOmens(int lookaheadTurns)
    {
        var battle = CurrentBattle;
        if (battle is null)
        {
            return Array.Empty<ForecastEntry>();
        }

        var seed = DeriveForecastSeed(battle);
        var currentYear = CurrentTimeline?.Turn ?? 0;
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(currentYear);
        return AttackIntentRoller.ForecastWithOmens(battle.Enemy, seed, lookaheadTurns, schedule);
    }

    /// <summary>
    /// 現在年（<see cref="CurrentTimeline"/>.Turn）の章の難易度・環境補正を畳んだ「時代基準」へ
    /// 実戦の個体差ジッタ（±15%）を乗せ、その時代に立つ敵 1 体を合成する。100 年史の難易度曲線
    /// （<see cref="EnemyScalingResolver"/>）を実戦の敵生成へ架橋する入口。SoT は変更しない（生成のみ）。
    ///
    /// <paramref name="seed"/> を与えれば決定論的に（同一年・同一シードから同一個体）、省略すれば
    /// 既定乱数で個体差を振って生成する（<see cref="StartBattle"/> の battleSeed と同じ任意シード方針）。
    /// </summary>
    /// <param name="archetype">生成する敵の原型（章ボス/通常敵）。</param>
    /// <param name="seed">個体差ジッタ用の任意シード（省略時は非決定的な既定乱数）。</param>
    /// <returns>現在年で時代スケール＋個体差合成された満タンの敵。</returns>
    public EnemyState CreateEraScaledEnemy(EnemyArchetype archetype, int? seed = null)
    {
        var currentYear = CurrentTimeline?.Turn ?? 0;
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        return EnemyScalingResolver.ComposeBattleEnemy(currentYear, archetype, rng);
    }

    /// <summary>
    /// 「今年は誰と戦うか」を暦から決め、現在年の時代スケール＋個体差で敵 1 体を生成する戦闘開始の正本。
    /// 章ボス出現年（25/50/75/100）はその章の章ボス、それ以外は通常敵（試練の門の守護者）を、
    /// <see cref="ChronicleTimelineConfig.BattleArchetypeForYear"/> で決定論的に選び、現在年で時代スケールする。
    /// デモ用の固定敵生成（旧 EnemyScaler.ScaleTrialGuardian 仮置き）を置き換える戦闘開始ファクトリ。
    /// SoT は変更しない（生成のみ）。
    /// </summary>
    /// <param name="seed">個体差ジッタ用の任意シード（省略時は非決定的な既定乱数）。</param>
    /// <returns>現在年に応じた原型で時代スケール＋個体差合成された満タンの敵。</returns>
    public EnemyState CreateCurrentYearEnemy(int? seed = null)
    {
        var currentYear = CurrentTimeline?.Turn ?? 0;
        var archetype = ChronicleTimelineConfig.BattleArchetypeForYear(currentYear);
        return CreateEraScaledEnemy(archetype, seed);
    }

    /// <summary>
    /// 戦況スナップショットの不変観測量から、先読み用の決定論シードを織る純粋写像。大きな素数で
    /// 混ぜて分散させ、敵の個体差（攻撃力・敏捷・最大 HP）とターン経過の双方を反映させる。
    /// </summary>
    private static uint DeriveForecastSeed(BattleSnapshot battle)
    {
        var enemy = battle.Enemy;
        var mixed = unchecked(
            (enemy.Attack * 73856093)
            ^ (enemy.Speed * 19349663)
            ^ (enemy.MaxHp * 83492791)
            ^ (battle.TurnNumber * 49979693));
        return unchecked((uint)mixed);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  セーブ＆ロード（永続化）
    // ════════════════════════════════════════════════════════════════════════
    //  4 つの不変状態 (CurrentEconomy / CurrentTimeline / BattalionRoster /
    //  _chronicleLog) を SaveManager 経由で user://save_data.json に保存・復元する。
    //
    //  ★ Random は保存しない。LoadGame では新しい Random を再注入する
    //    （ユーザー仕様: 「Random インスタンスは保存せず、ロード時に再注入」）。

    /// <summary>
    /// 現在の 4 状態を指定パス（既定 SaveManager.DefaultSavePath）へ保存する。
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
        ImmutableArray<ChronicleLogEntry> chronicleLog;
        ImmutableList<Equipment> inventory;

        lock (_stateLock)
        {
            if (!IsInitialized || CurrentTimeline is null) return false;
            economy      = CurrentEconomy;
            timeline     = CurrentTimeline;
            roster       = BattalionRoster;
            chronicleLog = _chronicleLog;
            inventory    = BrigadeInventory;
        }

        return SaveManager.SaveToFile(targetPath, economy, timeline, roster, chronicleLog, inventory);
    }

    /// <summary>
    /// 指定パスのセーブデータを読み込み、4 状態を一括で復元する。
    ///
    /// 復元後の処理:
    ///   - _rng に新しい Random（または引数 rng）を再注入（★ Random は保存しない設計）
    ///   - _chronicleLog をセーブから復元（旧 v1 セーブは空配列で後方互換）
    ///   - IsInitialized を true に
    ///   - StateInitialized / Economy / Timeline / Roster の全シグナルを発火し
    ///     UI を初期化と同じ経路で完全再描画させる（TimelineChanged で旅団史も再描画）
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
            BrigadeInventory = loaded.Inventory; // ★ 持ち物を永続化セーブから復元（v1〜v4 は空）
            PendingDropCandidates = ImmutableArray<Equipment>.Empty; // 選択待ちは永続化しない
            CurrentPhase    = GamePhaseFlow.InitialPhase; // ロード再開は Chronicle から
            CurrentAction   = PlannedAction.March;         // ロード再開時の既定行動は出撃
            CurrentFormation = FormationBoard.Empty();     // 盤面は永続化せずロードは空盤面から
            CurrentBattle   = null;              // 戦闘は永続化しない（ロード再開は非戦闘状態）
            _battleRng      = new Random();       // 戦闘乱数は次の StartBattle で再シード
            _pendingGenerationSkipYears = 0;     // 保留年数は保存しない（Chronicle 再開で再設定）
            _pendingProphecy = null;             // 保留予言も保存しない
            _chronicleLog   = loaded.ChronicleLog; // ★ 旅団史を永続化セーブから復元（v1 は空配列）
            IsInitialized   = true;
        }

        SafeEmit(SignalStateInitialized);
        SafeEmit(SignalEconomyChanged);
        SafeEmit(SignalTimelineChanged);
        SafeEmit(SignalRosterChanged);
        SafeEmit(SignalInventoryChanged);
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
