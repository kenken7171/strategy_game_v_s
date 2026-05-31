/**
 * ChroniclePage — フェーズ1: 年代記
 *
 * API 取得:
 *   GET /api/chronicle          年代記サマリー + 履歴
 *   GET /api/chronicle/preview  今年の敵プレビュー（±15%予測レンジ）
 *
 * 完了条件: 情報表示のみ → 常に canProceed=true（読込完了後）
 */
import { useEffect, useState } from "react";
import type { PhaseHandle } from "../../game/GameManager";
import { api } from "../../api/client";
import type {
  ChronicleResponse,
  EnemyPreviewResponse,
} from "../../api/types";

interface Props {
  year: number;
  phaseHandle: PhaseHandle;
}

export function ChroniclePage({ year, phaseHandle }: Props) {
  const [chronicle, setChronicle] = useState<ChronicleResponse | null>(null);
  const [preview, setPreview] = useState<EnemyPreviewResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      setLoading(true);
      const [c, p] = await Promise.all([
        api.getChronicle(),
        api.getEnemyPreview(),
      ]);
      setChronicle(c);
      setPreview(p);
      setLoading(false);
      phaseHandle.setCanProceed(true);
    })();
  }, [phaseHandle, year]);

  if (loading || !chronicle || !preview) {
    return (
      <section data-testid="chronicle-page-root" className="chronicle-page">
        <div
          data-testid="common-loading-spinner"
          className="common-loading-spinner"
        >
          ⏳ 年代記を読み込み中...
        </div>
      </section>
    );
  }

  return (
    <section data-testid="chronicle-page-root" className="chronicle-page">
      <h2 data-testid="chronicle-page-title">年代記 — Year {chronicle.year}</h2>

      <div data-testid="chronicle-summary-card" className="chronicle-summary-card">
        <h3 data-testid="chronicle-summary-title">本年の旅団状況</h3>
        <dl data-testid="chronicle-summary-stats" className="chronicle-stats">
          <dt data-testid="chronicle-stat-label-population">在籍人数</dt>
          <dd data-testid="chronicle-stat-value-population">
            {chronicle.brigadeSize} 名
          </dd>
          <dt data-testid="chronicle-stat-label-married-couples">結婚カップル</dt>
          <dd data-testid="chronicle-stat-value-married-couples">
            {chronicle.marriedCouples} 組
          </dd>
          <dt data-testid="chronicle-stat-label-descendants">継承者の数</dt>
          <dd data-testid="chronicle-stat-value-descendants">
            {chronicle.descendants} 名
          </dd>
        </dl>
      </div>

      <div
        data-testid="chronicle-enemy-preview-card"
        className="chronicle-enemy-preview-card"
      >
        <h3 data-testid="chronicle-enemy-preview-title">
          次の試練の門・予測（±15%）
        </h3>
        <dl
          data-testid="chronicle-enemy-preview-stats"
          className="chronicle-stats"
        >
          <dt data-testid="chronicle-enemy-stat-label-hp">HP</dt>
          <dd data-testid="chronicle-enemy-stat-value-hp">
            {preview.hp.min}〜{preview.hp.max}（基準 {Math.round(preview.hp.base)}）
          </dd>
          <dt data-testid="chronicle-enemy-stat-label-atk">攻撃</dt>
          <dd data-testid="chronicle-enemy-stat-value-atk">
            {preview.attack.min}〜{preview.attack.max}（基準 {Math.round(preview.attack.base)}）
          </dd>
          <dt data-testid="chronicle-enemy-stat-label-spd">スピード</dt>
          <dd data-testid="chronicle-enemy-stat-value-spd">
            {preview.speed.min}〜{preview.speed.max}（基準 {Math.round(preview.speed.base)}）
          </dd>
          <dt data-testid="chronicle-enemy-stat-label-count">敵数</dt>
          <dd data-testid="chronicle-enemy-stat-value-count">
            {preview.enemyCount} 体
          </dd>
        </dl>
      </div>

      <div data-testid="chronicle-history-section" className="chronicle-history">
        <h3 data-testid="chronicle-history-title">過去の出来事（直近50件）</h3>
        <ul
          data-testid="chronicle-history-list"
          className="chronicle-history-list"
        >
          {chronicle.history.length === 0 ? (
            <li data-testid="chronicle-history-empty" className="chronicle-history-empty">
              まだ歴史は語られていません
            </li>
          ) : (
            chronicle.history.slice().reverse().map((h, i) => (
              <li
                key={`${h.year}-${i}`}
                data-testid={`chronicle-history-item-${i}`}
                data-type={h.type}
                className="chronicle-history-item"
              >
                <span data-testid={`chronicle-history-year-${i}`} className="chronicle-history-year">
                  Y{h.year}
                </span>
                <span data-testid={`chronicle-history-text-${i}`}>{h.text}</span>
              </li>
            ))
          )}
        </ul>
      </div>

      <p data-testid="chronicle-page-hint" className="phase-hint">
        準備ができたら「次へ：人事」へ進んでください。
      </p>
    </section>
  );
}
