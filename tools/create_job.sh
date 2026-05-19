#!/usr/bin/env bash
# create_job.sh — 新しいジョブ定義をコンソール出力およびコードスニペットで提示する
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JOBS_JSON="${SCRIPT_DIR}/../config/jobs.json"

echo "╔══════════════════════════════════╗"
echo "║   ジョブ作成ウィザード            ║"
echo "╚══════════════════════════════════╝"
echo ""

read -rp "ジョブID (例: archer)          : " JOB_ID
read -rp "ジョブ名 (日本語可, 例: 弓兵)   : " JOB_NAME
read -rp "説明                           : " DESCRIPTION
read -rp "frontAttack (FA)               : " FA
read -rp "rearAttack  (RA)               : " RA
read -rp "speed       (SPD)              : " SPD
read -rp "maxHp       (HP)               : " HP
read -rp "SDF (分隊防御、なければ 0)      : " SDF
read -rp "BDF (大隊防御、なければ 0)      : " BDF
read -rp "AB  (攻撃バフ、なければ 0)      : " AB
read -rp "HL  (回復量、なければ 0)        : " HL

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "▶ TypeScript スニペット"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
cat <<SNIPPET
const ${JOB_ID} = new Unit({
  id: "${JOB_ID}_1",
  name: "${JOB_NAME}",
  age: 25, peakAge: 30, maxAge: 60,
  baseStats: { strength: 50, agility: ${SPD}, intelligence: 0, endurance: 50 },
  maxHp: ${HP}, hp: ${HP},
  speed: ${SPD},
  frontAttack: ${FA},
  rearAttack: ${RA},
  job: "${JOB_ID}" as JobType,
  sdf: ${SDF},
  bdf: ${BDF},
  ab: ${AB},
  hl: ${HL},
});
SNIPPET

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "▶ config/jobs.json エントリ"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

NEW_ENTRY=$(cat <<JSON
    {
      "id": "${JOB_ID}",
      "name": "${JOB_NAME}",
      "description": "${DESCRIPTION}",
      "defaults": { "frontAttack": ${FA}, "rearAttack": ${RA}, "speed": ${SPD}, "maxHp": ${HP}, "sdf": ${SDF}, "bdf": ${BDF}, "ab": ${AB}, "hl": ${HL} }
    }
JSON
)

echo "${NEW_ENTRY}"
echo ""

# config/jobs.json に追記するか確認
read -rp "config/jobs.json に追記しますか？ [y/N] " CONFIRM
if [[ "${CONFIRM}" =~ ^[Yy]$ ]]; then
  # jq がある場合は安全に挿入、なければ手動案内
  if command -v jq &>/dev/null; then
    TMP=$(mktemp)
    jq --argjson entry "${NEW_ENTRY}" '.jobs += [$entry]' "${JOBS_JSON}" > "${TMP}"
    mv "${TMP}" "${JOBS_JSON}"
    echo "✓ ${JOBS_JSON} に追記しました。"
  else
    echo "⚠ jq が見つかりません。上記エントリを手動で ${JOBS_JSON} の \"jobs\" 配列に追加してください。"
  fi
fi
