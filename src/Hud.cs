using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace MoreEvents;

/// <summary>
/// Every player's game: draws the running event (banner with title, line, timer and progress bar; rings on the
/// ground, icons over targets, clamped to the screen edge when off screen) and applies Cursed Ground's damage to the
/// local player. On the host it also drives the Director.
/// </summary>
public class Hud : MonoBehaviour
{
    public Hud(IntPtr ptr) : base(ptr) { }

    private const float BannerWidth = 600f, ResultSeconds = 6f, StaleSeconds = 3f;

    private GameObject bannerCanvas, worldCanvas, panel;
    private RectTransform panelRt, barBg, barFill;
    private TextMeshProUGUI title, timer, line;
    private Image accentLine;
    private readonly List<Image> segments = new();
    private readonly List<Image> icons = new();
    private int segUsed, iconUsed;
    private float nextDamage, nextErrorLog;
    private bool outsideSafe;

    private void Update()
    {
        try
        {
            Net.Tick();
            Director.Tick();
            CursedDamage();
        }
        catch (Exception ex) { LogOnce(ex); }
    }

    private void LateUpdate()
    {
        try { Draw(); }
        catch (Exception ex) { LogOnce(ex); }
    }

    private void LogOnce(Exception ex)
    {
        if (Time.unscaledTime < nextErrorLog) return;
        nextErrorLog = Time.unscaledTime + 10f;
        Plugin.Log.LogError($"Events: {ex}");
    }

    private static EventState Visible()
    {
        var s = Net.Current;
        if (s == null || !Game.Listening) return null;
        float age = Time.unscaledTime - Net.ReceivedAt;
        if (s.Phase == 0 && age > StaleSeconds) return null;
        if (s.Phase != 0 && age > ResultSeconds) return null;
        return s;
    }

    // ---------- Cursed Ground ----------

    private void CursedDamage()
    {
        outsideSafe = false;
        var s = Visible();
        if (s == null || s.Phase != 0) return;
        Marker safe = null;
        foreach (var m in s.Markers) if (m.Kind == MarkerKind.Safe) { safe = m; break; }
        if (safe == null) return;

        var me = Game.LocalPlayer();
        if (me == null) return;
        var h = Game.HealthOf(me);
        if (h == null || h.IsHealthZero()) return;
        if (Game.FlatDistance(me.transform.position, safe.Pos) <= safe.Radius) return;
        outsideSafe = true;
        if (Time.time < nextDamage) return;
        nextDamage = Time.time + 0.5f;
        float dmg = h.MaxHealth * safe.Dps / 100f * 0.5f;
        if (dmg > 0f) h.RemoveHealth(dmg, new TakeDamageOnLevelUpCurseDamageInfo(), false);
    }

    // ---------- drawing ----------

    private void Draw()
    {
        segUsed = iconUsed = 0;
        var s = Visible();
        if (s == null)
        {
            if (panel != null && panel.activeSelf) panel.SetActive(false);
            HideUnused();
            return;
        }
        if (!Build()) return;

        DrawBanner(s);
        var cam = Camera.main;
        if (cam != null && s.Phase == 0) DrawMarkers(s, cam);
        HideUnused();
    }

    private void DrawBanner(EventState s)
    {
        if (!panel.activeSelf) panel.SetActive(true);
        var accent = s.Phase == 1 ? new Color(0.45f, 1f, 0.45f) : s.Phase == 2 ? new Color(1f, 0.35f, 0.3f) : s.Accent;
        title.color = accent;
        accentLine.color = accent;

        if (s.Phase == 0)
        {
            title.text = s.Title.ToUpperInvariant();
            line.text = outsideSafe ? "<color=#FF6060>Outside the safe circle! Get back in!</color>" : s.Line;
            timer.text = s.Timer >= 0f ? $"{(int)(s.Timer / 60)}:{(int)(s.Timer % 60):00}" : "";
            timer.color = s.Timer >= 0f && s.Timer < 10f ? new Color(1f, 0.4f, 0.35f) : Color.white;
        }
        else
        {
            title.text = s.Title.ToUpperInvariant() + (s.Phase == 1 ? "  COMPLETE!" : "  FAILED");
            line.text = s.Result;
            timer.text = "";
        }

        bool bar = s.Phase == 0 && s.Progress >= 0f;
        barBg.gameObject.SetActive(bar);
        if (bar) barFill.sizeDelta = new Vector2((barBg.sizeDelta.x - 4f) * Mathf.Clamp01(s.Progress), barBg.sizeDelta.y - 4f);
        panelRt.sizeDelta = new Vector2(BannerWidth, bar ? 104f : 84f);
    }

