using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================================================
//  SAHTE ŞAHESER — TASARIM SİSTEMİ (token'lar, türler, yazı rolleri, doku önbelleği)
//  Görsel kimlik: müze sergisi + sanatçı eskiz defteri + arşiv/katalog + sergi etiketi.
//  Tüm çizim yardımcıları UIUtil'dedir; bu dosya yalnızca "ne" (renk, yazı, doku) bilgisini tutar.
//  Kurallar: UI_DESIGN.md (proje kökü).
// ============================================================================================

/// <summary>Panel türleri. Plaque = oyun içi koyu (varsayılan), diğerleri açık yüzey (kömür yazı).</summary>
public enum PanelKind { Plaque, Catalog, Sketch, Label }

/// <summary>Düğme türleri: Primary (bordo), Secondary (fildişi sekme), Danger (kırmızı hat), Tool (ikonlu kare).</summary>
public enum ButtonKind { Primary, Secondary, Danger, Tool }

/// <summary>Yazı rolleri (UITheme.Style). Boyutlar 1080p tabanlıdır, UITheme.Scale ile ölçeklenir.</summary>
public enum TextRole { Display, Title, Section, Body, BodyStrong, Button, Caption, Numeric, Mono }

public enum ToastKind { Info, Success, Warning, Danger }

public enum SlotState { Empty, Filled, Hover, Selected, Disabled }

public enum DialogChoice { None, Confirm, Cancel }

/// <summary>Simge ailesi (Resources/UI/Icons/icon_*.png, 64x64 beyaz çizgi; UIUtil.Icon ile renklendirilir).</summary>
public enum IconId
{
    Interact, Eye, Camera, Brush, Eraser, Undo, Bag, Money, Lock, Lockpick, Door,
    Painting, Vase, Bust, Ring, Mic, MicOff, Speaker, Check, Warning, Alarm, Jail,
    Map, Gear, Players, Chevron, Close, Clock, Truck
}

/// <summary>
/// Arayüz temasını, başka hiçbir OnGUI stil kopyalamadan önce uygular (kendini kurar) ve tasarım sisteminin
/// token'larını tutar. Toast kuyruğu varken her karede bildirimleri çizer, yoksa kendini kapatır.
/// </summary>
[DefaultExecutionOrder(-10000)]
public class UITheme : MonoBehaviour
{
    // ------------------------------------------------------------------ Renk token'ları
    public static readonly Color Ivory      = Hex(0xF2EAD8);   // kâğıt / açık yüzey, koyu üstünde ana yazı
    public static readonly Color IvoryDim   = Hex(0xD9CDB4);   // ikincil yazı (koyu üstünde)
    public static readonly Color Paper      = Hex(0xEFE6D2);   // eskiz kâğıdı
    public static readonly Color Charcoal   = Hex(0x1E1B19);   // plaket zemini, açık üstünde ana yazı
    public static readonly Color CharSoft   = Hex(0x2C2724);   // yuva / araç zemini
    public static readonly Color CharDeep   = Hex(0x141210);   // oluk, yazı alanı
    public static readonly Color Graphite   = Hex(0x4A443F);   // hat, açık üstünde ikincil yazı
    public static readonly Color Bordeaux   = Hex(0x7A1F2B);   // birincil eylem
    public static readonly Color Forest     = Hex(0x1F3A2E);   // koyu orman: güvenli alan / kamyon
    public static readonly Color Sage       = Hex(0x7FB58F);   // olumlu (koyu üstünde okunur yeşil)
    public static readonly Color Brass      = Hex(0xB08D57);   // çerçeve, bölüm başlığı, odak
    public static readonly Color BrassLight = Hex(0xD8B97A);   // vurgu, seçili, sayılar
    public static readonly Color Terracotta = Hex(0xB8664A);   // eskiz işaretleri, sıcak vurgu
    public static readonly Color Ochre      = Hex(0xD9A441);   // uyarı / şüphe
    public static readonly Color Danger     = Hex(0xC2483D);   // tehlike / alarm / hata
    public static readonly Color DangerLight= Hex(0xE58A7F);   // koyu üstünde tehlike yazısı

    /// <summary>Tam ekran perde (menü, diyalog arkası).</summary>
    public static readonly Color Scrim = new Color(0.078f, 0.07f, 0.063f, 0.62f);

    // ------------------------------------------------------------------ Boşluk ölçeği (px @1080p)
    public const float S1 = 4f, S2 = 8f, S3 = 12f, S4 = 16f, S5 = 24f, S6 = 32f, S7 = 48f, S8 = 64f;
    /// <summary>Hareket süreleri (sn, ölçeksiz zaman).</summary>
    public const float MotionFast = 0.12f, MotionBase = 0.18f, MotionSlow = 0.30f, ToastHold = 3.5f;

