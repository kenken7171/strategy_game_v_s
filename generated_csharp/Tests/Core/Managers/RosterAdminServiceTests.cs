// =============================================================================
//  ChronicleKnights.Tests — RosterAdminServiceTests.cs
// -----------------------------------------------------------------------------
//  人事（戦力外通告 = 手動解雇）純粋ロジック RosterAdminService.TryDismiss の
//  網羅検証。TS 正史 HumanDecisionService.dismissUnit 相当の「プレイヤー意思による
//  現役の即時除去」を、入力の静止画から決定論的に確認する。
//
//  検証の柱:
//    1. 成功       … 指定 Id を 1 名だけ除去し、本体を Dismissed で返す
//    2. 並び順保持 … 残った旅団員は元の登録順を崩さない
//    3. 対象不在   … ロスタに居ない Id は null（no-op・例外なし）
//    4. 単独除去   … 1 名ロスタからの解雇で空ロスタになる
//    5. 不変性     … 入力ロスタを一切破壊しない（新スナップショットのみ変化）
//    6. ヌル契約   … roster が null なら ArgumentNullException
//
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
//    名前キー等はすべて "first-a" のような ASCII プレースホルダを用いる。
// =============================================================================

using System;
using System.Collections.Immutable;
using System.Linq;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Managers;

public class RosterAdminServiceTests
{
    // ─── テスト用ファクトリ ────────────────────────────────────────────────

    /// <summary>生存・Lv1・装備なしのテストユニットを作る（解雇の基準点）。</summary>
    private static Unit MakeUnit(
        Guid id,
        JobId job = JobId.IronWallKnight,
        string first = "first-a",
        string last = "last-a") => new()
    {
        Id = id,
        Job = job,
        Age = 20,
        MaxAge = 60,
        FirstNameKey = first,
        LastNameKey = last,
    };

    // ─── 1. 成功: 指定 1 名を除去して本体を返す ───────────────────────────

    [Fact]
    public void TryDismiss_RemovesTarget_AndReturnsDismissedUnit()
    {
        var targetId = Guid.NewGuid();
        var target = MakeUnit(targetId, JobId.Sniper, "first-target", "last-target");
        var roster = ImmutableList.Create(
            MakeUnit(Guid.NewGuid()),
            target,
            MakeUnit(Guid.NewGuid()));

        var result = RosterAdminService.TryDismiss(roster, targetId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.NewRoster.Count);
        Assert.DoesNotContain(result.NewRoster, u => u.Id == targetId);
        // 返却される本体は除去された当該ユニットそのもの。
        Assert.Equal(target, result.Dismissed);
        Assert.Equal(targetId, result.Dismissed.Id);
        Assert.Equal(JobId.Sniper, result.Dismissed.Job);
    }

    // ─── 2. 残存メンバーの並び順保持 ───────────────────────────────────────

    [Fact]
    public void TryDismiss_PreservesOrderOfRemainingUnits()
    {
        var first = MakeUnit(Guid.NewGuid(), JobId.Medic);
        var middleId = Guid.NewGuid();
        var middle = MakeUnit(middleId, JobId.Tactician);
        var last = MakeUnit(Guid.NewGuid(), JobId.Sorcerer);
        var roster = ImmutableList.Create(first, middle, last);

        var result = RosterAdminService.TryDismiss(roster, middleId);

        Assert.NotNull(result);
        Assert.Equal(new[] { first, last }, result!.NewRoster.ToArray());
    }

    // ─── 3. 対象不在 → null（no-op） ──────────────────────────────────────

    [Fact]
    public void TryDismiss_UnknownId_ReturnsNull()
    {
        var roster = ImmutableList.Create(
            MakeUnit(Guid.NewGuid()),
            MakeUnit(Guid.NewGuid()));

        var result = RosterAdminService.TryDismiss(roster, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void TryDismiss_EmptyRoster_ReturnsNull()
    {
        var result = RosterAdminService.TryDismiss(ImmutableList<Unit>.Empty, Guid.NewGuid());

        Assert.Null(result);
    }

    // ─── 4. 単独除去 → 空ロスタ ───────────────────────────────────────────

    [Fact]
    public void TryDismiss_SingleMemberRoster_BecomesEmpty()
    {
        var soloId = Guid.NewGuid();
        var roster = ImmutableList.Create(MakeUnit(soloId));

        var result = RosterAdminService.TryDismiss(roster, soloId);

        Assert.NotNull(result);
        Assert.Empty(result!.NewRoster);
        Assert.Equal(soloId, result.Dismissed.Id);
    }

    // ─── 5. 不変性（入力を破壊しない） ─────────────────────────────────────

    [Fact]
    public void TryDismiss_DoesNotMutateInputRoster()
    {
        var targetId = Guid.NewGuid();
        var roster = ImmutableList.Create(
            MakeUnit(targetId),
            MakeUnit(Guid.NewGuid()));

        _ = RosterAdminService.TryDismiss(roster, targetId);

        // 入力ロスタは完全に不変（新スナップショットだけが変化する）。
        Assert.Equal(2, roster.Count);
        Assert.Contains(roster, u => u.Id == targetId);
    }

    // ─── 6. ヌル契約 ───────────────────────────────────────────────────────

    [Fact]
    public void TryDismiss_NullRoster_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => RosterAdminService.TryDismiss(null!, Guid.NewGuid()));
    }
}
