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
  AttackIntent,
  SquadRow,
  EnemyState,
} from "../../api/types";
import { formatJob } from "../../utils/job";
import { UnitIcon } from "../../components/UnitIcon";

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

const ROW_LABEL_MAP: Record<SquadRow, string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

const INTENT_KIND_LABEL: Record<AttackIntent["kind"], { icon: string; severity: "low" | "mid" | "high" }> = {
  SINGLE_STRIKE: { icon: "💥", severity: "high" },
  PINCER:        { icon: "⚔️",  severity: "mid"  },
  TOTAL_ASSAULT: { icon: "🔥", severity: "low"  },
};

/**
 * 敵ボスステータスカード（戦闘画面最上部、味方陣形の直上）。
 *
 * 「今敵がどんな状況か一目でわかる」ために、以下を集約表示:
 *   - 敵の名前
 *   - HP バー（current / max、低体力時は色変化）
 *   - frontAttack / rearAttack
 *   - speed
 *
 * 旧 EnemyIntentBanner はこのカードの直下に配置することで、
 * 「敵ステータス → 次の予告 → 自分の布陣」の視線移動だけで戦況が読める。
 */
function EnemyStatusCard({
  enemy, currentTurn,
}: {
  enemy: EnemyState;
  currentTurn: number;
}) {
  const ratio = enemy.maxHp > 0 ? Math.max(0, Math.min(1, enemy.hp / enemy.maxHp)) : 0;
  const pct = Math.round(ratio * 100);
  const isLow = ratio < 0.3 && enemy.hp > 0;
  return (
    <div
      data-testid="battle-enemy-status-card"
      className="battle-enemy-status-card"
    >
      <div
        data-testid="battle-enemy-status-header"
        className="battle-enemy-status-header"
      >
        <span data-testid="battle-enemy-status-icon" className="battle-enemy-status-icon">
          👹
        </span>
        <h3
          data-testid="battle-enemy-status-name"
          className="battle-enemy-status-name"
        >
          {enemy.name}
        </h3>
        <span
          data-testid="battle-enemy-status-turn-label"
          className="battle-enemy-status-turn-label"
        >
          Turn {currentTurn}
        </span>
      </div>
      <div data-testid="battle-enemy-hp-row" className="battle-enemy-hp-row">
        <span data-testid="battle-enemy-hp-label" className="battle-enemy-hp-label">
          HP
        </span>
        <div
          data-testid="battle-enemy-hp-bar-wrap"
          className="battle-enemy-hp-bar-wrap"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={enemy.maxHp}
          aria-valuenow={enemy.hp}
        >
          <div
            data-testid="battle-enemy-hp-bar-fill"
            data-low={isLow}
            className="battle-enemy-hp-bar-fill"
            style={{ width: `${pct}%` }}
          />
        </div>
        <span data-testid="battle-enemy-hp-text" className="battle-enemy-hp-text">
          {enemy.hp} / {enemy.maxHp}
        </span>
      </div>
      <div data-testid="battle-enemy-stats-row" className="battle-enemy-stats-row">
        <span
          data-testid="battle-enemy-stat-front-attack"
          className="battle-enemy-stat-chip"
        >
          ATK(前) <b>{enemy.frontAttack}</b>
        </span>
        <span
          data-testid="battle-enemy-stat-rear-attack"
          className="battle-enemy-stat-chip"
        >
          ATK(後) <b>{enemy.rearAttack}</b>
        </span>
        <span
          data-testid="battle-enemy-stat-speed"
          className="battle-enemy-stat-chip"
        >
          SPD <b>{enemy.speed}</b>
        </span>
      </div>
    </div>
  );
}

/**
 * 敵の次ターン攻撃予告バナー。
 * - skill name (battle-enemy-intent-skill-name)
 * - target range (battle-enemy-intent-target-range)
 * - damage per unit
 * - 影響を受けるユニット数（placements から自動計算）
 */
