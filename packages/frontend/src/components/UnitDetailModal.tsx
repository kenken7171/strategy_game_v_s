/**
 * UnitDetailModal — ユニット詳細プロファイル + 3×3 任命ミニグリッド
 *
 * 編成画面でベンチユニットをクリックした際に表示するモーダル。
 *
 * セクション構成:
 *   1. ヘッダ（アイコン + 名前 + ジョブバッジ + 性別/出自/年齢）
 *   2. 全ステータス + 血統情報
 *   3. ジョブ説明（独立ヘッダー）
 *      - 役割 (role)
 *      - 固有パッシブ（構造化リスト: BDF/SDF/AB/HL + 特殊行）
 *      - 推奨配置ガイド（ミニ V 字図 + 推奨マスをハイライト）
 *      - 戦術的使い方 (usage)
 *      - フレーバー (flavor)
 *   4. 任命ミニグリッド（V 字配置でマスを指定）
 *
 * data-testid:
 *   - unit-detail-modal-root / close-button
 *   - unit-detail-header / stats-section / assign-section
 *   - unit-detail-job-description-section (NEW)
 *   - unit-detail-job-description-header (NEW)
 *   - unit-detail-job-description-passive-{key} (NEW per BDF/SDF/AB/HL/special-*)
 *   - unit-detail-job-formation-guide-row-{row} (NEW with data-recommended)
 *   - unit-detail-ability-{job} / -role / -text / -usage / -flavor （既存維持）
 *   - formation-assign-btn-{row}-{col}
 */
import type { JSX } from "react";
import type { RosterUnit, SquadRow } from "../api/types";
import {
  formatJob,
  getJobAbility,
  getJobFormationGuide,
  explainJobPassives,
} from "../utils/job";
import { UnitIcon } from "./UnitIcon";

interface Props {
  unit: RosterUnit;
  grid: ReadonlyArray<{ row: SquadRow; col: number; unitId: string | null }>;
  unitsById: Map<string, RosterUnit>;
  onAssign: (row: SquadRow, col: number) => void;
  onClose: () => void;
}

const ROWS: SquadRow[] = ["FRONT", "REAR-L", "REAR-R"];
const ROW_LABEL: Record<SquadRow, string> = {
  FRONT: "前衛",
  "REAR-L": "後衛-左",
  "REAR-R": "後衛-右",
};

/**
 * ジョブの効果種別ごとに、推奨配置マスに表示するアスキー風グリフ。
 * 配色（青/金/緑/朱）と合わせて、視覚的に「これは何の系統か」を伝える。
 */
const EFFECT_KIND_GLYPH: Record<
  "buff" | "heal" | "defend" | "attack",
  string
> = {
  buff:   "✨",
  heal:   "💚",
  defend: "🛡",
  attack: "⚔",
};

