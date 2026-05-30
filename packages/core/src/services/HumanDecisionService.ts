/**
 * HumanDecisionService — 人事フェーズの器（手動介入用 API）
 *
 * instructions.md B-4/B-5 のルールに基づき、新人入団・子供雇用・老兵引退を
 * 「プレイヤーの選択」として扱うための純粋関数群。
 *
 * 自動リストラ（`utils/brigade.ts` の `enforceMaxBrigadeSize`）の代替として、
 * フロントエンド（Hono API 等）がこれらを順次呼び出して旅団状態を遷移させる。
 *
 * 設計原則:
 *   - 純粋関数（副作用なし、新 Brigade を返す）
 *   - DOM/UI 非依存（バックエンドとフロントの境界で使える）
 *   - イミュータビリティ厳守（Brigade / Unit を直接変更しない）
 */
import { Brigade } from "../models/Brigade";
import { Unit } from "../models/Unit";

// ─── 型定義 ──────────────────────────────────────────────────────────────────

/** 引退候補の理由分類 */
export type RetirementReason =
  | "decline"   // 衰退期に入っている (age > peakEndAge)
  | "old"       // 年配（peakStartAge を過ぎている）
  | "weak"      // stats.strength が旅団内で低位
  | "overflow"; // 定員超過による絞り込み候補

/** 引退候補ユニットの詳細 */
export interface RetirementCandidate {
  readonly unit: Unit;
  readonly reasons: ReadonlyArray<RetirementReason>;
  /** 旅団内 strength ランキング（1=最弱, 最大値=最強） */
  readonly strengthRank: number;
  /** 血統情報があるか（あれば「能力 vs 家系」の判断材料） */
  readonly hasLineage: boolean;
  /** 子孫の数（高いほど家系として残す価値） */
  readonly descendantCount: number;
}

/** 採用候補（志願者 or 15歳継承者） */
export interface RecruitCandidate {
  readonly unit: Unit;
  readonly source: "application" | "heir";
  /** 親情報があれば、関連する血統ユニットの ID */
  readonly relatedFamilyIds: ReadonlyArray<string>;
}

/** プレイヤーに提示する判断リスト */
export interface PendingDecisions {
  readonly recruits: ReadonlyArray<RecruitCandidate>;
  readonly retirementCandidates: ReadonlyArray<RetirementCandidate>;
  /** 旅団定員に対する超過数（>0 なら最低この数だけ除名が必要） */
  readonly overflowCount: number;
  /** 現在の旅団人数 */
  readonly currentSize: number;
  /** 定員上限 */
  readonly maxSize: number;
}

// ─── getPendingDecisions ─────────────────────────────────────────────────────

/**
 * プレイヤーに提示すべき判断リストを構造化して返す。
 *
 * @param brigade        現在の旅団
 * @param candidatePool  外部からの志願者リスト（advance() の結果や手動投入）
 * @param maxSize        旅団定員（CHRONICLE_CONFIG.LIMITS.MAX_BRIGADE_SIZE）
 */
export function getPendingDecisions(
  brigade: Brigade,
  candidatePool: ReadonlyArray<Unit>,
  maxSize: number
): PendingDecisions {
  // ── 採用候補の整形 ────────────────────────────────────────────────────────
  const recruits: RecruitCandidate[] = candidatePool.map((u) => {
    const family: string[] = [];
    if (u.parents) {
      family.push(u.parents.fatherId, u.parents.motherId);
    }
    return {
      unit: u,
      source: u.parents ? "heir" : "application",
      relatedFamilyIds: family,
    };
  });

  // ── 引退候補の整形 ────────────────────────────────────────────────────────
  // strength でランク付け（昇順）して低位から候補化
  const ranked = [...brigade.units]
    .map((u, _idx) => ({ unit: u, strength: u.stats.strength }))
    .sort((a, b) => a.strength - b.strength);

  // 子孫数のカウント（自分を親とするユニット数）
  const descendantCount = new Map<string, number>();
  for (const u of brigade.units) {
    if (u.parents) {
      descendantCount.set(u.parents.fatherId, (descendantCount.get(u.parents.fatherId) ?? 0) + 1);
      descendantCount.set(u.parents.motherId, (descendantCount.get(u.parents.motherId) ?? 0) + 1);
    }
  }

  const retirementCandidates: RetirementCandidate[] = ranked.map((row, i) => {
    const u = row.unit;
    const reasons: RetirementReason[] = [];
    if (u.age > u.peakEndAge) reasons.push("decline");
    else if (u.age > u.peakStartAge) reasons.push("old");
    // 下位25% は weak 候補とする
    if (i < Math.ceil(ranked.length * 0.25)) reasons.push("weak");
    return {
      unit: u,
      reasons,
      strengthRank: i + 1,
      hasLineage: u.parents !== null || u.spouseId !== null,
      descendantCount: descendantCount.get(u.id) ?? 0,
    };
  });

  const currentSize = brigade.units.length + candidatePool.length;
  const overflowCount = Math.max(0, currentSize - maxSize);

  return {
    recruits,
    retirementCandidates,
    overflowCount,
    currentSize: brigade.units.length,
    maxSize,
  };
}

