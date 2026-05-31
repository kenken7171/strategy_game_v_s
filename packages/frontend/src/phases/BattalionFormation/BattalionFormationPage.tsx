/**
 * BattalionFormationPage — フェーズ3: 編成（3×3 グリッド）
 *
 * canProceed の条件:
 *   - 9マス全てが埋まっていること（M3 でドラッグ&ドロップ実装、現状はモック）
 *
 * グリッド構成（instructions.md 絶対ルール: 3×3 構造）:
 *   FRONT  : col 0-2 (front 3 slots)
 *   REAR-L : col 0-2 (rear-left 3 slots)
 *   REAR-R : col 0-2 (rear-right 3 slots)
 */
import { useEffect, useMemo, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

type SquadRow = "FRONT" | "REAR-L" | "REAR-R";
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

const MOCK_AVAILABLE_UNITS = [
  { id: "u1", name: "Arthur",   job: "iron_wall_knight", strength: 95 },
  { id: "u2", name: "Elise",    job: "medic",            strength: 82 },
  { id: "u3", name: "Roland",   job: "sniper",           strength: 88 },
  { id: "u4", name: "Brigitte", job: "tactician",        strength: 76 },
  { id: "u5", name: "Ivan",     job: "heavy_infantry",   strength: 92 },
  { id: "u6", name: "Selene",   job: "sorcerer",         strength: 71 },
  { id: "u7", name: "Wulfric",  job: "scout",            strength: 80 },
  { id: "u8", name: "Heir-A",   job: "standard_bearer",  strength: 68 },
  { id: "u9", name: "Heir-B",   job: "sniper",           strength: 84 },
];

export function BattalionFormationPage({ year, phaseHandle }: Props) {
  const [grid, setGrid] = useState<GridCell[]>(EMPTY_GRID);

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

  const placeUnit = (row: SquadRow, col: number, unitId: string) => {
    setGrid((prev) =>
      prev.map((c) =>
        c.row === row && c.col === col ? { ...c, unitId } : c
      )
    );
  };

  const clearCell = (row: SquadRow, col: number) => {
    setGrid((prev) =>
      prev.map((c) =>
        c.row === row && c.col === col ? { ...c, unitId: null } : c
      )
    );
  };

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
        配置済み: {filledCount} / 9 名
      </div>

      <table
        data-testid="formation-grid-root"
        className="formation-grid"
      >
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
                const cell = grid.find(
                  (c) => c.row === row && c.col === col
                )!;
                const unit = cell.unitId
                  ? MOCK_AVAILABLE_UNITS.find((u) => u.id === cell.unitId)
                  : null;
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
                          [{unit.job}]
                        </span>
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
        <h3 data-testid="formation-roster-title">出撃可能ユニット</h3>
        <ul
          data-testid="formation-roster-list"
          className="formation-roster-list"
        >
          {MOCK_AVAILABLE_UNITS.map((u) => {
            const isPlaced = placedIds.has(u.id);
            return (
              <li
                key={u.id}
                data-testid={`formation-roster-card-${u.id}`}
                data-placed={isPlaced}
                className="formation-roster-card"
              >
                <span data-testid={`formation-roster-name-${u.id}`}>
                  {u.name}
                </span>
                <span data-testid={`formation-roster-job-${u.id}`}>
                  [{u.job}]
                </span>
                <span data-testid={`formation-roster-strength-${u.id}`}>
                  STR {u.strength}
                </span>
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
                            → {ROW_LABEL[row]} ({col + 1})
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
        9マス全てを埋めると「次へ：戦闘」へ進めます。
      </p>
    </section>
  );
}
