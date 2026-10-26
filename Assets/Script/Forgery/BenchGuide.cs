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
    private static GUIStyle bodyStyle, ruleStyle, noteStyle, headStyle, stepStyle;

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

    private static void EnsureStyles()
    {
        if (bodyStyle != null) return;
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
        bodyStyle.normal.textColor = Color.white;
        ruleStyle = new GUIStyle(bodyStyle) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        ruleStyle.normal.textColor = new Color(1f, 0.93f, 0.7f);
        noteStyle = new GUIStyle(bodyStyle) { fontSize = 12, alignment = TextAnchor.UpperRight };
        noteStyle.normal.textColor = new Color(1f, 1f, 1f, 0.78f);
        headStyle = new GUIStyle(bodyStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
        stepStyle = new GUIStyle(bodyStyle) { fontSize = 14 };
    }

    // ---------------- Öğretici kutu ----------------

    /// <summary>Açıksa ekranın ortasına öğretici kutuyu çizer (her şeyin ÜSTÜNDE; GUI.enabled = true iken çağır).</summary>
    public static void DrawTutorial()
    {
        if (!Showing) return;
        EnsureStyles();
        string k = Suffix(shownKind);

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(0f, 0f, 0f, 0.6f), 0, 0);
        float w = Mathf.Min(680f, Screen.width - 40f);
        float h = Mathf.Min(Screen.height - 60f, 470f);
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        UIUtil.DrawRect(r, new Color(0.06f, 0.07f, 0.1f, 0.96f));
        UIUtil.DrawRect(new Rect(r.x, r.y, 6f, r.height), new Color(1f, 0.82f, 0.3f));

        GUILayout.BeginArea(new Rect(r.x + 24f, r.y + 16f, r.width - 48f, r.height - 32f));
        GUILayout.Label(Loc.T("tut." + k + ".title"), headStyle);
        GUILayout.Space(8f);
        GUILayout.Label(Loc.T("tut." + k + ".body"), bodyStyle);
        GUILayout.FlexibleSpace();
        dontShow = GUILayout.Toggle(dontShow, " " + Loc.T("tut.dontshow"), GUILayout.Height(26f));
        GUILayout.Space(6f);
        if (GUILayout.Button(Loc.T("tut.ok"), UIUtil.ButtonStyle, GUILayout.Height(38f))) Close();
        GUILayout.Label(Loc.T("tut.hinth"), UIUtil.SmallStyle);
        GUILayout.EndArea();
    }

    // ---------------- Kural şeridi ----------------

    /// <summary>Eserin yapım kuralı + başarı ölçütü (tek / iki satır).</summary>
    public static void DrawRules(ArtKind kind, Rect r)
    {
        EnsureStyles();
        UIUtil.DrawRect(r, new Color(0f, 0f, 0f, 0.6f));
        UIUtil.DrawRect(new Rect(r.x, r.y, 4f, r.height), new Color(1f, 0.82f, 0.3f));
        GUI.Label(new Rect(r.x + 10f, r.y, r.width - 14f, r.height), Loc.T("rules." + Suffix(kind)), ruleStyle);
    }

    // ---------------- Arkadaşlar ne yapabilir? ----------------

    /// <summary>Küçük, sağa yaslı not (çizim uzun sürerken diğerleri boş durmasın).</summary>
    public static void DrawOthersNote(Rect r)
    {
        EnsureStyles();
        GUI.Label(r, Loc.T("bench.others"), noteStyle);
    }

    // ---------------- Yüzük rehberi ----------------

    /// <summary>Sağ tarafta adım adım yüzük yapımı: aktif adım vurgulu, tamamlananlar işaretli.</summary>
    public static void DrawRingGuide(Rect r, int step, JewelCraft craft)
    {
        EnsureStyles();
        UIUtil.DrawRect(r, new Color(0.04f, 0.05f, 0.08f, 0.9f));
        UIUtil.DrawRect(new Rect(r.x, r.y, 4f, r.height), new Color(1f, 0.82f, 0.3f));

        bool[] done =
        {
            craft != null && craft.fill > 0.05f,
            craft != null && craft.count > 0,
            craft != null && craft.polish > 0.5f,
        };
        string[] titles = { Loc.T("ring.step.cast"), Loc.T("ring.step.stones"), Loc.T("ring.step.polish") };

        GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 8f, r.width - 22f, r.height - 12f));
        GUILayout.Label(Loc.T("ring.guide.title"), headStyle);
        GUILayout.Space(4f);
        for (int i = 0; i < 3; i++)
        {
            bool cur = i == step;
            string mark = done[i] ? "[x] " : "[ ] ";
            string head = (cur ? "<color=#FFD54D>" : "<color=#BBBBBB>") + mark + (i + 1) + ". " + titles[i] + "</color>";
            GUILayout.Label("<b>" + head + "</b>", stepStyle);
            if (cur) GUILayout.Label(Loc.T("ring.guide.d" + (i + 1)), bodyStyle.FontSize(13));
            GUILayout.Space(2f);
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("ring.guide.goal"), bodyStyle.FontSize(12));
        GUILayout.Label(Loc.T("bench.others"), noteStyle);
        GUILayout.EndArea();
    }

    private static GUIStyle FontSize(this GUIStyle s, int size)
    {
        // Paylaşılan stili değiştirmeden boyutu ayarla: ucuz kopya önbelleği
        if (s == null) return null;
        GUIStyle c;
        if (!sizeCache.TryGetValue(s.fontSize * 1000 + size, out c) || c == null)
        {
            c = new GUIStyle(s) { fontSize = size };
            sizeCache[s.fontSize * 1000 + size] = c;
        }
        return c;
    }

    private static readonly System.Collections.Generic.Dictionary<int, GUIStyle> sizeCache = new System.Collections.Generic.Dictionary<int, GUIStyle>();
}
