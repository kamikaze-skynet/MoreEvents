using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreEvents;

/// <summary>One event, run on the host. Begin spawns what it needs; Tick returns null while running, then true
/// (succeeded) or false (failed); Fill describes it for the players; Finish hands out rewards or penalties.</summary>
internal abstract class RunEvent
{
    public abstract string Key { get; }
    public abstract string Title { get; }
    public abstract Color Accent { get; }
    public abstract int Weight { get; }
    protected float Started;
    protected float Elapsed => Time.time - Started;

    public bool Start() { Started = Time.time; return Begin(); }
    protected abstract bool Begin();
    public abstract bool? Tick(float dt);
    public abstract void Fill(EventState s);
    public abstract string Finish(bool success);

    /// <summary>About how long the event lasts (for keeping it clear of the game's own events).</summary>
    public virtual float Length => 75f;

    /// <summary>The game's own objective or boss started: tidy up what this event spawned (no reward, no penalty).</summary>
    public virtual void CallOff() { }

    protected static string Pretty(string id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var sb = new StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1]) && id[i - 1] != '_') sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    protected static readonly Color Gold = new(1f, 0.82f, 0.25f), Red = new(1f, 0.3f, 0.25f), Green = new(0.45f, 1f, 0.45f),
        Purple = new(0.75f, 0.35f, 1f), Orange = new(1f, 0.6f, 0.2f), White = new(0.95f, 0.95f, 0.95f);
}

// ===================================================================== Bounty
internal sealed class BountyEvent : RunEvent
{
    public override string Key => "bounty";
    public override string Title => "Bounty";
    public override Color Accent => Red;
    public override int Weight => Plugin.BountyWeight.Value;

    private Game.Tracked target;
    private string name = "";
    private float Limit => Mathf.Max(10f, Plugin.BountyTime.Value);
    public override float Length => Limit;
    public override void CallOff() { if (target != null) Game.Despawn(target.Obj); }

    protected override bool Begin()
    {
        EnemyType? type = null;
        foreach (var (_, elites) in Game.EliteLists()) { type = Game.RandomEliteFrom(elites); if (type != null) break; }
        if (type == null && Game.CanSpawn(EnemyType.Crablodon)) type = EnemyType.Crablodon;
        if (type == null) return false;
        Game.SpawnTracked(type.Value, Game.PointInRing(Game.PartyCentre(), 14f, 22f), out target);
        name = Pretty(type.Value.ToString());
        return target != null;
    }

    public override bool? Tick(float dt)
    {
        var fate = Game.FateOf(target);
        if (fate == Game.Fate.Killed) return true;
        if (fate == Game.Fate.Gone) return false;
        if (Elapsed >= Limit) { Game.Despawn(target.Obj); return false; }
        return null;
    }

    public override void Fill(EventState s)
    {
        s.Line = $"Kill the marked {name} before it escapes!";
        s.Timer = Mathf.Max(0f, Limit - Elapsed);
        if (!target.Dead && Game.Alive(target.Obj)) s.Markers.Add(new Marker { Kind = MarkerKind.Follow, NetId = target.Obj.NetworkObjectId, Icon = "skull", Color = Red, Radius = 2.2f });
    }

    public override string Finish(bool success)
    {
        if (success) return Game.GiveShop(StoreType.LevelUp);
        float pct = Mathf.Max(0f, Plugin.BountyEscapeBuff.Value);
        if (pct <= 0f) return "It got away.";
        WaveBuff.Arm(pct);
        return $"It got away: next wave's enemies have +{pct:0}% health";
    }
}

// ===================================================================== King of the Hill
internal sealed class HillEvent : RunEvent
{
    public override string Key => "hill";
    public override string Title => "King of the Hill";
    public override Color Accent => Gold;
    public override int Weight => Plugin.HillWeight.Value;

    private Vector3 zone;
    private float progress, nextSpawn;
    private int inside, spawned;
    private float Limit => Mathf.Max(15f, Plugin.HillTime.Value);
    public override float Length => Limit;
    private float Radius => Mathf.Clamp(Plugin.HillRadius.Value, 2f, 20f);

    protected override bool Begin()
    {
        zone = Game.ObjectivePoint(Game.PartyCentre(), 14f, 35f);
        return true;
    }

    public override bool? Tick(float dt)
    {
        inside = 0;
        foreach (var p in Game.LivingPlayerPositions())
            if (Game.FlatDistance(p, zone) <= Radius) inside++;

        float capture = Mathf.Max(3f, Plugin.HillCapture.Value);
        if (inside > 0) progress += dt * (1f + 0.5f * (inside - 1)) / capture;
        else progress -= dt / (capture * 1.6f);
        progress = Mathf.Clamp01(progress);

        if (Elapsed >= nextSpawn && spawned < 20)
        {
            nextSpawn = Elapsed + 2.5f;
            Game.SpawnWaveEnemy(Game.PointInRing(zone, Radius + 6f, Radius + 12f));
            spawned++;
        }

        if (progress >= 1f) return true;
        if (Elapsed >= Limit) return false;
        return null;
    }

