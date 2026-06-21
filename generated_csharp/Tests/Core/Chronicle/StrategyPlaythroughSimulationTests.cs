// =============================================================================
//  ChronicleKnights.Tests — StrategyPlaythroughSimulationTests.cs
// -----------------------------------------------------------------------------
//  「戦闘難易度を切り離した 100 年フロー検証」シミュ。旅団長の指定方針を実 Core
//  サービスで直接 orchestrate し、ループ全体（予言選択 → 行動 → 加齢 → 世代交代 →
//  経済・加入・婚姻・除外・装備）が最後まで回るかと、その軌跡・成長を観測する。
//
//  ★ 実 Core サービスを直結（模型ではない）:
//    NewGameFactory / ProphecyMaster / RestService / RosterLifecycle / ScoutService /
//    MarriageService / ShopService / BattleManager / PointsEconomy / TimelineEngine。
//    Godot 非依存・決定論。
//
//  ★ 2 つの方針（旅団長指定。ポイントの取り合いゆえ「いいとこ取り」は不可）:
//    - MemberAndEquip（増員＋装備重視）: ティア最高カード／毎ターン婚姻・スカウト／装備を購入・強化。
//      → 家計はほぼ横ばい（稼ぎを増員＋装備に回し切る）。
//    - Economy（家計優先）: RewardPoints を優先選択／婚姻なし／スカウトは最低限（戦力フロアの補充のみ）／
//      装備は無償ドロップのみ拾い購入はしない → 残高が伸びる。
//
//  ★ 共通ルール（実機準拠）:
//    - 敵ステ全 1 ＝ 戦闘は自動勝利・損耗ゼロ。出撃・上限カウント・除外・婚姻親は入団済（Age>=15）のみ。
//    - 15 歳未満の子は「成長中」＝入団せず戦闘にも出ない（別プールで加齢を待つ）。
//
//  ★ 成長について（C# 版の事実）:
//    ユニットの戦闘ステータスはジョブ固定値（JobMaster）で、Level でも年齢でも増えない。Level は引退/
//    ラストヒット進行のゲートに過ぎず戦闘力に乗らない。よって「基本→最終」の成長は装備（EquipmentBonus）
//    だけが生む。本シミュは最終旅団の base（素のジョブ値）と effective（装備込み）の差分を出力する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ChronicleKnights.Core.Bootstrap;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Shop;
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class StrategyPlaythroughSimulationTests
{
    /// <summary>旅団（入団済の現役）の上限＝旅団長運用上限 15 名（出撃枠の大隊 9 とは別概念）。</summary>
    private const int RosterCap = 15;

    /// <summary>家計優先時に維持する最低戦力（これを割ったらスカウトで補充。ブランド存続と戦闘成立のため）。</summary>
    private const int EconomyFloor = 9;

    /// <summary>入団・出撃可能となる成人年齢。15 歳未満は成長中。</summary>
    private const int AdultAge = 15;

    private const int ScoutCost = 3;
    private const int BattleVictoryReward = 7;
    private const int ChildMinLifespan = 55;
    private const int ChildMaxLifespan = 75;

    private enum PlaythroughPolicy { MemberAndEquip, Economy }

    // ─── 観測レポート ─────────────────────────────────────────────────────

    private sealed record PlaythroughReport
    {
        public required int FinalYear { get; init; }
        public required int Turns { get; init; }
        public required int Battles { get; init; }
        public required int RestTurns { get; init; }
        public required int FinalBalance { get; init; }
        public required int TotalEarned { get; init; }
        public required int TotalSpent { get; init; }
        public required int Births { get; init; }
        public required int ScoutHires { get; init; }
        public required int ProphecyRecruits { get; init; }
        public required int AgeDeaths { get; init; }
        public required int Evictions { get; init; }
        public required int EquipBought { get; init; }
        public required int EquipDropped { get; init; }
        public required int EquipUpgraded { get; init; }
        public required int PeakEnlisted { get; init; }
        public required int FinalEnlisted { get; init; }
        public required int FinalChildren { get; init; }
        public required int MaxLineageDepth { get; init; }
        public required ImmutableArray<string> Log { get; init; }
        public required ImmutableArray<string> EnlistedLines { get; init; }
        public required ImmutableArray<string> GrowthLines { get; init; }
    }

    // ─── 判定ヘルパ ───────────────────────────────────────────────────────

    private static bool IsEnlisted(Unit u) => u.IsAlive && u.Age >= AdultAge;
    private static bool IsGrowingChild(Unit u) => u.IsAlive && u.Age < AdultAge;
    private static int EnlistedCount(IReadOnlyList<Unit> roster) => roster.Count(IsEnlisted);

    /// <summary>予言選択。Economy は RewardPoints を最優先（同種内＋全体でレア度→効果量）。それ以外はティア最高。</summary>
    private static Prophecy PickProphecy(ImmutableArray<Prophecy> options, PlaythroughPolicy policy)
    {
        if (policy == PlaythroughPolicy.Economy)
        {
            var reward = options.Where(p => p.Kind == ProphecyKind.RewardPoints)
                                .OrderByDescending(p => (int)p.Rarity).ThenByDescending(p => p.Value)
                                .FirstOrDefault();
            if (reward is not null) return reward;
        }
        return options
            .OrderByDescending(p => (int)p.Rarity)
            .ThenByDescending(p => p.Value)
            .ThenBy(p => (int)p.Kind)
            .First();
    }

    private static ImmutableList<Unit> EvictMostDeclined(ImmutableList<Unit> roster)
    {
        var victim = roster
            .Where(IsEnlisted)
            .OrderByDescending(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge)
            .ThenByDescending(u => u.Age)
            .FirstOrDefault();
        return victim is null ? roster : roster.RemoveAll(u => u.Id == victim.Id);
    }

    private static ItemId ItemForJob(JobId job) => job switch
    {
        JobId.Sniper => ItemId.BowSniper,
        JobId.Sorcerer => ItemId.StaffMage,
        JobId.Medic => ItemId.RingPurelove,
        _ => ItemId.SwordKnight,
    };

    // ─── 1 宇宙（固定シード・方針指定）の 100 年完走シミュ ──────────────────────

    private static PlaythroughReport SimulatePlaythrough(int seed, PlaythroughPolicy policy)
    {
        var economyPriority = policy == PlaythroughPolicy.Economy;
        var rng = new Random(seed);

        var newGame = NewGameFactory.Create(rng);
        var roster = newGame.Roster;
        var economy = newGame.Economy;
        var timeline = TimelineEngine.CreateInitial(ProphecyMaster.Generate, rng);

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var generationOf = new Dictionary<Guid, int>();
        foreach (var u in roster) { usedNames.Add(u.FirstNameKey); generationOf[u.Id] = 0; }

        int turns = 0, battles = 0, restTurns = 0;
        int births = 0, scoutHires = 0, prophecyRecruits = 0, ageDeaths = 0, evictions = 0;
        int equipBought = 0, equipDropped = 0, equipUpgraded = 0;
        int peakEnlisted = EnlistedCount(roster), maxLineageDepth = 0;
        var log = ImmutableArray.CreateBuilder<string>();

        var year = ChronicleTimelineConfig.FirstYear;
        while (year <= ChronicleTimelineConfig.TotalYears)
        {
            turns++;
            var isBoss = ChronicleTimelineConfig.IsEpochBossYear(year);
            var chosen = PickProphecy(timeline.CurrentOptions, policy);
            var march = isBoss || chosen.Kind == ProphecyKind.Battle;

            // 1. 行動（敵ステ全 1 ＝ 自動勝利・損耗ゼロ。出撃は入団済のみ）。
            if (march)
            {
                battles++;
                economy = economy.EarnDirect(BattleVictoryReward);
                var leveler = roster.FirstOrDefault(u => IsEnlisted(u) && !u.IsAtMaxLevel);
                if (leveler is not null)
                    roster = roster.SetItem(roster.FindIndex(u => u.Id == leveler.Id), leveler.WithLevelUp(out _));
            }
            else
            {
                restTurns++;
                var rest = RestService.Resolve(roster, economy, chosen, rng);
                economy = rest.NextEconomy;
                if (rest.Outcome.RecruitedCount > 0)
                {
                    prophecyRecruits += rest.Outcome.RecruitedCount;
                    foreach (var u in rest.NextRoster)
                        if (!generationOf.ContainsKey(u.Id)) { generationOf[u.Id] = 0; usedNames.Add(u.FirstNameKey); }
                }
                roster = rest.NextRoster;

                // 無償の予言ドロップは両方針とも拾って未装備の入団済へ装着（コスト 0）。
                if (!rest.Outcome.DropCandidates.IsDefaultOrEmpty)
                {
                    var best = rest.Outcome.DropCandidates.OrderByDescending(e => e.Level).First();
                    var target = roster.FirstOrDefault(u => IsEnlisted(u) && !u.HasEquipment);
                    if (target is not null)
                    {
                        roster = roster.SetItem(roster.FindIndex(u => u.Id == target.Id), target.WithEquipment(best));
                        equipDropped++;
                    }
                }
            }

            // 2. 満員（入団済 >= 上限）なら最古参（寿命到達率最大）を除外。子供は数えない。
            while (EnlistedCount(roster) >= RosterCap)
            {
                var before = roster.Count;
                roster = EvictMostDeclined(roster);
                if (roster.Count == before) break;
                evictions++;
            }

            // 3. 婚姻（家系を伸ばす）。MemberAndEquip のみ。Economy は出費を避けて婚姻しない。
            if (!economyPriority && TryMarryOnce(ref roster, ref economy, rng, usedNames, generationOf))
            {
                births++;
                maxLineageDepth = Math.Max(maxLineageDepth, generationOf.Values.DefaultIfEmpty(0).Max());
            }

            // 4. スカウト。MemberAndEquip は上限まで増員、Economy は最低戦力フロアの補充のみ（家計温存）。
            var scoutCeiling = economyPriority ? EconomyFloor : RosterCap;
            if (EnlistedCount(roster) < scoutCeiling && economy.CanAfford(ScoutCost))
            {
                var scout = ScoutService.TryScout(economy, roster, ScoutCost, rng, usedNames);
                if (scout is not null)
                {
                    economy = scout.NewEconomy; roster = scout.NewRoster;
                    usedNames.Add(scout.Recruit.FirstNameKey); generationOf[scout.Recruit.Id] = 0;
                    scoutHires++;
                }
            }

            // 5. 装備購入/強化は MemberAndEquip のみ（Economy はドロップ拾いだけで購入はしない）。
            if (!economyPriority)
            {
                var unequipped = roster.FirstOrDefault(u => IsEnlisted(u) && !u.HasEquipment);
                if (unequipped is not null && economy.CanAfford(ShopService.BuyCost))
                {
                    var buy = ShopService.TryBuyEquipment(
                        economy, roster, unequipped.Id, ItemForJob(unequipped.Job), ShopService.BuyCost);
                    if (buy is not null) { economy = buy.NewEconomy; roster = buy.NewRoster; equipBought++; }
                }
                else
                {
                    var up = roster
                        .Where(u => IsEnlisted(u) && u.MainEquipment is { } e && e.Level < Equipment.MaxEquipmentLevel)
                        .OrderBy(u => u.MainEquipment!.Level).FirstOrDefault();
                    if (up is not null
                        && economy.CanAfford(ShopService.UpgradeCostFor(up.MainEquipment!.Level)))
                    {
                        var res = ShopService.TryUpgradeEquipment(
                            economy, roster, up.Id, ShopService.UpgradeCostFor(up.MainEquipment!.Level));
                        if (res is not null) { economy = res.NewEconomy; roster = res.NewRoster; equipUpgraded++; }
                    }
                }
            }

            peakEnlisted = Math.Max(peakEnlisted, EnlistedCount(roster));

            // 6. 年送り（加齢 → 寿命死の仕分け → 定期収入 → 暦と予言の前進）。
            var years = Math.Max(1, ChronicleTimelineConfig.ClampSkipToNextBossYear(year, chosen.SkipYears));
            var gen = RosterLifecycle.AdvanceGeneration(roster, years);
            ageDeaths += gen.DepartedRoster.Length;
            roster = gen.SurvivingRoster.ToImmutableList();
            economy = economy.EarnFromTimeSkip(years);
            timeline = timeline.AdvanceToNextTurn(ProphecyMaster.Generate, rng, years);
            year += years;

            if (isBoss || year > ChronicleTimelineConfig.TotalYears)
            {
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "year~{0,3}  enlisted={1,2}  growing={2,2}  balance={3,5}  births={4,2}  scouts={5,2}  deaths={6,2}  evict={7,2}  equip={8}/{9}/{10}  lineage={11}",
                    Math.Min(year, ChronicleTimelineConfig.TotalYears),
                    EnlistedCount(roster), roster.Count(IsGrowingChild), economy.CurrentBalance,
                    births, scoutHires, ageDeaths, evictions, equipBought, equipDropped, equipUpgraded, maxLineageDepth));
            }
        }

        // 最終年送りで成人化した子で上限超過した分を 1 度だけ除外して上限ちょうどへ収める。
        while (EnlistedCount(roster) > RosterCap)
        {
            var before = roster.Count;
            roster = EvictMostDeclined(roster);
            if (roster.Count == before) break;
            evictions++;
        }

        var enlistedLines = ImmutableArray.CreateBuilder<string>();
        var growthLines = ImmutableArray.CreateBuilder<string>();
        var idx = 1;
        foreach (var u in roster.Where(IsEnlisted)
                                .OrderBy(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge))
        {
            enlistedLines.Add(FormatUnitStatus(idx, u, generationOf.GetValueOrDefault(u.Id)));
            growthLines.Add(FormatGrowth(idx, u));
            idx++;
        }

        return new PlaythroughReport
        {
            FinalYear = Math.Min(year, ChronicleTimelineConfig.TotalYears),
            Turns = turns, Battles = battles, RestTurns = restTurns,
            FinalBalance = economy.CurrentBalance, TotalEarned = economy.TotalEarned, TotalSpent = economy.TotalSpent,
            Births = births, ScoutHires = scoutHires, ProphecyRecruits = prophecyRecruits,
            AgeDeaths = ageDeaths, Evictions = evictions,
            EquipBought = equipBought, EquipDropped = equipDropped, EquipUpgraded = equipUpgraded,
            PeakEnlisted = peakEnlisted, FinalEnlisted = EnlistedCount(roster),
            FinalChildren = roster.Count(IsGrowingChild), MaxLineageDepth = maxLineageDepth,
            Log = log.ToImmutable(), EnlistedLines = enlistedLines.ToImmutable(), GrowthLines = growthLines.ToImmutable(),
        };
    }

    private static bool TryMarryOnce(
        ref ImmutableList<Unit> roster, ref PointsEconomy economy, Random rng,
        HashSet<string> usedNames, Dictionary<Guid, int> generationOf)
    {
        bool Eligible(Unit u) => IsEnlisted(u) && u.SpouseId is null;
        var father = roster.FirstOrDefault(u => Eligible(u) && u.Gender == Gender.Male);
        var mother = roster.FirstOrDefault(u => Eligible(u) && u.Gender == Gender.Female);
        if (father is null || mother is null) return false;

        if (!MarriageService.IsNaturalMarriagePair(father, mother))
        {
            var cost = MarriageService.CalculateMarriageCost(father, mother);
            if (!economy.CanAfford(cost)) return false;
        }

        var spec = new NewbornSpec { MaxAge = rng.Next(ChildMinLifespan, ChildMaxLifespan + 1) };
        var result = MarriageService.ExecuteManualMarriage(economy, father, mother, spec, rng, usedNames);
        economy = result.NewEconomy;
        roster = roster
            .SetItem(roster.FindIndex(u => u.Id == father.Id), result.UpdatedFather)
            .SetItem(roster.FindIndex(u => u.Id == mother.Id), result.UpdatedMother)
            .Add(result.Child);
        usedNames.Add(result.Child.FirstNameKey);
        generationOf[result.Child.Id] =
            Math.Max(generationOf.GetValueOrDefault(father.Id), generationOf.GetValueOrDefault(mother.Id)) + 1;
        return true;
    }

    /// <summary>1 名を「全ステータス行」へ整形（JobStats 全項目＋装備＋属性）。</summary>
    private static string FormatUnitStatus(int index, Unit u, int generation)
    {
        var def = JobMaster.Find(u.Job);
        var s = def?.Stats;
        var special = def is { } d && !d.SpecialPassives.IsDefaultOrEmpty ? string.Join("+", d.SpecialPassives) : "-";
        string equip = "-";
        if (u.MainEquipment is { } e)
        {
            var affix = e.HasAnyAffix ? $" [{string.Join(",", e.AffixKeys)}]" : "";
            equip = $"{e.ItemId} Lv{e.Level}{affix}";
        }
        var bond = u.IsMarried ? "married" : (u.HasParentage ? "child" : "single");
        return string.Format(CultureInfo.InvariantCulture,
            "{0,2}. {1,-15} {2,-6} {3,-9} age{4,3}/{5,3} Lv{6} gen{7} | HP{8,3} SPD{9,2} FA{10,3} RA{11,3} BDEF{12,2} SDEF{13,2} BUF{14,2} HEAL{15,2} {16,-17} | {17}",
            index, u.Job, u.Gender, u.Origin, u.Age, u.MaxAge, u.Level, generation,
            s?.MaxHp ?? 0, s?.Speed ?? 0, s?.FrontAttack ?? 0, s?.RearAttack ?? 0,
            s?.BattalionDefense ?? 0, s?.SquadDefense ?? 0, s?.InitiativeBuff ?? 0, s?.TurnEndSquadHeal ?? 0,
            special, equip);
    }

    /// <summary>基本（素のジョブ値）→ 最終（装備込み）の成長差分を 1 行へ。差は装備のみが生む（C# は Lv/年齢で伸びない）。</summary>
    private static string FormatGrowth(int index, Unit u)
    {
        var s = JobMaster.Find(u.Job)?.Stats;
        var baseFa = s?.FrontAttack ?? 0;
        var baseRa = s?.RearAttack ?? 0;
        var baseSpd = s?.Speed ?? 0;
        var baseDef = s?.SquadDefense ?? 0;
        var dAtk = BattleManager.EquipmentAttackBonus(u);
        var dDef = BattleManager.EquipmentDefenseBonus(u);
        var dSpd = BattleManager.EquipmentSpeedBonus(u);
        return string.Format(CultureInfo.InvariantCulture,
            "{0,2}. {1,-15} FA {2,3}->{3,3} (+{4})  RA {5,3}->{6,3} (+{4})  SPD {7,2}->{8,3} (+{9})  DEF {10,2}->{11,3} (+{12})",
            index, u.Job, baseFa, baseFa + dAtk, dAtk, baseRa, baseRa + dAtk, baseSpd, baseSpd + dSpd, dSpd,
            baseDef, baseDef + dDef, dDef);
    }

    private static void DumpReport(string title, PlaythroughReport r)
    {
        Console.WriteLine($"=== {title} ===");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "seed=2024  final-year={0}  turns={1}  battles={2}  rests={3}", r.FinalYear, r.Turns, r.Battles, r.RestTurns));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "economy: balance={0}  earned={1}  spent={2}", r.FinalBalance, r.TotalEarned, r.TotalSpent));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "population: enlisted-peak={0}  enlisted-final={1}  growing-final={2}  | births={3}  scouts={4}  prophecy-recruits={5}  age-deaths={6}  evictions={7}  lineage-depth={8}",
            r.PeakEnlisted, r.FinalEnlisted, r.FinalChildren, r.Births, r.ScoutHires, r.ProphecyRecruits, r.AgeDeaths, r.Evictions, r.MaxLineageDepth));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "equipment: bought={0}  dropped={1}  upgraded={2}", r.EquipBought, r.EquipDropped, r.EquipUpgraded));
        foreach (var line in r.Log) Console.WriteLine(line);
        Console.WriteLine("--- FINAL BRIGADE (enlisted, age>=15) ---");
        foreach (var line in r.EnlistedLines) Console.WriteLine(line);
        Console.WriteLine("--- GROWTH base->effective (equipment only; Lv/age never scale unit combat) ---");
        foreach (var line in r.GrowthLines) Console.WriteLine(line);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  xUnit
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StrategyPlaythrough_EconomyPriority_GrowsTreasury_AndDumpsGrowth()
    {
        var report = SimulatePlaythrough(2024, PlaythroughPolicy.Economy);
        DumpReport("STRATEGY PLAYTHROUGH — ECONOMY PRIORITY (enemy stats=1 / RewardPoints-first / hoard)", report);

        Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
        Assert.True(report.FinalEnlisted <= RosterCap);
        Assert.True(report.FinalBalance >= 0);
        // 家計優先では残高が明確に伸びる（増員＋装備重視版の横ばい〜一桁とは桁が違う）。
        Assert.True(report.FinalBalance >= 50, $"economy priority should grow treasury; balance={report.FinalBalance}");
        // 出費を絞るので購入装備は無い（無償ドロップのみ）。
        Assert.Equal(0, report.EquipBought);
    }

    [Fact]
    public void StrategyPlaythrough_MemberAndEquipPriority_CompletesCentury()
    {
        var report = SimulatePlaythrough(2024, PlaythroughPolicy.MemberAndEquip);
        DumpReport("STRATEGY PLAYTHROUGH — MEMBER & EQUIP PRIORITY (enemy stats=1 / highest-tier / marry / recruit / equip)", report);

        Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
        Assert.True(report.PeakEnlisted <= RosterCap + 3);
        Assert.True(report.FinalEnlisted <= RosterCap);
        Assert.True(report.FinalBalance >= 0);
        Assert.True(report.Births > 0);
        Assert.True(report.EquipBought + report.EquipDropped > 0);
    }

    [Fact]
    public void StrategyPlaythrough_IsDeterministic_ForSameSeed()
    {
        var a = SimulatePlaythrough(7, PlaythroughPolicy.Economy);
        var b = SimulatePlaythrough(7, PlaythroughPolicy.Economy);
        Assert.Equal(a.FinalBalance, b.FinalBalance);
        Assert.Equal(a.ScoutHires, b.ScoutHires);
        Assert.Equal(a.AgeDeaths, b.AgeDeaths);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public void StrategyPlaythrough_ManySeeds_BothPolicies_AlwaysComplete(int seed)
    {
        foreach (var policy in new[] { PlaythroughPolicy.MemberAndEquip, PlaythroughPolicy.Economy })
        {
            var report = SimulatePlaythrough(seed, policy);
            Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
            Assert.True(report.FinalBalance >= 0);
            Assert.True(report.FinalEnlisted <= RosterCap);
        }
    }
}
