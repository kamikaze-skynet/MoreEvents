using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace MoreEvents;

/// <summary>
/// Six extra in-run events: Bounty, King of the Hill, Golden Swarm, Escort the Fungus, Cursed Ground and Elite
/// Gauntlet. The host runs them (spawns, rules, rewards) and sends their state to everyone with the mod, whose game
/// draws the banner, rings and markers.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "girthy_mods.MoreEvents";
    public const string Name = "MoreEvents";
    public const string Version = "1.0.6";

    internal static new ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> FirstEventAfter;
    internal static ConfigEntry<float> SecondsBetween;
    internal static ConfigEntry<float> RandomExtra;
    internal static ConfigEntry<bool> SkipDuringObjectives;
    internal static ConfigEntry<float> GapSeconds;
    internal static ConfigEntry<string> StartKey;

    internal static ConfigEntry<int> BountyWeight, HillWeight, SwarmWeight, EscortWeight, CursedWeight, GauntletWeight;

    internal static ConfigEntry<float> BountyTime, BountyEscapeBuff;
    internal static ConfigEntry<float> HillTime, HillCapture, HillRadius;
    internal static ConfigEntry<int> SwarmCount;
    internal static ConfigEntry<float> SwarmTime, SwarmXp;
    internal static ConfigEntry<float> EscortTime, EscortDistance, EscortHealth;
    internal static ConfigEntry<float> CursedTime, CursedDamage, CursedStartRadius, CursedEndRadius;
    internal static ConfigEntry<int> GauntletElites;
    internal static ConfigEntry<float> GauntletTime;

    public override void Load()
    {
        Log = base.Log;
        HostConfig.Init(this, Guid, new[] { "General.StartEventKey" }, new System.Collections.Generic.Dictionary<string, string> { ["General.Enabled"] = "false" });

        Enabled = Config.Bind("General", "Enabled", true, "Run the extra events (host).");
        FirstEventAfter = Config.Bind("General", "FirstEventAfterSeconds", 120f,
            new ConfigDescription("Run time before the first event.", new AcceptableValueRange<float>(10f, 3600f)));
        SecondsBetween = Config.Bind("General", "SecondsBetweenEvents", 150f,
            new ConfigDescription("Time from the end of one event to the start of the next.", new AcceptableValueRange<float>(20f, 3600f)));
        RandomExtra = Config.Bind("General", "RandomExtraSeconds", 60f,
            new ConfigDescription("Up to this much extra time is added at random between events.", new AcceptableValueRange<float>(0f, 600f)));
        SkipDuringObjectives = Config.Bind("General", "WaitForObjectives", true,
            "Keep events apart from the game's own objectives, the boss and its enemy events (the line of dogs, the mob...): an event only starts when none of those is running or due during it, and steps aside if an objective or the boss starts anyway.");
        GapSeconds = Config.Bind("General", "GapSeconds", 30f,
            new ConfigDescription("Quiet time kept between the game's own objectives/enemy events and these events.", new AcceptableValueRange<float>(0f, 300f)));
        StartKey = Config.Bind("General", "StartEventKey", "F5",
            "Key (host) that starts a random event right away (Unity Input System key name). Empty = no key.");

        const string w = "Chance weight of this event (0 = never).";
        BountyWeight = Config.Bind("Events", "Bounty", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));
        HillWeight = Config.Bind("Events", "KingOfTheHill", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));
        SwarmWeight = Config.Bind("Events", "GoldenSwarm", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));
        EscortWeight = Config.Bind("Events", "EscortTheFungus", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));
        CursedWeight = Config.Bind("Events", "CursedGround", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));
        GauntletWeight = Config.Bind("Events", "EliteGauntlet", 1, new ConfigDescription(w, new AcceptableValueRange<int>(0, 10)));

        BountyTime = Config.Bind("Bounty", "TimeLimit", 45f, "Seconds to kill the marked elite.");
        BountyEscapeBuff = Config.Bind("Bounty", "EscapeHealthBuffPercent", 30f, "If it escapes, enemies of the next wave get this much more health.");

        HillTime = Config.Bind("KingOfTheHill", "TimeLimit", 75f, "Seconds to fill the bar.");
        HillCapture = Config.Bind("KingOfTheHill", "SecondsToCapture", 25f, "Seconds for one player to fill the bar (more players fill it faster).");
        HillRadius = Config.Bind("KingOfTheHill", "ZoneRadius", 6f, "Size of the zone.");

        SwarmCount = Config.Bind("GoldenSwarm", "Crabs", 6, new ConfigDescription("Golden crabs per swarm.", new AcceptableValueRange<int>(1, 20)));
        SwarmTime = Config.Bind("GoldenSwarm", "TimeLimit", 40f, "Seconds before the crabs escape.");
        SwarmXp = Config.Bind("GoldenSwarm", "XpPerCrabPercent", 15f, "XP for each crab, as a percent of the current level's XP.");

        EscortTime = Config.Bind("EscortTheFungus", "TimeLimit", 120f, "Seconds to bring the fungus home.");
        EscortDistance = Config.Bind("EscortTheFungus", "Distance", 45f, "How far it has to walk.");
        EscortHealth = Config.Bind("EscortTheFungus", "Health", 100f, "Its health; each nearby enemy drains 5 per second.");

        CursedTime = Config.Bind("CursedGround", "Duration", 60f, "Seconds the curse lasts.");
        CursedDamage = Config.Bind("CursedGround", "DamagePercentPerSecond", 5f, "Max health lost per second outside the safe circle.");
        CursedStartRadius = Config.Bind("CursedGround", "StartRadius", 28f, "Safe circle size at the start.");
        CursedEndRadius = Config.Bind("CursedGround", "EndRadius", 8f, "Safe circle size at the end.");

        GauntletElites = Config.Bind("EliteGauntlet", "Elites", 3, new ConfigDescription("Elites in a row.", new AcceptableValueRange<int>(1, 8)));
        GauntletTime = Config.Bind("EliteGauntlet", "TimeLimit", 150f, "Seconds for the whole gauntlet.");

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(WaveBuff));
        try { harmony.PatchAll(typeof(Spacing)); } catch (System.Exception ex) { Log.LogWarning($"Spacing from the game's events unavailable: {ex.Message}"); }
        AddComponent<Hud>();
        Log.LogInfo($"{Name} {Version} ready.");
    }
}
