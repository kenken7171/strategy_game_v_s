// =============================================================================
//  ChronicleKnights.Tests — InheritedBonusTests.cs
// -----------------------------------------------------------------------------
//  子の血統継承ボーナス（両親の良いとこ取り 50%）の純粋ロジック検証。
//    - MarriageService.CalculateInheritedBonus: 各ステで
//        max(0, max(父,母) - 継承ジョブ値) * 0.5 を加算ボーナスにする。
//    - UnitStatProfile.EffectiveStats(.., lineageBonus): 素値へ合算してから Lv×加齢を掛ける。
//    - ExecuteManualMarriage: 生まれた子に InheritedBonus が刻まれる。
//    - SaveSerializer: InheritedBonus がラウンドトリップで欠落しない（v8）。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Persistence;
using ChronicleKnights.Core.Units;
using ChronicleKnights.Tests.TestSupport;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class InheritedBonusTests
{
    // ─── 算出ルール ───────────────────────────────────────────────────────

    [Fact]
    public void CalculateInheritedBonus_TakesHalfOfTheHigherParentSurplus()
    {
        // 子=重装歩兵(RA20) を継承、父=重装歩兵 × 母=呪術師(RA120)。
        // RA だけ母が高い: surplus 100 → 50% = +50。他項目は子(重装歩兵)が両親以上で 0。
        var bonus = MarriageService.CalculateInheritedBonus(
            JobId.HeavyInfantry, JobId.HeavyInfantry, JobId.Sorcerer);

        Assert.NotNull(bonus);
        Assert.Equal(50, bonus!.RearAttack);   // round((120-20)*0.5)
        Assert.Equal(0, bonus.FrontAttack);     // 70 vs 10 → 子(70)が高い
        Assert.Equal(0, bonus.MaxHp);           // 300 vs 40 → 子(300)が高い
        Assert.Equal(0, bonus.SquadDefense);    // 10 vs 0 → 子(10)が高い
    }

    [Fact]
    public void CalculateInheritedBonus_IsSymmetricForTheInheritedSide()
    {
        // 同じ両親でも子=呪術師(HP40/FA10) を継承すると、重装歩兵側の高ステを 50% 受ける。
        var bonus = MarriageService.CalculateInheritedBonus(
            JobId.Sorcerer, JobId.HeavyInfantry, JobId.Sorcerer);

        Assert.NotNull(bonus);
        Assert.Equal(130, bonus!.MaxHp);        // round((300-40)*0.5)
        Assert.Equal(30, bonus.FrontAttack);    // round((70-10)*0.5)
        Assert.Equal(5, bonus.SquadDefense);    // round((10-0)*0.5)
        Assert.Equal(0, bonus.RearAttack);      // 120 vs 20 → 子(120)が高い
    }

    [Fact]
    public void CalculateInheritedBonus_SameJobParents_IsNull()
    {
        // 両親が同職なら差分ゼロ → ボーナスなし（null）。
        var bonus = MarriageService.CalculateInheritedBonus(
            JobId.Sniper, JobId.Sniper, JobId.Sniper);

        Assert.Null(bonus);
    }

    // ─── 実効ステへの合流（Lv×加齢の前に加算） ─────────────────────────────

    [Fact]
    public void EffectiveStats_AddsLineageBonusBeforeScaling_AtPrimeEqualsBasePlusBonus()
    {
        var baseStats = JobMaster.Find(JobId.HeavyInfantry)!.Stats; // RA20
        var bonus = MarriageService.CalculateInheritedBonus(
            JobId.HeavyInfantry, JobId.HeavyInfantry, JobId.Sorcerer); // RA +50

        // 全盛期(age30)+Lv1 ＝ 係数 1.0 ＝ 素値+ボーナスがそのまま出る。
        var eff = UnitStatProfile.EffectiveStats(baseStats, level: 1, age: 30, lineageBonus: bonus);
        Assert.Equal(70, eff.RearAttack);                 // 20 + 50
        Assert.Equal(baseStats.FrontAttack, eff.FrontAttack); // ボーナス 0 の項は素値のまま
    }

    [Fact]
    public void EffectiveStats_LineageBonus_AlsoScalesWithLevelAndDecline()
    {
        var baseStats = JobMaster.Find(JobId.HeavyInfantry)!.Stats;
        var bonus = MarriageService.CalculateInheritedBonus(
            JobId.HeavyInfantry, JobId.HeavyInfantry, JobId.Sorcerer); // RA +50

        var prime   = UnitStatProfile.EffectiveStats(baseStats, 1, 30, bonus); // 70
        var decline = UnitStatProfile.EffectiveStats(baseStats, 1, 60, bonus); // 70 * 0.88^15

        Assert.True(decline.RearAttack < prime.RearAttack, "lineage bonus should decline with age");
        Assert.True(decline.RearAttack >= 1, "positive effective stat never floors to 0");
    }

    // ─── 婚姻で生まれた子に刻まれる ─────────────────────────────────────────

    [Fact]
    public void ExecuteManualMarriage_StampsInheritedBonus_OnChild()
    {
        var fatherId = Guid.NewGuid();
        var motherId = Guid.NewGuid();

        // 自然婚姻（相互好感度 >= しきい値）でコスト 0。父=重装歩兵 / 母=呪術師。
        var father = MakeUnit(JobId.HeavyInfantry, Gender.Male, fatherId) with
        {
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty.Add(motherId, 200),
        };
        var mother = MakeUnit(JobId.Sorcerer, Gender.Female, motherId) with
        {
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty.Add(fatherId, 200),
        };
        var economy = PointsEconomy.CreateInitial();
        // 子のジョブを重装歩兵で固定し、母(呪術師)の RA を 50% 受ける想定を決定論化。
        var newborn = new NewbornSpec { MaxAge = 60, OverrideJob = JobId.HeavyInfantry };

        var result = MarriageService.ExecuteManualMarriage(
            economy, father, mother, newborn, new Random(1));

        Assert.NotNull(result.Child.InheritedBonus);
        Assert.Equal(50, result.Child.InheritedBonus!.RearAttack);

        // 全盛期まで育った子の実効 RA は素 20 + ボーナス 50 = 70。
        var grown = result.Child with { Age = 30 };
        Assert.Equal(70, UnitStatProfile.Resolve(grown)!.RearAttack);
    }

    // ─── 永続化（v8）でラウンドトリップ ────────────────────────────────────

    [Fact]
    public void Roundtrip_PreservesInheritedBonus()
    {
        var state = SampleData.BuildState();
        var childId = Guid.NewGuid();
        var child = MakeUnit(JobId.HeavyInfantry, Gender.Male, childId) with
        {
            Age = 10,
            InheritedBonus = new JobStats
            {
                MaxHp = 0, Speed = 0, FrontAttack = 0, RearAttack = 50,
                BattalionDefense = 0, SquadDefense = 0, InitiativeBuff = 0, TurnEndSquadHeal = 0,
            },
        };

        var json = SaveSerializer.Serialize(
            state.Economy, state.Timeline, new[] { child }, state.ChronicleLog);
        var loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded);
        var restored = loaded!.Roster.Single(u => u.Id == childId);
        Assert.NotNull(restored.InheritedBonus);
        Assert.Equal(50, restored.InheritedBonus!.RearAttack);
        Assert.Equal(0, restored.InheritedBonus.MaxHp);
    }

    private static Unit MakeUnit(JobId job, Gender gender, Guid id) => new()
    {
        Id           = id,
        Job          = job,
        Age          = 30,
        MaxAge       = 60,
        FirstNameKey = "name-test",
        LastNameKey  = "name-test-family",
        Gender       = gender,
    };
}
