using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UObject = UnityEngine.Object;
using URandom = UnityEngine.Random;

namespace MoreEvents;

/// <summary>Finding the game's objects, players and enemies; spawning; rewards (host).</summary>
internal static class Game
{
    private static RunManager run;
    private static EnemySpawner spawner;
    private static PartyLevelManager party;
    private static ReviveManager revives;
    private static StoreManager store;
    private static ObjectiveDirector objectives;

    internal static bool Alive(UObject o) => o is not null && !o.WasCollected && o != null;

    private static readonly Dictionary<Type, float> nextLookup = new();

    // Re-scans the scene at most once a second while the object doesn't exist (e.g. in the lobby).
    private static T Find<T>(ref T cache) where T : UObject
    {
        if (Alive(cache)) return cache;
        if (nextLookup.TryGetValue(typeof(T), out var t) && Time.unscaledTime < t) return null;
        cache = UObject.FindObjectOfType<T>();
        if (!Alive(cache)) { cache = null; nextLookup[typeof(T)] = Time.unscaledTime + 1f; }
        return cache;
    }

    internal static RunManager Run => Find(ref run);
    internal static EnemySpawner Spawner => Find(ref spawner);
    internal static PartyLevelManager Party => Find(ref party);
    internal static ReviveManager Revives => Find(ref revives);
    internal static StoreManager Store => Find(ref store);
    internal static ObjectiveDirector Objectives => Find(ref objectives);

    internal static bool IsServer
    {
        get { try { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening && nm.IsServer; } catch { return false; } }
    }

    internal static bool Listening
    {
        get { try { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening; } catch { return false; } }
    }

    // ---------- players ----------
    // GameplayPlayerIdentity is only the player's record: it stays put and has no health. The wizard you move is a
    // separate GameplayPlayer object (swapped for a "dead player" object while down), so positions and health come
    // from GameplayPlayer.

    private static List<GameplayPlayer> players = new();
    private static float nextPlayerScan;

    /// <summary>The players' wizards, re-scanned twice a second (FindObjectsOfType is too slow for every frame).</summary>
    internal static List<GameplayPlayer> Players()
    {
        if (Time.unscaledTime >= nextPlayerScan)
        {
            nextPlayerScan = Time.unscaledTime + 0.5f;
            var list = new List<GameplayPlayer>();
            foreach (var p in UObject.FindObjectsOfType<GameplayPlayer>())
                if (p != null && p.IsSpawned) list.Add(p);
            players = list;
        }
        players.RemoveAll(p => !Alive(p));
        return players;
    }

    internal static PlayerHealthContainer HealthOf(GameplayPlayer p)
    {
        try
        {
            var h = p.HealthContainer;
            if (!Alive(h)) h = p.GetComponentInChildren<PlayerHealthContainer>(true);
            return Alive(h) ? h : null;
        }
        catch { return null; }
    }

    internal static Vector3 PositionOf(GameplayPlayer p)
    {
        try { return p.GetPosition(); } catch { return p.transform.position; }
    }

    internal static bool IsAlive(GameplayPlayer p)
    {
        try
        {
            if (!p.gameObject.activeInHierarchy) return false;
            var h = HealthOf(p);
            return h == null || !h.IsHealthZero();
        }
        catch { return false; }
    }

    internal static List<Vector3> LivingPlayerPositions()
    {
        var list = new List<Vector3>();
        foreach (var p in Players())
            if (IsAlive(p)) list.Add(PositionOf(p));
        return list;
    }

    internal static Vector3 PartyCentre()
    {
        var ps = LivingPlayerPositions();
        if (ps.Count == 0) foreach (var p in Players()) ps.Add(PositionOf(p));
        if (ps.Count == 0) return Vector3.zero;
        var c = Vector3.zero;
        foreach (var p in ps) c += p;
        return c / ps.Count;
    }

    internal static GameplayPlayer LocalPlayer()
    {
        foreach (var p in Players())
            if (p.IsOwner) return p;
        return null;
    }

    internal static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // ---------- positions ----------

