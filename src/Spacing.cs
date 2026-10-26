using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MoreEvents;

/// <summary>
/// Keeps these events apart from the game's own: its objectives (fungi, statues, capture zones...), the boss, and its
/// enemy events (the line of dogs, the mob...). Host only.
///
/// An event only starts when, for its whole length plus a quiet gap either side:
///   - no objective or boss is running, and the last one ended at least the gap ago;
///   - no enemy event is due (the game schedules each one when its wave starts, with a delay: hooked here);
///   - no wave that brings an objective starts (the game decides those waves when the run starts), and the boss
///     isn't due (end of the cycle).
/// If an objective or the boss starts anyway while an event runs, the event is called off (no penalty).
/// </summary>
[HarmonyPatch]
internal static class Spacing
{
    private const float EnemyEventLength = 40f; // about how long the line, mob and circle events take

    private static readonly List<(float from, float to)> enemyEvents = new(); // run time
    private static float lastBusy = -999f;    // run time when an objective / the boss was last seen running
    private static EndBossObjective boss;
    private static bool bossSeen;

    internal static float Gap => Mathf.Max(0f, Plugin.GapSeconds.Value);

    internal static void Reset()
    {
        enemyEvents.Clear();
        lastBusy = -999f;
        boss = null;
        bossSeen = false;
    }

    // ---------- what the game is doing ----------

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartEvent))]
    [HarmonyPostfix]
    private static void EnemyEvent_Postfix(RunManager __instance, EventConfiguration enemyEvent)
    {
        try
        {
            if (!Game.IsServer || enemyEvent == null) return;
            float now = __instance.currentRunTimeInSeconds;
            float at = now + Mathf.Max(0f, enemyEvent.SpawnTimeInSeconds);
            enemyEvents.Add((at, at + EnemyEventLength));
            enemyEvents.RemoveAll(w => w.to < now - 60f);
        }
        catch { }
    }

    [HarmonyPatch(typeof(EndBossObjective), nameof(EndBossObjective.OnNetworkSpawn))]
    [HarmonyPostfix]
    private static void Boss_Postfix(EndBossObjective __instance) { boss = __instance; bossSeen = false; }

    // From the boss objective appearing (its intro) until the boss is down. (Endless runs go on after it.)
    private static bool BossFight()
    {
        try
        {
            if (!Game.Alive(boss) || !boss.IsSpawned) return false;
            var h = boss.bossHealthContainer;
            bool alive = Game.Alive(h) && !h.IsHealthZero();
            if (alive) { bossSeen = true; return true; }
            return !bossSeen; // before the boss appears (its intro); once it has been seen, gone or dead = over
        }
        catch { return false; }
    }

    /// <summary>An objective or the boss is running now (also notes when it was last seen).</summary>
    internal static bool ObjectiveRunning(float runTime)
    {
        bool busy = BossFight();
        try { var d = Game.Objectives; if (d != null && d.ActiveObjective != null) busy = true; } catch { }
        if (busy) lastBusy = runTime;
        return busy;
    }

    internal static string Reason { get; private set; } = "";

    /// <summary>Can an event of this length start now without touching the game's own?</summary>
    internal static bool ClearFor(float runTime, float length)
    {
        float gap = Gap;
        if (ObjectiveRunning(runTime)) { Reason = "an objective is running"; return false; }
        if (runTime - lastBusy < gap) { Reason = "an objective just ended"; return false; }
        float end = runTime + length + gap;
        foreach (var (from, to) in enemyEvents)
            if (from < end && to > runTime - gap) { Reason = "an enemy event is due"; return false; }
        if (ObjectiveWaveWithin(runTime, end - runTime)) { Reason = "an objective wave is coming"; return false; }
        Reason = "";
        return true;
    }

    // Objectives start with their wave. The game counts waves from 1 (wave k starts (k - 1) wave lengths into the
    // cycle), picks an objective for the waves listed in its ObjectiveDeterminer, and the boss on the last wave.
    private static bool ObjectiveWaveWithin(float runTime, float seconds)
    {
        try
        {
            var rm = Game.Run;
            var cfg = rm?.Configuration;
            if (cfg == null || cfg.Waves == null || cfg.Waves.Count == 0 || cfg.RunDurationInSeconds <= 0f) return false;
            int waves = cfg.Waves.Count;
            float waveLen = cfg.RunDurationInSeconds / waves;
            float cycleTime = rm.currentCycleTimeInSeconds;
            var list = rm.objectiveDeterminer?.waveNumbersForObjectives;
            int currentIndex = Mathf.FloorToInt(cycleTime / waveLen); // 0-based
            for (int idx = currentIndex + 1; idx < waves; idx++)
            {
                float startsIn = idx * waveLen - cycleTime;
                if (startsIn > seconds) break;
                int number = idx + 1;
                if (number == waves) return true;                       // the boss
                if (list != null && list.Contains(number)) return true; // an objective
            }
        }
        catch { }
        return false;
    }
}
