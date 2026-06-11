// =============================================================================
//  ChronicleKnights — ChronicleLogEntry.cs
// -----------------------------------------------------------------------------
//  「1 世代 = 時間軸 1 周」の幕引きで旅団に刻まれた“歴史の一行”を写し取る完全不変
//  レコードと、その純粋な生成ファクトリ。
//
//  ★ 何を記録するのか（年代記ナレーションの素材）:
//    ゲームループ 1 周の幕引き（Battle→Chronicle・世代交代）では、SoT 内部で
//    決定論的に以下が起きる:
//      - 寿命到達（HasReachedMaxAge）で現役を退いた者 …… 引退 (Retired)
//      - 直前の戦闘で斃れた者（IsDead）             …… 戦死 (KilledInAction)
//      - その戦闘で経験を積み階級が上がった者       …… 昇級 (LevelGained)
//    本レコードはこの「損失・成長のイベントストリーム」を、表示テキストを持たない
//    純粋データ（名前キー・ジョブ・年齢・前後レベル）として 1 行ずつ写し取る。
//
//  ★ 設計憲法の遵守:
//    ① 日本語ハードコード禁止: 本レコードは名前“キー”(ASCII)・JobId(enum)・数値だけを
//       運び、表示用テキストを一切持たない。引退/戦死/昇級の日本語ナレーションや
//       ユニット名・ジョブ名の解決は UI 層が localization 経由で行う（脳と身体の分離）。
//    ② 完全不変: 全フィールド init 専用。ファクトリは入力から新規生成するだけの
//       副作用ゼロな純粋関数で、Godot に 1 ミリも依存しない（xUnit で完全検証可能）。
//    ③ 単一 SoT・単方向: 損失ユニットは世代交代の直後にロスタから不可逆に外れるため、
//       「外れる前の静止画」から名前キー等をこのレコードへ確定捕捉しておく。これにより
//       UI はロスタ実在に依存せず、過去に失った者の名も安全に解決できる。
//
//  略称（BDF/SDF/AB/HL — 正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Battle;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Chronicle;

/// <summary>
/// 年代記に刻まれる出来事の種別。表示テキストは持たず、UI 層が localization で解決する。
/// </summary>
public enum ChronicleEventKind
{
    /// <summary>寿命到達による引退（天寿を全うして現役を退いた）。</summary>
    Retired,

    /// <summary>戦闘での戦死（戦場に斃れて旅団を去った）。</summary>
    KilledInAction,

    /// <summary>戦果による昇級（経験を積み階級が上がった）。</summary>
    LevelGained,

    /// <summary>
    /// 戦力外通告（プレイヤーの編成判断による手動解雇）で旅団を去った。
    /// 引退（寿命）・戦死（戦場）と異なり、寿命前の現役を *任意* に外す人事決定を写し取る。
    /// </summary>
    Dismissed,
}

/// <summary>
/// 旅団史の一行を写し取る完全不変レコード。表示用の名前・ジョブ名・ナレーション文は
/// 一切持たず、突合・解決に必要な最小データ（名前キー・JobId・年齢・前後レベル）だけを運ぶ。
/// </summary>
public sealed record ChronicleLogEntry
{
    /// <summary>この出来事が起きた世代（タイムラインのターン番号・見出しに使う）。</summary>
    public required int Generation { get; init; }

    /// <summary>出来事の種別（引退 / 戦死 / 昇級）。</summary>
    public required ChronicleEventKind Kind { get; init; }

    /// <summary>当該ユニットの名（localization キー・ASCII）。表示名は UI が解決する。</summary>
    public required string UnitFirstNameKey { get; init; }

    /// <summary>当該ユニットの姓（localization キー・ASCII）。表示名は UI が解決する。</summary>
    public required string UnitLastNameKey { get; init; }

    /// <summary>当該ユニットのジョブ（表示名は UI が ResolveJobName で解決する）。</summary>
    public required JobId Job { get; init; }

    /// <summary>引退 / 戦死時の年齢（昇級では未使用・既定 0）。</summary>
    public int Age { get; init; }

    /// <summary>昇級前のレベル（引退 / 戦死では未使用・既定 0）。</summary>
    public int FromLevel { get; init; }

    /// <summary>昇級後のレベル（引退 / 戦死では未使用・既定 0）。</summary>
    public int ToLevel { get; init; }
}

