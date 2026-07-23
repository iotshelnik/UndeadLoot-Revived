# Changelog

## 1.3.1

- Fixed activation text staying orange after the player took all items from a corpse. The
  prompt now transitions to gray immediately when the bag is closed empty, with no extra
  re-open required.
- Fixed `ReadBagSlots` always returning null: replaced reflection field guessing with a
  direct `bag.GetSlots()` call.
- Fixed client-lock tracking being consumed prematurely by internal unlock calls during
  lock acquisition. The client lock is now captured in `OnLockedLocal` (after the lock is
  confirmed) rather than in `OnEntityActivated`.
- Removed incorrect InstancedLoot compatibility shim. InstancedLoot does not handle zombie
  corpses, so UndeadLoot always runs its own per-player instancing. The two mods cover
  different loot types and work side by side without conflict.

## 1.3.0

- **Instanced multiplayer loot.** Each player who opens a zombie corpse now gets their own
  personal loot roll from the same body. Players no longer compete over shared bag contents —
  one player taking items has no effect on what another player finds. The body stays the body:
  no bag, block, or entity is spawned. The loot is generated lazily on each player's first
  open and saved across re-opens (remaining items persist if they close and re-open the body).
- Activation text now reflects per-player state: green = *you* haven't opened this corpse yet,
  orange = you have but items remain, gray = your loot is gone. Other players see their own
  independent state on the same corpse.
- **Compatible with InstancedLoot by Kobonator.** If both mods are installed, UndeadLoot
  detects it at startup and skips its own instancing patches, deferring per-player loot
  distribution to InstancedLoot. The combination works better than either mod alone:
  UndeadLoot provides the lootable corpse, InstancedLoot handles distribution.
- Loot data is cleared on world load/restart to prevent stale state across sessions.

## 1.2.2

- Fixed dedicated-server multiplayer corpse interaction for remote clients.
- Replicated zombie corpses now receive local interaction state on every peer, allowing non-host
  players to see the Use prompt and request the server-authoritative loot bag.
- Verified with a separate game client connected to a dedicated server.

## 1.2.1

- Made the game-wide Activate-key color configurable through `activateKeyColor` in
  `Config/Localization.csv`.
- Accepts any six-digit RGB hex value, with or without a leading `#`.
- Invalid or missing values safely fall back to the default sky blue (`56B4E9`).

## 1.2.0

- Colored the Activate (Use) key hint on every interaction prompt game-wide while preserving
  user key bindings.

## 1.1.0

- Added game-wide color-coded loot prompts using the colorblind-safe Okabe–Ito palette.
- Added full humanoid-zombie and modded-zombie coverage.
- Added themed loot tables for Hazmat and Wight zombies.

## 1.0.0

- Initial 7 Days to Die V3.0 release with lootable real zombie bodies, static POI corpses,
  native loot prompts, and restored per-type loot tables.
