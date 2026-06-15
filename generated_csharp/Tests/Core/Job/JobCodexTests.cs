// =============================================================================
//  ChronicleKnights — JobCodexTests.cs
// -----------------------------------------------------------------------------
//  Locks the Job Manual content trace: every job has prose, English ASCII
//  display names, and the structured passive list whose numeric values are
//  sourced from JobMaster (single SoT) — matching the TS jobs.ts data.
// =============================================================================

using System.Linq;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Job;

public class JobCodexTests
{
    [Fact]
    public void EveryJob_HasManualTextAndDisplayName()
    {
        foreach (var id in JobMaster.DisplayOrder)
        {
            var text = JobCodex.TextFor(id);
            Assert.NotNull(text);
            Assert.False(string.IsNullOrWhiteSpace(text!.Role));
            Assert.False(string.IsNullOrWhiteSpace(text.AbilitySummary));
            Assert.False(string.IsNullOrWhiteSpace(text.Usage));
            Assert.False(string.IsNullOrWhiteSpace(text.Flavor));

            var name = JobCodex.DisplayName(id);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.All(name, ch => Assert.True(ch < 128)); // ASCII only (Constitution I)
        }
    }

    [Fact]
    public void NullJob_HasNoTextAndNoPassives()
    {
        Assert.Null(JobCodex.TextFor(null));
        Assert.Empty(JobCodex.Passives(null));
    }

    [Fact]
    public void Passives_NumericValues_MatchJobMasterStats()
    {
        foreach (var id in JobMaster.DisplayOrder)
        {
            var def = JobMaster.Find(id)!;
            var passives = JobCodex.Passives(id);

            // Only non-zero numeric passives appear; their values must equal stats.
            AssertPassive(passives, "bdf", def.Stats.BattalionDefense);
            AssertPassive(passives, "sdf", def.Stats.SquadDefense);
            AssertPassive(passives, "ab",  def.Stats.InitiativeBuff);
            AssertPassive(passives, "hl",  def.Stats.TurnEndSquadHeal);
        }
    }

    [Fact]
    public void IronWallKnight_HasBattalionAndSquadGuard()
    {
        var passives = JobCodex.Passives(JobId.IronWallKnight);
        Assert.Contains(passives, p => p.Key == "bdf" && p.Value == 10);
        Assert.Contains(passives, p => p.Key == "sdf" && p.Value == 15);
    }

    [Fact]
    public void Sniper_HasSpecialDoubleStrikeWithNullValue()
    {
        var passives = JobCodex.Passives(JobId.Sniper);
        var special = Assert.Single(passives, p => p.Key == "special-double-strike");
        Assert.Null(special.Value);
    }

    private static void AssertPassive(
        System.Collections.Immutable.ImmutableArray<JobPassiveLine> passives,
        string key,
        int statValue)
    {
        var line = passives.FirstOrDefault(p => p.Key == key);
        if (statValue > 0)
        {
            Assert.NotNull(line);
            Assert.Equal(statValue, line!.Value);
        }
        else
        {
            Assert.Null(line); // zero-valued passives are omitted
        }
    }
}
