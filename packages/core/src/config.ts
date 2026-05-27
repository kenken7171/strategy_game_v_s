/**
 * 旧 config エントリポイント。後方互換のため残置。
 * 新規コードは `config/ChronicleConfig.ts` の CHRONICLE_CONFIG を直接参照すること。
 */
import gameSettings from "../../../config/game_settings.json";
import { CHRONICLE_CONFIG } from "./config/ChronicleConfig";

/** @deprecated CHRONICLE_CONFIG.BATTLE.SQUAD_SIZE を参照すること */
export const MAX_UNITS_PER_SQUAD: number = CHRONICLE_CONFIG.BATTLE.SQUAD_SIZE;

/** 敵アクションループの最大長（JSON設定由来、CHRONICLE_CONFIG 管理外） */
export const MAX_ENEMY_ACTION_LOOP: number = gameSettings.max_enemy_action_loop;
