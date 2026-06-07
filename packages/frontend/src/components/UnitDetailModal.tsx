/**
 * UnitDetailModal — ユニット詳細プロファイル + 3×3 任命ミニグリッド
 *
 * 編成画面でベンチユニットをクリックした際に表示するモーダル。
 * - 全ステータス + 総合戦闘力
 * - ジョブのアビリティ説明（role/ability/usage/flavor）
 * - 3×3 ミニグリッドで「このマスに配置」を直接指示
 *   - 既に他ユニットが配置済みのマスは「(押し戻し: ◯◯)」と注釈
 *   - 既にこのユニットが配置済のマスは「現在配置中」と表示
 *
 * data-testid:
 *   - unit-detail-modal-root
 *   - unit-detail-modal-close-button
 *   - unit-detail-ability-{job}
 *   - formation-assign-btn-{row}-{col}
 */
import type { JSX } from "react";
import type { RosterUnit, SquadRow } from "../api/types";
import { formatJob, getJobAbility } from "../utils/job";
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

        {/* ─── ジョブ能力解説 ────────────────────────── */}
        {ability && (
          <div
            data-testid={`unit-detail-ability-${unit.job}`}
            className="unit-detail-ability-section"
          >
            <h3 data-testid="unit-detail-ability-title">
              ジョブ能力: {formatJob(unit.job)}
            </h3>
            <dl data-testid="unit-detail-ability-list" className="unit-detail-ability-list">
              <dt>役割</dt>
              <dd data-testid="unit-detail-ability-role">{ability.role}</dd>
              <dt>固有能力</dt>
              <dd data-testid="unit-detail-ability-text">{ability.ability}</dd>
              <dt>戦術的使い方</dt>
              <dd data-testid="unit-detail-ability-usage">{ability.usage}</dd>
              <dt>フレーバー</dt>
              <dd data-testid="unit-detail-ability-flavor" className="unit-detail-ability-flavor">
                「{ability.flavor}」
              </dd>
            </dl>
          </div>
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
