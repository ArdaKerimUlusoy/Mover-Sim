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
    [SerializeField] private Color color = new Color(0.847f, 0.725f, 0.478f);   // BrassLight (D8B97A)


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
        if (alpha <= 0.01f || string.IsNullOrEmpty(text)) return;
        // fontSize <= 14: oyuncu ismi (Caption); daha büyük: dünya yazısı (BodyStrong). Gölge/kontur yok.
        TextRole role = fontSize <= 14 ? TextRole.Caption : TextRole.BodyStrong;
        GUIStyle st = UITheme.Style(role, UITheme.Ivory, TextAnchor.MiddleCenter);
        Vector2 size = st.CalcSize(new GUIContent(text));
        float w = size.x + 16f, h = size.y + 6f;
        Rect r = new Rect(Mathf.Round(center.x - w * 0.5f), Mathf.Round(center.y - h * 0.5f), w, h);

        // Koyu plaket zemin + altında 3 px oyuncu rengi (vurgu) çizgisi
        UIUtil.DrawRect(r, UITheme.WithAlpha(UITheme.CharDeep, 0.78f * alpha));
        UIUtil.DrawRect(new Rect(r.x, r.yMax - 3f, r.width, 3f), new Color(color.r, color.g, color.b, alpha));

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(r.x, r.y - 1f, r.width, r.height - 2f), text, st);
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
