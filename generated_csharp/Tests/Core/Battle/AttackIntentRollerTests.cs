// =============================================================================
//  ChronicleKnights — AttackIntentRollerTests.cs
// -----------------------------------------------------------------------------
//  敵行動の N ターン先読み（運命の帯）を組む純粋関数 AttackIntentRoller.Forecast の
//  決定論・冪等性・番兵・実戦整合を網羅検証する xUnit スイート。
//
//  ★ 検証の核（要件②・未来確定アサーションの完全包囲）:
//    同じシード・同じ敵・同じ局面から Forecast を何千回呼んでも、戻り値の攻撃予告配列の
//    内容が 1 ビットの狂いもなく完全同一であることを厳格に検証する（決定論＝予言の信頼性）。
//
//  ★ 比較の注意（ImmutableArray の参照等価の罠を回避）:
//    AttackIntent は record だが、その TargetRows（ImmutableArray<SquadRow>）の等価は
//    「下層配列の参照一致」で判定される。SingleStrike は呼び出しごとに新配列を確保するため、
//    内容は同一でも record 等価では不一致になりうる。よって本スイートは各予告を ASCII の
//    「指紋」文字列（Kind|SkillKey|Damage|Rows）へ射影し、内容ベースで厳密に突き合わせる。
//
//  ★ 実戦整合（faithful projection）:
//    実戦 BattleResolver は 1 本の乱数器を毎ターン Roll へ通して次手を確定する。Forecast の
//    帯が「同一シードから立てた 1 本の乱数器で Roll を連続実行した列」と完全一致することを
//    検証し、予言が実戦の行動列を忠実に先取りしている（ズレない）ことを担保する。
//
//  開発憲法 ①（日本語直接書き込み禁止）順守: 文字列リテラルは ASCII のみ（コメントは日本語可）。
// =============================================================================

using System;
using System.Collections.Generic;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Formation;
using ChronicleKnights.Core.Job;
using Xunit;

namespace ChronicleKnights.Tests.Core.Battle;

public class AttackIntentRollerTests
{
    // ─── テスト用ファクトリ / 指紋ヘルパー ──────────────────────────────────

    /// <summary>固定ステータスの試練ボスを生成する（attack だけ可変・威力決定論の検証用）。</summary>
    private static EnemyState Boss(int attack = 40)
        => EnemyState.CreateAtFullHealth(EnemyArchetype.TrialGuardian, 500, attack, 120);

    /// <summary>
    /// 1 つの攻撃予告を「内容だけ」を表す ASCII 指紋へ射影する。ImmutableArray の参照等価に
    /// 惑わされず、種別・スキルキー・威力・対象行の中身を 1 ビット単位で比較するための鍵。
    /// </summary>
    private static string Fingerprint(AttackIntent intent)
        => $"{intent.Kind}|{intent.SkillNameKey}|{intent.DamagePerUnit}|{string.Join(",", intent.TargetRows)}";

    /// <summary>運命の帯（予告列）を指紋列へ射影する。</summary>
    private static List<string> Fingerprint(IReadOnlyList<AttackIntent> band)
    {
        var prints = new List<string>(band.Count);
        foreach (var intent in band)
        {
            prints.Add(Fingerprint(intent));
        }
        return prints;
    }

    // ─── 1. 引数ガード（番兵） ───────────────────────────────────────────────

