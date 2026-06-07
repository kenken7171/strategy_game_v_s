/**
 * JobManualOverlay — グローバルヘッダーから常時アクセス可能な「ジョブマニュアル」
 *
 * 「📖 ジョブ説明」ボタンから開かれる全画面オーバーレイ。特定ユニットを
 * 選択することなく、全 8 ジョブをカタログとして閲覧できる。
 *
 * 構造:
 *   - 左サイドバー: 全 8 ジョブの一覧（クリックで切替）
 *   - 右ペイン: 選択中ジョブの JobDescriptionView を描画
 *
 * data-testid:
 *   - job-manual-overlay-backdrop
 *   - job-manual-overlay-root
 *   - job-manual-close-button
 *   - job-manual-title
 *   - job-manual-sidebar / -item-${jobId}
 *   - job-manual-main-panel
 */
import { useState, type JSX } from "react";
import { JOB_JP_ENTRIES, formatJob } from "../utils/job";
import { JobDescriptionView } from "./JobDescriptionView";
import { UnitIcon } from "./UnitIcon";

interface Props {
  /** 初期表示するジョブ ID（既定: 配列先頭） */
  initialJobId?: string;
  /** 閉じるコールバック */
  onClose: () => void;
}

export function JobManualOverlay({ initialJobId, onClose }: Props): JSX.Element {
  const defaultJobId = initialJobId ?? JOB_JP_ENTRIES[0][0];
  const [activeJobId, setActiveJobId] = useState<string>(defaultJobId);

  return (
    <div
      data-testid="job-manual-overlay-backdrop"
      className="job-manual-overlay-backdrop"
      onClick={onClose}
    >
      <div
        data-testid="job-manual-overlay-root"
        className="job-manual-overlay-root"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
      >
        <button
          type="button"
          data-testid="job-manual-close-button"
          onClick={onClose}
          className="job-manual-close-button"
          aria-label="閉じる"
        >
          ✕
        </button>

        <header className="job-manual-header">
          <h2
            data-testid="job-manual-title"
            className="job-manual-title"
          >
            <span className="job-manual-title-icon">📖</span>
            ジョブマニュアル
            <span className="job-manual-title-subtitle">
              — 全 8 ジョブの推奨配置とパッシブ能力
            </span>
          </h2>
        </header>

        <div className="job-manual-body">
          {/* 左サイドバー: 全 8 ジョブの一覧 */}
          <nav
            data-testid="job-manual-sidebar"
            className="job-manual-sidebar"
            aria-label="ジョブ一覧"
          >
            <ul className="job-manual-sidebar-list">
              {JOB_JP_ENTRIES.map(([jobId, jobJp]) => {
                const isActive = jobId === activeJobId;
                return (
                  <li
                    key={jobId}
                    data-testid={`job-manual-sidebar-item-${jobId}`}
                    data-active={isActive}
                    className={`job-manual-sidebar-item ${isActive ? "active" : ""}`}
                  >
                    <button
                      type="button"
                      onClick={() => setActiveJobId(jobId)}
                      className="job-manual-sidebar-button"
                      aria-current={isActive ? "page" : undefined}
                    >
                      {/* ジョブアイコン（性別 male 固定でカタログ風に） */}
                      <span
                        className="unit-icon-slot unit-icon-slot-sm"
                        data-testid={`job-manual-sidebar-icon-slot-${jobId}`}
                      >
                        <UnitIcon
                          jobId={jobId}
                          gender="Male"
                          altName={jobJp}
                          testIdSuffix={`job-manual-${jobId}`}
                        />
                      </span>
                      <span className="job-manual-sidebar-item-jp">{jobJp}</span>
                      <span className="job-manual-sidebar-item-id">{jobId}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </nav>

          {/* 右ペイン: 選択中ジョブの説明 */}
          <main
            data-testid="job-manual-main-panel"
            className="job-manual-main-panel"
          >
            <div
              data-testid={`job-manual-active-job-${activeJobId}`}
              className="job-manual-active-job-header"
            >
              <span className="job-manual-active-job-name">
                {formatJob(activeJobId)}
              </span>
              <span className="job-manual-active-job-id">
                ({activeJobId})
              </span>
            </div>
            <JobDescriptionView jobId={activeJobId} />
          </main>
        </div>
      </div>
    </div>
  );
}
