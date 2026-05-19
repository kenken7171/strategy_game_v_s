import { MAX_ENEMY_ACTION_LOOP } from "../config";

export interface EnemyAction {
  readonly name: string;
  readonly targetSlotIds: "RANDOM" | "ALL" | "NONE" | string[];
  readonly damage: number;
  readonly hitCount: number;
  // "spread": apply damage to every candidate independently (hitCount times each)
  // "random": pick random candidates, hitCount total (default for RANDOM/string[])
  // ALL defaults to "spread"; omit for RANDOM or string[] to use random mode
  readonly multiTargetMode?: "spread" | "random";
}

export class Enemy {
  readonly hp: number;
  readonly maxHp: number;
  readonly speed: number;
  readonly actions: ReadonlyArray<EnemyAction>;

  constructor(props: {
    hp: number;
    maxHp: number;
    speed: number;
    actions: EnemyAction[];
  }) {
    if (props.actions.length > MAX_ENEMY_ACTION_LOOP) {
      throw new Error(
        `Enemy actions exceed max_enemy_action_loop (${MAX_ENEMY_ACTION_LOOP}): got ${props.actions.length}`
      );
    }
    this.hp = props.hp;
    this.maxHp = props.maxHp;
    this.speed = props.speed;
    this.actions = [...props.actions];
  }

  getActionForTurn(turn: number): EnemyAction {
    if (this.actions.length === 0) {
      throw new Error("Enemy has no actions defined");
    }
    return this.actions[turn % this.actions.length];
  }
}
