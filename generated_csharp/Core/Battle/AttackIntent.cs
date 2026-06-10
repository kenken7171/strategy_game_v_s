// =============================================================================
//  ChronicleKnights — AttackIntent.cs
// -----------------------------------------------------------------------------
//  「敵が次のターンに何をしてくるか」を 1 ターン先んじて開示する、完全不変の
//  攻撃予告レコード群。戦闘の戦術的緊張（=回転作戦の読み合い）の核となる、
//  未来を内包したデータ構造である。
//
//  ★ 役割（先読みの意味論）:
//    BattleSnapshot は常にちょうど 1 つの NextEnemyIntent を内包する。これは
//    「この静止画の次のターンで敵が放つ攻撃」を指し示す予言であり、UI は
//    これを読んで攻撃予告バナーや危険エリアの赤枠脈動を描く。プレイヤーは
//    予告された対象行（TargetRows）を見て、どの分隊を前に出すか／退げるかを
//    回転作戦で決める。予告が無ければ回転は博打に過ぎず、予告があって初めて
//    陣形が「意思決定」になる。
//
//  ★ 3 つの戦術パターン（移植元 TypeScript 版の思想を継承）:
//      SingleStrike … 単一分隊強襲（1 行へ高ダメージ。倍率 2.0）
//      Pincer       … 複数分隊挟撃（2 行へ中ダメージ。倍率 1.4）
//      TotalAssault … 全大隊総攻撃（3 行へ低ダメージ。倍率 0.9）
//    どのパターンを・どの行へ・何ダメージで放つかの抽選は純粋ファクトリ
//    AttackIntentRoller が外部注入の System.Random から決定論的に行う。本
//    レコード自身はロール済みの確定値を運ぶだけの不変データに徹する。
//
//  ★ 日本語ハードコード禁止（設計憲法 ①）:
//    スキルの表示名は SkillNameKey（ASCII キー）として保持し、実際の日本語名は
//    将来 localization 経由で解決する。本ファイルに日本語の「データ名」は埋めない。
//
//  完全不変（設計憲法 ②）。Godot 非依存の純粋 C#。略称（BDF/SDF/AB/HL）も未使用。
// =============================================================================

using System.Collections.Immutable;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.Core.Battle;

// ─── 攻撃パターン種別 ────────────────────────────────────────────────────────

/// <summary>
/// 敵の攻撃パターン（戦術の型）。表示名は localization 経由で解決する想定で、
/// 列挙体には日本語・絵文字を一切含めない（設計憲法 ①）。狙う行数と威力倍率が
/// 種別ごとに決まり、AttackIntentRoller がこの種別から TargetRows と威力を導く。
/// </summary>
public enum AttackPatternKind
{
    /// <summary>単一分隊強襲。1 行へ集中する高威力（倍率 2.0）。</summary>
    SingleStrike,

    /// <summary>複数分隊挟撃。2 行を同時に襲う中威力（倍率 1.4）。</summary>
    Pincer,

    /// <summary>全大隊総攻撃。3 行すべてを薙ぐ低威力（倍率 0.9）。</summary>
    TotalAssault,
}

// ─── 攻撃予告（未来を内包する不変レコード） ──────────────────────────────────

/// <summary>
/// 敵の「次ターン攻撃」を 1 ターン先んじて開示する完全不変レコード。
///
/// パターン種別・スキル名キー・対象となる分隊行の配列・1 ユニットあたりの基礎
/// ダメージ（防御軽減前）を束ねる。<see cref="DamagePerUnit"/> はパターン倍率と
/// ±15% ジッタを掛けた「素の威力」で、実際の被ダメージは解決時に対象行の防御で
/// 軽減される（BattleResolver が BattleManager.ResolveIncomingDamage へ委譲）。
/// </summary>
public sealed record AttackIntent
{
    /// <summary>この予告の戦術パターン種別。</summary>
    public required AttackPatternKind Kind { get; init; }

    /// <summary>
    /// 表示用スキル名を引き当てる localization キー（ASCII）。実際の日本語名は
    /// localization 層が本キーから解決する想定（設計憲法 ①）。
    /// </summary>
    public required string SkillNameKey { get; init; }

    /// <summary>
    /// 攻撃が降りかかる分隊行（1〜3 個）。SingleStrike は 1 行、Pincer は 2 行、
    /// TotalAssault は 3 行すべて。順序は正準順（Front → RearLeft → RearRight）。
    /// </summary>
    public required ImmutableArray<SquadRow> TargetRows { get; init; }

    /// <summary>
    /// 対象行の 1 ユニットあたりに与える基礎ダメージ（防御軽減前・常に 1 以上）。
    /// 解決時に対象行の大隊防御＋分隊防御で軽減されてから HP へ適用される。
    /// </summary>
    public required int DamagePerUnit { get; init; }

    // ─── 派生クエリ（読み取り専用ヘルパー） ──────────────────────────────

    /// <summary>指定行がこの予告の標的に含まれるか（UI の赤枠脈動判定などに使う）。</summary>
    public bool Targets(SquadRow row) => TargetRows.Contains(row);
}
