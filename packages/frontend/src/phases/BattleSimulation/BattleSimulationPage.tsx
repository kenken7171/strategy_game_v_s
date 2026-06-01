/**
 * BattleSimulationPage — フェーズ4: 戦闘（リアルタイム指揮システム）
 *
 * 旧仕様（戦闘前一括作戦選択）を廃止。
 * 新フロー: 毎ターン頭でプレイヤーがコマンド（CW/CCW/NONE）を選択する。
 *
 * 状態遷移:
 *   loading-init → waiting-command → running-turn → playing-log →
 *     (continue) waiting-command  もしくは
 *     (done) done
 *
 * API 使用:
 *   POST /api/battle/init  戦闘準備（初期 placements + 1ターン目 timeline）
 *   POST /api/battle/turn  1ターン分の戦略を送って実行（CW/CCW/NONE）
 *   POST /api/battle/finish 戦闘終了後の年送り
 */
import { useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type {
  BattleInitResponse,
  BattleTurnResponse,
  BattlePlacement,
  RotationStrategy,
  GridPlacement,
  PreviewTimelineEntry,
  TurnLog,
  Winner,
  SurvivorRow,
} from "../../api/types";
import { formatJob } from "../../utils/job";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

type BattleStatus =
  | "loading-init"     // /init 中
  | "waiting-command"  // ターン頭・コマンド待ち
  | "running-turn"     // /turn 通信中
  | "playing-log"      // ターンログ再生中
  | "done";

const ROWS = ["FRONT", "REAR-L", "REAR-R"] as const;
const ROW_LABEL: Record<(typeof ROWS)[number], string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

const STRATEGY_LABELS: Record<RotationStrategy, string> = {
  NONE: "そのまま",
  CW: "↻ 右回り",
  CCW: "↺ 左回り",
};
const STRATEGY_DESC: Record<RotationStrategy, string> = {
  NONE: "陣形を回転させず、この配置で1ターン戦う",
  CW:   "陣形を時計回りに1段回す。後衛の若手が前衛に出る",
  CCW:  "陣形を反時計回りに1段回す。CWの逆順",
};

export function BattleSimulationPage({ year, phaseHandle }: Props) {
  const [status, setStatus] = useState<BattleStatus>("loading-init");
  const [errMsg, setErrMsg] = useState<string>("");

  // 現在の戦況
  const [placements, setPlacements] = useState<GridPlacement[]>([]);
  const [timeline, setTimeline] = useState<PreviewTimelineEntry[]>([]);
  const [currentTurn, setCurrentTurn] = useState<number>(0);

  // ターンログ累積（UI 表示用）
  const [turnLogs, setTurnLogs] = useState<TurnLog[]>([]);
  const [logCursor, setLogCursor] = useState<number>(0); // 何ターンめまで「再生済み」か

  // 最新ターン
  const [latestTurnLog, setLatestTurnLog] = useState<TurnLog | null>(null);
  const [logPlaying, setLogPlaying] = useState<boolean>(false);

  // 戦闘終了
  const [winner, setWinner] = useState<Winner | null>(null);
  const [allySurvivors, setAllySurvivors] = useState<SurvivorRow[] | null>(null);
  const [enemySurvivors, setEnemySurvivors] = useState<SurvivorRow[] | null>(null);

  const canProceed = status === "done";

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  // /init 呼び出し
  useEffect(() => {
    (async () => {
      const raw = sessionStorage.getItem("formation:placements");
      if (!raw) {
        setErrMsg("編成データが見つかりません");
        return;
      }
      const pls: BattlePlacement[] = JSON.parse(raw);
      try {
        const res = await api.initBattle(pls);
        setPlacements(res.placements);
        setTimeline(res.timeline);
        setCurrentTurn(res.currentTurn);
        setStatus("waiting-command");
      } catch (e) {
        setErrMsg(String(e));
      }
    })();
  }, []);

  // 戦闘終了時の finish API（unmount で）
  useEffect(() => {
    return () => {
      if (status === "done") api.finishBattle().catch(() => {});
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  // ターンログ自動表示（700ms）
  useEffect(() => {
    if (status !== "playing-log" || !latestTurnLog) return;
    // 1ターンの全テキストを描画した後、次ターン頭に戻る
    const t = setTimeout(() => {
      setLogPlaying(false);
      setLatestTurnLog(null);
      // 戦闘終了か継続か
      if (winner !== null) {
        setStatus("done");
      } else {
        setStatus("waiting-command");
      }
    }, 1200);
    return () => clearTimeout(t);
  }, [status, latestTurnLog, winner]);

  // コマンド実行
  const sendCommand = async (strategy: RotationStrategy) => {
    setStatus("running-turn");
    try {
      const res: BattleTurnResponse = await api.runTurn(strategy);
      // ログ追加
      setTurnLogs((prev) => [...prev, res.turnLog]);
      setLatestTurnLog(res.turnLog);
      setLogCursor((prev) => prev + 1);
      // placements 更新
      setPlacements(res.turnLog.placements);
      setCurrentTurn(res.currentTurn);
      // 次ターンの timeline
      if (res.timeline) setTimeline(res.timeline);
      // 終了判定
      if (res.finished) {
        setWinner(res.winner);
        setAllySurvivors(res.allySurvivors);
        setEnemySurvivors(res.enemySurvivors);
      }
      setLogPlaying(true);
      setStatus("playing-log");
    } catch (e) {
      setErrMsg(String(e));
      setStatus("waiting-command");
    }
  };

  return (
    <section
      data-testid="battle-simulation-page-root"
      className="battle-simulation-page"
    >
      <h2 data-testid="battle-simulation-page-title">
        戦闘フェーズ — Year {year}（Turn {currentTurn}）
      </h2>

      {errMsg && (
        <div data-testid="battle-error-banner" className="common-error-banner">
          ❌ {errMsg}
        </div>
      )}

      {status === "loading-init" && (
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⏳ 戦闘を準備中...
        </div>
      )}

      {/* ライブ陣形グリッド（常時表示） */}
      {status !== "loading-init" && placements.length > 0 && (
        <div
          data-testid="battle-live-grid-section"
          className="battle-live-grid-section"
        >
          <h3 data-testid="battle-live-grid-title">現在の陣形</h3>
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
                  <th
                    data-testid={`battle-live-grid-row-label-${row}`}
                    className="formation-grid-row-label"
                  >
                    {ROW_LABEL[row]}
                  </th>
                  {[0, 1, 2].map((col) => {
                    const p = placements.find((x) => x.row === row && x.col === col);
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

      {/* ターン頭：行動順予報 + コマンド選択 */}
      {status === "waiting-command" && winner === null && (
        <>
          <div
            data-testid="battle-preview-timeline-section"
            className="battle-preview-timeline-section"
          >
            <h3 data-testid="battle-preview-title">
              🔮 Turn {currentTurn + 1} 行動順予報
            </h3>
            <ol data-testid="battle-preview-timeline" className="battle-preview-timeline">
              {timeline.map((e, i) => (
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
          </div>

          <div
            data-testid="battle-turn-command-root"
            className="battle-turn-command-root"
          >
            <h3 data-testid="battle-turn-command-title">
              ⚔ ターン {currentTurn + 1} の作戦を指示
            </h3>
            <div
              data-testid="battle-turn-command-options"
              className="battle-turn-command-options"
            >
              {(["NONE", "CW", "CCW"] as RotationStrategy[]).map((s) => (
                <button
                  key={s}
                  type="button"
                  data-testid={`battle-turn-command-${s}`}
                  onClick={() => sendCommand(s)}
                  className={`battle-turn-command-button battle-turn-command-${s}`}
                >
                  <span data-testid={`battle-turn-command-label-${s}`} className="battle-turn-command-label">
                    {STRATEGY_LABELS[s]}
                  </span>
                  <span data-testid={`battle-turn-command-desc-${s}`} className="battle-turn-command-desc">
                    {STRATEGY_DESC[s]}
                  </span>
                </button>
              ))}
            </div>
          </div>
        </>
      )}

      {status === "running-turn" && (
        <div data-testid="common-loading-spinner" className="common-loading-spinner">
          ⚔ 戦闘処理中...
        </div>
      )}

      {/* バトルログ累積（過去の全ターンを蓄積表示） */}
      {turnLogs.length > 0 && (
        <div data-testid="battle-log-section" className="battle-log-section">
          <h3 data-testid="battle-log-title">
            バトルログ（{turnLogs.length} ターン進行）
          </h3>
          <ol data-testid="battle-log-list" className="battle-log-list">
            {turnLogs.map((log, idx) => {
              const isLatest = idx === turnLogs.length - 1;
              return (
                <li
                  key={log.turn}
                  data-testid={`battle-log-row-${idx}`}
                  data-turn={log.turn}
                  data-victory={log.victory}
                  data-latest={isLatest}
                  className={`battle-log-row ${isLatest && logPlaying ? "playing" : ""}`}
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
              );
            })}
          </ol>
        </div>
      )}

      {/* 戦闘結果 */}
      {status === "done" && winner !== null && (
        <div
          data-testid="battle-result-section"
          className="battle-result-section"
          data-winner={winner}
        >
          <h3 data-testid="battle-result-title">戦闘結果</h3>
          <div
            data-testid="battle-result-winner-banner"
            className={`battle-result-winner-banner battle-result-winner-${winner}`}
          >
            {winner === "Allies"
              ? "🎉 VICTORY!"
              : winner === "Enemies"
                ? "💀 DEFEAT..."
                : "🤝 DRAW"}
          </div>
          <dl data-testid="battle-result-stats" className="battle-result-stats">
            <dt data-testid="battle-result-label-turns">ターン数</dt>
            <dd data-testid="battle-result-value-turns">{currentTurn}</dd>
            <dt data-testid="battle-result-label-survivors">味方生存</dt>
            <dd data-testid="battle-result-value-survivors">
              {allySurvivors?.length ?? 0} / 9
            </dd>
            <dt data-testid="battle-result-label-enemy-survivors">敵生存</dt>
            <dd data-testid="battle-result-value-enemy-survivors">
              {enemySurvivors?.length ?? 0} / 10
            </dd>
          </dl>
          {allySurvivors && allySurvivors.length > 0 && (
            <div data-testid="battle-survivors-section" className="battle-survivors-section">
              <h4 data-testid="battle-survivors-title">生存ユニット</h4>
              <ul data-testid="battle-survivors-list" className="battle-survivors-list">
                {allySurvivors.map((s, i) => (
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
          )}
          <p data-testid="battle-result-hint" className="phase-hint">
            「次年を迎える」で年送りします。
          </p>
        </div>
      )}
    </section>
  );
}
