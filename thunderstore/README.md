# MoreEvents

Six new events that pop up during runs, between the game's own objectives.

| Event | What happens | Reward |
|---|---|---|
| **Bounty** | An elite spawns, marked with a red ring and a skull. Kill it within 45 seconds. | A bonus upgrade for everyone. If it escapes, the next wave's enemies get +30% health. |
| **King of the Hill** | A zone appears. Stand in it while enemies pour in around it. More players inside fill the bar faster, and it drains while the zone is empty. | A bonus upgrade for everyone. |
| **Golden Swarm** | Golden crabs scatter around you. Catch them before they escape. | XP for each crab, and a bonus artifact for everyone if you catch them all. |
| **Escort the Fungus** | A fungus spirit walks toward a flag, but only while someone is near it. Enemies that reach it eat away its health. | A bonus artifact for everyone. |
| **Cursed Ground** | For 60 seconds, a safe circle shrinks and drifts. Standing outside it costs 5% of your health per second. | +50% of a level's XP. |
| **Elite Gauntlet** | Elites from different realms come one after another. | +1 revive for each one you kill (XP instead if revives are full). |

Each event shows a banner (title, timer, progress bar), rings on the ground, and icons over its targets. Icons stick to the screen edge when the target is off screen.

- The first event comes 2 minutes into a run, and then one roughly every 2.5–3.5 minutes after the last one ends.
- Events wait while one of the game's own objectives, or the boss, is active.
- **F5** (host) starts a random event right away, which is handy for trying them out. Clear `StartEventKey` to turn this off.

**Multiplayer:** the host runs the events. Everyone who wants to see the banner and markers needs the mod. Cursed Ground's damage is applied by each player's own game, so players without the mod don't take it.

## Config (`girthy_mods.MoreEvents.cfg`)

- **`[General]`**
  - `Enabled`
  - `FirstEventAfterSeconds` (120)
  - `SecondsBetweenEvents` (150)
  - `RandomExtraSeconds` (60)
  - `WaitForObjectives` (true): keep events clear of the game's objectives, the boss and its enemy events
  - `GapSeconds` (30): quiet time kept around those
  - `StartEventKey` (F5)
- **`[Events]`**: a chance weight for each event (0 = never).
- **`[Bounty]`**: `TimeLimit`, `EscapeHealthBuffPercent`.
- **`[KingOfTheHill]`**: `TimeLimit`, `SecondsToCapture`, `ZoneRadius`.
- **`[GoldenSwarm]`**: `Crabs`, `TimeLimit`, `XpPerCrabPercent`.
- **`[EscortTheFungus]`**: `TimeLimit`, `Distance`, `Health`.
- **`[CursedGround]`**: `Duration`, `DamagePercentPerSecond`, `StartRadius`, `EndRadius`.
- **`[EliteGauntlet]`**: `Elites`, `TimeLimit`.

## Multiplayer settings

In multiplayer everyone plays with the **host's** settings: the host's config is sent to every player from the lobby on, and the mod reads it instead of yours while you're connected. Your own the event key stay yours. Your config file is never changed. If the host doesn't have this mod, it stays off for you in that game.
