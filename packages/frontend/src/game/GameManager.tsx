/**
 * GameManager — 4フェーズ厳格遷移のホスト
 *
 * instructions.md F-1 ルール準拠:
 *   - フェーズは PHASE_ORDER の順で一方通行
 *   - 各フェーズの canProceed が true のときのみ次へ進める
 *   - 不正な遷移は no-op（コードレベルでガード）
 */
import { useCallback, useMemo, useState } from "react";
import {
  GamePhase,
  PHASE_LABEL,
  PHASE_ORDER,
  isYearAdvancingTransition,
  nextPhase,
} from "./GamePhase";
import { ChroniclePage } from "../phases/Chronicle/ChroniclePage";
import { GuildManagementPage } from "../phases/GuildManagement/GuildManagementPage";
import { BattalionFormationPage } from "../phases/BattalionFormation/BattalionFormationPage";
import { BattleSimulationPage } from "../phases/BattleSimulation/BattleSimulationPage";
import { JobManualOverlay } from "../components/JobManualOverlay";

/**
 * ゲーム全体の状態。M2 段階では最小限。
 * 後続マイルストーンで Brigade / Year / 戦闘ログ等を追加していく。
 */
export interface GameState {
  readonly year: number;
  readonly phase: GamePhase;
}

/**
 * 各フェーズが「次へ進める状態か」を子から親に通知する API。
 * 子コンポーネントは useEffect で setCanProceed(true/false) を呼ぶ。
 */
export interface PhaseHandle {
  readonly canProceed: boolean;
  readonly setCanProceed: (v: boolean) => void;
}

/**
 * フェーズインジケータ — 現在地と進行状況を視覚化。
 * data-testid: `phase-indicator-${phase}` を全フェーズに付与。
 */
function PhaseIndicator({ current }: { current: GamePhase }) {
  return (
    <ol
      data-testid="phase-indicator-root"
      className="phase-indicator"
    >
      {PHASE_ORDER.map((p) => {
        const isCurrent = p === current;
        const isPast =
          PHASE_ORDER.indexOf(p) < PHASE_ORDER.indexOf(current);
        const status = isCurrent ? "current" : isPast ? "past" : "future";
        return (
          <li
            key={p}
            data-testid={`phase-indicator-${p}`}
            data-status={status}
            className={`phase-step phase-step-${status}`}
          >
            {PHASE_LABEL[p]}
          </li>
        );
      })}
    </ol>
  );
}

/**
 * 次へ進むボタン（共通）。canProceed=false のときは必ず disabled。
 * data-testid: next-phase-button
 */
function NextPhaseButton({
  canProceed,
  currentPhase,
  onAdvance,
}: {
  canProceed: boolean;
  currentPhase: GamePhase;
  onAdvance: () => void;
}) {
  const nextLabel = PHASE_LABEL[nextPhase(currentPhase)];
  const isYearAdvance =
    currentPhase === "BATTLE_SIMULATION"; // 次は CHRONICLE = 新年
  return (
    <button
      type="button"
      data-testid="next-phase-button"
      data-can-proceed={canProceed}
      disabled={!canProceed}
      onClick={onAdvance}
      className="next-phase-button"
    >
      {isYearAdvance ? `次年を迎える（${nextLabel}）` : `次へ：${nextLabel}`}
    </button>
  );
}

/**
 * GameManager 本体。
 * - useState でフェーズ・年・canProceed を保持
 * - 子フェーズには PhaseHandle を渡して canProceed の更新を許可
 * - advance() で次フェーズへ遷移し、年送り時は year++ する
 */
export function GameManager() {
  const [state, setState] = useState<GameState>({
    year: 1,
    phase: "CHRONICLE",
  });
  const [canProceed, setCanProceed] = useState<boolean>(false);
  /**
   * ジョブマニュアル（ヘッダー常設の📖ボタンで開く全画面オーバーレイ）の表示状態。
   * ゲーム本編とは独立した補助ビューで、いつでも開閉できる。
   */
  const [isJobManualOpen, setIsJobManualOpen] = useState<boolean>(false);

  const handle: PhaseHandle = useMemo(
    () => ({ canProceed, setCanProceed }),
    [canProceed]
  );

  const advance = useCallback(() => {
    if (!canProceed) {
      // ★ instructions.md F-1: コードレベルガード（no-op）
      return;
    }
    setState((prev) => {
      const next = nextPhase(prev.phase);
      const yearDelta = isYearAdvancingTransition(prev.phase, next) ? 1 : 0;
      return {
        year: prev.year + yearDelta,
        phase: next,
      };
    });
    // フェーズ遷移時は次フェーズの canProceed を一旦 false にリセット
    setCanProceed(false);
  }, [canProceed]);

  return (
    <div data-testid="game-manager-root" className="game-manager">
      <header
        data-testid="game-manager-header"
        className="game-manager-header"
      >
        <h1 data-testid="game-manager-title">Chronicle Knights</h1>
        <div
          data-testid="game-manager-year"
          className="game-manager-year"
        >
          Year {state.year}
        </div>
        <PhaseIndicator current={state.phase} />
        {/* グローバルヘッダーから常時アクセスできる「ジョブ説明」ボタン。
            フェーズに関係なく、どの画面からでもジョブマニュアルを開ける。 */}
        <button
          type="button"
          data-testid="open-job-manual-button"
          onClick={() => setIsJobManualOpen(true)}
          className="open-job-manual-button"
          aria-label="ジョブ説明を開く"
          title="全 8 ジョブの推奨配置と能力をカタログ閲覧"
        >
          <span className="open-job-manual-button-icon">📖</span>
          <span className="open-job-manual-button-label">ジョブ説明</span>
        </button>
      </header>

      <main data-testid="game-manager-main" className="game-manager-main">
        {state.phase === "CHRONICLE" && (
          <ChroniclePage year={state.year} phaseHandle={handle} />
        )}
        {state.phase === "GUILD_MANAGEMENT" && (
          <GuildManagementPage year={state.year} phaseHandle={handle} />
        )}
        {state.phase === "BATTALION_FORMATION" && (
          <BattalionFormationPage year={state.year} phaseHandle={handle} />
        )}
        {state.phase === "BATTLE_SIMULATION" && (
          <BattleSimulationPage year={state.year} phaseHandle={handle} />
        )}
      </main>

      <footer
        data-testid="game-manager-footer"
        className="game-manager-footer"
      >
        <NextPhaseButton
          canProceed={canProceed}
          currentPhase={state.phase}
          onAdvance={advance}
        />
      </footer>

      {/* ジョブマニュアルオーバーレイ（ヘッダー📖ボタンから開く） */}
      {isJobManualOpen && (
        <JobManualOverlay onClose={() => setIsJobManualOpen(false)} />
      )}
    </div>
  );
}
