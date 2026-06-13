// =============================================================================
//  ChronicleKnights.Tests — EnemyScalingResolverTests.cs
// -----------------------------------------------------------------------------
//  敵ステータスの決定論的スケーリング純粋ファクトリ EnemyScalingResolver を網羅検証する。
//
//  検証の柱:
//    1. 決定論: 同一 (year, template) からは record 値等価で完全一致（乱数 1 ミリも不使用）。
//    2. 整数 floor の厳密値: 章の難易度・環境補正を整数除算（× パーセント / 100）で重ねた
//       結果が、手計算と 1 ビットの狂いもなく一致する（黎明=等倍／激動の floor 切り捨て）。
//    3. クランプ番兵（要件④）: 101 年目以降は最後の章（終焉）の年 100 値で頭打ち、0 年以下は
//       1 年へクランプ。例外を投げない。
//    4. 引数防御: null テンプレートで ArgumentNullException。
//
//  ★ 乱数・SoT 非依存。開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class EnemyScalingResolverTests
{
    // ─── 1. 決定論 ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveEnemyStats_SameYearAndTemplate_ProducesIdenticalStats()
    {
        var first = EnemyScalingResolver.ResolveEnemyStats(
            42, EnemyScalingResolver.TrialGuardianTemplate);
        var second = EnemyScalingResolver.ResolveEnemyStats(
            42, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(first, second); // record 値等価＝全フィールド一致（決定論の証明）。
    }

    // ─── 2. 整数 floor の厳密値 ─────────────────────────────────────────────

    [Fact]
    public void ResolveEnemyStats_Year1_Dawn_IsExactlyPureGrowth()
    {
        // 黎明（100%/100%）= 等倍。grown = base + 1*gain がそのまま出る。
        var stats = EnemyScalingResolver.ResolveEnemyStats(
            1, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(EpochId.Dawn, stats.Epoch);
        Assert.Equal(1, stats.Year);
        Assert.Equal(155, stats.Hp);      // 150 + 1*5
        Assert.Equal(31, stats.Attack);   // 30 + 1*1
        Assert.Equal(11, stats.Defense);  // 10 + 1*1
        Assert.Equal(101, stats.Speed);   // 100 + 1*1
    }

    [Fact]
    public void ResolveEnemyStats_Year26_Upheaval_AppliesIntegerFloorScaling()
    {
        // 激動（140%/110%）。grown → ×140/100（floor）→ ×110/100（floor）。
        var stats = EnemyScalingResolver.ResolveEnemyStats(
            26, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(EpochId.Upheaval, stats.Epoch);
        // HP : (150+130)=280 → 280*140/100=392 → 392*110/100=431.2→431
        Assert.Equal(431, stats.Hp);
        // ATK: (30+26)=56 → 56*140/100=78.4→78 → 78*110/100=85.8→85
        Assert.Equal(85, stats.Attack);
        // DEF: (10+26)=36 → 36*140/100=50.4→50 → 50*110/100=55
        Assert.Equal(55, stats.Defense);
        // SPD: (100+26)=126 → 126*140/100=176.4→176 → 176*110/100=193.6→193
        Assert.Equal(193, stats.Speed);
    }

    [Fact]
    public void ResolveEnemyStats_BossTemplate_ScalesFromHigherBase()
    {
        // 終焉の覇王（220%/130%）を最終年 100 で。素の高さ＋最大倍率で最強格になる。
        var boss = EnemyScalingResolver.ResolveEnemyStats(
            100, EnemyScalingResolver.EternalSovereignTemplate);
        var guardian = EnemyScalingResolver.ResolveEnemyStats(
            100, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(EnemyArchetype.EternalSovereign, boss.Archetype);
        Assert.Equal(EpochId.Twilight, boss.Epoch);
        Assert.True(boss.Hp > guardian.Hp);
        Assert.True(boss.Attack > guardian.Attack);
        Assert.True(boss.Defense > guardian.Defense);
    }

    [Fact]
    public void ResolveEnemyStats_AllStats_AtLeastMinimumValue()
    {
        // 下限ガード: どの年でも全ステータスは MinimumStatValue 以上（0 以下に沈まない）。
        var stats = EnemyScalingResolver.ResolveEnemyStats(
            1, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.True(stats.Hp >= EnemyScalingResolver.MinimumStatValue);
        Assert.True(stats.Attack >= EnemyScalingResolver.MinimumStatValue);
        Assert.True(stats.Defense >= EnemyScalingResolver.MinimumStatValue);
        Assert.True(stats.Speed >= EnemyScalingResolver.MinimumStatValue);
    }

    // ─── 3. クランプ番兵（要件④） ───────────────────────────────────────────

    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(int.MaxValue)]
    public void ResolveEnemyStats_BeyondYear100_PlateausAtYear100_NoThrow(int year)
    {
        // 101 年目以降は最後の章（終焉）の年 100 値で完全に頭打ちになる（例外を投げない）。
        var atCap = EnemyScalingResolver.ResolveEnemyStats(
            100, EnemyScalingResolver.TrialGuardianTemplate);
        var beyond = EnemyScalingResolver.ResolveEnemyStats(
            year, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(atCap, beyond);
        Assert.Equal(100, beyond.Year);
        Assert.Equal(EpochId.Twilight, beyond.Epoch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ResolveEnemyStats_BelowYear1_ClampsToYear1_NoThrow(int year)
    {
        var atFloor = EnemyScalingResolver.ResolveEnemyStats(
            1, EnemyScalingResolver.TrialGuardianTemplate);
        var below = EnemyScalingResolver.ResolveEnemyStats(
            year, EnemyScalingResolver.TrialGuardianTemplate);

        Assert.Equal(atFloor, below);
        Assert.Equal(1, below.Year);
        Assert.Equal(EpochId.Dawn, below.Epoch);
    }

    // ─── 4. テンプレート・カタログ ──────────────────────────────────────────

    [Theory]
    [InlineData(EnemyArchetype.TrialGuardian)]
    [InlineData(EnemyArchetype.DawnWarden)]
    [InlineData(EnemyArchetype.UpheavalConqueror)]
    [InlineData(EnemyArchetype.DeclineTyrant)]
    [InlineData(EnemyArchetype.EternalSovereign)]
    public void TemplateFor_KnownArchetypes_ReturnMatchingTemplate(EnemyArchetype archetype)
    {
        Assert.Equal(archetype, EnemyScalingResolver.TemplateFor(archetype).Archetype);
    }

    // ─── 5. 引数防御 ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveEnemyStats_NullTemplate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => EnemyScalingResolver.ResolveEnemyStats(10, null!));
    }
}
