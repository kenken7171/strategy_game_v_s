// =============================================================================
//  ChronicleKnights.Tests — RosterLifecycleTests.cs
// -----------------------------------------------------------------------------
//  世代交代（年送り）純粋層 RosterLifecycle.AdvanceGeneration の単体テスト。
//
//  検証観点（「1 世代 = 時間軸 1 周」の幕引き挙動を数値で固定する）:
//    1. 加齢      : 生存ユニットは skipYears ぶん齢を重ね、現役に残る
//    2. 寿命到達  : 加齢で MaxAge に届いたユニットは完全ロスト（離脱側）へ
//    3. 戦闘死    : 直前の戦闘で死亡したユニットは skipYears=0 でも離脱側へ
//    4. 順序保持  : 現役・離脱とも元のロスタ順を保つ
//    5. 集約値    : AppliedSkipYears / SurvivingCount / DepartedCount が一致
//    6. 堅牢性    : 負の年数・null ロスタは例外、空ロスタは空結果
//
//  ★ TimelineEngine.ApplyTimeSkipToRoster へ委譲した加齢ロジックを二重実装せず、
//    本テストは「加齢後の現役 / 離脱の仕分け」と集約結果のみを厳密検証する。
//  ★ 表示文字列は使わず、年齢としきい値だけで判定（ASCII キーのみ）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class RosterLifecycleTests
{
    // ─── テスト用ユニットファクトリ ────────────────────────────────────────
    //  年齢・寿命・死亡フラグだけが世代交代の判定に効くため、その 3 つを引数化する。
    //  ジョブ・名前キーは判定に無関係なので固定値を与える（ASCII のみ）。

    private static Unit MakeUnit(
        int age,
        int maxAge,
        bool isDead = false,
        JobId job = JobId.Scout)
    {
        var unit = new Unit
        {
            Id           = Guid.NewGuid(),
            Job          = job,
            Age          = age,
            MaxAge       = maxAge,
            FirstNameKey = "name-sample-roster",
            LastNameKey  = "name-family-sample",
        };
        return isDead ? unit.MarkDeadInBattle() : unit;
    }

    // ─── 1. 加齢（生存ユニットは現役に残り齢を重ねる） ─────────────────────

    [Fact]
    public void AdvanceGeneration_AgesSurvivingUnitsAndKeepsThemActive()
    {
        var roster = new List<Unit> { MakeUnit(age: 20, maxAge: 60) };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 4);

        Assert.Equal(4, result.AppliedSkipYears);
        Assert.Equal(1, result.SurvivingCount);
        Assert.Equal(0, result.DepartedCount);
        Assert.Equal(24, result.SurvivingRoster[0].Age); // 20 + 4
        Assert.True(result.DepartedRoster.IsEmpty);
    }

    // ─── 2. 寿命到達（加齢で MaxAge に届いたら完全ロスト） ─────────────────

    [Fact]
    public void AdvanceGeneration_PrunesUnitsReachingMaxAge()
    {
        // 58 + 4 = 62 >= 60 → 離脱。20 + 4 = 24 < 60 → 現役。
        var elder = MakeUnit(age: 58, maxAge: 60);
        var young = MakeUnit(age: 20, maxAge: 60);
        var roster = new List<Unit> { elder, young };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 4);

        Assert.Equal(1, result.SurvivingCount);
        Assert.Equal(1, result.DepartedCount);
        Assert.Equal(young.Id, result.SurvivingRoster[0].Id);
        Assert.Equal(elder.Id, result.DepartedRoster[0].Id);
        Assert.True(result.DepartedRoster[0].HasReachedMaxAge);
    }

    [Fact]
    public void AdvanceGeneration_BoundaryAgeEqualToMaxAge_IsDeparted()
    {
        // ちょうど MaxAge に一致 (59 + 1 = 60) でも IsRemovedFromRoster は真。
        var roster = new List<Unit> { MakeUnit(age: 59, maxAge: 60) };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 1);

        Assert.Equal(0, result.SurvivingCount);
        Assert.Equal(1, result.DepartedCount);
        Assert.Equal(60, result.DepartedRoster[0].Age);
    }

    // ─── 3. 戦闘死（skipYears=0 でも離脱側へ仕分け） ───────────────────────

    [Fact]
    public void AdvanceGeneration_PrunesBattleDeadUnitsEvenWithZeroSkip()
    {
        var dead = MakeUnit(age: 25, maxAge: 60, isDead: true);
        var alive = MakeUnit(age: 25, maxAge: 60);
        var roster = new List<Unit> { dead, alive };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 0);

        Assert.Equal(0, result.AppliedSkipYears);
        Assert.Equal(1, result.SurvivingCount);
        Assert.Equal(1, result.DepartedCount);
        Assert.Equal(dead.Id, result.DepartedRoster[0].Id);
        Assert.True(result.DepartedRoster[0].IsDead);
        // 死亡ユニットは加齢されない（据え置き）。
        Assert.Equal(25, result.DepartedRoster[0].Age);
        // 生存ユニットは skipYears=0 なので齢も据え置き。
        Assert.Equal(25, result.SurvivingRoster[0].Age);
    }

    [Fact]
    public void AdvanceGeneration_DeadUnitNotAgedWhenSkipPositive()
    {
        // 戦闘死ユニットは ApplyTimeSkipToRoster で加齢対象外（据え置き）。
        var dead = MakeUnit(age: 30, maxAge: 60, isDead: true);
        var roster = new List<Unit> { dead };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 4);

        Assert.Equal(1, result.DepartedCount);
        Assert.Equal(30, result.DepartedRoster[0].Age); // 加齢されない
    }

    // ─── 4. 順序保持（現役・離脱とも元のロスタ順を保つ） ───────────────────

    [Fact]
    public void AdvanceGeneration_PreservesRosterOrderForBothPartitions()
    {
        // 並び: 現役A, 離脱X, 現役B, 離脱Y, 現役C
        var a = MakeUnit(age: 20, maxAge: 80);            // 現役
        var x = MakeUnit(age: 30, maxAge: 60, isDead: true); // 離脱（戦闘死）
        var b = MakeUnit(age: 22, maxAge: 80);            // 現役
        var y = MakeUnit(age: 59, maxAge: 60);            // 離脱（寿命）
        var c = MakeUnit(age: 24, maxAge: 80);            // 現役
        var roster = new List<Unit> { a, x, b, y, c };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 2);

        Assert.Equal(new[] { a.Id, b.Id, c.Id },
            new[]
            {
                result.SurvivingRoster[0].Id,
                result.SurvivingRoster[1].Id,
                result.SurvivingRoster[2].Id,
            });
        Assert.Equal(new[] { x.Id, y.Id },
            new[]
            {
                result.DepartedRoster[0].Id,
                result.DepartedRoster[1].Id,
            });
    }

    // ─── 5. 堅牢性（境界・例外・空） ───────────────────────────────────────

    [Fact]
    public void AdvanceGeneration_EmptyRoster_YieldsEmptyResultWithAppliedYears()
    {
        var result = RosterLifecycle.AdvanceGeneration(new List<Unit>(), skipYears: 3);

        Assert.Equal(3, result.AppliedSkipYears);
        Assert.Equal(0, result.SurvivingCount);
        Assert.Equal(0, result.DepartedCount);
        Assert.True(result.SurvivingRoster.IsEmpty);
        Assert.True(result.DepartedRoster.IsEmpty);
    }

    [Fact]
    public void AdvanceGeneration_NegativeSkipYears_Throws()
    {
        var roster = new List<Unit> { MakeUnit(age: 20, maxAge: 60) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => RosterLifecycle.AdvanceGeneration(roster, skipYears: -1));
    }

    [Fact]
    public void AdvanceGeneration_NullRoster_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RosterLifecycle.AdvanceGeneration(null!, skipYears: 2));
    }

    [Fact]
    public void AdvanceGeneration_AllUnitsSurvive_NoDepartures()
    {
        var roster = new List<Unit>
        {
            MakeUnit(age: 20, maxAge: 80),
            MakeUnit(age: 25, maxAge: 80),
            MakeUnit(age: 30, maxAge: 80),
        };

        var result = RosterLifecycle.AdvanceGeneration(roster, skipYears: 4);

        Assert.Equal(3, result.SurvivingCount);
        Assert.Equal(0, result.DepartedCount);
        // 全員 +4 加齢されている。
        Assert.Equal(24, result.SurvivingRoster[0].Age);
        Assert.Equal(29, result.SurvivingRoster[1].Age);
        Assert.Equal(34, result.SurvivingRoster[2].Age);
    }
}
