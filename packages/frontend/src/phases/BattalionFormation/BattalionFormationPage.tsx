/**
 * BattalionFormationPage — フェーズ3: 編成（プルダウン版）
 *
 * UI 刷新 (M3):
 *   - 3×3 マスを <select> プルダウン化
 *   - Option 表記: 「[日本語ジョブ] 名前 (年齢) - HP/ATK/SPD [総合: XX]」
 *   - 他マスに配置済みのユニットは Option から除外（重複ガード）
 *   - ジョブ名は formatJob で日本語化
 *   - ベンチ表示も総合的な強さでソート
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
import { formatJob } from "../../utils/job";

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
const COLS = [0, 1, 2] as const;

interface GridCell {
  row: SquadRow;
  col: number;
  unitId: string | null;
}

const EMPTY_GRID: GridCell[] = ROWS.flatMap((row) =>
  COLS.map((col) => ({ row, col, unitId: null as string | null }))
);

// 好感度 → ハートアイコン
function affinityHeart(value: number): string {
  if (value >= 100) return "💖";
  if (value >= 70) return "❤️";
  if (value >= 35) return "🩷";
  if (value > 0) return "💗";
  return "";
}

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
        const married = a.spouseId === b.id && b.spouseId === a.id;
        const affAB = affinityMap[a.id]?.[b.id] ?? 0;
        const affBA = affinityMap[b.id]?.[a.id] ?? 0;
        const aff = Math.min(affAB, affBA);
        if (aff === 0 && !married) continue;
        const heart = married ? "❤️‍🔥" : affinityHeart(aff);
        if (!heart) continue;
        for (const [self, partner] of [[a, b], [b, a]] as const) {
          const prev = result.get(self.id);
          if (!prev || prev.affinity < aff) {
            result.set(self.id, { partnerName: partner.name, affinity: aff, heart, married });
          }
        }
      }
    }
  }
  return result;
}

/** Option ラベル: 「[ジョブ] 名前 (年齢) - HP/ATK/SPD [総合: XX]」 */
function formatOptionLabel(u: RosterUnit): string {
  return `[${formatJob(u.job)}] ${u.name} (${u.age}) - HP${u.maxHp}/ATK${u.attack}/SPD${u.speed} [総合: ${u.totalRating}]`;
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

  const heartMap = useMemo(() => {
    if (!roster) return new Map();
    return detectSquadmateHearts(grid, roster.units, roster.affinityMap);
  }, [grid, roster]);

  useEffect(() => {
    if (canProceed) {
      const placements: BattlePlacement[] = grid
        .filter((c) => c.unitId !== null)
        .map((c) => ({ row: c.row, col: c.col, unitId: c.unitId! }));
      sessionStorage.setItem("formation:placements", JSON.stringify(placements));
    }
  }, [canProceed, grid]);

  const handleSelectChange = (row: SquadRow, col: number, value: string) => {
    const unitId = value === "" ? null : value;
    setGrid((prev) =>
      prev.map((c) => (c.row === row && c.col === col ? { ...c, unitId } : c))
    );
  };

  if (loading || !roster) {
    return (
      <section data-testid="battalion-formation-page-root" className="battalion-formation-page">
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 旅団情報を読み込み中...
        </div>
      </section>
    );
  }

  const unitsById = new Map(roster.units.map((u) => [u.id, u]));
  // 編成候補（未引退）を totalRating 降順でソート
  const available = roster.units
    .filter((u) => !u.isRetired)
    .sort((a, b) => b.totalRating - a.totalRating);

  return (
    <section data-testid="battalion-formation-page-root" className="battalion-formation-page">
      <h2 data-testid="battalion-formation-page-title">編成フェーズ — Year {year}</h2>

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
              {COLS.map((col) => {
                const cell = grid.find((c) => c.row === row && c.col === col)!;
                const currentUnit = cell.unitId ? unitsById.get(cell.unitId) ?? null : null;
                const heart = currentUnit ? heartMap.get(currentUnit.id) : null;
                // このセルで選択可能な候補 = 未配置 OR このセル自身に配置中
                const optionable = available.filter(
                  (u) => !placedIds.has(u.id) || u.id === cell.unitId
                );
                return (
                  <td
                    key={col}
                    data-testid={`formation-grid-cell-${row}-${col}`}
                    data-filled={!!currentUnit}
                    className="formation-grid-cell"
                  >
                    <select
                      data-testid={`formation-select-cell-${row}-${col}`}
                      value={cell.unitId ?? ""}
                      onChange={(e) => handleSelectChange(row, col, e.target.value)}
                      className="formation-cell-select"
                    >
                      <option
                        data-testid={`formation-select-option-empty-${row}-${col}`}
                        value=""
                      >
                        ─ 空きスロット ─
                      </option>
                      {optionable.map((u) => (
                        <option
                          key={u.id}
                          data-testid={`formation-select-option-${row}-${col}-${u.id}`}
                          value={u.id}
                        >
                          {formatOptionLabel(u)}
                        </option>
                      ))}
                    </select>
                    {currentUnit && (
                      <div
                        data-testid={`formation-cell-unit-${row}-${col}`}
                        className="formation-cell-unit"
                      >
                        <span
                          data-testid={`formation-cell-unit-name-${row}-${col}`}
                          className="formation-cell-unit-name"
                        >
                          {currentUnit.name}
                        </span>
                        <span
                          data-testid={`formation-cell-unit-job-${row}-${col}`}
                          className="formation-cell-unit-job"
                        >
                          [{formatJob(currentUnit.job)}]
                        </span>
                        <span
                          data-testid={`formation-cell-unit-stats-${row}-${col}`}
                          className="formation-cell-unit-stats"
                        >
                          HP{currentUnit.maxHp} / ATK{currentUnit.attack} / SPD{currentUnit.speed}
                        </span>
                        <span
                          data-testid={`formation-cell-unit-total-${row}-${col}`}
                          className="formation-cell-unit-total"
                        >
                          総合 {currentUnit.totalRating}
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
                      </div>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>

      <div data-testid="formation-roster-section" className="formation-roster-section">
        <h3 data-testid="formation-roster-title">
          ベンチ：旅団全員（{available.length} 名 / 総合的な強さ降順）
        </h3>
        <ul data-testid="formation-roster-list" className="formation-roster-list">
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
                <span data-testid={`formation-roster-job-${u.id}`} className="formation-roster-job">
                  [{formatJob(u.job)}]
                </span>
                <span data-testid={`formation-roster-name-${u.id}`} className="formation-roster-name">
                  {u.name}
                </span>
                <span data-testid={`formation-roster-gender-${u.id}`}>
                  {u.gender === "Male" ? "♂" : "♀"}
                </span>
                <span data-testid={`formation-roster-age-${u.id}`}>{u.age}歳</span>
                <span data-testid={`formation-roster-stats-${u.id}`} className="unit-stats-inline">
                  HP <b data-testid={`formation-roster-hp-${u.id}`}>{u.maxHp}</b> /
                  ATK <b data-testid={`formation-roster-atk-${u.id}`}>{u.attack}</b> /
                  SPD <b data-testid={`formation-roster-spd-${u.id}`}>{u.speed}</b>
                </span>
                <span
                  data-testid={`formation-roster-total-${u.id}`}
                  className="unit-total-rating"
                  title="総合的な強さ（HP/5 + ATK + SPD）"
                >
                  総合 {u.totalRating}
                </span>
                {u.isMarried && (
                  <span data-testid={`formation-roster-married-badge-${u.id}`}>💍</span>
                )}
                {u.parents && (
                  <span data-testid={`formation-roster-heir-badge-${u.id}`}>🩸</span>
                )}
                {isPlaced && (
                  <span data-testid={`formation-roster-placed-badge-${u.id}`} className="formation-roster-placed-badge">
                    出撃予定
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      </div>

      <p data-testid="formation-hint" className="phase-hint">
        各マスのプルダウンから9名を編成してください。同分隊の男女ペアにハート ❤️ が灯ります。
      </p>
    </section>
  );
}
