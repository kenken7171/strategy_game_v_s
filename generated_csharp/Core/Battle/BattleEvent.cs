// =============================================================================
//  ChronicleKnights — BattleEvent.cs
// -----------------------------------------------------------------------------
//  1 ターンの戦闘で発生した出来事を表す不変イベント群（時系列ログの要素）。
//
//  ★ 役割:
//    BattleResolver.ResolveTurn は、解決の過程で起きた出来事をこのイベント群へ
//    発生順に積む。返り値 BattleTurnResult.Events がその時系列ログになる。
//    UI 層はこのログを走査してターンログ（攻撃・被弾・撃破・回復・回転・決着）を
//    人間可読に描画する。イベント自身は数値・ID のみを持ち、日本語テキストは
//    一切含まない（表示は localization 経由・憲法①）。
//
//  ★ 設計:
//    判別共用体（discriminated union）を C# の record 継承で表現する。
//    抽象基底 BattleEvent を 1 つ置き、各出来事を positional sealed record として
//    派生させることで、switch 式や `is` パターンで型安全に分岐・検査できる。
//
//  完全不変（憲法②）。Godot 非依存の純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.Core.Battle;

/// <summary>戦闘イベントの抽象基底。全イベントはこの型の派生 record として表す。</summary>
public abstract record BattleEvent;

/// <summary>
/// 分隊単位ローテーションが行われた（ターン冒頭の作戦適用）。
/// </summary>
/// <param name="Direction">適用された回転方向。</param>
public sealed record RotationPerformedEvent(RotationDirection Direction) : BattleEvent;

/// <summary>
/// 味方ユニットが敵へ攻撃した。
/// </summary>
/// <param name="AttackerId">攻撃した味方ユニットの Id。</param>
/// <param name="Damage">敵へ与えた最終ダメージ（バフ・連続攻撃込み）。</param>
/// <param name="EnemyHpAfter">この攻撃の後に残った敵の HP。</param>
public sealed record AllyOffenseEvent(Guid AttackerId, int Damage, int EnemyHpAfter) : BattleEvent;

/// <summary>
/// 敵が予告（NextEnemyIntent）の対象行へ攻撃した。狙う行は SingleStrike / Pincer /
/// TotalAssault の予告パターンに従って動的に決まり、対象行 1 つにつき 1 イベントを積む。
/// </summary>
/// <param name="TargetRow">攻撃対象となった分隊行。</param>
/// <param name="DamagePerUnit">防御軽減後・1 ユニットあたりの被ダメージ。</param>
public sealed record EnemyOffenseEvent(SquadRow TargetRow, int DamagePerUnit) : BattleEvent;

/// <summary>
/// 味方ユニットが被弾し、生き残った（HP が減ったが 0 ではない）。
/// </summary>
/// <param name="UnitId">被弾した味方ユニットの Id。</param>
/// <param name="Damage">実際に受けた軽減後ダメージ。</param>
/// <param name="HpAfter">被弾後に残った現在 HP（&gt; 0）。</param>
public sealed record UnitDamagedEvent(Guid UnitId, int Damage, int HpAfter) : BattleEvent;

/// <summary>
/// 味方ユニットが撃破された（HP が 0 に到達し、完全ロスト）。
/// </summary>
/// <param name="UnitId">撃破された味方ユニットの Id。</param>
public sealed record UnitDefeatedEvent(Guid UnitId) : BattleEvent;

/// <summary>
/// ターン終了時の分隊回復で味方ユニットの HP が回復した。
/// </summary>
/// <param name="UnitId">回復した味方ユニットの Id。</param>
/// <param name="HealAmount">実際に回復した量（最大 HP クランプ後の差分）。</param>
/// <param name="HpAfter">回復後の現在 HP。</param>
public sealed record UnitHealedEvent(Guid UnitId, int HealAmount, int HpAfter) : BattleEvent;

/// <summary>
/// 味方が敵にとどめを刺し、ラストヒット成長・装備強化／破壊が解決された。
/// </summary>
/// <param name="UnitId">とどめを刺した味方ユニットの Id。</param>
/// <param name="LevelOverflow">既に Lv3 で成長が余剰になった場合 true。</param>
/// <param name="ItemLevelUpTriggered">装備レベルが +1 された場合 true。</param>
/// <param name="ItemDestroyed">装備が Lv5 破壊判定で消滅した場合 true。</param>
/// <param name="GreedPointsStolen">強奪したポイント量（既定 0）。</param>
public sealed record LastHitResolvedEvent(
    Guid UnitId,
    bool LevelOverflow,
    bool ItemLevelUpTriggered,
    bool ItemDestroyed,
    int GreedPointsStolen) : BattleEvent;

/// <summary>
/// 戦闘がこのターンで決着した（勝利または敗北）。継続ターンでは発生しない。
/// </summary>
/// <param name="Outcome">確定した決着状態。</param>
public sealed record BattleConcludedEvent(BattleOutcome Outcome) : BattleEvent;
