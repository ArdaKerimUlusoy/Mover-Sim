using UnityEngine;

/// <summary>
/// Objenin üstünde yüzen yazı (test haritası için: "BLACK MARKET", "GARAGE SHOP"...).
/// Yazı çeviri tablosundan gelir; Item verilirse {0} yerine eşyanın adı yazılır.
/// Ayrıca oyuncu isim etiketleri de DrawTag ile çizilir: oyuncu renginde yazı, siyah kontur ve yarı saydam arka plan.
/// </summary>
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private string key = "label.shop";
    [SerializeField] private ItemDefinition item;
    [SerializeField] private float height = 1.2f;
    [SerializeField] private float maxDistance = 14f;
    [SerializeField] private Color color = new Color(1f, 0.85f, 0.4f);

    private static GUIStyle tagStyle;
    private static readonly GUIContent tagContent = new GUIContent();

    public void Setup(string labelKey, float labelHeight, ItemDefinition labelItem = null)
    {
        key = labelKey;
        height = labelHeight;
        item = labelItem;
    }

    /// <summary>
    /// Ekran noktasının (GUI koordinatı, y aşağı) üstüne ortalanmış etiket çizer: koyu yarı saydam arka plan, altta renk şeridi,
    /// siyah konturlu renkli yazı. Oyuncu isimleri ve dünya yazıları bunu kullanır (renk ayrışsın, her zemin üstünde okunsun).
    /// </summary>
    public static void DrawTag(Vector2 center, string text, Color color, float alpha = 1f, int fontSize = 16)
    {
        if (tagStyle == null)
        {
            tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
        }
        tagStyle.fontSize = fontSize;

        tagContent.text = text;
        Vector2 size = tagStyle.CalcSize(tagContent);
        float w = size.x + 16f, h = size.y + 4f;
        Rect r = new Rect(center.x - w * 0.5f, center.y - h * 0.5f, w, h);

        // Arka plan + altında oyuncu rengi şeridi
        UIUtil.DrawRect(r, new Color(0.03f, 0.03f, 0.05f, 0.62f * alpha));
        UIUtil.DrawRect(new Rect(r.x, r.yMax - 3f, r.width, 3f), new Color(color.r, color.g, color.b, alpha));

        // Kontur: 8 yönde siyah, sonra renkli yazı
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        tagStyle.normal.textColor = new Color(0f, 0f, 0f, 0.95f);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                GUI.Label(new Rect(r.x + dx * 1.5f, r.y + dy * 1.5f - 1f, r.width, r.height), text, tagStyle);
            }
        }
        tagStyle.normal.textColor = color;
        GUI.Label(new Rect(r.x, r.y - 1f, r.width, r.height), text, tagStyle);
        GUI.color = old;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return; // sadece çizim olayında (düzen olayında boşuna hesaplanmasın)
        Camera cam = Camera.main;
        if (cam == null || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || FloorPlanUI.IsOpen) return;

        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * height);
        if (screen.z <= 0f || screen.z > maxDistance) return;

        string text = item != null ? Loc.T(key, item.DisplayName) : Loc.T(key);
        // Müze girişinde bu haritanın müzesinin adı yazsın
        if (key == "label.museum" && MuseumSession.Instance != null && MuseumSession.Instance.Ready)
            text = MuseumSession.Instance.Theme.name + "\n" + text;
        float alpha = Mathf.Clamp01((maxDistance - screen.z) / 3f);

        DrawTag(new Vector2(screen.x, Screen.height - screen.y), text, color, alpha, 16);
    }
}
