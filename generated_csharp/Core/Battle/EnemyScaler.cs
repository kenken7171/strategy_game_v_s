// =============================================================================
//  ChronicleKnights — EnemyScaler.cs
// -----------------------------------------------------------------------------
//  実戦の敵 1 体に乗せる「個体差ジッタ（±15%）」と「HP 集約倍率」を司る純粋プリミティブ。
//
//  ★ 役割（現行・2026-06-22 整理）:
//    その年・その章の敵基準ステは Core/Chronicle/EnemyScalingResolver.cs が
//    （テンプレ × 年成長 × 章難易度 × 環境補正で）算出する。本クラスはそこへ
//    最後に乗せる個体差（ApplyJitter）と HP 集約倍率（HpAggregationFactor）の SoT だけを担う。
//    ＝ EnemyScalingResolver.ComposeBattleEnemy が本クラスの ApplyJitter / HpAggregationFactor を再利用する
//      （個体差の二重定義を避ける Chronicle → Battle の一方向結合）。
//
//    かつての固定敵生成 ScaleTrialGuardian（旧 TS makeTrialEnemy 直移植・年×レベルで素から合成）と
//    その基準/成長定数（BaseHp 等）は EnemyScalingResolver へ統合済みのため撤去した（死蔵の整理）。
//
//  ⚠ 決定論の絶対保証（設計憲法・要件②）:
//    揺らぎの乱数は **必ず引数で注入された System.Random** から取り出す（Random.Shared 不可）。
//    同一の (baseValue, シード) からは、どの環境・xUnit 上でも 100% 同一の結果になる。
//
//  本ファイルは Godot に 1 ミリも依存しない純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System;

namespace ChronicleKnights.Core.Battle;

/// <summary>
/// 敵基準ステへ個体差（±15%）と HP 集約を乗せる純粋プリミティブ（静的）。
/// 基準ステ自体の算出は <see cref="ChronicleKnights.Core.Chronicle.EnemyScalingResolver"/> が担う。
/// </summary>
public static class EnemyScaler
{
    // ─── 個体差・集約の SoT 定数 ──────────────────────────────────────────

    /// <summary>
    /// HP 集約倍率。旧 10 体集団の合算 HP を単体敵へ集約する係数（レガシー由来）。
    /// 実プレイで章ボスの HP が厚すぎ「削り切れない」（旅団長フィードバック 2026-06-21）ため、
    /// 全敵の HP 壁を一律に下げる単一レバーとして 10 → 6 へ緩和（ボス>通常の序列は係数共通ゆえ維持）。
    /// </summary>
    public const int HpAggregationFactor = 6;

    /// <summary>個体差（±15%）の下限係数。jitter = JitterFloor + rng * JitterSpan。</summary>
    public const double JitterFloor = 0.85;

    /// <summary>個体差（±15%）の振れ幅。0.85〜1.15 の一様乱数になる。</summary>
    public const double JitterSpan = 0.30;

    /// <summary>各ステータスの下限（揺らぎ・丸めで 0 以下に落ちないよう保証）。</summary>
    public const int MinimumStatValue = 1;

    // ─── 個体差プリミティブ ───────────────────────────────────────────────

    /// <summary>
    /// 基準値へ ±15% の個体差（jitter）を 1 回掛けて整数化し、下限でクランプする純粋プリミティブ。
    /// rng.NextDouble() をちょうど 1 回消費する（呼び出し順が決定論を決める核心）。丸めは JS の
    /// Math.round（正の .5 は切り上げ）に合わせ AwayFromZero を用いる。
    ///
    /// ★ 時代基準（EnemyScalingResolver.ResolveEnemyStats）へ実戦の個体差を乗せる合成で本プリミティブを
    ///   再利用する（個体差の SoT を二重定義しないため・Chronicle → Battle の一方向）。
    /// </summary>
    /// <param name="baseValue">個体差を乗せる前の基準値（時代基準ステータス等）。</param>
    /// <param name="rng">外部注入の乱数発生器（null 不可）。</param>
    /// <exception cref="ArgumentNullException">rng が null の場合。</exception>
    public static int ApplyJitter(double baseValue, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var jitter = JitterFloor + rng.NextDouble() * JitterSpan;
        var rounded = (int)Math.Round(baseValue * jitter, MidpointRounding.AwayFromZero);
        return Math.Max(MinimumStatValue, rounded);
    }
}