    public override void Fill(EventState s)
    {
        s.Line = inside > 0 ? $"Holding the zone ({inside} inside)" : "Stand in the zone to claim it!";
        s.Timer = Mathf.Max(0f, Limit - Elapsed);
        s.Progress = progress;
        s.Markers.Add(new Marker { Kind = MarkerKind.Ring, Pos = zone, Radius = Radius, Color = inside > 0 ? Gold : White, Icon = "crown" });
    }

    public override string Finish(bool success) => success ? Game.GiveShop(StoreType.LevelUp) : "The zone was lost.";
}

// ===================================================================== Golden Swarm
internal sealed class SwarmEvent : RunEvent
{
    public override string Key => "swarm";
    public override string Title => "Golden Swarm";
    public override Color Accent => Gold;
    public override int Weight => Plugin.SwarmWeight.Value;

    private readonly List<Game.Tracked> crabs = new();
    private readonly List<bool> done = new();
    private int caught, total;
    private float Limit => Mathf.Max(10f, Plugin.SwarmTime.Value);
    public override float Length => Limit;
    public override void CallOff() { for (int i = 0; i < crabs.Count; i++) if (!done[i]) { done[i] = true; EndCrab(crabs[i]); } }

    protected override bool Begin()
    {
        EnemyType type;
        if (Game.CanSpawn(EnemyType.HuntTargetBaby)) type = EnemyType.HuntTargetBaby;
        else if (Game.CanSpawn(EnemyType.HuntTargetDefault)) type = EnemyType.HuntTargetDefault;
        else return false;

        var centre = Game.PartyCentre();
        int n = Mathf.Clamp(Plugin.SwarmCount.Value, 1, 20);
        for (int i = 0; i < n; i++)
        {
            Game.SpawnTracked(type, Game.PointInRing(centre, 8f, 28f), out var c);
            if (c != null) { crabs.Add(c); done.Add(false); }
        }
        total = crabs.Count;
        return total > 0;
    }

    public override bool? Tick(float dt)
    {
        int open = 0;
        for (int i = 0; i < crabs.Count; i++)
        {
            if (done[i]) continue;
            var fate = Game.FateOf(crabs[i]);
            if (fate == Game.Fate.Killed) { done[i] = true; caught++; Game.GiveXp(Mathf.Max(0f, Plugin.SwarmXp.Value)); EndCrab(crabs[i]); }
            else if (fate == Game.Fate.Gone) done[i] = true;
            else open++;
        }
        if (open == 0) return caught == total;
        if (Elapsed >= Limit)
        {
            for (int i = 0; i < crabs.Count; i++) if (!done[i]) { done[i] = true; EndCrab(crabs[i]); }
            return caught == total;
        }
        return null;
    }

    // Without the Hunt objective nothing removes a golden crab: HuntTarget.EndLifetime plays its death (0 health) or
    // escape animation and despawns it.
    private static void EndCrab(Game.Tracked c)
    {
        try
        {
            var ht = Game.Alive(c.Obj) ? c.Obj.GetComponentInChildren<HuntTarget>(true) : null;
            if (ht != null) { ht.EndLifetime(); return; }
        }
        catch { }
        Game.Despawn(c.Obj);
    }

    public override void Fill(EventState s)
    {
        s.Line = $"Catch the golden crabs! {caught}/{total}";
        s.Timer = Mathf.Max(0f, Limit - Elapsed);
        s.Progress = total > 0 ? (float)caught / total : 0f;
        for (int i = 0; i < crabs.Count; i++)
            if (!done[i] && Game.Alive(crabs[i].Obj))
                s.Markers.Add(new Marker { Kind = MarkerKind.Follow, NetId = crabs[i].Obj.NetworkObjectId, Icon = "crab", Color = Gold, Radius = 0f });
    }

    public override string Finish(bool success)
    {
        string xp = $"{caught}/{total} caught (+{Plugin.SwarmXp.Value * caught:0}% XP)";
        return success ? xp + ". " + Game.GiveShop(StoreType.Artifact) : xp;
    }
}

// ===================================================================== Escort the Fungus
internal sealed class EscortEvent : RunEvent
{
    public override string Key => "escort";
    public override string Title => "Escort the Fungus";
    public override Color Accent => Green;
    public override int Weight => Plugin.EscortWeight.Value;

    private Vector3 pos, start, dest;
    private float health, maxHealth, nextScan, nextSpawn;
    private int escorts, attackers, spawned;
    private List<Vector3> enemies = new();
    private const float EscortRange = 7f, AttackRange = 4.5f, Speed = 2.2f;
    private float Limit => Mathf.Max(20f, Plugin.EscortTime.Value);
    public override float Length => Limit;

