// =============================================================================
//  ChronicleKnights — RosterLifecycle.cs
// -----------------------------------------------------------------------------
//  「1 世代 = 時間軸 1 周」の幕引きで旅団ロスタに起きる時間経過を司る純粋層。
//
//  ゲームループ (Chronicle → Guild → Formation → Battle → Chronicle) が 1 周
//  閉じるとき、選択された予言の SkipYears 分だけ全旅団員が一斉に齢を重ねる。
//  その結果として:
//    - 寿命 (MaxAge) に到達したユニットは「完全ロスト」として現役ロスタから外れる
//    - 直前の戦闘で死亡 (IsDead) したユニットも同じく現役ロスタから外れる
//  本クラスは、この「加齢 → 完全ロストの仕分け」を Godot 完全非依存の純粋関数
//  として提供する（xUnit で世代交代の数値挙動を厳密に検証できる）。
//
//  ★ 責務分離（TimelineEngine / PointsEconomy と同じ層別パターン）:
//    - 加齢そのもの (生存ユニットへの WithAgeProgress 適用) は TimelineEngine.
//      ApplyTimeSkipToRoster に委譲し、本クラスは「加齢後の現役 / 離脱の分割」と
//      その結果集約 (GenerationAdvanceResult) に専念する。重複ロジックを持たない。
//    - 経済 (定期収入) と予言再生成は呼び出し側 (ChronicleGlobal) が
//      PointsEconomy / TimelineEngine を順に叩いて束ねる。本クラスは経済・予言に
//      一切触れない（純粋な「人の生き死に」だけを扱う）。
//
//  ★ 完全ロストの不可逆性:
//    離脱したユニットは復元手段を持たない（Unit 仕様 IsRemovedFromRoster）。
//    DepartedRoster は「この世代で失われた者たち」の記録であり、UI 演出
//    （追悼・年代記の記録）や統計に使える。現役ロスタへ戻す用途は想定しない。
//
//  略称（BattalionDefense 等の正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

/// <summary>
/// 1 世代分の時間経過 (加齢 + 完全ロストの仕分け) を適用した結果を集約する不変レコード。
/// </summary>
public sealed record GenerationAdvanceResult
{
    /// <summary>
    /// 加齢後も現役として次世代へ持ち越される旅団員（元のロスタ順を保持）。
    /// </summary>
    public required ImmutableArray<Unit> SurvivingRoster { get; init; }

    /// <summary>
    /// この世代で現役ロスタから外れた旅団員（完全ロスト）。
    /// 離脱理由は寿命到達 (HasReachedMaxAge) または戦闘死 (IsDead) のいずれか。
    /// </summary>
    public required ImmutableArray<Unit> DepartedRoster { get; init; }

    /// <summary>実際に適用したタイムスキップ年数（Prophecy.SkipYears 由来）。</summary>
    public required int AppliedSkipYears { get; init; }

    /// <summary>次世代へ持ち越される現役人数。</summary>
    public int SurvivingCount => SurvivingRoster.Length;

    /// <summary>この世代で失われた人数（完全ロスト）。</summary>
    public int DepartedCount => DepartedRoster.Length;
}

/// <summary>
/// 旅団ロスタの世代交代（加齢と完全ロストの仕分け）を行う純粋な静的サービス。
/// </summary>
public static class RosterLifecycle
{
    /// <summary>
    /// 旅団ロスタに 1 世代分の時間経過を適用し、現役と離脱（完全ロスト）に分割する。
    ///
    /// 処理順序:
    ///   1. TimelineEngine.ApplyTimeSkipToRoster で全生存ユニットを skipYears 加齢する
    ///      （既に死亡 / 寿命到達のユニットは据え置かれる）。
    ///   2. 加齢後の各ユニットを IsRemovedFromRoster で判定し、現役 (SurvivingRoster) と
    ///      離脱 (DepartedRoster) に振り分ける。両リストとも元のロスタ順を保持する。
    ///
    /// 離脱条件 IsRemovedFromRoster = 戦闘死 (IsDead) または寿命到達 (HasReachedMaxAge)。
    /// したがって skipYears = 0 でも、直前の戦闘で死亡したユニットは離脱側へ仕分けされる
    /// （戦闘後のクリーンアップを兼ねる）。
    /// </summary>
    /// <param name="roster">世代交代前の旅団員リスト（不変・null 不可）。</param>
    /// <param name="skipYears">経過させる年数（非負・Prophecy.SkipYears の値）。</param>
    /// <returns>現役・離脱・適用年数を束ねた <see cref="GenerationAdvanceResult"/>。</returns>
    /// <exception cref="ArgumentNullException">roster が null の場合。</exception>
    /// <exception cref="ArgumentOutOfRangeException">skipYears が負の場合。</exception>
    public static GenerationAdvanceResult AdvanceGeneration(
        IReadOnlyList<Unit> roster,
        int skipYears)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (skipYears < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skipYears), skipYears,
                "skipYears must be non-negative for generation-advance semantics");
        }

        // 1. 全生存ユニットを一斉加齢（死亡 / 寿命到達ユニットは据え置き）。
        var aged = TimelineEngine.ApplyTimeSkipToRoster(roster, skipYears);

        // 2. 加齢後の状態で「現役」と「離脱（完全ロスト）」に仕分ける。
        var surviving = ImmutableArray.CreateBuilder<Unit>(aged.Length);
        var departed = ImmutableArray.CreateBuilder<Unit>();
        foreach (var unit in aged)
        {
            if (unit.IsRemovedFromRoster)
            {
                departed.Add(unit);
            }
            else
            {
                surviving.Add(unit);
            }
        }

        return new GenerationAdvanceResult
        {
            SurvivingRoster = surviving.ToImmutable(),
            DepartedRoster = departed.ToImmutable(),
            AppliedSkipYears = skipYears,
        };
    }
}
