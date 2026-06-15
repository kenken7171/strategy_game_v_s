// =============================================================================
//  ChronicleKnights — MarriageService.cs
// -----------------------------------------------------------------------------
//  手動婚姻（配合）と自然婚姻（純愛ルート）の判定・実行を司る純粋関数群。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  セクション 4) の婚姻機能の本体。
//
//  経済責務の分離:
//    - 残高管理・収入・消費は PointsEconomy.cs に完全委譲
//    - 本ファイルは「結婚特有の業務ロジック」のみを扱う:
//        自然婚姻判定 / コスト算出 / 子ユニット生成 / 結果集約
//
//  ★ 重要設計ルール:
//    - 結婚は手動・即時・有償が基本
//    - ただし BattleAffinity が NaturalMarriageThreshold (定数) に
//      双方向で達したペアは『タダ結婚（コスト 0）』の特権を得る
//    - 子の生成は NewbornSpec で呼び出し側が仕様注入（名前キー・寿命等）
//    - Job は OverrideJob 指定 or 父母から 50%/50% で乱数継承
//    - 将来 Affix 継承システム導入時の差し込み余白として InitialEquipment
//      が NewbornSpec に用意されている
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Naming;
using ChronicleKnights.Core.Units;

namespace ChronicleKnights.Core.Managers;

// ─── 子ユニット生成のための差し込みポイント ─────────────────────────────────

/// <summary>
/// 新生児ユニット（子供）の構築仕様を呼び出し側から受け取るための値レコード。
/// 名前キー・初期年齢・寿命など、Config 依存値や乱数決定値は呼び出し側で
/// 準備して本レコードに集約する。
/// </summary>
public sealed record NewbornSpec
{
    /// <summary>
    /// 子の名前 (first) を引く Config キー。null の場合は ExecuteManualMarriage が
    /// NameGenerator で「両親から継承した文化圏」の重複しないキーを自動生成する。
    /// </summary>
    public string? FirstNameKey { get; init; }

    /// <summary>
    /// 子の名前 (last) を引く Config キー。null の場合は自動生成（同上）。
    /// </summary>
    public string? LastNameKey { get; init; }

    /// <summary>子の初期年齢。タイムスキップで成長することを前提に 0 推奨。</summary>
    public int InitialAge { get; init; } = 0;

    /// <summary>子の寿命（個体差を持たせる）。</summary>
    public required int MaxAge { get; init; }

    /// <summary>
    /// 子のジョブを明示的に上書きする場合に指定。null なら父か母のジョブを
    /// 乱数で継承（50% / 50%）。将来 Affix 継承や血統補正で重み付けする土台。
    /// </summary>
    public JobId? OverrideJob { get; init; }

    /// <summary>子に初期装備を持たせる場合（既定 null = 装備なし）。</summary>
    public Equipment? InitialEquipment { get; init; }
}

/// <summary>
/// 婚姻実行の結果。新しい経済状態・誕生した子・実際に支払ったコスト・
/// タダ結婚だったかのフラグを集約する不変レコード。
/// </summary>
public sealed record MarriageResult
{
    /// <summary>消費後の新しい経済状態（自然婚姻なら未変化）。</summary>
    public required PointsEconomy NewEconomy { get; init; }
    /// <summary>誕生した子ユニット（Lv1 / Affix 継承の余白付き / Parentage 刻印済み）。</summary>
    public required Unit Child { get; init; }
    /// <summary>実際に支払ったコスト（自然婚姻なら 0）。</summary>
    public required int CostPaid { get; init; }
    /// <summary>タダ結婚成立フラグ（BattleAffinity 双方向 ≥ Threshold）。</summary>
    public required bool WasNaturalMarriage { get; init; }
    /// <summary>選んだ父ユニット ID（ログ・UI 用）。</summary>
    public required Guid FatherId { get; init; }
    /// <summary>選んだ母ユニット ID（ログ・UI 用）。</summary>
    public required Guid MotherId { get; init; }

    /// <summary>
    /// 配偶者 Id を相互リンクした後の父ユニット（SpouseId = MotherId）。
    /// 呼び出し側（ChronicleGlobal.ExecuteMarriage）は roster 内の旧父を
    /// 本インスタンスで置換し、婚姻の横リンクを SoT に定着させる責務を負う。
    /// </summary>
    public required Unit UpdatedFather { get; init; }

    /// <summary>
    /// 配偶者 Id を相互リンクした後の母ユニット（SpouseId = FatherId）。
    /// </summary>
    public required Unit UpdatedMother { get; init; }
}

/// <summary>
/// 結婚コスト見積もりレコード。UI で「いま結婚するといくら必要か」を見せる用。
/// </summary>
public sealed record MarriageQuote
{
    public required int Cost { get; init; }
    public required bool IsNaturalMarriage { get; init; }
    public required bool IsAffordable { get; init; }
}

