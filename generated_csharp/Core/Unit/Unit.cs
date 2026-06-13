// =============================================================================
//  ChronicleKnights — Unit.cs
// -----------------------------------------------------------------------------
//  旅団員 1 名分のすべての状態を表す完全不変なレコード。
//
//  ハクスラ・ローグライト設計憲法 (docs/MIGRATION_GODOT_HACK_AND_SLASH.md
//  フェーズ 1〜3) の中核データ:
//
//    - 完全ロスト: 戦闘で死亡 (IsDead = true) したユニットは旅団から
//                  永久に失われる。装備も同時にロストする。
//    - レベル上限 3: Level は 1〜3 のみ。WithLevelUp() で +1、上限到達
//                  時は overflow フラグで通知（ラストヒット報酬の余り）。
//    - Lv3 限定引退: Level == 3 のユニットだけが明示的引退（リタイア）
//                  対象になる。Lv1 / Lv2 のユニットは自然死または戦闘死
//                  のみで旅団から離脱する。
//    - タイムスキップ: 予言選択で SkipYears だけ Age が一気に進む。
//                  寿命を超えた場合は HasReachedMaxAge が true になる。
//    - 自然婚姻ポイント: 戦闘での隣接・バフ等により他ユニットへの
//                  BattleAffinity が蓄積する（手動婚姻のヒント値）。
//                  ※憲法セクション 4 に従い、結婚条件には不使用。
//                    結婚は MarriagePoints の支払いで即時実行される。
//
//  本ファイルには:
//    - ユニットの不変構造（Id / Job / Age / MaxAge / Level / Names /
//                          Equipment / BattleAffinity / IsDead）
//    - 不変更新メソッド (WithAgeProgress / WithLevelUp / WithAddedAffinity 等)
//    - 派生プロパティ (IsAlive / HasReachedMaxAge / CanRetire 等)
//  含まれないもの:
//    - 数値ステータス（HP/ATK/SPD/BattalionDefense 等）
//        → Job 経由で JobMaster から動的に解決（Unit はジョブ ID のみ保持）
//    - 日本語名前テキスト
//        → FirstNameKey / LastNameKey で Config から引き当て
//    - 略称（BDF/SDF/AB/HL/FA/RA）は完全未使用
//
//  名前空間ノート:
//    folder は generated_csharp/Core/Unit/ だが、namespace は型名 `Unit` との
//    衝突を避けるため `Units` (複数形) を採用。Microsoft 公式の命名ガイドライン
//    (Pluralize namespace names) にも整合する。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;
using ChronicleKnights.Core.Naming;

namespace ChronicleKnights.Core.Units;

/// <summary>
/// 血統リンク（親子関係）の不変レコード。
///
/// 婚姻で生まれた子ユニットにのみ刻まれる（手動入団・スカウト由来の初代は null）。
/// 家系図オーバーレイ (PedigreeOverlay) が縦系の血の連鎖を手繰り寄せる際の
/// 唯一のソース。Father/Mother いずれの Id も「過去に実在した（あるいは現存する）
/// ユニット」を指すが、当該ユニットが既に旅団から失われている可能性があるため、
/// 解決側は必ず TryGetValue + null 番兵で参照欠落に備える責務を負う。
/// </summary>
public sealed record Parentage
{
    /// <summary>父ユニットの Id。</summary>
    public required Guid FatherId { get; init; }

    /// <summary>母ユニットの Id。</summary>
    public required Guid MotherId { get; init; }
}

/// <summary>
/// 旅団員 1 名の完全不変なスナップショット。
///
/// すべてのフィールドは init-only。状態を変更するときは必ず With* メソッドで
/// 新インスタンスを返す関数型スタイルに従う（TS 版の Unit.ts と同じ思想）。
/// </summary>
public sealed record Unit
{
    // ─── 定数 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ユニットレベルの絶対上限（ハクスラ憲法仕様: 1〜3）。
    /// Lv4 以上は WithLevelUp の overflow=true で表現し、装備強化等の別系統に
    /// 余剰経験値を流す設計を想定。
    /// </summary>
    public const int MaxUnitLevel = 3;

