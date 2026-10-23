using System.Collections.Generic;
using UnityEngine;

/// <summary>Oda tabelası: yakındaki oyuncuya "KAYIT YASAK" / "Fotoğraf serbest" yazar (duvar arkasından görünmez).
/// Optimizasyon: tabelaların kendi OnGUI'si yok; görünürlük saniyede 10 kez hesaplanır (BodyCam.Update → Tick),
/// yazılar tek bir yerden çizilir (BodyCam.OnGUI → DrawAll).</summary>
public class PhotoSign : MonoBehaviour
{
    public bool allowed;
    private const float ShowDistance = 11f;
    private static GUIStyle style, shadow;
    private static readonly List<PhotoSign> All = new List<PhotoSign>();

    private bool visible;
    private float dist;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); visible = false; }

    /// <summary>Hangi tabelalar görünüyor? (Mesafe + duvar arkası kontrolü; ışın sadece yakındakilere.)</summary>
    public static void Tick()
    {
        Camera cam = Camera.main;
        bool rec = BodyCam.LocalRecording;
        for (int i = 0; i < All.Count; i++)
        {
            PhotoSign s = All[i];
            s.visible = false;
            if (cam == null || s == null) continue;
            if (s.allowed && !rec) continue;                 // "serbest" yazısı sadece kayıttayken
            Vector3 at = s.transform.position;
            float d = Vector3.Distance(cam.transform.position, at);
            s.dist = d;
            if (d > ShowDistance || (!s.allowed && !rec && d > 7f)) continue; // yasak tabelası yakından okunur
            if (Physics.Linecast(cam.transform.position, at, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.distance < d - 0.6f && hit.collider.GetComponentInParent<PlayerStealth>() == null) continue;
            s.visible = true;
        }
    }

    public static void DrawAll()
    {
        Camera cam = Camera.main;
        if (cam == null || All.Count == 0) return;
        for (int i = 0; i < All.Count; i++)
        {
            PhotoSign s = All[i];
            if (s == null || !s.visible) continue;
            Vector3 sp = cam.WorldToScreenPoint(s.transform.position);
            if (sp.z <= 0f) continue;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };
                shadow = new GUIStyle(style);
                shadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }
            float a = Mathf.Clamp01((ShowDistance - s.dist) / 3f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            string text = Loc.T(s.allowed ? "cam.sign.ok" : "cam.sign.no");
            Rect r = new Rect(sp.x - 120f, Screen.height - sp.y + 14f, 240f, 22f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            style.normal.textColor = s.allowed ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.35f, 0.3f);
            GUI.Label(r, text, style);
            GUI.color = old;
        }
    }
}