    protected override bool Begin()
    {
        var centre = Game.PartyCentre();
        start = pos = Game.PointInRing(centre, 4f, 8f);
        float d = Mathf.Max(15f, Plugin.EscortDistance.Value);
        dest = Game.ObjectivePoint(start, d * 0.8f, d * 1.3f);
        if (Game.FlatDistance(dest, start) < d * 0.5f) dest = Game.PointInRing(start, d * 0.8f, d * 1.1f);
        maxHealth = health = Mathf.Max(10f, Plugin.EscortHealth.Value);
        return true;
    }

    public override bool? Tick(float dt)
    {
        if (Elapsed >= nextScan)
        {
            nextScan = Elapsed + 0.4f;
            enemies = Game.EnemyPositions();
            escorts = 0;
            foreach (var p in Game.LivingPlayerPositions()) if (Game.FlatDistance(p, pos) <= EscortRange) escorts++;
        }
        attackers = 0;
        foreach (var e in enemies) if (Game.FlatDistance(e, pos) <= AttackRange) attackers++;
        health -= Mathf.Min(attackers, 8) * 5f * dt;

        if (escorts > 0)
        {
            var to = dest - pos; to.y = 0f;
            float step = Speed * (1f + 0.15f * (escorts - 1)) * dt;
            if (to.magnitude <= step) pos = new Vector3(dest.x, pos.y, dest.z);
            else pos += to.normalized * step;
        }
        if (Elapsed >= nextSpawn && spawned < 16)
        {
            nextSpawn = Elapsed + 5f;
            for (int i = 0; i < 2; i++) { Game.SpawnWaveEnemy(Game.PointInRing(pos, 10f, 14f)); spawned++; }
        }

        if (Game.FlatDistance(pos, dest) < 1.5f) return true;
        if (health <= 0f) return false;
        if (Elapsed >= Limit) return false;
        return null;
    }

    public override void Fill(EventState s)
    {
        int hp = Mathf.CeilToInt(Mathf.Max(0f, health) / maxHealth * 100f);
        s.Line = (escorts > 0 ? "Escorting the fungus" : "The fungus waits for you") + $"  -  health {hp}%" + (attackers > 0 ? "  (under attack!)" : "");
        s.Timer = Mathf.Max(0f, Limit - Elapsed);
        float total = Game.FlatDistance(start, dest);
        s.Progress = total > 0.1f ? Mathf.Clamp01(1f - Game.FlatDistance(pos, dest) / total) : 1f;
        var c = attackers > 0 ? Red : escorts > 0 ? Green : White;
        s.Markers.Add(new Marker { Kind = MarkerKind.Ring, Pos = pos, Radius = EscortRange, Color = c, Icon = "mushroom" });
        s.Markers.Add(new Marker { Kind = MarkerKind.Ring, Pos = dest, Radius = 2.5f, Color = Green, Icon = "flag" });
    }

    public override string Finish(bool success) =>
        success ? Game.GiveShop(StoreType.Artifact) : health <= 0f ? "The fungus was eaten." : "Out of time.";
}

// ===================================================================== Cursed Ground
internal sealed class CursedEvent : RunEvent
{
    public override string Key => "cursed";
    public override string Title => "Cursed Ground";
    public override Color Accent => Purple;
    public override int Weight => Plugin.CursedWeight.Value;

    private Vector3 from, to;
    private float Duration => Mathf.Max(10f, Plugin.CursedTime.Value);
    public override float Length => Duration;

    protected override bool Begin()
    {
        from = Game.PartyCentre();
        to = Game.PointInRing(from, 6f, 16f);
        return true;
    }

    private float T => Mathf.Clamp01(Elapsed / Duration);
    private Vector3 Centre => Vector3.Lerp(from, to, T);
    private float Radius => Mathf.Lerp(Mathf.Max(4f, Plugin.CursedStartRadius.Value), Mathf.Max(2f, Plugin.CursedEndRadius.Value), Mathf.SmoothStep(0f, 1f, T));

    public override bool? Tick(float dt) => Elapsed >= Duration ? true : null;

    public override void Fill(EventState s)
    {
        s.Line = "The ground is cursed! Stay inside the circle.";
        s.Timer = Mathf.Max(0f, Duration - Elapsed);
        s.Progress = T;
        s.Markers.Add(new Marker { Kind = MarkerKind.Safe, Pos = Centre, Radius = Radius, Color = Purple, Dps = Mathf.Max(0f, Plugin.CursedDamage.Value) });
    }

    public override string Finish(bool success) => "Survived the curse: " + Game.GiveXp(50f);
}

