# 🎣 How to Fish — Mod Menu by Vlad

> Memory-only mod menu for the game **How to Fish** · temporary until restart · press **F1** in-game.
> A PLITCH-style overlay with sidebar, search, cheat cards, ESP and aimbot — solo, host and joiner supported.

▶ Made by **Vlad** — YouTube: **[@vladuism](https://www.youtube.com/@vladuism)**

---

## ✨ Features

| Tab | What you get |
|---|---|
| **Player** | God mode (no damage / no drowning), infinite fullness, speed & jump multipliers, max vitals |
| **Money** | Infinite money (kept in multiplayer — free purchases via RPC), auto-sell fish *(host)*, +$99999 *(host)*, all baits free |
| **Fishing** | Instant catch, auto fish, always shiny, fish size multiplier, duplicate items *(host)*, creature spawner *(host)* |
| **Teleport** | Island swaps *(host)*, MP-safe self-teleport to any island position, jump to / pull players, damage & one-shot players, revive all |
| **Weapons** | Infinite ammo, no cooldown, refill, free bullet / attachment / sharpness upgrades, **aimbot for players, fish and bosses** (range + FOV sliders, camera snap) |
| **Casino** | Rigged roulette + jackpot slots *(host)* |
| **World** | Damage multiplier, one-shot toggle, ocean level, explosions / boss spawn / kill all *(host)*, sunset, ESP overlay (fish, players, loot, **islands**) |
| **Items** | Item browser (give anything free), unlock all skins, unlock all achievements |
| **Unlocks** | Boat + radar *(host)*, grill *(host)*, inventory pockets (works in multiplayer), presets, keybind rebinding |

Use the **search bar** at the top of the menu to filter every cheat instantly. The sidebar shows how many cheats are ON per tab.

**Solo / host vs joiner:** singleplayer and hosting = every feature works. Joining someone else's lobby =
island swaps, money pool edits, spawns, boat/grill unlocks and casino rigging stay host-only
(FishNet is host-authoritative — the game simply ignores those calls from clients).
Everything RPC-based keeps working as a joiner: player teleports, damage, aimbot hits,
free purchases with Infinite Money, pockets, revive, plus client-side features
(god, ESP, instant catch, ammo).

---

## 🚀 Usage

1. Launch the game and reach the main menu.
2. Run **`HowToFishInjector.exe` as administrator**
   (keep `VladMod.Core.dll` next to it — that's the only file it needs).
3. You should see `Injected OK` — press **F1** in-game. 🎮

The injector is at `Injector/bin/Release/` after building.
Every load is logged to `How to Fish_Data/VladModLog.txt`.

---

## ⌨️ Keybinds

| Key | Action |
|---|---|
| **F1 / Insert** | Open / close the menu |
| **F2** | God mode |
| **F3** | Infinite money |
| **F4** | Instant catch |
| **F5** | Auto fish |
| **F6** | ESP overlay |

All rebindable from the Unlocks tab — click **Rebind**, press a key.
**Esc** cancels, **Backspace** unbinds. Settings auto-save to
`How to Fish_Data/vladmod.cfg`.

---

## 🛠️ Build from source

Requires the **.NET 8 SDK** (Windows).

```bash
dotnet build HowToFishModMenu.sln -c Release
```

Or open `HowToFishModMenu.sln` in Visual Studio / VS Code (`Ctrl+Shift+B`).

```
howtofish-modmenu/
├── Core/                     # the mod — menu, patches, config, logging
│   ├── ModCore.cs            # entry point, IMGUI menu, auto loops
│   ├── Patches.cs            # 21 Harmony patches
│   ├── Deps.cs               # memory-only dependency loader
│   ├── Items.cs              # item / creature lookups
│   ├── Cfg.cs                # tiny ini config (debounced saves)
│   └── Log.cs                # file logger
├── Standalone/               # builds VladMod.Core.dll (the injectable DLL)
│   └── Libs/                 # Harmony + MonoMod + Cecil, embedded as resources
├── Injector/                 # builds HowToFishInjector.exe
├── ThirdParty/               # vendored SharpMonoInjector (MIT)
└── HowToFishModMenu.sln
```

---

## 🧠 How it works

[SharpMonoInjector](https://github.com/warbler/SharpMonoInjector) (MIT, vendored
under `ThirdParty/`) loads `VladMod.Core.dll` straight into the game's Mono
runtime from a byte array. Harmony and its dependencies are **embedded inside the
DLL** and resolved from memory (`Core/Deps.cs`) — nothing is ever written to the
game folder, and restarting the game unloads everything.

---

## ❓ Troubleshooting

- **`Could not find a process`** — launch the game first, then run the injector.
- **`Access denied` / injection failed** — run `HowToFishInjector.exe` as administrator.
- **Menu won't open** — check `How to Fish_Data/VladModLog.txt`. No log = injection
  didn't run. Log with errors = paste them in a comment on the
  [@vladuism](https://www.youtube.com/@vladuism) channel.
- **Menu worked, then a game update broke it** — updates can rename game code the
  patches hook into. Rescanning the new assembly is needed.

---

## 🧹 Uninstall

Restart the game without running the injector — the mod only lives in memory.
Your save is untouched. For zero trace, delete:

- `How to Fish_Data/vladmod.cfg`
- `How to Fish_Data/VladModLog.txt`