    /// <summary>Arayüz ölçeği: ekran yüksekliği / 1080 (0.85–1.6). Yalnızca yeni yazı rolleri ve yeni ekranlar kullanır.</summary>
    public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.85f, 1.6f);

    public static Color Hex(int rgb, float a = 1f) =>
        new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    /// <summary>Panel türünün üstündeki ana yazı rengi.</summary>
    public static Color TextOn(PanelKind kind) => kind == PanelKind.Plaque ? Ivory : Charcoal;

    /// <summary>Panel türünün üstündeki ikincil yazı rengi.</summary>
    public static Color MutedOn(PanelKind kind) => kind == PanelKind.Plaque ? IvoryDim : Graphite;

    /// <summary>Panel türünün üstündeki bölüm başlığı rengi.</summary>
    public static Color SectionOn(PanelKind kind) => kind == PanelKind.Plaque ? Brass : Bordeaux;

    public static Color ToastColor(ToastKind k)
    {
        switch (k)
        {
            case ToastKind.Success: return Sage;
            case ToastKind.Warning: return Ochre;
            case ToastKind.Danger: return Danger;
            default: return Brass;
        }
    }

    // ------------------------------------------------------------------ Doku önbelleği (Resources/UI/...)
    private static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

    /// <summary>Resources/UI/{path} dokusunu (uzantısız, ör. "Panels/panel_plaque") önbellekli yükler. Yoksa null.</summary>
    public static Texture2D Tex(string path)
    {
        if (texCache.TryGetValue(path, out Texture2D t)) return t;
        try { t = Resources.Load<Texture2D>("UI/" + path); } catch (Exception) { t = null; }
        if (t != null && path.StartsWith("Textures/tex_")) t.wrapMode = TextureWrapMode.Repeat;
        texCache[path] = t;
        return t;
    }

    private static string[] iconFiles;

    public static Texture2D IconTex(IconId id)
    {
        if (iconFiles == null)
        {
            Array values = Enum.GetValues(typeof(IconId));
            iconFiles = new string[values.Length];
            foreach (IconId v in values) iconFiles[(int)v] = "Icons/icon_" + Snake(v.ToString());
        }
        int i = (int)id;
        return i >= 0 && i < iconFiles.Length ? Tex(iconFiles[i]) : null;
    }

    private static string Snake(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Panel dokusu + 9 dilim kenarı. Plaque küçük kutularda küçük dokuya geçer.</summary>
    public static Texture2D PanelTex(PanelKind kind, Rect r, out RectOffset border)
    {
        switch (kind)
        {
            case PanelKind.Catalog: border = new RectOffset(16, 16, 16, 16); return Tex("Panels/panel_catalog");
            case PanelKind.Sketch:  border = new RectOffset(24, 24, 24, 24); return Tex("Panels/panel_sketch");
            case PanelKind.Label:   border = new RectOffset(8, 8, 8, 8);     return Tex("Panels/panel_label");
            default:
                if (r.width < 32f || r.height < 32f) { border = new RectOffset(6, 6, 6, 6); return Tex("Panels/panel_plaque_small"); }
                border = new RectOffset(12, 12, 12, 12); return Tex("Panels/panel_plaque");
        }
    }

    public static Color PanelFallback(PanelKind kind)
    {
        switch (kind)
        {
            case PanelKind.Catalog: return Ivory;
            case PanelKind.Sketch: return Paper;
            case PanelKind.Label: return Hex(0xF7F1E3);
            default: return WithAlpha(Charcoal, 0.93f);
        }
    }

    // ------------------------------------------------------------------ Yazı tipleri
    // Öncelik: Resources/UI/Fonts/{Display,Body,Mono} (projeye eklenecek OFL font) → işletim sistemi fontu → Unity yerleşik.
    private static bool fontsReady;
    private static Font displayFont, bodyFont, monoFont;

    public static Font DisplayFont { get { EnsureFonts(); return displayFont; } }
    public static Font BodyFont { get { EnsureFonts(); return bodyFont; } }
    public static Font MonoFont { get { EnsureFonts(); return monoFont; } }

    private static void EnsureFonts()
    {
        if (fontsReady) return;
        fontsReady = true;
        string[] installed = null;
        try { installed = Font.GetOSInstalledFontNames(); } catch (Exception) { }
        displayFont = LoadFont("Display", installed, "Georgia", "Constantia", "Palatino Linotype", "Book Antiqua", "DejaVu Serif", "Liberation Serif", "Times New Roman");
        bodyFont = LoadFont("Body", null);   // gövde: bilerek yerleşik font (Türkçe karakterler güvenli)
        monoFont = LoadFont("Mono", installed, "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Liberation Mono", "Menlo", "Courier New");
    }

    private static Font LoadFont(string resName, string[] installed, params string[] osNames)
    {
        Font f = null;
        try { f = Resources.Load<Font>("UI/Fonts/" + resName); } catch (Exception) { }
        if (f != null || installed == null) return f;
        foreach (string n in osNames)
        {
            if (Array.IndexOf(installed, n) < 0) continue;
            try { f = Font.CreateDynamicFontFromOSFont(n, 32); } catch (Exception) { f = null; }
            if (f != null) return f;
        }
        return null; // null → GUI.skin.font (Unity yerleşik)
    }

    // ------------------------------------------------------------------ Yazı rolleri
    private static GUIStyle[] roleStyles;
    private static float roleScale = -1f;

    /// <summary>Rolün temel boyutu (1080p).</summary>
    public static int BaseSize(TextRole role)
    {
        switch (role)
        {
            case TextRole.Display: return 54;
            case TextRole.Title: return 28;
            case TextRole.Section: return 13;
            case TextRole.Body: return 16;
            case TextRole.BodyStrong: return 16;
            case TextRole.Button: return 17;
            case TextRole.Caption: return 12;
            case TextRole.Numeric: return 20;
            default: return 14; // Mono
        }
    }

    /// <summary>
    /// Rolün PAYLAŞILAN stili (renk Ivory, hizalama MiddleLeft). Kalıcı değişiklik gerekiyorsa kopyala:
    /// new GUIStyle(UITheme.Style(TextRole.Body)). Yalnızca OnGUI içinde çağrılır.
    /// </summary>
    public static GUIStyle Style(TextRole role)
    {
        float sc = Scale;
        if (roleStyles == null || !Mathf.Approximately(sc, roleScale)) BuildRoleStyles(sc);
        GUIStyle s = roleStyles[(int)role];
        SetTextColor(s, Ivory);
        s.alignment = TextAnchor.MiddleLeft;
        return s;
    }

    /// <summary>Yazı rengi tüm durumlarda aynı: fare yazının üstüne gelince beyaza dönmesin (sadece düğmeler tepki verir).</summary>
    private static void SetTextColor(GUIStyle s, Color c)
    {
        s.normal.textColor = c;
        s.hover.textColor = c;
        s.active.textColor = c;
        s.focused.textColor = c;
        s.onNormal.textColor = c;
        s.onHover.textColor = c;
        s.onActive.textColor = c;
        s.onFocused.textColor = c;
    }

    /// <summary>Paylaşılan rol stili, verilen renk ve hizalamayla.</summary>
    public static GUIStyle Style(TextRole role, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        GUIStyle s = Style(role);
        SetTextColor(s, color);
        s.alignment = anchor;
        return s;
    }

    private static void BuildRoleStyles(float sc)
    {
        roleScale = sc;
        EnsureFonts();
        GUIStyle baseLabel = GUI.skin != null ? GUI.skin.label : new GUIStyle();
        int n = Enum.GetValues(typeof(TextRole)).Length;
        roleStyles = new GUIStyle[n];
        for (int i = 0; i < n; i++)
        {
            var role = (TextRole)i;
            var s = new GUIStyle(baseLabel)
            {
                fontSize = Mathf.RoundToInt(BaseSize(role) * sc),
                wordWrap = role == TextRole.Body || role == TextRole.Caption,
                richText = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            switch (role)
            {
                case TextRole.Display:
                case TextRole.Title:
                    if (displayFont != null) s.font = displayFont;
                    s.fontStyle = FontStyle.Bold;
                    break;
                case TextRole.Numeric:
                case TextRole.Mono:
                    if (monoFont != null) s.font = monoFont;
                    s.fontStyle = role == TextRole.Numeric ? FontStyle.Bold : FontStyle.Normal;
                    break;
                case TextRole.Section:
                case TextRole.BodyStrong:
                case TextRole.Button:
                    if (bodyFont != null) s.font = bodyFont;
                    s.fontStyle = FontStyle.Bold;
                    break;
                default:
                    if (bodyFont != null) s.font = bodyFont;
                    s.fontStyle = FontStyle.Normal;
                    break;
            }
            roleStyles[i] = s;
        }
    }

    /// <summary>Dile duyarlı BÜYÜK HARF (Türkçe'de i → İ). Bölüm başlıkları ve küçük etiketler için.</summary>
    public static string Caps(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        try
        {
            return Loc.Current == Language.Turkish
                ? s.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))
                : s.ToUpperInvariant();
        }
        catch (Exception) { return s.ToUpperInvariant(); }
    }

    // ------------------------------------------------------------------ Önyükleme + toast çizimi
    private static UITheme instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("UITheme");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UITheme>();
    }

    /// <summary>Toast kuyruğu dolunca çizim için kendini açar (UIUtil.ShowToast çağırır).</summary>
    internal static void Wake()
    {
        if (instance != null) instance.enabled = true;
    }

    private void OnGUI()
    {
        UIUtil.ApplySkin();
        GUI.depth = -50; // bildirimler diğer OnGUI ekranlarının üstünde
        if (!UIUtil.DrawToasts()) enabled = false; // kuyruk boş: kapat, her karede OnGUI maliyeti olmasın
    }
}
