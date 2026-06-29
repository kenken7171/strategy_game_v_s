// =============================================================================
//  ChronicleKnights.Tests — Core/Managers/ScoutCandidateTests.cs
// -----------------------------------------------------------------------------
//  Pure-layer tests for the scout candidate pool (人事フェーズ＝旅団組合のスカウト
//  タブが並べる外様候補と TargetRating 連動コスト). Covers:
//    - candidate count (>= minimum 3, parameterized) and clamping
//    - cost == ComputeScoutCost(job) and floored at MinScoutCost (rating-linked)
//    - determinism under a seeded Random (job/age/name/cost reproduce; Ids do not)
//    - distinct names within a pool
//    - TryRecruit: success spends exact cost + adds the *same* candidate unit;
//      insufficient balance / negative cost return null
//  ScoutService is Godot-independent, so these run headless under xUnit.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public sealed class ScoutCandidateTests
{
    private static IReadOnlySet<string> NoUsedNames() => new HashSet<string>(StringComparer.Ordinal);

    // ─── 候補数 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void CreateCandidatePool_ReturnsRequestedCount_WhenAtOrAboveMinimum(int count)
    {
        var pool = ScoutService.CreateCandidatePool(count, new Random(1), NoUsedNames());
        Assert.Equal(count, pool.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CreateCandidatePool_ClampsBelowMinimumToThree(int count)
    {
        var pool = ScoutService.CreateCandidatePool(count, new Random(1), NoUsedNames());
        Assert.Equal(ScoutService.MinScoutCandidateCount, pool.Count);
        Assert.Equal(3, ScoutService.MinScoutCandidateCount);
    }

    [Fact]
    public void DefaultCandidateCount_IsAtLeastThree()
    {
        Assert.True(ScoutService.DefaultScoutCandidateCount >= ScoutService.MinScoutCandidateCount);
    }

    // ─── コスト（TargetRating 連動） ───────────────────────────────────────

    [Fact]
    public void EveryCandidate_HasCostEqualToComputeScoutCost_AndAtLeastFloor()
    {
        var pool = ScoutService.CreateCandidatePool(6, new Random(42), NoUsedNames());
        foreach (var c in pool)
        {
            Assert.Equal(ScoutService.ComputeScoutCost(c.Unit.Job), c.Cost);
            Assert.True(c.Cost >= ScoutService.MinScoutCost);
        }
    }

    [Fact]
    public void ComputeScoutCost_MatchesTargetRatingFormula_ForEveryJob()
    {
        foreach (var job in JobMaster.DisplayOrder)
        {
            var rating = JobMaster.TargetRating[job];
            var expected = Math.Max(
                ScoutService.MinScoutCost,
                (int)Math.Ceiling(rating / (double)ScoutService.ScoutCostDivisor));
            Assert.Equal(expected, ScoutService.ComputeScoutCost(job));
        }
    }

    [Fact]
    public void ComputeScoutCost_IsMonotonicInTargetRating()
    {
        // 強い職（高 TargetRating）ほどコストが安くならない（rating 連動の単調性）。
        var jobs = JobMaster.DisplayOrder.OrderBy(j => JobMaster.TargetRating[j]).ToArray();
        for (int i = 1; i < jobs.Length; i++)
        {
            Assert.True(ScoutService.ComputeScoutCost(jobs[i]) >= ScoutService.ComputeScoutCost(jobs[i - 1]));
        }
    }

    // ─── 決定論・名前重複回避 ─────────────────────────────────────────────

    [Fact]
    public void CreateCandidatePool_IsDeterministic_ForSameSeed()
    {
        var a = ScoutService.CreateCandidatePool(4, new Random(123), NoUsedNames());
        var b = ScoutService.CreateCandidatePool(4, new Random(123), NoUsedNames());

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            // Id は Guid.NewGuid で常に異なるが、job/age/name/cost はシードで再現する。
            Assert.Equal(a[i].Unit.Job, b[i].Unit.Job);
            Assert.Equal(a[i].Unit.Age, b[i].Unit.Age);
            Assert.Equal(a[i].Unit.MaxAge, b[i].Unit.MaxAge);
            Assert.Equal(a[i].Unit.FirstNameKey, b[i].Unit.FirstNameKey);
            Assert.Equal(a[i].Unit.LastNameKey, b[i].Unit.LastNameKey);
            Assert.Equal(a[i].Cost, b[i].Cost);
        }
    }

    [Fact]
    public void CreateCandidatePool_HasDistinctFirstNameKeys()
    {
        var pool = ScoutService.CreateCandidatePool(8, new Random(7), NoUsedNames());
        var names = pool.Select(c => c.Unit.FirstNameKey).ToArray();
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void CreateCandidatePool_AvoidsAlreadyUsedNames()
    {
        // 先に一度払い出したキーを除外集合に渡すと、新プールはそれらを再使用しない。
        var first = ScoutService.CreateCandidatePool(3, new Random(99), NoUsedNames());
        var used = first.Select(c => c.Unit.FirstNameKey).ToHashSet(StringComparer.Ordinal);

        var second = ScoutService.CreateCandidatePool(3, new Random(99), used);
        foreach (var c in second)
        {
            Assert.DoesNotContain(c.Unit.FirstNameKey, used);
        }
    }

    // ─── 採用（TryRecruit） ───────────────────────────────────────────────

    [Fact]
    public void TryRecruit_Success_SpendsExactCost_AddsSameCandidateUnit()
    {
        var pool = ScoutService.CreateCandidatePool(3, new Random(5), NoUsedNames());
        var candidate = pool[0];
        var economy = PointsEconomy.CreateInitial().EarnDirect(candidate.Cost + 10);
        var roster = ImmutableList<Unit>.Empty;

        var result = ScoutService.TryRecruit(economy, roster, candidate);

        Assert.NotNull(result);
        Assert.Equal(economy.CurrentBalance - candidate.Cost, result!.NewEconomy.CurrentBalance);
        Assert.Single(result.NewRoster);
        Assert.Equal(candidate.Unit.Id, result.Recruit.Id);
        Assert.Equal(candidate.Unit.Id, result.NewRoster[0].Id);
    }

    [Fact]
    public void TryRecruit_InsufficientBalance_ReturnsNull_AndDoesNotMutate()
    {
        var pool = ScoutService.CreateCandidatePool(3, new Random(5), NoUsedNames());
        var candidate = pool[0];
        var economy = PointsEconomy.CreateInitial(); // 残高 0 < cost(>=3)
        var roster = ImmutableList<Unit>.Empty;

        var result = ScoutService.TryRecruit(economy, roster, candidate);

        Assert.Null(result);
        Assert.Equal(0, economy.CurrentBalance); // 入力は不変
        Assert.Empty(roster);
    }

    [Fact]
    public void TryRecruit_NegativeCost_ReturnsNull()
    {
        var pool = ScoutService.CreateCandidatePool(3, new Random(5), NoUsedNames());
        var bad = pool[0] with { Cost = -1 };
        var economy = PointsEconomy.CreateInitial().EarnDirect(100);

        var result = ScoutService.TryRecruit(economy, ImmutableList<Unit>.Empty, bad);

        Assert.Null(result);
    }
}
