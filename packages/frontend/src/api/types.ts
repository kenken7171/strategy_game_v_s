/**
 * API レスポンスの型定義（バックエンドと合わせる）。
 */

// 共通
export type Gender = "Male" | "Female";
export type Origin = "Japanese" | "European" | "Classical";
export type SquadRow = "FRONT" | "REAR-L" | "REAR-R";
export type Winner = "Allies" | "Enemies" | "Draw";

// /api/game/new, /state
export interface GameStateResponse {
  year: number;
  seed: number;
  brigadeSize: number;
  maxBrigadeSize: number;
  candidatePoolSize?: number;
}

// /api/chronicle
export interface ChronicleHistoryEntry {
  year: number;
  type: "join" | "retire" | "marriage" | "birth_planned" | "birth" | "battle";
  text: string;
}
export interface ChronicleResponse {
  year: number;
  brigadeSize: number;
  marriedCouples: number;
  descendants: number;
  history: ChronicleHistoryEntry[];
}

// /api/chronicle/preview
export interface EnemyPreviewStat {
  base: number;
  min: number;
  max: number;
}
export interface EnemyPreviewResponse {
  year: number;
  enemyCount: number;
  hp: EnemyPreviewStat;
  attack: EnemyPreviewStat;
  speed: EnemyPreviewStat;
}

// /api/guild/decisions
export interface BattleStatsFields {
  maxHp: number;
  attack: number;        // max(frontAttack, rearAttack)
  frontAttack: number;
  rearAttack: number;
  speed: number;
  totalRating: number;   // floor(maxHp/5 + attack + speed)
}
export interface RecruitRow extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  baseStrength: number;
  source: "application" | "heir";
  hasLineage: boolean;
  relatedFamilyIds: string[];
}
export interface RetireeRow extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  strength: number;
  strengthRank: number;
  reasons: string[];
  hasLineage: boolean;
  descendantCount: number;
  isMarried: boolean;
}
export interface GuildDecisionsResponse {
  year: number;
  currentSize: number;
  maxSize: number;
  overflowCount: number;
  recruits: RecruitRow[];
  retirementCandidates: RetireeRow[];
}

// /api/formation/roster
export interface RosterUnit extends BattleStatsFields {
  id: string;
  name: string;
  job: string | null;
  gender: Gender;
  origin: Origin;
  age: number;
  strength: number;
  baseStrength: number;
  growthFactor: number;
  isMarried: boolean;
  spouseId: string | null;
  parents: { fatherId: string; motherId: string } | null;
  isAlive: boolean;
  isRetired: boolean;
  descendantCount: number;
}
export interface FormationRosterResponse {
  year: number;
  units: RosterUnit[];
  affinityMap: Record<string, Record<string, number>>;
}

// /api/battle/run

export type RotationStrategy = "NONE" | "CW" | "CCW";

/** ターン開始時の各ユニットの配置スナップショット */
export interface GridPlacement {
  unitId: string;
  unitName: string;
  job: string | null;
  /** 性別（UI アイコン表示で使用） */
  gender: Gender;
  row: SquadRow;
  col: number;
  hp: number;
  maxHp: number;
}

/** 敵の攻撃パターン種別 */
export type AttackPatternKind = "SINGLE_STRIKE" | "PINCER" | "TOTAL_ASSAULT";

/** 敵の次ターン行動予告 */
export interface AttackIntent {
  kind: AttackPatternKind;
  skillName: string;
  targetRows: SquadRow[];
  damagePerUnit: number;
}

export interface TurnLog {
  turn: number;
  headerText: string;
  initiativeText: string;
  enemyActionText: string;
  allyAttackLines: string[];
  healLines: string[];
  victory: boolean;
  /** このターン開始時のローテーション通知（NONE なら null） */
  rotationNotice: string | null;
  /** このターン開始時点の配置 */
  placements: GridPlacement[];
  /** このターンに解決された攻撃 intent（前ターン予告と一致） */
  resolvedIntent: AttackIntent | null;
}

// /api/battle/preview
export interface PreviewTimelineEntry {
  kind: "ally" | "enemy";
  id: string;
  label: string;
  speed: number;
  jobs?: string[];
  members?: string[];
}
export interface BattlePreviewResponse {
  year: number;
  timeline: PreviewTimelineEntry[];
  enemyPreview: {
    count: number;
    maxSpeed: number;
    maxAttack: number;
  };
}

/** 敵ボスの現在状態（戦闘画面最上部の EnemyStatusCard で表示） */
export interface EnemyState {
  name: string;
  job: string | null;
  hp: number;
  maxHp: number;
  speed: number;
  frontAttack: number;
  rearAttack: number;
}

// /api/battle/init, /api/battle/turn
export interface BattleInitResponse {
  ok: boolean;
  year: number;
  placements: GridPlacement[];
  timeline: PreviewTimelineEntry[];
  isFinished: boolean;
  currentTurn: number;
  nextActionIntent: AttackIntent;
  /** 敵ボスの現在 state */
  enemy: EnemyState;
}
export interface BattleTurnResponse {
  ok: boolean;
  turnLog: TurnLog;
  timeline: PreviewTimelineEntry[] | null;
  finished: boolean;
  winner: Winner | null;
  currentTurn: number;
  allySurvivors: SurvivorRow[] | null;
  enemySurvivors: SurvivorRow[] | null;
  /** 次ターンの攻撃予告（戦闘終了時は null） */
  nextActionIntent: AttackIntent | null;
  /** 敵ボスの現在 state（HP は毎ターン減少） */
  enemy: EnemyState;
}
export interface SurvivorRow {
  name: string;
  job: string | null;
  /** 性別（味方のみ、敵は省略） */
  gender?: Gender;
  hp: number;
  maxHp: number;
}
export interface BattleRunResponse {
  ok: boolean;
  year: number;
  winner: Winner;
  turns: number;
  statistics: {
    totalDamageDealt: Record<string, number>;
    totalDamageMitigated: number;
    totalHealing: Record<string, number>;
    killCount: Record<string, number>;
  };
  allySurvivors: SurvivorRow[];
  enemySurvivors: SurvivorRow[];
  turnLogs: TurnLog[];
  rotationStrategy: RotationStrategy;
}

// /api/battle/finish
export interface BattleFinishResponse {
  ok: boolean;
  nextYear: number;
  eventsCount: number;
  brigadeSize: number;
}

// 編成データ送信用
export interface BattlePlacement {
  row: SquadRow;
  col: number;
  unitId: string;
}
