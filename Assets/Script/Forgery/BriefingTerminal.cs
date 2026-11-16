using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Garajdaki BRİFİNG TERMİNALİ (GDD: "Brifing — hedef eserleri gör").
/// Üstünde bu görevin hedef eserleri sırayla dönen hologram olarak görünür.
/// E → panel: müze, görev seviyesi, kota ve hedef eserlerin resimleri / değerleri / durumu.
/// Kurulum: obje + NetworkObject + bu bileşen.
/// </summary>
public class BriefingTerminal : Interactable
{
    [SerializeField] private float hologramHeight = 0.35f;

    /// <summary>Brifingde eserlerin görünüşü (dönen hologram, resimler) gösterilsin mi? Kapalı: kamyondaki kişi
    /// orijinali yalnızca müzedeki arkadaşının canlı yayınından görür (LiveFeed). Adlar / değerler / plan görünür.</summary>
    // Kapalı: dönen hologram kurulmaz, brifing listesinde resim yok; yalnızca eser adı / değer / plan bilgisi görünür.
    public static readonly bool ShowOriginals = false;

    private GameObject hologram;
    private int builtVersion = -1;
    private int holoIndex;
    private float holoTimer;
    private readonly List<ArtworkDisplay> holoTargets = new List<ArtworkDisplay>();

    public override bool HandleLocally => true;

    public override string GetPrompt(PlayerInteractor player) => Loc.T("brief.prompt");

    public override void ClientInteract(PlayerInteractor player) => BriefingUI.Open(player != null ? player.gameObject : null);

    protected override void ServerInteract(ulong clientId, NetworkObject player) { }

    private void Update()
    {
        MuseumSession ms = MuseumSession.Instance;
        if (ms == null || !ms.Ready) return;

        if (!ShowOriginals)
        {
            if (hologram != null) { Destroy(hologram); hologram = null; }
            return;
        }

        if (builtVersion != MuseumSession.LookVersion)
        {
            builtVersion = MuseumSession.LookVersion;
            holoTargets.Clear();
            foreach (ArtworkDisplay d in ArtworkDisplay.All)
                if (d != null && d.Artwork != null && MuseumSession.IsDisplayActive(d) && d.Kind != ArtKind.None)
                    holoTargets.Add(d);
            holoIndex = 0;
            BuildHologram();
        }

        if (hologram != null)
        {
            // Kesintisiz döner: her eser tam bir tur (360°) atar, sonra sıradaki esere geçilir
            holoTimer += HologramSpinSpeed * Time.deltaTime;
            if (holoTimer >= 360f)
            {
                holoTimer -= 360f;
                if (holoTargets.Count > 1)
                {
                    holoIndex = (holoIndex + 1) % holoTargets.Count;
                    BuildHologram();
                }
            }
            if (hologram != null) hologram.transform.rotation = Quaternion.Euler(0f, holoTimer, 0f);
        }
    }

    private const float HologramSpinSpeed = 72f; // derece / saniye: bir tur 5 saniye

    private void BuildHologram()
    {
        if (hologram != null) Destroy(hologram);
        if (holoTargets.Count == 0) return;
        MuseumSession ms = MuseumSession.Instance;
        ArtworkDisplay d = holoTargets[holoIndex];
        if (ms == null || d == null) return;
        hologram = ArtworkLook.BuildOriginalVisual(d.Kind, ms.Seed, ms.ThemeIndex, d.name);
        if (hologram == null) return;

        // Tablo dönerken arkası da resmi göstersin (tuvalin bir kopyası arka yüze)
        if (d.Kind == ArtKind.Painting)
        {
            Transform canvas = hologram.transform.Find("Canvas");
            if (canvas != null)
            {
                Transform back = Instantiate(canvas.gameObject, hologram.transform).transform;
                Vector3 p = canvas.localPosition;
                back.localPosition = new Vector3(p.x, p.y, -p.z);
                back.localRotation = Quaternion.Euler(0f, 180f, 0f);
                back.localScale = canvas.localScale;
            }
        }

        ItemVisuals.Fit(hologram, 0.5f);
        hologram.transform.rotation = Quaternion.Euler(0f, holoTimer, 0f);
        Collider c = GetComponentInChildren<Collider>();
        float top = c != null ? c.bounds.max.y : transform.position.y + 0.5f;
        hologram.transform.position = new Vector3(transform.position.x, top + hologramHeight * 0.2f, transform.position.z);
    }

    public override void OnDestroy()
    {
        if (hologram != null) Destroy(hologram);
        base.OnDestroy();
    }
}