function EnemyIntentBanner({
  intent, placements,
}: {
  intent: AttackIntent;
  placements: GridPlacement[];
}) {
  const meta = INTENT_KIND_LABEL[intent.kind];
  // 対象分隊の生存ユニット数
  const affectedUnits = placements.filter(
    (p) => p.hp > 0 && intent.targetRows.includes(p.row)
  );
  const targetLabels = intent.targetRows.map((r) => ROW_LABEL_MAP[r]).join(" + ");
  return (
    <div
      data-testid="battle-enemy-intent-banner"
      data-kind={intent.kind}
      data-severity={meta.severity}
      className={`battle-enemy-intent-banner severity-${meta.severity}`}
    >
      <div data-testid="battle-enemy-intent-header" className="battle-enemy-intent-header">
        <span data-testid="battle-enemy-intent-icon" className="battle-enemy-intent-icon">
          {meta.icon}
        </span>
        <span data-testid="battle-enemy-intent-warning-text" className="battle-enemy-intent-warning-text">
          ⚠️ 敵の次ターン行動予告
        </span>
      </div>
      <div data-testid="battle-enemy-intent-body" className="battle-enemy-intent-body">
        <span
          data-testid="battle-enemy-intent-skill-name"
          className="battle-enemy-intent-skill-name"
        >
          【{intent.skillName}】
        </span>
        <span data-testid="battle-enemy-intent-arrow" className="battle-enemy-intent-arrow">
          ➔
        </span>
        <span
          data-testid="battle-enemy-intent-target-range"
          className="battle-enemy-intent-target-range"
        >
          {targetLabels} の全員
        </span>
        <span
          data-testid="battle-enemy-intent-damage"
          className="battle-enemy-intent-damage"
        >
          {intent.damagePerUnit} ダメージ！
        </span>
      </div>
      <div
        data-testid="battle-enemy-intent-affected-list"
        className="battle-enemy-intent-affected-list"
      >
        影響圏内: {affectedUnits.length} 名
        {affectedUnits.length === 0 ? (
          <span
            data-testid="battle-enemy-intent-affected-empty"
            className="battle-enemy-intent-affected-empty"
          >
            （該当ユニットなし）
          </span>
        ) : (
          <ul
            data-testid="battle-enemy-intent-affected-units"
            className="battle-enemy-intent-affected-units"
          >
            {affectedUnits.map((u) => (
              <li
                key={u.unitId}
                data-testid={`battle-enemy-intent-affected-unit-${u.unitId}`}
                className="battle-enemy-intent-affected-unit"
              >
                <span
                  data-testid={`battle-enemy-intent-affected-icon-slot-${u.unitId}`}
                  className="unit-icon-slot unit-icon-slot-xs"
                >
                  <UnitIcon
                    jobId={u.job}
                    gender={u.gender}
                    altName={u.unitName}
                    testIdSuffix={`intent-affected-${u.unitId}`}
                  />
                </span>
                <span data-testid={`battle-enemy-intent-affected-name-${u.unitId}`}>
                  {u.unitName}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}
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
  const [nextIntent, setNextIntent] = useState<AttackIntent | null>(null);
  /** 敵ボスの現在ステータス（毎ターン更新） */
  const [enemy, setEnemy] = useState<EnemyState | null>(null);

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
        setNextIntent(res.nextActionIntent);
        setEnemy(res.enemy);
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
      // 次ターンの timeline + 攻撃予告
      if (res.timeline) setTimeline(res.timeline);
      setNextIntent(res.nextActionIntent);
      // 敵 state（HP は毎ターン減少する）
      setEnemy(res.enemy);
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

      {/* ★ 1. 敵ステータスカード（最上部・常時表示）
            戦闘中いつでも「敵が今どんな状況か」が一目でわかるよう、
            ローディング以外の全ステージで表示する。 */}
      {status !== "loading-init" && enemy && (
        <EnemyStatusCard enemy={enemy} currentTurn={currentTurn} />
      )}

      {/* ★ 2. 敵の次ターン攻撃予告（waiting-command のときだけ・敵カード直下） */}
      {status === "waiting-command" && winner === null && nextIntent && (
        <EnemyIntentBanner intent={nextIntent} placements={placements} />
      )}

      {/* ★ 3. ライブ陣形グリッド（V字 + ターゲット分隊の赤枠脈動）
            敵が予告中（waiting-command）かつ nextIntent.targetRows に含まれる
            分隊は赤枠＋脈動で「危険地帯」を可視化する。 */}
      {status !== "loading-init" && placements.length > 0 && (
        <div
          data-testid="battle-live-grid-section"
          className="battle-live-grid-section"
        >
          <h3 data-testid="battle-live-grid-title">現在の陣形</h3>
          <div
            data-testid="battle-live-grid-table"
            className="formation-v-shape formation-v-shape--battle"
          >
            {ROWS.map((row) => {
              const squadModifier =
                row === "FRONT" ? "front"
                : row === "REAR-L" ? "rear-l"
                : "rear-r";
              // 赤枠 ON 条件: 敵予告フェーズ + targetRows に含まれる
              const isTargeted =
                status === "waiting-command" &&
                winner === null &&
                nextIntent !== null &&
                nextIntent.targetRows.includes(row);
              return (
                <div
                  key={row}
                  data-testid={`battle-live-v-squad-${row}`}
                  data-row={row}
                  data-targeted={isTargeted}
                  className={`formation-v-squad formation-v-squad-${squadModifier}`}
                >
                  <div
                    data-testid={`battle-live-grid-row-label-${row}`}
                    className="formation-v-squad-header"
                  >
                    {isTargeted ? "🎯 " : row === "FRONT" ? "⚔ " : "🛡 "}
                    {ROW_LABEL[row]}
                    {isTargeted && (
                      <span
                        data-testid={`battle-live-v-squad-targeted-mark-${row}`}
                        className="battle-live-v-squad-targeted-mark"
                      >
                        【狙われている】
                      </span>
                    )}
                  </div>
                  <div
                    data-testid={`battle-live-v-slot-row-${row}`}
                    className="formation-v-slot-row"
                  >
                    {[0, 1, 2].map((col) => {
                      const p = placements.find((x) => x.row === row && x.col === col);
                      return (
                        <div
                          key={col}
                          data-testid={`battle-live-grid-cell-${row}-${col}`}
                          data-alive={p ? p.hp > 0 : false}
                          className="formation-v-slot battle-live-grid-cell"
                        >
                          {p ? (
                            <div
                              data-testid={`battle-live-grid-unit-${row}-${col}`}
                              className={`battle-live-grid-unit ${p.hp <= 0 ? "fallen" : ""}`}
                            >
                              <span
                                data-testid={`battle-live-grid-icon-slot-${row}-${col}`}
                                className="unit-icon-slot unit-icon-slot-md"
                              >
                                <UnitIcon
                                  jobId={p.job}
                                  gender={p.gender}
                                  altName={p.unitName}
                                  testIdSuffix={`battle-live-${row}-${col}`}
                                />
                              </span>
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
                        </div>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* ★ 4. 行動順予報 + 5. コマンド選択（waiting-command のときのみ）
            ※ 攻撃予告バナーは敵カード直下に集約済みのためここからは削除 */}
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
                    <span
                      data-testid={`battle-survivor-icon-slot-${i}`}
                      className="unit-icon-slot unit-icon-slot-sm"
                    >
                      <UnitIcon
                        jobId={s.job}
                        gender={s.gender ?? null}
                        altName={s.name}
                        testIdSuffix={`battle-survivor-${i}`}
                      />
                    </span>
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
