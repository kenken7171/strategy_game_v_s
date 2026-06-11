// =============================================================================
//  ChronicleKnights.Tests — SampleData.cs
// -----------------------------------------------------------------------------
//  テスト全体で共有する決定論的なサンプル状態のファクトリ。
//
//  ラウンドトリップ等価性テスト等で再利用する「経済・タイムライン・ロスター」の
//  3 状態を、固定 Guid と固定値で構築する。固定 Guid を使うことで、テスト側で
//  個々のユニット・装備・好感度キーを正確にアサートできる。
//
//  ★ 旅団員には「装備あり + 好感度あり (IronWallKnight)」と「装備なし + 好感度なし
//    (Sniper)」の両極端を混在させ、シリアライズの分岐 (null / 空辞書 / 非空) を
//    すべて踏むようにしている。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Tests.TestSupport;

/// <summary>テストで共有する決定論的なサンプル状態の生成ヘルパー。</summary>
public static class SampleData
{
    // ─── 固定 Guid（アサート用の既知の値） ───────────────────────────────

    public static readonly Guid IronWallUnitId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid IronWallEquipmentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid AffinityTargetId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly Guid SniperUnitId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>IronWallKnight が AffinityTargetId に対して持つ好感度値。</summary>
    public const int IronWallAffinityValue = 120;

    // ─── 状態スナップショット ─────────────────────────────────────────────

    /// <summary>
    /// 経済・タイムライン・ロスター・旅団史の 4 状態の束。テスト間で受け渡しやすい
    /// 軽量レコード。
    /// </summary>
    public sealed record State
    {
        public required PointsEconomy Economy { get; init; }
        public required TimelineEngine Timeline { get; init; }
        public required ImmutableList<Unit> Roster { get; init; }
        public required ImmutableArray<ChronicleLogEntry> ChronicleLog { get; init; }
    }

    /// <summary>
    /// 決定論的なサンプル状態を構築する。
    ///   - 経済   : 残高 9 / 累計獲得 11 / 累計消費 2
    ///   - タイムライン: DefaultGenerator(new Random(2024)) による 3 予言
    ///   - ロスター : IronWallKnight(Lv3, 装備+好感度あり) と Sniper(Lv1, 装備なし)
    ///   - 旅団史   : 昇級(LevelGained) 1 行 + 解雇(Dismissed) 1 行（永続化ラウンドトリップ用）
    /// </summary>
    public static State BuildState()
    {
        var economy = new PointsEconomy
        {
            CurrentBalance = 9,
            TotalEarned    = 11,
            TotalSpent     = 2,
        };

        // タイムラインは DefaultGenerator で 3 予言を生成（seeded で Kind/年数は再現）。
        var timeline = TimelineEngine.CreateInitial(
            TimelineEngine.DefaultGenerator, new Random(2024));

        // 装備あり + 好感度あり（シリアライズの非 null / 非空辞書ルートを踏む）
        var ironWall = new Unit
        {
            Id           = IronWallUnitId,
            Job          = JobId.IronWallKnight,
            Age          = 30,
            MaxAge       = 60,
            FirstNameKey = "name-sample-ironwall",
            LastNameKey  = "name-family-sample",
            Origin       = Origin.Japanese,
            Level        = 3,
            MainEquipment = new Equipment
            {
                Id        = IronWallEquipmentId,
                ItemId    = ItemId.SwordKnight,
                Level     = 3,
                AffixKeys = ImmutableArray.Create("affix-sample-guard", "affix-sample-speed"),
            },
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty
                .Add(AffinityTargetId, IronWallAffinityValue),
            IsDead = false,
        };

        // 装備なし + 好感度なし（シリアライズの null / 空辞書ルートを踏む）
        var sniper = new Unit
        {
            Id            = SniperUnitId,
            Job           = JobId.Sniper,
            Age           = 19,
            MaxAge        = 58,
            FirstNameKey  = "name-sample-sniper",
            LastNameKey   = "name-family-sample",
            Origin        = Origin.European,
            Level         = 1,
            MainEquipment = null,
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty,
            IsDead        = false,
        };

        var roster = ImmutableList.Create(ironWall, sniper);

        // 旅団史 2 行: 昇級(成長系・FromLevel/ToLevel を使う)と解雇(損失系・Age/FromLevel を使う)
        // の両分岐を踏み、シリアライズが全フィールドを取りこぼさないことを担保する。
        var chronicleLog = ImmutableArray.Create(
            new ChronicleLogEntry
            {
                Generation       = 2,
                Kind             = ChronicleEventKind.LevelGained,
                UnitFirstNameKey = "name-sample-ironwall",
                UnitLastNameKey  = "name-family-sample",
                Job              = JobId.IronWallKnight,
                FromLevel        = 2,
                ToLevel          = 3,
            },
            new ChronicleLogEntry
            {
                Generation       = 4,
                Kind             = ChronicleEventKind.Dismissed,
                UnitFirstNameKey = "name-sample-sniper",
                UnitLastNameKey  = "name-family-sample",
                Job              = JobId.Sniper,
                Age              = 41,
                FromLevel        = 3,
            });

        return new State
        {
            Economy      = economy,
            Timeline     = timeline,
            Roster       = roster,
            ChronicleLog = chronicleLog,
        };
    }
}
