// =============================================================================
//  ChronicleKnights — MetricsReporter.cs
// -----------------------------------------------------------------------------
//  実走ログ（[metrics] 行）からマクロ統計へ至る血流の「逆シリアル化層」。
//  MetricsLogFormatter.FormatSample が吐いた自己完結の battle-sample 行を、決定論的に
//  BattleMetricSample へ逆コンパイルする純粋関数を提供する。
//
//  ★ 純粋・無状態・Godot 非依存（設計憲法 ②③）:
//    入力は ASCII 文字列の行配列、出力は不変の BattleMetricSample 配列。SoT を 1 ミリも持たず、
//    同じ行配列からは常に同じ結果を返す（決定論）。GD.Print は呼ばない（xUnit で完全検証可能）。
//
//  ★ 頑健性の死守（要件①）:
//    空行・接頭辞の無い無関係なデバッグ出力・破損 JSON・battle-sample 以外のイベント
//    （battle-start / battle-settlement 等）が混ざっても、文字列パターンマッチと型チェックの
//    番兵で例外を一切投げずに対象外として読み飛ばす。復元できた battle-sample 行だけを集める。
//
//  ★ 可逆性の契約:
//    FormatSample が運ぶ全 7 フィールドをそのまま復元する。ただし Outcome は victory 真偽だけを
//    運ぶため、勝利は BattalionVictory、非勝利は BattalionDefeat へ正規化される（IsVictory の
//    集計意味は完全に保たれる＝MetricsCollector の勝率・黒字幅は 1 ビットの狂いもなく再現する）。
//
//  ★ 開発憲法①（日本語ハードコード禁止）:
//    解析対象のキー（"event"/"year"/"earned" 等）はすべて ASCII。表示テキストは持たない。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ChronicleKnights.Core.Battle;

namespace ChronicleKnights.Core.Chronicle;

/// <summary>
/// [metrics] battle-sample 行を BattleMetricSample へ逆コンパイルする純粋な静的パーサ。
/// 破損行・無関係行・他イベント行は例外を投げず安全に読み飛ばす。
/// </summary>
public static class MetricsReporter
{
    // ─── battle-sample 行のフィールドキー（Formatter の出力と一致・ASCII） ──

    private const string FieldEvent = "event";
    private const string FieldYear = "year";
    private const string FieldEarned = "earned";
    private const string FieldSpent = "spent";
    private const string FieldCombatants = "combatants";
    private const string FieldLost = "lost";
    private const string FieldVictory = "victory";
    private const string FieldBoss = "boss";

    /// <summary>
    /// ログ行配列を走査し、復元できた battle-sample 行だけを BattleMetricSample 配列へ逆コンパイルする。
    /// 空行・無関係行・破損 JSON・他イベントは安全にスキップ（例外を投げない）。順序は入力順を保つ。
    /// </summary>
    /// <param name="lines">[metrics] 行を含み得る ASCII 文字列の行配列（null 不可・要素 null/空は無視）。</param>
    /// <returns>復元された計測標本（入力順・不変）。</returns>
    /// <exception cref="ArgumentNullException">lines が null の場合。</exception>
    public static ImmutableArray<BattleMetricSample> ParseLogLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var builder = ImmutableArray.CreateBuilder<BattleMetricSample>();
        foreach (var line in lines)
        {
            if (TryParseSample(line, out var sample) && sample is not null)
            {
                builder.Add(sample);
            }
        }

