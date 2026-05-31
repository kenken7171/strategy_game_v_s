/**
 * GuildManagementPage — フェーズ2: 人事（採用・解雇）
 *
 * canProceed の条件:
 *   - 定員超過していないこと（overflowCount === 0）
 *   - API 通信中（pending）でないこと
 *
 * M2 段階では暫定UI。Brigade/HumanDecisionService との接続は M3 で。
 */
import { useEffect, useMemo, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

interface MockCandidate {
  id: string;
  name: string;
  job: string;
  source: "application" | "heir";
  hasLineage: boolean;
}

interface MockRetiree {
  id: string;
  name: string;
  age: number;
  strength: number;
  reasons: string[];
  hasLineage: boolean;
}

const MOCK_CANDIDATES: MockCandidate[] = [
  { id: "cand-1", name: "Newbie A", job: "sniper", source: "application", hasLineage: false },
  { id: "cand-2", name: "Heir B", job: "iron_wall_knight", source: "heir", hasLineage: true },
];
const MOCK_RETIREES: MockRetiree[] = [
  { id: "ret-1", name: "OldKnight", age: 42, strength: 38, reasons: ["decline", "weak"], hasLineage: false },
  { id: "ret-2", name: "Elise", age: 38, strength: 56, reasons: ["decline"], hasLineage: true },
];

const MOCK_BRIGADE_SIZE = 51;
const MAX_BRIGADE_SIZE = 50;

export function GuildManagementPage({ year, phaseHandle }: Props) {
  const [accepted, setAccepted] = useState<Set<string>>(new Set());
  const [dismissed, setDismissed] = useState<Set<string>>(new Set());
  const [pending] = useState<boolean>(false); // API 通信中フラグ（M3 で接続）

  const projectedSize = useMemo(() => {
    return MOCK_BRIGADE_SIZE + accepted.size - dismissed.size;
  }, [accepted, dismissed]);

  const overflowCount = Math.max(0, projectedSize - MAX_BRIGADE_SIZE);
  const canProceed = !pending && overflowCount === 0;

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  const toggleAccept = (id: string) => {
    setAccepted((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const toggleDismiss = (id: string) => {
    setDismissed((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  return (
    <section
      data-testid="guild-management-page-root"
      className="guild-management-page"
    >
      <h2 data-testid="guild-management-page-title">
        人事フェーズ — Year {year}
      </h2>

      <div
        data-testid="guild-overflow-summary"
        className="guild-overflow-summary"
        data-overflow={overflowCount}
      >
        <span data-testid="guild-overflow-current">
          予想人数: {projectedSize} 名
        </span>
        <span data-testid="guild-overflow-max">/ 定員 {MAX_BRIGADE_SIZE} 名</span>
        {overflowCount > 0 && (
          <strong
            data-testid="guild-overflow-warning"
            className="guild-overflow-warning"
          >
            ⚠ {overflowCount} 名超過しています。誰かを解雇してください
          </strong>
        )}
      </div>

      <div
        data-testid="guild-candidates-section"
        className="guild-candidates-section"
      >
        <h3 data-testid="guild-candidates-title">志願者・継承者</h3>
        <ul
          data-testid="guild-candidates-list"
          className="guild-candidates-list"
        >
          {MOCK_CANDIDATES.map((c) => {
            const isAccepted = accepted.has(c.id);
            return (
              <li
                key={c.id}
                data-testid={`guild-candidate-card-${c.id}`}
                data-accepted={isAccepted}
                data-has-lineage={c.hasLineage}
                className="guild-candidate-card"
              >
                <span data-testid={`guild-candidate-name-${c.id}`}>
                  {c.name}
                </span>
                <span data-testid={`guild-candidate-job-${c.id}`}>
                  [{c.job}]
                </span>
                <span data-testid={`guild-candidate-source-${c.id}`}>
                  {c.source === "heir" ? "🩸 継承者" : "✨ 志願者"}
                </span>
                <button
                  type="button"
                  data-testid={`guild-accept-button-${c.id}`}
                  data-active={isAccepted}
                  onClick={() => toggleAccept(c.id)}
                  className="guild-accept-button"
                >
                  {isAccepted ? "採用予定" : "採用する"}
                </button>
              </li>
            );
          })}
        </ul>
      </div>

      <div
        data-testid="guild-retirees-section"
        className="guild-retirees-section"
      >
        <h3 data-testid="guild-retirees-title">引退候補</h3>
        <ul
          data-testid="guild-retirees-list"
          className="guild-retirees-list"
        >
          {MOCK_RETIREES.map((r) => {
            const isDismissed = dismissed.has(r.id);
            return (
              <li
                key={r.id}
                data-testid={`guild-retiree-card-${r.id}`}
                data-dismissed={isDismissed}
                data-has-lineage={r.hasLineage}
                className="guild-retiree-card"
              >
                <span data-testid={`guild-retiree-name-${r.id}`}>
                  {r.name}
                </span>
                <span data-testid={`guild-retiree-age-${r.id}`}>
                  ({r.age}歳)
                </span>
                <span data-testid={`guild-retiree-strength-${r.id}`}>
                  STR {r.strength}
                </span>
                <span data-testid={`guild-retiree-reasons-${r.id}`}>
                  [{r.reasons.join(",")}]
                </span>
                {r.hasLineage && (
                  <span data-testid={`guild-retiree-lineage-badge-${r.id}`}>
                    🩸 血統あり
                  </span>
                )}
                <button
                  type="button"
                  data-testid={`guild-dismiss-button-${r.id}`}
                  data-active={isDismissed}
                  onClick={() => toggleDismiss(r.id)}
                  className="guild-dismiss-button"
                >
                  {isDismissed ? "解雇予定" : "解雇する"}
                </button>
              </li>
            );
          })}
        </ul>
      </div>

      <p data-testid="guild-management-hint" className="phase-hint">
        定員以下に調整したら「次へ：編成」へ進めます。
      </p>
    </section>
  );
}