    internal static Vector3 Snap(Vector3 p, float range = 12f)
    {
        try
        {
            if (NavMesh.SamplePosition(p, out var hit, range, -1)) return hit.position;
        }
        catch { }
        return p;
    }

    internal static bool OnNavMesh(Vector3 p, float range, out Vector3 snapped)
    {
        snapped = p;
        try
        {
            if (NavMesh.SamplePosition(p, out var hit, range, -1)) { snapped = hit.position; return true; }
        }
        catch { return true; }
        return false;
    }

    /// <summary>A walkable point between min and max distance from the centre (tries a few times).</summary>
    internal static Vector3 PointInRing(Vector3 centre, float min, float max)
    {
        for (int i = 0; i < 12; i++)
        {
            float a = URandom.Range(0f, Mathf.PI * 2f), r = URandom.Range(min, max);
            var p = centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            if (OnNavMesh(p, 4f, out var s)) return s;
        }
        return Snap(centre + new Vector3(min, 0f, 0f));
    }

    /// <summary>One of the map's objective spawn points between min and max from the centre, else a ring point.</summary>
    internal static Vector3 ObjectivePoint(Vector3 centre, float min, float max)
    {
        try
        {
            var options = new List<Vector3>();
            foreach (var sp in UObject.FindObjectsOfType<ObjectiveSpawnPoint>())
            {
                if (sp == null) continue;
                float d = FlatDistance(sp.transform.position, centre);
                if (d >= min && d <= max) options.Add(sp.transform.position);
            }
            if (options.Count > 0) return Snap(options[URandom.Range(0, options.Count)]);
        }
        catch { }
        return PointInRing(centre, min, max);
    }

    // ---------- enemies ----------

    internal static NetworkObject Spawn(EnemyType type, Vector3 position)
    {
        var sp = Spawner;
        if (sp == null) return null;
        var dict = sp.enemyTypeToPrefab;
        if (dict == null || !dict.ContainsKey(type)) return null;
        var prefab = dict[type];
        if (prefab == null) return null;
        return sp.SpawnEnemy(prefab, Snap(position), EnemySpawnRotation.TowardsClosestPlayer);
    }

    internal static bool CanSpawn(EnemyType type)
    {
        try { var d = Spawner?.enemyTypeToPrefab; return d != null && d.ContainsKey(type) && d[type] != null; }
        catch { return false; }
    }

