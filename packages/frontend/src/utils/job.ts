/**
 * ジョブ表示ヘルパー（フロント側）。
 *
 * バックエンド core/data/jobs.ts の JOB_JP をフロントにミラーリング。
 * 全画面（年代記・人事・編成・戦闘ログ・生存者リスト）で必ずこのヘルパー
 * を経由して英語ジョブ識別子を日本語に変換する。
 */

const JOB_JP: Record<string, string> = {
  iron_wall_knight: "鉄壁騎士",
  tactician: "戦術官",
  medic: "衛生兵",
  sniper: "狙撃兵",
  sorcerer: "呪術師",
  standard_bearer: "旗手",
  heavy_infantry: "重装歩兵",
  scout: "斥候",
};

/** UI のセレクト等で順番表示するためのエントリ配列 */
export const JOB_JP_ENTRIES: ReadonlyArray<readonly [string, string]> = [
  ["iron_wall_knight", "鉄壁騎士"],
  ["heavy_infantry",   "重装歩兵"],
  ["standard_bearer",  "旗手"],
  ["tactician",        "戦術官"],
  ["medic",            "衛生兵"],
  ["sniper",           "狙撃兵"],
  ["sorcerer",         "呪術師"],
  ["scout",            "斥候"],
];

/** ジョブID を日本語ラベルに変換。null/未知ジョブは "—" にフォールバック */
export function formatJob(job: string | null | undefined): string {
  if (!job) return "—";
  return JOB_JP[job] ?? job;
}

/**
 * 各ジョブのアビリティ解説（フロント用ミラー）。
 * core/data/jobs.ts の JOB_ABILITY と完全に同期する。
 * 詳細表示モーダル等で使用。
 */
export interface JobAbilityInfo {
  role: string;
  ability: string;
  usage: string;
  flavor: string;
}
export const JOB_ABILITY: Record<string, JobAbilityInfo> = {
  iron_wall_knight: {
    role: "前衛の盾。大隊を守護する重装騎士",
    ability: "BDF（FRONT配置時、大隊全員の被ダメを-10）+ SDF（自分隊の被ダメを-15）。複数いれば加算される",
    usage: "前衛中央に置き、後衛アタッカーへの被害を最小化する。複数編成で堅さが倍増する",
    flavor: "古き誓いを纏う者。我が身を盾とし、後ろの者たちを生かす",
  },
  heavy_infantry: {
    role: "単騎完結型の前衛アタッカー",
    ability: "全ジョブ最高 HP=300 + 高い前衛攻撃力 FA=70 + 自分隊軽減 SDF=10",
    usage: "鉄壁騎士の隣に置き、削れない盾として攻撃も担う。長期戦に強い",
    flavor: "壊れぬ鎧、屈せぬ意志。最後の一人になるまで戦線を支える",
  },
  standard_bearer: {
    role: "大隊全員の火力を底上げする精神的支柱",
    ability: "AB=40（ターン開始時、自分以外の全員に SPD+40 / 攻撃+40）。重ね掛け可能",
    usage: "前衛か後衛どちらでも機能する。複数編成で大隊全員が「化け物」になる",
    flavor: "翻る軍旗が士気を呼び覚ます。皆の心に火を灯す者",
  },
  tactician: {
    role: "速度寄りの軽量バフ役",
    ability: "AB=20（速度+20 / 攻撃+20）+ 自身も中速 SPD=35",
    usage: "旗手より控えめだが速度値も持つため、複合運用で先制力強化が狙える",
    flavor: "盤上の駒を読む冷徹な頭脳。号令一つで戦況を変える",
  },
  medic: {
    role: "唯一の継続回復役",
    ability: "HL=30（ターン末、自分隊の生存者全員を +30 HP 回復）。HP上限まで",
    usage: "脆い後衛分隊に必ず配置。前衛にも近代戦の必需品。長期戦の生命線",
    flavor: "戦場の天使、または、最後の希望。手当ての先に新たな未来を見る",
  },
  sniper: {
    role: "後衛超火力 + 2連撃の砲台",
    ability: "RA=90（後衛攻撃力90）。イニシアチブ1番手かつ分隊最速なら2連撃発動",
    usage: "REAR-L/REAR-R に配置必須。tactician/standard_bearer の SPD バフで2連撃確率を高める",
    flavor: "風の音を聴き、息を止める。一矢で戦況を変える狙撃の達人",
  },
  sorcerer: {
    role: "全ジョブ最強の火力、ただし最も脆い砲台",
    ability: "RA=120（後衛攻撃力120で全職最強）。HP=40で全ジョブ最弱",
    usage: "REAR 配置必須。前衛が崩れる前に決着をつけるために編成する高リスク高リターン札",
    flavor: "古き禁呪を扱う者。一発の魔弾に命を込める。あるいは、命を奪われる",
  },
  scout: {
    role: "速度最強の先制削り役",
    ability: "SPD=60（全ジョブ最速）+ 前後均等 FA=RA=40 で配置自由度高",
    usage: "REAR 配置で先制を取り、敵の出鼻を挫く。ジョブ最速の利を活かして tactician の補佐も可能",
    flavor: "誰よりも早く動き、誰よりも先に敵を見る。風のように戦場を駆ける",
  },
};

