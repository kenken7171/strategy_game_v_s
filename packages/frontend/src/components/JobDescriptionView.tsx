/**
 * JobDescriptionView — 1 つのジョブ説明を描画する純粋コンポーネント。
 *
 * 元々は UnitDetailModal にインラインで書かれていたジョブ説明セクションを
 * 抽出し、以下 2 箇所から共通利用するための専用 View にしたもの:
 *   - UnitDetailModal （特定ユニットの詳細表示）
 *   - JobManualOverlay（全 8 ジョブをカタログ閲覧）
 *
 * 表示要素（順番固定）:
 *   1. 📜 役割 (role)
 *   2. ⚙ 固有パッシブ（構造化リスト: 大隊総守護力/分隊守護力/突撃号令/治癒/二の矢）
 *   3. 🗺 推奨配置（ミニ V 字図 + ヘッドライン + 効果範囲）
 *   4. 💡 戦術的使い方 (usage)
 *   5. フレーバー (flavor, blockquote)
 *
 * data-testid:
 *   - unit-detail-job-description-passives / -passive-{bdf|sdf|ab|hl|special-*}
 *   - unit-detail-job-formation-guide / -row-{row} / -headline / -effect-range
 *   - unit-detail-ability-{role|text|usage|flavor}
 *   (※ 既存 testid 規約を破壊しないため "unit-detail-" 接頭辞をそのまま維持)
 */
import type { JSX } from "react";
import type { SquadRow } from "../api/types";
import {
  formatJob,
  getJobAbility,
  getJobFormationGuide,
  explainJobPassives,
} from "../utils/job";

const ROWS: SquadRow[] = ["FRONT", "REAR-L", "REAR-R"];
const ROW_LABEL: Record<SquadRow, string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

/** 効果種別ごとに、推奨配置マスに表示するグリフ。 */
const EFFECT_KIND_GLYPH: Record<
  "buff" | "heal" | "defend" | "attack",
  string
> = {
  buff:   "⚡",
  heal:   "💚",
  defend: "🛡",
  attack: "⚔",
};

interface Props {
  /** 表示するジョブ ID。null/未知ジョブのときはフォールバック描画 */
  jobId: string | null | undefined;
  /**
   * data-testid のプレフィックス。デフォルトは "unit-detail-job-" で
   * 既存規約を維持。Job Manual 等の独立画面で別 namespace にしたい場合に
   * 上書きできる。
   */
  testIdPrefix?: string;
}

