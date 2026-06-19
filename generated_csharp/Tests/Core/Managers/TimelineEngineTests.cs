// =============================================================================
//  ChronicleKnights.Tests — TimelineEngineTests.cs
// -----------------------------------------------------------------------------
//  Locks "the calendar year advances by the prophecy's SkipYears" — the fix for
//  "○ years elapse did not work; it was 1 year per turn". Turn (the calendar
//  year) must jump by the years passed to AdvanceToNextTurn (>= 1), so the year,
//  aging and income all move by the same amount.
//
//  ASCII only (Constitution I). Godot-independent / deterministic (seeded Random).
// =============================================================================

using System;
using ChronicleKnights.Core.Managers;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class TimelineEngineTests
{
    [Theory]
    [InlineData(1, 1)]   // +1 year -> Turn +1
    [InlineData(2, 2)]
    [InlineData(3, 3)]   // the "+3 years" prophecy actually advances the year by 3
    [InlineData(4, 4)]
    [InlineData(0, 1)]   // defensive: never stalls -> at least +1
    public void AdvanceToNextTurn_AdvancesYearBy_GivenYears(int years, int expectedDelta)
    {
        var rng = new Random(7);
        var engine = TimelineEngine.CreateInitial(TimelineEngine.DefaultGenerator, rng);
        var startTurn = engine.Turn; // CreateInitial seeds year 1

        var next = engine.AdvanceToNextTurn(TimelineEngine.DefaultGenerator, rng, years);

        Assert.Equal(startTurn + expectedDelta, next.Turn);
        Assert.Equal(TimelineEngine.OptionsPerTurn, next.CurrentOptions.Length);
    }

    [Fact]
    public void AdvanceToNextTurn_DefaultsToOneYear()
    {
        var rng = new Random(7);
        var engine = TimelineEngine.CreateInitial(TimelineEngine.DefaultGenerator, rng);

        var next = engine.AdvanceToNextTurn(TimelineEngine.DefaultGenerator, rng);

        Assert.Equal(engine.Turn + 1, next.Turn);
    }

    [Fact]
    public void AdvanceToNextTurn_IsCumulative_OverMultipleGenerations()
    {
        var rng = new Random(7);
        var engine = TimelineEngine.CreateInitial(TimelineEngine.DefaultGenerator, rng);
        var startTurn = engine.Turn; // 1

        // Three generations of +3 / +2 / +4 years -> calendar advances by 9 total.
        engine = engine.AdvanceToNextTurn(TimelineEngine.DefaultGenerator, rng, 3);
        engine = engine.AdvanceToNextTurn(TimelineEngine.DefaultGenerator, rng, 2);
        engine = engine.AdvanceToNextTurn(TimelineEngine.DefaultGenerator, rng, 4);

        Assert.Equal(startTurn + 9, engine.Turn);
    }
}
