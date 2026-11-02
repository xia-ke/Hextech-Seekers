using UnityEngine;

namespace HextechSeekers.Native
{
    /// <summary>
    /// Full-window three-card augment draft, with a darkened battlefield,
    /// gold/silver distinctions and hover focus. Uses runtime-generated colors,
    /// not third-party card artwork. Choice identities come only from the
    /// independent eight-augment draft in NativeHextechPool.
    /// </summary>
    public static class NativeHextechDraftUI
    {
        static Texture2D backdrop, gold, goldHover, silver, silverHover;
        static GUIStyle goldStyle, silverStyle, goldHeader, silverHeader, heading, subtitle;

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontSave;
            return texture;
        }

        static GUIStyle Card(Texture2D normal, Texture2D over)
        {
            var style = new GUIStyle(GUI.skin.button);
            style.normal.background = normal;
            style.hover.background = over;
            style.active.background = over;
            style.focused.background = over;
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;
            style.fontSize = 21;
            style.fontStyle = FontStyle.Bold;
            style.wordWrap = true;
            style.padding = new RectOffset(18, 18, 26, 26);
            style.border = new RectOffset(0, 0, 0, 0);
            return style;
        }

        static GUIStyle Label(int size, Color color)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = size;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = color;
            style.alignment = TextAnchor.MiddleCenter;
            style.wordWrap = true;
            return style;
        }

        static void Init()
        {
            if (backdrop && goldStyle != null) return;
            backdrop = Solid(new Color(.015f, .03f, .07f, .92f));
            gold = Solid(new Color(.39f, .24f, .065f, .97f));
            goldHover = Solid(new Color(.56f, .37f, .1f));
            silver = Solid(new Color(.12f, .21f, .31f, .97f));
            silverHover = Solid(new Color(.18f, .34f, .48f));
            goldStyle = Card(gold, goldHover);
            silverStyle = Card(silver, silverHover);
            goldHeader = Label(17, new Color(1f, .83f, .35f));
            silverHeader = Label(17, new Color(.72f, .86f, 1f));
            heading = Label(32, Color.white);
            subtitle = Label(16, new Color(.73f, .82f, .95f));
        }

        public static bool Draw(NativeHextechDefinition[] options,
                                out NativeHextech selection)
        {
            selection = default;
            if (options == null || options.Length != 3) return false;
            Init();
            float w = Screen.width, h = Screen.height;
            GUI.DrawTexture(new Rect(0, 0, w, h), backdrop, ScaleMode.StretchToFill);

            float total = Mathf.Min(w * .92f, 1140f);
            float gap = Mathf.Clamp(w * .018f, 14f, 28f);
            float cardWidth = (total - gap * 2) / 3;
            float cardHeight = Mathf.Min(h * .44f, 335f);
            float x = (w - total) * .5f;
            float y = (h - cardHeight) * .5f + 18f;
            GUI.Label(new Rect(0, y - 117f, w, 45), "HEXTECH SEEKERS", heading);
            GUI.Label(new Rect(0, y - 75f, w, 33),
                      "选择你的海克斯强化  ·  三选一  ·  每位角色独立抽选",
                      subtitle);

            for (int i = 0; i < 3; i++)
            {
                var definition = options[i];
                bool isGold = definition.Name.Contains("金");
                var style = isGold ? goldStyle : silverStyle;
                var headerStyle = isGold ? goldHeader : silverHeader;
                float left = x + i * (cardWidth + gap);
                GUI.Label(new Rect(left, y - 24f, cardWidth, 22f),
                          isGold ? "GOLD AUGMENT" : "SILVER AUGMENT", headerStyle);
                if (GUI.Button(new Rect(left, y, cardWidth, cardHeight),
                               definition.Name + "\n\n" +
                               (definition.Active ? "主动强化" : "被动强化") +
                               "\n点击选择", style))
                {
                    selection = definition.Id;
                    return true;
                }
            }
            GUI.Label(new Rect(0, y + cardHeight + 21f, w, 30f),
                      "云顶式三选一视觉布局 · 原创占位卡面；正式图标和特效仍待制作",
                      subtitle);
            return false;
        }
    }
}
