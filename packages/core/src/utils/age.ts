/**
 * ユニットの年齢ランダム化ヘルパー
 *
 * 新人 : BASE_PEAK_START_AGE ±3, BASE_PEAK_END_AGE ±3
 * 子供 : 両親の peakStartAge / peakEndAge 平均 ±1
 *
 * いずれも `peakStartAge < peakEndAge` を必ず満たすようガードする。
 * Unit のコンストラクタは純粋（RNG 非依存）に保つため、個体差の決定は
 * 生成側で本ヘルパーを呼んで Unit に渡す方針。
 */
import { CHRONICLE_CONFIG } from "../config/ChronicleConfig";

export interface PeakAges {
  readonly peakStartAge: number;
  readonly peakEndAge: number;
}

/** ±delta の整数オフセット（一様分布、両端含む） */
function rollOffset(rng: () => number, delta: number): number {
  // 例: delta=3 → 0..6 のランダム → -3..+3
  const span = delta * 2 + 1;
  return Math.floor(rng() * span) - delta;
}

/**
 * 新人用: BASE_PEAK_START_AGE / BASE_PEAK_END_AGE をそれぞれ ±3 で揺らす。
 * 互いに独立にロールするので、組み合わせ次第で start >= end になりうるため
 * ガードして必ず `start < end` を保証する。
 */
export function rollPeakAges(rng: () => number): PeakAges {
  const baseStart = CHRONICLE_CONFIG.TIME.BASE_PEAK_START_AGE;
  const baseEnd   = CHRONICLE_CONFIG.TIME.BASE_PEAK_END_AGE;
  let start = baseStart + rollOffset(rng, 3); // 21〜27
  let end   = baseEnd   + rollOffset(rng, 3); // 25〜31
  if (start >= end) {
    // start を end - 1 に丸めて最小限の補正
    start = end - 1;
  }
  return { peakStartAge: start, peakEndAge: end };
}

/**
 * 子供用: 両親の peakStartAge / peakEndAge の平均 ±1 でロール。
 * 「成長タイプの遺伝性」を表現するための狭めレンジ。
 */
export function rollChildPeakAges(
  fatherPeakStart: number,
  fatherPeakEnd: number,
  motherPeakStart: number,
  motherPeakEnd: number,
  rng: () => number
): PeakAges {
  const avgStart = (fatherPeakStart + motherPeakStart) / 2;
  const avgEnd   = (fatherPeakEnd   + motherPeakEnd)   / 2;
  let start = Math.round(avgStart + rollOffset(rng, 1));
  let end   = Math.round(avgEnd   + rollOffset(rng, 1));
  if (start >= end) {
    start = end - 1;
  }
  return { peakStartAge: start, peakEndAge: end };
}
