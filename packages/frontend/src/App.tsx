/**
 * App — エントリポイント。初回ロード時に API 経由でゲーム開始 →
 * GameManager をマウント。
 */
import { useEffect, useState } from "react";
import { GameManager } from "./game/GameManager";
import { api } from "./api/client";

type BootStatus = "loading" | "ready" | "error";

export function App() {
  const [status, setStatus] = useState<BootStatus>("loading");
  const [errMsg, setErrMsg] = useState<string>("");

  useEffect(() => {
    (async () => {
      try {
        await api.newGame(42);
        setStatus("ready");
      } catch (e) {
        setErrMsg(String(e));
        setStatus("error");
      }
    })();
  }, []);

  if (status === "loading") {
    return (
      <div data-testid="app-root" className="app">
        <div
          data-testid="common-loading-spinner"
          className="common-loading-spinner"
        >
          ⏳ Loading game session...
        </div>
      </div>
    );
  }
  if (status === "error") {
    return (
      <div data-testid="app-root" className="app">
        <div data-testid="common-error-banner" className="common-error-banner">
          ❌ API 接続失敗: {errMsg}
          <p>
            別ターミナルで <code>cd apps/api &amp;&amp; bun run dev</code>{" "}
            を起動してください。
          </p>
        </div>
      </div>
    );
  }
  return (
    <div data-testid="app-root" className="app">
      <GameManager />
    </div>
  );
}
