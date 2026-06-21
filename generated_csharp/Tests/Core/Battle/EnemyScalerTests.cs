// =============================================================================
//  ChronicleKnights — EnemyScalerTests.cs
// -----------------------------------------------------------------------------
//  敵の決定論的スケーリング純粋ファクトリ EnemyScaler を網羅検証する。
//
//  検証の柱:
//    1. 決定論: 同一シードの Random を 2 本渡せば、出力 EnemyState は完全一致する。
//    2. 個体差（揺らぎ）の固定: NextDouble を固定する Random テストダブルで、
//       揺らぎを止めた／下限／上限の各ケースの正確な数値を断定する。
//    3. 成長曲線: 世代（年数）とレベルの進行に応じて最大 HP・攻撃力・速度が
//       想定通りスケールすること（例: Lv3 HP は Lv1 HP の 2 倍）。
//    4. 乱数消費順序: HP → 攻撃 → 速度 の順で 1 回ずつ消費すること。
//    5. 引数防御: null 乱数・負の世代・最小レベル未満で例外。
//
//  ★ 乱数は 100% 外部注入。Random.Shared 等のグローバル乱数は一切使わないため、
//    本テストはどの環境でも同じ結果になる（要件②の構造的保証）。
//  ★ 開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using ChronicleKnights.Core.Battle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class EnemyScalerTests
{
    // ─── 乱数テストダブル（NextDouble は virtual なのでオーバーライド可能） ──

    /// <summary>NextDouble が常に同一値を返す乱数（揺らぎを固定する）。</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    /// <summary>NextDouble が渡された配列を順に返す乱数（消費順序の検証用）。</summary>
    private sealed class SequencedRandom : Random
    {
        private readonly double[] _values;
        private int _index;
        public SequencedRandom(params double[] values) => _values = values;
        public override double NextDouble() => _values[_index++];
    }

    // 揺らぎ係数の代表点: 0.0 → 0.85（下限） / 0.5 → 1.0（無揺らぎ） / 1.0 → 1.15（上限）。
    private const double NoJitterSample = 0.5;
    private const double FloorJitterSample = 0.0;
    private const double CeilingJitterSample = 1.0;

    // ─── 1. 決定論 ─────────────────────────────────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_SameSeed_ProducesIdenticalState()
    {
        var first = EnemyScaler.ScaleTrialGuardian(50, 2, new Random(12345));
        var second = EnemyScaler.ScaleTrialGuardian(50, 2, new Random(12345));

        // record 値等価性で「全フィールド一致」を一括断定（決定論の証明）。
        Assert.Equal(first, second);
    }

    [Fact]
    public void ScaleTrialGuardian_DefaultLevelOverload_EqualsExplicitBaseLevel()
    {
        var viaOverload = EnemyScaler.ScaleTrialGuardian(30, new Random(777));
        var viaExplicit = EnemyScaler.ScaleTrialGuardian(30, EnemyScaler.BaseLevel, new Random(777));

        Assert.Equal(viaExplicit, viaOverload);
    }

    // ─── 2. 揺らぎ固定での正確値 ───────────────────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_NoJitter_Year0Level1_ProducesExactBaseStats()
    {
        var enemy = EnemyScaler.ScaleTrialGuardian(0, 1, new FixedRandom(NoJitterSample));

        // (150 + 0) * 6 = 900 / (30 + 0) = 30 / (100 + 0) = 100
        Assert.Equal(900, enemy.MaxHp);
        Assert.Equal(30, enemy.Attack);
        Assert.Equal(100, enemy.Speed);
    }

    [Fact]
    public void ScaleTrialGuardian_NoJitter_Year100Level1_MatchesCanonicalBaseline()
    {
        var enemy = EnemyScaler.ScaleTrialGuardian(100, 1, new FixedRandom(NoJitterSample));

        // 基準値: era HP=650(×6=3900), ATK=90, SPD=160。
        Assert.Equal(3900, enemy.MaxHp);
        Assert.Equal(90, enemy.Attack);
        Assert.Equal(160, enemy.Speed);
    }

    [Fact]
    public void ScaleTrialGuardian_FloorJitter_AppliesMinus15Percent()
    {
        var enemy = EnemyScaler.ScaleTrialGuardian(0, 1, new FixedRandom(FloorJitterSample));

        // 900 * 0.85 = 765 / 100 * 0.85 = 85
        Assert.Equal(765, enemy.MaxHp);
        Assert.Equal(85, enemy.Speed);
    }

    [Fact]
    public void ScaleTrialGuardian_CeilingJitter_AppliesPlus15Percent()
    {
        var enemy = EnemyScaler.ScaleTrialGuardian(0, 1, new FixedRandom(CeilingJitterSample));

        // 900 * 1.15 = 1035 / 100 * 1.15 = 115
        Assert.Equal(1035, enemy.MaxHp);
        Assert.Equal(115, enemy.Speed);
    }

    // ─── 3. 成長曲線（世代・レベル） ───────────────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_YearProgression_IncreasesEveryStat()
    {
        var young = EnemyScaler.ScaleTrialGuardian(0, 1, new FixedRandom(NoJitterSample));
        var old = EnemyScaler.ScaleTrialGuardian(100, 1, new FixedRandom(NoJitterSample));

        Assert.True(old.MaxHp > young.MaxHp);
        Assert.True(old.Attack > young.Attack);
        Assert.True(old.Speed > young.Speed);
    }

    [Fact]
    public void ScaleTrialGuardian_LevelProgression_ScalesStatsMonotonically()
    {
        var level1 = EnemyScaler.ScaleTrialGuardian(0, 1, new FixedRandom(NoJitterSample));
        var level2 = EnemyScaler.ScaleTrialGuardian(0, 2, new FixedRandom(NoJitterSample));
        var level3 = EnemyScaler.ScaleTrialGuardian(0, 3, new FixedRandom(NoJitterSample));

        // Lv1=×1.0 / Lv2=×1.5 / Lv3=×2.0（PerLevelGain=0.5）。HP は ×6 集約。
        Assert.Equal(900, level1.MaxHp);
        Assert.Equal(1350, level2.MaxHp);
        Assert.Equal(1800, level3.MaxHp);

        // 単調増加かつ「Lv3 HP は Lv1 HP の 2 倍」。
        Assert.True(level1.MaxHp < level2.MaxHp);
        Assert.True(level2.MaxHp < level3.MaxHp);
        Assert.Equal(level1.MaxHp * 2, level3.MaxHp);

        // 攻撃・速度も同じ倍率で伸びる。
        Assert.Equal(45, level2.Attack);
        Assert.Equal(150, level2.Speed);
    }

    // ─── 4. 乱数消費順序（HP → 攻撃 → 速度） ───────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_ConsumesRngInHpAttackSpeedOrder()
    {
        // 連続する 3 値を HP / 攻撃 / 速度 が順に消費する。
        //   HP    ← 0.0 → jitter 0.85 → 900 * 0.85 = 765
        //   攻撃  ← 0.5 → jitter 1.00 →  30 * 1.00 =  30
        //   速度  ← 1.0 → jitter 1.15 → 100 * 1.15 = 115
        var rng = new SequencedRandom(
            FloorJitterSample, NoJitterSample, CeilingJitterSample);

        var enemy = EnemyScaler.ScaleTrialGuardian(0, 1, rng);

        Assert.Equal(765, enemy.MaxHp);
        Assert.Equal(30, enemy.Attack);
        Assert.Equal(115, enemy.Speed);
    }

    // ─── 5. 生成結果の健全性 ───────────────────────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_ProducesEnemyAtFullHealth()
    {
        var enemy = EnemyScaler.ScaleTrialGuardian(40, 2, new Random(2024));

        Assert.Equal(EnemyArchetype.TrialGuardian, enemy.Archetype);
        Assert.Equal(enemy.MaxHp, enemy.Hp);
        Assert.True(enemy.IsAlive);
    }

    // ─── 6. 引数防御 ───────────────────────────────────────────────────────

    [Fact]
    public void ScaleTrialGuardian_NullRng_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => EnemyScaler.ScaleTrialGuardian(10, 1, null!));
    }

    [Fact]
    public void ScaleTrialGuardian_NegativeYear_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnemyScaler.ScaleTrialGuardian(-1, 1, new FixedRandom(NoJitterSample)));
    }

    [Fact]
    public void ScaleTrialGuardian_LevelBelowBase_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnemyScaler.ScaleTrialGuardian(10, 0, new FixedRandom(NoJitterSample)));
    }
}
