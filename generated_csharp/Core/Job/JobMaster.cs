// =============================================================================
//  ChronicleKnights — JobMaster.cs
// -----------------------------------------------------------------------------
//  8 ジョブの「数値データの SoT (Single Source of Truth)」。
//
//  TypeScript 版の JOB_DEFAULTS / JOB_FORMATION_GUIDE / ROLE_BONUS /
//  JOB_TARGET_RATING を 1 ファイルに集約して翻訳したもの。
//
//  本ファイルには:
//    - 数値（HP, ATK, Speed, BattalionDefense 等）         ← OK
//    - 配置 row や効果スコープなどの enum 値                ← OK
//    - 婚姻ポイントシステムの拡張用パラメータ                ← OK（既定値）
//  本ファイルに含めないもの:
//    - 日本語表示テキスト（役割・使い方・フレーバー・名前等） ← localization_ja.json
//    - 文字列ハードコード（ジョブ識別子は enum のみ）         ← JobData.cs
//
//  読み取り専用静的辞書として保持し、ランタイムで一切変更されない。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Job;

/// <summary>
/// 全 8 ジョブの完全な不変データを保持する静的 SoT。
/// </summary>
public static class JobMaster
{
    // ─── 全 8 ジョブのデータ ─────────────────────────────────────────────

    /// <summary>
    /// JobId → JobDefinition の不変辞書。
    /// 全ジョブのステータス・配置メタ・婚姻フック・ロール貢献値・特殊パッシブを
    /// 1 箇所に集約する SoT。新ジョブ追加時は本辞書にエントリを足し、
    /// localization_ja.json に対応キーを足すだけで完結する。
    /// </summary>
    public static readonly ImmutableDictionary<JobId, JobDefinition> All
        = BuildAll();

