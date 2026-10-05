# Changelog

All notable changes to the Public Lockers plugin.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.2.0] — 2026-10-05

### Added
- **Placed lockers** — `/locker place` spawns a locker terminal where the admin stands, for
  monuments that have no lockers of their own. It is stored relative to the monument like a
  marked spot, can't be damaged or picked up, and is never written to the world save.
- `/locker list` says whether each spot is a marked spot or a spawned locker.

## [0.1.0] — 2026-10-04

First release. Playtested on a live server.

### Added
- **Global lockers** — one private locker per player, opened from any terminal on the map.
- **Terminals** — the lockers already standing in monuments. An admin looks at one and runs
  `/locker add`; the spot is stored relative to the monument, so it survives map wipes and
  applies to every copy of that monument. Players press the use key on it. Monuments with
  no bounds of their own (the lighthouse) are matched by distance.
- **Daily rent** — an unpaid locker locks and keeps its contents for a grace period; after
  that they are held until the rent is paid, or destroyed if the server is set that way.
- **Paying through Public Works** (2.11.0 and later) — a "Public locker" row on the office's
  MY ACCOUNTS page, at the clerk and over the phone line.
- **Standalone rent** — `/locker rent [days]` takes scrap at a terminal when Public Works is
  not loaded.
- **Barred items** — an optional list of shortnames a locker refuses (empty by default).
- 48-slot lockers by default.
- **Storage that follows the world save** — the data file is written with the server save
  and on unload, so a crash rolls lockers and inventories back to the same moment.
- Optional emptying of every locker on a map wipe (on by default; paid rent carries over).
- `GetHelpInfo` hook for the HelpMenu plugin. The page lists where the locker terminals are
  on the current map (monument and map grid) and refreshes when one is marked or removed.