    private void DrawMarkers(EventState s, Camera cam)
    {
        float ui = Mathf.Max(0.6f, Screen.height / 1080f);
        Il2CppSystem.Collections.Generic.Dictionary<ulong, NetworkObject> spawned = null;
        bool looked = false;

        foreach (var m in s.Markers)
        {
            switch (m.Kind)
            {
                case MarkerKind.Ring:
                    Circle(cam, m.Pos, m.Radius, m.Color, 3f * ui);
                    if (!string.IsNullOrEmpty(m.Icon)) Icon(cam, m.Pos + Vector3.up * 2.5f, m.Icon, m.Color, ui);
                    break;
                case MarkerKind.Safe:
                    Circle(cam, m.Pos, m.Radius, m.Color, 5f * ui);
                    Circle(cam, m.Pos, m.Radius + 0.6f, new Color(m.Color.r, m.Color.g, m.Color.b, 0.35f), 3f * ui);
                    break;
                case MarkerKind.Icon:
                    Icon(cam, m.Pos, m.Icon, m.Color, ui);
                    break;
                case MarkerKind.Follow:
                    if (!looked) { looked = true; try { spawned = NetworkManager.Singleton?.SpawnManager?.SpawnedObjects; } catch { } }
                    NetworkObject obj = null;
                    try { if (spawned == null || !spawned.TryGetValue(m.NetId, out obj)) break; } catch { break; }
                    if (!Game.Alive(obj)) break;
                    var p = obj.transform.position;
                    if (m.Radius > 0f) Circle(cam, p, m.Radius, m.Color, 3f * ui);
                    Icon(cam, p + Vector3.up * 3.2f, m.Icon, m.Color, ui);
                    break;
            }
        }
    }

    private const int CircleSteps = 48;
    private readonly Vector3[] pts = new Vector3[CircleSteps + 1];

    private void Circle(Camera cam, Vector3 centre, float radius, Color color, float thickness)
    {
        for (int i = 0; i <= CircleSteps; i++)
        {
            float a = i * Mathf.PI * 2f / CircleSteps;
            pts[i] = cam.WorldToScreenPoint(centre + new Vector3(Mathf.Cos(a) * radius, 0.15f, Mathf.Sin(a) * radius));
        }
        for (int i = 0; i < CircleSteps; i++)
        {
            var a = pts[i]; var b = pts[i + 1];
            if (a.z <= 0f || b.z <= 0f) continue;
            if (Offscreen(a) && Offscreen(b)) continue;
            var seg = Segment();
            var rt = seg.rectTransform;
            var d = new Vector2(b.x - a.x, b.y - a.y);
            rt.anchoredPosition = new Vector2((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f);
            rt.sizeDelta = new Vector2(d.magnitude + thickness * 0.5f, thickness);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            seg.color = color;
        }
    }

    private static bool Offscreen(Vector3 p) => p.x < -50f || p.y < -50f || p.x > Screen.width + 50f || p.y > Screen.height + 50f;

    private void Icon(Camera cam, Vector3 world, string name, Color color, float ui)
    {
        var sprite = Icons.Get(name);
        if (sprite == null) return;
        var sp = cam.WorldToScreenPoint(world);
        bool behind = sp.z < 0f;
        if (behind) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }
        float margin = 40f * ui;
        bool clamped = behind || sp.x < margin || sp.y < margin || sp.x > Screen.width - margin || sp.y > Screen.height - margin;
        sp.x = Mathf.Clamp(sp.x, margin, Screen.width - margin);
        sp.y = Mathf.Clamp(sp.y, margin, Screen.height - margin);

        var img = IconImage();
        img.sprite = sprite;
        img.color = clamped ? new Color(color.r, color.g, color.b, 0.75f) : color;
        var rt = img.rectTransform;
        rt.anchoredPosition = new Vector2(sp.x, sp.y);
        float size = (clamped ? 30f : 38f) * ui;
        rt.sizeDelta = new Vector2(size, size * sprite.rect.height / sprite.rect.width);
    }

