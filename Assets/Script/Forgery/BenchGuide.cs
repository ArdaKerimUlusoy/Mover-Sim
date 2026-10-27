using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Atölye rehberi (sadece yerel arayüz, ağ yok):
///  - Öğretici kutu: her eser türünün ilk açılışında bir kez çıkar ("Bir daha gösterme" PlayerPrefs'e yazılır).
///    H tuşu istenen zaman yeniden açar / kapatır. Açıkken altındaki arayüz tıklanamaz (WorkbenchUI GUI.enabled'ı kapatır).
///  - Kural şeridi: seçili eserin yapım kuralı ve başarı ölçütü (tablo, vazo, büst, yüzük birbirinden net ayrılır).
///  - Yüzük rehberi: sağ tarafta adım adım nasıl yapılacağı.
///  - Not: çizen oyuncu uzun süre çalışırken diğerlerinin yapabilecekleri.
/// </summary>
public static class BenchGuide
{
    /// <summary>Öğretici kutu şu an açık mı? (açıkken çalışma girdileri durur)</summary>
    public static bool Showing { get; private set; }

    private static ArtKind shownKind;
    private static bool dontShow;

    private static string Suffix(ArtKind k)
    {
        switch (k)
        {
            case ArtKind.Painting: return "painting";
            case ArtKind.Vase: return "vase";
            case ArtKind.Bust: return "bust";
            case ArtKind.Jewelry: return "ring";
            default: return "painting";
        }
    }

    private static string PrefKey(ArtKind k) => "bench.tutorial.seen." + Suffix(k);

    private static bool Seen(ArtKind k)
    {
        try { return PlayerPrefs.GetInt(PrefKey(k), 0) == 1; }
        catch { return false; }
    }

    private static void MarkSeen(ArtKind k, bool seen)
    {
        try { PlayerPrefs.SetInt(PrefKey(k), seen ? 1 : 0); PlayerPrefs.Save(); }
        catch { /* depolama yoksa sessizce geç */ }
    }

    /// <summary>Çalışma başladı: bu tür daha önce görülmediyse öğretici açılır.</summary>
    public static void OnWorkStart(ArtKind kind)
    {
        shownKind = kind;
        dontShow = Seen(kind);
        Showing = !Seen(kind);
    }

    /// <summary>Atölye kapandı.</summary>
    public static void OnWorkEnd() => Showing = false;

    /// <summary>H: aç / kapat, Esc (açıkken): kapat. true dönerse bu karede başka girdi işlenmemeli.</summary>
    public static bool Tick(Keyboard kb)
    {
        if (kb == null) return Showing;
        if (kb.hKey.wasPressedThisFrame)
        {
            if (Showing) Close();
            else { dontShow = Seen(shownKind); Showing = true; }
            return true;
        }
        if (Showing && kb.escapeKey.wasPressedThisFrame) { Close(); return true; }
        return Showing;
    }

    private static void Close()
    {
        if (dontShow) MarkSeen(shownKind, true);
        else MarkSeen(shownKind, false);
        Showing = false;
    }

    // ---------------- Yardımcılar ----------------

    /// <summary>Yazı yüksekliği tahmini (GUI'ye dokunmaz; Update içinden de çağrılabilir). Caption ~ 12 px @1080.</summary>
    private static float Estimate(string text, float width)
    {
        float fontPx = 12f * UITheme.Scale;
        float charW = fontPx * 0.58f;
        int cpl = Mathf.Max(8, Mathf.FloorToInt(width / charW));
        int lines = 0;
        foreach (string part in text.Split('\n')) lines += Mathf.Max(1, Mathf.CeilToInt(part.Length / (float)cpl));
        return lines * fontPx * 1.35f;
    }

    private static GUIStyle richCaption;
    private static int richCaptionSize;