export function JobDescriptionView({
  jobId,
  testIdPrefix = "unit-detail-job",
}: Props): JSX.Element | null {
  const ability = getJobAbility(jobId);
  if (!ability) return null;
  const guide = getJobFormationGuide(jobId);
  const passives = explainJobPassives(jobId);

  return (
    <div
      data-testid={`${testIdPrefix}-description-body`}
      className="unit-detail-job-description-body"
    >
      {/* 既存 testid `unit-detail-ability-{job}` をラッパとして維持 */}
      <div data-testid={`unit-detail-ability-${jobId}`}>
        {/* 📜 役割 */}
        <div className="unit-detail-job-desc-row unit-detail-job-desc-row-role">
          <span className="unit-detail-job-desc-label">📜 役割</span>
          <p
            data-testid="unit-detail-ability-role"
            className="unit-detail-job-desc-value"
          >
            {ability.role}
          </p>
        </div>

        {/* ⚙ 固有パッシブ */}
        <div
          data-testid="unit-detail-job-description-passives"
          className="unit-detail-job-desc-row unit-detail-job-desc-row-passives"
        >
          <span className="unit-detail-job-desc-label">⚙ 固有パッシブ</span>
          <p
            data-testid="unit-detail-ability-text"
            className="unit-detail-job-passives-summary"
          >
            {ability.ability}
          </p>
          {passives.length === 0 ? (
            <p
              data-testid="unit-detail-job-description-passives-empty"
              className="unit-detail-job-passives-empty"
            >
              — 数値プロパティ上のパッシブはなし（純戦闘ステータスで機能）
            </p>
          ) : (
            <ul className="unit-detail-job-passive-list">
              {passives.map((p) => (
                <li
                  key={p.key}
                  data-testid={`unit-detail-job-description-passive-${p.key}`}
                  data-passive-key={p.key}
                  className="unit-detail-job-passive-item"
                >
                  <div className="unit-detail-job-passive-head">
                    <span className="unit-detail-job-passive-label">
                      {p.label}
                    </span>
                    {p.value !== null && (
                      <span className="unit-detail-job-passive-value">
                        値: <strong>{p.value}</strong>
                      </span>
                    )}
                  </div>
                  <div className="unit-detail-job-passive-scope">
                    🎯 {p.scope}
                  </div>
                  <div className="unit-detail-job-passive-desc">
                    {p.description}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        {/* 🗺 推奨配置 + ミニ V 字図 */}
        {guide && (
          <div
            data-testid="unit-detail-job-formation-guide"
            data-effect-kind={guide.effectKind}
            className="unit-detail-job-desc-row unit-detail-job-desc-row-guide"
          >
            <span className="unit-detail-job-desc-label">🗺 推奨配置</span>

            <div
              className="formation-v-shape formation-v-shape--guide"
              data-effect-kind={guide.effectKind}
            >
              {ROWS.map((row) => {
                const squadModifier =
                  row === "FRONT" ? "front"
                  : row === "REAR-L" ? "rear-l"
                  : "rear-r";
                const isRecommended = guide.recommendedRows.includes(row);
                const isInEffectRange = guide.effectRange.includes(row);
                return (
                  <div
                    key={row}
                    data-testid={`unit-detail-job-formation-guide-row-${row}`}
                    data-row={row}
                    data-recommended={isRecommended}
                    data-in-effect-range={isInEffectRange}
                    className={`formation-v-squad formation-v-squad-${squadModifier}`}
                  >
                    <div className="formation-v-squad-header">
                      {row === "FRONT" ? "⚔ " : "🛡 "}
                      {ROW_LABEL[row]}
                      {isRecommended && (
                        <span className="unit-detail-job-formation-guide-rec-badge">
                          ✓ 推奨
                        </span>
                      )}
                    </div>
                    <div className="formation-v-slot-row">
                      {[0, 1, 2].map((col) => (
                        <div
                          key={col}
                          className="formation-v-slot"
                          data-recommended={isRecommended}
                        >
                          {isRecommended
                            ? EFFECT_KIND_GLYPH[guide.effectKind]
                            : "□"}
                        </div>
                      ))}
                    </div>
                  </div>
                );
              })}
            </div>

            <div
              data-testid="unit-detail-job-formation-guide-headline"
              data-effect-kind={guide.effectKind}
              className="unit-detail-job-formation-guide-headline"
            >
              [{guide.headline}]
            </div>
            <div
              data-testid="unit-detail-job-formation-guide-effect-range"
              className="unit-detail-job-formation-guide-effect-range"
            >
              🎯 効果範囲: {guide.effectRangeNote}
            </div>
          </div>
        )}

        {/* 💡 戦術的使い方 */}
        <div className="unit-detail-job-desc-row unit-detail-job-desc-row-usage">
          <span className="unit-detail-job-desc-label">💡 戦術的使い方</span>
          <p
            data-testid="unit-detail-ability-usage"
            className="unit-detail-job-desc-value"
          >
            {ability.usage}
          </p>
        </div>

        {/* フレーバー */}
        <blockquote
          data-testid="unit-detail-ability-flavor"
          className="unit-detail-ability-flavor unit-detail-job-flavor-quote"
        >
          「{ability.flavor}」
        </blockquote>
      </div>
    </div>
  );
}

/**
 * ジョブ説明セクションの「ヘッダ部 + 本文」をひとまとめにしたコンポーネント。
 * 既存 UnitDetailModal で使われていた `<section> + <h3> + <body>` 構造を
 * そのまま再現し、testid を完全保持。
 */
export function JobDescriptionSection({
  jobId,
  testIdPrefix = "unit-detail-job",
}: Props): JSX.Element | null {
  const ability = getJobAbility(jobId);
  if (!ability) return null;
  return (
    <section
      data-testid={`${testIdPrefix}-description-section`}
      className="unit-detail-job-description-section"
    >
      <h3
        data-testid={`${testIdPrefix}-description-header`}
        className="unit-detail-job-description-header"
      >
        <span className="unit-detail-job-description-header-icon">📖</span>
        <span className="unit-detail-job-description-header-title">
          ジョブ説明
        </span>
        <span className="unit-detail-job-description-header-jobname">
          {formatJob(jobId)}
        </span>
        <span className="unit-detail-job-description-header-jobid">
          ({jobId})
        </span>
      </h3>
      <JobDescriptionView jobId={jobId} testIdPrefix={testIdPrefix} />
    </section>
  );
}
