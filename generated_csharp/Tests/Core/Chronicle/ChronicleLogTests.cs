// =============================================================================
//  ChronicleKnights — ChronicleLogTests.cs
// -----------------------------------------------------------------------------
//  年代記ナレーション素材の純粋ファクトリ ChronicleLog.BuildGenerationEntries を
//  網羅検証する。世代交代 1 周で「成長（昇級）」と「損失（引退 / 戦死）」を 1 本の
//  不変イベント列へ束ねる挙動を、入力の静止画から決定論的に確認する。
//
//  検証の柱:
//    1. 損失の種別判定   … HasReachedMaxAge → 引退 / IsDead → 戦死（IsDead 優先）
//    2. 損失の素材捕捉   … 名前キー・ジョブ・年齢を「外れる前の静止画」から確定捕捉
//    3. 成長の素材解決   … BattleSpoils の突合キー(UnitId)を lookup で名前・ジョブへ解決
//    4. 並び順           … 成長（昇級）→ 損失（引退 / 戦死）の時系列順
//    5. 突合スキップ     … lookup で引き当て不能な昇級キーは安全に捨てる
//    6. ヌル安全         … spoils / lookup が null でも例外なく損失だけを返す
//    7. 空入力           … 損失なし・戦果なしなら空配列
//    8. 世代見出し       … 全エントリに generation が一様に刻まれる
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
//    名前キー等はすべて "first-test" のような ASCII プレースホルダを用いる。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Chronicle;

public class ChronicleLogTests
{
    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>生存・Lv1・装備なしのテストユニットを作る（突合の基準点）。</summary>
    private static Unit MakeUnit(
        Guid id,
        JobId job = JobId.IronWallKnight,
        string first = "first-test",
        string last = "last-test") => new()
    {
        Id = id,
        Job = job,
        Age = 20,
        MaxAge = 60,
        FirstNameKey = first,
        LastNameKey = last,
    };

    /// <summary>Id → Unit の不変辞書を組み立てる（昇級者の名前解決 lookup 用）。</summary>
    private static ImmutableDictionary<Guid, Unit> Lookup(params Unit[] units)
        => units.ToImmutableDictionary(u => u.Id);

    /// <summary>UnitLevelGain だけを持つ最小の BattleSpoils を作る（昇級素材）。</summary>
    private static BattleSpoils SpoilsWithGains(params UnitLevelGain[] gains)
        => BattleSpoils.Empty with { UnitLevelGains = gains.ToImmutableArray() };

    // ─── 1. 損失の種別判定（引退 / 戦死） ──────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_ClassifiesRetirement_OnReachedMaxAge()
    {
        var id = Guid.NewGuid();
        // 寿命到達（Age >= MaxAge）かつ未戦死 → 引退。
        var retiree = MakeUnit(id, JobId.Medic) with { Age = 60 };

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 7,
            departedRoster: ImmutableArray.Create(retiree),
            battleSpoils: null,
            levelGainUnitLookup: null);

