/**
 * UnitIcon — ユニットの 16-bit ドット絵アイコンを表示する共通コンポーネント
 *
 * 設計方針（M5 リファクタ）:
 *   - 自分自身は固定サイズを持たない「親要素フィット型」
 *   - <img> は CSS で width: 100%; height: 100%; object-fit: contain; を適用
 *   - 呼び出し側は親に `.unit-icon-slot` + サイズ修飾子（xs/sm/md/lg/xl）を付けて
 *     表示寸法を制御する（global.css 参照）
 *
 * パス規則: /image/{jobId}/{gender}.png
 *   例: /image/iron_wall_knight/male.png
 *
 * 16-bit ドット絵がぼやけないよう、image-rendering: pixelated を
 * CSS で確実に適用する（unit-icon クラス参照）。
 *
 * 画像が見つからない場合は <onError> で broken state にし、
 * フォールバックの円形＋頭文字アイコンを表示する。
 */
import { useState } from "react";
import { formatJob } from "../utils/job";

/** 全 8 ジョブ ID（型レベルで限定したい場合に参照） */
export const SUPPORTED_JOB_IDS = [
  "iron_wall_knight",
  "tactician",
  "medic",
  "sniper",
  "sorcerer",
  "standard_bearer",
  "heavy_infantry",
  "scout",
] as const;
export type SupportedJobId = (typeof SUPPORTED_JOB_IDS)[number];

export type IconGender = "Male" | "Female" | "male" | "female";

/**
 * jobId + gender からアセットパスを生成する。
 *
 * - 未知ジョブ・gender null の場合は null を返す（呼び出し側がフォールバックUI表示）
 * - パス規則: /image/{jobId}/{gender}.png
 * - gender は API レスポンスの "Male"|"Female" を小文字化して合わせる
 */
export function getJobIconPath(
  jobId: string | null | undefined,
  gender: IconGender | null | undefined
): string | null {
  if (!jobId || !gender) return null;
  if (!SUPPORTED_JOB_IDS.includes(jobId as SupportedJobId)) return null;
  const g = gender.toString().toLowerCase();
  if (g !== "male" && g !== "female") return null;
  return `/image/${jobId}/${g}.png`;
}

interface Props {
  /** ジョブID (例: "iron_wall_knight") */
  jobId: string | null | undefined;
  /** 性別 ("Male" | "Female") */
  gender: IconGender | null | undefined;
  /** alt 属性に使う名前（無ければジョブ日本語名） */
  altName?: string;
  /** 追加のクラス名（任意） */
  className?: string;
  /** data-testid に付与する suffix（カード内で複数表示するときに ID と組み合わせる） */
  testIdSuffix?: string;
}

/**
 * 親要素フィット型のユニットアイコン。
 * 呼び出し側は `<span class="unit-icon-slot unit-icon-slot-{xs|sm|md|lg|xl}">` などで
 * 親に寸法を与えた上で本コンポーネントを置く。
 */
export function UnitIcon({
  jobId, gender, altName, className, testIdSuffix,
}: Props) {
  const path = getJobIconPath(jobId, gender);
  const [broken, setBroken] = useState(false);
  const alt = altName ?? formatJob(jobId);
  const testid = testIdSuffix
    ? `unit-icon-${testIdSuffix}`
    : "unit-icon";

  if (!path || broken) {
    // フォールバック: 円形のジョブ頭文字（親 100% フィット）
    const initial = formatJob(jobId).slice(0, 1) || "?";
    return (
      <span
        data-testid={`${testid}-fallback`}
        data-job={jobId ?? "none"}
        data-gender={gender ?? "none"}
        className={`unit-icon-fallback ${className ?? ""}`}
        aria-label={alt}
        title={alt}
      >
        {initial}
      </span>
    );
  }

  return (
    <img
      src={path}
      alt={alt}
      title={alt}
      data-testid={testid}
      data-job={jobId}
      data-gender={gender}
      className={`unit-icon ${className ?? ""}`}
      onError={() => setBroken(true)}
      draggable={false}
    />
  );
}
