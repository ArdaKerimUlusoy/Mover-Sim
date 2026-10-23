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
    private static GUIStyle style, shadow;

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

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            shadow = new GUIStyle(style);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        }

        bool faded = ArtNames.IsFaded(display);
        string text = ArtNames.MuseumName(display);
        float alpha = Mathf.Clamp01((ShowDistance - sp.z) / 1.2f);
        Rect r = new Rect(sp.x - 150f, Screen.height - sp.y - 13f, 300f, 26f);
        UIUtil.DrawRect(new Rect(r.x + 60f, r.y + 2f, r.width - 120f, r.height - 4f), new Color(0.25f, 0.18f, 0.08f, 0.55f * alpha));
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, alpha * (faded ? 0.45f : 1f));
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
        style.normal.textColor = faded ? new Color(0.75f, 0.72f, 0.65f) : new Color(1f, 0.9f, 0.6f);
        GUI.Label(r, text, style);
        GUI.color = old;
    }
}