    /// <summary>ユニットレベルの初期値（旅団加入直後）。</summary>
    public const int InitialLevel = 1;

    /// <summary>Lv3 限定引退ルールで、引退可能となる最小レベル。</summary>
    public const int RetirementEligibleLevel = MaxUnitLevel;

    // ─── 必須プロパティ ───────────────────────────────────────────────────

    /// <summary>ユニットの一意な識別子。BattleAffinity の key としても使われる。</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// ジョブ識別子。HP/ATK/SPD/各種パッシブ値は本フィールドから
    /// JobMaster.All[Job].Stats で動的に解決する（Unit 自身には保持しない）。
    /// </summary>
    public required JobId Job { get; init; }

    /// <summary>現在の年齢。WithAgeProgress(years) で加算される。</summary>
    public required int Age { get; init; }

    /// <summary>
    /// ユニット個別の寿命。Age がこれ以上になると HasReachedMaxAge が true になり、
    /// 旅団管理側で「自然死による離脱」として処理される（完全ロスト）。
    /// </summary>
    public required int MaxAge { get; init; }

    /// <summary>
    /// ファーストネームの Config 引き当てキー。
    /// 例: "name-ja-male-001" → "太郎"。表示テキストは
    /// Config/localization_ja.json の names.firstNames 等から解決する。
    /// </summary>
    public required string FirstNameKey { get; init; }

    /// <summary>
    /// ラストネームの Config 引き当てキー。
    /// 例: "name-european-002" → "Aldridge"。
    /// </summary>
    public required string LastNameKey { get; init; }

    /// <summary>
    /// ユニットの命名文化圏（血統属性）。名前プールの分離だけでなく、
    /// スカウト由来・婚姻継承でどの系譜を引いているかを識別・追跡するために
    /// ユニットへ永続保持する（セーブ対象）。
    ///
    /// 既定値は Origin.European（手動入団・既存セーブ互換のための安全側デフォルト）。
    /// NameGenerator が払い出した GeneratedName.Origin をそのまま格納する想定。
    /// </summary>
    public Origin Origin { get; init; } = Origin.European;

    /// <summary>
    /// ユニットの現在レベル（1 〜 MaxUnitLevel=3）。
    /// 既定値は InitialLevel (=1)。WithLevelUp で +1 する。
    /// </summary>
    public int Level { get; init; } = InitialLevel;

    /// <summary>
    /// 装備しているメインアイテム。装備なしの場合は null。
    /// ユニットが戦闘中に死亡 (IsDead = true) した場合、本装備も同時に
    /// ロストする（完全ロスト仕様、責務は RosterManager 側）。
    /// </summary>
    public Equipment? MainEquipment { get; init; }

    /// <summary>
    /// 他ユニットの Guid → 自然婚姻ポイント（好感度）の不変マップ。
    /// 戦闘中の隣接配置・同分隊メンバー・バフ授受などで蓄積する。
    ///
    /// ★ 憲法セクション 4 ルールに従い、結婚成立条件には一切使用しない
    ///   （結婚は MarriagePoints の支払いで即時実行）。
    ///   本フィールドは「相性ヒント」「血統補正」「UI 表示」用途のみ。
    /// </summary>
    public IReadOnlyDictionary<Guid, int> BattleAffinity { get; init; }
        = ImmutableDictionary<Guid, int>.Empty;

    /// <summary>
    /// 戦闘中の死亡フラグ。BattleManager が HP 0 到達時に true を設定する。
    /// 一度 true になったユニットは旅団から永久に失われ、復活手段は存在しない
    /// （完全ロスト）。
    /// </summary>
    public bool IsDead { get; init; } = false;

    /// <summary>
    /// 血統リンク（父母 Id）。婚姻で生まれた子のみ非 null。
    /// 既定 null = 初代（手動入団・スカウト由来。両親を持たない系譜の根）。
    /// PedigreeOverlay が縦系（祖父母→父母→本人→子孫）を遡上・降下する際の唯一の鍵。
    /// </summary>
    public Parentage? Parentage { get; init; }