export function UnitDetailModal({
  unit, grid, unitsById, onAssign, onClose,
}: Props): JSX.Element {
  const ability = getJobAbility(unit.job);
  return (
    <div
      data-testid="unit-detail-modal-backdrop"
      className="unit-detail-modal-backdrop"
      onClick={onClose}
    >
      <div
        data-testid="unit-detail-modal-root"
        data-unit-id={unit.id}
        className="unit-detail-modal-root"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
      >
        <button
          type="button"
          data-testid="unit-detail-modal-close-button"
          onClick={onClose}
          className="unit-detail-modal-close-button"
          aria-label="閉じる"
        >
          ✕
        </button>

        {/* ─── ヘッダ ─────────────────────────────── */}
        <div data-testid="unit-detail-header" className="unit-detail-header">
          <span
            data-testid={`unit-detail-icon-slot-${unit.id}`}
            className="unit-icon-slot unit-icon-slot-xl"
          >
            <UnitIcon
              jobId={unit.job}
              gender={unit.gender}
              altName={unit.name}
              testIdSuffix={`detail-${unit.id}`}
            />
          </span>
          <span data-testid="unit-detail-job-badge" className="unit-detail-job-badge">
            {formatJob(unit.job)}
          </span>
          <h2 data-testid="unit-detail-name" className="unit-detail-name">
            {unit.name}
          </h2>
          <span data-testid="unit-detail-gender" className="unit-detail-gender">
            {unit.gender === "Male" ? "♂" : "♀"}
          </span>
          <span data-testid="unit-detail-origin" className="unit-detail-origin">
            {unit.origin}
          </span>
          <span data-testid="unit-detail-age" className="unit-detail-age">
            {unit.age}歳
          </span>
        </div>

        {/* ─── 全ステータス ─────────────────────────── */}
        <div data-testid="unit-detail-stats-section" className="unit-detail-stats-section">
          <h3 data-testid="unit-detail-stats-title">全ステータス</h3>
          <dl data-testid="unit-detail-stats-grid" className="unit-detail-stats-grid">
            <dt>HP</dt>
            <dd data-testid="unit-detail-stat-hp">{unit.maxHp}</dd>
            <dt>ATK (FA / RA)</dt>
            <dd data-testid="unit-detail-stat-attack">
              {unit.frontAttack} / {unit.rearAttack}
              <span className="unit-detail-stat-effective"> (実効 {unit.attack})</span>
            </dd>
            <dt>SPD</dt>
            <dd data-testid="unit-detail-stat-speed">{unit.speed}</dd>
            <dt>遺伝 baseStr</dt>
            <dd data-testid="unit-detail-stat-base-strength">{unit.baseStrength}</dd>
            <dt>growthFactor</dt>
            <dd data-testid="unit-detail-stat-growth">{(unit.growthFactor * 100).toFixed(0)}%</dd>
          </dl>
          <div
            data-testid="unit-detail-total-rating"
            className="unit-detail-total-rating"
          >
            総合的な強さ <strong>{unit.totalRating}</strong>
          </div>
          <div data-testid="unit-detail-lineage-section" className="unit-detail-lineage-section">
            {unit.isMarried && (
              <span data-testid="unit-detail-married-badge">💍 既婚</span>
            )}
            {unit.parents && (
              <span data-testid="unit-detail-heir-badge">🩸 継承者</span>
            )}
            {unit.descendantCount > 0 && (
              <span data-testid="unit-detail-descendants-badge">
                👶 子孫 {unit.descendantCount} 名
              </span>
            )}
            {!unit.isMarried && !unit.parents && unit.descendantCount === 0 && (
              <span data-testid="unit-detail-no-lineage" className="unit-detail-no-lineage">
                — 血統情報なし
              </span>
            )}
          </div>
        </div>

        {/* ─── ジョブ説明（独立セクション） ──────────────
              ユニット個人テキスト（名前/血統等）とジョブ固有スキル説明を
              明確に分離するため、ここから下を独立した「ジョブ説明」ヘッダ
              として扱う。 */}
        {ability && (
          <section
            data-testid="unit-detail-job-description-section"
            className="unit-detail-job-description-section"
          >
            <h3
              data-testid="unit-detail-job-description-header"
              className="unit-detail-job-description-header"
            >
              <span className="unit-detail-job-description-header-icon">📖</span>
              <span className="unit-detail-job-description-header-title">
                ジョブ説明
              </span>
              <span className="unit-detail-job-description-header-jobname">
                {formatJob(unit.job)}
              </span>
              <span className="unit-detail-job-description-header-jobid">
                ({unit.job})
              </span>
            </h3>

            {/* 既存 testid `unit-detail-ability-{job}` をラッパに維持 */}
            <div
              data-testid={`unit-detail-ability-${unit.job}`}
              className="unit-detail-job-description-body"
            >
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
                {(() => {
                  const passives = explainJobPassives(unit.job);
                  if (passives.length === 0) {
                    return (
                      <p
                        data-testid="unit-detail-job-description-passives-empty"
                        className="unit-detail-job-passives-empty"
                      >
                        — 数値プロパティ上のパッシブはなし（純戦闘ステータスで機能）
                      </p>
                    );
                  }
                  return (
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
                  );
                })()}
              </div>

              {/* 🗺 推奨配置 + ミニ V 字図 */}
              {(() => {
                const guide = getJobFormationGuide(unit.job);
                if (!guide) return null;
                return (
                  <div
                    data-testid="unit-detail-job-formation-guide"
                    data-effect-kind={guide.effectKind}
                    className="unit-detail-job-desc-row unit-detail-job-desc-row-guide"
                  >
                    <span className="unit-detail-job-desc-label">🗺 推奨配置</span>

                    {/* 読み取り専用 V 字図（guide variant） */}
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
                );
              })()}

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

              {/* フレーバー（ジョブアーキタイプの一言） */}
              <blockquote
                data-testid="unit-detail-ability-flavor"
                className="unit-detail-ability-flavor unit-detail-job-flavor-quote"
              >
                「{ability.flavor}」
              </blockquote>
            </div>
          </section>
        )}

        {/* ─── 任命ミニグリッド ──────────────────────── */}
        <div data-testid="unit-detail-assign-section" className="unit-detail-assign-section">
          <h3 data-testid="unit-detail-assign-title">配置先を指定</h3>
          <p data-testid="unit-detail-assign-hint" className="unit-detail-assign-hint">
            下記のマスをクリックすると、選択中のユニットがそこにハメ込まれます
            （既に誰かいる場合は自動でベンチに押し戻されます）
          </p>
          <div
            data-testid="unit-detail-mini-grid"
            className="unit-detail-mini-grid formation-v-shape formation-v-shape--mini"
          >
            {ROWS.map((row) => {
              const squadModifier =
                row === "FRONT" ? "front"
                : row === "REAR-L" ? "rear-l"
                : "rear-r";
              return (
                <div
                  key={row}
                  data-testid={`unit-detail-mini-grid-row-${row}`}
                  data-row={row}
                  className={`unit-detail-mini-grid-row formation-v-squad formation-v-squad-${squadModifier}`}
                >
                  <div
                    data-testid={`unit-detail-mini-grid-row-label-${row}`}
                    className="unit-detail-mini-grid-row-label formation-v-squad-header"
                  >
                    {row === "FRONT" ? "⚔ " : "🛡 "}
                    {ROW_LABEL[row]}
                  </div>
                  <div
                    data-testid={`unit-detail-mini-grid-slot-row-${row}`}
                    className="formation-v-slot-row"
                  >
                    {[0, 1, 2].map((col) => {
                      const cell = grid.find((g) => g.row === row && g.col === col);
                      const occupantId = cell?.unitId ?? null;
                      const occupant = occupantId ? unitsById.get(occupantId) : null;
                      const isSelf = occupantId === unit.id;
                      return (
                        <button
                          key={col}
                          type="button"
                          data-testid={`formation-assign-btn-${row}-${col}`}
                          data-occupied={!!occupant && !isSelf}
                          data-self={isSelf}
                          onClick={() => onAssign(row, col)}
                          className={`formation-v-slot unit-detail-mini-grid-btn ${
                            isSelf ? "self" : occupant ? "occupied" : "empty"
                          }`}
                        >
                          <span className="unit-detail-mini-grid-slot-label">
                            スロット{col + 1}
                          </span>
                          {isSelf ? (
                            <span data-testid={`formation-assign-btn-self-mark-${row}-${col}`}>
                              ✓ 現在配置中
                            </span>
                          ) : occupant ? (
                            <span
                              data-testid={`formation-assign-btn-occupant-${row}-${col}`}
                              className="unit-detail-mini-grid-occupant"
                            >
                              <span
                                data-testid={`formation-assign-btn-occupant-icon-slot-${row}-${col}`}
                                className="unit-icon-slot unit-icon-slot-sm"
                              >
                                <UnitIcon
                                  jobId={occupant.job}
                                  gender={occupant.gender}
                                  altName={occupant.name}
                                  testIdSuffix={`assign-occupant-${row}-${col}`}
                                />
                              </span>
                              {occupant.name} を押し戻す
                            </span>
                          ) : (
                            <span>空きマス</span>
                          )}
                        </button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </div>
  );
}
