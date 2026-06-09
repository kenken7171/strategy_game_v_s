// =============================================================================
//  ChronicleKnights — GamePhase.cs
// -----------------------------------------------------------------------------
//  ゲーム 1 周（1 世代）の進行状態を表す状態マシンの純粋ロジック層。
//  Godot 完全非依存（enum + 純粋関数のみ）なので xUnit から直接検証できる。
//
//  TypeScript 版 packages/frontend/src/game/GamePhase.ts の
//  「一方通行・不可逆遷移（自由遷移 API なし、nextPhase(current) のみ）」という
//  思想を C# へ翻訳したもの。Godot 側（ChronicleGlobal）はこの純粋ロジックへ
//  状態保持とシグナル発火だけを委ね、遷移可否の判断は本クラスへ集約する。
//
//  ★ フェーズの循環（1 周 = 1 世代）:
//
//        ┌─────────────────────────────────────────────┐
//        │                                             ▼
//    Chronicle ──▶ Guild ──▶ Formation ──▶ Battle ──┘（次の世代の Chronicle へ）
//     歴史・予言    婚姻・     編成          戦闘結果
//      選択        スカウト                  解決
//
//    - 各フェーズからの「次」はただ 1 つ（Next）。これ以外の遷移はすべて不正。
//    - Battle → Chronicle はループの巻き戻りではなく「次の世代へ進む」正規遷移。
//    - 後退（例 Guild → Chronicle）や飛び越し（例 Chronicle → Battle）は禁止。
//
//  ★ 略称（大隊総守護力などの正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.GameFlow;

// ─── フェーズ列挙（1 周の 4 局面） ───────────────────────────────────────────

/// <summary>
/// ゲーム 1 周（1 世代）の進行局面。一方通行の循環で進む。
/// </summary>
public enum GamePhase
{
    /// <summary>歴史・予言選択（年代記を読み、進むべき予言を 1 つ選ぶ）。</summary>
    Chronicle,

    /// <summary>婚姻・スカウト（旅団の人員を整える）。</summary>
    Guild,

    /// <summary>編成（大隊 9 名の配置を決める）。</summary>
    Formation,

    /// <summary>戦闘結果解決（戦闘の決着と報酬・成長の反映）。</summary>
    Battle,
}

// ─── 遷移ルール（純粋・静的・テスト可能） ────────────────────────────────────

/// <summary>
/// <see cref="GamePhase"/> の合法遷移だけを定義する純粋な状態マシン規則。
/// 「現在のフェーズから進める先はただ 1 つ」という不可逆・一方通行を保証する。
/// </summary>
public static class GamePhaseFlow
{
    /// <summary>1 周の正規順序（先頭が開始フェーズ）。検証・列挙用の固定順。</summary>
    public static readonly ImmutableArray<GamePhase> Cycle =
        ImmutableArray.Create(
            GamePhase.Chronicle,
            GamePhase.Guild,
            GamePhase.Formation,
            GamePhase.Battle);

    /// <summary>ゲーム開始時・新規 1 周開始時の初期フェーズ。</summary>
    public static GamePhase InitialPhase => GamePhase.Chronicle;

    /// <summary>
    /// 指定フェーズから進める唯一の合法な次フェーズを返す。
    /// Battle の次は次世代の Chronicle（循環）。
    /// </summary>
    public static GamePhase Next(GamePhase current) => current switch
    {
        GamePhase.Chronicle => GamePhase.Guild,
        GamePhase.Guild     => GamePhase.Formation,
        GamePhase.Formation => GamePhase.Battle,
        GamePhase.Battle    => GamePhase.Chronicle,
        _ => throw new ArgumentOutOfRangeException(
            nameof(current), current, "未知の GamePhase です。"),
    };

    /// <summary>
    /// <paramref name="from"/> から <paramref name="to"/> への遷移が合法か。
    /// 合法なのは「ちょうど次のフェーズ」へ進む場合のみ。後退・飛び越し・
    /// 自己遷移（from == to）はすべて false（不正な順序を許さない一方通行）。
    /// </summary>
    public static bool CanTransition(GamePhase from, GamePhase to)
        => Next(from) == to;

    /// <summary>
    /// フェーズの ASCII スラッグ（ローカライズキー "phase-chronicle" 等の組み立て用）。
    /// 日本語ラベルは Config/localization_ja.json 側が引き当て先。
    /// </summary>
    public static string Slug(this GamePhase phase) => phase switch
    {
        GamePhase.Chronicle => "chronicle",
        GamePhase.Guild     => "guild",
        GamePhase.Formation => "formation",
        GamePhase.Battle    => "battle",
        _ => throw new ArgumentOutOfRangeException(
            nameof(phase), phase, "未知の GamePhase です。"),
    };
}