// ─── acceptRecruit ───────────────────────────────────────────────────────────

/**
 * 志願者・継承者を旅団に受け入れる。
 *
 * @returns 新しい Brigade（イミュータブル）。historicalNames は自動更新。
 * @throws  recruit と同 ID のユニットが既に旅団にいる場合
 */
export function acceptRecruit(brigade: Brigade, recruit: Unit): Brigade {
  if (brigade.units.some((u) => u.id === recruit.id)) {
    throw new Error(`[acceptRecruit] Unit "${recruit.id}" は既に旅団に所属しています`);
  }
  return new Brigade(
    [...brigade.units, recruit],
    [...brigade.squads],
    brigade.currentYear,
    brigade.pendingBirths,
    brigade.historicalNames // コンストラクタが recruit.name を自動追加
  );
}

// ─── dismissUnit ─────────────────────────────────────────────────────────────

/** 解雇結果 */
export interface DismissResult {
  readonly brigade: Brigade;
  /** 除名されたユニット（unitId が見つからなかった場合は null） */
  readonly dismissed: Unit | null;
}

/**
 * 指定ユニットを戦力外通告（引退）させる。
 *
 * @param brigade 現在の旅団
 * @param unitId  除名対象のユニットID
 * @returns 新しい Brigade と除名された Unit。
 *          unitId が存在しなかった場合は dismissed: null で旅団は不変。
 */
export function dismissUnit(brigade: Brigade, unitId: string): DismissResult {
  const target = brigade.units.find((u) => u.id === unitId);
  if (!target) {
    return { brigade, dismissed: null };
  }
  const next = new Brigade(
    brigade.units.filter((u) => u.id !== unitId),
    [...brigade.squads],
    brigade.currentYear,
    brigade.pendingBirths,
    brigade.historicalNames
  );
  return { brigade: next, dismissed: target };
}

// ─── applyDecisions（一括適用） ───────────────────────────────────────────────

/** プレイヤーの選択結果 */
export interface HumanDecisions {
  /** 採用するユニットIDのリスト */
  readonly acceptIds: ReadonlyArray<string>;
  /** 除名するユニットIDのリスト */
  readonly dismissIds: ReadonlyArray<string>;
}

/** 適用結果のサマリー */
export interface DecisionsApplied {
  readonly brigade: Brigade;
  readonly accepted: ReadonlyArray<Unit>;
  readonly dismissed: ReadonlyArray<Unit>;
  readonly ignoredAcceptIds: ReadonlyArray<string>; // 候補に無かったID
  readonly ignoredDismissIds: ReadonlyArray<string>;
}

/**
 * 採用・解雇判断を一括適用する。
 *
 * 処理順: 先に dismiss（空きを作る） → 次に accept（受け入れ）
 * これにより「定員ぎりぎりで入れ替える」操作も安全に行える。
 */
export function applyDecisions(
  brigade: Brigade,
  candidatePool: ReadonlyArray<Unit>,
  decisions: HumanDecisions
): DecisionsApplied {
  let working = brigade;
  const dismissed: Unit[] = [];
  const accepted: Unit[] = [];
  const ignoredAcceptIds: string[] = [];
  const ignoredDismissIds: string[] = [];

  // 1) 除名
  for (const id of decisions.dismissIds) {
    const res = dismissUnit(working, id);
    working = res.brigade;
    if (res.dismissed) dismissed.push(res.dismissed);
    else ignoredDismissIds.push(id);
  }

  // 2) 採用
  const poolMap = new Map(candidatePool.map((u) => [u.id, u]));
  for (const id of decisions.acceptIds) {
    const candidate = poolMap.get(id);
    if (!candidate) {
      ignoredAcceptIds.push(id);
      continue;
    }
    try {
      working = acceptRecruit(working, candidate);
      accepted.push(candidate);
    } catch {
      ignoredAcceptIds.push(id);
    }
  }

  return { brigade: working, accepted, dismissed, ignoredAcceptIds, ignoredDismissIds };
}
