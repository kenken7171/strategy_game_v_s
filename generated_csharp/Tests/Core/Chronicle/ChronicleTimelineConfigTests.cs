// =============================================================================
//  ChronicleKnights.Tests — ChronicleTimelineConfigTests.cs
// -----------------------------------------------------------------------------
//  100 年史マスターデータ ChronicleTimelineConfig（4 章の骨格）を網羅検証する。
//
//  検証の柱:
//    1. 章の連続性: 4 章が [1,100] を隙間・重なりなく覆い、難易度/環境が単調増加する。
//    2. 年 → 章の引き当てとクランプ番兵: 範囲内は正しい章、範囲外（0 以下/101 以上）は
//       端の章へ安全に丸められ例外を投げない（要件④）。
//    3. 章ボス出現年（25/50/75/100）の判定。
//    4. 章ボス接近前兆スケジュールの決定論的ビルダ（次ボスまでの残り年・原型・キー）。
//
//  ★ 乱数・SoT 非依存。開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class ChronicleTimelineConfigTests
{
    // ─── 1. 章の連続性・単調な難易度曲線 ──────────────────────────────────

    [Fact]
    public void Epochs_Cover1To100_WithoutGapOrOverlap()
    {
        var epochs = ChronicleTimelineConfig.Epochs;
        Assert.Equal(4, epochs.Length);

        // 先頭は 1 年、末尾は 100 年で始終する。
        Assert.Equal(ChronicleTimelineConfig.FirstYear, epochs[0].StartYear);
        Assert.Equal(ChronicleTimelineConfig.TotalYears, epochs[epochs.Length - 1].EndYear);

        // 各章は前章の終了年 + 1 から始まる（隙間も重なりも無い連続帯）。
        for (var i = 1; i < epochs.Length; i++)
        {
            Assert.Equal(epochs[i - 1].EndYear + 1, epochs[i].StartYear);
            Assert.True(epochs[i].EndYear > epochs[i].StartYear);
        }
    }

    [Fact]
    public void Epochs_DifficultyStrictlyIncreases_EnvironmentNonDecreasing()
    {
        var epochs = ChronicleTimelineConfig.Epochs;
        for (var i = 1; i < epochs.Length; i++)
        {
            // 難易度は章ごとに厳密に上がる（黎明 100 < 激動 140 < 斜陽 150 < 終焉 170）。
            Assert.True(epochs[i].DifficultyScalePercent > epochs[i - 1].DifficultyScalePercent);
            // 環境補正は単調非減少（黄金均衡への調律で後半をフラット化＝100,110,110,110）。
            Assert.True(epochs[i].EnvironmentModifierPercent >= epochs[i - 1].EnvironmentModifierPercent);
        }

        // 章ごとの基準値（黎明=等倍、終焉=最も過酷だが超えられる傾斜壁）を固定アサート。
        Assert.Equal(100, epochs[0].DifficultyScalePercent);
        Assert.Equal(170, epochs[3].DifficultyScalePercent);
    }

    // ─── 2. 年 → 章の引き当て ──────────────────────────────────────────────

    [Theory]
    [InlineData(1, EpochId.Dawn)]
    [InlineData(25, EpochId.Dawn)]
    [InlineData(26, EpochId.Upheaval)]
    [InlineData(50, EpochId.Upheaval)]
    [InlineData(51, EpochId.Decline)]
    [InlineData(75, EpochId.Decline)]
    [InlineData(76, EpochId.Twilight)]
    [InlineData(100, EpochId.Twilight)]
    public void EpochForYear_InRange_MapsToCorrectEpoch(int year, EpochId expected)
    {
        Assert.Equal(expected, ChronicleTimelineConfig.EpochForYear(year).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-9999)]
    public void EpochForYear_BelowFirstYear_ClampsToDawn_NoThrow(int year)
    {
        Assert.Equal(EpochId.Dawn, ChronicleTimelineConfig.EpochForYear(year).Id);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(250)]
    [InlineData(int.MaxValue)]
    public void EpochForYear_BeyondTotalYears_ClampsToTwilight_NoThrow(int year)
    {
        // 要件④: 101 年目以降は最後の章へ安全にクランプ（IndexOutOfRange を投げない）。
        Assert.Equal(EpochId.Twilight, ChronicleTimelineConfig.EpochForYear(year).Id);
    }

    // ─── 3. 章ボス出現年 ───────────────────────────────────────────────────

    [Theory]
    [InlineData(25, true)]
    [InlineData(50, true)]
    [InlineData(75, true)]
    [InlineData(100, true)]
    [InlineData(1, false)]
    [InlineData(24, false)]
    [InlineData(26, false)]
    [InlineData(99, false)]
    [InlineData(101, false)]
    [InlineData(0, false)]
    public void IsEpochBossYear_OnlyTrueAtEpochFinalYears(int year, bool expected)
    {
        Assert.Equal(expected, ChronicleTimelineConfig.IsEpochBossYear(year));
    }

    // ─── 3.5 戦闘出現原型の選定（戦闘開始ファクトリの正本） ─────────────────

    [Theory]
    [InlineData(25, EnemyArchetype.DawnWarden)]
    [InlineData(50, EnemyArchetype.UpheavalConqueror)]
    [InlineData(75, EnemyArchetype.DeclineTyrant)]
    [InlineData(100, EnemyArchetype.EternalSovereign)]
    [InlineData(1, EnemyArchetype.TrialGuardian)]
    [InlineData(24, EnemyArchetype.TrialGuardian)]
    [InlineData(26, EnemyArchetype.TrialGuardian)]
    [InlineData(74, EnemyArchetype.TrialGuardian)]
    [InlineData(99, EnemyArchetype.TrialGuardian)]
    public void BattleArchetypeForYear_BossYearsYieldEpochBoss_OthersTrialGuardian(
        int year, EnemyArchetype expected)
    {
        Assert.Equal(expected, ChronicleTimelineConfig.BattleArchetypeForYear(year));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(500)]
    [InlineData(int.MaxValue)]
    public void BattleArchetypeForYear_BeyondTotalYears_ClampsToFinalBoss(int year)
    {
        // 101 年目以降は最終年 100（章ボス出現年）へクランプ → 終焉の覇王。例外を投げない。
        Assert.Equal(EnemyArchetype.EternalSovereign, ChronicleTimelineConfig.BattleArchetypeForYear(year));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void BattleArchetypeForYear_BelowFirstYear_IsTrialGuardian(int year)
    {
        // 0 年以下は黎明 1 年（通常年）へクランプ → 試練の門の守護者。
        Assert.Equal(EnemyArchetype.TrialGuardian, ChronicleTimelineConfig.BattleArchetypeForYear(year));
    }

    // ─── 4. 章ボス接近前兆スケジュールのビルダ ─────────────────────────────

    [Theory]
    [InlineData(24, 1, EnemyArchetype.DawnWarden, "epoch-dawn")]
    [InlineData(49, 1, EnemyArchetype.UpheavalConqueror, "epoch-upheaval")]
    [InlineData(74, 1, EnemyArchetype.DeclineTyrant, "epoch-decline")]
    [InlineData(99, 1, EnemyArchetype.EternalSovereign, "epoch-twilight")]
    [InlineData(22, 3, EnemyArchetype.DawnWarden, "epoch-dawn")]
    [InlineData(25, 25, EnemyArchetype.UpheavalConqueror, "epoch-upheaval")] // ボス年では「次の」章ボスを指す
    public void BuildOmenScheduleForYear_PointsToNextBoss_Deterministically(
        int currentYear, int expectedTurnsUntil, EnemyArchetype expectedBoss, string expectedEpochKey)
    {
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(currentYear);

        Assert.True(schedule.BossApproaching);
        Assert.Equal(expectedTurnsUntil, schedule.TurnsUntilArrival);
        Assert.Equal(expectedBoss, schedule.BossArchetype);
        Assert.Equal(expectedEpochKey, schedule.EpochNameKey);
        Assert.Equal(ChronicleTimelineConfig.EpochBossForewarnLeadTurns, schedule.ForewarnLeadTurns);
        Assert.Equal(ChronicleTimelineConfig.EpochBossOmenSkillNameKey, schedule.OmenSkillNameKey);
    }

    [Theory]
    [InlineData(100)] // 最終ボス年。これ以上将来の章ボスは無い。
    [InlineData(101)]
    [InlineData(9999)]
    public void BuildOmenScheduleForYear_AtOrBeyondFinalBoss_ReturnsNone(int currentYear)
    {
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(currentYear);

        Assert.False(schedule.BossApproaching);
        Assert.Equal(EpochBossOmenSchedule.None, schedule);
    }

    [Fact]
    public void BuildOmenScheduleForYear_BelowFirstYear_StillSafe_NoThrow()
    {
        // 0 年以下でも例外を投げず、最初の章ボス（25 年）までの残り年として安全に弾く。
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(0);

        Assert.True(schedule.BossApproaching);
        Assert.Equal(25, schedule.TurnsUntilArrival);
        Assert.Equal(EnemyArchetype.DawnWarden, schedule.BossArchetype);
    }

    [Fact]
    public void BuildOmenScheduleForYear_IsDeterministic()
    {
        var first = ChronicleTimelineConfig.BuildOmenScheduleForYear(48);
        var second = ChronicleTimelineConfig.BuildOmenScheduleForYear(48);
        Assert.Equal(first, second);
    }

    // ─── 5. 章ボス年スナップ（年送りジャンプがボス年を踏み越さない） ────────────

    [Theory]
    // 跨ぐ場合: ボス年へちょうど着地する年数へクランプ（スナップ）。
    [InlineData(23, 4, 2)]   // 23 + 4 = 27 が 25 を踏み越す -> 25 へ着地 (=2)
    [InlineData(24, 3, 1)]   // 24 + 3 = 27 -> 25 へ着地 (=1)
    [InlineData(48, 4, 2)]   // 48 + 4 = 52 -> 50 へ着地 (=2)
    [InlineData(98, 5, 2)]   // 98 + 5 = 103 -> 100 へ着地 (=2)
    public void ClampSkipToNextBossYear_SnapsOntoBossYear_WhenWouldOvershoot(
        int currentYear, int requested, int expected)
    {
        Assert.Equal(expected, ChronicleTimelineConfig.ClampSkipToNextBossYear(currentYear, requested));
    }

    [Theory]
    // 跨がない場合・ちょうど着地する場合・最終ボス超えは、要求年数をそのまま返す。
    [InlineData(10, 3, 3)]    // 10 + 3 = 13、25 まで余裕 -> クランプなし
    [InlineData(22, 3, 3)]    // 22 + 3 = 25 ちょうど着地 -> クランプなし（次周がボス戦）
    [InlineData(25, 4, 4)]    // ボス年に居る -> 次ボス 50 まで余裕 -> クランプなし
    [InlineData(100, 3, 3)]   // 最終ボス(100)以降はボス年なし -> クランプなし
    [InlineData(105, 4, 4)]   // 100 超え -> クランプなし
    public void ClampSkipToNextBossYear_LeavesUnchanged_WhenNoBossCrossed(
        int currentYear, int requested, int expected)
    {
        Assert.Equal(expected, ChronicleTimelineConfig.ClampSkipToNextBossYear(currentYear, requested));
    }

    [Fact]
    public void ClampSkipToNextBossYear_AlwaysAdvancesAtLeastOneYear()
    {
        // 防御的: 要求 0 でも暦は必ず 1 以上前進する。
        Assert.True(ChronicleTimelineConfig.ClampSkipToNextBossYear(10, 0) >= 1);
        Assert.True(ChronicleTimelineConfig.ClampSkipToNextBossYear(24, 0) >= 1);
    }
}
