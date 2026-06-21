// =============================================================================
//  ChronicleKnights.Tests — UnitStatProfileTests.cs
// -----------------------------------------------------------------------------
//  ユニットの実効戦闘ステ解決（UnitStatProfile）の純粋ロジック検証。
//    - レベル成長: Lv1=×1.0 / Lv2=×1.25 / Lv3=×1.5
//    - 三段階加齢: 修業期(線形成長) → 全盛期(1.0) → 衰退期(年3%減)
//    - 0 値の項は 0 のまま（治癒/防御の 0 を 1 に膨らませない保護）
// =============================================================================

using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Units;

public class UnitStatProfileTests
{
    // ─── レベル係数 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.25)]
    [InlineData(3, 1.5)]
    [InlineData(5, 1.5)] // 上限 Lv3 でクランプ
    public void LevelMultiplier_RisesPerLevel_AndClampsAtMax(int level, double expected)
        => Assert.Equal(expected, UnitStatProfile.LevelMultiplier(level), 6);

    // ─── 加齢係数（三段階） ────────────────────────────────────────────────

    [Fact]
    public void AgeFactor_GrowthPhase_RisesLinearlyTowardPrime()
    {
        // 修業期: age/MaturityAge。15/25=0.6、20/25=0.8。
        Assert.Equal(0.6, UnitStatProfile.AgeFactor(15), 6);
        Assert.Equal(0.8, UnitStatProfile.AgeFactor(20), 6);
        Assert.True(UnitStatProfile.AgeFactor(15) < UnitStatProfile.AgeFactor(20));
    }

    [Theory]
    [InlineData(UnitStatProfile.MaturityAge)]   // 全盛期入り
    [InlineData(35)]                            // 全盛期の中
    [InlineData(UnitStatProfile.DeclineAge)]    // 全盛期の終わり際
    public void AgeFactor_PrimePhase_IsExactlyOne(int age)
        => Assert.Equal(1.0, UnitStatProfile.AgeFactor(age), 6);

    [Fact]
    public void AgeFactor_DeclinePhase_FallsBelowOne_AndMonotonicallyDecreases()
    {
        // 衰退期: 0.97^(age-DeclineAge)。DeclineAge=45 → 50 で 0.97^5。
        var at50 = UnitStatProfile.AgeFactor(50);
        var at60 = UnitStatProfile.AgeFactor(60);
        Assert.True(at50 < 1.0, $"decline should drop below 1.0 but was {at50}");
        Assert.True(at60 < at50, "older = weaker in decline phase");
        Assert.Equal(System.Math.Pow(0.97, 5), at50, 6);
    }

    // ─── 実効ステ（成長は 0 値を 0 のまま保つ） ───────────────────────────────

    [Fact]
    public void EffectiveStats_PrimeLevel1_EqualsBase()
    {
        // 全盛期(age 30) + Lv1 ＝ 係数 1.0 ＝ 素のジョブ値そのまま。
        var baseStats = JobMaster.Find(JobId.HeavyInfantry)!.Stats;
        var eff = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 30);
        Assert.Equal(baseStats, eff);
    }

    [Fact]
    public void EffectiveStats_Level3Prime_ScalesPositiveStatsByOnePointFive_ZerosStayZero()
    {
        // 重装歩兵: HP300/FA70/RA20、BDEF0/InitiativeBuff0。Lv3 全盛期 ＝ ×1.5。0 値は 0 のまま。
        var baseStats = JobMaster.Find(JobId.HeavyInfantry)!.Stats;
        var eff = UnitStatProfile.EffectiveStats(baseStats, level: 3, age: 30);

        Assert.Equal(450, eff.MaxHp);        // 300×1.5
        Assert.Equal(105, eff.FrontAttack);  // 70×1.5
        Assert.Equal(30, eff.RearAttack);    // 20×1.5
        Assert.Equal(0, eff.BattalionDefense);   // 0 は 0 のまま（1 に膨らませない）
        Assert.Equal(0, eff.InitiativeBuff);     // 0 は 0 のまま
    }

    [Fact]
    public void EffectiveStats_YoungRecruit_IsWeakerThanPrime()
    {
        var baseStats = JobMaster.Find(JobId.Sniper)!.Stats;
        var young = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 15);  // 0.6
        var prime = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 30);  // 1.0
        Assert.True(young.RearAttack < prime.RearAttack, "young recruit weaker than prime");
        Assert.Equal(baseStats.RearAttack, prime.RearAttack);
    }

    [Fact]
    public void EffectiveStats_ElderInDecline_IsWeakerThanPrime()
    {
        var baseStats = JobMaster.Find(JobId.Sorcerer)!.Stats;
        var prime = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 30);  // 1.0
        var elder = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 60);  // decline
        Assert.True(elder.RearAttack < prime.RearAttack, "elder weaker than prime (decline)");
        Assert.True(elder.RearAttack >= 1, "positive stat never floors to 0");
    }
}
