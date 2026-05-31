/**
 * BattleSimulationPage — フェーズ4: 戦闘
 *
 * API:
 *   POST /api/battle/run      編成データを送信して戦闘実行
 *   POST /api/battle/finish   年送り（advance）
 *
 * フロー:
 *   ready → start クリック → POST /run → 結果取得 → ターンログを 1秒ごとにステップ再生
 *   → 全ターン表示完了で done → 次年へ進むボタン押下時に POST /finish
 *
 * 完了条件: status === "done"
 */
import { useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type { BattleRunResponse, BattlePlacement } from "../../api/types";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

type BattleStatus = "ready" | "running" | "replaying" | "done";

export function BattleSimulationPage({ year, phaseHandle }: Props) {
  const [status, setStatus] = useState<BattleStatus>("ready");
  const [result, setResult] = useState<BattleRunResponse | null>(null);
  const [displayedTurns, setDisplayedTurns] = useState<number>(0);
  const [errMsg, setErrMsg] = useState<string>("");
  const [finishing, setFinishing] = useState<boolean>(false);

  const canProceed = status === "done" && !finishing;

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  // 次年に進む直前に POST /finish を呼んで year を進める副作用フック
  // GameManager の advance が呼ばれる前に保存
  useEffect(() => {
    // canProceed=true 時に「次へ」が押されると親が advance を呼び phase が切り替わる
    // その瞬間 finish API も叩いてサーバ側の年を進めたいので、unmount で呼ぶ
    return () => {
      if (status === "done" && !finishing && result) {
        // ベストエフォート（失敗しても UI は次年へ進む）
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
      setErrMsg("編成データが見つかりません（編成フェーズに戻れません）");
      return;
    }
    const placements: BattlePlacement[] = JSON.parse(raw);
    try {
      const res = await api.runBattle(placements);
      setResult(res);
      setStatus("replaying");
      // ステップ再生開始
      setDisplayedTurns(0);
    } catch (e) {
      setErrMsg(String(e));
    }
  };

  // ステップ再生: turnLogs を1秒ごとに開示
  useEffect(() => {
    if (status !== "replaying" || !result) return;
    if (displayedTurns >= result.turnLogs.length) {
      setStatus("done");
      return;
    }
    const t = setTimeout(() => {
      setDisplayedTurns((n) => n + 1);
    }, 700);
    return () => clearTimeout(t);
  }, [status, result, displayedTurns]);

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

      {status === "ready" && (
        <div data-testid="battle-start-section" className="battle-start-section">
          <p data-testid="battle-start-hint" className="phase-hint">
            編成した9名で試練の門に挑みます。
          </p>
          <button
            type="button"
            data-testid="battle-start-button"
            onClick={startBattle}
            className="battle-start-button"
          >
            ⚔ 戦闘を開始する
          </button>
        </div>
      )}

      {status === "running" && (
        <div data-testid="battle-running-section" className="battle-running-section">
          <div
            data-testid="common-loading-spinner"
            className="common-loading-spinner"
          >
            ⏳ 戦闘進行中...
          </div>
        </div>
      )}

      {(status === "replaying" || status === "done") && result && (
        <>
          <div
            data-testid="battle-log-section"
            className="battle-log-section"
          >
            <h3 data-testid="battle-log-title">
              バトルログ（{displayedTurns} / {result.turnLogs.length} ターン）
            </h3>
            <ol
              data-testid="battle-log-list"
              className="battle-log-list"
            >
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
                  <div
                    data-testid={`battle-log-initiative-${idx}`}
                    className="battle-log-initiative"
                  >
                    順序: {log.initiativeText}
                  </div>
                  <div
                    data-testid={`battle-log-enemy-action-${idx}`}
                    className="battle-log-enemy-action"
                  >
                    敵: {log.enemyActionText}
                  </div>
                  {log.allyAttackLines.map((line, i) => (
                    <div
                      key={i}
                      data-testid={`battle-log-ally-attack-${idx}-${i}`}
                      className="battle-log-ally-attack"
                    >
                      味方: {line}
                    </div>
                  ))}
                  {log.healLines.map((line, i) => (
                    <div
                      key={i}
                      data-testid={`battle-log-heal-${idx}-${i}`}
                      className="battle-log-heal"
                    >
                      {line}
                    </div>
                  ))}
                  {log.victory && (
                    <div
                      data-testid={`battle-log-victory-mark-${idx}`}
                      className="battle-log-victory-mark"
                    >
                      ★ VICTORY ★
                    </div>
                  )}
                </li>
              ))}
            </ol>
          </div>

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
              <dl
                data-testid="battle-result-stats"
                className="battle-result-stats"
              >
                <dt data-testid="battle-result-label-turns">ターン数</dt>
                <dd data-testid="battle-result-value-turns">{result.turns}</dd>
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

              <div
                data-testid="battle-survivors-section"
                className="battle-survivors-section"
              >
                <h4 data-testid="battle-survivors-title">生存ユニット</h4>
                <ul
                  data-testid="battle-survivors-list"
                  className="battle-survivors-list"
                >
                  {result.allySurvivors.map((s, i) => (
                    <li
                      key={i}
                      data-testid={`battle-survivor-row-${i}`}
                      className="battle-survivor-row"
                    >
                      <span data-testid={`battle-survivor-name-${i}`}>
                        {s.name}
                      </span>
                      <span data-testid={`battle-survivor-job-${i}`}>
                        [{s.job ?? "?"}]
                      </span>
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
