## 1.0.5
- **Events keep clear of the game's own.** An event now only starts when none of the game's objectives (fungi, statues, capture zones...), the boss or its enemy events (the line of dogs, the mob...) is running or due while it lasts, with a quiet gap either side (new setting `General.GapSeconds`, 30 s). It knows which waves bring an objective and when the boss is due. If an objective or the boss starts anyway, the event is called off with no penalty.

## 1.0.4
- **Fixed King of the Hill and Escort the Fungus.** The mod was reading each player's position from an object that never moves with the wizard, so it never saw anyone in the zone: the hill never filled and the fungus never moved. It now tracks the wizards themselves.
- The same fix applies to the other events: bounty targets, golden crabs and gauntlet elites now spawn around where the party actually is, and Cursed Ground checks where you really are.

## 1.0.3
- **Everyone uses the host's settings.** While you're in someone's game, this mod reads the host's config instead of yours, from the lobby on. Your own `StartEventKey` stays yours. Your config file isn't changed, and your own values are back when you leave. If the host doesn't have the mod, it stays off for you in that game.

## 1.0.2
- Fixed events not showing for other players: their games rejected every event update from the host (wrong field count).

## 1.0.1
- Fixed the host failing to send event state ('Method unstripping failed'), so other players now see the banner, rings and markers.

## 1.0.0
- First release: Bounty, King of the Hill, Golden Swarm, Escort the Fungus, Cursed Ground and Elite Gauntlet.
