using System.Collections.Generic;
using UnityEngine;

namespace MoreEvents;

/// <summary>Small pixel-art icons made at runtime (white shapes with a dark outline, tinted by the marker colour).</summary>
internal static class Icons
{
    private static readonly Dictionary<string, string[]> Art = new()
    {
        ["skull"] = new[]
        {
            "..XXXXXXXX..", ".XXXXXXXXXX.", "XXXXXXXXXXXX", "XX..XXXX..XX", "XX..XXXX..XX", "XXXXXXXXXXXX",
            "XXXXX..XXXXX", ".XXXXXXXXXX.", "..XXXXXXXX..", "..X.X..X.X..", "..XXXXXXXX..",
        },
        ["crown"] = new[]
        {
            "X....XX....X", "XX..XXXX..XX", "XXX.XXXX.XXX", "XXXXXXXXXXXX", "XXXXXXXXXXXX", ".XXXXXXXXXX.", ".XXXXXXXXXX.",
        },
        ["crab"] = new[]
        {
            "XX........XX", "X.X......X.X", ".XX......XX.", "..X.XXXX.X..", "...XXXXXX...", "..XXXXXXXX..",
            ".XXXXXXXXXX.", ".X.X.XX.X.X.", "X..X....X..X",
        },
        ["mushroom"] = new[]
        {
            "...XXXXXX...", ".XXX..XXXXX.", "XXXXXXXX..XX", "XX..XXXXXXXX", "XXXXXXXXXXXX", "....XXXX....",
            "....XXXX....", "....XXXX....", "...XXXXXX...",
        },
        ["flag"] = new[]
        {
            "XXXXXXXX....", "XXXXXXXXXX..", "XXXXXXXXXXXX", "XXXXXXXXXX..", "XXXXXXXX....", "X...........",
            "X...........", "X...........", "X...........",
        },
        ["sword"] = new[]
        {
            "..........XX", ".........XXX", "........XXX.", ".......XXX..", "......XXX...", ".X...XXX....",
            "..X.XXX.....", "...XXX......", "...XX.......", "..X..X......", ".X..........",
        },
    };

    private static readonly Dictionary<string, Sprite> cache = new();

    internal static Sprite Get(string name)
    {
        if (string.IsNullOrEmpty(name) || !Art.ContainsKey(name)) return null;
        if (cache.TryGetValue(name, out var s) && s != null && !s.WasCollected) return s;

        var rows = Art[name];
        int h = rows.Length, w = rows[0].Length;
        int W = w + 2, H = h + 2;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        bool On(int x, int y) // x, y in art space (y = 0 top)
            => y >= 0 && y < h && x >= 0 && x < w && rows[y][x] == 'X';
        var clear = new Color(0f, 0f, 0f, 0f);
        var outline = new Color(0.05f, 0.03f, 0.08f, 1f);
        for (int ty = 0; ty < H; ty++)
            for (int tx = 0; tx < W; tx++)
            {
                int ax = tx - 1, ay = (H - 1 - ty) - 1; // texture y goes up
                Color c = clear;
                if (On(ax, ay)) c = Color.white;
                else if (On(ax - 1, ay) || On(ax + 1, ay) || On(ax, ay - 1) || On(ax, ay + 1)) c = outline;
                tex.SetPixel(tx, ty, c);
            }
        tex.Apply();
        Object.DontDestroyOnLoad(tex);
        s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        Object.DontDestroyOnLoad(s);
        cache[name] = s;
        return s;
    }
}
