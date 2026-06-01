/**
 * BattleSimulationPage — フェーズ4: 戦闘
 *
 * 拡張 (M3):
 *   - 戦闘前「作戦選択フェーズ」を追加（CW/CCW/NONE）
 *   - 1ターン目の行動順予報（タイムライン）を表示
 *   - 戦闘中、turnLog.placements で 3×3 配置を毎ターン再描画
 *   - rotationNotice をターンログとして表示
 *
 * 状態遷移:
 *   preview → strategy-selecting → running → replaying → done
 */
import { useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type {
  BattleRunResponse,
  BattlePlacement,
  BattlePreviewResponse,
  RotationStrategy,
  GridPlacement,
} from "../../api/types";
import { formatJob } from "../../utils/job";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

type BattleStatus =
  | "loading-preview"
  | "strategy-selecting"
  | "running"
  | "replaying"
  | "done";

const ROWS = ["FRONT", "REAR-L", "REAR-R"] as const;
const ROW_LABEL: Record<(typeof ROWS)[number], string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

const STRATEGY_LABELS: Record<RotationStrategy, string> = {
  NONE: "そのまま（陣形固定）",
  CW:   "右回り（時計回り）",
  CCW:  "左回り（反時計回り）",
};
const STRATEGY_DESC: Record<RotationStrategy, string> = {
  NONE: "陣形を回転させずに固定。安定した役割分担で戦う",
  CW:   "毎ターン陣形が時計回りに回転。後衛が前衛に出るリスクと火力分散の戦略",
  CCW:  "毎ターン陣形が反時計回りに回転。CWの逆順",
};

export function BattleSimulationPage({ year, phaseHandle }: Props) {
  const [status, setStatus] = useState<BattleStatus>("loading-preview");
  const [preview, setPreview] = useState<BattlePreviewResponse | null>(null);
  const [strategy, setStrategy] = useState<RotationStrategy>("NONE");
  const [result, setResult] = useState<BattleRunResponse | null>(null);
  const [displayedTurns, setDisplayedTurns] = useState<number>(0);
  const [errMsg, setErrMsg] = useState<string>("");
  const [finishing, setFinishing] = useState<boolean>(false);

  const canProceed = status === "done" && !finishing;

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  // 編成 placements を sessionStorage から取得して preview API を叩く
  useEffect(() => {
    (async () => {
      setErrMsg("");
      const raw = sessionStorage.getItem("formation:placements");
      if (!raw) {
        setErrMsg("編成データが見つかりません");
        return;
      }
      const placements: BattlePlacement[] = JSON.parse(raw);
      try {
        const p = await api.previewBattle(placements);
        setPreview(p);
        setStatus("strategy-selecting");
      } catch (e) {
        setErrMsg(String(e));
      }
    })();
  }, []);

  // unmount で年送り API を叩く
  useEffect(() => {
    return () => {
      if (status === "done" && !finishing && result) {
        api.finishBattle().catch(() => {});
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status, finishing, result]);

  const startBattle = async () => {
    setStatus("running");
    setErrMsg("");
    const raw = sessionStorage.getItem("formation:placements");
    if (!raw) {
      setErrMsg("編成データが見つかりません");
      return;
    }
    const placements: BattlePlacement[] = JSON.parse(raw);
    try {
      const res = await api.runBattle(placements, strategy);
      setResult(res);
      setStatus("replaying");
      setDisplayedTurns(0);
    } catch (e) {
      setErrMsg(String(e));
    }
  };

  // ステップ再生
  useEffect(() => {
    if (status !== "replaying" || !result) return;
    if (displayedTurns >= result.turnLogs.length) {
      setStatus("done");
      return;
    }
    const t = setTimeout(() => setDisplayedTurns((n) => n + 1), 700);
    return () => clearTimeout(t);
  }, [status, result, displayedTurns]);

  // 現在再生中の最終ターンの placements（戦況グリッド用）
  const currentPlacements: GridPlacement[] | null =
    result && displayedTurns > 0 ? result.turnLogs[displayedTurns - 1].placements : null;

  return (
    <section
      data-testid="battle-simulation-page-root"
      className="battle-simulation-page"
    >
      <h2 data-testid="battle-simulation-page-title">
        戦闘フェーズ — Year {year}
      </h2>

      {errMsg && (
        <div data-testid="battle-error-banner" className="common-error-banner">
          ❌ {errMsg}
        </div>
      )}

      {status === "loading-preview" && (
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 戦況を予測中...
        </div>
      )}

      {/* ─── 戦闘前: 作戦選択フェーズ ────────────────── */}
      {status === "strategy-selecting" && preview && (
        <>
          {/* 行動順予報 */}
          <div
            data-testid="battle-preview-timeline-section"
            className="battle-preview-timeline-section"
          >
            <h3 data-testid="battle-preview-title">
              🔮 1ターン目 行動順予報
            </h3>
            <ol
              data-testid="battle-preview-timeline"
              className="battle-preview-timeline"
            >
              {preview.timeline.map((e, i) => (
                <li
                  key={`${e.kind}-${e.id}`}
                  data-testid={`battle-preview-timeline-item-${i}`}
                  data-kind={e.kind}
                  className={`battle-preview-timeline-item battle-preview-${e.kind}`}
                >
                  <span data-testid={`battle-preview-order-${i}`} className="battle-preview-order">
                    #{i + 1}
                  </span>
                  <span data-testid={`battle-preview-icon-${i}`} className="battle-preview-icon">
                    {e.kind === "enemy" ? "🔴" : "🟦"}
                  </span>
                  <span data-testid={`battle-preview-label-${i}`} className="battle-preview-label">
                    {e.label}
                  </span>
                  <span data-testid={`battle-preview-speed-${i}`} className="battle-preview-speed">
                    SPD {e.speed.toFixed(1)}
                  </span>
                </li>
              ))}
            </ol>
            <div
              data-testid="battle-preview-enemy-stats"
              className="battle-preview-enemy-stats"
            >
              敵情報: {preview.enemyPreview.count} 体 / 最大 SPD {preview.enemyPreview.maxSpeed} / 最大 ATK {preview.enemyPreview.maxAttack}
            </div>
          </div>

          {/* 作戦選択 */}
          <div
            data-testid="battle-strategy-select-root"
            className="battle-strategy-select-root"
          >
            <h3 data-testid="battle-strategy-select-title">⚔ 作戦を選択</h3>
            <p data-testid="battle-strategy-select-hint" className="phase-hint">
              1ターンごとに陣形を回転させる作戦を選んでください。
            </p>
            <div
              data-testid="battle-strategy-options"
              className="battle-strategy-options"
              role="radiogroup"
            >
              {(["NONE", "CW", "CCW"] as RotationStrategy[]).map((s) => (
                <label
                  key={s}
                  data-testid={`battle-strategy-option-${s}`}
                  data-selected={strategy === s}
                  className={`battle-strategy-option ${strategy === s ? "selected" : ""}`}
                >
                  <input
                    type="radio"
                    name="rotation-strategy"
                    value={s}
                    checked={strategy === s}
                    onChange={() => setStrategy(s)}
                    data-testid={`battle-strategy-radio-${s}`}
                  />
                  <span data-testid={`battle-strategy-label-${s}`} className="battle-strategy-label">
                    {STRATEGY_LABELS[s]}
                  </span>
                  <span data-testid={`battle-strategy-desc-${s}`} className="battle-strategy-desc">
                    {STRATEGY_DESC[s]}
                  </span>
                </label>
              ))}
            </div>
            <button
              type="button"
              data-testid="battle-start-button"
              onClick={startBattle}
              className="battle-start-button"
            >
              ⚔ この作戦で出撃する
            </button>
          </div>
        </>
      )}

      {/* ─── 戦闘中ローディング ───────────────────── */}
      {status === "running" && (
        <div data-testid="battle-running-section" className="battle-running-section">
          <div data-testid="common-loading-spinner" className="common-loading-spinner">
            ⏳ 戦闘進行中...
          </div>
        </div>
      )}

      {/* ─── ステップ再生中 + 完了 ───────────────── */}
      {(status === "replaying" || status === "done") && result && (
        <>
          {/* 現在のフォーメーション可視化 */}
          {currentPlacements && (
            <div
              data-testid="battle-live-grid-section"
              className="battle-live-grid-section"
            >
              <h3 data-testid="battle-live-grid-title">
                現在の陣形（Turn {displayedTurns}）
                {result.rotationStrategy !== "NONE" && (
                  <span data-testid="battle-live-grid-strategy-badge" className="battle-live-grid-strategy-badge">
                    {STRATEGY_LABELS[result.rotationStrategy]}
                  </span>
                )}
              </h3>
              <table data-testid="battle-live-grid-table" className="formation-grid battle-live-grid">
                <thead>
                  <tr>
                    <th>分隊</th>
                    <th>1</th>
                    <th>2</th>
                    <th>3</th>
                  </tr>
                </thead>
                <tbody>
                  {ROWS.map((row) => (
                    <tr key={row} data-testid={`battle-live-grid-row-${row}`}>
                      <th data-testid={`battle-live-grid-row-label-${row}`} className="formation-grid-row-label">
                        {ROW_LABEL[row]}
                      </th>
                      {[0, 1, 2].map((col) => {
                        const p = currentPlacements.find((x) => x.row === row && x.col === col);
                        return (
                          <td
                            key={col}
                            data-testid={`battle-live-grid-cell-${row}-${col}`}
                            data-alive={p ? p.hp > 0 : false}
                            className="formation-grid-cell battle-live-grid-cell"
                          >
                            {p ? (
                              <div
                                data-testid={`battle-live-grid-unit-${row}-${col}`}
                                className={`battle-live-grid-unit ${p.hp <= 0 ? "fallen" : ""}`}
                              >
                                <span data-testid={`battle-live-grid-name-${row}-${col}`} className="battle-live-grid-name">
                                  {p.unitName}
                                </span>
                                <span data-testid={`battle-live-grid-job-${row}-${col}`} className="battle-live-grid-job">
                                  [{formatJob(p.job)}]
                                </span>
                                <span data-testid={`battle-live-grid-hp-${row}-${col}`} className="battle-live-grid-hp">
                                  HP {p.hp}/{p.maxHp}
                                </span>
                              </div>
                            ) : (
                              <span className="formation-cell-empty">—</span>
                            )}
                          </td>
                        );
                      })}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* バトルログ */}
          <div
            data-testid="battle-log-section"
            className="battle-log-section"
          >
            <h3 data-testid="battle-log-title">
              バトルログ（{displayedTurns} / {result.turnLogs.length} ターン）
            </h3>
            <ol data-testid="battle-log-list" className="battle-log-list">
              {result.turnLogs.slice(0, displayedTurns).map((log, idx) => (
                <li
                  key={log.turn}
                  data-testid={`battle-log-row-${idx}`}
                  data-turn={log.turn}
                  data-victory={log.victory}
                  className="battle-log-row"
                >
                  <div data-testid={`battle-log-header-${idx}`} className="battle-log-header">
                    🏰 {log.headerText}
                  </div>
                  {log.rotationNotice && (
                    <div
                      data-testid={`battle-log-rotation-notice-${idx}`}
                      className="battle-log-rotation-notice"
                    >
                      {log.rotationNotice}
                    </div>
                  )}
                  <div data-testid={`battle-log-initiative-${idx}`} className="battle-log-initiative">
                    順序: {log.initiativeText}
                  </div>
                  <div data-testid={`battle-log-enemy-action-${idx}`} className="battle-log-enemy-action">
                    敵: {log.enemyActionText}
                  </div>
                  {log.allyAttackLines.map((line, i) => (
                    <div key={i} data-testid={`battle-log-ally-attack-${idx}-${i}`} className="battle-log-ally-attack">
                      味方: {line}
                    </div>
                  ))}
                  {log.healLines.map((line, i) => (
                    <div key={i} data-testid={`battle-log-heal-${idx}-${i}`} className="battle-log-heal">
                      {line}
                    </div>
                  ))}
                  {log.victory && (
                    <div data-testid={`battle-log-victory-mark-${idx}`} className="battle-log-victory-mark">
                      ★ VICTORY ★
                    </div>
                  )}
                </li>
              ))}
            </ol>
          </div>

          {/* 戦闘結果 */}
          {status === "done" && (
            <div
              data-testid="battle-result-section"
              className="battle-result-section"
              data-winner={result.winner}
            >
              <h3 data-testid="battle-result-title">戦闘結果</h3>
              <div
                data-testid="battle-result-winner-banner"
                className={`battle-result-winner-banner battle-result-winner-${result.winner}`}
              >
                {result.winner === "Allies"
                  ? "🎉 VICTORY!"
                  : result.winner === "Enemies"
                    ? "💀 DEFEAT..."
                    : "🤝 DRAW"}
              </div>
              <dl data-testid="battle-result-stats" className="battle-result-stats">
                <dt data-testid="battle-result-label-turns">ターン数</dt>
                <dd data-testid="battle-result-value-turns">{result.turns}</dd>
                <dt data-testid="battle-result-label-strategy">作戦</dt>
                <dd data-testid="battle-result-value-strategy">
                  {STRATEGY_LABELS[result.rotationStrategy]}
                </dd>
                <dt data-testid="battle-result-label-survivors">味方生存</dt>
                <dd data-testid="battle-result-value-survivors">
                  {result.allySurvivors.length} / 9
                </dd>
                <dt data-testid="battle-result-label-enemy-survivors">敵生存</dt>
                <dd data-testid="battle-result-value-enemy-survivors">
                  {result.enemySurvivors.length} / 10
                </dd>
                <dt data-testid="battle-result-label-mitigation">被ダメ軽減</dt>
                <dd data-testid="battle-result-value-mitigation">
                  {result.statistics.totalDamageMitigated} HP
                </dd>
              </dl>

              <div data-testid="battle-survivors-section" className="battle-survivors-section">
                <h4 data-testid="battle-survivors-title">生存ユニット</h4>
                <ul data-testid="battle-survivors-list" className="battle-survivors-list">
                  {result.allySurvivors.map((s, i) => (
                    <li key={i} data-testid={`battle-survivor-row-${i}`} className="battle-survivor-row">
                      <span data-testid={`battle-survivor-name-${i}`}>{s.name}</span>
                      <span data-testid={`battle-survivor-job-${i}`}>[{formatJob(s.job)}]</span>
                      <span data-testid={`battle-survivor-hp-${i}`}>
                        HP {s.hp}/{s.maxHp}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>

              <p data-testid="battle-result-hint" className="phase-hint">
                準備が整いました。「次年を迎える」ボタンで年送りします。
              </p>
            </div>
          )}
        </>
      )}
    </section>
  );
}
