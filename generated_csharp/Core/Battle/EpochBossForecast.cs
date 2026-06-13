// =============================================================================
//  ChronicleKnights — EpochBossForecast.cs
// -----------------------------------------------------------------------------
//  100 年史の各 Epoch 最終年に君臨する「章ボス（EpochBoss）」の接近を、運命の帯
//  （AttackIntentRoller.Forecast）へ重ねて開示するための完全不変データ群。
//
//  ★ なぜ AttackPatternKind を増やさないのか（既存スイッチ非破壊の規律）:
//    章ボスの前兆は通常の攻撃予告（SingleStrike/Pincer/TotalAssault）とは
//    意味論が異なる「予兆（omen）」である。AttackPatternKind に種別を増やすと
//    既存の網羅スイッチ（UI/ProphecyTimelineOverlay の KindLabel 等）を壊し、
//    非網羅スイッチ警告（CS8509/CS8524）を誘発しかねない。よって前兆は独立した
//    <see cref="EpochBossOmen"/> として表現し、各ターンの通常脅威（<see cref="AttackIntent"/>）に
//    「重ねる」形（<see cref="ForecastEntry"/>）で運ぶ。既存 Forecast とその検証は一切壊さない。
//
//  ★ 依存方向（層の規律）:
//    本ファイルは Core.Battle に属し、上位の Core.Chronicle（ChronicleTimelineConfig）が
//    どの年にどの章ボスが現れるかを司って <see cref="EpochBossOmenSchedule"/> を組み立てる。
//    Battle 層は「章（Epoch）」という暦の概念を enum で知らず、必要な表示キー（ASCII 文字列）と
//    敵原型（<see cref="EnemyArchetype"/>）だけを受け取る。Chronicle → Battle の一方向で循環しない。
//
//  ★ 完全不変（設計憲法 ②）・日本語ハードコード禁止（設計憲法 ①）:
//    表示テキストは持たず、localization 解決用の ASCII キーだけを運ぶ。Godot 非依存の純粋 C#。
// =============================================================================

namespace ChronicleKnights.Core.Battle;

// ─── 章ボス前兆（運命の帯へ重ねる「予兆」インテント） ─────────────────────────

/// <summary>
/// 「あと <see cref="TurnsUntilArrival"/> ターンで章ボスが接近する」ことを開示する
/// 完全不変の前兆レコード。運命の帯（<see cref="ForecastEntry"/>）の該当ターンに重ねて運ばれ、
/// UI（時間の矢）が「??ターン後にボス接近中」を描く根拠になる。
/// </summary>
public sealed record EpochBossOmen
{
    /// <summary>接近中の章ボスの原型（表示名は localization キー経由で解決する）。</summary>
    public required EnemyArchetype BossArchetype { get; init; }

    /// <summary>
    /// この帯ターン時点で、章ボス到来まで残り何ターンか（0 = このターンに到来）。
    /// 前兆は到来の数ターン前から、残りターンを減らしながら連続して現れる。
    /// </summary>
    public required int TurnsUntilArrival { get; init; }

    /// <summary>この章（Epoch）の表示名 localization キー（ASCII・例: "epoch-twilight"）。</summary>
    public required string EpochNameKey { get; init; }

    /// <summary>前兆スキル名の表示 localization キー（ASCII・例: "epoch-boss-omen-approach"）。</summary>
    public required string OmenSkillNameKey { get; init; }

    /// <summary>章ボスがまさにこの帯ターンに到来するか（残り 0）。</summary>
    public bool ArrivesThisTurn => TurnsUntilArrival <= 0;
}

// ─── 章ボス接近スケジュール（暦から弾く前兆の窓口仕様） ───────────────────────

/// <summary>
/// 章ボスが運命の帯のどこへ・どの先行ターンから前兆を現すかを定める完全不変の窓口仕様。
/// Core.Chronicle の <c>ChronicleTimelineConfig.BuildOmenScheduleForYear</c> が暦（年）から
/// 決定論的に組み立て、<see cref="AttackIntentRoller.ForecastWithOmens"/> がこれを読んで
/// 各帯ターンへ前兆を重ねる。乱数・SoT には一切依存しない（整数だけで未来を投影する）。
/// </summary>
public sealed record EpochBossOmenSchedule
{
    /// <summary>章ボスが（少なくとも将来）接近しているか。false なら前兆は一切出ない。</summary>
    public required bool BossApproaching { get; init; }

    /// <summary>
    /// 今（帯の起点）から見て、章ボスが帯の何ターン目（1 始まり）に到来するか。
    /// 例: 1 なら次ターン（T+1）に到来。<see cref="BossApproaching"/> が false のとき意味を持たない。
    /// </summary>
    public required int TurnsUntilArrival { get; init; }

    /// <summary>到来の何ターン前から前兆を出し始めるか（先行告知の長さ・0 以上）。</summary>
    public required int ForewarnLeadTurns { get; init; }

    /// <summary>接近中の章ボスの原型。</summary>
    public required EnemyArchetype BossArchetype { get; init; }

    /// <summary>章（Epoch）の表示名 localization キー（ASCII）。</summary>
    public required string EpochNameKey { get; init; }

    /// <summary>前兆スキル名の表示 localization キー（ASCII）。</summary>
    public required string OmenSkillNameKey { get; init; }

    /// <summary>
    /// 「接近する章ボスなし」を表す不活性スケジュール。<see cref="AttackIntentRoller.ForecastWithOmens"/>
    /// にこれを渡すと、前兆を一切重ねない（通常の運命の帯と同一結果になる）。
    /// </summary>
    public static EpochBossOmenSchedule None { get; } = new()
    {
        BossApproaching   = false,
        TurnsUntilArrival = 0,
        ForewarnLeadTurns = 0,
        BossArchetype     = EnemyArchetype.TrialGuardian,
        EpochNameKey      = string.Empty,
        OmenSkillNameKey  = string.Empty,
    };
}

// ─── 運命の帯の 1 要素（通常脅威＋任意の章ボス前兆） ──────────────────────────

/// <summary>
/// 運命の帯（先読み）1 ターン分を表す完全不変レコード。各ターンには必ず通常の攻撃脅威
/// （<see cref="Threat"/>）があり、章ボスが接近している該当ターンにのみ前兆
/// （<see cref="BossOmen"/>）が重なる。前兆の有無で UI は通常予告／ボス接近警告を描き分ける。
/// </summary>
public sealed record ForecastEntry
{
    /// <summary>帯における 1 始まりのターン位置（1 = T+1、2 = T+2 …）。</summary>
    public required int TurnOffset { get; init; }

    /// <summary>このターンに敵が放つ通常の攻撃脅威（必ず存在する）。</summary>
    public required AttackIntent Threat { get; init; }

    /// <summary>章ボスがこのターンに前兆を現すなら非 null（通常ターンは null）。</summary>
    public EpochBossOmen? BossOmen { get; init; }

    /// <summary>このターンが章ボスの接近を告げているか。</summary>
    public bool HeraldsEpochBoss => BossOmen is not null;
}
