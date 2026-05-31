/**
 * RosterControls — ベンチ/候補リストの並び替え・フィルタリング UI
 *
 * 人事画面・編成画面の両方で共通利用される。
 * onChange でソート/フィルタ条件を通知し、親側で配列を加工する。
 *
 * data-testid:
 *   - roster-controls-root
 *   - roster-sort-select
 *   - roster-filter-job-select
 */
import type { JSX } from "react";
import { JOB_JP_ENTRIES } from "../utils/job";

export type SortKey =
  | "totalDesc"
  | "totalAsc"
  | "ageAsc"
  | "ageDesc"
  | "hpDesc"
  | "atkDesc"
  | "spdDesc"
  | "descendantsDesc";

export type JobFilter = "all" | string;

export interface SortableUnit {
  id: string;
  job: string | null;
  age: number;
  maxHp: number;
  attack: number;
  speed: number;
  totalRating: number;
  descendantCount?: number;
  hasLineage?: boolean;
}

/** 与えられたユニットリストを sort/filter で加工して返す（純関数） */
export function applyRosterControls<T extends SortableUnit>(
  units: ReadonlyArray<T>,
  sort: SortKey,
  jobFilter: JobFilter
): T[] {
  // フィルタ
  let arr = jobFilter === "all"
    ? [...units]
    : units.filter((u) => u.job === jobFilter);

  // ソート
  const cmp = (a: T, b: T): number => {
    switch (sort) {
      case "totalDesc":       return b.totalRating - a.totalRating;
      case "totalAsc":        return a.totalRating - b.totalRating;
      case "ageAsc":          return a.age - b.age;
      case "ageDesc":         return b.age - a.age;
      case "hpDesc":          return b.maxHp - a.maxHp;
      case "atkDesc":         return b.attack - a.attack;
      case "spdDesc":         return b.speed - a.speed;
      case "descendantsDesc": return (b.descendantCount ?? 0) - (a.descendantCount ?? 0);
      default: return 0;
    }
  };
  arr.sort(cmp);
  return arr;
}

const SORT_LABELS: Record<SortKey, string> = {
  totalDesc:       "総合（強い順）",
  totalAsc:        "総合（弱い順）",
  ageAsc:          "年齢（若い順）",
  ageDesc:         "年齢（高齢順）",
  hpDesc:          "HP（高い順）",
  atkDesc:         "ATK（高い順）",
  spdDesc:         "SPD（高い順）",
  descendantsDesc: "子孫数（多い順）",
};

interface Props {
  sort: SortKey;
  jobFilter: JobFilter;
  onSortChange: (s: SortKey) => void;
  onJobFilterChange: (j: JobFilter) => void;
  /** 表示中の絞り込み結果件数（"3 / 25 名"のように表示するためのオプション） */
  visibleCount?: number;
  totalCount?: number;
}

export function RosterControls({
  sort,
  jobFilter,
  onSortChange,
  onJobFilterChange,
  visibleCount,
  totalCount,
}: Props): JSX.Element {
  return (
    <div data-testid="roster-controls-root" className="roster-controls">
      <label data-testid="roster-controls-sort-label" className="roster-controls-label">
        並び替え:
        <select
          data-testid="roster-sort-select"
          value={sort}
          onChange={(e) => onSortChange(e.target.value as SortKey)}
          className="roster-controls-select"
        >
          {(Object.keys(SORT_LABELS) as SortKey[]).map((key) => (
            <option
              key={key}
              data-testid={`roster-sort-option-${key}`}
              value={key}
            >
              {SORT_LABELS[key]}
            </option>
          ))}
        </select>
      </label>

      <label data-testid="roster-controls-filter-label" className="roster-controls-label">
        ジョブ:
        <select
          data-testid="roster-filter-job-select"
          value={jobFilter}
          onChange={(e) => onJobFilterChange(e.target.value)}
          className="roster-controls-select"
        >
          <option data-testid="roster-filter-job-option-all" value="all">
            全ジョブ
          </option>
          {JOB_JP_ENTRIES.map(([jobId, label]) => (
            <option
              key={jobId}
              data-testid={`roster-filter-job-option-${jobId}`}
              value={jobId}
            >
              {label}
            </option>
          ))}
        </select>
      </label>

      {visibleCount !== undefined && totalCount !== undefined && (
        <span data-testid="roster-controls-count" className="roster-controls-count">
          {visibleCount} / {totalCount} 名
        </span>
      )}
    </div>
  );
}
