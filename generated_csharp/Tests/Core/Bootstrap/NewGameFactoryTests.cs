// =============================================================================
//  ChronicleKnights.Tests — NewGameFactoryTests.cs
// -----------------------------------------------------------------------------
//  新規ゲーム初期状態ファクトリ（NewGameFactory）の純粋ロジック単体テスト。
//  「初期パーティーの役割保証（リセマラ抑止のバランス制約）」が、どのシードでも
//  構造的に満たされることを固定する:
//    - 必ず 9 名（大隊規模）
//    - 前衛 >= 2 / 回復 >= 1 / 支援 >= 1（詰み開幕の排除）
//    - 同一ジョブは MaxSameJob 以下（同職ダブり過多の排除）
//    - 名前キーは創設メンバー間で重複しない
//    - 同一シードは同一構成（決定論・Id を除く）
//
//  Godot 非依存の純粋層テスト（開発憲法④：Random は引数注入）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Bootstrap;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Bootstrap;

public class NewGameFactoryTests
{
    // ─── 役割保証（全シードで成立すること） ───────────────────────────────────

    [Fact]
    public void Create_EverySeed_SatisfiesRoleGuaranteesAndJobCap()
    {
        for (var seed = 0; seed < 600; seed++)
        {
            var party = NewGameFactory.Create(new Random(seed)).Roster;

            Assert.Equal(NewGameFactory.InitialRosterSize, party.Count);

            var frontline = party.Count(u => NewGameFactory.IsFrontLineJob(u.Job));
            var healers   = party.Count(u => NewGameFactory.IsHealerJob(u.Job));
            var support   = party.Count(u => NewGameFactory.IsSupportJob(u.Job));

            Assert.True(frontline >= NewGameFactory.GuaranteedFrontLine,
                $"seed {seed}: frontline={frontline}");
            Assert.True(healers >= NewGameFactory.GuaranteedHealer,
                $"seed {seed}: healers={healers}");
            Assert.True(support >= NewGameFactory.GuaranteedSupport,
                $"seed {seed}: support={support}");

            // 同職ダブりは上限以下。
            foreach (var group in party.GroupBy(u => u.Job))
            {
                Assert.True(group.Count() <= NewGameFactory.MaxSameJob,
                    $"seed {seed}: job {group.Key} appears {group.Count()} times");
            }
        }
    }

    [Fact]
    public void Create_FirstNameKeys_AreUniqueAcrossFoundingMembers()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var party = NewGameFactory.Create(new Random(seed)).Roster;
            var keys = party.Select(u => u.FirstNameKey).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }
    }

    [Fact]
    public void Create_IsDeterministic_ForSameSeed()
    {
        var a = NewGameFactory.Create(new Random(2024)).Roster;
        var b = NewGameFactory.Create(new Random(2024)).Roster;

        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Job, b[i].Job);
            Assert.Equal(a[i].FirstNameKey, b[i].FirstNameKey);
            Assert.Equal(a[i].Gender, b[i].Gender);
            Assert.Equal(a[i].Age, b[i].Age);
        }
    }

    [Fact]
    public void Create_InitialEconomy_HasFoundingBalance()
    {
        var seed = NewGameFactory.Create(new Random(7));
        Assert.Equal(NewGameFactory.InitialPointsBalance, seed.Economy.CurrentBalance);
    }

    // ─── 役割判定（データ駆動・職名ハードコードなし）の妥当性 ───────────────────

    [Theory]
    [InlineData(JobId.IronWallKnight)]
    [InlineData(JobId.HeavyInfantry)]
    public void IsFrontLineJob_TrueForDedicatedFront(JobId job)
        => Assert.True(NewGameFactory.IsFrontLineJob(job));

    [Theory]
    [InlineData(JobId.Medic)]
    [InlineData(JobId.Sniper)]
    [InlineData(JobId.Sorcerer)]
    [InlineData(JobId.StandardBearer)]
    public void IsFrontLineJob_FalseForNonDedicatedFront(JobId job)
        => Assert.False(NewGameFactory.IsFrontLineJob(job));

    [Fact]
    public void IsHealerJob_OnlyMedic()
    {
        var healers = Enum.GetValues<JobId>().Where(NewGameFactory.IsHealerJob).ToList();
        Assert.Equal(new[] { JobId.Medic }, healers);
    }

    [Fact]
    public void IsSupportJob_StandardBearerAndTactician()
    {
        var support = Enum.GetValues<JobId>().Where(NewGameFactory.IsSupportJob).ToHashSet();
        Assert.Contains(JobId.StandardBearer, support);
        Assert.Contains(JobId.Tactician, support);
        Assert.DoesNotContain(JobId.Sniper, support);
    }
}