        return builder.ToImmutable();
    }

    // ─── マクロレポート（章別の勝率・生存率・黒字幅を一望する ASCII カラム表） ──

    /// <summary>全数値整形を ASCII（"." 小数点）に固定する不変カルチャ。</summary>
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// ログ行配列から一気通貫でマクロレポート文字列を編む純粋導管:
    /// ParseLogLines → MetricsCollector.Aggregate → FormatMacroReport。SoT を一切持たず、
    /// 同一ログからは常に同一レポートを返す（決定論・副作用ゼロ）。
    /// </summary>
    public static string BuildReportFromLogs(IEnumerable<string> logLines)
        => FormatMacroReport(MetricsCollector.Aggregate(ParseLogLines(logLines)));

    /// <summary>
    /// 集約済みマクロ統計を、章ごとの勝率・生存率・黒字幅（Earned vs Spent）が一望できる整然とした
    /// ASCII カラム表へ整形する純粋関数。見出し・キーはすべて ASCII（設計憲法①）。末尾に全体総計の
    /// TOTAL 行を置く。Godot 非依存ゆえ xUnit で完全検証可能（GD.Print は呼ばない）。
    /// </summary>
    public static string FormatMacroReport(ChronicleMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var builder = new StringBuilder();
        builder.AppendLine("=== chronicle-macro-metrics (100-year) ===");
        builder.AppendLine(FormatRow(
            "epoch", "battles", "winrate", "survival", "earned", "spent", "net", "avg_net", "inv_eff", "boss_win"));

        foreach (var epoch in metrics.Epochs)
        {
            builder.AppendLine(FormatRow(
                epoch.EpochNameKey,
                epoch.BattleCount.ToString(Invariant),
                epoch.VictoryRate.ToString("F3", Invariant),
                epoch.PopulationSurvivalRate.ToString("F3", Invariant),
                epoch.TotalEarned.ToString(Invariant),
                epoch.TotalSpent.ToString(Invariant),
                epoch.NetSurplus.ToString(Invariant),
                epoch.AverageNetSurplus.ToString("F2", Invariant),
                epoch.InvestmentEfficiency.ToString("F2", Invariant),
                epoch.BossVictories.ToString(Invariant) + "/" + epoch.BossBattles.ToString(Invariant)));
        }

        var overallAverageNet = metrics.TotalBattles <= 0
            ? 0.0
            : (double)metrics.NetSurplus / metrics.TotalBattles;

        builder.AppendLine(FormatRow(
            "TOTAL",
            metrics.TotalBattles.ToString(Invariant),
            metrics.OverallVictoryRate.ToString("F3", Invariant),
            metrics.OverallSurvivalRate.ToString("F3", Invariant),
            metrics.TotalEarned.ToString(Invariant),
            metrics.TotalSpent.ToString(Invariant),
            metrics.NetSurplus.ToString(Invariant),
            overallAverageNet.ToString("F2", Invariant),
            metrics.OverallInvestmentEfficiency.ToString("F2", Invariant),
            metrics.TotalBossVictories.ToString(Invariant) + "/" + metrics.TotalBossBattles.ToString(Invariant)));

        return builder.ToString();
    }

    /// <summary>
    /// 集約済みマクロ統計を ASCII カラム表として標準出力へ一過性にダンプする（100 年シミュレーション
    /// 末尾・マクロテスト環境の通電点）。整形は純粋層 FormatMacroReport に委譲し、本メソッドは
    /// Console.WriteLine への一過性委譲のみ＝SoT・永続キャッシュを一切増設しない。
    /// </summary>
    public static void DumpMacroReport(ChronicleMetrics metrics)
    {
        Console.WriteLine(FormatMacroReport(metrics));
    }

    /// <summary>10 列の固定幅 ASCII カラム 1 行を組む（章名は左詰め・数値は右詰め）。</summary>
    private static string FormatRow(
        string epoch, string battles, string winRate, string survival, string earned,
        string spent, string net, string averageNet, string investmentEfficiency, string bossWin)
    {
        return epoch.PadRight(16)
            + battles.PadLeft(9)
            + winRate.PadLeft(9)
            + survival.PadLeft(10)
            + earned.PadLeft(9)
            + spent.PadLeft(8)
            + net.PadLeft(8)
            + averageNet.PadLeft(9)
            + investmentEfficiency.PadLeft(9)
            + bossWin.PadLeft(9);
    }

    /// <summary>
    /// 1 行を battle-sample として逆コンパイルする。対象外（接頭辞なし・破損 JSON・非 Object・
    /// 他イベント・必須フィールド欠落/型不一致）は false を返してスキップ（例外を投げない）。
    /// </summary>
    private static bool TryParseSample(string? line, out BattleMetricSample? sample)
    {
        sample = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(MetricsLogFormatter.LogPrefix, StringComparison.Ordinal))
        {
            return false; // 接頭辞なし＝無関係なデバッグ出力。
        }

        var json = trimmed[MetricsLogFormatter.LogPrefix.Length..];

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetString(root, FieldEvent, out var eventName)
                || !string.Equals(eventName, MetricsLogFormatter.EventBattleSample, StringComparison.Ordinal))
            {
                return false; // battle-start / battle-settlement / 他イベントは復元対象外。
            }

            if (!TryGetInt(root, FieldYear, out var year)) return false;
            if (!TryGetInt(root, FieldEarned, out var earned)) return false;
            if (!TryGetInt(root, FieldSpent, out var spent)) return false;
            if (!TryGetInt(root, FieldCombatants, out var combatants)) return false;
            if (!TryGetInt(root, FieldLost, out var lost)) return false;
            if (!TryGetBool(root, FieldVictory, out var victory)) return false;
            if (!TryGetBool(root, FieldBoss, out var boss)) return false;

            sample = new BattleMetricSample
            {
                Year           = year,
                EarnedPoints   = earned,
                SpentPoints    = spent,
                CombatantCount = combatants,
                LostCount      = lost,
                Outcome        = victory ? BattleOutcome.BattalionVictory : BattleOutcome.BattalionDefeat,
                IsEpochBoss    = boss,
            };
            return true;
        }
        catch (JsonException)
        {
            return false; // 破損 JSON は安全にスキップ。
        }
    }

    /// <summary>整数フィールドを安全に引く（存在し Number で Int32 化できるときだけ true）。</summary>
    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (obj.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    /// <summary>真偽フィールドを安全に引く（存在し True/False のときだけ true）。</summary>
    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (obj.TryGetProperty(name, out var element)
            && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
        {
            value = element.GetBoolean();
            return true;
        }

        return false;
    }

    /// <summary>文字列フィールドを安全に引く（存在し String のときだけ true）。</summary>
    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (obj.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return true;
        }

        return false;
    }
}
