/**
 * GuildManagementPage — フェーズ2: 人事
 *
 * API:
 *   GET  /api/guild/decisions   採用候補 + 引退候補
 *   POST /api/guild/accept      採用
 *   POST /api/guild/dismiss     解雇
 *
 * 完了条件: overflowCount === 0 && !pending
 */
import { useCallback, useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type { GuildDecisionsResponse } from "../../api/types";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

export function GuildManagementPage({ year, phaseHandle }: Props) {
  const [data, setData] = useState<GuildDecisionsResponse | null>(null);
  const [pending, setPending] = useState<boolean>(false);
  const [loading, setLoading] = useState<boolean>(true);

  const reload = useCallback(async () => {
    const d = await api.getDecisions();
    setData(d);
    setLoading(false);
  }, []);

  useEffect(() => {
    reload();
  }, [reload, year]);

  // canProceed: 定員以下 かつ API 通信中でない
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

  if (loading || !data) {
    return (
      <section
        data-testid="guild-management-page-root"
        className="guild-management-page"
      >
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 人事情報を読み込み中...
        </div>
      </section>
    );
  }

  return (
    <section
      data-testid="guild-management-page-root"
      className="guild-management-page"
    >
      <h2 data-testid="guild-management-page-title">人事フェーズ — Year {year}</h2>

      {pending && (
        <div
          data-testid="common-loading-spinner"
          className="common-loading-spinner"
        >
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
          <strong
            data-testid="guild-overflow-warning"
            className="guild-overflow-warning"
          >
            ⚠ {data.overflowCount} 名超過しています。誰かを解雇してください
          </strong>
        )}
      </div>

      <div
        data-testid="guild-candidates-section"
        className="guild-candidates-section"
      >
        <h3 data-testid="guild-candidates-title">
          志願者・継承者（{data.recruits.length} 名）
        </h3>
        <ul
          data-testid="guild-candidates-list"
          className="guild-candidates-list"
        >
          {data.recruits.length === 0 && (
            <li
              data-testid="guild-candidates-empty"
              className="guild-candidates-empty"
            >
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
              <span data-testid={`guild-candidate-name-${c.id}`}>{c.name}</span>
              <span data-testid={`guild-candidate-job-${c.id}`}>[{c.job ?? "?"}]</span>
              <span data-testid={`guild-candidate-gender-${c.id}`}>
                {c.gender === "Male" ? "♂" : "♀"}
              </span>
              <span data-testid={`guild-candidate-origin-${c.id}`}>
                {c.origin}
              </span>
              <span data-testid={`guild-candidate-age-${c.id}`}>{c.age}歳</span>
              <span data-testid={`guild-candidate-strength-${c.id}`}>
                STR {c.baseStrength}
              </span>
              <span data-testid={`guild-candidate-source-${c.id}`}>
                {c.source === "heir" ? "🩸 継承者" : "✨ 志願者"}
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

      <div
        data-testid="guild-retirees-section"
        className="guild-retirees-section"
      >
        <h3 data-testid="guild-retirees-title">
          引退候補（弱者・老兵 / {data.retirementCandidates.length} 名）
        </h3>
        <ul
          data-testid="guild-retirees-list"
          className="guild-retirees-list"
        >
          {data.retirementCandidates.map((r) => (
            <li
              key={r.id}
              data-testid={`guild-retiree-card-${r.id}`}
              data-has-lineage={r.hasLineage}
              data-rank={r.strengthRank}
              className="guild-retiree-card"
            >
              <span data-testid={`guild-retiree-rank-${r.id}`}>
                #{r.strengthRank}
              </span>
              <span data-testid={`guild-retiree-name-${r.id}`}>{r.name}</span>
              <span data-testid={`guild-retiree-job-${r.id}`}>[{r.job ?? "?"}]</span>
              <span data-testid={`guild-retiree-gender-${r.id}`}>
                {r.gender === "Male" ? "♂" : "♀"}
              </span>
              <span data-testid={`guild-retiree-age-${r.id}`}>{r.age}歳</span>
              <span data-testid={`guild-retiree-strength-${r.id}`}>
                STR {r.strength}
              </span>
              {r.reasons.length > 0 && (
                <span
                  data-testid={`guild-retiree-reasons-${r.id}`}
                  className="guild-retiree-reasons"
                >
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
