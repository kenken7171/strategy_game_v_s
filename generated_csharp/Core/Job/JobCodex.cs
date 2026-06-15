// =============================================================================
//  ChronicleKnights — JobCodex.cs
// -----------------------------------------------------------------------------
//  Single source of truth for the Job Manual / Job Description *display text*.
//  Faithful trace of the TS web data (packages/core/src/data/jobs.ts JOB_ABILITY
//  / JOB_FORMATION_GUIDE and packages/frontend/src/utils/job.ts).
//
//  ★ Language exception (this command only): UI display strings are Japanese
//    (UTF-8) literals. Identifiers / comments stay ASCII (Constitution I for
//    logic is preserved). Numeric passive values (BDF/SDF/AB/HL) and the special
//    passive (sniper) are read from JobMaster at call time, so numbers stay
//    single-sourced and never drift.
// =============================================================================

using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Job;

/// <summary>Prose block for one job (role / ability / usage / flavor + formation text).</summary>
public sealed record JobCodexText
{
    public required string Role { get; init; }
    public required string AbilitySummary { get; init; }
    public required string Usage { get; init; }
    public required string Flavor { get; init; }
    public required string FormationHeadline { get; init; }
    public required string EffectRangeNote { get; init; }
}

/// <summary>One structured passive row (key is stable for testids).</summary>
public sealed record JobPassiveLine
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public int? Value { get; init; }
    public required string Scope { get; init; }
    public required string Description { get; init; }
}

/// <summary>Pure, Godot-independent catalog of job manual text + passive explainer.</summary>
public static class JobCodex
{
    // ─── Japanese display names ───────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<JobId, string> DisplayNames =
        new Dictionary<JobId, string>
        {
            [JobId.IronWallKnight] = "鉄壁騎士",
            [JobId.HeavyInfantry]  = "重装歩兵",
            [JobId.StandardBearer] = "旗手",
            [JobId.Tactician]      = "戦術官",
            [JobId.Medic]          = "衛生兵",
            [JobId.Sniper]         = "狙撃兵",
            [JobId.Sorcerer]       = "呪術師",
            [JobId.Scout]          = "斥候",
        };

    /// <summary>Japanese job name. Falls back to the ASCII enum name.</summary>
    public static string DisplayName(JobId id)
        => DisplayNames.TryGetValue(id, out var name) ? name : id.ToString();

    // ─── Manual prose (Japanese trace of the TS JOB_ABILITY + guide text) ─────

    private static readonly IReadOnlyDictionary<JobId, JobCodexText> Texts =
        new Dictionary<JobId, JobCodexText>
        {
            [JobId.IronWallKnight] = new JobCodexText
            {
                Role           = "前衛の盾。大隊を守護する重装騎士。",
                AbilitySummary = "大隊総守護力（前衛配置時、大隊全員の被ダメージ -10）＋ 分隊守護力（自分隊の被ダメージ -15）。複数編成で加算される。",
                Usage          = "前衛中央に置き、後衛アタッカーへの被害を最小化する。複数編成で堅さが倍増する。",
                Flavor         = "古き誓いを纏う者。我が身を盾とし、後ろの者たちを生かす。",
                FormationHeadline = "推奨：最前線（大隊総守護力の発動条件）",
                EffectRangeNote   = "大隊総守護力は前衛配置時のみ大隊全体へ届く。分隊守護力は所属分隊のみ。",
            },
            [JobId.HeavyInfantry] = new JobCodexText
            {
                Role           = "単騎で完結する前衛アタッカー。",
                AbilitySummary = "全ジョブ最高の体力 300 ＋ 高い前衛攻撃力 70 ＋ 分隊守護力 10。",
                Usage          = "鉄壁騎士の隣に置き、削れぬ盾として攻撃も担う。長期戦に強い。",
                Flavor         = "壊れぬ鎧、屈せぬ意志。最後の一人になるまで戦線を支える。",
                FormationHeadline = "推奨：最前線（壊れぬ盾）",
                EffectRangeNote   = "分隊守護力 10 で自分隊を軽減。高い体力と前衛攻撃で前線を支える。",
            },
            [JobId.StandardBearer] = new JobCodexText
            {
                Role           = "大隊全員の火力を底上げする精神的支柱。",
                AbilitySummary = "突撃号令 40（ターン開始時、自分以外の全員に 俊敏+40 / 攻撃+40）。重ね掛け可能。",
                Usage          = "前衛・後衛どちらでも機能する。複数編成で大隊全員が化け物になる。",
                Flavor         = "翻る軍旗が士気を呼び覚ます。皆の心に火を灯す者。",
                FormationHeadline = "推奨：どこでも可（大隊全員を強化）",
                EffectRangeNote   = "突撃号令 40 で自分以外の大隊全員に 俊敏+40 / 攻撃+40 を撒く。",
            },
            [JobId.Tactician] = new JobCodexText
            {
                Role           = "速度寄りの軽量バフ役。",
                AbilitySummary = "突撃号令 20（俊敏+20 / 攻撃+20）＋ 自身も中速 俊敏 35。",
                Usage          = "旗手より控えめだが速度値も持つため、複合運用で先制力を高められる。",
                Flavor         = "盤上の駒を読む冷徹な頭脳。号令一つで戦況を変える。",
                FormationHeadline = "推奨：どこでも可（軽量バフ役）",
                EffectRangeNote   = "突撃号令 20 で自分以外の大隊全員に 俊敏+20 / 攻撃+20 を撒く。",
            },
            [JobId.Medic] = new JobCodexText
            {
                Role           = "唯一の継続回復役。",
                AbilitySummary = "ターン末分隊治癒 30（ターン終了時、自分隊の生存者全員を +30 回復）。最大体力まで。",
                Usage          = "脆い後衛分隊に必ず配置。前衛にも必需品。長期戦の生命線。",
                Flavor         = "戦場の天使、あるいは最後の希望。手当ての先に新たな未来を見る。",
                FormationHeadline = "推奨：脆い後衛（自分隊を継続回復）",
                EffectRangeNote   = "ターン末分隊治癒 30 は所属分隊のみ。配置した分隊の生存者全員を回復する。",
            },
            [JobId.Sniper] = new JobCodexText
            {
                Role           = "後衛の超火力。二の矢を放つ砲台。",
                AbilitySummary = "後衛攻撃力 90。行動順1番手かつ分隊最速なら 二の矢（2連撃）が発動する。",
                Usage          = "後衛-左／後衛-右へ配置必須。戦術官・旗手の俊敏バフで二の矢の確率を高める。",
                Flavor         = "風の音を聴き、息を止める。一矢で戦況を変える狙撃の達人。",
                FormationHeadline = "推奨：後衛（二の矢の砲台）",
                EffectRangeNote   = "後衛攻撃力 90 は後衛時のみフル火力。二の矢が決まれば爆発的。",
            },
            [JobId.Sorcerer] = new JobCodexText
            {
                Role           = "全ジョブ最強の火力、しかし最も脆い砲台。",
                AbilitySummary = "後衛攻撃力 120（全職最強）。体力 40 で全ジョブ最弱。",
                Usage          = "後衛配置必須。前衛が崩れる前に決着をつける高リスク高リターンの札。",
                Flavor         = "古き禁呪を扱う者。一発の魔弾に命を込める。あるいは命を奪われる。",
                FormationHeadline = "推奨：後衛（高リスク高リターン）",
                EffectRangeNote   = "後衛攻撃力 120 で全職最強の砲台。体力 40 ゆえ前衛は不可。",
            },
            [JobId.Scout] = new JobCodexText
            {
                Role           = "速度最強の先制削り役。",
                AbilitySummary = "俊敏 60（全ジョブ最速）＋ 前後均等 攻撃 40 で配置自由度が高い。",
                Usage          = "後衛配置で先制を取り、敵の出鼻を挫く。最速の利で戦術官の補佐も可能。",
                Flavor         = "誰よりも早く動き、誰よりも先に敵を見る。風のように戦場を駆ける。",
                FormationHeadline = "推奨：どこでも可（最速の先制削り役）",
                EffectRangeNote   = "前後均等 攻撃 40 で配置自由。俊敏 60 で常時先制を取る。",
            },
        };

