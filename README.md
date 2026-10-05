# Public Lockers

*One rented locker, every terminal on the map.*

An [Oxide/uMod](https://umod.org/) plugin for **Rust**. Players rent a personal locker by
the day and open it from a locker terminal at a monument. Terminals are the lockers
already standing in the monuments; an admin marks which ones are in service. The locker is **global**: whatever
goes in at one terminal is there at every other.

Rent is paid at the [Public Works](https://github.com/lowpoplabs/PublicWorks) office when
that plugin is installed, so the island keeps a single payment desk. Without it, the
plugin takes scrap at the terminal and works on its own.

<!-- lpl:links -->
**[Download v0.2.0](https://github.com/lowpoplabs/PublicLockers/releases/latest)** · **Flyer:** [web](https://lowpoplabs.github.io/flyers/PublicLockers.html) / [PDF](PublicLockers-Flyer.pdf) · **[Changelog](CHANGELOG.md)** · **[Ko-fi](https://ko-fi.com/lowpoplabs)**
<!-- /lpl:links -->

## How to use (in-game)

1. Rent a locker: at the Public Works office, open **MY ACCOUNTS** and press **+1 DAY** on
   the *Public locker* row (the office phone line works too). On a server without Public
   Works, stand at a terminal and run `/locker rent`.
2. Look at a locker terminal at a monument and press the **use** key. Your own locker opens.
3. Keep the rent paid. When it runs out the locker locks; the contents are kept for the
   grace period and, by default, held after that until you pay again.

The locker is private to its renter. Teammates can't open it.

## Installation

1. Copy `PublicLockers.cs` into your server's `oxide/plugins/` (or `carbon/plugins/`) folder.
2. The plugin compiles and loads automatically and writes `oxide/config/PublicLockers.json`.
3. Go to each monument that should have one, look at a locker that is part of the
   monument, and run `/locker add`. Where a monument has no lockers of its own, stand where
   one should go, face the way its doors should open from, and run `/locker place`.

## Commands

| Command | Who | What it does |
| --- | --- | --- |
| `/locker` | everyone | How long your locker is paid for, and where to pay |
| `/locker rent [days]` | everyone | Pay rent in scrap at a terminal (only when Public Works is not loaded) |
| `/locker add` | admin | Mark the monument locker you are looking at (within 5 m) as a terminal |
| `/locker place` | admin | Spawn a locker where you stand, facing you, for monuments that have none |
| `/locker remove` | admin | Remove the nearest terminal, marked or placed (within 5 m) |
| `/locker list` | admin | List the marked spots, and outline the nearby ones for a few seconds |
| `/locker grant <player> <days>` | admin | Add rent to a player's locker for free |

## Permissions

- `publiclockers.admin` — the admin subcommands above.
- `publiclockers.use` — renting and opening a locker, only checked when
  `Require permission to use lockers` is on.

```bash
oxide.grant group admin publiclockers.admin
```

## Configuration

`oxide/config/PublicLockers.json`:

```json
{
  "Rent per day (scrap)": 50,
  "Locker size (slots, 1-48)": 48,
  "Maximum days of rent a player can hold in advance": 14,
  "Grace period after the rent runs out (days the locked locker keeps its contents)": 3.0,
  "Destroy the contents when the grace period ends (false = hold them until the rent is paid)": false,
  "Empty every locker on a map wipe": true,
  "Items that can't be stored (shortnames, e.g. explosive.timed; empty = nothing is refused)": [],
  "Require permission to use lockers (publiclockers.use)": false,
  "Take rent in scrap at the terminal when PublicWorks is not loaded (/locker rent)": true,
  "Distance from a terminal that counts as standing at it (meters)": 4.0,
  "Reach when using a terminal (meters)": 3.0,
  "How close to the marked spot the player must be looking (meters)": 1.0,
  "Prefab for lockers spawned with /locker place": "assets/prefabs/deployable/locker/locker.deployed.prefab",
  "Terminals (marked with /locker add)": []
}
```

Notes:

- **Terminals** are written by `/locker add`. Nothing is spawned: the plugin remembers the
  spot you were looking at and treats the scenery there as a terminal. A spot inside a
  monument is stored relative to it, so it survives map wipes and applies to every copy of
  that monument (every gas station, say). A spot outside any monument is a fixed world
  position and will not survive a map wipe.
- **Placed lockers** (`/locker place`) are for monuments with no lockers. The plugin spawns
  one at the spot, facing the way you were standing; it can't be damaged or picked up, is
  never written to the world save, and is respawned on every load. Players open it like any
  container and get their own locker. Like marks, it stands at every copy of the monument.
- **Barred items** — nothing is refused by default. List shortnames (for example
  `explosive.timed`) to keep them out of lockers.
- **Lowering the slot count** never removes items: a locker opens large enough for what is
  already inside.
- **Items tied to a world entity** (photos, cassettes and the like) are always refused,
  because they can't be rebuilt from a saved record.

## How storage works

Locker contents live in `oxide/data/PublicLockers.json`, one record per item (amount, skin,
condition, loaded ammo, attachments, text and so on). While a locker is open its items are
real items in a hidden container only the owner can see.

The data file is written **with the server's world save** and when the plugin unloads, not
on every change. A crash therefore rolls lockers and player inventories back to the same
save, so an item can't end up in both places or in neither. If the data file can't be
read at load, lockers stay closed and nothing is written until it is repaired.

## Public Works integration

With Public Works 2.11.0 or later loaded, Public Lockers registers a bill with the office.
Each player sees a *Public locker* row on the MY ACCOUNTS page showing the time left; the
button buys one more day, up to the configured maximum in advance. `/locker rent` is
switched off while the office is available.

## Support

Provided as-is. Bug reports welcome via GitHub Issues. No Discord, no custom work, no promises on turnaround. If it saved you time or you and your players enjoy it:

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/lowpoplabs)

## License

[MIT](LICENSE)
