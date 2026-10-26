using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using URandom = UnityEngine.Random;

namespace MoreEvents;

/// <summary>
/// Host: decides when an event starts (run time, gap since the last one, no game objective running, game not
/// paused), picks one by weight, ticks it and publishes its state.
/// </summary>
internal static class Director
{
    private static RunEvent active;
    private static float nextAt = -1f;      // run time of the next event
    private static IntPtr runPtr;
    private static float nextPublish;
    private static readonly HashSet<string> failedToStart = new();

    internal static bool Running => active != null;

    internal static void Reset()
    {
        Spacing.Reset();
        active = null;
        nextAt = -1f;
        runPtr = IntPtr.Zero;
        failedToStart.Clear();
    }

    private static List<RunEvent> All() => new()
    {
        new BountyEvent(), new HillEvent(), new SwarmEvent(), new EscortEvent(), new CursedEvent(), new GauntletEvent(),
    };

    internal static void Tick()
    {
        if (!Game.IsServer) return;
        var rm = Game.Run;
        if (rm == null) { if (active != null) Reset(); return; }
        if (rm.Pointer != runPtr) { Reset(); runPtr = rm.Pointer; }

        bool started;
        float runTime;
        try { started = rm.runHasStarted; runTime = rm.currentRunTimeInSeconds; }
        catch { return; }
        if (!started) return;
        if (nextAt < 0f) nextAt = Mathf.Max(runTime, 0f) + Mathf.Max(10f, Plugin.FirstEventAfter.Value);
        bool objectiveNow = Spacing.ObjectiveRunning(runTime); // every frame, so the gap after one is measured right

        if (active != null)
        {
            // The game's own objective or the boss came up: this event steps aside.
            if (Plugin.SkipDuringObjectives.Value && !startedBesideObjective && objectiveNow) { CallOff(runTime); return; }
            bool? result;
            try { result = active.Tick(Time.deltaTime); }
            catch (Exception ex) { Plugin.Log.LogError($"{active.Title}: {ex}"); result = false; }
            if (result == null)
            {
                if (Time.unscaledTime >= nextPublish) { nextPublish = Time.unscaledTime + 0.25f; Publish(0, ""); }
                return;
            }
            Finish(result.Value, runTime);
            return;
        }

        bool keyPressed = Pressed(Plugin.StartKey.Value);
        if (!keyPressed)
        {
            if (!Plugin.Enabled.Value || runTime < nextAt) return;
            if (Time.timeScale <= 0.01f) return;
            if (Plugin.SkipDuringObjectives.Value && objectiveNow) return;
        }
        StartRandom(runTime, keyPressed);
    }

    private static bool startedBesideObjective;
    private static float nextWaitLog;

    private static void CallOff(float runTime)
    {
        var ev = active;
        try { ev.CallOff(); } catch (Exception ex) { Plugin.Log.LogWarning($"{ev.Title} (call off): {ex.Message}"); }
        Publish(2, "Called off: the realm's own challenge has begun.");
        Plugin.Log.LogInfo($"Event called off: {ev.Title} (an objective or the boss started).");
        active = null;
        nextAt = runTime + Mathf.Max(20f, Plugin.SecondsBetween.Value) * 0.5f;
    }

    private static void StartRandom(float runTime, bool forced)
    {
        var options = All().FindAll(e => e.Weight > 0 && !failedToStart.Contains(e.Key));
        if (options.Count == 0) { nextAt = runTime + 30f; failedToStart.Clear(); return; }
        int total = 0;
        foreach (var e in options) total += e.Weight;
        int roll = URandom.Range(0, total);
        RunEvent pick = options[0];
        foreach (var e in options) { if (roll < e.Weight) { pick = e; break; } roll -= e.Weight; }

        // Keep clear of the game's own objectives, boss and enemy events (unless the host pressed the key).
        if (!forced && Plugin.SkipDuringObjectives.Value && !Spacing.ClearFor(runTime, pick.Length))
        {
            nextAt = runTime + 5f;
            if (Time.unscaledTime >= nextWaitLog) { nextWaitLog = Time.unscaledTime + 60f; Plugin.Log.LogInfo($"Next event waits: {Spacing.Reason}."); }
            return;
        }

        bool ok;
        try { ok = pick.Start(); }
        catch (Exception ex) { Plugin.Log.LogError($"Starting {pick.Title}: {ex}"); ok = false; }
        if (!ok)
        {
            Plugin.Log.LogWarning($"{pick.Title} couldn't start here; trying another event soon.");
            failedToStart.Add(pick.Key);
            nextAt = runTime + 5f;
            return;
        }
        failedToStart.Clear();
        active = pick;
        startedBesideObjective = Spacing.ObjectiveRunning(runTime);
        Plugin.Log.LogInfo($"Event started: {pick.Title}.");
        Publish(0, "");
    }

    private static void Finish(bool success, float runTime)
    {
        var ev = active;
        string result;
        try { result = ev.Finish(success); }
        catch (Exception ex) { Plugin.Log.LogError($"{ev.Title} rewards: {ex}"); result = ""; }
        Publish(success ? 1 : 2, result);
        Plugin.Log.LogInfo($"Event {(success ? "won" : "lost")}: {ev.Title}. {result}");
        active = null;
        nextAt = runTime + Mathf.Max(20f, Plugin.SecondsBetween.Value) + URandom.Range(0f, Mathf.Max(0f, Plugin.RandomExtra.Value));
    }

    private static void Publish(int phase, string result)
    {
        var ev = active;
        if (ev == null) return;
        var s = new EventState { Key = ev.Key, Title = ev.Title, Accent = ev.Accent, Phase = phase, Result = result };
        try { if (phase == 0) ev.Fill(s); } catch (Exception ex) { Plugin.Log.LogError($"{ev.Title} state: {ex.Message}"); }
        Net.Publish(s);
    }

    private static bool Pressed(string keyName)
    {
        try
        {
            var kb = Keyboard.current;
            if (kb == null || string.IsNullOrEmpty(keyName)) return false;
            if (!Enum.TryParse(keyName, true, out Key key) || key == Key.None) return false;
            var c = kb[key];
            return c != null && c.wasPressedThisFrame;
        }
        catch { return false; }
    }
}
