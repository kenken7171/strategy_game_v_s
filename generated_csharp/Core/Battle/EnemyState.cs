// =============================================================================
//  ChronicleKnights — EnemyState.cs
// -----------------------------------------------------------------------------
//  戦闘の標的となる「敵」1 体の完全不変スナップショット。
//
//  ★ 味方 Unit との設計対比:
//    味方 Unit はステータスを自分で持たず JobMaster へ委譲する（ジョブ ID のみ保持）。
//    対して敵は「個体ごとにスケールされた固有値」を持つため、本レコード自身が
//    現在 HP・最大 HP・攻撃力・速度を直接内包する。スケール値の決定論的な算出は
//    純粋ファクトリ EnemyScaler が担い、本レコードは生成済みの値を運ぶだけに徹する。
//
//  ★ 不変セマンティクス（設計憲法 ②）:
//    すべてのフィールドは init 専用。ダメージ適用などの状態遷移は自身を一切変更せず
//    `with` 式で新しいインスタンスを返す。よって複数スレッドから安全に共有できる
//    （参照読み取りはアトミックで、内容は変更不能）。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）:
//    ボスの表示名は EnemyArchetype（ASCII 列挙キー）として保持し、実際の日本語名は
//    将来 localization 経由で解決する。本ファイルに日本語の「データ名」を埋め込まない。
// =============================================================================

using System;

namespace ChronicleKnights.Core.Battle;

// ─── 敵の原型（ボス種別） ──────────────────────────────────────────────────

/// <summary>
/// 敵個体の原型（ボス種別）。表示名は本キーから localization 経由で解決する想定で、
/// 列挙体には日本語・絵文字を一切含めない（設計憲法 ①）。
///
/// <see cref="TrialGuardian"/> は通常戦の標準敵。残り 4 体は 100 年史の各 Epoch
/// （黎明/激動/斜陽/終焉）の最終年に君臨する章ボス（EpochBoss）で、
/// <c>ChronicleTimelineConfig</c> がどの年にどの章ボスが現れるかを司る。
/// </summary>
public enum EnemyArchetype
{
    /// <summary>試練の門の守護者（通常戦の標準敵）。</summary>
    TrialGuardian,

    /// <summary>黎明（1-25 年）の章ボス。最初の試練を象徴する守り手。</summary>
    DawnWarden,

    /// <summary>激動（26-50 年）の章ボス。乱世を駆ける征服者。</summary>
    UpheavalConqueror,

    /// <summary>斜陽（51-75 年）の章ボス。衰亡の世に君臨する暴君。</summary>
    DeclineTyrant,

    /// <summary>終焉（76-100 年）の章ボス。100 年史を締めくくる永劫の覇王。</summary>
    EternalSovereign,
}

// ─── 敵の戦闘状態スナップショット ──────────────────────────────────────────

/// <summary>
/// 戦闘中の敵 1 体を表す完全不変レコード。基礎ステータス（最大 HP・攻撃力・速度）と
/// 一時状態（現在 HP）を併せ持ち、被弾は <see cref="WithDamageTaken"/> が新インスタンスで返す。
/// </summary>
public sealed record EnemyState
{
    /// <summary>この敵の原型（ボス種別）。表示名解決のキーを兼ねる。</summary>
    public required EnemyArchetype Archetype { get; init; }

    /// <summary>最大 HP（個体スケール後の固定値、常に 1 以上）。</summary>
    public required int MaxHp { get; init; }

    /// <summary>現在 HP（被弾で減少。0〜MaxHp の範囲に保たれる）。</summary>
    public required int Hp { get; init; }

    /// <summary>攻撃力（個体スケール後の固定値、非負）。分隊への被ダメ算出の基値。</summary>
    public required int Attack { get; init; }

    /// <summary>速度（個体スケール後の固定値、非負）。イニシアチブ（行動順）の比較値。</summary>
    public required int Speed { get; init; }

    // ─── 派生ゲッタ ─────────────────────────────────────────────────────

    /// <summary>HP が尽きて撃破された状態か。</summary>
    public bool IsDefeated => Hp <= 0;

    /// <summary>まだ生存しているか。</summary>
    public bool IsAlive => Hp > 0;

    /// <summary>HP の充足率（0.0〜1.0、UI のゲージ描画用）。MaxHp が 0 以下なら 0。</summary>
    public double HpRatio => MaxHp <= 0 ? 0.0 : (double)Hp / MaxHp;

    // ─── ファクトリ ─────────────────────────────────────────────────────

    /// <summary>
    /// 満タン（Hp = MaxHp）の敵を生成する。スケール後の確定値を受け取る入口であり、
    /// 不変条件（MaxHp ≥ 1、Attack・Speed ≥ 0）をここで検証する。
    /// </summary>
    /// <param name="archetype">敵の原型（ボス種別）。</param>
    /// <param name="maxHp">最大 HP（1 以上）。生成時の現在 HP もこの値になる。</param>
    /// <param name="attack">攻撃力（非負）。</param>
    /// <param name="speed">速度（非負）。</param>
    /// <exception cref="ArgumentOutOfRangeException">いずれかのステータスが範囲外の場合。</exception>
    public static EnemyState CreateAtFullHealth(
        EnemyArchetype archetype, int maxHp, int attack, int speed)
    {
        if (maxHp < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHp), maxHp, "max hp must be at least 1");
        }
        if (attack < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attack), attack, "attack must be non-negative");
        }
        if (speed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(speed), speed, "speed must be non-negative");
        }

        return new EnemyState
        {
            Archetype = archetype,
            MaxHp = maxHp,
            Hp = maxHp,
            Attack = attack,
            Speed = speed,
        };
    }

    // ─── 不変な状態遷移 ─────────────────────────────────────────────────

    /// <summary>
    /// 指定量のダメージを受けた新しい敵を返す。現在 HP から amount を引き、0 で下限を
    /// 打ち切る。元インスタンスは一切変更しない（不変セマンティクス）。
    ///
    /// amount が 0、または既に撃破済み（HP が変化しない）の場合は自身をそのまま返す
    /// （不要な再生成を避ける）。
    /// </summary>
    /// <param name="amount">受けるダメージ量（非負）。</param>
    /// <exception cref="ArgumentOutOfRangeException">amount が負の場合。</exception>
    public EnemyState WithDamageTaken(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "damage amount must be non-negative");
        }
        if (amount == 0) return this;

        var nextHp = Math.Max(0, Hp - amount);
        if (nextHp == Hp) return this; // 既に 0（撃破済みへのオーバーキル）は無変更

        return this with { Hp = nextHp };
    }
}