/// <summary>
/// 1 世代の世代交代から年代記ナレーションの素材（損失・成長のイベント列）を純粋生成する
/// 静的サービス。Godot 完全非依存・副作用ゼロ（xUnit で挙動を厳密に検証できる）。
/// </summary>
public static class ChronicleLog
{
    /// <summary>
    /// この世代で起きた「成長（昇級）」と「損失（引退 / 戦死）」を 1 つのイベント列へ束ねる。
    ///
    /// 並び（世代内の時系列に忠実）:
    ///   1. 成長（昇級）…… その世代の戦闘で Lv が上がった者（戦闘は時間軸スキップより前）。
    ///   2. 損失（引退 / 戦死）…… 戦闘後のタイムスキップで現役を外れた者。
    ///      戦死 (IsDead) と引退 (HasReachedMaxAge) は IsDead を優先して判定する。
    ///
    /// 名前解決の規律:
    ///   - 損失イベントは <paramref name="departedRoster"/> の各 Unit（外れる前の静止画）から
    ///     名前キー・ジョブ・年齢をその場で確定捕捉する（ロスタ実在に依存しない）。
    ///   - 昇級イベントは <paramref name="battleSpoils"/> が突合キー(UnitId)と前後レベルだけを
    ///     持つため、<paramref name="levelGainUnitLookup"/>（生存者＋離脱者の Id→Unit）で
    ///     名前・ジョブを引き当てる。引き当て不能な Id は安全にスキップする。
    ///
    /// いずれの入力が空・null でも例外を投げず、解決できた分だけを返す（ヌル安全）。
    /// </summary>
    /// <param name="generation">この世代の見出し番号（タイムラインのターン）。</param>
    /// <param name="departedRoster">この世代で現役を外れた者（完全ロスト・外れる前の静止画）。</param>
    /// <param name="battleSpoils">この世代の戦闘の戦果決算（昇級の前後レベルを持つ）。</param>
    /// <param name="levelGainUnitLookup">昇級者の名前解決用 Id→Unit（生存者＋離脱者）。</param>
    /// <returns>この世代の年代記イベント列（昇級→損失の順）。</returns>
    public static ImmutableArray<ChronicleLogEntry> BuildGenerationEntries(
        int generation,
        ImmutableArray<Unit> departedRoster,
        BattleSpoils? battleSpoils,
        IReadOnlyDictionary<Guid, Unit>? levelGainUnitLookup)
    {
        var builder = ImmutableArray.CreateBuilder<ChronicleLogEntry>();

        // ── 1. 成長（昇級）: その世代の戦闘で階級が上がった者 ────────────────
        if (battleSpoils is not null && levelGainUnitLookup is not null)
        {
            foreach (var gain in battleSpoils.UnitLevelGains)
            {
                if (!levelGainUnitLookup.TryGetValue(gain.UnitId, out var unit) || unit is null)
                {
                    continue; // 名前解決できない突合キーは安全にスキップ。
                }

                builder.Add(new ChronicleLogEntry
                {
                    Generation       = generation,
                    Kind             = ChronicleEventKind.LevelGained,
                    UnitFirstNameKey = unit.FirstNameKey,
                    UnitLastNameKey  = unit.LastNameKey,
                    Job              = unit.Job,
                    FromLevel        = gain.FromLevel,
                    ToLevel          = gain.ToLevel,
                });
            }
        }

        // ── 2. 損失（引退 / 戦死）: タイムスキップで現役を外れた者 ────────────
        foreach (var unit in departedRoster)
        {
            if (unit is null) continue;

            // IsDead を優先（戦死）。それ以外の離脱は寿命到達（引退）。
            var kind = unit.IsDead
                ? ChronicleEventKind.KilledInAction
                : ChronicleEventKind.Retired;

            builder.Add(new ChronicleLogEntry
            {
                Generation       = generation,
                Kind             = kind,
                UnitFirstNameKey = unit.FirstNameKey,
                UnitLastNameKey  = unit.LastNameKey,
                Job              = unit.Job,
                Age              = unit.Age,
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// プレイヤーの編成判断による手動解雇（戦力外通告）1 件を、年代記の一行へ純粋に写し取る。
    ///
    /// ★ 呼び出しタイミングの規律（<see cref="BuildGenerationEntries"/> と同じ思想）:
    ///   解雇ユニットはこの直後にロスタから不可逆に外れるため、外れる前の静止画
    ///   （<paramref name="dismissed"/>）から名前キー・ジョブ・年齢・レベルをその場で確定捕捉する。
    ///   これにより UI はロスタ実在に依存せず、過去に解雇した者の名も安全に解決できる。
    ///
    /// ★ フィールドの割り当て:
    ///   解雇は「年齢」と「最終レベル」の双方が意味を持つ出来事のため、
    ///   <see cref="ChronicleLogEntry.Age"/> に解雇時の年齢、<see cref="ChronicleLogEntry.FromLevel"/> に
    ///   解雇時のレベルを格納する（ToLevel は未使用・0）。UI 側の整形がこの取り決めに対応する。
    /// </summary>
    /// <param name="generation">この解雇が起きた世代（タイムラインのターン番号・見出しに使う）。</param>
    /// <param name="dismissed">戦力外通告で外れる旅団員（外れる前の静止画）。</param>
    /// <returns>解雇 1 件を表す不変な年代記エントリ。</returns>
    public static ChronicleLogEntry BuildDismissalEntry(int generation, Unit dismissed)
    {
        ArgumentNullException.ThrowIfNull(dismissed);

        return new ChronicleLogEntry
        {
            Generation       = generation,
            Kind             = ChronicleEventKind.Dismissed,
            UnitFirstNameKey = dismissed.FirstNameKey,
            UnitLastNameKey  = dismissed.LastNameKey,
            Job              = dismissed.Job,
            Age              = dismissed.Age,
            FromLevel        = dismissed.Level, // 解雇時の最終レベル（ToLevel は未使用）。
        };
    }
}
