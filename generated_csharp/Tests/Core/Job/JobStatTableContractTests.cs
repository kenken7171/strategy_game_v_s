// =============================================================================
//  ChronicleKnights.Tests — Core/Job/JobStatTableContractTests.cs
// -----------------------------------------------------------------------------
//  Pins the *data* shown by the 人事フェーズ（旅団組合）のユニットリスト／スカウトの
//  パラメータ表へ。表は UI（MarriageUI）が描くが、その中身は純粋 Core から決まる:
//    - 共通表 (HP / 前 / 後 / 速 / 総合): JobMaster.All[job].Stats と TargetRating。
//    - 固有表: JobCodex.Passives(job) の「数値を持つ」パッシブのみ（二の矢のような
//      binary パッシブ＝Value==null は表に出さない）。
//  これらを Core で固定しておくことで、UI 表の中身（列・数値）が静かに壊れない。
//  すべて Godot 非依存ゆえ xUnit で実行できる。
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Job;

public sealed class JobStatTableContractTests
{
    // ─── 共通表 (HP / 前 / 後 / 速 / 総合) ─────────────────────────────────

    [Fact]
    public void CommonStats_ArePresentAndPositive_ForEveryJob()
    {
        foreach (var id in JobMaster.DisplayOrder)
        {
            var s = JobMaster.All[id].Stats;
            Assert.True(s.MaxHp > 0, $"{id} MaxHp");
            Assert.True(s.Speed > 0, $"{id} Speed");
            // 前後いずれかは必ず正（攻撃手段ゼロのジョブは存在しない）。
            Assert.True(s.FrontAttack > 0 || s.RearAttack > 0, $"{id} attack");
            Assert.True(JobMaster.TargetRating[id] > 0, $"{id} TargetRating");
        }
    }

    [Fact]
    public void TargetRating_MatchesPublishedFormula_ForEveryJob()
    {
        // 総合 = floor(MaxHp / HpRatingDivisor + max(前,後) + 速) + RoleBonus。
        foreach (var id in JobMaster.DisplayOrder)
        {
            var def = JobMaster.All[id];
            var s = def.Stats;
            var expected = (int)System.Math.Floor(
                s.MaxHp / JobMaster.HpRatingDivisor
                + System.Math.Max(s.FrontAttack, s.RearAttack)
                + s.Speed) + def.RoleBonus;

            Assert.Equal(expected, JobMaster.TargetRating[id]);
            Assert.Equal(JobMaster.CalculateTargetRating(id), JobMaster.TargetRating[id]);
        }
    }

    [Theory]
    [InlineData(JobId.IronWallKnight, 140)]
    [InlineData(JobId.HeavyInfantry, 145)]
    [InlineData(JobId.StandardBearer, 145)]
    [InlineData(JobId.Tactician, 144)]
    [InlineData(JobId.Medic, 145)]
    [InlineData(JobId.Sniper, 146)]
    [InlineData(JobId.Sorcerer, 143)]
    [InlineData(JobId.Scout, 148)]
    public void TargetRating_HasExactExpectedValue(JobId job, int expected)
    {
        // 総合（右下の値）を実数で固定し、ステ／除数／RoleBonus の意図せぬ漂流を検出する。
        Assert.Equal(expected, JobMaster.TargetRating[job]);
    }

    // ─── 固有表 (JobCodex.Passives の数値パッシブのみ) ────────────────────

    /// <summary>固有表に出る列（短見出しの素になる Key と値）を Core から再現する。</summary>
    private static IReadOnlyList<(string Key, int Value)> NumericUniqueStats(JobId job)
        => JobCodex.Passives(job)
            .Where(p => p.Value.HasValue)
            .Select(p => (p.Key, p.Value!.Value))
            .ToList();

    [Fact]
    public void UniqueTable_OmitsBinaryPassives()
    {
        // 二の矢（ConsecutiveStrike）は Value==null ゆえ数値固有表に出ない。
        foreach (var id in JobMaster.DisplayOrder)
        {
            Assert.DoesNotContain(NumericUniqueStats(id), c => c.Key == "special-double-strike");
        }
    }

    [Fact]
    public void UniqueTable_ValuesMatchJobMasterStats()
    {
        foreach (var id in JobMaster.DisplayOrder)
        {
            var s = JobMaster.All[id].Stats;
            var map = NumericUniqueStats(id).ToDictionary(c => c.Key, c => c.Value);

            AssertSlot(map, "bdf", s.BattalionDefense);
            AssertSlot(map, "sdf", s.SquadDefense);
            AssertSlot(map, "ab", s.InitiativeBuff);
            AssertSlot(map, "hl", s.TurnEndSquadHeal);
        }
    }

    [Theory]
    [InlineData(JobId.IronWallKnight, "bdf:10", "sdf:15")]
    [InlineData(JobId.HeavyInfantry, "sdf:10")]
    [InlineData(JobId.StandardBearer, "sdf:5", "ab:40")]
    [InlineData(JobId.Tactician, "ab:20")]
    [InlineData(JobId.Medic, "hl:30")]
    public void UniqueTable_HasExpectedColumns_ForJobsThatHaveThem(JobId job, params string[] expected)
    {
        var actual = NumericUniqueStats(job)
            .Select(c => $"{c.Key}:{c.Value}")
            .ToArray();
        Assert.Equal(expected, actual); // 順序込みで固定（表の列順）
    }

    [Theory]
    [InlineData(JobId.Sniper)]   // 二の矢のみ＝数値固有なし
    [InlineData(JobId.Sorcerer)] // 純アタッカー
    [InlineData(JobId.Scout)]    // 純アタッカー
    public void UniqueTable_IsEmpty_ForPureAttackers(JobId job)
    {
        // これらは固有表ごと省略される（UI は count==0 で描かない）。
        Assert.Empty(NumericUniqueStats(job));
    }

    private static void AssertSlot(IReadOnlyDictionary<string, int> map, string key, int statValue)
    {
        if (statValue > 0)
        {
            Assert.True(map.TryGetValue(key, out var v), $"missing {key}");
            Assert.Equal(statValue, v);
        }
        else
        {
            Assert.DoesNotContain(key, map.Keys);
        }
    }
}
