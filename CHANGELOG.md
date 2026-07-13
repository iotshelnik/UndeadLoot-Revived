# Changelog

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