    /// <summary>Caption ölçüsünde, zengin metin destekli paylaşılmayan stil (çözünürlük değişince yenilenir).</summary>
    private static GUIStyle RichCaption()
    {
        GUIStyle c = UITheme.Style(TextRole.Caption);
        if (richCaption == null || richCaptionSize != c.fontSize)
        {
            richCaption = new GUIStyle(c) { richText = true, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            richCaptionSize = c.fontSize;
        }
        richCaption.normal.textColor = UITheme.Ivory;
        return richCaption;
    }

    // ---------------- Öğretici kutu ----------------

    /// <summary>Açıksa ekranın ortasına öğretici kutuyu çizer (her şeyin ÜSTÜNDE; GUI.enabled = true iken çağır). Eskiz kâğıdı kartı.</summary>
    public static void DrawTutorial()
    {
        if (!Showing) return;
        UIUtil.ApplySkin();
        string k = Suffix(shownKind);
        float sc = UITheme.Scale;
        float m = BenchKit.M;

        UIUtil.Scrim(UITheme.Scrim);
        UIUtil.Vignette(0.8f);
        float w = Mathf.Min(Mathf.Clamp(Screen.width * 0.5f, 460f, 700f), Screen.width - m * 2f);
        float pad = BenchKit.Px(30f, 22f);
        float iw = w - pad * 2f;
        string title = Loc.T("tut." + k + ".title");
        string body = Loc.T("tut." + k + ".body");
        string dont = " " + Loc.T("tut.dontshow");
        float titleH = Mathf.Max(34f, 38f * sc);
        float bodyH = UIUtil.MeasureHeight(body, TextRole.Body, iw - 16f);
        float toggleH = Mathf.Max(26f, 28f * sc);
        float btnH = Mathf.Max(36f, 42f * sc);
        float fixedH = pad + titleH + 14f + 10f + toggleH + 10f + btnH + 8f + 22f + pad;
        float h = Mathf.Min(Screen.height - m * 2f, fixedH + bodyH);
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        UIUtil.Panel(r, PanelKind.Sketch);
        UIUtil.Corners(r);

        // Başlık
        BenchKit.FitText(new Rect(r.x + pad, r.y + pad, iw, titleH), title, TextRole.Title, UITheme.Charcoal);
        UIUtil.Divider(r.x + pad, r.y + pad + titleH + 6f, iw, UITheme.WithAlpha(UITheme.Bordeaux, 0.7f), true);

        // Gövde (taşarsa kaydırılır)
        float bodyTop = r.y + pad + titleH + 14f;
        Rect view = new Rect(r.x + pad, bodyTop, iw, r.yMax - pad - 22f - btnH - 8f - toggleH - 10f - bodyTop - 10f);
        bool scrolls = bodyH > view.height;
        tutScroll = GUI.BeginScrollView(view, tutScroll, new Rect(0f, 0f, iw - (scrolls ? 16f : 0f), bodyH));
        BenchKit.Wrapped(new Rect(0f, 0f, iw - (scrolls ? 16f : 0f) - 8f, bodyH), body, TextRole.Body, UITheme.Charcoal);
        GUI.EndScrollView();

        // Bir daha gösterme + Tamam
        float y = view.yMax + 10f;
        dontShow = GUI.Toggle(new Rect(r.x + pad, y, iw, toggleH), dontShow, dont);
        y += toggleH + 10f;
        if (UIUtil.Button(new Rect(r.x + pad, y, iw, btnH), Loc.T("tut.ok"), ButtonKind.Primary, true, true)) Close();
        y += btnH + 8f;

        // H ipucu (KeyCap)
        string hText = Loc.T("tut.hinth");
        int cut = hText.IndexOf(": ");
        string key = cut > 0 && cut <= 4 ? hText.Substring(0, cut) : "H";
        string lab = cut > 0 && cut <= 4 ? hText.Substring(cut + 2) : hText;
        float kh = Mathf.Max(20f, 22f * sc);
        float kw = UIUtil.KeyCapWidth(key);
        UIUtil.KeyCap(new Rect(r.x + pad, y, kw, kh), key);
        UIUtil.Text(new Rect(r.x + pad + kw + 8f, y, iw - kw - 8f, kh), lab, TextRole.Caption, UITheme.Graphite);
    }

    private static Vector2 tutScroll;

    // ---------------- Kural şeridi ----------------

    /// <summary>Kural şeridi yüksekliği (tahmin; GUI gerektirmez).</summary>
    public static float RulesHeight(ArtKind kind, float width)
    {
        return Mathf.Max(BenchKit.Px(30f, 26f), Estimate(Loc.T("rules." + Suffix(kind)), width - 36f) + 12f);
    }

    /// <summary>Eserin yapım kuralı + başarı ölçütü: koyu plaket şerit, başlık pirinç, metin fildişi.</summary>
    public static void DrawRules(ArtKind kind, Rect r)
    {
        UIUtil.Panel(r, PanelKind.Plaque);
        UIUtil.DrawRect(new Rect(r.x + 1f, r.y + 3f, 3f, r.height - 6f), UITheme.Brass);
        string t = Loc.T("rules." + Suffix(kind));
        int cut = t.IndexOf(": ");
        if (cut > 0 && cut < 24)
            t = "<b><color=#" + ColorUtility.ToHtmlStringRGB(UITheme.BrassLight) + ">" + t.Substring(0, cut + 1) + "</color></b>" + t.Substring(cut + 1);
        GUI.Label(new Rect(r.x + 14f, r.y, r.width - 22f, r.height), t, RichCaption());
    }

    // ---------------- Arkadaşlar ne yapabilir? ----------------

    public static float NoteHeight(float width) => Mathf.Max(BenchKit.Px(30f, 26f), Estimate(Loc.T("bench.others"), width - 20f) + 8f);

    /// <summary>Küçük not (çizim uzun sürerken diğerleri boş durmasın).</summary>
    public static void DrawOthersNote(Rect r)
    {
        BenchKit.Wrapped(r, Loc.T("bench.others"), TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight);
    }

    // ---------------- Yüzük rehberi ----------------

    /// <summary>Sağ tarafta adım adım yüzük yapımı (eskiz kâğıdı): aktif adım bordo, tamamlananlar onay işaretli.</summary>
    public static void DrawRingGuide(Rect r, int step, JewelCraft craft)
    {
        UIUtil.Panel(r, PanelKind.Sketch);
        bool[] done =
        {
            craft != null && craft.fill > 0.05f,
            craft != null && craft.count > 0,
            craft != null && craft.polish > 0.5f,
        };
        string[] titles = { Loc.T("ring.step.cast"), Loc.T("ring.step.stones"), Loc.T("ring.step.polish") };

        float px = BenchKit.Px(20f, 16f), py = BenchKit.Px(18f, 14f);
        Rect inner = new Rect(r.x + px, r.y + py, r.width - px * 2f, r.height - py * 2f);
        GUI.BeginGroup(inner); // taşan içerik kırpılır
        var f = new Flow(0f, 0f, inner.width);
        f.Section(Loc.T("ring.guide.title"), PanelKind.Sketch);
        float icon = Mathf.Max(16f, 18f * UITheme.Scale);
        for (int i = 0; i < 3; i++)
        {
            bool cur = i == step;
            Color tc = cur ? UITheme.Bordeaux : UITheme.Graphite;
            Rect row = f.Row(icon + 4f);
            if (done[i]) UIUtil.Icon(new Rect(row.x, row.y + 1f, icon, icon), IconId.Check, UITheme.Forest);
            else UIUtil.Outline(new Rect(row.x + 3f, row.y + 4f, icon - 6f, icon - 6f), 1f, UITheme.Graphite);
            UIUtil.Text(new Rect(row.x + icon + 8f, row.y, row.width - icon - 8f, row.height), (i + 1) + ". " + titles[i], cur ? TextRole.BodyStrong : TextRole.Body, tc);
            if (cur)
            {
                var sub = new Flow(f.X + icon + 8f, f.Y, f.W - icon - 8f);
                sub.Text(Loc.T("ring.guide.d" + (i + 1)), TextRole.Caption, UITheme.Charcoal);
                f.Y = sub.Y;
            }
            f.Gap(6f);
        }
        f.Gap(4f);
        UIUtil.DrawRect(new Rect(0f, f.Y, inner.width, 1f), UITheme.WithAlpha(UITheme.Graphite, 0.35f));
        f.Gap(6f);
        f.Text(Loc.T("ring.guide.goal"), TextRole.Caption, UITheme.Charcoal);
        f.Gap(4f);
        f.Text(Loc.T("bench.others"), TextRole.Caption, UITheme.Graphite);
        GUI.EndGroup();
    }
}
