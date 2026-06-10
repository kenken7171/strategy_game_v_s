// =============================================================================
//  ChronicleKnights — FormationBoard.cs
// -----------------------------------------------------------------------------
//  V字 3×3 編成盤面の純粋論理層（Godot 完全非依存）。
//
//  大隊 9 名を「3 分隊 × 各 3 スロット」の V 字に配置する盤面を、完全不変の
//  レコード群で表現する。配置・除去・交換・回転はすべて「自身は変更されず新しい
//  FormationBoard を返す」不変セマンティクスで提供し、xUnit から純粋に検証できる。
//
//  ★ V 字の幾何（行 = 分隊、列 = 分隊内スロット 0/1/2）:
//
//                    Front     [0][1][2]   ← 前衛（中央上にせり出す分隊）
//                   /      \
//            RearLeft        RearRight
//            [0][1][2]        [0][1][2]    ← 後衛 左／右（左下・右下の分隊）
//
//  ★ 二重 SoT の禁止（設計憲法 ③）:
//    盤面は Unit 実体を保持せず Guid?（OccupantId）だけを持つ薄い参照レイヤである。
//    Unit の正本は常に ChronicleGlobal のロスタ側にあり、盤面は「どのマスに誰の ID が
//    居るか」のみを表す。これにより状態の二重持ちを構造的に封じる。
//
//  ★ 不変条件（常に成立）:
//    - スロットは常にちょうど 9 個（3 行 × 3 列）、座標は一意。
//    - 同一 Guid は盤面上で最大 1 マスにしか存在しない（WithUnitAt が重複を自動退去）。
//
//  ★ 回転の意味論（TypeScript 版 BattleSimulator.rotateGrid の思想を継承）:
//      時計回り (Clockwise):     RearLeft → Front → RearRight → RearLeft
//      反時計回り (CounterClockwise): RearRight → Front → RearLeft → RearRight
//    いずれも分隊（行）単位で占有者の三つ組をまるごと移し替え、列順 (0/1/2) は
//    完全保持する。敵の分隊単位ダメージが「行」にバインドされる整合性を、盤面の
//    不変回転でそのまま再現できる。
//
//  ★ 等価性に関する注意:
//    本レコードの内部表現は ImmutableArray（参照ベースの等価性）であるため、
//    レコード既定の構造的等価（==）に内容一致を期待しないこと。盤面の比較は
//    OccupantAt / PlacedUnitIds 等のクエリ API を介して行う。
//
//  略称（大隊総守護力などの正式名称のみを使う方針）は本ファイルでも完全未使用。
// =============================================================================

using System;
using System.Collections.Immutable;
using ChronicleKnights.Core.Job;

namespace ChronicleKnights.Core.Formation;

// ─── 分隊行（V 字の 3 行） ───────────────────────────────────────────────────
//
//  V 字編成の 3 行（分隊）を表す SquadRow は、ジョブ定義側（Core.Job.JobData）に
//  既に正本が存在する（Front / RearLeft / RearRight、列挙順は回転の基準順でもある）。
//  本盤面はそれを再利用し、同概念の enum を二重定義しない（単一 SoT・設計憲法 ③）。
//  上の using ChronicleKnights.Core.Job; が当該 SquadRow を持ち込む。

// ─── 回転方向 ────────────────────────────────────────────────────────────────

/// <summary>
/// 分隊単位ローテーションの向き。
/// </summary>
public enum RotationDirection
{
    /// <summary>時計回り（RearLeft → Front → RearRight → RearLeft）。</summary>
    Clockwise,

    /// <summary>反時計回り（RearRight → Front → RearLeft → RearRight）。</summary>
    CounterClockwise,
}

// ─── スロット座標（行 × 列の値型） ──────────────────────────────────────────

/// <summary>
/// 盤面上の 1 マスの座標。行（<see cref="SquadRow"/>）と列（0/1/2）を持つ軽量な値型。
/// 等価比較・ハッシュが安価で、辞書キーや data-testid（formation-slot-{row}-{col}）の
/// 組み立てにそのまま使える。列の妥当性（0〜2）は盤面操作側で検証する。
/// </summary>
public readonly record struct SlotCoordinate(SquadRow Row, int Column);

// ─── スロット（占有者を持つ不変レコード） ──────────────────────────────────

/// <summary>
/// 盤面の 1 スロット。空席を許容するため占有者は <see cref="Guid"/>? で保持する
/// （null = 空席）。Unit 実体は持たず、ロスタ側 ID への参照のみを保持する。
/// </summary>
public sealed record FormationSlot
{
    /// <summary>このスロットの固定座標。</summary>
    public required SlotCoordinate Coordinate { get; init; }

    /// <summary>占有しているユニットの ID。空席なら null。</summary>
    public Guid? OccupantId { get; init; }

    /// <summary>空席か。</summary>
    public bool IsEmpty => OccupantId is null;

    /// <summary>占有されているか。</summary>
    public bool IsOccupied => OccupantId is not null;
}