    private static ImmutableDictionary<JobId, JobDefinition> BuildAll()
    {
        var b = ImmutableDictionary.CreateBuilder<JobId, JobDefinition>();

        // ── 鉄壁騎士: 前衛の盾。BattalionDefense + SquadDefense ──────────
        b.Add(JobId.IronWallKnight, new JobDefinition
        {
            Id = JobId.IronWallKnight,
            Stats = new JobStats
            {
                MaxHp            = 250,
                Speed            = 10,
                FrontAttack      = 50,
                RearAttack       = 10,
                BattalionDefense = 10,
                SquadDefense     = 15,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(SquadRow.Front),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.EntireBattalionWhenFront,
                EffectKind      = EffectKind.Defend,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 30, // BattalionDefense + SquadDefense の戦術的価値
        });

        // ── 重装歩兵: 単騎完結の前衛アタッカー。SquadDefense + 高 HP/FA ──
        b.Add(JobId.HeavyInfantry, new JobDefinition
        {
            Id = JobId.HeavyInfantry,
            Stats = new JobStats
            {
                MaxHp            = 300,
                Speed            = 15,
                FrontAttack      = 70,
                RearAttack       = 20,
                BattalionDefense = 0,
                SquadDefense     = 10,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(SquadRow.Front),
                EffectRange     = ImmutableArray.Create(SquadRow.Front),
                EffectScope     = EffectScope.OwnSquad,
                EffectKind      = EffectKind.Attack,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 0, // 純戦闘力で既に高い（HP=300, FA=70）
        });

        // ── 旗手: 大隊全員の InitiativeBuff 撒き（最大規模） ─────────────
        b.Add(JobId.StandardBearer, new JobDefinition
        {
            Id = JobId.StandardBearer,
            Stats = new JobStats
            {
                MaxHp            = 150,
                Speed            = 20,
                FrontAttack      = 30,
                RearAttack       = 30,
                BattalionDefense = 0,
                SquadDefense     = 5,
                InitiativeBuff   = 40,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.EntireBattalion,
                EffectKind      = EffectKind.Buff,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 65, // 大隊全員を底上げするバフ役の戦術的価値
        });

        // ── 戦術官: 軽量 InitiativeBuff + 自身も中速 ─────────────────────
        b.Add(JobId.Tactician, new JobDefinition
        {
            Id = JobId.Tactician,
            Stats = new JobStats
            {
                MaxHp            = 120,
                Speed            = 35,
                FrontAttack      = 20,
                RearAttack       = 20,
                BattalionDefense = 0,
                SquadDefense     = 0,
                InitiativeBuff   = 20,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.EntireBattalion,
                EffectKind      = EffectKind.Buff,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 65, // 軽量バフ + 速度値の戦術的価値
        });

        // ── 衛生兵: 唯一の継続回復役（TurnEndSquadHeal）──────────────────
        b.Add(JobId.Medic, new JobDefinition
        {
            Id = JobId.Medic,
            Stats = new JobStats
            {
                MaxHp            = 100,
                Speed            = 25,
                FrontAttack      = 10,
                RearAttack       = 10,
                BattalionDefense = 0,
                SquadDefense     = 0,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 30,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.OwnSquad,
                EffectKind      = EffectKind.Heal,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 90, // 唯一の継続支援、最大加点
        });

        // ── 狙撃兵: 後衛超火力 + 連続攻撃（ConsecutiveStrike）──────────
        b.Add(JobId.Sniper, new JobDefinition
        {
            Id = JobId.Sniper,
            Stats = new JobStats
            {
                MaxHp            = 80,
                Speed            = 40,
                FrontAttack      = 20,
                RearAttack       = 90,
                BattalionDefense = 0,
                SquadDefense     = 0,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.OwnRow,
                EffectKind      = EffectKind.Attack,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 0, // 純戦闘力で既に高い（RA=90 + 連続攻撃）
            // ★ 数値プロパティで表現できない特殊パッシブ
            SpecialPassives = ImmutableArray.Create(PassiveKind.ConsecutiveStrike),
        });

        // ── 呪術師: 全職最強の砲台、ただし最も脆い ───────────────────────
        b.Add(JobId.Sorcerer, new JobDefinition
        {
            Id = JobId.Sorcerer,
            Stats = new JobStats
            {
                MaxHp            = 40,
                Speed            = 15,
                FrontAttack      = 10,
                RearAttack       = 120,
                BattalionDefense = 0,
                SquadDefense     = 0,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight),
                EffectScope     = EffectScope.OwnRow,
                EffectKind      = EffectKind.Attack,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 0, // 純戦闘力で既に最強（RA=120）
        });

        // ── 斥候: 速度最強の先制削り役 ────────────────────────────────────
        b.Add(JobId.Scout, new JobDefinition
        {
            Id = JobId.Scout,
            Stats = new JobStats
            {
                MaxHp            = 90,
                Speed            = 60,
                FrontAttack      = 40,
                RearAttack       = 40,
                BattalionDefense = 0,
                SquadDefense     = 0,
                InitiativeBuff   = 0,
                TurnEndSquadHeal = 0,
            },
            FormationGuide = new JobFormationGuide
            {
                RecommendedRows = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight, SquadRow.Front),
                EffectRange     = ImmutableArray.Create(
                    SquadRow.RearLeft, SquadRow.RearRight, SquadRow.Front),
                EffectScope     = EffectScope.OwnRow,
                EffectKind      = EffectKind.Attack,
            },
            MarriageHooks = new JobMarriageHooks(),
            RoleBonus     = 30, // 速度差先制（SPD=60 の戦術的価値）
        });

        return b.ToImmutable();
    }

    // ─── 公開ヘルパー ──────────────────────────────────────────────────────

    /// <summary>
    /// 指定ジョブの定義を取得する。null や未知ジョブの場合は null を返す。
    /// </summary>
    public static JobDefinition? Find(JobId? id)
        => id.HasValue && All.TryGetValue(id.Value, out var def) ? def : null;

    /// <summary>
    /// 指定ジョブが指定種別のパッシブを発動する条件を満たすかをデータ駆動で判定する。
    ///
    /// 数値プロパティに依存するパッシブ（BattalionDefense / SquadDefense /
    /// InitiativeBuff / TurnEndSquadHeal）は JobStats の対応値が 0 より大きいか、
    /// 特殊パッシブ（ConsecutiveStrike 等）は SpecialPassives に含まれるかで判定する。
    ///
    /// BattleManager 翻訳時に「unit.job == "..."」の文字列マッチを完全排除するため、
    /// 本ヘルパーを predicate として使う設計を予定。
    /// </summary>
    public static bool HasPassive(JobId id, PassiveKind kind)
    {
        if (!All.TryGetValue(id, out var def)) return false;
        return kind switch
        {
            PassiveKind.BattalionDefense => def.Stats.BattalionDefense > 0,
            PassiveKind.SquadDefense     => def.Stats.SquadDefense > 0,
            PassiveKind.InitiativeBuff   => def.Stats.InitiativeBuff > 0,
            PassiveKind.TurnEndSquadHeal => def.Stats.TurnEndSquadHeal > 0,
            _ => def.SpecialPassives.Contains(kind),
        };
    }

    /// <summary>
    /// TargetRating 算出で MaxHp を攻撃・速度と同じ目盛りへ均すための除数（HP は容量値ゆえ縮約する）。
    /// 式: floor(MaxHp / 本値 + max(FA, RA) + Speed) + RoleBonus。
    /// </summary>
    public const double HpRatingDivisor = 5.0;

    /// <summary>
    /// UI 比較用のジョブ基準総合値を算出する（旧 baseJobRating + ROLE_BONUS）。
    /// 全ジョブの平均総合値が 140〜148 程度に揃うよう ROLE_BONUS で調整済み。
    ///
    /// 式: floor(MaxHp / HpRatingDivisor + max(FrontAttack, RearAttack) + Speed) + RoleBonus
    /// </summary>
    public static int CalculateTargetRating(JobId id)
    {
        var def = All[id];
        var s = def.Stats;
        var baseStat = (int)Math.Floor(
            s.MaxHp / HpRatingDivisor + Math.Max(s.FrontAttack, s.RearAttack) + s.Speed);
        return baseStat + def.RoleBonus;
    }

    /// <summary>
    /// 全ジョブの基準総合値を JobId → int の不変辞書で返す（UI 参照用）。
    /// </summary>
    public static readonly ImmutableDictionary<JobId, int> TargetRating
        = BuildTargetRating();

    private static ImmutableDictionary<JobId, int> BuildTargetRating()
    {
        var b = ImmutableDictionary.CreateBuilder<JobId, int>();
        foreach (var id in All.Keys)
        {
            b.Add(id, CalculateTargetRating(id));
        }
        return b.ToImmutable();
    }

    /// <summary>
    /// 表示順（人事画面のソート・編成画面のフィルタプルダウンで使う固定順）。
    /// 役割の系統別（防御 → 支援 → 回復 → 攻撃）でグルーピング。
    /// </summary>
    public static readonly ImmutableArray<JobId> DisplayOrder
        = ImmutableArray.Create(
            JobId.IronWallKnight,
            JobId.HeavyInfantry,
            JobId.StandardBearer,
            JobId.Tactician,
            JobId.Medic,
            JobId.Sniper,
            JobId.Sorcerer,
            JobId.Scout);
}