    /// <summary>
    /// 配偶者ユニットの Id。婚姻成立時に双方へ相互リンクされる。
    /// 既定 null = 未婚。家系図で「本人・配偶者」を横に並べる際に参照する。
    /// </summary>
    public Guid? SpouseId { get; init; }

    // ─── 派生プロパティ（純粋な読み取り専用） ─────────────────────────────

    /// <summary>戦闘で死亡しておらず、かつ寿命にも達していない状態。</summary>
    public bool IsAlive => !IsDead && !HasReachedMaxAge;

    /// <summary>
    /// 寿命を超えたか。タイムスキップで Age が一気に進む可能性があるため、
    /// 等号ではなく以上で判定する。
    /// </summary>
    public bool HasReachedMaxAge => Age >= MaxAge;

    /// <summary>レベルが上限 (3) に達しているか。</summary>
    public bool IsAtMaxLevel => Level >= MaxUnitLevel;

    /// <summary>
    /// 自然な離脱（死亡 or 寿命）または引退で旅団から消える対象になっているか。
    /// 旅団管理側がアクティブ ロスター更新の判定に使用。
    /// </summary>
    public bool IsRemovedFromRoster => IsDead || HasReachedMaxAge;

    /// <summary>
    /// 明示引退（リタイア）の対象として選べるか。
    /// Lv3 限定ルールに従い Level == MaxUnitLevel かつ生存中のみ true。
    /// </summary>
    public bool CanRetire => IsAlive && Level >= RetirementEligibleLevel;

    /// <summary>装備を所持しているか（MainEquipment != null）。</summary>
    public bool HasEquipment => MainEquipment is not null;

    /// <summary>
    /// 現在装着している装備の種別（<see cref="ItemId"/>）を読み取り専用で射影する派生プロパティ。
    /// 装備なし（<see cref="MainEquipment"/> == null）の場合は null を返す。
    ///
    /// ★ 単一 SoT の堅持（設計憲法③）:
    ///   装備の正本はあくまで完全不変な <see cref="MainEquipment"/>（個体 Guid・レベル・Affix まで
    ///   保持する豊かなレコード）であり、本プロパティはそこから ItemId だけを引き出す純粋な射影に
    ///   過ぎない。ItemId を別フィールドとして二重に持たせる（＝SoT 分裂）ことはしない。また
    ///   「装備なし」は ItemId enum に番兵 None を増設するのではなく nullable（ItemId?）の null で
    ///   表現する。これにより BattleSpoils / ShopService / SaveSerializer 等の網羅 switch
    ///   （CS8509/CS8524）を 1 つも破壊せず、5 大マスター enum の純度を保つ。
    /// </summary>
    public ItemId? EquippedItemId => MainEquipment?.ItemId;

    /// <summary>血統リンク（父母）を持つか。false の場合は系譜の根（初代）。</summary>
    public bool HasParentage => Parentage is not null;

    /// <summary>配偶者を持つか（SpouseId != null）。</summary>
    public bool IsMarried => SpouseId is not null;

    // ─── 不変更新メソッド ─────────────────────────────────────────────────

    /// <summary>
    /// 指定年数だけ Age を一気に進めた新インスタンスを返す（タイムスキップ専用）。
    ///
    /// Prophecy.SkipYears の値（典型 2〜4 年）をそのまま渡す想定。
    /// 結果として Age >= MaxAge になった場合、HasReachedMaxAge が true となり、
    /// 旅団管理側で「自然死による離脱」として処理される（IsRemovedFromRoster=true）。
    ///
    /// 注: 本メソッドは IsDead を変更しない（戦闘死と寿命死は意味が異なるため）。
    /// 寿命到達は派生プロパティ HasReachedMaxAge で判定する。
    /// </summary>
    /// <param name="years">スキップする年数（非負）</param>
    public Unit WithAgeProgress(int years)
    {
        if (years < 0)
            throw new ArgumentOutOfRangeException(
                nameof(years), years, "years must be non-negative for time-skip semantics");
        return this with { Age = Age + years };
    }