    /// <summary>A random enemy of the current wave at a position (the game's own pick).</summary>
    internal static void SpawnWaveEnemy(Vector3 position)
    {
        try
        {
            var sp = Spawner;
            var wave = sp?.currentWave;
            if (sp == null || wave == null) return;
            sp.SpawnRandomEnemy(wave, Snap(position));
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Spawning a wave enemy: {ex.Message}"); }
    }

    /// <summary>An enemy an event spawned: its health is looked up once (dying switches the object off at once,
    /// before it's despawned, and an inactive child isn't found by a normal lookup).</summary>
    internal sealed class Tracked
    {
        public NetworkObject Obj;
        public EnemyHealthContainer Health;
        public bool Dead;
    }

    internal static Tracked Track(NetworkObject o)
    {
        if (o == null) return null;
        var t = new Tracked { Obj = o };
        try { t.Health = o.GetComponentInChildren<EnemyHealthContainer>(true); } catch { }
        try
        {
            // Event enemies mustn't be removed by the spawner's "off screen too long" clean-up.
            var e = o.GetComponentInChildren<Enemy>(true);
            if (e != null) e.SetOverrideImmuneToDespawning(true);
        }
        catch { }
        return t;
    }

    internal static NetworkObject SpawnTracked(EnemyType type, Vector3 position, out Tracked tracked)
    {
        var o = Spawn(type, position);
        tracked = Track(o);
        return o;
    }

    internal enum Fate { Alive, Killed, Gone }

    internal static Fate FateOf(Tracked t)
    {
        if (t == null) return Fate.Gone;
        if (t.Dead) return Fate.Killed;
        try
        {
            if (Alive(t.Health) && t.Health.IsHealthZero()) { t.Dead = true; return Fate.Killed; }
            if (!Alive(t.Obj) || !t.Obj.IsSpawned) return Fate.Gone;
            if (!t.Obj.gameObject.activeInHierarchy) return Fate.Gone;
        }
        catch { return Fate.Gone; }
        return Fate.Alive;
    }

    internal static void Despawn(NetworkObject o)
    {
        try { if (Alive(o) && o.IsSpawned) o.Despawn(true); } catch { }
    }

    internal static List<Vector3> EnemyPositions() // the caller throttles this
    {
        var list = new List<Vector3>();
        foreach (var e in UObject.FindObjectsOfType<EnemyHealthContainer>())
        {
            try { if (e != null && e.gameObject.activeInHierarchy && !e.IsHealthZero()) list.Add(e.transform.position); }
            catch { }
        }
        return list;
    }

    // ---------- elites ----------

    internal static RunConfiguration ConfigFor(RunManager rm, int world) => world switch
    {
        0 => rm.verdantMeadowsConfiguration,
        1 => rm.frozenAncientsConfiguration,
        2 => rm.pyrestormPitConfiguration,
        3 => rm.astralPlanesConfiguration,
        _ => null,
    };

    /// <summary>Elite lists of the realms: this run's realm first, then the others.</summary>
    internal static List<(int world, Il2CppSystem.Collections.Generic.List<EliteEnemyConfiguration> elites)> EliteLists()
    {
        var result = new List<(int, Il2CppSystem.Collections.Generic.List<EliteEnemyConfiguration>)>();
        var rm = Run;
        if (rm == null) return result;
        IntPtr current = IntPtr.Zero;
        try { current = rm.Configuration?.Pointer ?? IntPtr.Zero; } catch { }
        for (int w = 0; w < 4; w++)
        {
            var cfg = ConfigFor(rm, w);
            if (cfg?.EliteEnemies == null || cfg.EliteEnemies.Count == 0) continue;
            if (cfg.Pointer == current) result.Insert(0, (w, cfg.EliteEnemies));
            else result.Add((w, cfg.EliteEnemies));
        }
        return result;
    }

    internal static EnemyType? RandomEliteFrom(Il2CppSystem.Collections.Generic.List<EliteEnemyConfiguration> list)
    {
        for (int tries = 0; tries < 6 && list.Count > 0; tries++)
        {
            var e = list[URandom.Range(0, list.Count)];
            if (e == null) continue;
            var t = e.GetRandomEnemy();
            if (CanSpawn(t)) return t;
        }
        return null;
    }

    internal static readonly string[] RealmNames = { "Verdant Meadows", "Frozen Ancients", "Pyrestorm Pit", "Astral Planes" };

    // ---------- rewards (host) ----------

    internal static string GiveXp(float percentOfLevel)
    {
        try
        {
            var p = Party;
            if (p == null) return "";
            var st = p.GetState();
            float xp = Mathf.Max(1f, st.RequiredExperience * percentOfLevel / 100f);
            p.TryAddExperience(xp);
            return $"+{Mathf.RoundToInt(percentOfLevel)}% XP";
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"XP reward: {ex.Message}"); return ""; }
    }

    internal static string GiveShop(StoreType type)
    {
        try
        {
            var s = Store;
            if (s == null) return "";
            s.OfferStoreToAllPlayers(type);
            return type == StoreType.Artifact ? "Bonus artifact for everyone" : "Bonus upgrade for everyone";
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Shop reward: {ex.Message}"); return ""; }
    }

    internal static string GiveRevive()
    {
        try
        {
            var r = Revives;
            if (r?.CurrentRevives == null) return GiveXp(20f);
            int total = r.CalculateTotalRevives();
            if (total > 0 && r.CurrentRevives.Value >= total) return GiveXp(20f) + " (revives full)";
            r.CurrentRevives.Value = r.CurrentRevives.Value + 1;
            return "+1 Revive";
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Revive reward: {ex.Message}"); return ""; }
    }
}
