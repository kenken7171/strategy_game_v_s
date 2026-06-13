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
    public void Epochs_DifficultyAndEnvironment_StrictlyIncreaseByEpoch()
    {
        var epochs = ChronicleTimelineConfig.Epochs;
        for (var i = 1; i < epochs.Length; i++)
        {
            Assert.True(epochs[i].DifficultyScalePercent > epochs[i - 1].DifficultyScalePercent);
            Assert.True(epochs[i].EnvironmentModifierPercent > epochs[i - 1].EnvironmentModifierPercent);
        }

        // 章ごとの基準値（黎明=等倍、終焉=最も過酷）を固定アサート。
        Assert.Equal(100, epochs[0].DifficultyScalePercent);
        Assert.Equal(220, epochs[3].DifficultyScalePercent);
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
}
