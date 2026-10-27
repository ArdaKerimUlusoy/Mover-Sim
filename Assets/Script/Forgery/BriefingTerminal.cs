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

    // Brifing: katalog sayfası. Üstte görev başlığı + kota, altında hedef eser kartları (Label), altta kapat ipucu.
    private void OnGUI()
    {
        MuseumSession ms = MuseumSession.Instance;
        float sc = UITheme.Scale;
        float enter = UIUtil.Ease.OutCubic((Time.unscaledTime - openedAt) / UITheme.MotionBase);

        float pw = Mathf.Min(Screen.width - 32f, 820f * sc);
        float ph = Mathf.Min(Screen.height - 32f, 700f * sc);
        Rect page = new Rect((Screen.width - pw) * 0.5f, (Screen.height - ph) * 0.5f + (1f - enter) * 12f, pw, ph);
        float pad = UITheme.S5 * sc;

        Color oldC = GUI.color;
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.Scrim, UITheme.Scrim.a * enter));
        GUI.color = new Color(1f, 1f, 1f, enter);
        UIUtil.Shadow(page);
        UIUtil.Panel(page, PanelKind.Catalog);
        UIUtil.Corners(new Rect(page.x + 6f, page.y + 6f, page.width - 12f, page.height - 12f), 0.6f);

        float x = page.x + pad, w = page.width - pad * 2f;
        float y = page.y + pad;

        // Alt çubuk: kapat ipucu (tuş kapakları) + Kapat düğmesi
        string closeTxt = Loc.T("brief.close");
        int colon = closeTxt.IndexOf(':');
        string closeLabel = colon >= 0 ? closeTxt.Substring(colon + 1).Trim() : closeTxt;
        float footH = 40f * sc;
        float footY = page.yMax - pad - footH;
        float bw = Mathf.Max(110f * sc, UIUtil.Measure(closeLabel, TextRole.Button).x + 40f);
        if (UIUtil.Button(new Rect(x + w - bw, footY, bw, footH), closeLabel, ButtonKind.Secondary)) { GUI.color = oldC; Close(); return; }
        float kx = x;
        float ky = footY + (footH - 24f * sc) * 0.5f;
        foreach (string k in new[] { "E", "Esc" })
        {
            float kw = UIUtil.KeyCapWidth(k, 14);
            UIUtil.KeyCap(new Rect(kx, ky, kw, 24f * sc), k, 14);
            kx += kw + 6f;
        }
        UIUtil.Text(new Rect(kx + 4f, footY, Mathf.Max(10f, w - bw - (kx - x) - 16f), footH), closeLabel, TextRole.Caption, UITheme.Graphite);

        if (ms == null || !ms.Ready)
        {
            UIUtil.Text(new Rect(x, y, w, 40f * sc), Loc.T("brief.waiting"), TextRole.Title, UITheme.Charcoal);
            GUI.color = oldC;
            return;
        }

        // --- Başlık: bölüm etiketi + görev adı + kota ---
        MuseumTheme th = ms.Theme;
        UIUtil.Text(new Rect(x, y, w, 16f * sc), UITheme.Caps(Loc.T("brief.goals.title", CountTargets())), TextRole.Section, UITheme.Bordeaux);
        y += 18f * sc;
        UIUtil.Text(new Rect(x, y, w, 40f * sc), Loc.T("mission.title", ms.Level, th.name, th.City), TextRole.Title, UITheme.Charcoal);
        y += 42f * sc;
        UIUtil.Divider(x, y, w, UITheme.WithAlpha(UITheme.Bordeaux, 0.7f), true);
        y += 12f * sc;

        // Kota: yazı + ilerleme çubuğu
        string quotaTxt = Loc.T("brief.quota", ms.Quota, Mathf.Max(0, ms.Earned));
        float quotaH = Mathf.Ceil(UIUtil.MeasureHeight(quotaTxt, TextRole.Body, w));
        UIUtil.Text(new Rect(x, y, w, quotaH), quotaTxt, TextRole.Body, UITheme.Graphite, TextAnchor.UpperLeft);
        y += quotaH + 6f * sc;
        float ratio = ms.Quota > 0 ? Mathf.Clamp01(Mathf.Max(0, ms.Earned) / (float)ms.Quota) : 0f;
        UIUtil.ProgressBar(new Rect(x, y, w, 10f * sc), ratio, UITheme.Forest);
        y += 10f * sc + 14f * sc;

        // --- Hedef eser kartları (kaydırmalı) ---
        Rect view = new Rect(x - 4f, y, w + 8f, Mathf.Max(40f, footY - y - 10f * sc));
        float gap = 8f * sc;
        float cardW = view.width - 18f;   // dikey kaydırma çubuğuna yer
        float contentH = 0f;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            contentH += CardHeight(d, sc) + gap;
        }

        // Yön tuşlarıyla kaydırma
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.UpArrow) { scroll.y -= 48f * sc; e.Use(); }
            else if (e.keyCode == KeyCode.DownArrow) { scroll.y += 48f * sc; e.Use(); }
        }

        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, cardW, Mathf.Max(contentH, view.height - 1f)));
        float cy = 0f;
        int no = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            no++;
            float ch = CardHeight(d, sc);
            DrawTargetCard(new Rect(4f, cy, cardW - 4f, ch), ms, d, no, sc);
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

    private static float CardHeight(ArtworkDisplay d, float sc)
    {
        float h = 64f * sc;
        if (d.IsSwapped || d.IsStaffOnlyPiece) h += 20f * sc;
        return h;
    }

    // Tek hedef: Label kartı — sıra no (Mono), tür ikonu, ad, ağırlık · yöntem, sağda değer (Numeric)
    private void DrawTargetCard(Rect r, MuseumSession ms, ArtworkDisplay d, int no, float sc)
    {
        ArtKind kind = ArtworkLook.KindOf(d.Artwork);
        UIUtil.Panel(r, PanelKind.Label);

        float ix = r.x + 14f * sc;
        UIUtil.Text(new Rect(ix, r.y + 8f * sc, 34f * sc, 16f * sc), no.ToString("00"), TextRole.Mono, UITheme.Terracotta);
        UIUtil.Icon(new Rect(ix, r.y + 26f * sc, 28f * sc, 28f * sc), IconForKind(kind), UITheme.Charcoal);

        float tx = ix + 44f * sc;
        string valTxt = "$" + d.Value;
        float valW = UIUtil.Measure(valTxt, TextRole.Numeric).x + 8f;
        UIUtil.Text(new Rect(r.xMax - valW - 14f * sc, r.y + 6f * sc, valW, 28f * sc), valTxt, TextRole.Numeric, UITheme.Bordeaux, TextAnchor.MiddleRight);

        float nameW = Mathf.Max(10f, r.xMax - valW - 24f * sc - tx);
        UIUtil.Text(new Rect(tx, r.y + 6f * sc, nameW, 28f * sc), ms.ArtworkName(d), TextRole.BodyStrong, UITheme.Charcoal);
        string how = Loc.T(WorkbenchUI.MethodKey(kind));
        UIUtil.Text(new Rect(tx, r.y + 34f * sc, r.xMax - tx - 14f * sc, 20f * sc),
            d.Artwork.weightKg.ToString("0.#") + " kg  ·  " + how, TextRole.Caption, UITheme.Graphite);

        float ey = r.y + 56f * sc;
        if (d.IsSwapped)
        {
            UIUtil.Icon(new Rect(tx, ey + 1f, 16f * sc, 16f * sc), IconId.Check, UITheme.Forest);
            UIUtil.Text(new Rect(tx + 22f * sc, ey, r.xMax - tx - 36f * sc, 18f * sc), Loc.T("brief.swapped", d.LeftSimilarity), TextRole.Caption, UITheme.Forest);
        }
        else if (d.IsStaffOnlyPiece) // özel hedef
        {
            UIUtil.Icon(new Rect(tx, ey + 1f, 16f * sc, 16f * sc), IconId.Lock, UITheme.Bordeaux);
            UIUtil.Text(new Rect(tx + 22f * sc, ey, r.xMax - tx - 36f * sc, 18f * sc), Loc.T("brief.staff"), TextRole.Caption, UITheme.Bordeaux);
        }
    }
}