// ─── 盤面（9 スロット固定の不変レコード） ──────────────────────────────────

/// <summary>
/// V 字 3×3 編成盤面。常にちょうど 9 個の固定スロットを内包する完全不変レコード。
/// 変更系 API はすべて新しい <see cref="FormationBoard"/> を返し、自身は変更しない。
/// </summary>
public sealed record FormationBoard
{
    // ─── 形状定数 ─────────────────────────────────────────────────────────

    /// <summary>1 分隊（行）あたりのスロット列数。</summary>
    public const int ColumnsPerRow = 3;

    /// <summary>分隊（行）の数。</summary>
    public const int RowCount = 3;

    /// <summary>盤面の総スロット数（= RowCount × ColumnsPerRow = 9）。</summary>
    public const int SlotCount = RowCount * ColumnsPerRow;

    /// <summary>
    /// 行の正準順（Front → RearLeft → RearRight）。スロットの内部格納順・回転の
    /// 基準順であり、インデックス算出（行番号 × 列数 + 列）と一致する。
    /// </summary>
    public static readonly ImmutableArray<SquadRow> RowOrder =
        ImmutableArray.Create(SquadRow.Front, SquadRow.RearLeft, SquadRow.RearRight);

    // ─── 内部表現 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 9 スロットを正準順（RowOrder × 列 0..2）で保持する不変配列。
    /// 既定構築を禁じるため required。生成は <see cref="Empty"/> を起点とする。
    /// </summary>
    public required ImmutableArray<FormationSlot> Slots { get; init; }

    // ─── 構築 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 9 マスすべてを空席で初期化した盤面を生成する。
    /// </summary>
    public static FormationBoard Empty()
    {
        var builder = ImmutableArray.CreateBuilder<FormationSlot>(SlotCount);
        foreach (var row in RowOrder)
        {
            for (int column = 0; column < ColumnsPerRow; column++)
            {
                builder.Add(new FormationSlot
                {
                    Coordinate = new SlotCoordinate(row, column),
                    OccupantId = null,
                });
            }
        }
        return new FormationBoard { Slots = builder.MoveToImmutable() };
    }

    // ─── 座標 → インデックス変換（内部） ─────────────────────────────────

    /// <summary>行の正準インデックス（Front=0 / RearLeft=1 / RearRight=2）。</summary>
    private static int RowIndex(SquadRow row) => row switch
    {
        SquadRow.Front     => 0,
        SquadRow.RearLeft  => 1,
        SquadRow.RearRight => 2,
        _ => throw new ArgumentOutOfRangeException(
            nameof(row), row, "未知の SquadRow です。"),
    };

