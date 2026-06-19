// =============================================================================
//  ChronicleKnights — AffixMaster.cs
// -----------------------------------------------------------------------------
//  装備の付加効果 (Affix) の数値 SoT ＋ 決定論的な生成・解決マスター。
//
//  Equipment.AffixKeys（IReadOnlyList<string>）に積まれた ASCII キー
//  （例 "affix-sharp"）を、本マスターが具体的な戦闘ステ補正へ解決する。
//  装備本体（Equipment）は「種別 × レベル」で基礎ステを決め、Affix は
//  その上に載る「個体ごとのランダムな味付け」＝ハクスラのドロップ快感の核。
//
//  ★ 設計方針（既存の配線へ合流し、新しい戦闘式を増設しない）:
//      - Affix の効果は ATK / DEF / SPD の整数フラット加算のみ（MVP）。
//      - 既に BattleManager.Equipment{Attack,Defense,Speed}Bonus が装備ステを
//        実戦へ流し込んでいるので、そこへ Affix 分を加算合流させるだけで効く。
//      - レベル乗算（Equipment.ComputeScaledStat）は通さない。Affix は素のフラット値。
//
//  ★ 決定論: 生成は Random を引数注入で受け取り、同一シード＋同一レベルからは
//      同一の Affix 列を返す（開発憲法④）。日本語表示名は本ファイルに持たず、
//      Config/localization_ja.json の affixes.{key}.name から引く（憲法①）。
//
//  略称 (BDF/SDF/AB/HL/FA/RA) は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Units;

// ─── Affix 種別（戦闘ステ軸） ───────────────────────────────────────────────

/// <summary>
/// 付加効果の種別。MVP では戦闘ステ（攻撃 / 守護 / 速度）の 3 軸のみ。
/// 各種別の数値・表示名キーは <see cref="AffixMaster"/> が SoT として保持する。
/// </summary>
public enum AffixKind
{
    /// <summary>鋭利の — 攻撃力 (ATK) を底上げ。</summary>
    Sharp,
    /// <summary>堅牢の — 分隊守護力 (DEF) を底上げ。</summary>
    Sturdy,
    /// <summary>俊敏の — 行動速度 (SPD) を底上げ。</summary>
    Swift,
}

// ─── Affix 定義（1 種別の不変 SoT 値） ──────────────────────────────────────

/// <summary>
/// Affix 1 種別の不変定義。ASCII キー・表示名キー・戦闘ステ補正値を束ねる。
/// </summary>
public sealed record AffixDefinition
{
    /// <summary>種別 enum。</summary>
    public required AffixKind Kind { get; init; }

    /// <summary>ASCII キー（Equipment.AffixKeys に積まれる値。例 "affix-sharp"）。</summary>
    public required string Key { get; init; }

    /// <summary>表示名のローカライズキー（affixes.{Key}.name で引く）。</summary>
    public required string NameKey { get; init; }

    /// <summary>攻撃力 (ATK) フラット補正。</summary>
    public int AttackBonus { get; init; }

    /// <summary>分隊守護力 (DEF) フラット補正。</summary>
    public int DefenseBonus { get; init; }

    /// <summary>行動速度 (SPD) フラット補正。</summary>
    public int SpeedBonus { get; init; }
}

// ─── Affix マスター（数値 SoT ＋ 生成・解決） ───────────────────────────────

/// <summary>
/// Affix の数値 SoT ＋ 決定論的な生成・解決を司る純粋な静的マスター。
/// </summary>
public static class AffixMaster
{
    // ─── 生成数の境界 ─────────────────────────────────────────────────────

    /// <summary>ドロップ装備が持ちうる Affix の下限（兵器廠購入品など素の装備は 0）。</summary>
    public const int MinAffixCount = 0;

    /// <summary>1 個の装備が持ちうる Affix の上限。</summary>
    public const int MaxAffixCount = 2;

    // ─── 全 Affix 定義の SoT ──────────────────────────────────────────────

    /// <summary>種別 → 定義の不変辞書（数値 SoT）。</summary>
    public static readonly ImmutableDictionary<AffixKind, AffixDefinition> All
        = BuildAll();

