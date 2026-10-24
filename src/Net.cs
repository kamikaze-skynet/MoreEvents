using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Il2CppInterop.Runtime;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MoreEvents;

internal enum MarkerKind { Ring, Follow, Icon, Safe }

internal sealed class Marker
{
    public MarkerKind Kind;
    public Vector3 Pos;
    public float Radius;
    public ulong NetId;
    public string Icon = "";
    public Color Color = Color.white;
    public float Dps;              // Safe: max health percent lost per second outside
}

/// <summary>What the players see of the running event.</summary>
internal sealed class EventState
{
    public string Key = "";
    public int Phase;              // 0 running, 1 succeeded, 2 failed
    public string Title = "", Line = "", Result = "";
    public float Timer = -1f;      // seconds left, -1 = none
    public float Progress = -1f;   // 0..1, -1 = none
    public Color Accent = Color.white;
    public readonly List<Marker> Markers = new();
}

/// <summary>
/// The host sends the event's state four times a second (and once when it ends) as a Netcode named message; every
/// player's game with the mod draws it. The host uses its own copy directly.
/// </summary>
internal static class Net
{
    private const string MessageName = "girthy_mods.MoreEvents.State";
    private static CustomMessagingManager.HandleNamedMessageDelegate handler; // kept alive for IL2CPP
    private static IntPtr registeredOn;

    internal static EventState Current;
    internal static float ReceivedAt;

    internal static void Tick()
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) { registeredOn = IntPtr.Zero; return; }
            if (nm.IsServer) return;
            var cmm = nm.CustomMessagingManager;
            if (registeredOn == cmm.Pointer) return;
            handler ??= DelegateSupport.ConvertDelegate<CustomMessagingManager.HandleNamedMessageDelegate>(new Action<ulong, FastBufferReader>(OnReceived));
            cmm.RegisterNamedMessageHandler(MessageName, handler);
            registeredOn = cmm.Pointer;
        }
        catch (Exception ex) { Plugin.Log.LogError($"Listen: {ex.Message}"); }
    }

    internal static void Publish(EventState s)
    {
        Current = s;
        ReceivedAt = Time.unscaledTime;
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsServer || nm.CustomMessagingManager == null) return;
            string payload = Serialize(s);
            var writer = new FastBufferWriter(payload.Length * 2 + 64, Allocator.Temp, -1);
            try
            {
                writer.WriteValueSafe(payload, false);
                nm.CustomMessagingManager.SendNamedMessageToAll(MessageName, writer, NetworkDelivery.ReliableFragmentedSequenced);
            }
            finally { writer.Dispose(); }
        }
        catch (Exception ex) { Plugin.Log.LogError($"Sending event state: {ex.Message}"); }
    }

    internal static void Clear() { Current = null; }

    private static string Clean(string s) => (s ?? "").Replace("|", "/").Replace("~", "-").Replace(",", " ");
    private static string F(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);
    // ColorUtility.ToHtmlStringRGBA is stripped from this game build: format and parse colours ourselves.
    private static string Hex(Color c)
    {
        int B(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        return $"{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
    }

    private static string Serialize(EventState s)
    {
        var sb = new StringBuilder();
        sb.Append("3|").Append(Clean(s.Key)).Append('|').Append(s.Phase).Append('|')
          .Append(Clean(s.Title)).Append('|').Append(Clean(s.Line)).Append('|').Append(Clean(s.Result)).Append('|')
          .Append(F(s.Timer)).Append('|').Append(F(s.Progress)).Append('|').Append(Hex(s.Accent)).Append('|');
        for (int i = 0; i < s.Markers.Count; i++)
        {
            var m = s.Markers[i];
            if (i > 0) sb.Append('~');
            switch (m.Kind)
            {
                case MarkerKind.Ring: sb.Append("R,").Append(F(m.Pos.x)).Append(',').Append(F(m.Pos.y)).Append(',').Append(F(m.Pos.z)).Append(',').Append(F(m.Radius)).Append(',').Append(Hex(m.Color)).Append(',').Append(Clean(m.Icon)); break;
                case MarkerKind.Safe: sb.Append("S,").Append(F(m.Pos.x)).Append(',').Append(F(m.Pos.y)).Append(',').Append(F(m.Pos.z)).Append(',').Append(F(m.Radius)).Append(',').Append(Hex(m.Color)).Append(',').Append(F(m.Dps)); break;
                case MarkerKind.Follow: sb.Append("F,").Append(m.NetId.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Clean(m.Icon)).Append(',').Append(Hex(m.Color)).Append(',').Append(F(m.Radius)); break;
                case MarkerKind.Icon: sb.Append("I,").Append(F(m.Pos.x)).Append(',').Append(F(m.Pos.y)).Append(',').Append(F(m.Pos.z)).Append(',').Append(Clean(m.Icon)).Append(',').Append(Hex(m.Color)); break;
            }
        }
        return sb.ToString();
    }

    private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static Color C(string s)
    {
        try
        {
            if (s.Length < 6) return Color.white;
            float H(int i) => int.Parse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255f;
            return new Color(H(0), H(2), H(4), s.Length >= 8 ? H(6) : 1f);
        }
        catch { return Color.white; }
    }

    private static void OnReceived(ulong sender, FastBufferReader reader)
    {
        try
        {
            reader.ReadValueSafe(out string payload, false);
            var f = payload.Split('|');
            if (f.Length < 10 || f[0] != "3") return; // 3|key|phase|title|line|result|timer|progress|accent|markers
            var s = new EventState
            {
                Key = f[1], Phase = int.Parse(f[2], CultureInfo.InvariantCulture), Title = f[3], Line = f[4], Result = f[5],
                Timer = P(f[6]), Progress = P(f[7]), Accent = C(f[8]),
            };
            if (f[9].Length > 0)
                foreach (var ms in f[9].Split('~'))
                {
                    var p = ms.Split(',');
                    switch (p[0])
                    {
                        case "R" when p.Length >= 7:
                            s.Markers.Add(new Marker { Kind = MarkerKind.Ring, Pos = new Vector3(P(p[1]), P(p[2]), P(p[3])), Radius = P(p[4]), Color = C(p[5]), Icon = p[6] }); break;
                        case "S" when p.Length >= 7:
                            s.Markers.Add(new Marker { Kind = MarkerKind.Safe, Pos = new Vector3(P(p[1]), P(p[2]), P(p[3])), Radius = P(p[4]), Color = C(p[5]), Dps = P(p[6]) }); break;
                        case "F" when p.Length >= 5:
                            s.Markers.Add(new Marker { Kind = MarkerKind.Follow, NetId = ulong.Parse(p[1], CultureInfo.InvariantCulture), Icon = p[2], Color = C(p[3]), Radius = P(p[4]) }); break;
                        case "I" when p.Length >= 6:
                            s.Markers.Add(new Marker { Kind = MarkerKind.Icon, Pos = new Vector3(P(p[1]), P(p[2]), P(p[3])), Icon = p[4], Color = C(p[5]) }); break;
                    }
                }
            Current = s;
            ReceivedAt = Time.unscaledTime;
        }
        catch (Exception ex) { Plugin.Log.LogError($"Reading event state: {ex.Message}"); }
    }
}
