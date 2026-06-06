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
import { UnitDetailModal } from "../../components/UnitDetailModal";
import { UnitIcon } from "../../components/UnitIcon";

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
  /** モーダルで詳細表示中のユニット ID */
  const [detailUnitId, setDetailUnitId] = useState<string | null>(null);
  // ベンチのソート・フィルタ状態
  const [sort, setSort] = useState<SortKey>("totalDesc");
  const [jobFilter, setJobFilter] = useState<JobFilter>("all");

  useEffect(() => {
    (async () => {
      setLoading(true);
      const r = await api.getRoster();
      setRoster(r);
      setGrid(EMPTY_GRID);
      setDetailUnitId(null);
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

  /** ベンチクリック: 詳細モーダルを開く */
  const onBenchClick = (unitId: string) => {
    setDetailUnitId(unitId);
  };

  /** 詳細モーダル内の「配置先指定」ボタンクリック */
  const onAssign = (row: SquadRow, col: number) => {
    if (!detailUnitId) return;
    setGrid((prev) =>
      prev.map((c) => {
        // 元の位置を空ける（押し戻し）
        if (c.unitId === detailUnitId) return { ...c, unitId: null };
        // 目的マスに配置
        if (c.row === row && c.col === col) return { ...c, unitId: detailUnitId };
        return c;
      })
    );
    setDetailUnitId(null); // 配置後はモーダルを閉じる
  };

  /**
   * 3×3 グリッドのマスクリック: そのマスが埋まっていれば配置解除のみ。
   * 新規配置は詳細モーダル経由でのみ可能。
   */
  const onCellClick = (row: SquadRow, col: number) => {
    setGrid((prev) =>
      prev.map((c) =>
        c.row === row && c.col === col && c.unitId ? { ...c, unitId: null } : c
      )
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
  const availableAll = roster.units.filter((u) => !u.isRetired);
  const visibleBench = applyRosterControls(availableAll, sort, jobFilter);
  const detailUnit = detailUnitId ? unitsById.get(detailUnitId) ?? null : null;

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

      <p data-testid="formation-instruction-banner" className="formation-instruction-banner">
        💡 ベンチのユニットをクリックして詳細を確認・配置を指定してください。
        配置済みのマスをクリックするとそのユニットがベンチに戻ります。
      </p>

      {/* ─── V字（くさび型）フォーメーション ────────────────────────────── */}
      <div
        data-testid="formation-v-shape-root"
        className="formation-v-shape"
      >
        {ROWS.map((row) => {
          const squadModifier =
            row === "FRONT" ? "front"
            : row === "REAR-L" ? "rear-l"
            : "rear-r";
          return (
            <div
              key={row}
              data-testid={`formation-v-squad-${row}`}
              data-row={row}
              className={`formation-v-squad formation-v-squad-${squadModifier}`}
            >
              <div
                data-testid={`formation-v-squad-label-${row}`}
                className="formation-v-squad-header"
              >
                {row === "FRONT" ? "⚔ " : "🛡 "}
                {ROW_LABEL[row]}
              </div>
              <div
                data-testid={`formation-v-slot-row-${row}`}
                className="formation-v-slot-row"
              >
                {COLS.map((col) => {
                  const cell = grid.find((c) => c.row === row && c.col === col)!;
                  const currentUnit = cell.unitId ? unitsById.get(cell.unitId) ?? null : null;
                  const heart = currentUnit ? heartMap.get(currentUnit.id) : null;
                  return (
                    <div
                      key={col}
                      data-testid={`formation-target-slot-${row}-${col}`}
                      data-filled={!!currentUnit}
                      onClick={() => onCellClick(row, col)}
                      className="formation-v-slot formation-target-slot"
                      role="button"
                      tabIndex={0}
                    >
                      {currentUnit ? (
                        <div
                          data-testid={`formation-cell-unit-${row}-${col}`}
                          className="formation-cell-unit"
                        >
                          <span
                            data-testid={`formation-cell-icon-slot-${row}-${col}`}
                            className="unit-icon-slot unit-icon-slot-md"
                          >
                            <UnitIcon
                              jobId={currentUnit.job}
                              gender={currentUnit.gender}
                              altName={currentUnit.name}
                              testIdSuffix={`formation-cell-${row}-${col}`}
                            />
                          </span>
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
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>

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
            return (
              <li
                key={u.id}
                data-testid={`formation-bench-card-${u.id}`}
                data-placed={isPlaced}
                data-married={u.isMarried}
                onClick={() => onBenchClick(u.id)}
                className={`formation-roster-card formation-bench-card ${isPlaced ? "placed" : ""}`}
                role="button"
                tabIndex={0}
              >
                <span
                  data-testid={`formation-bench-icon-slot-${u.id}`}
                  className="unit-icon-slot unit-icon-slot-sm"
                >
                  <UnitIcon
                    jobId={u.job}
                    gender={u.gender}
                    altName={u.name}
                    testIdSuffix={`bench-${u.id}`}
                  />
                </span>
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
              </li>
            );
          })}
        </ul>
      </div>

      <p data-testid="formation-hint" className="phase-hint">
        ベンチをクリックすると詳細パネルが開きます。そこから配置先のマスを指定してください。
      </p>

      {/* ─── 詳細モーダル ──────────────────────────── */}
      {detailUnit && (
        <UnitDetailModal
          unit={detailUnit}
          grid={grid}
          unitsById={unitsById}
          onAssign={onAssign}
          onClose={() => setDetailUnitId(null)}
        />
      )}
    </section>
  );
}
