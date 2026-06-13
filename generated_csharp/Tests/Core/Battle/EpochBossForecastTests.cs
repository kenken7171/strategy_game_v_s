// =============================================================================
//  ChronicleKnights.Tests — EpochBossForecastTests.cs
// -----------------------------------------------------------------------------
//  運命の帯へ章ボス（EpochBoss）接近前兆を重ねる AttackIntentRoller.ForecastWithOmens を
//  網羅検証する。
//
//  検証の柱:
//    1. 忠実な投影: 各ターンの通常脅威（Threat）は通常の Forecast と 1 ビットの狂いもなく一致
//       （前兆を重ねても下地の脅威列は揺らがない）。
//    2. 前兆の窓: 章ボス到来の数ターン前（ForewarnLeadTurns 以内）にだけ前兆が重なり、残り
//       ターン（TurnsUntilArrival）が正しく減衰し、到来後・窓外には一切重ならない。
//    3. None スケジュール: 前兆を一切重ねない（通常の帯と同一の意味）。
//    4. 決定論・番兵: 同入力から完全同一、null 引数で例外、非正ルックアヘッドは空帯。
//
//  ★ 乱数は 100% 外部注入シード。開発憲法 ①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Core.Battle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class EpochBossForecastTests
{
    // ─── テスト用ファクトリ / 指紋ヘルパー ──────────────────────────────────

    private static EnemyState Boss(int attack = 40)
        => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 500, attack, 120);

    /// <summary>章ボス接近スケジュールを件数だけ制御して組む（既定は黎明の守り手・黎明章）。</summary>
    private static EpochBossOmenSchedule Schedule(
        int turnsUntilArrival, int forewarnLead, EnemyArchetype boss = EnemyArchetype.DawnWarden)
        => new()
        {
            BossApproaching   = true,
            TurnsUntilArrival = turnsUntilArrival,
            ForewarnLeadTurns = forewarnLead,
            BossArchetype     = boss,
            EpochNameKey      = "epoch-dawn",
            OmenSkillNameKey  = "epoch-boss-omen-approach",
        };

    private static string ThreatFingerprint(AttackIntent intent)
        => $"{intent.Kind}|{intent.SkillNameKey}|{intent.DamagePerUnit}|{string.Join(",", intent.TargetRows)}";

    private static string EntryFingerprint(ForecastEntry entry)
    {
        var omen = entry.BossOmen is null
            ? "-"
            : $"{entry.BossOmen.BossArchetype}:{entry.BossOmen.TurnsUntilArrival}:"
              + $"{entry.BossOmen.EpochNameKey}:{entry.BossOmen.OmenSkillNameKey}";
        return $"{entry.TurnOffset}|{ThreatFingerprint(entry.Threat)}|{omen}";
    }

    // ─── 1. 忠実な投影（Threat は通常 Forecast と完全一致） ──────────────────

    [Fact]
    public void ForecastWithOmens_Threats_MatchPlainForecast_ByteForByte()
    {
        var enemy = Boss(37);
        const uint seed = 0x00C0FFEEu;
        const int lookahead = 8;

        var plain = AttackIntentRoller.Forecast(enemy, seed, lookahead);
        var withOmens = AttackIntentRoller.ForecastWithOmens(enemy, seed, lookahead, Schedule(2, 3));

        Assert.Equal(plain.Count, withOmens.Count);
        for (var i = 0; i < plain.Count; i++)
        {
            // 前兆を重ねても、下地の脅威列は通常 Forecast と寸分違わない。
            Assert.Equal(ThreatFingerprint(plain[i]), ThreatFingerprint(withOmens[i].Threat));
            Assert.Equal(i + 1, withOmens[i].TurnOffset); // 1 始まりで連番。
        }
    }

    // ─── 2. 前兆の窓（先行告知・残り減衰・到来後/窓外は無し） ────────────────

    [Fact]
    public void ForecastWithOmens_OmenAppears_WithinForewarnWindow_CountingDownToArrival()
    {
        // 到来まで 3 ターン・先行告知 3。帯ターン 1..3 に前兆（残り 2,1,0）が重なり、4 以降は無し。
        var band = AttackIntentRoller.ForecastWithOmens(
            Boss(), 7u, 6, Schedule(3, 3, EnemyArchetype.UpheavalConqueror));

        Assert.True(band[0].HeraldsEpochBoss);
        Assert.Equal(2, band[0].BossOmen!.TurnsUntilArrival);
        Assert.True(band[1].HeraldsEpochBoss);
        Assert.Equal(1, band[1].BossOmen!.TurnsUntilArrival);
        Assert.True(band[2].HeraldsEpochBoss);
        Assert.Equal(0, band[2].BossOmen!.TurnsUntilArrival);
        Assert.True(band[2].BossOmen!.ArrivesThisTurn);

        // 到来後は前兆を重ねない。
        Assert.False(band[3].HeraldsEpochBoss);
        Assert.False(band[4].HeraldsEpochBoss);
        Assert.False(band[5].HeraldsEpochBoss);

        // 前兆は一貫して同じ章ボス・章・スキルキーを運ぶ。
        Assert.Equal(EnemyArchetype.UpheavalConqueror, band[0].BossOmen!.BossArchetype);
        Assert.Equal("epoch-dawn", band[0].BossOmen!.EpochNameKey);
        Assert.Equal("epoch-boss-omen-approach", band[0].BossOmen!.OmenSkillNameKey);
    }

    [Fact]
    public void ForecastWithOmens_ArrivalNextTurn_OnlyHeadHeralds()
    {
        // 到来まで 1 ターン。先頭（T+1）だけが残り 0 の前兆を持ち、以降は無し。
        var band = AttackIntentRoller.ForecastWithOmens(Boss(), 11u, 4, Schedule(1, 3));

        Assert.True(band[0].HeraldsEpochBoss);
        Assert.Equal(0, band[0].BossOmen!.TurnsUntilArrival);
        Assert.False(band[1].HeraldsEpochBoss);
        Assert.False(band[2].HeraldsEpochBoss);
        Assert.False(band[3].HeraldsEpochBoss);
    }

    [Fact]
    public void ForecastWithOmens_BeyondForewarnWindow_NoOmenYet()
    {
        // 到来まで 10 ターンだが先行告知は 3 ターン。帯 1..5（残り 9..5）はまだ窓の外＝前兆なし。
        var band = AttackIntentRoller.ForecastWithOmens(Boss(), 3u, 5, Schedule(10, 3));

        foreach (var entry in band)
        {
            Assert.False(entry.HeraldsEpochBoss);
        }
    }

    [Fact]
    public void ForecastWithOmens_NoneSchedule_NeverHeralds()
    {
        var band = AttackIntentRoller.ForecastWithOmens(
            Boss(), 99u, AttackIntentRoller.MaxForecastTurns, EpochBossOmenSchedule.None);

        Assert.Equal(AttackIntentRoller.MaxForecastTurns, band.Count);
        foreach (var entry in band)
        {
            Assert.False(entry.HeraldsEpochBoss);
            Assert.Null(entry.BossOmen);
        }
    }

    // ─── 3. 決定論（前兆込みで 1 ビットも揺るがない） ────────────────────────

    [Fact]
    public void ForecastWithOmens_IsDeterministic_AcrossManyCalls()
    {
        var enemy = Boss(28);
        const uint seed = 0x1234ABCDu;
        var schedule = Schedule(2, 3, EnemyArchetype.DeclineTyrant);

        var baseline = new List<string>();
        foreach (var e in AttackIntentRoller.ForecastWithOmens(enemy, seed, 6, schedule))
        {
            baseline.Add(EntryFingerprint(e));
        }

        for (var i = 0; i < 2000; i++)
        {
            var again = new List<string>();
            foreach (var e in AttackIntentRoller.ForecastWithOmens(enemy, seed, 6, schedule))
            {
                again.Add(EntryFingerprint(e));
            }
            Assert.Equal(baseline, again);
        }
    }

    // ─── 4. 番兵・引数防御 ──────────────────────────────────────────────────

    [Fact]
    public void ForecastWithOmens_NullEnemy_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AttackIntentRoller.ForecastWithOmens(null!, 1u, 3, EpochBossOmenSchedule.None));
    }

    [Fact]
    public void ForecastWithOmens_NullSchedule_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AttackIntentRoller.ForecastWithOmens(Boss(), 1u, 3, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ForecastWithOmens_NonPositiveLookahead_ReturnsEmptyBand(int lookahead)
    {
        var band = AttackIntentRoller.ForecastWithOmens(Boss(), 7u, lookahead, Schedule(1, 3));
        Assert.Empty(band);
    }

    [Fact]
    public void ForecastWithOmens_OversizedLookahead_ClampsToMaxForecastTurns()
    {
        var band = AttackIntentRoller.ForecastWithOmens(
            Boss(), 7u, AttackIntentRoller.MaxForecastTurns + 500, EpochBossOmenSchedule.None);
        Assert.Equal(AttackIntentRoller.MaxForecastTurns, band.Count);
    }
}
