<!--
  Ready-to-paste description for the 7D2DMods mod page (Details -> Description, Markdown mode).
  Screenshots are uploaded separately under the Media tab, so this text embeds no images.
  Keep this in sync with README.md when features change.
-->

# UndeadLoot Revived

**A 7 Days to Die V3.0 rework of _UndeadLoot_ by 7ModsToDead — loot the *actual* bodies of the dead.**

Kill a zombie, walk up to its corpse, and search it like any other container. No decoy chest, no fake body spawned next to it — you loot the real ragdoll where it fell. Static corpses lying around in POIs are lootable too.

## Features

- **Loot the real defeated body** — press your Use key (E) on a dead zombie to search its corpse. It's the actual body, not a spawned block or bag.
- **Instanced multiplayer loot** — in multiplayer, each player gets their own independent loot roll from the same zombie corpse. No competing over shared bags. The body stays the body — nothing extra is spawned.
- **Per-player loot state** — green = *you* haven't opened this body yet, orange = you have but items remain, gray = your loot is gone. Every player sees their own independent state on every corpse.
- **Game-wide color-coded prompts** — instant state recognition with the colorblind-safe Okabe–Ito palette, applied to **all** containers, corpses, doors, workstations, and pickups:
  - 🟢 **Green** = untouched / unlocked
  - 🟠 **Orange** = opened
  - ⚪ **Gray** = empty
  - 🔴 **Red** = locked
- **Configurable action-key hint** — the Use key (e.g. `(E)`) is tinted sky blue by default on **every** interaction prompt in the game, and follows your key rebinding. Change `activateKeyColor` in `Config/Localization.csv` to any six-digit RGB hex color. Other hints (reload, jump, …) are left alone.
- **Static POI corpses are lootable.**
- **Every humanoid zombie type & tier** — feral / radiated / **charged** / **infernal**.
- **Per-type loot** — cops/mutated (weapons, ammo), soldiers/demolishers, nurses (medical), businessmen (money), hazmat (chemistry), wights (elite), and a balanced generic default.
- **Modded-zombie friendly** — any zombie from another mod is looted automatically (generic table, or themed by name, e.g. a modded "…Nurse" gets medical loot).
- Loot respects loot stage / game scaling. Corpses stay harvestable as before.

## Compatibility

**Works with InstancedLoot by Kobonator.** If both mods are installed, UndeadLoot detects InstancedLoot at startup and skips its own instancing, deferring per-player distribution to that mod. The combo works better than either alone: UndeadLoot provides the lootable corpse, InstancedLoot handles distribution across all container types.

## Requirements

- **7 Days to Die V3.0** — this is a code mod compiled for V3.0; a major game update may need a rebuild.
- **EasyAntiCheat MUST be OFF** — DLL/Harmony mods do not load with EAC on.
- Do not run alongside other mods that recolor loot text (e.g. ColoredLootText) — they fight over the same strings. UndeadLoot already does the coloring.

## Installation

1. Download the latest release zip.
2. Extract the `UndeadLoot` folder into `7 Days To Die/Mods/`.
3. Launch the game **with EasyAntiCheat disabled**.
4. Recommended: start on a **new save**.

**Multiplayer:** install on the **server and every client**, all with EAC off (it patches client-side interaction/UI, so server-only is not enough).

## Credits & license

- **Original mod, concept, and loot tables:** 7ModsToDead
- **V3.0 rework (code + mechanics):** iotshelnik
- Used and modified under the original's terms — _"free to use and modify, with credit and a link to the original source."_

## Changelog

**1.3.0**
- Instanced multiplayer loot: each player gets their own independent loot roll per corpse. Loot is generated on first open, saved on close, cleared on world restart.
- Activation text now shows per-player state rather than shared bag state.
- Auto-detects InstancedLoot by Kobonator; instancing patches are skipped when it is present so both mods cooperate without conflict.

**1.2.2**
- Fixed dedicated-server multiplayer: remote clients can now see the Use prompt and loot zombie bodies. The server remains authoritative for corpse locking and loot synchronization.

**1.2.1**
- The game-wide Use-key color can now be changed with `activateKeyColor` in `Config/Localization.csv`. Invalid values fall back safely to the default sky blue.

**1.2.0**
- The Use key hint — e.g. `(E)` — is now colored sky blue on **every** interaction prompt game-wide (workstations, doors, pickups, vehicles, NPCs, containers, and more), not just loot prompts. Done at the source, so it follows your Use-key rebinding.

**1.1.0**
- Game-wide color-coded loot prompts (colorblind-safe Okabe–Ito palette).
- Full humanoid-zombie coverage: every vanilla type and all tiers (feral / radiated / charged / infernal), plus automatic handling of modded zombies.
- New themed loot tables for Hazmat and Wight zombies.

**1.0.0**
- Initial release. Rework of UndeadLoot for game V3.0: real dead bodies lootable via a Harmony DLL, native loot prompt with untouched/opened/empty states, static POI corpses lootable, per-type loot tables restored.