    /// <summary>ASCII キー → 定義の逆引き辞書（Equipment.AffixKeys 解決用）。</summary>
    public static readonly ImmutableDictionary<string, AffixDefinition> ByKey
        = All.Values.ToImmutableDictionary(d => d.Key, StringComparer.Ordinal);

    private static ImmutableDictionary<AffixKind, AffixDefinition> BuildAll()
    {
        var b = ImmutableDictionary.CreateBuilder<AffixKind, AffixDefinition>();

        b.Add(AffixKind.Sharp, new AffixDefinition
        {
            Kind = AffixKind.Sharp,
            Key = "affix-sharp",
            NameKey = "affix-sharp",
            AttackBonus = 3,
        });

        b.Add(AffixKind.Sturdy, new AffixDefinition
        {
            Kind = AffixKind.Sturdy,
            Key = "affix-sturdy",
            NameKey = "affix-sturdy",
            DefenseBonus = 2,
        });

        b.Add(AffixKind.Swift, new AffixDefinition
        {
            Kind = AffixKind.Swift,
            Key = "affix-swift",
            NameKey = "affix-swift",
            SpeedBonus = 2,
        });

        return b.ToImmutable();
    }

    // ─── 解決（キー → 定義） ───────────────────────────────────────────────

    /// <summary>
    /// ASCII キーから定義を解決する。未知キー（旧セーブ・タイポ等）は null を返し、
    /// 効果合算では 0 として無視される（画面を落とさない＝未知キーは無害化）。
    /// </summary>
    public static AffixDefinition? Resolve(string? key)
        => key is not null && ByKey.TryGetValue(key, out var d) ? d : null;

    // ─── 効果合算（Equipment.AffixKeys → 戦闘ステ補正） ───────────────────

    /// <summary>キー列が与える攻撃力 (ATK) 補正の総和。未知キーは 0。</summary>
    public static int SumAttackBonus(IEnumerable<string> keys)
        => Sum(keys, d => d.AttackBonus);

    /// <summary>キー列が与える分隊守護力 (DEF) 補正の総和。未知キーは 0。</summary>
    public static int SumDefenseBonus(IEnumerable<string> keys)
        => Sum(keys, d => d.DefenseBonus);

    /// <summary>キー列が与える行動速度 (SPD) 補正の総和。未知キーは 0。</summary>
    public static int SumSpeedBonus(IEnumerable<string> keys)
        => Sum(keys, d => d.SpeedBonus);

    private static int Sum(IEnumerable<string> keys, Func<AffixDefinition, int> selector)
    {
        if (keys is null) return 0;
        var total = 0;
        foreach (var key in keys)
        {
            var def = Resolve(key);
            if (def is not null) total += selector(def);
        }
        return total;
    }

    // ─── 決定論的な生成（ドロップ時のロール） ─────────────────────────────

    /// <summary>
    /// ドロップ装備のレベルに応じた Affix 個数。Lv1〜2 は 1 個、Lv3 以上は 2 個。
    /// （ドロップは必ず最低 1 個の Affix を持ち、レアリティの上昇を体感させる）。
    /// </summary>
    public static int AffixCountForLevel(int level)
        => level <= 2 ? 1 : MaxAffixCount;

    /// <summary>
    /// 指定レベルに応じた個数ぶん、相異なる Affix 種別を決定論的に抽選してキー列を返す。
    /// 同一シードからは同一の結果（開発憲法④）。種別は重複しない（多様性のため distinct 抽選）。
    /// </summary>
    /// <param name="level">ドロップ装備のレベル（個数決定に使用）。</param>
    /// <param name="rng">注入された乱数（決定論の源）。</param>
    public static ImmutableArray<string> RollAffixKeys(int level, Random rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var count = Math.Clamp(AffixCountForLevel(level), MinAffixCount, MaxAffixCount);
        if (count <= 0) return ImmutableArray<string>.Empty;

        // 全種別を取り出し、Fisher-Yates で先頭 count 個を相異なるよう抽選する。
        var pool = new List<AffixDefinition>(All.Values);
        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var take = Math.Min(count, pool.Count);
        var builder = ImmutableArray.CreateBuilder<string>(take);
        for (var i = 0; i < take; i++) builder.Add(pool[i].Key);
        return builder.MoveToImmutable();
    }
}
