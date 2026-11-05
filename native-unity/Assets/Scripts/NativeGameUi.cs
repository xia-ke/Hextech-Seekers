using System;
using System.Collections.Generic;
using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Presentation-only IMGUI palette for the standalone Player. Does not own
    /// match state, gameplay input, vision, or any Unity Editor dependencies.
    /// UI coordinates use a 1280x720 reference, scaled uniformly at runtime.
    /// </summary>
    public static class NativeGameUi
    {
        static readonly Dictionary<string, Texture2D> swatches =
            new Dictionary<string, Texture2D>();
        static readonly Color Ink = new Color(.035f, .07f, .12f, .91f);
        static readonly Color Edge = new Color(.33f, .48f, .59f, .6f);
        static readonly Color Gold = new Color(.95f, .75f, .34f);
        static readonly Color Cyan = new Color(.39f, .86f, .91f);
        static GUIStyle primary, secondary, selected, neutral, small;
        static float uiScale = 1f;
        public static Color Accent => Gold;
        public static Color Bright => new Color(.94f, .96f, .99f);
        public static Color Muted => new Color(.65f, .76f, .84f);
        public static Color Teal => Cyan;

        static Texture2D Solid(string key, Color color)
        {
            Texture2D tex;
            if (swatches.TryGetValue(key, out tex) && tex) return tex;
            tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.DontSave;
            swatches[key] = tex;
            return tex;
        }

        static GUIStyle ButtonStyle(string key, Color normal, Color hover,
                                    Color text, int size)
        {
            var style = new GUIStyle(GUI.skin.button);
            style.normal.background = Solid(key + "normal", normal);
            style.hover.background = Solid(key + "hover", hover);
            style.active.background = style.hover.background;
            style.focused.background = style.normal.background;
            style.normal.textColor = text;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = text;
            style.fontSize = size;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;
            style.padding = new RectOffset(8, 8, 8, 8);
            style.border = new RectOffset(0, 0, 0, 0);
            return style;
        }

        static void Prepare()
        {
            if (primary != null) return;
            primary = ButtonStyle("primary", new Color(.57f, .41f, .17f, .99f),
                new Color(.79f, .57f, .22f), Color.white, 16);
            secondary = ButtonStyle("secondary", new Color(.1f, .23f, .32f, .96f),
                new Color(.16f, .39f, .47f), Bright, 16);
            selected = ButtonStyle("selected", new Color(.19f, .36f, .43f, .99f),
                new Color(.25f, .49f, .54f), Color.white, 17);
            neutral = ButtonStyle("neutral", new Color(.11f, .17f, .24f, .96f),
                new Color(.2f, .29f, .37f), Muted, 16);
            small = ButtonStyle("small", new Color(.14f, .23f, .3f, .98f),
                new Color(.24f, .38f, .45f), Bright, 14);
        }

        public static float Begin()
        {
            Prepare();
            uiScale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f,
                                            Screen.height / 720f), .65f, 2.5f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            return uiScale;
        }

        public static float Width => Screen.width / uiScale;
        public static float Height => Screen.height / uiScale;
        public static void End() { GUI.matrix = Matrix4x4.identity; GUI.color = Color.white; }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        public static void Panel(Rect rect, bool bright = false)
        {
            Fill(new Rect(rect.x + 5, rect.y + 8, rect.width, rect.height),
                new Color(0, 0, 0, .25f));
            Fill(rect, bright ? new Color(.05f, .105f, .16f, .91f) : Ink);
            Fill(new Rect(rect.x, rect.y, rect.width, 1f), Edge);
            Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Edge);
            Fill(new Rect(rect.x, rect.y, 1f, rect.height), Edge);
            Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Edge);
            Fill(new Rect(rect.x + 12f, rect.y + 1f, 68f, 2f), Gold);
        }

        public static void Text(Rect rect, string value, int size = 15,
                                Color? color = null, bool bold = false,
                                TextAnchor align = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.normal.textColor = color ?? Bright;
            style.fontSize = size;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.alignment = align;
            style.clipping = TextClipping.Clip;
            style.wordWrap = false;
            GUI.Label(rect, value ?? "", style);
        }

        public static bool Button(Rect rect, string label, bool isPrimary = false,
                                  bool isSelected = false, bool isSmall = false)
        {
            return GUI.Button(rect, label, isSelected ? selected :
                (isSmall ? small : (isPrimary ? primary : secondary)));
        }

        public static void Rule(float x, float y, float width)
        {
            Fill(new Rect(x, y, width, 1f), new Color(.54f, .66f, .71f, .24f));
        }

        public static void Pill(Rect rect, string label, bool positive)
        {
            Fill(rect, positive ? new Color(.1f, .35f, .3f, .96f) :
                new Color(.38f, .29f, .16f, .96f));
            Text(rect, label, 12, positive ? new Color(.64f, 1f, .78f) :
                new Color(1f, .79f, .5f), true, TextAnchor.MiddleCenter);
        }

        public static void DrawLoading(string status, bool failed)
        {
            Begin();
            try
            {
                Fill(new Rect(0, 0, Width, Height),
                     new Color(.018f, .035f, .063f, .98f));
                float w = Mathf.Min(Width - 60f, 570f);
                float x = (Width - w) * .5f;
                float y = Height * .41f - 100f;
                Panel(new Rect(x, y, w, 254f), true);
                Text(new Rect(x + 30, y + 23, w - 60, 33), "HEXTECH SEEKERS",
                     32, Accent, true, TextAnchor.MiddleCenter);
                Text(new Rect(x + 28, y + 65, w - 56, 26),
                     "NATIVE 3D  /  SUMMONER'S RIFT", 14, Muted,
                     true, TextAnchor.MiddleCenter);
                Rule(x + 35, y + 105, w - 70);
                Text(new Rect(x + 28, y + 124, w - 56, 30),
                     failed ? "SCENE INITIALIZATION FAILED" :
                     "Preparing your Rift experience", 20,
                     failed ? new Color(1f, .55f, .45f) : Bright,
                     true, TextAnchor.MiddleCenter);
                Text(new Rect(x + 22, y + 156, w - 44, 30),
                     failed ? status : "Checking map, navigation and character assets...",
                     13, Muted, false, TextAnchor.MiddleCenter);
                Fill(new Rect(x + 38, y + 208, w - 76, 5),
                     new Color(.23f, .31f, .39f));
                if (!failed)
                {
                    float track = w - 76;
                    // Indeterminate loading indicator, never a fabricated %.
                    float position = Mathf.Repeat(Time.unscaledTime * 95f,
                                                   Mathf.Max(1f, track - 84f));
                    Fill(new Rect(x + 38 + position, y + 208, 84f, 5f), Teal);
                }
            }
            finally { End(); }
        }
    }
}
