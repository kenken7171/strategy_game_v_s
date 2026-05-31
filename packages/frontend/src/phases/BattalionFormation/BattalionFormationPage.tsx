/**
 * BattalionFormationPage — フェーズ3: 編成
 *
 * API:
 *   GET /api/formation/roster   旅団全員 + 好感度マップ
 *
 * 完了条件: 9マスすべて埋まる
 * UI拡張: 同分隊の未婚男女ペアに好感度に応じてハートマーク表示
 */
import { useEffect, useMemo, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type {
  FormationRosterResponse,
  RosterUnit,
  BattlePlacement,
  SquadRow,
} from "../../api/types";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

const ROWS: SquadRow[] = ["FRONT", "REAR-L", "REAR-R"];
const ROW_LABEL: Record<SquadRow, string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

interface GridCell {
  row: SquadRow;
  col: number;
  unitId: string | null;
}

const EMPTY_GRID: GridCell[] = ROWS.flatMap((row) =>
  [0, 1, 2].map((col) => ({ row, col, unitId: null as string | null }))
);

// 好感度 → ハートアイコン
function affinityHeart(value: number): string {
  if (value >= 100) return "💖"; // 結婚閾値
  if (value >= 70) return "❤️";
  if (value >= 35) return "🩷";
  if (value > 0) return "💗";
  return "";
}

// 同分隊の未婚男女ペアを抽出して、最大好感度のハートを返す
function detectSquadmateHearts(
  grid: GridCell[],
  units: RosterUnit[],
  affinityMap: Record<string, Record<string, number>>
): Map<string, { partnerName: string; affinity: number; heart: string; married: boolean }> {
  const result = new Map<string, { partnerName: string; affinity: number; heart: string; married: boolean }>();
  const unitById = new Map(units.map((u) => [u.id, u]));

  for (const row of ROWS) {
    const cells = grid.filter((c) => c.row === row && c.unitId !== null);
    for (let i = 0; i < cells.length; i++) {
      for (let j = i + 1; j < cells.length; j++) {
        const a = unitById.get(cells[i].unitId!);
        const b = unitById.get(cells[j].unitId!);
        if (!a || !b) continue;
        if (a.gender === b.gender) continue;
        // 既に既婚（互いに spouseId）かチェック
        const married = a.spouseId === b.id && b.spouseId === a.id;
        const affAB = affinityMap[a.id]?.[b.id] ?? 0;
        const affBA = affinityMap[b.id]?.[a.id] ?? 0;
        const aff = Math.min(affAB, affBA); // 互いに必要なので min
        if (aff === 0 && !married) continue;
        const heart = married ? "❤️‍🔥" : affinityHeart(aff);
        if (!heart) continue;
        // 双方の cell に対して登録（より高い好感度で上書き）
        for (const [self, partner] of [[a, b], [b, a]] as const) {
          const prev = result.get(self.id);
          if (!prev || prev.affinity < aff) {
            result.set(self.id, {
              partnerName: partner.name,
              affinity: aff,
              heart,
              married,
            });
          }
        }
      }
    }
  }
  return result;
}

export function BattalionFormationPage({ year, phaseHandle }: Props) {
  const [roster, setRoster] = useState<FormationRosterResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [grid, setGrid] = useState<GridCell[]>(EMPTY_GRID);

  useEffect(() => {
    (async () => {
      setLoading(true);
      const r = await api.getRoster();
      setRoster(r);
      // 旅団が変わったらグリッドリセット
      setGrid(EMPTY_GRID);
      setLoading(false);
    })();
  }, [year]);

  const placedIds = useMemo(
    () => new Set(grid.map((c) => c.unitId).filter((id): id is string => id !== null)),
    [grid]
  );

  const filledCount = useMemo(
    () => grid.filter((c) => c.unitId !== null).length,
    [grid]
  );
  const canProceed = filledCount === 9;

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  // 好感度ハート判定
  const heartMap = useMemo(() => {
    if (!roster) return new Map();
    return detectSquadmateHearts(grid, roster.units, roster.affinityMap);
  }, [grid, roster]);

  // BattalionFormationPage 内部で配置中の placements を保持し、battle.run に送る
  useEffect(() => {
    if (canProceed) {
      const placements: BattlePlacement[] = grid
        .filter((c) => c.unitId !== null)
        .map((c) => ({ row: c.row, col: c.col, unitId: c.unitId! }));
      // セッションストレージに格納（BattleSimulationPage が拾う）
      sessionStorage.setItem("formation:placements", JSON.stringify(placements));
    }
  }, [canProceed, grid]);

  const placeUnit = (row: SquadRow, col: number, unitId: string) => {
    setGrid((prev) =>
      prev.map((c) => (c.row === row && c.col === col ? { ...c, unitId } : c))
    );
  };
  const clearCell = (row: SquadRow, col: number) => {
    setGrid((prev) =>
      prev.map((c) => (c.row === row && c.col === col ? { ...c, unitId: null } : c))
    );
  };

  if (loading || !roster) {
    return (
      <section
        data-testid="battalion-formation-page-root"
        className="battalion-formation-page"
      >
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 旅団情報を読み込み中...
        </div>
      </section>
    );
  }

  const unitsById = new Map(roster.units.map((u) => [u.id, u]));
  // 引退済み以外を編成候補に
  const available = roster.units.filter((u) => !u.isRetired);

  return (
    <section
      data-testid="battalion-formation-page-root"
      className="battalion-formation-page"
    >
      <h2 data-testid="battalion-formation-page-title">
        編成フェーズ — Year {year}
      </h2>

      <div
        data-testid="formation-progress"
        className="formation-progress"
        data-filled={filledCount}
      >
        配置済み: {filledCount} / 9 名（旅団 {roster.units.length} 名から選出）
      </div>

      <table data-testid="formation-grid-root" className="formation-grid">
        <thead data-testid="formation-grid-header">
          <tr>
            <th data-testid="formation-grid-header-row-label">分隊</th>
            <th data-testid="formation-grid-header-col-0">スロット1</th>
            <th data-testid="formation-grid-header-col-1">スロット2</th>
            <th data-testid="formation-grid-header-col-2">スロット3</th>
          </tr>
        </thead>
        <tbody data-testid="formation-grid-body">
          {ROWS.map((row) => (
            <tr
              key={row}
              data-testid={`formation-grid-row-${row}`}
              className="formation-grid-row"
            >
              <th
                data-testid={`formation-grid-row-label-${row}`}
                className="formation-grid-row-label"
              >
                {ROW_LABEL[row]}
              </th>
              {[0, 1, 2].map((col) => {
                const cell = grid.find((c) => c.row === row && c.col === col)!;
                const unit = cell.unitId ? unitsById.get(cell.unitId) : null;
                const heart = unit ? heartMap.get(unit.id) : null;
                return (
                  <td
                    key={col}
                    data-testid={`formation-grid-cell-${row}-${col}`}
                    data-filled={!!unit}
                    className="formation-grid-cell"
                  >
                    {unit ? (
                      <div
                        data-testid={`formation-cell-unit-${row}-${col}`}
                        className="formation-cell-unit"
                      >
                        <span data-testid={`formation-cell-unit-name-${row}-${col}`}>
                          {unit.name}
                        </span>
                        <span data-testid={`formation-cell-unit-job-${row}-${col}`}>
                          [{unit.job ?? "?"}]
                        </span>
                        <span data-testid={`formation-cell-unit-strength-${row}-${col}`}>
                          STR {unit.strength}
                        </span>
                        {heart && (
                          <span
                            data-testid={`formation-cell-heart-${row}-${col}`}
                            className="formation-cell-heart"
                            title={`${heart.partnerName} と好感度 ${heart.affinity}${heart.married ? "（既婚）" : ""}`}
                          >
                            {heart.heart}
                          </span>
                        )}
                        <button
                          type="button"
                          data-testid={`formation-cell-remove-button-${row}-${col}`}
                          onClick={() => clearCell(row, col)}
                          className="formation-cell-remove-button"
                        >
                          ✕
                        </button>
                      </div>
                    ) : (
                      <span
                        data-testid={`formation-cell-empty-${row}-${col}`}
                        className="formation-cell-empty"
                      >
                        空き
                      </span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>

      <div
        data-testid="formation-roster-section"
        className="formation-roster-section"
      >
        <h3 data-testid="formation-roster-title">
          出撃可能ユニット（{available.length} 名）
        </h3>
        <ul
          data-testid="formation-roster-list"
          className="formation-roster-list"
        >
          {available.map((u) => {
            const isPlaced = placedIds.has(u.id);
            return (
              <li
                key={u.id}
                data-testid={`formation-roster-card-${u.id}`}
                data-placed={isPlaced}
                data-married={u.isMarried}
                className="formation-roster-card"
              >
                <span data-testid={`formation-roster-name-${u.id}`}>
                  {u.name}
                </span>
                <span data-testid={`formation-roster-job-${u.id}`}>
                  [{u.job ?? "?"}]
                </span>
                <span data-testid={`formation-roster-gender-${u.id}`}>
                  {u.gender === "Male" ? "♂" : "♀"}
                </span>
                <span data-testid={`formation-roster-age-${u.id}`}>
                  {u.age}歳
                </span>
                <span data-testid={`formation-roster-strength-${u.id}`}>
                  STR {u.strength}
                </span>
                {u.isMarried && (
                  <span data-testid={`formation-roster-married-badge-${u.id}`}>
                    💍
                  </span>
                )}
                {u.parents && (
                  <span data-testid={`formation-roster-heir-badge-${u.id}`}>
                    🩸
                  </span>
                )}
                {!isPlaced && (
                  <div
                    data-testid={`formation-roster-place-targets-${u.id}`}
                    className="formation-roster-place-targets"
                  >
                    {ROWS.map((row) =>
                      [0, 1, 2].map((col) => {
                        const filled = grid.find(
                          (c) => c.row === row && c.col === col
                        )?.unitId;
                        if (filled) return null;
                        return (
                          <button
                            key={`${row}-${col}`}
                            type="button"
                            data-testid={`formation-place-button-${u.id}-${row}-${col}`}
                            onClick={() => placeUnit(row, col, u.id)}
                            className="formation-place-button"
                          >
                            → {ROW_LABEL[row]}({col + 1})
                          </button>
                        );
                      })
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      </div>

      <p data-testid="formation-hint" className="phase-hint">
        9マスを埋めると「次へ：戦闘」へ進めます。同分隊の男女ペアにハート ❤️ が灯ります。
      </p>
    </section>
  );
}
