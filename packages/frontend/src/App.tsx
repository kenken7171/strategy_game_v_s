/**
 * App — エントリポイント。GameManager をマウントするだけ。
 */
import { GameManager } from "./game/GameManager";

export function App() {
  return (
    <div data-testid="app-root" className="app">
      <GameManager />
    </div>
  );
}