/// <summary>Brifing paneli (yerel).</summary>
public class BriefingUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }
    private static BriefingUI current;

    /// <summary>Açık brifing panelini kapatır (kamyon sekansı başlarken).</summary>
    public static void ForceClose()
    {
        if (current != null) current.Close();
    }

    private PlayerController controller;
    private int openedFrame;
    private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private Vector2 scroll;
    private float openedAt;

    public static void Open(GameObject player)
    {
        if (IsOpen || WorkbenchUI.IsOpen) return;
        var go = new GameObject("BriefingUI");
        var ui = go.AddComponent<BriefingUI>();
        ui.controller = player != null ? player.GetComponent<PlayerController>() : null;
        ui.openedFrame = Time.frameCount;
        ui.openedAt = Time.unscaledTime;
        current = ui;
        if (ui.controller != null) ui.controller.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsOpen = true;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || Time.frameCount == openedFrame) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) Close();
    }

    private void Close()
    {
        if (controller != null) controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        IsOpen = false;
        Destroy(gameObject);
    }

    private void OnDestroy() { IsOpen = false; if (current == this) current = null; }

    private Texture2D IconFor(MuseumSession ms, ArtworkDisplay d, ArtKind kind)
    {
        string key = ms.Seed + "|" + ms.ThemeIndex + "|" + d.name;
        // t != null: ArtworkLook.ClearCaches dokuyu yok ettiyse (Unity'de sahte null) yeniden üret
        if (icons.TryGetValue(key, out Texture2D t) && t != null) return t;
        t = ArtworkLook.OriginalIcon(kind, ms.Seed, ms.ThemeIndex, d.name);
        if (t == null) t = d.Artwork.icon;
        icons[key] = t;
        return t;
    }

    private static IconId IconForKind(ArtKind kind)
    {
        switch (kind)
        {
            case ArtKind.Vase: return IconId.Vase;
            case ArtKind.Bust: return IconId.Bust;
            case ArtKind.Jewelry: return IconId.Ring;
            default: return IconId.Painting;
        }
    }

    // Brifing: koyu sayfa. Üstte görev adı + kota, altında hedef listesi (yalnızca adlar ve bilgiler — resim yok).
    // Kapat: sağ üstteki düğme, E ya da Esc (ekranda tuş ipucu yazılmaz).
    private void OnGUI()
    {
        MuseumSession ms = MuseumSession.Instance;
        float sc = UITheme.Scale;
        float enter = UIUtil.Ease.OutCubic((Time.unscaledTime - openedAt) / UITheme.MotionBase);

        float pw = Mathf.Min(Screen.width - 32f, 720f * sc);
        float ph = Mathf.Min(Screen.height - 32f, 620f * sc);
        Rect page = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f + (1f - enter) * 12f, pw, ph);
        float pad = UITheme.S5 * sc;

        Color oldC = GUI.color;
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.Scrim, UITheme.Scrim.a * enter));
        GUI.color = new Color(1f, 1f, 1f, enter);
        UIUtil.Panel(page, PanelKind.Plaque);

        float x = page.x + pad, w = page.width - pad * 2f;
        float y = page.y + pad;

        float cb = 32f * sc;
        if (UIUtil.Button(new Rect(page.xMax - pad - cb + 6f, y - 2f, cb, cb), "", ButtonKind.Tool, true, false, IconId.Close)) { GUI.color = oldC; Close(); return; }

        if (ms == null || !ms.Ready)
        {
            UIUtil.Text(new Rect(x, y, w - cb - 10f, 36f * sc), Loc.T("brief.waiting"), TextRole.Title, UITheme.Ivory);
            GUI.color = oldC;
            return;
        }

        // --- Başlık: görev adı + kota ---
        MuseumTheme th = ms.Theme;
        string title = EconomyPanelUI.Fit(Loc.T("mission.title", ms.Level, th.name, th.City), TextRole.Title, w - cb - 10f);
        UIUtil.Text(new Rect(x, y, w - cb - 10f, 34f * sc), title, TextRole.Title, UITheme.Ivory);
        y += 40f * sc;
        string quotaTxt = Loc.T("brief.quota", ms.Quota, Mathf.Max(0, ms.Earned));
        float quotaH = Mathf.Ceil(UIUtil.MeasureHeight(quotaTxt, TextRole.Body, w));
        UIUtil.Text(new Rect(x, y, w, quotaH), quotaTxt, TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += quotaH + 8f * sc;
        float ratio = ms.Quota > 0 ? Mathf.Clamp01(Mathf.Max(0, ms.Earned) / (float)ms.Quota) : 0f;
        UIUtil.ProgressBar(new Rect(x, y, w, 4f * sc), ratio, UITheme.Sage);
        y += 4f * sc + 22f * sc;

        // Çantaya sığmayan hedef varsa (ör. büst) göreve çıkmadan önce net uyarı
        ArtworkDisplay heavy = BagWarning.HeaviestTooHeavy(out float heavyKg);
        if (heavy != null)
        {
            string wt = BagWarning.Message(heavy, heavyKg);
            float iconS = 20f * sc, bp = 10f * sc;
            float wh = Mathf.Ceil(UIUtil.MeasureHeight(wt, TextRole.Body, w - iconS - bp * 3f));
            Rect warn = new Rect(x, y - 10f * sc, w, wh + bp * 2f);
            UIUtil.Round(warn, UITheme.WithAlpha(UITheme.Ochre, 0.14f), UIUtil.RadiusSmall);
            UIUtil.DrawRect(new Rect(warn.x, warn.y + 4f, 3f, warn.height - 8f), UITheme.Ochre);
            UIUtil.Icon(new Rect(warn.x + bp, warn.y + bp, iconS, iconS), IconId.Bag, UITheme.Ochre);
            UIUtil.Text(new Rect(warn.x + bp * 2f + iconS, warn.y + bp, w - iconS - bp * 3f, wh), wt, TextRole.Body, UITheme.Ivory, TextAnchor.UpperLeft);
            y = warn.yMax + 14f * sc;
        }

        UIUtil.Text(new Rect(x, y, w, 20f * sc), Loc.T("brief.goals.title", CountTargets()), TextRole.Section, UITheme.IvoryDim);
        y += 26f * sc;

        // --- Hedef listesi (kaydırmalı) ---
        Rect view = new Rect(x, y, w, Mathf.Max(40f, page.yMax - pad - y));
        float gap = 6f * sc;
        float contentH = 0f;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            contentH += RowHeight(d, sc) + gap;
        }
        bool scrolls = contentH > view.height;
        float rowW = view.width - (scrolls ? 14f : 0f);

        // Yön tuşlarıyla kaydırma
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.UpArrow) { scroll.y -= 48f * sc; e.Use(); }
            else if (e.keyCode == KeyCode.DownArrow) { scroll.y += 48f * sc; e.Use(); }
        }

        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, rowW, Mathf.Max(contentH, view.height - 1f)));
        float cy = 0f;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            float ch = RowHeight(d, sc);
            DrawTargetRow(new Rect(0f, cy, rowW, ch), ms, d, sc);
            cy += ch + gap;
        }
        GUI.EndScrollView();
        GUI.color = oldC;
    }

    private static int CountTargets()
    {
        int n = 0;
        foreach (ArtworkDisplay t in ArtworkDisplay.All)
            if (t != null && t.Artwork != null && MuseumSession.IsDisplayActive(t)) n++;
        return n;
    }

    private static float RowHeight(ArtworkDisplay d, float sc) => (d.IsSwapped || d.IsStaffOnlyPiece || BagWarning.TooHeavy(d)) ? 74f * sc : 56f * sc;

    // Tek hedef: hafif zeminli satır — ad (kalın), altında tür · ağırlık · yapım yöntemi; sağda değer; varsa durum satırı
    private void DrawTargetRow(Rect r, MuseumSession ms, ArtworkDisplay d, float sc)
    {
        ArtKind kind = ArtworkLook.KindOf(d.Artwork);
        UIUtil.Round(r, new Color(1f, 1f, 1f, d.IsSwapped ? 0.03f : 0.05f), UIUtil.RadiusSmall);

        float px = 14f * sc;
        string valTxt = "$" + d.Value;
        float valW = UIUtil.Measure(valTxt, TextRole.Numeric).x + 8f;
        UIUtil.Text(new Rect(r.xMax - valW - px, r.y + 8f * sc, valW, 24f * sc), valTxt, TextRole.Numeric, d.IsSwapped ? UITheme.IvoryDim : UITheme.Ivory, TextAnchor.MiddleRight);

        float nameW = Mathf.Max(10f, r.width - valW - px * 3f);
        UIUtil.Text(new Rect(r.x + px, r.y + 8f * sc, nameW, 24f * sc), EconomyPanelUI.Fit(ms.ArtworkName(d), TextRole.BodyStrong, nameW), TextRole.BodyStrong, d.IsSwapped ? UITheme.IvoryDim : UITheme.Ivory);
        string how = Loc.T(WorkbenchUI.MethodKey(kind));
        UIUtil.Text(new Rect(r.x + px, r.y + 30f * sc, r.width - px * 2f, 18f * sc),
            d.Artwork.weightKg.ToString("0.#") + " kg  ·  " + how, TextRole.Caption, UITheme.IvoryDim);

        float ey = r.y + 50f * sc;
        if (d.IsSwapped)
            UIUtil.Text(new Rect(r.x + px, ey, r.width - px * 2f, 18f * sc), Loc.T("brief.swapped", d.LeftSimilarity), TextRole.Caption, UITheme.Sage);
        else if (BagWarning.TooHeavy(d)) // çantaya sığmıyor: ne alınacağı yazılır
            UIUtil.Text(new Rect(r.x + px, ey, r.width - px * 2f, 18f * sc), Loc.T("bagwarn.row", BagWarning.LocalCapacity.ToString("0.#"), Loc.T("upg.biggerbag.name")), TextRole.Caption, UITheme.Ochre);
        else if (d.IsStaffOnlyPiece) // özel hedef
            UIUtil.Text(new Rect(r.x + px, ey, r.width - px * 2f, 18f * sc), Loc.T("brief.staff"), TextRole.Caption, UITheme.Ochre);
    }
}
