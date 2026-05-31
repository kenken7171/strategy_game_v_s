/**
 * GuildManagementPage — フェーズ2: 人事
 *
 * 拡張（M3）:
 *   - 全ユニットに HP/ATK/SPD/総合的な強さ（totalRating）を表示
 *   - ジョブ名を日本語化（formatJob）
 *   - 引退候補は totalRating 昇順（弱い順）でソート済み API を表示
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type { GuildDecisionsResponse } from "../../api/types";
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

export function GuildManagementPage({ year, phaseHandle }: Props) {
  const [data, setData] = useState<GuildDecisionsResponse | null>(null);
  const [pending, setPending] = useState<boolean>(false);
  const [loading, setLoading] = useState<boolean>(true);
  // 引退候補リストのソート・フィルタ
  const [sort, setSort] = useState<SortKey>("totalAsc");
  const [jobFilter, setJobFilter] = useState<JobFilter>("all");

  const reload = useCallback(async () => {
    const d = await api.getDecisions();
    setData(d);
    setLoading(false);
  }, []);

  useEffect(() => { reload(); }, [reload, year]);

  useEffect(() => {
    const canProceed = data ? !pending && data.overflowCount === 0 : false;
    phaseHandle.setCanProceed(canProceed);
  }, [data, pending, phaseHandle]);

  const onAccept = async (unitId: string) => {
    setPending(true);
    await api.acceptRecruit(unitId);
    await reload();
    setPending(false);
  };
  const onDismiss = async (unitId: string) => {
    setPending(true);
    await api.dismissUnit(unitId);
    await reload();
    setPending(false);
  };

  // 引退候補に sort/filter を適用（オリジナルの strengthRank は API 由来で固定）
  const visibleRetirees = useMemo(() => {
    if (!data) return [];
    // descendantCount を含まないので、convert して filter
    const enriched = data.retirementCandidates.map((r) => ({
      ...r,
      descendantCount: r.descendantCount,
    }));
    return applyRosterControls(enriched, sort, jobFilter);
  }, [data, sort, jobFilter]);

  if (loading || !data) {
    return (
      <section data-testid="guild-management-page-root" className="guild-management-page">
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 人事情報を読み込み中...
        </div>
      </section>
    );
  }

  return (
    <section data-testid="guild-management-page-root" className="guild-management-page">
      <h2 data-testid="guild-management-page-title">人事フェーズ — Year {year}</h2>

      {pending && (
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 処理中...
        </div>
      )}

      <div
        data-testid="guild-overflow-summary"
        className="guild-overflow-summary"
        data-overflow={data.overflowCount}
      >
        <span data-testid="guild-overflow-current">
          現在: {data.currentSize} 名（候補 {data.recruits.length} 名を含むと予想 {data.currentSize + data.recruits.length} 名）
        </span>
        <span data-testid="guild-overflow-max">/ 定員 {data.maxSize} 名</span>
        {data.overflowCount > 0 && (
          <strong data-testid="guild-overflow-warning" className="guild-overflow-warning">
            ⚠ {data.overflowCount} 名超過しています。誰かを解雇してください
          </strong>
        )}
      </div>

      <div data-testid="guild-candidates-section" className="guild-candidates-section">
        <h3 data-testid="guild-candidates-title">
          志願者・継承者（{data.recruits.length} 名）
        </h3>
        <ul data-testid="guild-candidates-list" className="guild-candidates-list">
          {data.recruits.length === 0 && (
            <li data-testid="guild-candidates-empty" className="guild-candidates-empty">
              本年の志願者はいません
            </li>
          )}
          {data.recruits.map((c) => (
            <li
              key={c.id}
              data-testid={`guild-candidate-card-${c.id}`}
              data-has-lineage={c.hasLineage}
              data-source={c.source}
              className="guild-candidate-card"
            >
              <span data-testid={`guild-candidate-source-${c.id}`} className="guild-candidate-source-icon">
                {c.source === "heir" ? "🩸" : "✨"}
              </span>
              <span data-testid={`guild-candidate-job-${c.id}`} className="guild-candidate-job">
                [{formatJob(c.job)}]
              </span>
              <span data-testid={`guild-candidate-name-${c.id}`} className="guild-candidate-name">
                {c.name}
              </span>
              <span data-testid={`guild-candidate-gender-${c.id}`}>
                {c.gender === "Male" ? "♂" : "♀"}
              </span>
              <span data-testid={`guild-candidate-age-${c.id}`}>{c.age}歳</span>
              <span data-testid={`guild-candidate-stats-${c.id}`} className="unit-stats-inline">
                HP <b data-testid={`guild-candidate-hp-${c.id}`}>{c.maxHp}</b> /
                ATK <b data-testid={`guild-candidate-atk-${c.id}`}>{c.attack}</b> /
                SPD <b data-testid={`guild-candidate-spd-${c.id}`}>{c.speed}</b>
              </span>
              <span
                data-testid={`guild-candidate-total-${c.id}`}
                className="unit-total-rating"
                title="総合的な強さ（HP/5 + ATK + SPD）"
              >
                総合 {c.totalRating}
              </span>
              <button
                type="button"
                data-testid={`guild-accept-button-${c.id}`}
                disabled={pending}
                onClick={() => onAccept(c.id)}
                className="guild-accept-button"
              >
                採用する
              </button>
            </li>
          ))}
        </ul>
      </div>

      <div data-testid="guild-retirees-section" className="guild-retirees-section">
        <h3 data-testid="guild-retirees-title">
          引退候補（{data.retirementCandidates.length} 名）
        </h3>
        <RosterControls
          sort={sort}
          jobFilter={jobFilter}
          onSortChange={setSort}
          onJobFilterChange={setJobFilter}
          visibleCount={visibleRetirees.length}
          totalCount={data.retirementCandidates.length}
        />
        <ul data-testid="guild-retirees-list" className="guild-retirees-list">
          {visibleRetirees.map((r) => (
            <li
              key={r.id}
              data-testid={`guild-retiree-card-${r.id}`}
              data-has-lineage={r.hasLineage}
              data-rank={r.strengthRank}
              className="guild-retiree-card"
            >
              <span data-testid={`guild-retiree-rank-${r.id}`} className="guild-retiree-rank">
                #{r.strengthRank}
              </span>
              <span data-testid={`guild-retiree-job-${r.id}`} className="guild-retiree-job">
                [{formatJob(r.job)}]
              </span>
              <span data-testid={`guild-retiree-name-${r.id}`} className="guild-retiree-name">
                {r.name}
              </span>
              <span data-testid={`guild-retiree-gender-${r.id}`}>
                {r.gender === "Male" ? "♂" : "♀"}
              </span>
              <span data-testid={`guild-retiree-age-${r.id}`}>{r.age}歳</span>
              <span data-testid={`guild-retiree-stats-${r.id}`} className="unit-stats-inline">
                HP <b data-testid={`guild-retiree-hp-${r.id}`}>{r.maxHp}</b> /
                ATK <b data-testid={`guild-retiree-atk-${r.id}`}>{r.attack}</b> /
                SPD <b data-testid={`guild-retiree-spd-${r.id}`}>{r.speed}</b>
              </span>
              <span
                data-testid={`guild-retiree-total-${r.id}`}
                className="unit-total-rating"
                title="総合的な強さ（HP/5 + ATK + SPD）"
              >
                総合 {r.totalRating}
              </span>
              {r.reasons.length > 0 && (
                <span data-testid={`guild-retiree-reasons-${r.id}`} className="guild-retiree-reasons">
                  [{r.reasons.join(", ")}]
                </span>
              )}
              {r.hasLineage && (
                <span
                  data-testid={`guild-retiree-lineage-badge-${r.id}`}
                  className="guild-retiree-lineage-badge"
                  title="この騎士には家系（配偶者または親）がいます"
                >
                  🩸 血統
                </span>
              )}
              {r.descendantCount > 0 && (
                <span
                  data-testid={`guild-retiree-descendant-badge-${r.id}`}
                  className="guild-retiree-descendant-badge"
                >
                  👶 子孫 {r.descendantCount}
                </span>
              )}
              <button
                type="button"
                data-testid={`guild-dismiss-button-${r.id}`}
                disabled={pending}
                onClick={() => onDismiss(r.id)}
                className="guild-dismiss-button"
              >
                解雇する
              </button>
            </li>
          ))}
        </ul>
      </div>

      <p data-testid="guild-management-hint" className="phase-hint">
        定員以下に調整したら「次へ：編成」へ進めます。
      </p>
    </section>
  );
}