    /// <summary>
    /// ラストヒット報酬等でレベルを +1 した新インスタンスを返す。
    /// 既に MaxUnitLevel (=3) に達している場合はレベル不変のまま新インスタンスを返し、
    /// overflow = true で「余剰経験値の発生」を呼び出し側に通知する。
    ///
    /// 呼び出し側は overflow=true のときに、装備強化や婚姻ポイント加算等の
    /// 別系統への振替を行う設計を想定（憲法セクション 4-2 と連携可能）。
    /// </summary>
    /// <param name="overflow">true なら既に Lv3 でレベル変化なし</param>
    public Unit WithLevelUp(out bool overflow)
    {
        if (Level >= MaxUnitLevel)
        {
            overflow = true;
            // 状態は実質不変だが、戻り値契約「常に新インスタンス」を守るため with{} を使う。
            return this with { };
        }
        overflow = false;
        return this with { Level = Level + 1 };
    }

    /// <summary>
    /// 指定相手への BattleAffinity（自然婚姻ポイント）を加算した新インスタンスを返す。
    ///
    /// 同分隊で同戦闘を生き残った、バフを授受した、援護攻撃した、等の
    /// 蓄積イベントで呼ばれる想定。負の値（敵対関係）も理論上は許容するため、
    /// 値の正負はチェックしない（呼び出し側の設計に委ねる）。
    ///
    /// 自分自身への加算 (targetUnitId == this.Id) も技術的には許容するが、
    /// 意味的に不要なので呼び出し側で防ぐ責務とする。
    /// </summary>
    /// <param name="targetUnitId">好感度を蓄積する対象ユニットの Id</param>
    /// <param name="amount">加算量（既存値 + amount にセット）</param>
    public Unit WithAddedAffinity(Guid targetUnitId, int amount)
    {
        var currentValue = BattleAffinity.TryGetValue(targetUnitId, out var v) ? v : 0;
        var newValue = currentValue + amount;

        IReadOnlyDictionary<Guid, int> nextDict = BattleAffinity switch
        {
            // 既に ImmutableDictionary ならそのまま SetItem
            ImmutableDictionary<Guid, int> imm => imm.SetItem(targetUnitId, newValue),
            // それ以外なら一度コピーして ImmutableDictionary 化（不変性の徹底）
            _ => ImmutableDictionary.CreateRange(BattleAffinity).SetItem(targetUnitId, newValue),
        };
        return this with { BattleAffinity = nextDict };
    }

    /// <summary>
    /// 装備をセット/差し替えた新インスタンスを返す。null を渡すと装備解除。
    /// 既存装備の処理（ベンチに戻す/破棄する）は呼び出し側 (RosterManager) の責務。
    /// </summary>
    public Unit WithEquipment(Equipment? equipment)
        => this with { MainEquipment = equipment };

    /// <summary>
    /// 戦闘中に死亡したことをマークした新インスタンスを返す。
    /// 完全ロスト仕様により、本メソッドが呼ばれたユニットは旅団から永久に失われる。
    /// </summary>
    public Unit MarkDeadInBattle()
        => this with { IsDead = true };

    /// <summary>
    /// 血統リンク（父母 Id）を刻んだ新インスタンスを返す（婚姻で生まれた子に付与）。
    /// 父母が同一 Id の場合は系譜が縮退するため弾く。
    /// </summary>
    /// <param name="fatherId">父ユニットの Id</param>
    /// <param name="motherId">母ユニットの Id</param>
    public Unit WithParentage(Guid fatherId, Guid motherId)
    {
        if (fatherId == motherId)
            throw new ArgumentException(
                "Father and mother must be different units for parentage.", nameof(motherId));
        return this with { Parentage = new Parentage { FatherId = fatherId, MotherId = motherId } };
    }

    /// <summary>
    /// 配偶者 Id を相互リンクした新インスタンスを返す（婚姻成立時に双方へ適用）。
    /// 自分自身との婚姻は意味を成さないため弾く。
    /// </summary>
    /// <param name="spouseId">配偶者ユニットの Id</param>
    public Unit WithSpouse(Guid spouseId)
    {
        if (spouseId == Id)
            throw new ArgumentException(
                "A unit cannot be married to itself.", nameof(spouseId));
        return this with { SpouseId = spouseId };
    }
}
