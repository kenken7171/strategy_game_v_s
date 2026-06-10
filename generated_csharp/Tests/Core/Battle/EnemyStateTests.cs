// =============================================================================
//  ChronicleKnights — EnemyStateTests.cs
// -----------------------------------------------------------------------------
//  敵の完全不変スナップショット EnemyState の不変セマンティクスを検証する。
//    - 満タン生成と不変条件（範囲外で例外）
//    - 被弾で「元インスタンスは無変更・HP を減らした新インスタンス」を返す
//    - 0 ダメージ／撃破済みオーバーキルは自身をそのまま返す（不要な再生成を避ける）
//    - 派生ゲッタ（IsAlive / IsDefeated / HpRatio）の正しさ
//    - record の値等価性（同一フィールドなら等しい）
//
//  開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using ChronicleKnights.Core.Battle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class EnemyStateTests
{
    // ─── 1. 生成と不変条件 ─────────────────────────────────────────────────

    [Fact]
    public void CreateAtFullHealth_StartsAtMaxHp()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 500, 40, 120);

        Assert.Equal(EnemyArchetype.TrialGuardian, enemy.Archetype);
        Assert.Equal(500, enemy.MaxHp);
        Assert.Equal(500, enemy.Hp);
        Assert.Equal(40, enemy.Attack);
        Assert.Equal(120, enemy.Speed);
        Assert.True(enemy.IsAlive);
        Assert.False(enemy.IsDefeated);
        Assert.Equal(1.0, enemy.HpRatio);
    }

    [Fact]
    public void CreateAtFullHealth_MaxHpBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 0, 10, 10));
    }

    [Fact]
    public void CreateAtFullHealth_NegativeAttack_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, -1, 10));
    }

    [Fact]
    public void CreateAtFullHealth_NegativeSpeed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 10, -1));
    }

    // ─── 2. 被弾の不変セマンティクス ───────────────────────────────────────

    [Fact]
    public void WithDamageTaken_ReturnsNewInstance_AndLeavesOriginalUntouched()
    {
        var original = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 200, 30, 80);

        var damaged = original.WithDamageTaken(70);

        // 新インスタンスは HP が減っている。
        Assert.Equal(130, damaged.Hp);
        Assert.Equal(200, damaged.MaxHp);
        Assert.Equal(30, damaged.Attack);
        Assert.Equal(80, damaged.Speed);

        // 元インスタンスは 1 ミリも変わらない（不変保証）。
        Assert.Equal(200, original.Hp);
        Assert.False(ReferenceEquals(original, damaged));
    }

    [Fact]
    public void WithDamageTaken_ClampsAtZero_AndMarksDefeated()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80);

        var slain = enemy.WithDamageTaken(250);

        Assert.Equal(0, slain.Hp);
        Assert.True(slain.IsDefeated);
        Assert.False(slain.IsAlive);
        Assert.Equal(0.0, slain.HpRatio);
        Assert.Equal(100, enemy.Hp); // 元は無変更
    }

    [Fact]
    public void WithDamageTaken_ExactLethal_ReachesZero()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80);

        var slain = enemy.WithDamageTaken(100);

        Assert.Equal(0, slain.Hp);
        Assert.True(slain.IsDefeated);
    }

    [Fact]
    public void WithDamageTaken_ZeroAmount_ReturnsSameInstance()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80);

        Assert.Same(enemy, enemy.WithDamageTaken(0));
    }

    [Fact]
    public void WithDamageTaken_OverkillOnDefeated_ReturnsSameInstance()
    {
        var defeated = EnemyState
            .CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80)
            .WithDamageTaken(100);

        // 既に HP 0。追撃しても HP は変わらないので自身を返す。
        Assert.Same(defeated, defeated.WithDamageTaken(50));
    }

    [Fact]
    public void WithDamageTaken_NegativeAmount_Throws()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80);

        Assert.Throws<ArgumentOutOfRangeException>(() => enemy.WithDamageTaken(-1));
    }

    [Fact]
    public void WithDamageTaken_CanBeChained_Immutably()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 100, 30, 80);

        var afterTwoHits = enemy.WithDamageTaken(30).WithDamageTaken(45);

        Assert.Equal(25, afterTwoHits.Hp);
        Assert.Equal(100, enemy.Hp); // 連鎖しても起点は不変
    }

    // ─── 3. 派生ゲッタ ─────────────────────────────────────────────────────

    [Fact]
    public void HpRatio_ReflectsRemainingFraction()
    {
        var enemy = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 200, 30, 80);

        var half = enemy.WithDamageTaken(100);

        Assert.Equal(0.5, half.HpRatio);
    }

    // ─── 4. record 値等価性 ────────────────────────────────────────────────

    [Fact]
    public void Equality_SameFields_AreEqual()
    {
        var a = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 300, 25, 90);
        var b = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 300, 25, 90);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentHp_AreNotEqual()
    {
        var full = EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 300, 25, 90);
        var hurt = full.WithDamageTaken(10);

        Assert.NotEqual(full, hurt);
    }
}
