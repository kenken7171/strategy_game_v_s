// =============================================================================
//  ChronicleKnights — BattleSnapshot.cs
// -----------------------------------------------------------------------------
//  1 ターンの戦闘状態を写し取る「完全不変の静止画」と、その遷移結果を表す型。
//
//  ★ 設計意図（3 つの純粋コンポーネントの接着面）:
//    戦闘は 3 つの独立した純粋部品で構成される:
//      - FormationBoard … 「どのマスに誰が居るか」だけを持つ薄い参照盤面
//      - Unit           … 旅団員の不変スナップショット（数値ステータスは持たない）
//      - EnemyState     … 敵 1 体の不変スナップショット（自前で HP/ATK/SPD を持つ）
//    BattleSnapshot はこれらを 1 枚の静止画へ束ねる「文脈」であり、
//    BattleResolver がこの静止画を入力に次の静止画を生成する。
//
//  ★ 二重 SoT の回避（設計憲法 ③）と HP の隔離:
//    Unit は「数値ステータスを持たない」設計のため、戦闘中の現在 HP を
//    Unit へ書き込むことはしない。現在 HP は本レコードの
//    UnitHitPoints（ImmutableDictionary&lt;Guid,int&gt;）に完全隔離して管理する。
//    Unit 側の正本（Level / IsDead / 装備）と HP 軸を分離することで、
//    「HP は戦闘文脈のみの一時状態」という意味論を構造で担保する。
//
//  ★ 完全不変（設計憲法 ②）:
//    全フィールドは init 専用。状態遷移は BattleResolver が `with`／新規生成で
//    新しい BattleSnapshot を返し、本レコード自身は一切変更されない。
//
//  本ファイルは Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Battle;

// ─── 戦闘の決着状態 ─────────────────────────────────────────────────────────

/// <summary>
/// 1 戦闘全体の決着状態。BattleResolver が各ターン終了時に再評価する。
/// 表示ラベルは localization 経由で解決する想定で、enum には日本語を含めない（憲法①）。
/// </summary>
public enum BattleOutcome
{
    /// <summary>まだ決着していない（戦闘継続中）。</summary>
    Ongoing,

    /// <summary>敵を撃破した（大隊の勝利）。</summary>
    BattalionVictory,

    /// <summary>大隊の生存者が尽きた（大隊の敗北）。</summary>
    BattalionDefeat,
}

// ─── 戦闘の静止画 ───────────────────────────────────────────────────────────

/// <summary>
/// ある瞬間の戦闘状態を丸ごと写し取った完全不変レコード。
///
/// 盤面・参加ユニット実体・各ユニットの現在 HP・敵・経過ターン・決着状態を
/// 1 枚の静止画として保持する。BattleResolver.ResolveTurn はこの静止画を
/// 入力に取り、一切変更せず次の静止画を生成する純粋関数として振る舞う。
/// </summary>
public sealed record BattleSnapshot
{
    /// <summary>V 字 3×3 盤面（占有者 ID のみを持つ薄い参照レイヤ）。</summary>
    public required FormationBoard Board { get; init; }

    /// <summary>
    /// 戦闘参加ユニットの実体（Id → Unit）。Unit は数値ステータスを持たず、
    /// Level / IsDead / 装備など「HP 以外の正本」を運ぶ。現在 HP は
    /// <see cref="UnitHitPoints"/> 側に隔離する。
    /// </summary>
    public required ImmutableDictionary<Guid, Unit> Combatants { get; init; }

    /// <summary>
    /// 各ユニットの戦闘中現在 HP（Id → 現在 HP）。Unit から完全隔離された
    /// 戦闘文脈専用の一時状態。初期値は各ジョブの最大 HP（JobMaster 由来）。
    /// </summary>
    public required ImmutableDictionary<Guid, int> UnitHitPoints { get; init; }

    /// <summary>敵 1 体の不変スナップショット（HP/ATK/SPD を自前で保持）。</summary>
    public required EnemyState Enemy { get; init; }

    /// <summary>
    /// 次ターンに敵が放つ攻撃の予告（先読み。常に非 null）。この静止画の「未来」を
    /// 1 つだけ内包し、UI はこれを読んで攻撃予告バナーや危険エリアの赤枠脈動を描く。
    /// CreateInitial が初手を、ResolveTurn が各ターン解決後に次手を決定論的にロールして
    /// 封入する。決着済みスナップショットでは直前の予告を据え置く（次ターンは無いため）。
    /// </summary>
    public required AttackIntent NextEnemyIntent { get; init; }

    /// <summary>これまでに解決したターン数（初期 0、ResolveTurn ごとに +1）。</summary>
    public required int TurnNumber { get; init; }

    /// <summary>現在の決着状態。</summary>
    public required BattleOutcome Outcome { get; init; }

    // ─── 派生クエリ（読み取り専用ヘルパー） ──────────────────────────────

    /// <summary>戦闘が決着済みか（勝利または敗北）。</summary>
    public bool IsConcluded => Outcome != BattleOutcome.Ongoing;

    /// <summary>指定ユニットの現在 HP を返す（未登録なら 0）。</summary>
    public int HitPointsOf(Guid unitId)
        => UnitHitPoints.TryGetValue(unitId, out var hp) ? hp : 0;

    /// <summary>指定ユニットの実体を返す（未参加なら null）。</summary>
    public Unit? CombatantOf(Guid unitId)
        => Combatants.TryGetValue(unitId, out var unit) ? unit : null;

    /// <summary>指定ユニットが戦闘続行可能か（実体が生存 かつ 現在 HP &gt; 0）。</summary>
    public bool IsCombatantAlive(Guid unitId)
        => Combatants.TryGetValue(unitId, out var unit) && unit.IsAlive && HitPointsOf(unitId) > 0;
}

// ─── ターン解決の結果 ───────────────────────────────────────────────────────

/// <summary>
/// 1 ターン解決の結果。次の静止画（<see cref="Snapshot"/>）と、その過程で
/// 発生した戦闘イベントの時系列ログ（<see cref="Events"/>）を不変で束ねる。
///
/// UI 層はこの結果を受け取り、Snapshot で盤面・HP・敵カードを再描画し、
/// Events でターンログ（攻撃・被弾・撃破・回復・回転・決着）を追記する。
/// </summary>
public sealed record BattleTurnResult
{
    /// <summary>このターン解決後の新しい静止画。</summary>
    public required BattleSnapshot Snapshot { get; init; }

    /// <summary>このターン中に発生したイベントの時系列ログ（発生順）。</summary>
    public required ImmutableArray<BattleEvent> Events { get; init; }
}