// ===================================================================== Elite Gauntlet
internal sealed class GauntletEvent : RunEvent
{
    public override string Key => "gauntlet";
    public override string Title => "Elite Gauntlet";
    public override Color Accent => Orange;
    public override int Weight => Plugin.GauntletWeight.Value;

    private readonly List<(EnemyType type, int world)> stages = new();
    private int stage;
    private Game.Tracked current;
    private float spawnAt = -1f;
    private readonly List<string> rewards = new();
    private float Limit => Mathf.Max(30f, Plugin.GauntletTime.Value);
    public override float Length => Limit;
    public override void CallOff() { if (current != null) Game.Despawn(current.Obj); }

    protected override bool Begin()
    {
        var lists = Game.EliteLists();
        if (lists.Count == 0) return false;
        // Different realms first (shuffled), repeating if the gauntlet is longer than the realms available.
        var order = new List<(int world, Il2CppSystem.Collections.Generic.List<EliteEnemyConfiguration> elites)>(lists);
        for (int i = order.Count - 1; i > 0; i--) { int j = URandom.Range(0, i + 1); (order[i], order[j]) = (order[j], order[i]); }
        int n = Mathf.Clamp(Plugin.GauntletElites.Value, 1, 8);
        for (int i = 0; stages.Count < n && i < n * 4; i++)
        {
            var (world, elites) = order[i % order.Count];
            var t = Game.RandomEliteFrom(elites);
            if (t != null) stages.Add((t.Value, world));
        }
        if (stages.Count == 0) return false;
        return SpawnStage();
    }

    private bool SpawnStage()
    {
        Game.SpawnTracked(stages[stage].type, Game.PointInRing(Game.PartyCentre(), 14f, 22f), out current);
        spawnAt = -1f;
        return current != null;
    }

    public override bool? Tick(float dt)
    {
        if (Elapsed >= Limit) return false;
        if (spawnAt >= 0f)
        {
            if (Elapsed >= spawnAt && !SpawnStage()) { stage++; if (stage >= stages.Count) return true; spawnAt = Elapsed + 1f; }
            return null;
        }
        var fate = Game.FateOf(current);
        if (fate == Game.Fate.Alive) return null;
        if (fate == Game.Fate.Killed) rewards.Add(Game.GiveRevive());
        stage++;
        if (stage >= stages.Count) return true;
        spawnAt = Elapsed + 2f;
        return null;
    }

    public override void Fill(EventState s)
    {
        int shown = Mathf.Min(stage, stages.Count - 1);
        var (type, world) = stages[shown];
        s.Line = $"Elite {shown + 1}/{stages.Count}: {Pretty(type.ToString())} from {Game.RealmNames[Mathf.Clamp(world, 0, 3)]}";
        s.Timer = Mathf.Max(0f, Limit - Elapsed);
        s.Progress = (float)stage / stages.Count;
        if (spawnAt < 0f && current != null && !current.Dead && Game.Alive(current.Obj))
            s.Markers.Add(new Marker { Kind = MarkerKind.Follow, NetId = current.Obj.NetworkObjectId, Icon = "sword", Color = Orange, Radius = 2.2f });
    }

    public override string Finish(bool success)
    {
        int revives = rewards.FindAll(r => r.StartsWith("+1 Revive")).Count;
        string got = revives > 0 ? $"+{revives} revive{(revives > 1 ? "s" : "")}" : rewards.Count > 0 ? string.Join(", ", rewards) : "no rewards";
        return success ? $"Gauntlet cleared: {got}" : $"Gauntlet failed ({stage}/{stages.Count}): {got}";
    }
}

// ===================================================================== wave buff (Bounty escape)
/// <summary>The escaped bounty buffs the next wave: enemies spawned during it get more max health (host).</summary>
[HarmonyPatch]
internal static class WaveBuff
{
    private static int wave = -1;
    private static float multiplier = 1f;

    internal static void Arm(float percent)
    {
        try
        {
            var rm = Game.Run;
            if (rm?.CurrentWave == null) return;
            wave = rm.CurrentWave.Value + 1;
            multiplier = 1f + percent / 100f;
        }
        catch { }
    }

    internal static void Reset() { wave = -1; multiplier = 1f; }

    [HarmonyPatch(typeof(EnemyHealthContainer), nameof(EnemyHealthContainer.CalculateMaxHealth))]
    [HarmonyPostfix]
    private static void MaxHealth_Postfix(EnemyHealthContainer __instance, ref float __result)
    {
        if (wave < 0) return;
        try
        {
            if (__instance.IsBoss) return;
            var rm = Game.Run;
            if (rm?.CurrentWave == null) return;
            int w = rm.CurrentWave.Value;
            if (w > wave) { Reset(); return; }
            if (w == wave) __result *= multiplier;
        }
        catch { }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnDestroy))]
    [HarmonyPostfix]
    private static void RunEnded() { Reset(); Director.Reset(); }
}
