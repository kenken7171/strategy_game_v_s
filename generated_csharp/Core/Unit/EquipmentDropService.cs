// =============================================================================
//  ChronicleKnights — EquipmentDropService.cs
// -----------------------------------------------------------------------------
//  装備ドロップ（EquipmentDrop 予言の報酬）の「3 択候補」を決定論的に生成する
//  Godot 非依存の純粋層。
//
//  ★ 設計 — ハクスラの脳汁ポイント:
//    1 個を自動付与する旧仕様を、複数の候補から 1 つを選ぶ「3 択ドロップ」へ拡張する
//    ための候補生成器。各候補は種別（ItemId）・Affix（AffixMaster でロール）を散らし、
//    レベルは予言の Value（クランプ済）で揃える。選ばれた 1 個は持ち物（インベントリ）
//    へ入り、残りは破棄される（呼び出し側の責務）。
//
//  ★ 決定論（開発憲法④）:
//    種別・Affix の抽選は注入された Random から。個体 Guid も外部注入できる
//    オーバーロードを提供し、テストで候補列を厳密にアサートできる。
//
//  略称（正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;

namespace ChronicleKnights.Core.Units;

/// <summary>
/// 装備ドロップの 3 択候補を決定論的に生成する純粋ユーティリティ。状態を持たない。
/// </summary>
public static class EquipmentDropService
{
    /// <summary>ドロップで提示する候補数（3 択）。</summary>
    public const int DropCandidateCount = 3;

    /// <summary>
    /// 指定レベルのドロップ候補を <paramref name="count"/> 個生成する。個体 Guid は
    /// <see cref="Guid.NewGuid"/> で採番する。
    /// </summary>
    /// <param name="level">候補装備のレベル（Equipment の下限〜上限へクランプ）。</param>
    /// <param name="rng">注入された乱数（種別・Affix 抽選の決定論の源）。</param>
    /// <param name="count">候補数（既定 3）。</param>
    public static ImmutableArray<Equipment> RollCandidates(
        int level, Random rng, int count = DropCandidateCount)
        => RollCandidates(level, rng, count, static () => Guid.NewGuid());

    /// <summary>
    /// <see cref="RollCandidates(int, Random, int)"/> の決定論オーバーロード。個体 Guid を
    /// <paramref name="idFactory"/> から採番でき、テストで候補列を厳密にアサートできる。
    /// </summary>
    /// <param name="level">候補装備のレベル（Equipment の下限〜上限へクランプ）。</param>
    /// <param name="rng">注入された乱数（種別・Affix 抽選の決定論の源）。</param>
    /// <param name="count">候補数。</param>
    /// <param name="idFactory">個体 Guid の採番器（決定論注入用）。</param>
    public static ImmutableArray<Equipment> RollCandidates(
        int level, Random rng, int count, Func<Guid> idFactory)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(idFactory);
        if (count <= 0) return ImmutableArray<Equipment>.Empty;

        var clampedLevel = Math.Clamp(
            level, Equipment.MinEquipmentLevel, Equipment.MaxEquipmentLevel);
        var items = Enum.GetValues<ItemId>();

        var builder = ImmutableArray.CreateBuilder<Equipment>(count);
        for (var i = 0; i < count; i++)
        {
            var itemId = items[rng.Next(items.Length)];
            var affixKeys = AffixMaster.RollAffixKeys(clampedLevel, rng);
            builder.Add(new Equipment
            {
                Id        = idFactory(),
                ItemId    = itemId,
                Level     = clampedLevel,
                AffixKeys = affixKeys,
            });
        }
        return builder.MoveToImmutable();
    }
}
