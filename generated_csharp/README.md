# Chronicle Knights — 決定論的100年クロニクルRPG

> 起動ガイド（日本語）。
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

**方法2 — ラッパースクリプトで恒久的にパスを通す（推奨）:**

```sh
cat > /usr/local/bin/godot <<'WRAP'
#!/bin/bash
exec "/Applications/Godot_mono.app/Contents/MacOS/Godot" "$@"
WRAP
chmod +x /usr/local/bin/godot
```

以後はどこからでも `godot --path .` で起動できます。確認:

```sh
godot --version
```

> **symlink にしてはいけません。** Godot は `GodotSharp/` を起動した実バイナリの位置から探すため、
> `/usr/local/bin/godot` をバンドルへの symlink にすると `/usr/local/bin/GodotSharp/...` を見に行って
> `unable to find .NET assemblies directory` で失敗します。必ず上記のような**実バイナリを `exec` する
> ラッパースクリプト**にしてください（Godot のアプリ名が異なる場合はそのパスに読み替え）。

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
> `godot --version` が `4.3.stable.mono.official` を返すことを確認してください。
> 付属の `./play.command` は最初からバンドル内の実バイナリを直接起動するため、symlink 問題は起きません。

`Main.tscn` が立ち上がり、`GameDirector` が Title 画面から起動します。

> macOS の `--headless` は Godot 4.3 の既知の不具合（`recursive_mutex lock failed`）でクラッシュします。
> 画面確認は必ず通常（windowed）起動で行ってください。ヘッドレスは CI でも避けます。

---

## ジョブ画像アセット

ジョブ別イラストは統合配置ディレクトリへ集約しています。

```
res://Assets/Textures/Jobs/{ジョブ識別子}/{male|female}.png
```

`JobTextureLibrary` が「① ResourceLoader（インポート済み）→ ② `Image.LoadFromFile` による
生ディスク復号フォールバック」の2段で安全にロードするため、Godot がまだ `.import` を生成していない
「ソースから起動」状態でも画像が表示されます（性別 male/female は当該ユニットの性別で読み分け）。

---

## プロジェクトの鉄の憲法（設計の四柱）

1. **不変 SoT（唯一の真実の源: `ChronicleGlobal`）**
   `/root/ChronicleGlobal` という autoload シングルトンだけが、全ゲーム状態（経済・タイムライン・
   ロスター・戦闘スナップショット・年代記ログ・英霊アーカイブ）を保持します。すべての変更はここを
   通り、シグナル（EconomyChanged / TimelineChanged / RosterChanged / BattleChanged / FormationChanged /
   PhaseChanged）で観測側へ伝わります。

2. **無状態 UI（Stateless UI）**
   ビューはゲーム変数を一切キャッシュしません。描画のたびに SoT をその場で読み直し、ラベルや
   HP バーへ一方通行で流し込みます（Push バインド）。保持するのは二重実行ガード等の UI ラッチのみで、
   ゲームデータは決して持ちません。

3. **完全リークフリーなライフサイクル（台帳 + ノード束縛 Tween）**
   動的生成したノードはビューごとの台帳（registry）に記録し、再描画の冒頭とシーン退場（`_ExitTree`）で
   `QueueFree()` して更地化します。すべての演出 Tween（Flash / CountUp / Typewriter / GrowLine 等）は
   対象ノード自身へ束縛され、ノードが解放されると自動的に失効します（コールバックは `IsInstanceValid`
   ガード付き）。シグナル購読は `_Ready` で張り、`_ExitTree`（およびビュー切替）で完全に解除します。

4. **完全決定論シード（Deterministic PRNG Seeding）**
   新規ゲームは一つのシードを注入します（`StartNewGame(seed)`）。同じシードは同じ100年を再現します。
   ロジックは副作用なしで外部からシードされるため、環境に依存しません。

> 補足 — 開発憲法①（厳格 ASCII）: `Core/` および UI 層の識別子・コンポーネント名・testid・
> 内部ログ・アセットパス・コメントはすべて ASCII のみ。プレイヤー向けの表示文字列のみ日本語を許可します。
> 本書のような人間向けドキュメントは日本語で記述します。

---

## 主なゲームの掟（実装と一致）

- 大隊規模: 9 名（▲ウェッジ陣形 = 前衛1分隊 + 後衛-左/後衛-右の2分隊、各3スロット）。
- 無人出撃の封鎖: 盤面に最低1名を配置しないと編成→戦闘へ前進できません（`DeploymentGate`）。
- 婚姻は男女ペア限定（父=男性 / 母=女性）。同性・性別逆転の組は UI と SoT 双方で拒絶します。
- 新人入団・子供雇用・老兵引退・解雇はすべて手動選択。

---

## 実機検収の結果（この環境で確認済み）

- `dotnet build ChronicleKnights.csproj --configuration Debug` → 0 警告 / 0 エラー。
- `dotnet test Tests/ChronicleKnights.Tests.csproj`（環境変数なし、焼き込んだ roll-forward で net8.0→net10）
  → 失敗 0 / 合格 630 / 警告 0。
- 実機起動（Intel Mac / macOS 12.7.6 / Godot 4.3 .NET）→ Vulkan(Forward+) でウィンドウ描画、
  ジョブ画像ロード・.NET 解決エラーなし。

旅団長、大回廊は開き、聖典は据えられ、実機の光は放たれました。出陣の刻にございます。
