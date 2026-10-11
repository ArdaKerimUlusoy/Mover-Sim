using UnityEngine;

/// <summary>
/// Objenin üstünde yüzen yazı (test haritası için: "BLACK MARKET", "GARAGE SHOP"...).
/// Yazı çeviri tablosundan gelir; Item verilirse {0} yerine eşyanın adı yazılır.
/// </summary>
public class WorldLabel : MonoBehaviour
{
    [SerializeField] private string key = "label.shop";
    [SerializeField] private ItemDefinition item;
    [SerializeField] private float height = 1.2f;
    [SerializeField] private float maxDistance = 14f;
    [SerializeField] private Color color = new Color(1f, 0.85f, 0.4f);

    private static GUIStyle style, shadow;

    public void Setup(string labelKey, float labelHeight, ItemDefinition labelItem = null)
    {
        key = labelKey;
        height = labelHeight;
        item = labelItem;
    }

    private void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || SessionMenuUI.IsPaused) return;

        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * height);
        if (screen.z <= 0f || screen.z > maxDistance) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        }

        string text = item != null ? Loc.T(key, item.DisplayName) : Loc.T(key);
        float alpha = Mathf.Clamp01((maxDistance - screen.z) / 3f);

        Rect r = new Rect(screen.x - 150f, Screen.height - screen.y - 12f, 300f, 24f);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        GUI.color = old;
    }
}