    /// <summary>Manual prose for a job (null for null/unknown jobs).</summary>
    public static JobCodexText? TextFor(JobId? id)
        => id is { } jid && Texts.TryGetValue(jid, out var t) ? t : null;

    // ─── Structured passive explainer (numbers sourced from JobMaster) ────────

    /// <summary>
    /// Build the structured passive list for a job, reading numeric values from
    /// JobMaster (single SoT for numbers) and Japanese labels/scope/desc here.
    /// Returns an empty list for null/unknown jobs.
    /// </summary>
    public static ImmutableArray<JobPassiveLine> Passives(JobId? id)
    {
        var def = JobMaster.Find(id);
        if (def is null) return ImmutableArray<JobPassiveLine>.Empty;

        var stats = def.Stats;
        var builder = ImmutableArray.CreateBuilder<JobPassiveLine>();

        if (stats.BattalionDefense > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "bdf",
                Label       = "大隊総守護力",
                Value       = stats.BattalionDefense,
                Scope       = "前衛配置時のみ発動／効果は大隊全体",
                Description = "大隊全員の被ダメージを軽減（他分隊にも届く）。",
            });
        }
        if (stats.SquadDefense > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "sdf",
                Label       = "分隊守護力",
                Value       = stats.SquadDefense,
                Scope       = "常時発動／効果は所属分隊のみ",
                Description = "自分が所属する分隊の被ダメージを軽減。",
            });
        }
        if (stats.InitiativeBuff > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "ab",
                Label       = "突撃号令",
                Value       = stats.InitiativeBuff,
                Scope       = "毎ターン開始時／自分以外の大隊全員",
                Description = "ターン開始時に俊敏・攻撃バフを撒く（重ね掛け可）。",
            });
        }
        if (stats.TurnEndSquadHeal > 0)
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "hl",
                Label       = "ターン末分隊治癒",
                Value       = stats.TurnEndSquadHeal,
                Scope       = "毎ターン終了時／所属分隊の生存者全員",
                Description = "ターン終了時に分隊の体力を回復（最大体力で頭打ち）。",
            });
        }

        if (def.SpecialPassives.Contains(PassiveKind.ConsecutiveStrike))
        {
            builder.Add(new JobPassiveLine
            {
                Key         = "special-double-strike",
                Label       = "二の矢（2連撃）",
                Value       = null,
                Scope       = "行動順1番手 かつ 分隊先頭スロット時のみ",
                Description = "通常攻撃が2回行われる（1ターン中の追撃）。",
            });
        }

        return builder.ToImmutable();
    }
}
