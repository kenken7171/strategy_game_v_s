// =============================================================================
//  ChronicleKnights.Tests — ProphecyTimelineContractTests.cs
// -----------------------------------------------------------------------------
//  拠点の予言オーバーレイ（ProphecyTimelineOverlay）が時間の矢と章ボスマーカーを描くために
//  読む「年ベースの真実」の契約を純粋層で固定する。
//
//  ★ なぜ純粋層なのか:
//    ProphecyTimelineOverlay は Godot.Control、TimelineChanged / CurrentTimeline は
//    Godot.Node（ChronicleGlobal）ゆえテストプロジェクト（Core/** のみコンパイル）から直接は
//    叩けない。購読ライフサイクル（_Ready で TimelineChanged 購読 → 年次進行で RenderAll 再描画、
//    _ExitTree で完全解除、_timelineNodes 台帳の全 QueueFree で更地化）は論理検証で担保し、
//    本テストは「オーバーレイが配置するマーカーの年集合」と「年→時間の矢への写像」を包囲する。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Linq;
using ChronicleKnights.Core.Chronicle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class ProphecyTimelineContractTests
{
    /// <summary>オーバーレイ OmenThreshold と同値（残り 10 ターン以内で前兆カードを出す契約）。</summary>
    private const int OmenCardThreshold = 10;

    [Fact]
    public void TimelineSpan_IsYearOneToHundred()
    {
        // 時間の矢（hub-view-timeline-progress）の Min/Max の根拠。
        Assert.Equal(1, ChronicleTimelineConfig.FirstYear);
        Assert.Equal(100, ChronicleTimelineConfig.TotalYears);
    }

    [Fact]
    public void BossMarkerYears_AreExactlyTheFourEpochBossYears_Ascending()
    {
        // オーバーレイは Epochs を 1 周し、各章の BossYear 位置へマーカーを 1 本ずつ置く。
        var bossYears = ChronicleTimelineConfig.Epochs.Select(epoch => epoch.BossYear).ToArray();

        Assert.Equal(new[] { 25, 50, 75, 100 }, bossYears);
    }

    [Fact]
    public void TimelineFraction_AnchorsAndClampsAndIsMonotonic()
    {
        // 起点は左端（0.0）、終焉ボス（100 年）は右端（1.0）。
        Assert.Equal(0.0, Fraction(ChronicleTimelineConfig.FirstYear), 5);
        Assert.Equal(1.0, Fraction(ChronicleTimelineConfig.TotalYears), 5);

        // 範囲外はクランプ（時間の矢からはみ出さない）。
        Assert.Equal(0.0, Fraction(-10), 5);
        Assert.Equal(1.0, Fraction(9999), 5);

        // 章ボス位置は左から右へ単調増加（25 < 50 < 75 < 100）。
        Assert.True(Fraction(25) < Fraction(50));
        Assert.True(Fraction(50) < Fraction(75));
        Assert.True(Fraction(75) < Fraction(100));
    }

    [Fact]
    public void OmenSchedule_WithinThreshold_HeraldsApproachingBoss()
    {
        // 20 年: 次の章ボス（25 年）まで残り 5 ターン → 前兆カード（hub-view-omen-card）が出る圏内。
        var omen = ChronicleTimelineConfig.BuildOmenScheduleForYear(20);

        Assert.True(omen.BossApproaching);
        Assert.Equal(5, omen.TurnsUntilArrival);
        Assert.True(omen.TurnsUntilArrival <= OmenCardThreshold);
    }

    [Fact]
    public void OmenSchedule_ImminentWindow_EntersForewarnFlash()
    {
        // 23 年: 残り 2 ターン ≤ 先行告知窓 → 秒読み段階（緋色フラッシュ＋コンソール警告）。
        var omen = ChronicleTimelineConfig.BuildOmenScheduleForYear(23);

        Assert.True(omen.BossApproaching);
        Assert.Equal(2, omen.TurnsUntilArrival);
        Assert.True(omen.TurnsUntilArrival <= omen.ForewarnLeadTurns);
    }

    [Fact]
    public void OmenSchedule_AfterFinalBoss_IsInactive_NoCard()
    {
        // 100 年（終焉ボス年）以降は接近する章ボスが無い → 前兆カードは一切出ない。
        var omen = ChronicleTimelineConfig.BuildOmenScheduleForYear(100);

        Assert.False(omen.BossApproaching);
    }

    /// <summary>
    /// ProphecyTimelineOverlay.TimelineFraction と同一式のレプリカ（契約固定用）。
    /// f(year) = (year - FirstYear) / (TotalYears - FirstYear)、範囲外は [0,1] へクランプ。
    /// </summary>
    private static double Fraction(int year)
    {
        double span = ChronicleTimelineConfig.TotalYears - ChronicleTimelineConfig.FirstYear;
        if (span <= 0.0)
        {
            return 0.0;
        }

        var fraction = (year - ChronicleTimelineConfig.FirstYear) / span;
        return Math.Clamp(fraction, 0.0, 1.0);
    }
}
