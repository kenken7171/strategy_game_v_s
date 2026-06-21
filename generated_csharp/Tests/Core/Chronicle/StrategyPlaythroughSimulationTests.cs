// =============================================================================
//  ChronicleKnights.Tests — StrategyPlaythroughSimulationTests.cs
// -----------------------------------------------------------------------------
//  「戦闘難易度を切り離した 100 年フロー検証」シミュ。旅団長の指定方針を実 Core
//  サービスで直接 orchestrate し、ループ全体（予言選択 → 行動 → 加齢 → 世代交代 →
//  経済・加入・婚姻・除外・装備）が最後まで回るかと、その軌跡を観測する。
//
//  ★ 実 Core サービスを直結（模型ではない）:
//    NewGameFactory / ProphecyMaster / RestService / RosterLifecycle / ScoutService /
//    MarriageService / ShopService / PointsEconomy / TimelineEngine。Godot 非依存・決定論。
//
//  ★ 指定方針（旅団長 2026-06-21）:
//    - 敵ステータスは全て 1 ＝ 戦闘は必ず自動勝利・損耗ゼロ（戦闘を難易度から外す）。
//    - 予言は「ティア最高」を選ぶ（レア度 Gold>Silver>Bronze、同率は効果量→種別順）。
//    - 旅団（入団済の現役）上限 15 名。満員でこれを超えると「衰退期が一番長い」者を除外。
//      ※ C# に衰退ステージは無いため、寿命到達率 Age/MaxAge 最大＝最も終わりに近い者を除外と解釈。
//    - 家計を伸ばす（増員投資しつつ）＝ 余力でスカウト増員。
//    - 婚姻・子作りを含める（自然婚姻 0pt 優先・無理なら有償で子を得て家系を伸ばす）。
//    - 装備を運用する（予言ドロップを装着・余力で購入/強化）。
//
//  ★ 子供の扱い（実機ルール準拠）:
//    婚姻で生まれた子は 15 歳未満は「成長中」＝入団しておらず戦闘にも出ない。よって上限カウント・
//    戦闘・除外の対象は「入団済＝生存かつ Age >= AdultAge(15)」だけ。子は別プールで加齢を待つ。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using ChronicleKnights.Core.Bootstrap;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.GameFlow;
using ChronicleKnights.Core.Job;             // JobMaster（最終ステータス解決）
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;          // Gender
using ChronicleKnights.Core.Shop;            // ShopService（装備購入/強化）
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class StrategyPlaythroughSimulationTests
{
    /// <summary>旅団（入団済の現役）の上限。旅団長運用上限の 15 名（出撃枠の大隊 9 とは別概念）。</summary>
    private const int RosterCap = 15;

    /// <summary>入団・出撃可能となる成人年齢（実機 MarriageUI / FormationUI と同じ）。15 歳未満は成長中。</summary>
    private const int AdultAge = 15;

    /// <summary>外様スカウトの固定コスト（実機 MarriageUI と同レート）。</summary>
    private const int ScoutCost = 3;

    /// <summary>戦闘勝利の婚姻ポイント（BattleSpoils 同型: VictoryBase5 ＋ 昇級1件×2 ＝ 7）。敵ステ1で常に勝利。</summary>
    private const int BattleVictoryReward = 7;

    /// <summary>子の寿命下限/上限（スカウトと同レンジ）。</summary>
    private const int ChildMinLifespan = 55;
    private const int ChildMaxLifespan = 75;

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
        public required ImmutableArray<string> ChildrenLines { get; init; }
    }

    // ─── 判定ヘルパ ───────────────────────────────────────────────────────

    /// <summary>入団済＝生存かつ成人（15 歳以上）。上限カウント・戦闘・除外の対象。</summary>
    private static bool IsEnlisted(Unit u) => u.IsAlive && u.Age >= AdultAge;

    /// <summary>成長中＝生存だが 15 歳未満（入団前・非戦闘）。</summary>
    private static bool IsGrowingChild(Unit u) => u.IsAlive && u.Age < AdultAge;

    private static int EnlistedCount(IReadOnlyList<Unit> roster) => roster.Count(IsEnlisted);

    /// <summary>3 予言から「ティア最高」を選ぶ（レア度降順 → 効果量降順 → 種別順）。</summary>
    private static Prophecy PickHighestTier(ImmutableArray<Prophecy> options) =>
        options
            .OrderByDescending(p => (int)p.Rarity)
            .ThenByDescending(p => p.Value)
            .ThenBy(p => (int)p.Kind)
            .First();

    /// <summary>「衰退期が一番長い」＝寿命到達率 Age/MaxAge 最大の入団済を 1 体除外する。</summary>
    private static ImmutableList<Unit> EvictMostDeclined(ImmutableList<Unit> roster)
    {
        var victim = roster
            .Where(IsEnlisted)
            .OrderByDescending(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge)
            .ThenByDescending(u => u.Age)
            .FirstOrDefault();
        return victim is null ? roster : roster.RemoveAll(u => u.Id == victim.Id);
    }

    /// <summary>ジョブに合った購入アイテム種別（前衛=剣 / 狙撃=弓 / 呪術=杖 / 衛生=指輪 / その他=剣）。</summary>
    private static ItemId ItemForJob(JobId job) => job switch
    {
        JobId.Sniper => ItemId.BowSniper,
        JobId.Sorcerer => ItemId.StaffMage,
        JobId.Medic => ItemId.RingPurelove,
        _ => ItemId.SwordKnight,
    };

    // ─── 1 宇宙（固定シード）の 100 年完走シミュ ──────────────────────────────

    private static PlaythroughReport SimulatePlaythrough(int seed)
    {
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
            var chosen = PickHighestTier(timeline.CurrentOptions);
            var march = isBoss || chosen.Kind == ProphecyKind.Battle;

            // 1. 行動の解決（敵ステ全 1 ＝ 戦闘は自動勝利・損耗ゼロ。出撃は入団済のみ）。
            if (march)
            {
                battles++;
                economy = economy.EarnDirect(BattleVictoryReward);
                var leveler = roster.FirstOrDefault(u => IsEnlisted(u) && !u.IsAtMaxLevel);
                if (leveler is not null)
                {
                    var leveled = leveler.WithLevelUp(out _);
                    roster = roster.SetItem(roster.FindIndex(u => u.Id == leveler.Id), leveled);
                }
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

                // 予言ドロップ（EquipmentDrop）を 1 つ確保し、未装備の入団済へ装着する。
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

            // 2. 満員（入団済 >= 上限）なら「衰退期が一番長い」入団済を除外。子供は数えない。
            while (EnlistedCount(roster) >= RosterCap)
            {
                var before = roster.Count;
                roster = EvictMostDeclined(roster);
                if (roster.Count == before) break;
                evictions++;
            }

            // 3. 婚姻（家系を伸ばす）。子は成長中プールに入り上限を圧迫しないので、席に関係なく試行。
            if (TryMarryOnce(ref roster, ref economy, rng, usedNames, generationOf))
            {
                births++;
                maxLineageDepth = Math.Max(maxLineageDepth, generationOf.Values.DefaultIfEmpty(0).Max());
            }

            // 4. 増員投資（入団済に空きがあり、コストを払える限りスカウト）。
            if (EnlistedCount(roster) < RosterCap && economy.CanAfford(ScoutCost))
            {
                var scout = ScoutService.TryScout(economy, roster, ScoutCost, rng, usedNames);
                if (scout is not null)
                {
                    economy = scout.NewEconomy;
                    roster = scout.NewRoster;
                    usedNames.Add(scout.Recruit.FirstNameKey);
                    generationOf[scout.Recruit.Id] = 0;
                    scoutHires++;
                }
            }

            // 5. 装備運用：未装備の入団済が居れば購入装着、全員装備済なら最低 Lv を 1 段強化（余力時）。
            var unequipped = roster.FirstOrDefault(u => IsEnlisted(u) && !u.HasEquipment);
            if (unequipped is not null && economy.CanAfford(ShopService.BuyCost))
            {
                var buy = ShopService.TryBuyEquipment(
                    economy, roster, unequipped.Id, ItemForJob(unequipped.Job), ShopService.BuyCost);
                if (buy is not null) { economy = buy.NewEconomy; roster = buy.NewRoster; equipBought++; }
            }
            else
            {
                var upgradable = roster
                    .Where(u => IsEnlisted(u) && u.MainEquipment is { } e && e.Level < Equipment.MaxEquipmentLevel)
                    .OrderBy(u => u.MainEquipment!.Level)
                    .FirstOrDefault();
                if (upgradable is not null
                    && economy.CanAfford(ShopService.UpgradeCostFor(upgradable.MainEquipment!.Level)))
                {
                    var up = ShopService.TryUpgradeEquipment(
                        economy, roster, upgradable.Id, ShopService.UpgradeCostFor(upgradable.MainEquipment!.Level));
                    if (up is not null) { economy = up.NewEconomy; roster = up.NewRoster; equipUpgraded++; }
                }
            }

            peakEnlisted = Math.Max(peakEnlisted, EnlistedCount(roster));

            // 6. 年送り（加齢 → 寿命死の仕分け → 定期収入 → 暦と予言の前進）。子供もここで加齢し、
            //    15 歳に達した者は次ターンから入団済としてカウントされる。
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
                    "year~{0,3}  enlisted={1,2}  growing={2,2}  balance={3,4}  births={4,2}  scouts={5,2}  deaths={6,2}  evict={7,2}  equip(buy/drop/up)={8}/{9}/{10}  lineage={11}",
                    Math.Min(year, ChronicleTimelineConfig.TotalYears),
                    EnlistedCount(roster), roster.Count(IsGrowingChild), economy.CurrentBalance,
                    births, scoutHires, ageDeaths, evictions, equipBought, equipDropped, equipUpgraded, maxLineageDepth));
            }
        }

        // 最終整理: 最終年送りで 15 歳に達した子が入団して上限を超える場合があるため、
        //   次の年代記フェーズで起きる除外を 1 度先取りして上限ちょうどへ収める。
        while (EnlistedCount(roster) > RosterCap)
        {
            var before = roster.Count;
            roster = EvictMostDeclined(roster);
            if (roster.Count == before) break;
            evictions++;
        }

        // 最終旅団（入団済）と成長中の子をそれぞれ整形（入団済は若い順）。
        var enlistedLines = ImmutableArray.CreateBuilder<string>();
        var ei = 1;
        foreach (var u in roster.Where(IsEnlisted)
                                .OrderBy(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge))
            enlistedLines.Add(FormatUnitStatus(ei++, u, generationOf.GetValueOrDefault(u.Id)));

        var childLines = ImmutableArray.CreateBuilder<string>();
        var ci = 1;
        foreach (var u in roster.Where(IsGrowingChild).OrderBy(u => u.Age))
            childLines.Add(FormatUnitStatus(ci++, u, generationOf.GetValueOrDefault(u.Id)));

        return new PlaythroughReport
        {
            FinalYear = Math.Min(year, ChronicleTimelineConfig.TotalYears),
            Turns = turns,
            Battles = battles,
            RestTurns = restTurns,
            FinalBalance = economy.CurrentBalance,
            TotalEarned = economy.TotalEarned,
            TotalSpent = economy.TotalSpent,
            Births = births,
            ScoutHires = scoutHires,
            ProphecyRecruits = prophecyRecruits,
            AgeDeaths = ageDeaths,
            Evictions = evictions,
            EquipBought = equipBought,
            EquipDropped = equipDropped,
            EquipUpgraded = equipUpgraded,
            PeakEnlisted = peakEnlisted,
            FinalEnlisted = EnlistedCount(roster),
            FinalChildren = roster.Count(IsGrowingChild),
            MaxLineageDepth = maxLineageDepth,
            Log = log.ToImmutable(),
            EnlistedLines = enlistedLines.ToImmutable(),
            ChildrenLines = childLines.ToImmutable(),
        };
    }

    /// <summary>成人・生存・未婚の男女ペアを 1 組、自然婚姻優先・無理でも有償で婚姻させ子を得る。成立で true。</summary>
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
        var special = def is { } d && !d.SpecialPassives.IsDefaultOrEmpty
            ? string.Join("+", d.SpecialPassives) : "-";
        string equip = "-";
        if (u.MainEquipment is { } e)
        {
            var affix = e.HasAnyAffix ? $" [{string.Join(",", e.AffixKeys)}]" : "";
            equip = $"{e.ItemId} Lv{e.Level}{affix} (+ATK{e.AffixAttackBonus}/+DEF{e.AffixDefenseBonus}/+SPD{e.AffixSpeedBonus})";
        }
        var bond = u.IsMarried ? "married" : (u.HasParentage ? "child" : "single");
        return string.Format(CultureInfo.InvariantCulture,
            "{0,2}. {1,-15} {2,-6} {3,-9} age{4,3}/{5,3} Lv{6} gen{7} | HP{8,3} SPD{9,2} FA{10,3} RA{11,3} BDEF{12,2} SDEF{13,2} BUF{14,2} HEAL{15,2} {16,-17} | {17,-40} | {18}",
            index, u.Job, u.Gender, u.Origin, u.Age, u.MaxAge, u.Level, generation,
            s?.MaxHp ?? 0, s?.Speed ?? 0, s?.FrontAttack ?? 0, s?.RearAttack ?? 0,
            s?.BattalionDefense ?? 0, s?.SquadDefense ?? 0, s?.InitiativeBuff ?? 0, s?.TurnEndSquadHeal ?? 0,
            special, equip, bond);
    }

    // ════════════════════════════════════════════════════════════════════════
    //  xUnit: 戦略プレイスルーが 100 年完走し、方針の不変条件を満たす
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StrategyPlaythrough_AllStatsOne_CompletesCentury_AndDumpsTrajectory()
    {
        var report = SimulatePlaythrough(2024);

        Console.WriteLine("=== STRATEGY PLAYTHROUGH (enemy stats=1 / highest-tier / cap=15 enlisted / marry / recruit / equip) ===");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "seed=2024  final-year={0}  turns={1}  battles={2}  rests={3}",
            report.FinalYear, report.Turns, report.Battles, report.RestTurns));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "economy: balance={0}  earned={1}  spent={2}", report.FinalBalance, report.TotalEarned, report.TotalSpent));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "population: enlisted-peak={0}  enlisted-final={1}  growing-final={2}  | births={3}  scouts={4}  prophecy-recruits={5}  age-deaths={6}  evictions={7}  lineage-depth={8}",
            report.PeakEnlisted, report.FinalEnlisted, report.FinalChildren, report.Births, report.ScoutHires,
            report.ProphecyRecruits, report.AgeDeaths, report.Evictions, report.MaxLineageDepth));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "equipment: bought={0}  dropped={1}  upgraded={2}", report.EquipBought, report.EquipDropped, report.EquipUpgraded));
        foreach (var line in report.Log) Console.WriteLine(line);
        Console.WriteLine("--- FINAL BRIGADE (enlisted, age>=15) ---");
        foreach (var line in report.EnlistedLines) Console.WriteLine(line);
        Console.WriteLine("--- GROWING CHILDREN (age<15, not yet enlisted) ---");
        foreach (var line in report.ChildrenLines) Console.WriteLine(line);

        Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
        Assert.True(report.PeakEnlisted <= RosterCap + 3, $"enlisted overshoot: {report.PeakEnlisted}");
        Assert.True(report.FinalEnlisted <= RosterCap, $"final enlisted over cap: {report.FinalEnlisted}");
        Assert.True(report.FinalBalance >= 0, $"balance went negative: {report.FinalBalance}");
        Assert.True(report.Births > 0, "marriage policy should produce at least one child");
        Assert.True(report.MaxLineageDepth >= 1, "lineage should deepen by at least one generation");
        Assert.True(report.EquipBought + report.EquipDropped > 0, "equipment policy should equip at least one item");
    }

    [Fact]
    public void StrategyPlaythrough_IsDeterministic_ForSameSeed()
    {
        var a = SimulatePlaythrough(7);
        var b = SimulatePlaythrough(7);

        Assert.Equal(a.FinalYear, b.FinalYear);
        Assert.Equal(a.FinalBalance, b.FinalBalance);
        Assert.Equal(a.Births, b.Births);
        Assert.Equal(a.AgeDeaths, b.AgeDeaths);
        Assert.Equal(a.Evictions, b.Evictions);
        Assert.Equal(a.EquipBought, b.EquipBought);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public void StrategyPlaythrough_ManySeeds_AlwaysCompleteWithoutException(int seed)
    {
        var report = SimulatePlaythrough(seed);
        Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
        Assert.True(report.FinalBalance >= 0);
        Assert.True(report.FinalEnlisted <= RosterCap);
    }
}