export function getJobAbility(job: string | null | undefined): JobAbilityInfo | null {
  if (!job) return null;
  return JOB_ABILITY[job] ?? null;
}

// ─── JOB_DEFAULTS ミラー（パッシブ値の取り出し用） ──────────────────────────
//
// core/data/jobs.ts の JOB_DEFAULTS と同期。BDF/SDF/AB/HL の生値を取り出して
// 「⚙ 固有パッシブ」セクションの構造化リストに使う。
// 設定変更時は本ミラーと core 側を両方更新すること（既存パターン）。

export interface JobDefaultsInfo {
  maxHp: number; speed: number;
  frontAttack: number; rearAttack: number;
  bdf: number; sdf: number; ab: number; hl: number;
}
export const JOB_DEFAULTS: Record<string, JobDefaultsInfo> = {
  iron_wall_knight: { maxHp: 250, speed: 10, frontAttack: 50, rearAttack:  10, bdf: 10, sdf: 15, ab:  0, hl:  0 },
  tactician:        { maxHp: 120, speed: 35, frontAttack: 20, rearAttack:  20, bdf:  0, sdf:  0, ab: 20, hl:  0 },
  medic:            { maxHp: 100, speed: 25, frontAttack: 10, rearAttack:  10, bdf:  0, sdf:  0, ab:  0, hl: 30 },
  sniper:           { maxHp:  80, speed: 40, frontAttack: 20, rearAttack:  90, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  sorcerer:         { maxHp:  40, speed: 15, frontAttack: 10, rearAttack: 120, bdf:  0, sdf:  0, ab:  0, hl:  0 },
  standard_bearer:  { maxHp: 150, speed: 20, frontAttack: 30, rearAttack:  30, bdf:  0, sdf:  5, ab: 40, hl:  0 },
  heavy_infantry:   { maxHp: 300, speed: 15, frontAttack: 70, rearAttack:  20, bdf:  0, sdf: 10, ab:  0, hl:  0 },
  scout:            { maxHp:  90, speed: 60, frontAttack: 40, rearAttack:  40, bdf:  0, sdf:  0, ab:  0, hl:  0 },
};

// ─── JOB_FORMATION_GUIDE ミラー（推奨配置・効果範囲） ──────────────────────
//
// core/data/jobs.ts の JOB_FORMATION_GUIDE と完全同期。UnitDetailModal で
// V 字図のハイライトと「推奨：最前線」ヘッドラインに使う。

export type SquadRowKey = "FRONT" | "REAR-L" | "REAR-R";
export type JobEffectKind = "buff" | "heal" | "defend" | "attack";

export interface JobFormationGuideInfo {
  recommendedRows: ReadonlyArray<SquadRowKey>;
  effectRange:     ReadonlyArray<SquadRowKey>;
  effectRangeNote: string;
  effectKind:      JobEffectKind;
  headline:        string;
}

export const JOB_FORMATION_GUIDE: Record<string, JobFormationGuideInfo> = {
  iron_wall_knight: {
    recommendedRows: ["FRONT"],
    effectRange:     ["FRONT", "REAR-L", "REAR-R"],
    effectRangeNote: "BDFはFRONT配置時のみ大隊全体に届く / SDFは所属分隊のみ",
    effectKind:      "defend",
    headline:        "推奨：最前線（BDF発動条件）",
  },
  heavy_infantry: {
    recommendedRows: ["FRONT"],
    effectRange:     ["FRONT"],
    effectRangeNote: "SDF=10で自分隊を軽減 / 高HP+高FAで前線を支える",
    effectKind:      "attack",
    headline:        "推奨：最前線（壊れぬ盾）",
  },
  standard_bearer: {
    recommendedRows: ["FRONT", "REAR-L", "REAR-R"],
    effectRange:     ["FRONT", "REAR-L", "REAR-R"],
    effectRangeNote: "AB=40で自分以外の大隊全員にSPD+40/攻撃+40を撒く",
    effectKind:      "buff",
    headline:        "推奨：どこでも可（大隊全員を強化）",
  },
  tactician: {
    recommendedRows: ["FRONT", "REAR-L", "REAR-R"],
    effectRange:     ["FRONT", "REAR-L", "REAR-R"],
    effectRangeNote: "AB=20で自分以外の大隊全員にSPD+20/攻撃+20を撒く",
    effectKind:      "buff",
    headline:        "推奨：どこでも可（軽量バフ役）",
  },
  medic: {
    recommendedRows: ["REAR-L", "REAR-R"],
    effectRange:     ["REAR-L", "REAR-R"],
    effectRangeNote: "HL=30は所属分隊のみ。配置した分隊の生存者全員を回復",
    effectKind:      "heal",
    headline:        "推奨：脆い後衛（自分隊を継続回復）",
  },
  sniper: {
    recommendedRows: ["REAR-L", "REAR-R"],
    effectRange:     ["REAR-L", "REAR-R"],
    effectRangeNote: "RA=90で後衛時のみフル火力 / 1番手かつ先頭で2連撃発動",
    effectKind:      "attack",
    headline:        "推奨：後衛(2連撃の砲台)",
  },
  sorcerer: {
    recommendedRows: ["REAR-L", "REAR-R"],
    effectRange:     ["REAR-L", "REAR-R"],
    effectRangeNote: "RA=120で全職最強の砲台 / HP=40なので前衛は不可",
    effectKind:      "attack",
    headline:        "推奨：後衛（高リスク高リターン）",
  },
  scout: {
    recommendedRows: ["REAR-L", "REAR-R", "FRONT"],
    effectRange:     ["REAR-L", "REAR-R", "FRONT"],
    effectRangeNote: "FA=RA=40で配置自由 / SPD=60で常時先制取り",
    effectKind:      "attack",
    headline:        "推奨：どこでも可（最速の先制削り役）",
  },
};

export function getJobFormationGuide(
  job: string | null | undefined
): JobFormationGuideInfo | null {
  if (!job) return null;
  return JOB_FORMATION_GUIDE[job] ?? null;
}

// ─── パッシブ解析ヘルパー ─────────────────────────────────────────────────
//
// JOB_DEFAULTS から非0の BDF/SDF/AB/HL 値を取り出して構造化リスト化する。
// UnitDetailModal の「⚙ 固有パッシブ」セクションで反復表示するためのデータ。
//
// 値が 0 のパッシブは項目から除外。全項目 0 の純アタッカー職（sniper/sorcerer/
// scout）は数値プロパティ無しなので、SPECIAL_NOTE の特殊行を返す場合がある。

export interface PassiveExplanation {
  /** ユニークキー (data-testid 用) — "bdf" | "sdf" | "ab" | "hl" | "special-*" */
  readonly key: string;
  /** UI ラベル（絵文字 + 略号 + 補足） */
  readonly label: string;
  /** 数値（特殊パッシブで該当なしの場合 null） */
  readonly value: number | null;
  /** 発動条件・スコープの一行説明 */
  readonly scope: string;
  /** 効果の一行説明 */
  readonly description: string;
}

/** ジョブ固有の特殊パッシブ（数値プロパティでは表現できないもの） */
const JOB_SPECIAL_PASSIVES: Record<string, ReadonlyArray<PassiveExplanation>> = {
  sniper: [
    {
      key: "special-double-strike",
      label: "🎯 2連撃",
      value: null,
      scope: "イニシアチブ1番手 ∧ 分隊先頭スロット時のみ",
      description: "通常攻撃が 2 回行われる（1 ターン中の追撃）",
    },
  ],
};

/**
 * 指定ジョブのパッシブ能力を構造化リスト化する。
 *
 * 数値プロパティ (bdf/sdf/ab/hl) は非0のものだけを項目化し、加えて
 * `JOB_SPECIAL_PASSIVES` に登録された特殊行を末尾に連結する。
 */
export function explainJobPassives(
  job: string | null | undefined
): ReadonlyArray<PassiveExplanation> {
  if (!job) return [];
  const d = JOB_DEFAULTS[job];
  if (!d) return [];

  const out: PassiveExplanation[] = [];
  if (d.bdf > 0) {
    out.push({
      key: "bdf",
      label: "🛡 BDF (Brigade Defense / 大隊軽減)",
      value: d.bdf,
      scope: "FRONT配置時のみ発動 / 効果は大隊全体",
      description: "大隊全員の被ダメージを軽減（自分以外の分隊にも届く）",
    });
  }
  if (d.sdf > 0) {
    out.push({
      key: "sdf",
      label: "🛡 SDF (Squad Defense / 自分隊軽減)",
      value: d.sdf,
      scope: "常時発動 / 効果は所属分隊のみ",
      description: "自分が所属する分隊の被ダメージを軽減",
    });
  }
  if (d.ab > 0) {
    out.push({
      key: "ab",
      label: "✨ AB (Ally Buff / 大隊バフ)",
      value: d.ab,
      scope: "毎ターン開始時 / 自分以外の大隊全員",
      description: "速度と攻撃力に値分のバフを撒く（重ね掛け可）",
    });
  }
  if (d.hl > 0) {
    out.push({
      key: "hl",
      label: "💚 HL (Heal / 自分隊回復)",
      value: d.hl,
      scope: "毎ターン終了時 / 所属分隊の生存者全員",
      description: "HP を値分回復（最大HPでキャップ）",
    });
  }

  const special = JOB_SPECIAL_PASSIVES[job];
  if (special) out.push(...special);

  return out;
}
