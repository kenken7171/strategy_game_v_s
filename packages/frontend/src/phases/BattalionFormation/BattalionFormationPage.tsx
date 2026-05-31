/**
 * BattalionFormationPage — フェーズ3: 編成（クリック選択方式）
 *
 * UI 仕様（M3 拡張）:
 *   ① ベンチのユニットをクリック → アクティブ状態（ハイライト）
 *   ② アクティブパネルに詳細表示（HP/ATK/SPD/総合/血統情報）
 *   ③ 3×3 マスをクリック → アクティブユニットを配置
 *      - 既存ユニットがいた場合は自動でベンチに戻る（入れ替え）
 *   ④ 配置済みマスをアクティブなしでクリック → 配置解除
 *   ⑤ ベンチには SortFilter（総合/年齢/HP/ATK/SPD/子孫数/ジョブ絞込）
 *
 * プルダウン方式（Select）は完全廃止。
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
import {
  RosterControls,
  applyRosterControls,
  type SortKey,
  type JobFilter,
} from "../../components/RosterControls";

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

// ─── 好感度ハート ──────────────────────────────────────────────

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

// ─── メインコンポーネント ──────────────────────────────────────

export function BattalionFormationPage({ year, phaseHandle }: Props) {
  const [roster, setRoster] = useState<FormationRosterResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [grid, setGrid] = useState<GridCell[]>(EMPTY_GRID);
  const [activeUnitId, setActiveUnitId] = useState<string | null>(null);
  // ベンチのソート・フィルタ状態
  const [sort, setSort] = useState<SortKey>("totalDesc");
  const [jobFilter, setJobFilter] = useState<JobFilter>("all");

  useEffect(() => {
    (async () => {
      setLoading(true);
      const r = await api.getRoster();
      setRoster(r);
      setGrid(EMPTY_GRID);
      setActiveUnitId(null);
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

  // ─── 操作ハンドラ ─────────────────────────────────────

  /** ベンチクリック: 同じユニットなら解除、別なら active 切替 */
  const onBenchClick = (unitId: string) => {
    setActiveUnitId((prev) => (prev === unitId ? null : unitId));
  };

  /**
   * マスクリック:
   *   - active がある → 配置（既存があれば押し戻し）+ active 解除
   *   - active なし → 配置済みなら解除（ベンチに戻す）
   */
  const onCellClick = (row: SquadRow, col: number) => {
    setGrid((prev) => {
      const cell = prev.find((c) => c.row === row && c.col === col)!;
      if (activeUnitId) {
        // active を配置する: 元の active がいた別マスも空ける（自動入れ替え）
        return prev.map((c) => {
          if (c.unitId === activeUnitId) return { ...c, unitId: null };
          if (c.row === row && c.col === col) return { ...c, unitId: activeUnitId };
          return c;
        });
      }
      // active なし: このマスを空ける
      if (cell.unitId) {
        return prev.map((c) =>
          c.row === row && c.col === col ? { ...c, unitId: null } : c
        );
      }
      return prev;
    });
    if (activeUnitId) setActiveUnitId(null);
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
  const availableAll = roster.units.filter((u) => !u.isRetired);
  const visibleBench = applyRosterControls(availableAll, sort, jobFilter);
  const activeUnit = activeUnitId ? unitsById.get(activeUnitId) ?? null : null;

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

      {/* ─── アクティブパネル ────────────────────────── */}
      <div
        data-testid="formation-active-panel-root"
        className={activeUnit ? "formation-active-panel active" : "formation-active-panel"}
        data-active={!!activeUnit}
      >
        {activeUnit ? (
          <>
            <strong data-testid="formation-active-panel-hint" className="formation-active-panel-hint">
              ▶ マスをクリックして配置（もう一度ベンチを押すと解除）
            </strong>
            <div data-testid="formation-active-panel-content" className="formation-active-panel-content">
              <span data-testid="formation-active-panel-job" className="formation-active-panel-job">
                [{formatJob(activeUnit.job)}]
              </span>
              <span data-testid="formation-active-panel-name" className="formation-active-panel-name">
                {activeUnit.name}
              </span>
              <span data-testid="formation-active-panel-gender">
                {activeUnit.gender === "Male" ? "♂" : "♀"}
              </span>
              <span data-testid="formation-active-panel-age">{activeUnit.age}歳</span>
              <span data-testid="formation-active-panel-stats" className="unit-stats-inline">
                HP <b data-testid="formation-active-panel-hp">{activeUnit.maxHp}</b> /
                ATK <b data-testid="formation-active-panel-atk">{activeUnit.attack}</b> /
                SPD <b data-testid="formation-active-panel-spd">{activeUnit.speed}</b>
              </span>
              <span data-testid="formation-active-panel-total" className="unit-total-rating">
                総合 {activeUnit.totalRating}
              </span>
              {activeUnit.isMarried && (
                <span data-testid="formation-active-panel-married">💍 既婚</span>
              )}
              {activeUnit.parents && (
                <span data-testid="formation-active-panel-heir">🩸 継承者</span>
              )}
              {activeUnit.descendantCount > 0 && (
                <span data-testid="formation-active-panel-descendants">
                  👶 子孫 {activeUnit.descendantCount}
                </span>
              )}
            </div>
          </>
        ) : (
          <span data-testid="formation-active-panel-placeholder" className="formation-active-panel-placeholder">
            ▷ ベンチからユニットを選択してください（クリックでアクティブ）
          </span>
        )}
      </div>

      {/* ─── 3×3 グリッド ────────────────────────────── */}
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
                const isAcceptable = !!activeUnit; // active がいれば、どのマスも配置可能（既存は押し戻し）
                return (
                  <td
                    key={col}
                    data-testid={`formation-target-slot-${row}-${col}`}
                    data-filled={!!currentUnit}
                    data-acceptable={isAcceptable}
                    onClick={() => onCellClick(row, col)}
                    className={`formation-grid-cell formation-target-slot ${isAcceptable ? "acceptable" : ""}`}
                    role="button"
                    tabIndex={0}
                  >
                    {currentUnit ? (
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
                          HP{currentUnit.maxHp}/ATK{currentUnit.attack}/SPD{currentUnit.speed}
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
                    ) : (
                      <span
                        data-testid={`formation-cell-empty-${row}-${col}`}
                        className="formation-cell-empty"
                      >
                        + 空き
                      </span>
                    )}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>

      {/* ─── ベンチ（ソート・フィルタ付き） ───────────── */}
      <div data-testid="formation-roster-section" className="formation-roster-section">
        <h3 data-testid="formation-roster-title">
          ベンチ：旅団全員
        </h3>
        <RosterControls
          sort={sort}
          jobFilter={jobFilter}
          onSortChange={setSort}
          onJobFilterChange={setJobFilter}
          visibleCount={visibleBench.length}
          totalCount={availableAll.length}
        />
        <ul data-testid="formation-roster-list" className="formation-roster-list">
          {visibleBench.map((u) => {
            const isPlaced = placedIds.has(u.id);
            const isActive = activeUnitId === u.id;
            return (
              <li
                key={u.id}
                data-testid={`formation-bench-card-${u.id}`}
                data-placed={isPlaced}
                data-married={u.isMarried}
                data-active={isActive}
                onClick={() => onBenchClick(u.id)}
                className={`formation-roster-card formation-bench-card ${isActive ? "active" : ""} ${isPlaced ? "placed" : ""}`}
                role="button"
                tabIndex={0}
              >
                <span data-testid={`formation-bench-job-${u.id}`} className="formation-roster-job">
                  [{formatJob(u.job)}]
                </span>
                <span data-testid={`formation-bench-name-${u.id}`} className="formation-roster-name">
                  {u.name}
                </span>
                <span data-testid={`formation-bench-gender-${u.id}`}>
                  {u.gender === "Male" ? "♂" : "♀"}
                </span>
                <span data-testid={`formation-bench-age-${u.id}`}>{u.age}歳</span>
                <span data-testid={`formation-bench-stats-${u.id}`} className="unit-stats-inline">
                  HP <b data-testid={`formation-bench-hp-${u.id}`}>{u.maxHp}</b> /
                  ATK <b data-testid={`formation-bench-atk-${u.id}`}>{u.attack}</b> /
                  SPD <b data-testid={`formation-bench-spd-${u.id}`}>{u.speed}</b>
                </span>
                <span
                  data-testid={`formation-bench-total-${u.id}`}
                  className="unit-total-rating"
                  title="総合的な強さ"
                >
                  総合 {u.totalRating}
                </span>
                {u.isMarried && (
                  <span data-testid={`formation-bench-married-${u.id}`}>💍</span>
                )}
                {u.parents && (
                  <span data-testid={`formation-bench-heir-${u.id}`}>🩸</span>
                )}
                {u.descendantCount > 0 && (
                  <span data-testid={`formation-bench-descendants-${u.id}`}>
                    👶{u.descendantCount}
                  </span>
                )}
                {isPlaced && (
                  <span
                    data-testid={`formation-bench-placed-badge-${u.id}`}
                    className="formation-roster-placed-badge"
                  >
                    出撃予定
                  </span>
                )}
                {isActive && (
                  <span
                    data-testid={`formation-bench-active-badge-${u.id}`}
                    className="formation-bench-active-badge"
                  >
                    ▶ 選択中
                  </span>
                )}
              </li>
            );
          })}
        </ul>
      </div>

      <p data-testid="formation-hint" className="phase-hint">
        ベンチからユニットを選択 → 3×3 のマスをクリックして配置。同分隊の男女ペアにハート ❤️ が灯ります。
      </p>
    </section>
  );
}
