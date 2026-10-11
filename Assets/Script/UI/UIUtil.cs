using UnityEngine;

/// <summary>OnGUI arayüzleri için ortak çizim yardımcıları.</summary>
public static class UIUtil
{
    private static GUIStyle promptStyle, promptShadow;
    private static Texture2D panelTex;

    public static Texture2D PanelTexture
    {
        get
        {
            if (panelTex == null)
            {
                panelTex = new Texture2D(1, 1);
                panelTex.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.9f));
                panelTex.Apply();
            }
            return panelTex;
        }
    }

    public static void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>Ekran ortasında gölgeli yazı (etkileşim yazıları için).</summary>
    public static void DrawPrompt(string text, float y, Color color, int fontSize = 18)
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            promptShadow = new GUIStyle(promptStyle);
            promptShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }

        promptStyle.fontSize = fontSize;
        promptShadow.fontSize = fontSize;

        Rect r = new Rect(0f, y, Screen.width, 30f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, promptShadow);
        promptStyle.normal.textColor = color;
        GUI.Label(r, text, promptStyle);
    }

    /// <summary>Basılı tutma halkası: progress 0–1.</summary>
    public static void DrawRing(Vector2 center, float radius, float progress, Color fill)
    {
        const int segments = 48;
        int active = Mathf.RoundToInt(segments * Mathf.Clamp01(progress));

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * 360f - 90f;
            float rad = angle * Mathf.Deg2Rad;
            float x = center.x + Mathf.Cos(rad) * radius;
            float y = center.y + Mathf.Sin(rad) * radius;

            if (i < active) DrawRect(new Rect(x - 2.5f, y - 2.5f, 5f, 5f), fill);
            else DrawRect(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), new Color(0.3f, 0.3f, 0.3f, 0.45f));
        }
    }
}
