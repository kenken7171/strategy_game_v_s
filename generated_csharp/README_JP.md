# Chronicle Knights -- Ketteironteki 100-Nen Kuroniku RPG

(Nihongo Romaji-ban Kidou Seiten. Subete ASCII de kisai shite ari, donna compile kankyou demo
binary anzen ni yomeru. Eigo-ban wa README.md wo sanshou.)

> Ryodanchou, Dotnet no Pass wo Tooshi, Jikki no Hikari wo Tokihanate!!!

---

## Project Title & Architecture

**Chronicle Knights** -- Ketteironteki (deterministic) 100-Nen Kuroniku RPG.
Godot 4 / .NET 8 / C# 12. Kishi-dan (brigade) wo hikiite, tatta hitotsu no seed kara kessei
sareru 100-nen no rekishi wo kakenukeru. 1-nen = dai-kairou (grand corridor) no 1-shuu:

```
Title  ->  Hub  ->  Battle  ->  Settlement  ->  Hub  ->  ...
(seed)   (keizai/    (tekii      (todome +      (tsugi
         yogen/      yochi +     senka kanryuu  no nen)
         senryoku)   kessen)     + nendaiki)
```

Onaji seed kara wa onaji 100-nen ga saigen sareru (kanzen ni ketteironteki).

---

## Prerequisites

- **.NET 8 SDK** (project wa `net8.0` target, C# 12). **.NET 10 SDK** demo doukou suru
  (roll-forward de testhost wo 10.x runtime jou de jikkou kanou).
- **Godot Engine 4.x** with .NET / C# support (Godot.NET.Sdk 4.3.0 de kakunin zumi).

---

## Mac / zsh de 'command not found: dotnet' ga deta baai

Kore wa dotnet ni Pass ga tootte inai dake. Tsugi no dochiraka de kaiketsu suru.

**Houhou 1 -- Homebrew de install:**

```sh
brew install --cask dotnet-sdk
```

**Houhou 2 -- .zshrc he Pass wo tooshi (sudeni install zumi no baai):**

```sh
echo 'export PATH="$PATH:/usr/local/share/dotnet"' >> ~/.zshrc
source ~/.zshrc
```

Kakunin:

```sh
dotnet --version
```

(Windows no baai wa winget install Microsoft.DotNet.SDK.8, mata wa koushiki installer.
Godot wa .NET-tsuki no "Mono" build wo dotnet site kara nyuushu suru koto.)

---

## Build & Run (CLI)

Subete kono directory (`generated_csharp/`) kara jikkou suru.

**Build (Debug):**

```sh
dotnet build ChronicleKnights.csproj --configuration Debug
```

**Game wo kidou (Godot editor / windowed):**

```sh
godot --path .
```

Main.tscn ga tachiagari, mujoutai view router (UserInterfaceRoot) ga Title gamen kara boot suru.

**Test wo jikkou (xUnit contracts):**

```sh
dotnet test Tests/ChronicleKnights.Tests.csproj
```

8.0 runtime ga naku 10.x dake no baai wa roll-forward de jikkou:

```sh
DOTNET_ROLL_FORWARD=LatestMajor dotnet test Tests/ChronicleKnights.Tests.csproj
```

---

## Tetsu no Kenpo (Architectural Pillars)

1. **Fuben SoT (Single Source of Truth: `ChronicleGlobal`)**
   `/root/ChronicleGlobal` no autoload singleton dake ga zen game joutai (keizai, timeline, roster,
   battle snapshot, nendaiki log, eirei archive) wo motsu. Subete no henkou wa koko wo tooshi,
   signal (EconomyChanged / TimelineChanged / RosterChanged / BattleChanged / PhaseChanged) de tsutaeru.

2. **Mushitai UI (Stateless UI)**
   View wa game hensuu wo issai cache shinai. Egaku tabi ni SoT wo sono ba de yominaoshi, label / bar
   he ichihoukou ni nagasu (push bind). Hoyuu suru no wa nijuu jikkou guard nado no UI latch dake de,
   game data wa kessite motanai.

3. **Leak-Free Lifecycle (4-Dai Daicho + Node-Bound Tweens)**
   Dousei seisei shita node wa view goto no daicho (ledger) ni kiroku shi, saibyouga no boutou to
   `_ExitTree` de `QueueFree()` shite koushichika suru:
   `_timelineNodes` / `_rosterNodes` / `_battleNodes` / `_settlementNodes`.
   Subete no juice tween (Flash / CountUp / Typewriter) wa taishou node he bind sare, node ga free
   sareru to jidou shikkou suru (callback wa `IsInstanceValid` guard tsuki). Signal koudoku wa
   `_Ready` de hari, `_ExitTree` (oyobi view kirikae) de kanzen kaijo suru.

4. **Ketteironteki PRNG Seeding (Deterministic PRNG)**
   Shinki game wa hitotsu no seed wo chuunyuu (`StartNewGame(seed)`). Onaji seed wa onaji 100-nen wo
   saigen suru. Logic wa fukusayou nashi de gaibu seed sareru tame, kankyou ni izon shinai.

5. **Kaihatsu Kenpo I (Strict ASCII)**
   `Core/` to `UserInterface/` no shikibetsushi, component-mei, test id, hyouji text, comment wa
   subete ASCII nomi (hi-ASCII byte zero). Hyouji you no localized label wa `Config/` no key kara kaiketsu.

---

## Jikki Kenshuu Kekka (Verification, kono kankyou de kakunin zumi)

- `dotnet build ChronicleKnights.csproj --configuration Debug` -> 0 keikoku / 0 error.
- `dotnet test` (net8.0 wo 10.x he roll-forward) -> shippai 0 / goukaku 614 / keikoku 0.

Ryodanchou, dai-kairou wa hiraki, seiten wa oki, jikki no hikari wa hanatareta. Shutsujin no toki nari.
