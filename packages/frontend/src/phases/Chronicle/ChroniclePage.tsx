/**
 * ChroniclePage — フェーズ1: 年代記（年初の状況確認）
 *
 * UI: 現在年・前年の戦闘ログ・家系図サマリーを表示。
 * 「次へ進む」は無条件で可（情報表示のみ）。
 *
 * data-testid 規約: instructions.md F-2 準拠
 */
import { useEffect } from "react";
import type { PhaseHandle } from "../../game/GameManager";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

export function ChroniclePage({ year, phaseHandle }: Props) {
  // CHRONICLE フェーズは情報表示のみ → 常に canProceed=true
  useEffect(() => {
    phaseHandle.setCanProceed(true);
  }, [phaseHandle]);

  return (
    <section data-testid="chronicle-page-root" className="chronicle-page">
      <h2 data-testid="chronicle-page-title">年代記 — Year {year}</h2>

      <div
        data-testid="chronicle-summary-card"
        className="chronicle-summary-card"
      >
        <h3 data-testid="chronicle-summary-title">本年の旅団状況</h3>
        <dl data-testid="chronicle-summary-stats" className="chronicle-stats">
          <dt data-testid="chronicle-stat-label-population">在籍人数</dt>
          <dd data-testid="chronicle-stat-value-population">— 名</dd>
          <dt data-testid="chronicle-stat-label-married-couples">結婚カップル</dt>
          <dd data-testid="chronicle-stat-value-married-couples">— 組</dd>
          <dt data-testid="chronicle-stat-label-descendants">継承者の数</dt>
          <dd data-testid="chronicle-stat-value-descendants">— 名</dd>
        </dl>
      </div>

      <div
        data-testid="chronicle-history-section"
        className="chronicle-history"
      >
        <h3 data-testid="chronicle-history-title">前年までの出来事</h3>
        <ul
          data-testid="chronicle-history-list"
          className="chronicle-history-list"
        >
          <li
            data-testid="chronicle-history-empty"
            className="chronicle-history-empty"
          >
            まだ歴史は語られていません（M2 暫定）
          </li>
        </ul>
      </div>

      <p data-testid="chronicle-page-hint" className="phase-hint">
        準備ができたら「次へ：人事」へ進んでください。
      </p>
    </section>
  );
}
