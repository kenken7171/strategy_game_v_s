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
