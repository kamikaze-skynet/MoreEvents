# MoreEvents

A [BepInEx](https://github.com/BepInEx/BepInEx) IL2CPP mod for **The Spell Brigade** that adds six in-run events.

See [`thunderstore/README.md`](thunderstore/README.md) for the player-facing description and config.

## How it works

- **Director (host).** Starts an event when the run time passes the next slot. It skips the slot while the game is paused (timeScale 0) and, by default, while `ObjectiveDirector.ActiveObjective` is set. It picks an event by weight, ticks it each frame, and publishes its state 4×/s as a Netcode named message (`girthy_mods.MoreEvents.State`).
- **Spawning.**
  - `EnemySpawner.SpawnEnemy(enemyTypeToPrefab[type], pos, TowardsClosestPlayer)` spawns a specific enemy.
  - `EnemySpawner.SpawnRandomEnemy(currentWave, pos)` spawns pressure enemies.
  - Positions are snapped to the navmesh with `NavMesh.SamplePosition`.
- **Elites.** Taken from each realm's `RunConfiguration.EliteEnemies` via `EliteEnemyConfiguration.GetRandomEnemy()`.
- **Golden crabs.** `EnemyType.HuntTargetBaby` (falling back to `HuntTargetDefault`), the Hunt objective's targets.
- **Rewards.**
  - `StoreManager.OfferStoreToAllPlayers(LevelUp/Artifact)`, the same call artifact chests use.
  - `PartyLevelManager.TryAddExperience`.
  - `ReviveManager.CurrentRevives`, capped at `CalculateTotalRevives()`.
- **Bounty penalty.** A postfix on `EnemyHealthContainer.CalculateMaxHealth` multiplies max health during the next wave.
- **Cursed Ground.** Each player's game damages its own player while outside the circle: `RemoveHealth(…, new TakeDamageOnLevelUpCurseDamageInfo(), false)`, the call the level-up damage curse uses.
- **HUD.** A TextMeshPro banner. Rings are projected circles drawn from UI segments, and icons are runtime pixel-art sprites, clamped to the screen edge when off screen.
