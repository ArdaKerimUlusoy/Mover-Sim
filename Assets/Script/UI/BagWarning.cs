using UnityEngine;

/// <summary>
/// AĞIR HEDEF UYARISI: bu görevde çantana SIĞMAYAN bir hedef eser varsa (ör. 4. seviyedeki Mermer Büst 12 kg, başlangıç çantası 10 kg)
/// görev başlamadan haber verir — eseri değiştirmek için sahteyi götürüp orijinali çantaya koyman gerekir.
///  - Garajda ve kamyonda sol üstte uyarı kartı (hangi eser, kaç kg, çantan kaç kg, ne almalısın).
///  - Yeni seviye başlayınca bir kez bildirim.
///  - Brifing panelinde de aynı uyarı ve satır notu (BriefingTerminal bu sınıfı kullanır).
/// Kendini kurar; sadece okur (oyun mantığını değiştirmez). Hesap yarım saniyede bir (ucuz).
/// </summary>
public class BagWarning : MonoBehaviour
{
    private static BagWarning instance;

    private ArtworkDisplay heavy;      // çantaya sığmayan en ağır hedef (yoksa null)
    private float heavyKg, capKg;
    private float nextCheck;
    private int toastLevel = -1, toastSeed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("BagWarning");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BagWarning>();
    }

    /// <summary>Yerel oyuncunun çanta kapasitesi (kg). Oyuncu yoksa -1.</summary>
    public static float LocalCapacity => PlayerInventory.Local != null ? PlayerInventory.Local.CapacityKg : -1f;

    /// <summary>Bu hedef yerel oyuncunun çantasına (boşken bile) sığmıyor mu?</summary>
    public static bool TooHeavy(ArtworkDisplay d)
    {
        float cap = LocalCapacity;
        return d != null && d.Artwork != null && cap > 0f && d.Artwork.weightKg > cap + 0.0001f;
    }

    /// <summary>Bu görevde çantaya sığmayan en ağır (henüz değiştirilmemiş) hedef. Yoksa null.</summary>
    public static ArtworkDisplay HeaviestTooHeavy(out float kg)
    {
        kg = 0f;
        ArtworkDisplay best = null;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || d.IsSwapped || !MuseumSession.IsDisplayActive(d)) continue;
            if (d.Kind == ArtKind.None || !TooHeavy(d)) continue;
            if (d.Artwork.weightKg > kg) { kg = d.Artwork.weightKg; best = d; }
        }
        return best;
    }

    /// <summary>Uyarı metni (eser adı, kg, çanta kg, ne alınmalı).</summary>
    public static string Message(ArtworkDisplay d, float kg)
    {
        MuseumSession ms = MuseumSession.Instance;
        string name = ms != null ? ms.ArtworkName(d) : d.name;
        return Loc.T("bagwarn.body", name, kg.ToString("0.#"), LocalCapacity.ToString("0.#"), Loc.T("upg.biggerbag.name"));
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        MuseumSession ms = MuseumSession.Instance;
        if (ms == null || !ms.Ready || !GameFlow.LocalSpawned || PlayerInventory.Local == null) { heavy = null; return; }
        heavy = HeaviestTooHeavy(out heavyKg);
        capKg = LocalCapacity;

        // Yeni seviye / yeni müze başlayınca bir kez bildirim
        if (heavy != null && (toastLevel != ms.Level || toastSeed != ms.Seed))
        {
            toastLevel = ms.Level; toastSeed = ms.Seed;
            UIUtil.ShowToast(Loc.T("bagwarn.title") + " — " + Message(heavy, heavyKg), ToastKind.Warning, 9f, IconId.Bag);
        }
    }

    private static bool Visible =>
        GameFlow.LocalSpawned && (GameFlow.Phase == GamePhase.Garage || GameFlow.Phase == GamePhase.Truck) &&
        !SessionMenuUI.IsPaused && !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen && !BodyCam.PanelOpen;

    private void OnGUI()
    {
        if (heavy == null || !Visible) return;
        if (Event.current.type != EventType.Repaint) return;
        float sc = UITheme.Scale;
        float w = Mathf.Min(Screen.width - 24f, 340f * sc);
        float pad = UITheme.S3 * sc;
        float iconS = 20f * sc;
        float innerW = w - pad * 2f - iconS - 8f;
        string title = Loc.T("bagwarn.title");
        string body = Message(heavy, heavyKg);
        float bodyH = Mathf.Ceil(UIUtil.MeasureHeight(body, TextRole.Caption, innerW));
        float h = pad + 22f * sc + 4f + bodyH + pad;

        // Sol üst: oda kodu / görev / hazırlık kutularının altına dizilir (çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.Lobby + 5, w, h);
        UIUtil.HudCard(r);
        UIUtil.DrawRect(new Rect(r.x, r.y + 6f, 3f, r.height - 12f), UITheme.Ochre); // sol vurgu şeridi
        float x = r.x + pad;
        UIUtil.Icon(new Rect(x, r.y + pad, iconS, iconS), IconId.Warning, UITheme.Ochre);
        x += iconS + 8f;
        UIUtil.Text(new Rect(x, r.y + pad, innerW, 22f * sc), title, TextRole.BodyStrong, UITheme.Ochre);
        UIUtil.Text(new Rect(x, r.y + pad + 22f * sc + 4f, innerW, bodyH), body, TextRole.Caption, UITheme.Ivory, TextAnchor.UpperLeft);
    }
}
