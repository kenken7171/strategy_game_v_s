/**
 * 旅団運営ヘルパー
 *
 * 定員管理（リストラ）など、Brigade のコア機能には組み込まない
 * 「シミュレーション上の運営ルール」を提供する。
 */
import { Brigade } from "../models/Brigade";
import { Unit } from "../models/Unit";

/**
 * 定員管理: 旅団人数が maxSize を超過していたら、優先順位に従って
 * 超過分を除名（旅団から消去）した新しい Brigade を返す。
 *
 * リストラ優先順位（低い方から先に除名される）:
 *   1. 衰退期に入っており、かつ stats.strength が低い順
 *   2. 衰退期でなくても stats.strength が低い順
 *
 * 同点の場合は age 高位（年配）が優先除名される（若い者が残る）。
 * historicalNames / pendingBirths は永続記録のため引き継がれる。
 */
export interface RetirementResult {
  readonly brigade: Brigade;
  readonly retired: ReadonlyArray<Unit>;
}

export function enforceMaxBrigadeSize(brigade: Brigade, maxSize: number): RetirementResult {
  if (brigade.units.length <= maxSize) {
    return { brigade, retired: [] };
  }

  const excess = brigade.units.length - maxSize;
  // 弱者・老兵スコア: 大きいほど除名候補（先頭が真っ先にクビ）
  const scored = brigade.units.map((u) => ({
    unit: u,
    // 主要: stats.strength（低いほど除名候補=スコア高）
    weakness: -u.stats.strength,
    // 補助: 衰退期にいるなら +500（強烈にブースト）、それ以外は0
    declineBonus: u.age > u.peakEndAge ? 500 : 0,
    // tiebreaker: age 高位（年配）が優先除名
    age: u.age,
  }));
  scored.sort((a, b) => {
    const scoreA = a.weakness + a.declineBonus;
    const scoreB = b.weakness + b.declineBonus;
    if (scoreA !== scoreB) return scoreB - scoreA; // スコア高い順
    return b.age - a.age; // age 高い順
  });

  const toRetire = scored.slice(0, excess).map((s) => s.unit);
  const retireIds = new Set(toRetire.map((u) => u.id));
  const remaining = brigade.units.filter((u) => !retireIds.has(u.id));

  const next = new Brigade(
    remaining,
    [...brigade.squads],
    brigade.currentYear,
    brigade.pendingBirths,
    brigade.historicalNames // 引退者名も永続記録に残る
  );
  return { brigade: next, retired: toRetire };
}