    [Fact]
    public void Forecast_NullEnemy_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => AttackIntentRoller.Forecast(null!, 1u, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-9999)]
    public void Forecast_NonPositiveLookahead_ReturnsEmptyBand(int lookahead)
    {
        var band = AttackIntentRoller.Forecast(Boss(), 7u, lookahead);

        Assert.Empty(band); // 覗く未来なし。例外を投げず空帯で穏当に倒す。
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Forecast_ReturnsExactlyRequestedLength(int lookahead)
    {
        var band = AttackIntentRoller.Forecast(Boss(), 7u, lookahead);

        Assert.Equal(lookahead, band.Count);
    }

    [Fact]
    public void Forecast_OversizedLookahead_ClampsToMaxForecastTurns()
    {
        // 過大値は上限でクランプ（過剰確保・長時間ループの構造的封じ込め）。
        var band = AttackIntentRoller.Forecast(
            Boss(), 7u, AttackIntentRoller.MaxForecastTurns + 1000);

        Assert.Equal(AttackIntentRoller.MaxForecastTurns, band.Count);
    }

    // ─── 2. 決定論・冪等性（未来確定アサーションの完全包囲） ──────────────────

    [Fact]
    public void Forecast_IsByteForByteDeterministic_AcrossManyCalls()
    {
        var enemy = Boss(37);
        const uint seed = 0x00C0FFEEu;
        const int lookahead = 8;

        var baseline = Fingerprint(AttackIntentRoller.Forecast(enemy, seed, lookahead));

        // 同じシード・同じ敵・同じ局面から何千回呼んでも帯は 1 ビットも揺るがない。
        for (int i = 0; i < 5000; i++)
        {
            var again = Fingerprint(AttackIntentRoller.Forecast(enemy, seed, lookahead));
            Assert.Equal(baseline, again);
        }
    }

    [Fact]
    public void Forecast_SameSeed_SameEnemyFields_DistinctInstances_AreIdentical()
    {
        // 敵は別インスタンスでもフィールドが同じなら、同一シードの帯は完全一致する。
        var first = Fingerprint(AttackIntentRoller.Forecast(Boss(50), 99u, 6));
        var second = Fingerprint(AttackIntentRoller.Forecast(Boss(50), 99u, 6));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Forecast_DifferentSeeds_ProduceVariety()
    {
        // 予言がシードに鋭敏（定数でない）ことの健全性チェック。
        var enemy = Boss(40);
        var distinct = new HashSet<string>();

        for (uint seed = 0; seed < 200u; seed++)
        {
            var band = AttackIntentRoller.Forecast(enemy, seed, 4);
            distinct.Add(string.Join(">", Fingerprint(band)));
        }

        Assert.True(distinct.Count > 1, "Forecast must vary with the seed (not a constant).");
    }

    // ─── 3. 実戦整合（faithful projection・連続乱数列との一致） ────────────────

    [Fact]
    public void Forecast_HeadElement_EqualsSingleRollFromSameSeed()
    {
        // 帯の先頭（T+1）は、同一シードで立てた乱数器の最初の Roll と一致する。
        var enemy = Boss(44);
        const uint seed = 13579u;

        var band = AttackIntentRoller.Forecast(enemy, seed, 3);
        var directFirst = AttackIntentRoller.Roll(enemy, new Random(unchecked((int)seed)));

        Assert.Equal(Fingerprint(directFirst), Fingerprint(band[0]));
    }

    [Fact]
    public void Forecast_Band_EqualsContinuousRollStreamFromSeed()
    {
        // 帯全体が「同一シードの 1 本の乱数器で Roll を連続実行した列」と完全一致する。
        // これは実戦 BattleResolver の乱数前進様式と同型であり、予言が行動列を忠実に先取る証左。
        var enemy = Boss(28);
        const uint seed = 0x1234ABCDu;
        const int lookahead = 5;

        var band = AttackIntentRoller.Forecast(enemy, seed, lookahead);

        var manualRng = new Random(unchecked((int)seed));
        var manual = new List<AttackIntent>(lookahead);
        for (int i = 0; i < lookahead; i++)
        {
            manual.Add(AttackIntentRoller.Roll(enemy, manualRng));
        }

        Assert.Equal(Fingerprint(manual), Fingerprint(band));
    }

    // ─── 4. 各予告の構造的健全性（範囲外・破綻の不在） ────────────────────────

    [Fact]
    public void Forecast_EveryIntent_HasValidShapeAndPositiveDamage()
    {
        var enemy = Boss(33);
        var band = AttackIntentRoller.Forecast(
            enemy, 4242u, AttackIntentRoller.MaxForecastTurns);

        Assert.Equal(AttackIntentRoller.MaxForecastTurns, band.Count);

        foreach (var intent in band)
        {
            Assert.False(string.IsNullOrEmpty(intent.SkillNameKey));
            Assert.True(intent.DamagePerUnit >= AttackIntentRoller.MinimumDamagePerUnit);

            var expectedRowCount = intent.Kind switch
            {
                AttackPatternKind.SingleStrike => 1,
                AttackPatternKind.Pincer => 2,
                AttackPatternKind.TotalAssault => 3,
                _ => -1,
            };
            Assert.Equal(expectedRowCount, intent.TargetRows.Length);

            // 対象行はすべて盤面の正準行（Front / RearLeft / RearRight）に属する。
            foreach (var row in intent.TargetRows)
            {
                Assert.Contains(row, FormationBoard.RowOrder);
            }
        }
    }
}