        var entry = Assert.Single(entries);
        Assert.Equal(ChronicleEventKind.Retired, entry.Kind);
        Assert.Equal(7, entry.Generation);
        Assert.Equal("first-test", entry.UnitFirstNameKey);
        Assert.Equal("last-test", entry.UnitLastNameKey);
        Assert.Equal(JobId.Medic, entry.Job);
        Assert.Equal(60, entry.Age);
    }

    [Fact]
    public void BuildGenerationEntries_ClassifiesKilledInAction_OnDead()
    {
        var id = Guid.NewGuid();
        // 戦死（IsDead）→ KilledInAction。年齢も静止画から捕捉される。
        var fallen = MakeUnit(id, JobId.Sniper) with { Age = 31, IsDead = true };

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 3,
            departedRoster: ImmutableArray.Create(fallen),
            battleSpoils: null,
            levelGainUnitLookup: null);

        var entry = Assert.Single(entries);
        Assert.Equal(ChronicleEventKind.KilledInAction, entry.Kind);
        Assert.Equal(JobId.Sniper, entry.Job);
        Assert.Equal(31, entry.Age);
    }

    [Fact]
    public void BuildGenerationEntries_PrefersKilledInAction_WhenDeadAndOverMaxAge()
    {
        // IsDead 優先: 寿命到達と戦死が同時に立っても、戦死として記録する。
        var id = Guid.NewGuid();
        var both = MakeUnit(id) with { Age = 60, IsDead = true };

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 1,
            departedRoster: ImmutableArray.Create(both),
            battleSpoils: null,
            levelGainUnitLookup: null);

        Assert.Equal(ChronicleEventKind.KilledInAction, Assert.Single(entries).Kind);
    }

    // ─── 2. 成長（昇級）の素材解決 ─────────────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_ResolvesLevelGain_FromLookup()
    {
        var id = Guid.NewGuid();
        var grower = MakeUnit(id, JobId.Tactician, "grower-first", "grower-last");
        var spoils = SpoilsWithGains(new UnitLevelGain { UnitId = id, FromLevel = 2, ToLevel = 4 });

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 5,
            departedRoster: ImmutableArray<Unit>.Empty,
            battleSpoils: spoils,
            levelGainUnitLookup: Lookup(grower));

        var entry = Assert.Single(entries);
        Assert.Equal(ChronicleEventKind.LevelGained, entry.Kind);
        Assert.Equal(5, entry.Generation);
        Assert.Equal("grower-first", entry.UnitFirstNameKey);
        Assert.Equal("grower-last", entry.UnitLastNameKey);
        Assert.Equal(JobId.Tactician, entry.Job);
        Assert.Equal(2, entry.FromLevel);
        Assert.Equal(4, entry.ToLevel);
        // 昇級では年齢は未使用（既定 0）。
        Assert.Equal(0, entry.Age);
    }

    [Fact]
    public void BuildGenerationEntries_SkipsLevelGain_WhenUnitNotInLookup()
    {
        // lookup に居ない昇級キーは名前解決できないため安全にスキップする。
        var resolvable = Guid.NewGuid();
        var orphan = Guid.NewGuid();
        var known = MakeUnit(resolvable, JobId.Sorcerer);

        var spoils = SpoilsWithGains(
            new UnitLevelGain { UnitId = resolvable, FromLevel = 1, ToLevel = 2 },
            new UnitLevelGain { UnitId = orphan, FromLevel = 1, ToLevel = 5 });

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 2,
            departedRoster: ImmutableArray<Unit>.Empty,
            battleSpoils: spoils,
            levelGainUnitLookup: Lookup(known));

        var entry = Assert.Single(entries);
        Assert.Equal(JobId.Sorcerer, entry.Job);
        Assert.Equal(ChronicleEventKind.LevelGained, entry.Kind);
    }

    // ─── 3. 並び順（成長 → 損失） ──────────────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_OrdersGrowthBeforeLosses()
    {
        var grower = Guid.NewGuid();
        var retiree = Guid.NewGuid();

        var growerUnit = MakeUnit(grower, JobId.StandardBearer);
        var retireeUnit = MakeUnit(retiree, JobId.HeavyInfantry) with { Age = 60 };
        var spoils = SpoilsWithGains(new UnitLevelGain { UnitId = grower, FromLevel = 1, ToLevel = 2 });

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 9,
            departedRoster: ImmutableArray.Create(retireeUnit),
            battleSpoils: spoils,
            levelGainUnitLookup: Lookup(growerUnit, retireeUnit));

        Assert.Equal(2, entries.Length);
        // 先頭が成長（昇級）、続いて損失（引退）。
        Assert.Equal(ChronicleEventKind.LevelGained, entries[0].Kind);
        Assert.Equal(ChronicleEventKind.Retired, entries[1].Kind);
    }

    // ─── 4. ヌル安全 ───────────────────────────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_NullSpoils_StillEmitsLosses()
    {
        var id = Guid.NewGuid();
        var fallen = MakeUnit(id) with { IsDead = true };

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 4,
            departedRoster: ImmutableArray.Create(fallen),
            battleSpoils: null,                 // 戦果なし（非戦闘でも落ちない）
            levelGainUnitLookup: null);

        Assert.Equal(ChronicleEventKind.KilledInAction, Assert.Single(entries).Kind);
    }

    [Fact]
    public void BuildGenerationEntries_NullLookup_SkipsGrowthButKeepsLosses()
    {
        var grower = Guid.NewGuid();
        var retiree = Guid.NewGuid();
        var retireeUnit = MakeUnit(retiree) with { Age = 60 };
        var spoils = SpoilsWithGains(new UnitLevelGain { UnitId = grower, FromLevel = 1, ToLevel = 3 });

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 6,
            departedRoster: ImmutableArray.Create(retireeUnit),
            battleSpoils: spoils,
            levelGainUnitLookup: null);          // 名前解決不能 → 昇級は出さない

        var entry = Assert.Single(entries);
        Assert.Equal(ChronicleEventKind.Retired, entry.Kind);
    }

    // ─── 5. 空入力 ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_EmptyEverything_ReturnsEmpty()
    {
        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 0,
            departedRoster: ImmutableArray<Unit>.Empty,
            battleSpoils: BattleSpoils.Empty,    // 戦果ゼロ
            levelGainUnitLookup: Lookup());

        Assert.Empty(entries);
    }

    // ─── 6. 世代見出しの一様性 + 複合 ──────────────────────────────────────

    [Fact]
    public void BuildGenerationEntries_StampsGenerationUniformly_AcrossAllKinds()
    {
        var grower = Guid.NewGuid();
        var retiree = Guid.NewGuid();
        var fallen = Guid.NewGuid();

        var growerUnit = MakeUnit(grower, JobId.Tactician);
        var retireeUnit = MakeUnit(retiree, JobId.Medic) with { Age = 70 };
        var fallenUnit = MakeUnit(fallen, JobId.Sniper) with { Age = 29, IsDead = true };

        var spoils = SpoilsWithGains(new UnitLevelGain { UnitId = grower, FromLevel = 3, ToLevel = 5 });

        var entries = ChronicleLog.BuildGenerationEntries(
            generation: 42,
            departedRoster: ImmutableArray.Create(retireeUnit, fallenUnit),
            battleSpoils: spoils,
            levelGainUnitLookup: Lookup(growerUnit, retireeUnit, fallenUnit));

        Assert.Equal(3, entries.Length);
        Assert.All(entries, e => Assert.Equal(42, e.Generation));

        // 種別の内訳（成長 1 + 引退 1 + 戦死 1）。
        Assert.Single(entries, e => e.Kind == ChronicleEventKind.LevelGained);
        Assert.Single(entries, e => e.Kind == ChronicleEventKind.Retired);
        Assert.Single(entries, e => e.Kind == ChronicleEventKind.KilledInAction);
    }

    // ─── 7. 手動解雇（戦力外通告）の年代記化 ────────────────────────────────

    [Fact]
    public void BuildDismissalEntry_CapturesFinalStatus_FromStillPicture()
    {
        var id = Guid.NewGuid();
        // 寿命前の現役を編成判断で外す静止画（年齢・最終レベルの双方が意味を持つ）。
        var dismissed = MakeUnit(id, JobId.Scout, "dismissed-first", "dismissed-last")
            with { Age = 33, Level = 3 };

        var entry = ChronicleLog.BuildDismissalEntry(generation: 11, dismissed);

        Assert.Equal(ChronicleEventKind.Dismissed, entry.Kind);
        Assert.Equal(11, entry.Generation);
        Assert.Equal("dismissed-first", entry.UnitFirstNameKey);
        Assert.Equal("dismissed-last", entry.UnitLastNameKey);
        Assert.Equal(JobId.Scout, entry.Job);
        // 解雇は Age（解雇時年齢）と FromLevel（解雇時の最終 Lv）を運ぶ。ToLevel は未使用。
        Assert.Equal(33, entry.Age);
        Assert.Equal(3, entry.FromLevel);
        Assert.Equal(0, entry.ToLevel);
    }

    [Fact]
    public void BuildDismissalEntry_NullUnit_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ChronicleLog.BuildDismissalEntry(generation: 1, dismissed: null!));
    }
}
