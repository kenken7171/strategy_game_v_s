/**
 * BattleSimulationPage — フェーズ4: 戦闘
 *
 * canProceed の条件:
 *   - 戦闘実行が完了していること（pending=false かつ 結果が確定）
 *
 * 「戦闘を開始する」→ シミュレーション実行（M3 で BattleSimulator 接続）→
 * 結果表示 → 「次年へ進む」ボタンで CHRONICLE に戻り年送り。
 */
import { useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

type BattleStatus = "ready" | "running" | "done";

interface MockBattleResult {
  winner: "Allies" | "Enemies" | "Draw";
  turns: number;
  mvpJob: string;
  survivors: number;
  enemySurvivors: number;
}

const MOCK_RESULT: MockBattleResult = {
  winner: "Allies",
  turns: 8,
  mvpJob: "iron_wall_knight",
  survivors: 6,
  enemySurvivors: 0,
};

export function BattleSimulationPage({ year, phaseHandle }: Props) {
  const [status, setStatus] = useState<BattleStatus>("ready");
  const [result, setResult] = useState<MockBattleResult | null>(null);

  const canProceed = status === "done";

  useEffect(() => {
    phaseHandle.setCanProceed(canProceed);
  }, [canProceed, phaseHandle]);

  const startBattle = () => {
    setStatus("running");
    // M2: モックタイマーで完了をシミュレート
    setTimeout(() => {
      setResult(MOCK_RESULT);
      setStatus("done");
    }, 800);
  };

  // 敵ステータスの「±15% 予測レンジ」表示（instructions.md B-3 ルール）
  const baseHp = 150 + year * 5;
  const baseAtk = 30 + year * 0.6;
  const baseSpd = 100 + year * 0.6;
  const fmtRange = (base: number) =>
    `${Math.round(base * 0.85)}〜${Math.round(base * 1.15)}`;

  return (
    <section
      data-testid="battle-simulation-page-root"
      className="battle-simulation-page"
    >
      <h2 data-testid="battle-simulation-page-title">
        戦闘フェーズ — Year {year}
      </h2>

      <div
        data-testid="battle-enemy-preview-card"
        className="battle-enemy-preview-card"
      >
        <h3 data-testid="battle-enemy-preview-title">試練の門・敵情報</h3>
        <dl
          data-testid="battle-enemy-preview-stats"
          className="battle-enemy-preview-stats"
        >
          <dt data-testid="battle-enemy-stat-label-hp">HP（±15%予測）</dt>
          <dd data-testid="battle-enemy-stat-value-hp">{fmtRange(baseHp)}</dd>
          <dt data-testid="battle-enemy-stat-label-atk">ATK（±15%予測）</dt>
          <dd data-testid="battle-enemy-stat-value-atk">{fmtRange(baseAtk)}</dd>
          <dt data-testid="battle-enemy-stat-label-spd">SPD（±15%予測）</dt>
          <dd data-testid="battle-enemy-stat-value-spd">{fmtRange(baseSpd)}</dd>
          <dt data-testid="battle-enemy-stat-label-count">敵数</dt>
          <dd data-testid="battle-enemy-stat-value-count">10 体</dd>
        </dl>
      </div>

      {status === "ready" && (
        <div
          data-testid="battle-start-section"
          className="battle-start-section"
        >
          <p data-testid="battle-start-hint" className="phase-hint">
            準備が整ったら戦闘を開始してください。
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
        <div
          data-testid="battle-running-section"
          className="battle-running-section"
        >
          <p data-testid="battle-running-message">⏳ 戦闘進行中...</p>
        </div>
      )}

      {status === "done" && result && (
        <div
          data-testid="battle-result-section"
          className="battle-result-section"
          data-winner={result.winner}
        >
          <h3 data-testid="battle-result-title">戦闘結果</h3>
          <dl
            data-testid="battle-result-stats"
            className="battle-result-stats"
          >
            <dt data-testid="battle-result-label-winner">結果</dt>
            <dd data-testid="battle-result-value-winner">{result.winner}</dd>
            <dt data-testid="battle-result-label-turns">ターン数</dt>
            <dd data-testid="battle-result-value-turns">{result.turns}</dd>
            <dt data-testid="battle-result-label-mvp">MVP</dt>
            <dd data-testid="battle-result-value-mvp">{result.mvpJob}</dd>
            <dt data-testid="battle-result-label-survivors">味方生存</dt>
            <dd data-testid="battle-result-value-survivors">
              {result.survivors} / 9
            </dd>
            <dt data-testid="battle-result-label-enemy-survivors">敵生存</dt>
            <dd data-testid="battle-result-value-enemy-survivors">
              {result.enemySurvivors} / 10
            </dd>
          </dl>
          <p data-testid="battle-result-hint" className="phase-hint">
            次年へ進む準備が整いました。
          </p>
        </div>
      )}
    </section>
  );
}
