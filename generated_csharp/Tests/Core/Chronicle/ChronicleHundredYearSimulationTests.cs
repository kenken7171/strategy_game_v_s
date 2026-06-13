// =============================================================================
//  ChronicleKnights.Tests — ChronicleHundredYearSimulationTests.cs
// -----------------------------------------------------------------------------
//  100 年史を 1 年から 100 年まで実際に回し、歴史の「理（ことわり）」を完全包囲する
//  統合シミュレーション検証（要件④の本丸）。
//
//  検証アサーション:
//    1. 難易度曲線: 1→100 年で敵ステータスが毎年単調に成長し、章境界（26/51/76 年）では
//       章内の 1 年刻みより大きな段差（難易度ジャンプ）を踏むこと。
//    2. 章ボス前兆の混入: 各章ボス出現年（25/50/75/100）の直前年において、暦から組んだ
//       スケジュールを通すと予言ルックアヘッド（ForecastWithOmens）の先頭へ、正しい章ボスの
//       前兆が 1 ビットの狂いもなく混入すること（決定論）。
//    3. クランプ番兵: 1〜120 年を通しても例外を投げず、101 年目以降は年 100 の値で頭打ちに
//       なること。
//
//  ★ 乱数は外部注入シードのみ。開発憲法 ①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class ChronicleHundredYearSimulationTests
{
    private static EnemyState ForecastCarrier()
        => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 500, 40, 120);

    // ─── 1. 難易度曲線（1→100 年で毎年単調成長・章境界で段差） ──────────────

    [Fact]
    public void HundredYearLoop_EveryStat_StrictlyIncreasesEachYear()
    {
        var template = EnemyScalingResolver.TrialGuardianTemplate;
        var previous = EnemyScalingResolver.ResolveEnemyStats(
            ChronicleTimelineConfig.FirstYear, template);

        for (var year = ChronicleTimelineConfig.FirstYear + 1;
             year <= ChronicleTimelineConfig.TotalYears;
             year++)
        {
            var current = EnemyScalingResolver.ResolveEnemyStats(year, template);

            Assert.True(current.Hp > previous.Hp, $"HP must rise at year {year}");
            Assert.True(current.Attack > previous.Attack, $"ATK must rise at year {year}");
            Assert.True(current.Defense > previous.Defense, $"DEF must rise at year {year}");
            Assert.True(current.Speed > previous.Speed, $"SPD must rise at year {year}");

            previous = current;
        }
    }

    [Theory]
    [InlineData(26)] // 黎明 → 激動
    [InlineData(51)] // 激動 → 斜陽
    [InlineData(76)] // 斜陽 → 終焉
    public void HundredYearLoop_EpochBoundary_ProducesLargerJumpThanWithinEpochStep(int boundaryYear)
    {
        var template = EnemyScalingResolver.TrialGuardianTemplate;

        var beforeBoundary = EnemyScalingResolver.ResolveEnemyStats(boundaryYear - 1, template);
        var atBoundary = EnemyScalingResolver.ResolveEnemyStats(boundaryYear, template);
        var afterBoundary = EnemyScalingResolver.ResolveEnemyStats(boundaryYear + 1, template);

        var boundaryJump = atBoundary.Hp - beforeBoundary.Hp;     // 章を跨ぐ段差
        var withinEpochStep = afterBoundary.Hp - atBoundary.Hp;    // 章内の 1 年刻み

        // 章境界の難易度ジャンプは、章内の 1 年刻みより明確に大きい（難易度曲線の起伏）。
        Assert.True(boundaryJump > withinEpochStep,
            $"boundary jump ({boundaryJump}) must exceed within-epoch step ({withinEpochStep}) at year {boundaryYear}");
    }

    // ─── 2. 章ボス前兆の混入（出現年直前で予言へ漏れなく・決定論） ───────────

    [Theory]
    [InlineData(25, EnemyArchetype.DawnWarden, "epoch-dawn")]
    [InlineData(50, EnemyArchetype.UpheavalConqueror, "epoch-upheaval")]
    [InlineData(75, EnemyArchetype.DeclineTyrant, "epoch-decline")]
    [InlineData(100, EnemyArchetype.EternalSovereign, "epoch-twilight")]
    public void EpochBossYear_ForecastHeadHeraldsCorrectBoss_OneBitExact(
        int bossYear, EnemyArchetype expectedBoss, string expectedEpochKey)
    {
        // 章ボス出現年の直前年から暦でスケジュールを組み、運命の帯へ通す。
        var currentYear = bossYear - 1;
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(currentYear);

        var band = AttackIntentRoller.ForecastWithOmens(ForecastCarrier(), 12345u, 4, schedule);

        // 先頭（T+1 = まさに章ボス出現年）に、正しい章ボスの前兆が残り 0 で混入する。
        var head = band[0];
        Assert.True(head.HeraldsEpochBoss);
        Assert.Equal(expectedBoss, head.BossOmen!.BossArchetype);
        Assert.Equal(expectedEpochKey, head.BossOmen!.EpochNameKey);
        Assert.Equal(0, head.BossOmen!.TurnsUntilArrival);
        Assert.True(head.BossOmen!.ArrivesThisTurn);

        // 決定論: 同じ暦・同じシードから何度組んでも前兆は揺るがない。
        var again = AttackIntentRoller.ForecastWithOmens(ForecastCarrier(), 12345u, 4, schedule);
        Assert.Equal(head.BossOmen!.BossArchetype, again[0].BossOmen!.BossArchetype);
        Assert.Equal(head.BossOmen!.TurnsUntilArrival, again[0].BossOmen!.TurnsUntilArrival);
    }

    [Fact]
    public void NonBossApproachYear_ForecastCarriesNoOmen()
    {
        // 章ボスから遠い年（10 年・次ボスは 25 年で残り 15）かつ短い帯では、前兆窓に入らず無し。
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(10);
        var band = AttackIntentRoller.ForecastWithOmens(ForecastCarrier(), 777u, 5, schedule);

        foreach (var entry in band)
        {
            Assert.False(entry.HeraldsEpochBoss);
        }
    }

    [Fact]
    public void FinalYear_NoUpcomingBoss_ForecastCarriesNoOmen()
    {
        // 最終ボス年 100 ではこれ以上将来の章ボスが無く、None スケジュール＝前兆ゼロ。
        var schedule = ChronicleTimelineConfig.BuildOmenScheduleForYear(
            ChronicleTimelineConfig.TotalYears);
        var band = AttackIntentRoller.ForecastWithOmens(ForecastCarrier(), 5u, 6, schedule);

        Assert.Equal(EpochBossOmenSchedule.None, schedule);
        foreach (var entry in band)
        {
            Assert.False(entry.HeraldsEpochBoss);
        }
    }

    // ─── 3. クランプ番兵（1〜120 年・例外なし・101+ で頭打ち） ───────────────

    [Fact]
    public void Simulation_FromYear1To120_NeverThrows_AndPlateausPast100()
    {
        var template = EnemyScalingResolver.TrialGuardianTemplate;
        var atCap = EnemyScalingResolver.ResolveEnemyStats(
            ChronicleTimelineConfig.TotalYears, template);

        for (var year = 1; year <= 120; year++)
        {
            // 例外を投げないこと自体が検証（範囲外でも安全にクランプ）。
            var stats = EnemyScalingResolver.ResolveEnemyStats(year, template);

            // ついでに暦の引き当ても安全（章・前兆スケジュール）。
            _ = ChronicleTimelineConfig.EpochForYear(year);
            _ = ChronicleTimelineConfig.BuildOmenScheduleForYear(year);

            if (year >= ChronicleTimelineConfig.TotalYears)
            {
                // 101 年目以降は年 100 の値で完全に頭打ち。
                Assert.Equal(atCap.Hp, stats.Hp);
                Assert.Equal(atCap.Attack, stats.Attack);
                Assert.Equal(atCap.Defense, stats.Defense);
                Assert.Equal(atCap.Speed, stats.Speed);
            }
        }
    }
}
