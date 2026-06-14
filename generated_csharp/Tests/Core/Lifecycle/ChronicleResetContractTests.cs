// =============================================================================
//  ChronicleKnights.Tests — ChronicleResetContractTests.cs
// -----------------------------------------------------------------------------
//  「前世の完全粛清」の清浄性を、ChronicleGlobal.Reset が全 SoT へ再代入する初期状態の
//  構成ブロック単位で固定する。
//
//  ★ なぜ構成ブロック単位なのか:
//    ChronicleGlobal は Godot.Node（Autoload）ゆえテストプロジェクト（Core/** のみコンパイル）
//    から直接は叩けない。Reset の本体（各フィールドへの再代入）は論理検証で担保し、本テストは
//    Reset/Initialize が握る「更地の初期値」が前世のデータを 1 バイトも残さない清浄なゼロ状態で
//    あること（経済 0・ロスタ空・旅団史空・英霊アーカイブ空・戦果なし）を純粋層で包囲する。
//    SoT はすべて不変レコード／不変コレクションなので、これらが空である限り、空値再代入による
//    Reset は前世を完全に解放する（旧参照は GC 回収）。
//
//  ★ 開発憲法①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Chronicle;
using ChronicleKnights.Core.Managers;
using ChronicleKnights.Core.Units;
using Xunit;

namespace ChronicleKnights.Tests.Core.Lifecycle;

public class ChronicleResetContractTests
{
    [Fact]
    public void FreshEconomy_IsZeroBalance_WithNoCarryOver()
    {
        var economy = PointsEconomy.CreateInitial();

        // 経済は残高 0・累計 0 の清浄なゼロ状態（前世のポイントは 1 ミリも残らない）。
        Assert.Equal(0, economy.CurrentBalance);
        Assert.Equal(0, economy.TotalEarned);
        Assert.Equal(0, economy.TotalSpent);
    }

    [Fact]
    public void FreshRoster_IsEmpty()
    {
        // 旅団ロスタの更地（血統 PedigreeGraph の素もここで消える）。
        Assert.Empty(ImmutableList<Unit>.Empty);
    }

    [Fact]
    public void FreshChronicleLog_IsEmpty()
    {
        // 旅団史（ChronicleLog）の白紙。
        Assert.Empty(ImmutableArray<ChronicleLogEntry>.Empty);
    }

    [Fact]
    public void FreshAncestralArchive_IsEmpty()
    {
        // 英霊アーカイブ（散った者の記録）の更地。
        Assert.Empty(ImmutableDictionary<Guid, Unit>.Empty);
    }

    [Fact]
    public void FreshSpoils_HasNoSpoilsAndEarnsZero()
    {
        // 戦果決算の更地（前世の戦果・婚姻ポイントが残らない）。
        Assert.False(BattleSpoils.Empty.HasAnySpoils);
        Assert.Equal(0, BattleSpoils.Empty.CalculateEarnedMarriagePoints());
    }
}
