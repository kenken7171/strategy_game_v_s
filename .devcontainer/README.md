# Dev Container — Chronicle Knights

このディレクトリは、**どの端末・どのOSからでも「リポジトリを開くだけ」で `dotnet build / test` と
`godot --headless` が環境依存ゼロで走る**開発・検証用コンテナ（VS Code Dev Containers）を定義します。

ホスト側に .NET や Godot を一切インストールする必要はありません。Intel Mac / Apple Silicon Mac /
Windows (x86_64) のいずれからでも、同一の「聖域」が立ち上がります。

> 補足: 本ファイルは人間向けの解説書のため日本語で記述しています。`devcontainer.json` / `Dockerfile`
> および `generated_csharp/` のコード・識別子・ログは開発憲法①により ASCII 限定を死守しています。

---

## 前提条件（ホスト側）

- **Docker Desktop**（起動済みであること）
- **Visual Studio Code**
- VS Code 拡張 **Dev Containers**（`ms-vscode-remote.remote-containers`）

---

## 最短手順（クローン → コンテナ出陣）

```sh
git clone https://github.com/kenken7171/strategy_game_v_s.git
cd strategy_game_v_s
code .
```

VS Code が `.devcontainer/` を自動検出します。右下のトーストの **「Reopen in Container」**
（または `F1` → `Dev Containers: Reopen in Container`）を選ぶと、初回のみイメージが自動ビルドされ、
必須拡張のインストールと `dotnet restore` が自動執行されます。

コンテナ内ターミナルが開いたら、即座に出陣できます：

```sh
cd generated_csharp

# ビルド（0 警告 / 0 エラー）
dotnet build ChronicleKnights.csproj --configuration Debug

# テスト（xUnit 617 件グリーン）
dotnet test Tests/ChronicleKnights.Tests.csproj

# Godot .NET アセンブリ結合の画面なし検証
godot --headless --path . --quit-after 30
```

---

## ゲーム画面を「見る」には（VNC / ポート経由）

> **重要な前提**: Godot のデスクトップゲームは Web サーバーではありません。画面は OS のディスプレイへ
> ネイティブのウィンドウとして描かれます。コンテナ（Linux）には物理ディスプレイが無いため、
> `--headless` でも通常起動でも、そのままでは「ウィンドウ」は出ません（ポート転送だけでは映りません）。

そこで本コンテナは **仮想ディスプレイ + VNC** を同梱し、その画面を**ポート経由でブラウザに映す**経路を
用意しています（`desktop-lite` feature）。Godot は Mesa のソフトウェア GL（llvmpipe）でレンダリングします。

**手順:**

1. コンテナを **Rebuild**（`F1` → `Dev Containers: Rebuild Container`）して VNC 同梱版にする。
2. VS Code の「ポート」タブで **6080**（noVNC web）を開く（自動転送される）。
   ブラウザで `http://localhost:6080` を開く（パスワード既定: `vscode`）。
3. 表示された Linux デスクトップで端末を開き、ゲームをウィンドウ起動する:

   ```sh
   cd /workspaces/strategy_game_v_s/generated_csharp
   godot --path . --rendering-driver opengl3
   ```

4. ブラウザの中に Chronicle Knights のタイトル画面が立ち上がります。

> ソフトウェア GL のため描画は軽快ではありません。**実際に快適に遊ぶなら、画面のあるホスト
> （Mac/Windows デスクトップ）に .NET版 Godot を入れて `godot --path .` を直接叩く**のが最善です。
> コンテナはあくまで「ビルド・テスト・CI 検証 + 動作確認用の覗き窓」です。

---

## このコンテナが提供するもの

| 要素 | 内容 |
|---|---|
| ベースイメージ | `mcr.microsoft.com/dotnet/sdk:8.0`（Microsoft 公式・Debian） |
| Godot | Godot Engine **4.3 stable .NET (mono)** headless 一式（`/usr/local/bin/godot`） |
| 自動導入される VS Code 拡張 | `ms-dotnettools.csharp` / `ms-dotnettools.csdevkit` / `geequlim.godot-tools` |
| 起動時の自動処理 | `postCreateCommand` で `dotnet restore`（本体 + テストの 2 csproj） |
| Godot ツール連携 | `godotTools.editorPath.godot4 = /usr/local/bin/godot` を自動設定 |

### アーキテクチャ互換の防壁

`Dockerfile` は BuildKit が設定する `ARG TARGETPLATFORM` を読み、ホストのアーキテクチャに合わせて
Godot の Linux アセットを自動選択します（`linux/amd64 → linux_x86_64` / `linux/arm64 → linux_arm64`）。
これにより Intel / Apple Silicon / Windows x86_64 のどのホストからビルドしても正しいイメージになります。

Godot mono zip は「実行ファイル名（`..._mono_linux.x86_64`＝ドット）」と「フォルダ名（`..._mono_linux_x86_64`
＝アンダースコア）」が食い違うため、`Dockerfile` は実行ファイルを決め打ちせず `find` で動的に索敵して
`/usr/local/bin/godot` へ通電します。`GodotSharp/` フォルダは実行ファイルと同一階層のまま維持されます。

---

## 構成ファイル

```
.devcontainer/
  devcontainer.json   VS Code 設定（拡張・postCreateCommand・Godot パス・ビルド引数）
  Dockerfile          .NET 8 SDK ベース + Godot 4.3 mono headless 自動配備
  README.md           本書
```

### バージョンの上書き

`devcontainer.json` の `build.args` で版を切り替えられます（既定はプロジェクトの
`Godot.NET.Sdk/4.3.0` に合わせた 4.3 stable）：

```jsonc
"args": {
  "DOTNET_SDK_TAG": "8.0",
  "GODOT_VERSION": "4.3",
  "GODOT_RELEASE": "stable"
}
```

---

## トラブルシュート

- **拡張や restore が反映されない / 設定を変えた**
  `F1` → `Dev Containers: Rebuild Container` でイメージを作り直してください。
- **`godot: command not found`（コンテナ内）**
  イメージのビルドが完了していない可能性があります。Rebuild Container を実行してください。
  通電済みなら `command -v godot` が `/usr/local/bin/godot` を返します。
- **Docker のディスク逼迫**
  不要イメージを `docker image prune` で掃除してください。

---

## 検証済みであること

このコンテナは実ビルド・実起動で動作確認済みです：

- `godot --version` → `4.3.stable.mono.official`（`.mono` 版が headless 駆動）
- `dotnet --version` → `8.0.x`
- `GodotSharp/` が実行ファイルと同一階層に在り（分離なし）
- `generated_csharp/` の不変ロジック（xUnit 617 件・黄金比・▲陣形提示層）は一切変更なし