    /// <summary>
    /// 座標を内部配列インデックス（0..8）へ変換する。列が 0..2 の範囲外なら
    /// 構造的不正（プログラマエラー）として例外を投げる。
    /// </summary>
    private static int IndexOf(SlotCoordinate coordinate)
    {
        if (coordinate.Column < 0 || coordinate.Column >= ColumnsPerRow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate), coordinate.Column,
                "Column は 0..2 の範囲でなければなりません。");
        }
        return RowIndex(coordinate.Row) * ColumnsPerRow + coordinate.Column;
    }

    // ─── クエリ ───────────────────────────────────────────────────────────

    /// <summary>指定座標のスロットを取得する。</summary>
    public FormationSlot SlotAt(SlotCoordinate coordinate) => Slots[IndexOf(coordinate)];

    /// <summary>指定座標の占有者 ID を取得する（空席なら null）。</summary>
    public Guid? OccupantAt(SlotCoordinate coordinate) => Slots[IndexOf(coordinate)].OccupantId;

    /// <summary>指定ユニットが盤面のいずれかのマスに配置されているか。</summary>
    public bool Contains(Guid unitId)
    {
        foreach (var slot in Slots)
        {
            if (slot.OccupantId == unitId) return true;
        }
        return false;
    }

    /// <summary>指定ユニットが居る座標を返す（未配置なら null）。一意性により最大 1 件。</summary>
    public SlotCoordinate? CoordinateOf(Guid unitId)
    {
        foreach (var slot in Slots)
        {
            if (slot.OccupantId == unitId) return slot.Coordinate;
        }
        return null;
    }

    /// <summary>配置済みユニット ID の一覧（正準順）。</summary>
    public ImmutableArray<Guid> PlacedUnitIds
    {
        get
        {
            var builder = ImmutableArray.CreateBuilder<Guid>();
            foreach (var slot in Slots)
            {
                if (slot.OccupantId is { } id) builder.Add(id);
            }
            return builder.ToImmutable();
        }
    }

    /// <summary>空席の座標一覧（正準順）。</summary>
    public ImmutableArray<SlotCoordinate> EmptyCoordinates
    {
        get
        {
            var builder = ImmutableArray.CreateBuilder<SlotCoordinate>();
            foreach (var slot in Slots)
            {
                if (slot.IsEmpty) builder.Add(slot.Coordinate);
            }
            return builder.ToImmutable();
        }
    }

    /// <summary>占有されているスロット数。</summary>
    public int OccupiedCount
    {
        get
        {
            int count = 0;
            foreach (var slot in Slots)
            {
                if (slot.IsOccupied) count++;
            }
            return count;
        }
    }

    /// <summary>9 マスすべてが埋まっているか。</summary>
    public bool IsFull => OccupiedCount == SlotCount;

    /// <summary>1 マスも占有されていないか。</summary>
    public bool IsEmpty => OccupiedCount == 0;

    // ─── 変更（すべて新インスタンスを返す不変操作） ─────────────────────

    /// <summary>
    /// 指定座標にユニットを配置した新しい盤面を返す。
    /// 同一 ID が既に別マスに居た場合は、そのマスを自動的に空席化してから配置する
    /// （盤面上で同一ユニットが二重に現れない不変条件を保証する）。対象マスに別の
    /// ユニットが居た場合、そのユニットは上書きされ盤面から退去する。
    /// </summary>
    public FormationBoard WithUnitAt(SlotCoordinate coordinate, Guid unitId)
    {
        int targetIndex = IndexOf(coordinate);

        var builder = Slots.ToBuilder();
        for (int i = 0; i < builder.Count; i++)
        {
            var slot = builder[i];
            if (i == targetIndex)
            {
                if (slot.OccupantId != unitId)
                {
                    builder[i] = slot with { OccupantId = unitId };
                }
            }
            else if (slot.OccupantId == unitId)
            {
                // 同一 ID が別マスに居たら退去（重複配置の自動解消）。
                builder[i] = slot with { OccupantId = null };
            }
        }

        return this with { Slots = builder.ToImmutable() };
    }

    /// <summary>
    /// 指定座標の占有者を取り除いた新しい盤面を返す。元から空席なら自身をそのまま返す。
    /// </summary>
    public FormationBoard ClearedAt(SlotCoordinate coordinate)
    {
        int index = IndexOf(coordinate);
        var slot = Slots[index];
        if (slot.IsEmpty) return this;

        return this with { Slots = Slots.SetItem(index, slot with { OccupantId = null }) };
    }

    /// <summary>
    /// 2 座標の占有者を入れ替えた新しい盤面を返す。同一座標が渡された場合は自身を返す。
    /// </summary>
    public FormationBoard SwapSlots(SlotCoordinate first, SlotCoordinate second)
    {
        int firstIndex = IndexOf(first);
        int secondIndex = IndexOf(second);
        if (firstIndex == secondIndex) return this;

        var firstSlot = Slots[firstIndex];
        var secondSlot = Slots[secondIndex];

        var swapped = Slots
            .SetItem(firstIndex, firstSlot with { OccupantId = secondSlot.OccupantId })
            .SetItem(secondIndex, secondSlot with { OccupantId = firstSlot.OccupantId });

        return this with { Slots = swapped };
    }

    /// <summary>
    /// 分隊（行）単位で占有者の三つ組をローテーションした新しい盤面を返す。
    /// 列順 (0/1/2) は完全保持する。
    /// </summary>
    public FormationBoard Rotated(RotationDirection direction)
    {
        var builder = ImmutableArray.CreateBuilder<FormationSlot>(SlotCount);
        foreach (var row in RowOrder)
        {
            var source = SourceRow(row, direction);
            for (int column = 0; column < ColumnsPerRow; column++)
            {
                var occupant = OccupantAt(new SlotCoordinate(source, column));
                builder.Add(new FormationSlot
                {
                    Coordinate = new SlotCoordinate(row, column),
                    OccupantId = occupant,
                });
            }
        }
        return this with { Slots = builder.MoveToImmutable() };
    }

    /// <summary>
    /// 回転後の <paramref name="target"/> 行に入るべき占有者群の供給元（回転前の行）を返す。
    /// 時計回り:     Front←RearLeft, RearLeft←RearRight, RearRight←Front
    /// 反時計回り:   Front←RearRight, RearLeft←Front, RearRight←RearLeft
    /// </summary>
    private static SquadRow SourceRow(SquadRow target, RotationDirection direction) => direction switch
    {
        RotationDirection.Clockwise => target switch
        {
            SquadRow.Front     => SquadRow.RearLeft,
            SquadRow.RearLeft  => SquadRow.RearRight,
            SquadRow.RearRight => SquadRow.Front,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target), target, "未知の SquadRow です。"),
        },
        RotationDirection.CounterClockwise => target switch
        {
            SquadRow.Front     => SquadRow.RearRight,
            SquadRow.RearLeft  => SquadRow.Front,
            SquadRow.RearRight => SquadRow.RearLeft,
            _ => throw new ArgumentOutOfRangeException(
                nameof(target), target, "未知の SquadRow です。"),
        },
        _ => throw new ArgumentOutOfRangeException(
            nameof(direction), direction, "未知の RotationDirection です。"),
    };
}