    private Image Segment()
    {
        if (segUsed < segments.Count) { var s = segments[segUsed++]; if (!s.gameObject.activeSelf) s.gameObject.SetActive(true); return s; }
        var go = new GameObject("Seg");
        go.transform.SetParent(worldCanvas.transform, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        segments.Add(img);
        segUsed++;
        return img;
    }

    private Image IconImage()
    {
        if (iconUsed < icons.Count) { var s = icons[iconUsed++]; if (!s.gameObject.activeSelf) s.gameObject.SetActive(true); s.transform.SetAsLastSibling(); return s; }
        var go = new GameObject("Icon");
        go.transform.SetParent(worldCanvas.transform, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.preserveAspect = true;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        icons.Add(img);
        iconUsed++;
        return img;
    }

    private void HideUnused()
    {
        for (int i = segUsed; i < segments.Count; i++) if (segments[i] != null && segments[i].gameObject.activeSelf) segments[i].gameObject.SetActive(false);
        for (int i = iconUsed; i < icons.Count; i++) if (icons[i] != null && icons[i].gameObject.activeSelf) icons[i].gameObject.SetActive(false);
    }

    // ---------- building the UI ----------

    private bool Build()
    {
        if (Game.Alive(panel) && Game.Alive(worldCanvas)) return true;
        TMP_FontAsset font = null;
        try
        {
            var any = UObject.FindObjectOfType<TextMeshProUGUI>();
            if (any != null) font = any.font;
            if (font == null) font = TMP_Settings.defaultFontAsset;
        }
        catch { }
        if (font == null) return false;

        segments.Clear(); icons.Clear();

        // Rings and icons, in screen pixels, under the game's HUD.
        worldCanvas = new GameObject("MoreEvents_World");
        DontDestroyOnLoad(worldCanvas);
        var wc = worldCanvas.AddComponent<Canvas>();
        wc.renderMode = RenderMode.ScreenSpaceOverlay;
        wc.sortingOrder = -1;

        // The banner, below where the game's own objective banner sits.
        bannerCanvas = new GameObject("MoreEvents_Banner");
        DontDestroyOnLoad(bannerCanvas);
        var bc = bannerCanvas.AddComponent<Canvas>();
        bc.renderMode = RenderMode.ScreenSpaceOverlay;
        bc.sortingOrder = 450;
        var scaler = bannerCanvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        panel = new GameObject("Panel");
        panelRt = panel.AddComponent<RectTransform>();
        panel.transform.SetParent(bannerCanvas.transform, false);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 1f);
        panelRt.pivot = new Vector2(0.5f, 1f);
        panelRt.anchoredPosition = new Vector2(0f, -200f);
        panelRt.sizeDelta = new Vector2(BannerWidth, 104f);
        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.04f, 0.12f, 0.78f);
        bg.raycastTarget = false;

        accentLine = Child<Image>("Accent", panel.transform, new Vector2(0f, 0f), new Vector2(BannerWidth, 3f), new Vector2(0.5f, 1f));
        accentLine.raycastTarget = false;

        title = Text("Title", font, 26f, FontStyles.Bold, new Vector2(0f, -8f), new Vector2(BannerWidth - 120f, 34f), TextAlignmentOptions.Center);
        timer = Text("Timer", font, 24f, FontStyles.Bold, new Vector2(BannerWidth / 2f - 50f, -9f), new Vector2(90f, 32f), TextAlignmentOptions.Right);
        line = Text("Line", font, 18f, FontStyles.Normal, new Vector2(0f, -44f), new Vector2(BannerWidth - 30f, 26f), TextAlignmentOptions.Center);

        barBg = Child<Image>("Bar", panel.transform, new Vector2(0f, -76f), new Vector2(460f, 14f), new Vector2(0.5f, 1f)).rectTransform;
        barBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        var fill = Child<Image>("Fill", barBg, new Vector2(2f, -2f), new Vector2(0f, 10f), new Vector2(0f, 1f));
        fill.color = new Color(1f, 0.85f, 0.35f, 0.95f);
        barFill = fill.rectTransform;
        barFill.anchorMin = barFill.anchorMax = new Vector2(0f, 1f);

        panel.SetActive(false);
        return true;
    }

    private static T Child<T>(string name, Transform parent, Vector2 pos, Vector2 size, Vector2 anchor) where T : Component
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(anchor.x, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var c = go.AddComponent<T>();
        if (c is Graphic g) g.raycastTarget = false;
        return c;
    }

    private TextMeshProUGUI Text(string name, TMP_FontAsset font, float size, FontStyles style, Vector2 pos, Vector2 box, TextAlignmentOptions align)
    {
        var t = Child<TextMeshProUGUI>(name, panel.transform, pos, box, new Vector2(0.5f, 1f));
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.richText = true;
        t.color = Color.white;
        t.outlineWidth = 0.15f;
        t.outlineColor = new Color32(0, 0, 0, 200);
        return t;
    }
}
