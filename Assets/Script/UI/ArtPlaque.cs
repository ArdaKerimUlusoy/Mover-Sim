using UnityEngine;

/// <summary>
/// Eser adları: müzedeki bazı eserlerin tabelası SOLUK (harflerin çoğu okunmuyor). Tam ad yalnızca kamyondaki
/// kat planında (FloorPlanUI) ve atölyede görünür: kamyondaki kişi planı okuyup müzedeki arkadaşını doğru esere yönlendirir.
/// Hangi eserin soluk olduğu görev tohumundan seçilir (her bilgisayarda aynı).
/// </summary>
public static class ArtNames
{
    /// <summary>Bu görevde bu eserin tabelası soluk mu? (Eserlerin yaklaşık yarısı.)</summary>
    public static bool IsFaded(ArtworkDisplay d)
    {
        MuseumSession ms = MuseumSession.Instance;
        if (d == null || ms == null || !ms.Ready || d.Kind == ArtKind.None) return false;
        // Birleştirme: soluk etiketler BodyCam'de (yakından okununca herkes için açılır). Okunmamışsa soluk.
        if (!FeatureMerge.FriendFadedNames) return BodyCam.IsFaded(d) && ms.ArtworkName(d) != ms.RealArtworkName(d);
        int h = ArtworkLook.Hash(d.name) ^ (ms.Seed * 7919);
        return ((h >> 4) & 1) == 0;
    }

    /// <summary>Tam ad (kat planı / atölye / brifing).</summary>
    public static string FullName(ArtworkDisplay d)
    {
        MuseumSession ms = MuseumSession.Instance;
        if (d == null) return "";
        if (ms == null) return d.Artwork != null ? d.Artwork.DisplayName : d.name;
        // Birleştirme: BodyCam açıkken ad, okunana kadar BodyCam'in maskesiyle gelir (ArtworkName); arkadaşın sistemi açıksa gerçek ad
        return FeatureMerge.FriendFadedNames ? ms.RealArtworkName(d) : ms.ArtworkName(d);
    }

    /// <summary>Müzede (tabelada, etkileşim yazısında) görünen ad: soluksa harflerin çoğu okunmaz.</summary>
    public static string MuseumName(ArtworkDisplay d)
    {
        string full = FullName(d);
        if (!FeatureMerge.FriendFadedNames) return full; // BodyCam maskesi zaten uygulandı
        if (!IsFaded(d)) return full;
        MuseumSession ms = MuseumSession.Instance;
        return Fade(full, ArtworkLook.Hash(d.name) ^ (ms != null ? ms.Seed : 0));
    }

    /// <summary>Harflerin ~%70'ini "·" yapar (boşluklar ve ilk harf kalır). Aynı anahtar → aynı sonuç.</summary>
    public static string Fade(string s, int key)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var rng = new System.Random(key);
        var chars = s.ToCharArray();
        bool first = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i])) { first = true; continue; }
            bool keep = (first && i == 0) || rng.NextDouble() < 0.3;
            first = false;
            if (!keep) chars[i] = '·';
        }
        return new string(chars);
    }
}

/// <summary>
/// Eserin altındaki tabela (yakına gelince ekranda yazı). Soluk tabelalar zor okunur.
/// MuseumLayout her esere ekler; sahneye bir şey eklemeye gerek yok.
/// </summary>
[RequireComponent(typeof(ArtworkDisplay))]
public class ArtPlaque : MonoBehaviour
{
    private const float ShowDistance = 3.6f;

    private ArtworkDisplay display;

    private void Awake() => display = GetComponent<ArtworkDisplay>();

    private Vector3 Anchor
    {
        get
        {
            if (display.WallMounted) return transform.position - Vector3.up * 0.78f + transform.forward * 0.06f;
            return transform.position + Vector3.up * 0.15f + transform.forward * 0.55f;
        }
    }

    private void OnGUI()
    {
        if (display == null || display.Artwork == null || !display.IsActiveForMission) return;
        if (SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || FloorPlanUI.IsOpen) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 anchor = Anchor;
        Vector3 sp = cam.WorldToScreenPoint(anchor);
        if (sp.z <= 0.1f || sp.z > ShowDistance) return;
        // Tablonun önünden bakılıyorsa (arkasından değil)
        if (display.WallMounted && Vector3.Dot(cam.transform.position - transform.position, transform.forward) < 0f) return;

        bool faded = ArtNames.IsFaded(display);
        string text = ArtNames.MuseumName(display);
        float alpha = Mathf.Clamp01((ShowDistance - sp.z) / 1.2f);
        if (alpha <= 0.01f || string.IsNullOrEmpty(text)) return;

        // Sergi etiketi: fildişi küçük kart (Label), kömür başlık, ince çizgi, altta mono katalog no
        GUIStyle ts = UITheme.Style(TextRole.BodyStrong, faded ? UITheme.Graphite : UITheme.Charcoal, TextAnchor.MiddleCenter);
        GUIStyle ns = UITheme.Style(TextRole.Mono, UITheme.Graphite, TextAnchor.MiddleCenter);
        float sc = UITheme.Scale;
        float maxW = Mathf.Min(Screen.width - 40f, 360f * sc);
        float tw = Mathf.Min(maxW - 28f, ts.CalcSize(new GUIContent(text)).x);
        float w = Mathf.Max(tw + 28f, 120f * sc);
        float titleH = Mathf.Round(ts.CalcSize(new GUIContent(text)).x > maxW - 28f ? 40f * sc : 24f * sc);
        float numH = Mathf.Round(16f * sc);
        float h = titleH + numH + 14f;
        Rect r = new Rect(Mathf.Round(sp.x - w * 0.5f), Mathf.Round(Screen.height - sp.y - h * 0.5f), w, h);

        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha * (faded ? 0.8f : 1f));
        UIUtil.Panel(r, PanelKind.Label, 0.97f);
        ts.wordWrap = true;
        GUI.Label(new Rect(r.x + 12f, r.y + 5f, r.width - 24f, titleH), text, ts);
        ts.wordWrap = false;
        UIUtil.DrawRect(new Rect(r.x + 14f, r.y + 7f + titleH, r.width - 28f, 1f), UITheme.WithAlpha(UITheme.Graphite, 0.5f * alpha));
        GUI.Label(new Rect(r.x + 12f, r.y + 8f + titleH, r.width - 24f, numH), "No. " + (100 + (ArtworkLook.Hash(display.name) & 0x7FFFFFFF) % 900), ns);
        GUI.color = old;
    }
}
