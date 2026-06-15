# Chronicle Knights — 決定論的100年クロニクルRPG

> 日本語版 起動ガイド。英語版は [README.md](README.md) を参照してください。
>
> ※ 本ドキュメントは読みやすさのため日本語（ひらがな・漢字・カタカナ）で記述しています。
> ただし**ソースコードの識別子・ノード名・testid・コア内部ログは開発憲法①により ASCII 限定**を
> 1ビットの隙もなく死守しています（本書はあくまで人間向けの解説書です）。

---

## タイトルと概要

**Chronicle Knights** — 決定論的（deterministic）な100年クロニクル RPG。
Godot 4 / .NET 8 / C# 12 で構築。騎士団（旅団）を率い、たった一つのシードから決定される
100年の歴史を駆け抜けます。1年 = 大回廊（grand corridor）の1周です。

```
Title  ->  Hub  ->  Battle  ->  Settlement  ->  Hub  ->  ...
(シード)  (経済/      (敵意       (とどめ +       (次の
          予言/       先読み +    戦果還流 +      年へ)
          戦力)       決戦)       年代記印字)
```

同じシードからは、まったく同じ100年が再現されます（完全な決定論）。

---

## 前提条件

- **.NET 8 SDK**（プロジェクトは `net8.0` ターゲット、C# 12）。**.NET 10 SDK** でも動作します
  （テストは roll-forward で 10.x ランタイム上で実行可能）。
- **Godot Engine 4.x**（.NET / C# 対応版。いわゆる「Mono」ビルド。Godot.NET.Sdk 4.3.0 で確認済み）。

---

## Mac で `command not found: godot` が出たときの対策

Godot を `.app` でインストールしただけでは、ターミナルから `godot` を直接叩けません。
次のいずれかで解決します。

**方法1 — アプリケーションを直接叩く（その場しのぎ）:**

```sh
/Applications/Godot.app/Contents/MacOS/Godot --path .
```

**方法2 — シンボリックリンクで恒久的にパスを通す（推奨）:**

```sh
sudo ln -s /Applications/Godot.app/Contents/MacOS/Godot /usr/local/bin/godot
```

以後はどこからでも `godot --path .` で起動できます。確認:

```sh
godot --version
```

（Godot のアプリ名が `Godot_mono.app` など異なる場合は、そのパスに読み替えてください。）

---

## Mac で `command not found: dotnet` が出たときの対策

`dotnet` にパスが通っていないだけです。次のいずれかで解決します。

**方法1 — Homebrew でインストール:**

```sh
brew install --cask dotnet-sdk
```

**方法2 — `.zshrc` へパスを通す（インストール済みの場合）:**

```sh
echo 'export PATH="$PATH:/usr/local/share/dotnet"' >> ~/.zshrc
source ~/.zshrc
```

確認:

```sh
dotnet --version
```

---

## ビルド＆実行コマンド

すべてこのディレクトリ（`generated_csharp/`）から実行します。

**ビルド（Debug）:**

```sh
dotnet build ChronicleKnights.csproj --configuration Debug
```

**テスト（xUnit 契約テスト）:**

```sh
dotnet test Tests/ChronicleKnights.Tests.csproj
```

本体・テストの両 csproj は `<RollForward>LatestMajor</RollForward>` をビルドへ焼き込んであります。
そのため `net8.0` ターゲットでありながら、8.0 ランタイムが無く 10.x のみの Mac でも、環境変数や
追加インストール無しでそのまま実行できます（古い手順の `DOTNET_ROLL_FORWARD=...` は不要になりました）。

**Mac でローカル起動（最短）:**

.NET（mono）版の Godot 4.3 を `godot` として通した上で、付属ランチャを叩くだけです。

```sh
./play.command          # ゲームを起動（C# を自動ビルドしてから実機起動）
./play.command -e       # Godot エディタを開く
```

Finder からダブルクリックしても起動します。手動で叩く場合は以下と等価です:

```sh
dotnet build ChronicleKnights.csproj --configuration Debug
godot --path .
```

> **重要 — Godot は .NET（mono）版が必須**。標準版（`GodotSharp` 非同梱）では C# が動きません。
> `godot --version` が `4.3.stable.mono.official` を返すこと、`which godot` が .NET 版を指すことを確認してください。
> 例: `ln -sf /Applications/Godot_mono.app/Contents/MacOS/Godot /usr/local/bin/godot`

`Main.tscn` が立ち上がり、無状態のシーンルータ（`UserInterfaceRoot`）が Title 画面から起動します。

> macOS の `--headless` は Godot 4.3 の既知の不具合（`recursive_mutex lock failed`）でクラッシュします。
> 画面確認は必ず通常（windowed）起動で行ってください。ヘッドレスは CI でも避けます。

---

## プロジェクトの鉄の憲法（設計の四柱）

1. **不変 SoT（唯一の真実の源: `ChronicleGlobal`）**
   `/root/ChronicleGlobal` という autoload シングルトンだけが、全ゲーム状態（経済・タイムライン・
   ロスター・戦闘スナップショット・年代記ログ・英霊アーカイブ）を保持します。すべての変更はここを
   通り、シグナル（EconomyChanged / TimelineChanged / RosterChanged / BattleChanged / PhaseChanged）で
   観測側へ伝わります。

2. **無状態 UI（Stateless UI）**
   ビューはゲーム変数を一切キャッシュしません。描画のたびに SoT をその場で読み直し、ラベルや
    HP バーへ一方通行で流し込みます（Push バインド）。保持するのは二重実行ガード等の UI ラッチのみで、
   ゲームデータは決して持ちません。

3. **完全リークフリーなライフサイクル（4大台帳 + ノード束縛 Tween）**
   動的生成したノードはビューごとの台帳（registry）に記録し、再描画の冒頭とシーン退場（`_ExitTree`）で
   `QueueFree()` して更地化します:
   `_timelineNodes` / `_rosterNodes` / `_battleNodes` / `_settlementNodes`。
   すべての演出 Tween（Flash / CountUp / Typewriter）は対象ノード自身へ束縛され、ノードが解放されると
   自動的に失効します（コールバックは `IsInstanceValid` ガード付き）。シグナル購読は `_Ready` で張り、
   `_ExitTree`（およびビュー切替）で完全に解除します。

4. **完全決定論シード（Deterministic PRNG Seeding）**
   新規ゲームは一つのシードを注入します（`StartNewGame(seed)`）。同じシードは同じ100年を再現します。
   ロジックは副作用なしで外部からシードされるため、環境に依存しません。

> 補足 — 開発憲法①（厳格 ASCII）: `Core/` および `UserInterface/` 層の識別子・コンポーネント名・
> testid・表示テキスト・コメントはすべて ASCII のみ（非 ASCII バイトはゼロ）。表示用の日本語ラベルは
> `Config/` のキーから解決します。本書のような人間向けドキュメントだけが日本語で記述されます。

---

## 実機検収の結果（この環境で確認済み）

- `dotnet build ChronicleKnights.csproj --configuration Debug` → 0 警告 / 0 エラー。
- `dotnet test Tests/ChronicleKnights.Tests.csproj`（環境変数なし、焼き込んだ roll-forward で net8.0→net10）→ 失敗 0 / 合格 617 / 警告 0。
- 実機起動（Intel Mac / macOS 12.7.6 / Godot 4.3 .NET）→ Vulkan(Forward+) でウィンドウ描画、.NET 解決エラーなし。

旅団長、大回廊は開き、聖典は据えられ、実機の光は放たれました。出陣の刻にございます。
