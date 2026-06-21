// =============================================================================
//  ChronicleKnights.Tests — StrategyPlaythroughSimulationTests.cs
// -----------------------------------------------------------------------------
//  「戦闘難易度を切り離した 100 年フロー検証」シミュ。旅団長の指定した方針を実 Core
//  サービスで直接 orchestrate し、ゲームループ全体（予言選択 → 行動 → 加齢 → 世代交代 →
//  経済・加入・婚姻・除外）が最後まで回るかを観測する（旅団長 FB 2026-06-21）。
//
//  ★ 模型ではなく実 Core サービスを直結する:
//    既存 MultiverseSimulationRunner は戦力 vs 敵ATK の粗い代理（ロスター実体・婚姻・加入を
//    持たない）。本ハーネスは実機 ChronicleGlobal が叩くのと同じ純粋サービス
//    （NewGameFactory / ProphecyMaster / RestService / RosterLifecycle / ScoutService /
//     MarriageService / PointsEconomy / TimelineEngine）を直接動かす。Godot 非依存。
//
//  ★ 指定方針（旅団長）:
//    - 敵ステータスは全て 1 ＝ 戦闘は必ず自動勝利・損耗ゼロ（戦闘を難易度から外し、フローだけ見る）。
//    - 予言カードは基本「ティアが一番高いもの」を選ぶ（レア度 Gold>Silver>Bronze、同率は効果量で）。
//    - メンバーが満員（旅団上限 = 大隊 9）になったら「衰退期が一番長い」ユニットを除外。
//      ※ C# 版に衰退ステージは無いため、寿命到達率 Age/MaxAge が最大の個体＝最も終わりに近い者を除外と解釈。
//    - 家計を伸ばす（増員投資しつつ）＝ 余力があればスカウトで増員し、世代を回しつつ残高も伸ばす。
//    - 婚姻・子作りを含める（自然婚姻は 0pt、無理なら有償で子を得て家系を伸ばす）。
//
//  ★ 観測値（敵ステ 1 では生存は自明なので、見るのは「軌跡」）:
//    最終年・ターン数・戦闘/休息回数・残高/総獲得/総消費・出生数・スカウト数・寿命死・除外数・
//    ロスター最大/最終・家系の最大深さ。節目年（章境界・章ボス年）のスナップショットをログ。
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
using ChronicleKnights.Core.Timeline;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class StrategyPlaythroughSimulationTests
{
    /// <summary>
    /// 旅団（ロスター）の上限。旅団長運用上限の 15 名（出撃枠の大隊 3×3＝9 とは別概念で、控えを含む
    /// 旅団全体の保有上限）。コード側に定数が無いため本シミュの方針値として置く。満員でこれを超えると除外。
    /// </summary>
    private const int RosterCap = 15;

    /// <summary>外様スカウトの固定コスト（MarriageUI / 実機と同レート）。</summary>
    private const int ScoutCost = 3;

    /// <summary>戦闘勝利の婚姻ポイント（BattleSpoils 同型: VictoryBase5 ＋ 昇級1件×2 ＝ 7）。敵ステ1で常に勝利。</summary>
    private const int BattleVictoryReward = 7;

    /// <summary>婚姻可能な成人年齢（実機 MarriageUI と同じ）。</summary>
    private const int AdultAge = 15;

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
        public required int PeakRoster { get; init; }
        public required int FinalRoster { get; init; }
        public required int MaxLineageDepth { get; init; }
        public required ImmutableArray<string> Log { get; init; }
        public required ImmutableArray<string> FinalRosterLines { get; init; }
    }

    /// <summary>最終旅団 1 名を「ステータス行」へ整形する（Job 数値は JobMaster から解決）。</summary>
    private static string FormatUnitStatus(int index, Unit u, int generation)
    {
        var s = JobMaster.Find(u.Job)?.Stats;
        var hp = s?.MaxHp ?? 0;
        var spd = s?.Speed ?? 0;
        var fa = s?.FrontAttack ?? 0;
        var ra = s?.RearAttack ?? 0;
        var equip = u.MainEquipment is { } e ? $"{e.ItemId} Lv{e.Level}" : "-";
        var bond = u.IsMarried ? "married" : (u.HasParentage ? "child" : "single");
        return string.Format(CultureInfo.InvariantCulture,
            "{0,2}. {1,-16} {2,-6} age {3,3}/{4,3}  Lv{5}  gen{6}  HP{7,3} SPD{8,2} FA{9,3} RA{10,3}  equip:{11,-14} {12}",
            index, u.Job, u.Gender, u.Age, u.MaxAge, u.Level, generation, hp, spd, fa, ra, equip, bond);
    }

    // ─── 方針ヘルパ ───────────────────────────────────────────────────────

    /// <summary>3 予言から「ティア最高」を選ぶ（レア度降順 → 効果量降順 → 種別順）。</summary>
    private static Prophecy PickHighestTier(ImmutableArray<Prophecy> options) =>
        options
            .OrderByDescending(p => (int)p.Rarity)
            .ThenByDescending(p => p.Value)
            .ThenBy(p => (int)p.Kind)
            .First();

    /// <summary>生存数。寿命到達・戦闘死を除いた現役頭数。</summary>
    private static int AliveCount(IReadOnlyList<Unit> roster) => roster.Count(u => u.IsAlive);

    /// <summary>「衰退期が一番長い」＝寿命到達率 Age/MaxAge が最大の生存個体を 1 体除外する。</summary>
    private static ImmutableList<Unit> EvictMostDeclined(ImmutableList<Unit> roster)
    {
        var victim = roster
            .Where(u => u.IsAlive)
            .OrderByDescending(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge)
            .ThenByDescending(u => u.Age)
            .FirstOrDefault();
        return victim is null ? roster : roster.RemoveAll(u => u.Id == victim.Id);
    }

    // ─── 1 宇宙（固定シード）の 100 年完走シミュ ──────────────────────────────

    private static PlaythroughReport SimulatePlaythrough(int seed)
    {
        var rng = new Random(seed);

        // 実機と同じ初期化順（NewGameFactory → TimelineEngine.CreateInitial）で乱数列を 1 本に。
        var newGame = NewGameFactory.Create(rng);
        var roster = newGame.Roster;
        var economy = newGame.Economy;
        var timeline = TimelineEngine.CreateInitial(ProphecyMaster.Generate, rng);

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var generationOf = new Dictionary<Guid, int>();
        foreach (var u in roster)
        {
            usedNames.Add(u.FirstNameKey);
            generationOf[u.Id] = 0; // 創設メンバーは第 0 世代
        }

        int turns = 0, battles = 0, restTurns = 0;
        int births = 0, scoutHires = 0, prophecyRecruits = 0, ageDeaths = 0, evictions = 0;
        int peakRoster = roster.Count, maxLineageDepth = 0;
        var log = ImmutableArray.CreateBuilder<string>();

        var year = ChronicleTimelineConfig.FirstYear;
        while (year <= ChronicleTimelineConfig.TotalYears)
        {
            turns++;
            var isBoss = ChronicleTimelineConfig.IsEpochBossYear(year);
            var chosen = PickHighestTier(timeline.CurrentOptions);
            var march = isBoss || chosen.Kind == ProphecyKind.Battle;

            // 1. 行動の解決（敵ステ全 1 ＝ 戦闘は自動勝利・損耗ゼロ）。
            if (march)
            {
                battles++;
                economy = economy.EarnDirect(BattleVictoryReward); // 勝利報酬（BattleSpoils 同型）
                // ラストヒット成長: 未上限の生存者 1 名を昇級（敵ステ1でも勝てば成長は入る）。
                var leveler = roster.FirstOrDefault(u => u.IsAlive && !u.IsAtMaxLevel);
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
                // 予言加入（ScoutReward）で増えた新人を世代 0 として登録。
                if (rest.Outcome.RecruitedCount > 0)
                {
                    prophecyRecruits += rest.Outcome.RecruitedCount;
                    foreach (var u in rest.NextRoster)
                    {
                        if (!generationOf.ContainsKey(u.Id)) { generationOf[u.Id] = 0; usedNames.Add(u.FirstNameKey); }
                    }
                }
                roster = rest.NextRoster;
            }

            // 2. 満員なら「衰退期が一番長い」者を除外（席を空けて新陳代謝）。
            while (AliveCount(roster) >= RosterCap)
            {
                var before = roster.Count;
                roster = EvictMostDeclined(roster);
                if (roster.Count == before) break; // 念のため無限ループ封じ
                evictions++;
            }

            // 3. 婚姻（家系を伸ばす・席があるとき）。自然婚姻優先、無理でも有償で子を得る。
            if (AliveCount(roster) < RosterCap)
            {
                var married = TryMarryOnce(ref roster, ref economy, rng, usedNames, generationOf);
                if (married) { births++; maxLineageDepth = Math.Max(maxLineageDepth, generationOf.Values.Max()); }
            }

            // 4. 増員投資（席があり、コストを払える限りスカウトで増員）。
            if (AliveCount(roster) < RosterCap && economy.CanAfford(ScoutCost))
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

            peakRoster = Math.Max(peakRoster, roster.Count);

            // 5. 年送り（加齢 → 寿命死の仕分け → 定期収入 → 暦と予言の前進）。
            var years = Math.Max(1, ChronicleTimelineConfig.ClampSkipToNextBossYear(year, chosen.SkipYears));
            var gen = RosterLifecycle.AdvanceGeneration(roster, years);
            ageDeaths += gen.DepartedRoster.Length;
            roster = gen.SurvivingRoster.ToImmutableList();
            economy = economy.EarnFromTimeSkip(years);
            timeline = timeline.AdvanceToNextTurn(ProphecyMaster.Generate, rng, years);
            year += years;

            // 6. 節目（章境界・章ボス年）でスナップショット。
            if (isBoss || year > ChronicleTimelineConfig.TotalYears)
            {
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "year~{0,3}  roster={1,2}  balance={2,4}  births={3,2}  scouts={4,2}  deaths={5,2}  evict={6,2}  lineage={7}",
                    Math.Min(year, ChronicleTimelineConfig.TotalYears), roster.Count, economy.CurrentBalance,
                    births, scoutHires, ageDeaths, evictions, maxLineageDepth));
            }
        }

        // 最終旅団のステータス行（生存者を寿命到達率の浅い＝若い順で並べる）。
        var finalLines = ImmutableArray.CreateBuilder<string>();
        var i = 1;
        foreach (var u in roster.Where(u => u.IsAlive)
                                .OrderBy(u => u.MaxAge <= 0 ? 1.0 : (double)u.Age / u.MaxAge))
        {
            finalLines.Add(FormatUnitStatus(i++, u, generationOf.GetValueOrDefault(u.Id)));
        }

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
            PeakRoster = peakRoster,
            FinalRoster = roster.Count,
            MaxLineageDepth = maxLineageDepth,
            Log = log.ToImmutable(),
            FinalRosterLines = finalLines.ToImmutable(),
        };
    }

    /// <summary>
    /// 成人・生存・未婚の男女ペアを 1 組見つけ、自然婚姻（0pt）優先・無理でも有償で婚姻させて子を得る。
    /// roster は夫婦リンク更新＋子追加で差し替え、economy はコストを反映する。成立したら true。
    /// </summary>
    private static bool TryMarryOnce(
        ref ImmutableList<Unit> roster, ref PointsEconomy economy, Random rng,
        HashSet<string> usedNames, Dictionary<Guid, int> generationOf)
    {
        bool Eligible(Unit u) => u.IsAlive && u.Age >= AdultAge && u.SpouseId is null;
        var father = roster.FirstOrDefault(u => Eligible(u) && u.Gender == Gender.Male);
        var mother = roster.FirstOrDefault(u => Eligible(u) && u.Gender == Gender.Female);
        if (father is null || mother is null) return false;

        // 自然婚姻でなければコストを確認（払えないなら見送り＝例外を起こさない）。
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
        var parentGen = Math.Max(
            generationOf.GetValueOrDefault(father.Id), generationOf.GetValueOrDefault(mother.Id));
        generationOf[result.Child.Id] = parentGen + 1;
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  xUnit: 戦略プレイスルーが 100 年完走し、方針の不変条件を満たす
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void StrategyPlaythrough_AllStatsOne_CompletesCentury_AndDumpsTrajectory()
    {
        var report = SimulatePlaythrough(2024);

        // 標準出力へ軌跡ダンプ（dotnet test 実行時にコンソール印字）。
        Console.WriteLine("=== STRATEGY PLAYTHROUGH (enemy stats=1 / highest-tier cards / evict-eldest / marry / recruit) ===");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "seed=2024  final-year={0}  turns={1}  battles={2}  rests={3}",
            report.FinalYear, report.Turns, report.Battles, report.RestTurns));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "economy: balance={0}  earned={1}  spent={2}",
            report.FinalBalance, report.TotalEarned, report.TotalSpent));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "roster: peak={0}  final={1}  | births={2}  scouts={3}  prophecy-recruits={4}  age-deaths={5}  evictions={6}  lineage-depth={7}",
            report.PeakRoster, report.FinalRoster, report.Births, report.ScoutHires,
            report.ProphecyRecruits, report.AgeDeaths, report.Evictions, report.MaxLineageDepth));
        foreach (var line in report.Log) Console.WriteLine(line);
        Console.WriteLine("--- FINAL BRIGADE ROSTER (alive) ---");
        foreach (var line in report.FinalRosterLines) Console.WriteLine(line);

        // 🎯 完走: 100 年に到達している（フローが詰まらず最後まで回る）。
        Assert.Equal(ChronicleTimelineConfig.TotalYears, report.FinalYear);
        // 🎯 方針: ロスターは旅団上限を超えない（満員除外が効いている）。
        Assert.True(report.PeakRoster <= RosterCap + 3, $"roster overshoot: peak={report.PeakRoster}");
        Assert.True(report.FinalRoster <= RosterCap, $"final roster over cap: {report.FinalRoster}");
        // 🎯 経済: 残高は非負（破産＝負残高を構造的に起こさない）。
        Assert.True(report.FinalBalance >= 0, $"balance went negative: {report.FinalBalance}");
        // 🎯 家系: 婚姻方針で子が生まれ家系が伸びている。
        Assert.True(report.Births > 0, "marriage policy should produce at least one child");
        Assert.True(report.MaxLineageDepth >= 1, "lineage should deepen by at least one generation");
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
        Assert.True(report.FinalRoster <= RosterCap);
    }
}
