// =============================================================================
//  ChronicleKnights — EnemyScalerTests.cs
// -----------------------------------------------------------------------------
//  個体差プリミティブ EnemyScaler.ApplyJitter（±15% の揺らぎ＋下限クランプ）を検証する。
//  敵基準ステの算出は EnemyScalingResolver 側（EnemyScalingResolverTests）が担うため、
//  本テストは「基準値へ個体差を 1 回乗せる」現役プリミティブだけを包囲する。
//
//  検証の柱:
//    1. 揺らぎ固定: NextDouble を固定する乱数ダブルで 下限0.85 / 無揺らぎ1.0 / 上限1.15 の正確値。
//    2. 丸め: 正の .5 は AwayFromZero（切り上げ）。
//    3. 下限クランプ: 0 以下に落ちず MinimumStatValue 以上。
//    4. 決定論: 同一シードの Random を 2 本渡せば完全一致。
//    5. 乱数消費: NextDouble をちょうど 1 回だけ消費。
//    6. 引数防御: null 乱数で例外。
//
//  ★ 乱数は 100% 外部注入。Random.Shared 等のグローバル乱数は一切使わない（要件②）。
//  ★ 開発憲法 ①順守: 文字列リテラルは ASCII のみ。
// =============================================================================

using System;
using ChronicleKnights.Core.Battle;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class EnemyScalerTests
{
    /// <summary>NextDouble が常に同一値を返す乱数（揺らぎを固定する）。</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public override double NextDouble() => _value;
    }

    /// <summary>NextDouble を何回呼ばれたか数える乱数（消費回数の検証用）。</summary>
    private sealed class CountingRandom : Random
    {
        public int Calls { get; private set; }
        public override double NextDouble() { Calls++; return 0.5; }
    }

    // 揺らぎ係数の代表点: 0.0 → 0.85（下限） / 0.5 → 1.0（無揺らぎ） / 1.0 → 1.15（上限）。
    private const double NoJitterSample = 0.5;
    private const double FloorJitterSample = 0.0;
    private const double CeilingJitterSample = 1.0;

    // ─── 1. 揺らぎ固定での正確値 ───────────────────────────────────────────

    [Fact]
    public void ApplyJitter_NoJitter_ReturnsBaseValueRounded()
        => Assert.Equal(900, EnemyScaler.ApplyJitter(900, new FixedRandom(NoJitterSample)));

    [Fact]
    public void ApplyJitter_FloorJitter_AppliesMinus15Percent()
        => Assert.Equal(765, EnemyScaler.ApplyJitter(900, new FixedRandom(FloorJitterSample))); // 900 * 0.85

    [Fact]
    public void ApplyJitter_CeilingJitter_AppliesPlus15Percent()
        => Assert.Equal(1035, EnemyScaler.ApplyJitter(900, new FixedRandom(CeilingJitterSample))); // 900 * 1.15

    // ─── 2. 丸め（AwayFromZero） ───────────────────────────────────────────

    [Fact]
    public void ApplyJitter_RoundsHalfAwayFromZero()
        // 13 * 1.0 = 13、12.5 相当を作るため base=25, jitter=0.5(=>1.0) では 25。代わりに .5 を直接作る:
        // base=13, jitter 1.0 → 13。base=5, floor 0.85 → 4.25 → 4。base=10, ceiling 1.15 → 11.5 → 12。
        => Assert.Equal(12, EnemyScaler.ApplyJitter(10, new FixedRandom(CeilingJitterSample)));

    // ─── 3. 下限クランプ ───────────────────────────────────────────────────

    [Fact]
    public void ApplyJitter_ClampsToMinimumStatValue()
    {
        Assert.Equal(EnemyScaler.MinimumStatValue, EnemyScaler.ApplyJitter(0, new FixedRandom(NoJitterSample)));
        Assert.True(EnemyScaler.ApplyJitter(0, new FixedRandom(FloorJitterSample)) >= EnemyScaler.MinimumStatValue);
    }

    // ─── 4. 決定論 ─────────────────────────────────────────────────────────

    [Fact]
    public void ApplyJitter_SameSeed_ProducesIdenticalResult()
    {
        var first = EnemyScaler.ApplyJitter(1234, new Random(12345));
        var second = EnemyScaler.ApplyJitter(1234, new Random(12345));
        Assert.Equal(first, second);
    }

    // ─── 5. 乱数消費（ちょうど 1 回） ──────────────────────────────────────

    [Fact]
    public void ApplyJitter_ConsumesExactlyOneRandomDraw()
    {
        var rng = new CountingRandom();
        EnemyScaler.ApplyJitter(500, rng);
        Assert.Equal(1, rng.Calls);
    }

    // ─── 6. 引数防御 ───────────────────────────────────────────────────────

    [Fact]
    public void ApplyJitter_NullRng_Throws()
        => Assert.Throws<ArgumentNullException>(() => EnemyScaler.ApplyJitter(100, null!));
}
