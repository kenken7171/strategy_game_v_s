// =============================================================================
//  ChronicleKnights — MetricsLogFormatter.cs
// -----------------------------------------------------------------------------
//  実走ログハーネスの「純粋な整形層」。戦闘開始・決算のフックが手にした素の数値を、
//  機械可読な ASCII 構造化ログ（JSON 風 1 行）へ写す純粋関数だけを提供する。
//
//  ★ 層別（SaveSerializer / MasterDataNameResolver と同じ思想）:
//    本クラスは Godot に 1 ミリも依存せず（GD.Print は呼ばない）、文字列を返すだけ。
//    標準出力への一過性ダンプ（Godot I/O）は呼び出し側（ChronicleGlobal）が GD.Print で行う。
//    これにより整形ロジックを xUnit で完全検証でき、SoT も一切持たない（純粋・副作用ゼロ）。
//
//  ★ testid との同期（要件③）:
//    敵原型は UI の testid `battle-enemy-instance-{archetype}` と同じ ASCII kebab スラッグ
//    （EternalSovereign → "eternal-sovereign"）で書き、ログと画面状態を機械的に突合できるようにする。
//    決算行は収支パネル（spoils-economy-total-val 等）が表す内訳と同じ数値を運ぶ。
//
//  ★ 開発憲法①（日本語ハードコード禁止・ASCII 構造化キー）:
//    出力に現れるのは ASCII の固定キー（"event"/"year"/"enemy"/"earned_total" 等）と数値・真偽・
//    kebab スラッグのみ。日本語・絵文字は一切含めない（機械可読性と憲法①の両立）。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System.Text;
using ChronicleKnights.Core.Battle;

namespace ChronicleKnights.Core.Chronicle;

/// <summary>
/// 実走ログ 1 行を機械可読な ASCII 構造化テキスト（JSON 風）へ整形する純粋な静的フォーマッタ。
/// Godot 非依存・状態なし・副作用なしで、同一入力からは常に同一文字列を返す。
/// </summary>
public static class MetricsLogFormatter
{
    /// <summary>全ログ行に付ける grep 用の固定接頭辞（ASCII）。</summary>
    public const string LogPrefix = "[metrics] ";

    /// <summary>
    /// 戦闘開始の構造化ログ 1 行を整形する。年・出現した敵原型（kebab スラッグ）・敵の素ステータス
    /// （HP/ATK/SPD）を JSON 風 ASCII で運ぶ。UI の battle-start-era-scaled /
    /// battle-enemy-instance-{archetype} が表す状態と同期する。
    /// </summary>
    public static string FormatBattleStart(int year, EnemyArchetype archetype, int hp, int attack, int speed)
    {
        return LogPrefix + "{"
            + "\"event\":\"battle-start\","
            + "\"year\":" + year + ","
            + "\"enemy\":\"" + ArchetypeSlug(archetype) + "\","
            + "\"hp\":" + hp + ","
            + "\"atk\":" + attack + ","
            + "\"spd\":" + speed
            + "}";
    }

    /// <summary>
    /// 決算（婚姻ポイント）の構造化ログ 1 行を整形する。年・勝敗・内訳（基礎値/昇級ボーナス/進化
    /// ボーナス/ロスト罰/獲得合計）・適用後の総残高を JSON 風 ASCII で運ぶ。収支パネルの
    /// spoils-economy-total-val 等が表す内訳と同じ数値を映す。
    /// </summary>
    public static string FormatSettlement(int year, MarriagePointsBreakdown breakdown, int balanceAfter)
    {
        return LogPrefix + "{"
            + "\"event\":\"battle-settlement\","
            + "\"year\":" + year + ","
            + "\"victory\":" + (breakdown.IsVictory ? "true" : "false") + ","
            + "\"base\":" + breakdown.VictoryBase + ","
            + "\"levelgain_bonus\":" + breakdown.LevelGainBonus + ","
            + "\"evolution_bonus\":" + breakdown.EvolutionBonus + ","
            + "\"loss_penalty\":" + breakdown.LossPenalty + ","
            + "\"earned_total\":" + breakdown.Total + ","
            + "\"balance_after\":" + balanceAfter
            + "}";
    }

    /// <summary>
    /// 敵原型 enum を testid と同じ ASCII kebab スラッグへ写す（EternalSovereign → "eternal-sovereign"）。
    /// 表示テキストではなく機械可読キーゆえ localization は不要（PascalCase の語境界に '-' を差す）。
    /// </summary>
    private static string ArchetypeSlug(EnemyArchetype archetype)
    {
        var name = archetype.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