// ─── 結婚サービス本体（静的純粋関数群） ───────────────────────────────────

/// <summary>
/// 結婚に関するすべての判定・算出・実行を提供する静的サービス。
/// 状態は持たず、PointsEconomy を引数で受け取って消費後の新状態を返す関数型 API。
/// </summary>
public static class MarriageService
{
    // ─── 婚姻特化定数 ─────────────────────────────────────────────────────

    /// <summary>
    /// 自然婚姻成立の双方向しきい値。a→b と b→a の両方向の BattleAffinity が
    /// 本値以上に達したペアはコスト 0 で結婚できる（タダ結婚）。
    /// 「一定の膨大な規定値」として 150 を採用。
    /// </summary>
    public const int NaturalMarriageThreshold = 150;

    /// <summary>
    /// 必要コスト算出の基準スケール除数。両親 TotalRating × MarriageCostMultiplier
    /// の和を本値で割り、Math.Ceiling して切上でコスト pt を出す。
    /// </summary>
    public const int CostDivisor = 20;

    // ─── 自然婚姻判定 ─────────────────────────────────────────────────────

    /// <summary>
    /// 2 ユニットが自然婚姻ペア（双方向で BattleAffinity が
    /// NaturalMarriageThreshold 以上）になっているか判定する純粋関数。
    /// </summary>
    public static bool IsNaturalMarriagePair(Unit a, Unit b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Id == b.Id) return false;
        var aToB = a.BattleAffinity.TryGetValue(b.Id, out var ab) ? ab : 0;
        var bToA = b.BattleAffinity.TryGetValue(a.Id, out var ba) ? ba : 0;
        return aToB >= NaturalMarriageThreshold && bToA >= NaturalMarriageThreshold;
    }

    /// <summary>
    /// 2 ユニットが婚姻可能な男女ペア（異性）かを判定する純粋関数。
    /// 婚姻は父 = Male / 母 = Female の男女ペアでのみ成立する（子を生す V&amp;B 規範）。
    /// UI のドロップダウン分離（父＝男性のみ／母＝女性のみ）の根拠であり、
    /// <see cref="ExecuteManualMarriage"/> のハードガードでもある。
    /// </summary>
    public static bool AreOppositeGenders(Unit a, Unit b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Gender != b.Gender;
    }

    // ─── 結婚コスト算出 ───────────────────────────────────────────────────

    /// <summary>
    /// 結婚に必要なポイントを算出する純粋関数。
    ///
    /// 算式（コア）:
    ///   父コスト = JobMaster.CalculateTargetRating(father.Job)
    ///              * JobMarriageHooks.MarriageCostMultiplier
    ///   母コスト = 同上 for mother
    ///   合計     = ceil((父コスト + 母コスト) / CostDivisor)
    ///
    /// MarriageCostMultiplier が既定 1.0 なら、両者 TotalRating 145 同士で
    /// ceil((145 + 145) / 20) = 15 pt 程度に落ち着く。
    ///
    /// 自然婚姻ペアの場合はこの値ではなく 0 として扱う（呼び出し側で IsNatural
    /// を確認してから本メソッドを呼ぶ運用）。
    /// </summary>
    public static int CalculateMarriageCost(Unit father, Unit mother)
    {
        ArgumentNullException.ThrowIfNull(father);
        ArgumentNullException.ThrowIfNull(mother);

        var fatherRating = JobMaster.CalculateTargetRating(father.Job);
        var motherRating = JobMaster.CalculateTargetRating(mother.Job);
        var fatherMul = JobMaster.Find(father.Job)?.MarriageHooks.MarriageCostMultiplier ?? 1.0;
        var motherMul = JobMaster.Find(mother.Job)?.MarriageHooks.MarriageCostMultiplier ?? 1.0;

        var weighted = (fatherRating * fatherMul) + (motherRating * motherMul);
        return (int)Math.Ceiling(weighted / CostDivisor);
    }

    // ─── 結婚コスト見積もり（UI 表示用） ──────────────────────────────────

    /// <summary>
    /// 結婚を確定せずにコスト見積もりだけを取得する。
    /// 自然婚姻ペアなら Cost = 0、それ以外は CalculateMarriageCost の値。
    /// IsAffordable で現在残高でまかなえるかも合わせて返す。
    /// </summary>
    public static MarriageQuote QuoteMarriage(
        PointsEconomy economy,
        Unit father,
        Unit mother)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(father);
        ArgumentNullException.ThrowIfNull(mother);

        var isNatural = IsNaturalMarriagePair(father, mother);
        var cost = isNatural ? 0 : CalculateMarriageCost(father, mother);
        return new MarriageQuote
        {
            Cost = cost,
            IsNaturalMarriage = isNatural,
            IsAffordable = economy.CanAfford(cost),
        };
    }

    // ─── 手動婚姻（配合）の実行 ───────────────────────────────────────────

    /// <summary>
    /// 手動婚姻を実行し、ポイントを消費して子ユニットを生成する。
    ///
    /// 流れ:
    ///   1. 自然婚姻判定 → IsNatural なら cost = 0
    ///   2. それ以外は CalculateMarriageCost で必要コスト算出
    ///   3. PointsEconomy.SpendPoints (共通サイフ) を通じて消費
    ///   4. 子ユニットを生成 → MarriageResult を返す
    ///
    /// 子は Level=1, Equipment は NewbornSpec.InitialEquipment（既定 null）,
    /// BattleAffinity は空。Job は newborn.OverrideJob で固定可、null なら
    /// 父母から 50%/50% で乱数継承（将来は Affix 重み付け対応の土台）。
    /// </summary>
    /// <param name="usedFirstNameKeys">
    /// 名前自動生成時の重複回避に使う使用済みファーストネームキー集合。
    /// NewbornSpec で名前キーが明示されている場合は参照されない。null の場合は
    /// 空集合として扱う（呼び出し側で現在の roster から渡すのが望ましい）。
    /// </param>
    public static MarriageResult ExecuteManualMarriage(
        PointsEconomy economy,
        Unit father,
        Unit mother,
        NewbornSpec newborn,
        Random rng,
        IReadOnlySet<string>? usedFirstNameKeys = null)
    {
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(father);
        ArgumentNullException.ThrowIfNull(mother);
        ArgumentNullException.ThrowIfNull(newborn);
        ArgumentNullException.ThrowIfNull(rng);

        if (!father.IsAlive)
            throw new InvalidOperationException("Father unit must be alive to marry.");
        if (!mother.IsAlive)
            throw new InvalidOperationException("Mother unit must be alive to marry.");
        if (father.Id == mother.Id)
            throw new ArgumentException(
                "Father and mother must be different units.", nameof(mother));
        // 婚姻は男女ペア（父=Male / 母=Female）でのみ成立する。UI でドロップダウンを
        // 性別分離して防いでいるが、SoT 直叩き等の経路に対するハードガードを置く。
        if (father.Gender == mother.Gender)
            throw new ArgumentException(
                "Marriage requires opposite genders (a male father and a female mother).",
                nameof(mother));

        // 1. 自然婚姻判定
        var isNatural = IsNaturalMarriagePair(father, mother);
        var cost = isNatural ? 0 : CalculateMarriageCost(father, mother);

        // 2. 共通サイフから消費（自然婚姻ならコスト 0 で素通り）
        var nextEconomy = cost == 0 ? economy : economy.SpendPoints(cost);

        // 3. 子を生成（Lv1, ジョブは父母どちらかから乱数継承）
        var inheritedJob = newborn.OverrideJob
            ?? (rng.NextDouble() < 0.5 ? father.Job : mother.Job);

        // 4. 文化圏は両親のどちらかを 50%/50% で継承（血統系譜の追跡）。
        var childOrigin = rng.NextDouble() < 0.5 ? father.Origin : mother.Origin;

        // 5. 名前キーは NewbornSpec の明示値を優先し、未指定なら NameGenerator が
        //    「継承した文化圏」のプールから重複しないキーを払い出す。
        var used = usedFirstNameKeys ?? new HashSet<string>(StringComparer.Ordinal);
        var generated = NameGenerator.Draw(
            childOrigin,
            NameGenerator.PickRandomGender(rng),
            used,
            rng);

        var child = new Unit
        {
            Id = Guid.NewGuid(),
            Job = inheritedJob,
            Age = newborn.InitialAge,
            MaxAge = newborn.MaxAge,
            FirstNameKey = newborn.FirstNameKey ?? generated.FirstNameKey,
            LastNameKey = newborn.LastNameKey ?? generated.LastNameKey,
            Origin = childOrigin,
            // 子の性別は自動生成名と整合する generated.Gender を採用（名前と性別の一致を担保）。
            Gender = generated.Gender,
            Level = Unit.InitialLevel,
            MainEquipment = newborn.InitialEquipment,
            BattleAffinity = ImmutableDictionary<Guid, int>.Empty,
            IsDead = false,
            // ★ 血統リンクを刻む。これが家系図の縦軸（父母→子）の唯一のソース。
            Parentage = new Parentage { FatherId = father.Id, MotherId = mother.Id },
        };

        // 6. 婚姻の横リンク（配偶者）を双方へ相互定着させる。呼び出し側が roster の
        //    旧父・旧母をこれらで置換することで、SoT に「夫婦」が記録される。
        var updatedFather = father.WithSpouse(mother.Id);
        var updatedMother = mother.WithSpouse(father.Id);

        return new MarriageResult
        {
            NewEconomy = nextEconomy,
            Child = child,
            CostPaid = cost,
            WasNaturalMarriage = isNatural,
            FatherId = father.Id,
            MotherId = mother.Id,
            UpdatedFather = updatedFather,
            UpdatedMother = updatedMother,
        };
    }
}
