using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OnGUI arayüzleri için ortak çizim yardımcıları — "Sahte Şaheser" sade arayüz sistemi.
///
/// Görsel dil (UI_DESIGN.md): düz, sakin, oyun içi. Yarı saydam koyu kartlar (yuvarlatılmış köşe, ince kenar),
/// açık yüzeyler için düz kırık beyaz kart; süs YOK: köşe süsü, pirinç çerçeve, başlık altı kısa çizgi, numaralı liste,
/// eskiz ızgarası, tuş kapağı şeritleri kullanılmaz. Her şey kodla çizilir (GUI.DrawTexture yuvarlatma) — doku gerekmez.
/// ESKİ API (Panel, DrawRect, BoxStyle, TitleStyle, ..., Gold, Cream, Corners, Tape, BrassFrame) imzasıyla korunur;
/// süs fonksiyonları artık hiçbir şey çizmez ya da ince bir hatta dönüşür.
/// HUD köşe yerleşimi (HudRect): aynı köşedeki kutular önceliğe göre üst üste DİZİLİR, asla çakışmaz.
/// </summary>
public static class UIUtil
{
    // ---------------- Eski tema renkleri (imza korunur) ----------------
    public static readonly Color Ink = UITheme.WithAlpha(UITheme.Charcoal, 0.93f);   // panel zemini
    public static readonly Color Frame = UITheme.WithAlpha(UITheme.Brass, 0.95f);     // vurgu
    public static readonly Color Gold = UITheme.BrassLight;                           // başlık / vurgu
    public static readonly Color Cream = UITheme.Ivory;                               // ana yazı (koyu üstünde)
    public static readonly Color Muted = UITheme.IvoryDim;                            // ikincil yazı
    public static readonly Color Wine = UITheme.Bordeaux;                             // düğme
    public static readonly Color Mint = UITheme.Sage;                                 // olumlu
    public static readonly Color Coral = UITheme.DangerLight;                         // olumsuz / tehlike (koyu üstünde)

    // ---------------- Yüzey renkleri (yeni sistem) ----------------
    /// <summary>Koyu kart (menü, pencere): yarı saydam kömür.</summary>
    public static readonly Color SurfaceDark = new Color(0.075f, 0.07f, 0.066f, 0.9f);
    /// <summary>Koyu kartın ince kenarı.</summary>
    public static readonly Color SurfaceDarkEdge = new Color(1f, 1f, 1f, 0.07f);
    /// <summary>HUD kartı (oyun dünyası üstünde, daha saydam).</summary>
    public static readonly Color SurfaceHud = new Color(0.05f, 0.047f, 0.044f, 0.62f);
    /// <summary>Açık kart (belge / tezgah notu): düz kırık beyaz.</summary>
    public static readonly Color SurfaceLight = new Color(0.945f, 0.93f, 0.895f, 0.98f);
    public static readonly Color SurfaceLightEdge = new Color(0f, 0f, 0f, 0.10f);
    /// <summary>İç kutu / yuva zemini (koyu, her iki yüzeyde de okunur).</summary>
    public static readonly Color Well = new Color(0.14f, 0.13f, 0.12f, 0.92f);
    public static readonly Color WellHover = new Color(0.2f, 0.185f, 0.17f, 0.96f);

    public const float RadiusBig = 10f, RadiusSmall = 6f;

    private static GUIStyle promptStyle;
    private static bool texturesReady;
    private static Texture2D panelTex, panelSmallTex, btnTex, btnHoverTex, btnActiveTex, fieldTex, fieldFocusTex;
    private static Texture2D iconBrush, iconLockpick, iconPaint;
    private static RectOffset panelBorder, panelSmallBorder, buttonBorder, fieldBorder;

    private static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

    /// <summary>Temanın panel dokusu (9 dilim; kenar payı PanelBorder).</summary>
    public static Texture2D PanelTexture { get { EnsureTextures(); return panelTex; } }
    public static RectOffset PanelBorder { get { EnsureTextures(); return new RectOffset(panelBorder.left, panelBorder.right, panelBorder.top, panelBorder.bottom); } }

    /// <summary>Hızlı envanter / alet rozetleri için simgeler. Yoksa null.</summary>
    public static Texture2D IconBrush { get { EnsureTextures(); return iconBrush; } }
    public static Texture2D IconLockpick { get { EnsureTextures(); return iconLockpick; } }
    public static Texture2D IconPaint { get { EnsureTextures(); return iconPaint; } }

    // ---- Panel stilleri (eski ekranlar kullanır) ----
    private static GUIStyle box, title, row, small, button, hint, accent;

    public static GUIStyle BoxStyle { get { EnsureStyles(); return box; } }
    public static GUIStyle TitleStyle { get { EnsureStyles(); return title; } }
    public static GUIStyle RowStyle { get { EnsureStyles(); return row; } }
    public static GUIStyle SmallStyle { get { EnsureStyles(); return small; } }
    public static GUIStyle ButtonStyle { get { EnsureStyles(); return button; } }
    public static GUIStyle HintStyle { get { EnsureStyles(); return hint; } }
    public static GUIStyle AccentStyle { get { EnsureStyles(); return accent; } }

    private static void EnsureStyles()
    {
        if (box != null) return;
        ApplySkin();

        box = ThemeBox(new GUIStyle(GUI.skin.box));

        title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (UITheme.BodyFont != null) title.font = UITheme.BodyFont;
        title.normal.textColor = Cream;

        row = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        row.normal.textColor = Cream;

        small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        small.normal.textColor = Muted;

        hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, wordWrap = false };

        accent = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        accent.normal.textColor = Cream;

        button = ThemeButton(new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold });
    }

    // ---------------- Eski GUISkin için düz dokular (kodla üretilir) ----------------

    private static RectOffset RO(int l, int r, int t, int b) => new RectOffset(l, r, t, b);

    private static void EnsureTextures()
    {
        if (texturesReady) return;
        texturesReady = true;

        panelTex = MakeRounded(8, 1f, SurfaceDark, new Color(0.2f, 0.19f, 0.18f, 0.9f)); panelBorder = RO(10, 10, 10, 10);
        panelSmallTex = MakeRounded(5, 1f, SurfaceDark, new Color(0.2f, 0.19f, 0.18f, 0.9f)); panelSmallBorder = RO(7, 7, 7, 7);

        btnTex = MakeRounded(5, 0f, UITheme.Bordeaux, UITheme.Bordeaux);
        btnHoverTex = MakeRounded(5, 0f, UITheme.Hex(0x93303D), UITheme.Hex(0x93303D));
        btnActiveTex = MakeRounded(5, 0f, UITheme.Hex(0x5E1721), UITheme.Hex(0x5E1721));
        buttonBorder = RO(7, 7, 7, 7);

        fieldTex = MakeRounded(5, 1f, new Color(0.05f, 0.047f, 0.044f, 0.95f), new Color(1f, 1f, 1f, 0.14f));
        fieldFocusTex = MakeRounded(5, 1.5f, new Color(0.05f, 0.047f, 0.044f, 0.98f), new Color(1f, 1f, 1f, 0.5f));
        fieldBorder = RO(7, 7, 7, 7);

        iconBrush = UITheme.IconTex(IconId.Brush);
        iconLockpick = UITheme.IconTex(IconId.Lockpick);
        iconPaint = UITheme.IconTex(IconId.Painting);
    }

    /// <summary>Yuvarlatılmış, kenarı çizgili doku (yumuşatılmış). 9 dilim için orta 1 piksel uzar.</summary>
    private static Texture2D MakeRounded(int radius, float borderW, Color fill, Color border)
    {
        int size = radius * 2 + 5;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
        };
        var px = new Color[size * size];
        float half = size * 0.5f, ext = half - 0.5f - radius;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - ext;
                float qy = Mathf.Abs(y + 0.5f - half) - ext;
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float cover = Mathf.Clamp01(0.5f - outside);
                float edge = borderW > 0f ? Mathf.Clamp01(outside + borderW + 0.5f) : 0f;
                Color c = Color.Lerp(fill, border, edge);
                c.a *= cover;
                px[y * size + x] = c;
            }
        }
        tex.SetPixels(px);
        tex.Apply(false, true);
        return tex;
    }

    /// <summary>Kutu stilini temaya çevirir (koyu kart).</summary>
    public static GUIStyle ThemeBox(GUIStyle s, bool big = false)
    {
        EnsureTextures();
        s.normal.background = big ? panelTex : panelSmallTex;
        RectOffset b = big ? panelBorder : panelSmallBorder;
        s.border = new RectOffset(b.left, b.right, b.top, b.bottom);
        s.normal.textColor = Cream;
        return s;
    }

    /// <summary>Düğme stilini temaya çevirir (düz bordo; üzerine gelince açılır).</summary>
    public static GUIStyle ThemeButton(GUIStyle s)
    {
        EnsureTextures();
        s.normal.background = btnTex;
        s.hover.background = btnHoverTex;
        s.active.background = btnActiveTex;
        s.focused.background = btnTex;
        s.onNormal.background = btnActiveTex;
        s.onHover.background = btnHoverTex;
        s.onActive.background = btnActiveTex;
        s.onFocused.background = btnActiveTex;
        s.border = new RectOffset(buttonBorder.left, buttonBorder.right, buttonBorder.top, buttonBorder.bottom);
        s.padding = new RectOffset(Mathf.Max(s.padding.left, 12), Mathf.Max(s.padding.right, 12), s.padding.top, s.padding.bottom);
        s.normal.textColor = Cream;
        s.focused.textColor = Cream;
        s.onNormal.textColor = Color.white;
        s.hover.textColor = Color.white;
        s.onHover.textColor = Color.white;
        s.active.textColor = Color.white;
        s.onActive.textColor = Color.white;
        return s;
    }

    /// <summary>Yazı kutusu stilini temaya çevirir (koyu, ince açık kenar; odakta belirgin kenar).</summary>
    public static GUIStyle ThemeField(GUIStyle s)
    {
        EnsureTextures();
        s.normal.background = fieldTex;
        s.hover.background = fieldTex;
        s.focused.background = fieldFocusTex;
        s.active.background = fieldFocusTex;
        s.onNormal.background = fieldTex;
        s.onFocused.background = fieldFocusTex;
        s.border = new RectOffset(fieldBorder.left, fieldBorder.right, fieldBorder.top, fieldBorder.bottom);
        s.normal.textColor = Cream;
        s.hover.textColor = Cream;
        s.focused.textColor = Color.white;
        s.active.textColor = Color.white;
        s.onNormal.textColor = Cream;
        s.onFocused.textColor = Color.white;
        return s;
    }

    private static GUISkin skinApplied;

    /// <summary>
    /// Varsayılan GUI kutusu / düğmesi / yazı kutusu / kaydırıcı / kaydırma çubuğunu temaya çevirir (bir kez).
    /// Boyutlar (fixedWidth/Height) DEĞİŞMEZ; yalnızca dokular ve renkler. Sadece OnGUI içinde çağrılabilir.
    /// </summary>
    public static void ApplySkin()
    {
        GUISkin s = GUI.skin;
        if (s == null || s == skinApplied) return;
        skinApplied = s;
        ThemeBox(s.box);
        ThemeButton(s.button);
        ThemeField(s.textField);
        ThemeField(s.textArea);
        s.label.normal.textColor = Cream;
        // Etiketler fareye tepki vermesin (üstüne gelince beyaza dönmesin; sadece düğmeler tepki verir)
        s.label.hover.textColor = Cream; s.label.active.textColor = Cream; s.label.focused.textColor = Cream;
        s.label.onNormal.textColor = Cream; s.label.onHover.textColor = Cream; s.label.onActive.textColor = Cream; s.label.onFocused.textColor = Cream;
        s.settings.selectionColor = new Color(1f, 1f, 1f, 0.25f);
        s.settings.cursorColor = UITheme.Ivory;

        // Kaydırıcı: ince yuvarlak oluk + yuvarlak tutamak
        Texture2D track = MakeRounded(2, 0f, new Color(1f, 1f, 1f, 0.16f), new Color(1f, 1f, 1f, 0.16f));
        Texture2D thumb = MakeRounded(6, 0f, UITheme.Ivory, UITheme.Ivory);
        Texture2D thumbHi = MakeRounded(6, 0f, Color.white, Color.white);
        s.horizontalSlider.normal.background = track;
        s.horizontalSlider.border = RO(3, 3, 3, 3);
        s.horizontalSlider.fixedHeight = 4f;
        s.horizontalSlider.margin = new RectOffset(s.horizontalSlider.margin.left, s.horizontalSlider.margin.right, 8, 8);
        GUIStyle th = s.horizontalSliderThumb;
        th.normal.background = thumb; th.hover.background = thumbHi; th.active.background = thumbHi; th.focused.background = thumb;
        th.border = RO(0, 0, 0, 0);
        th.fixedWidth = 14f; th.fixedHeight = 14f;
        th.margin = new RectOffset(0, 0, -5, 0);

        Texture2D sTrack = MakeRounded(3, 0f, new Color(0f, 0f, 0f, 0.25f), new Color(0f, 0f, 0f, 0.25f));
        Texture2D sThumb = MakeRounded(3, 0f, new Color(1f, 1f, 1f, 0.35f), new Color(1f, 1f, 1f, 0.35f));
        Texture2D sThumbHi = MakeRounded(3, 0f, new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.55f));
        s.verticalScrollbar.normal.background = sTrack;
        s.verticalScrollbar.border = RO(4, 4, 4, 4);
        s.verticalScrollbarThumb.normal.background = sThumb;
        s.verticalScrollbarThumb.hover.background = sThumbHi;
        s.verticalScrollbarThumb.active.background = sThumbHi;
        s.verticalScrollbarThumb.border = RO(4, 4, 4, 4);
        s.horizontalScrollbar.normal.background = sTrack;
        s.horizontalScrollbar.border = RO(4, 4, 4, 4);
        s.horizontalScrollbarThumb.normal.background = sThumb;
        s.horizontalScrollbarThumb.hover.background = sThumbHi;
        s.horizontalScrollbarThumb.border = RO(4, 4, 4, 4);
    }

    // ---------------- Temel çizim ----------------

    /// <summary>Ekran ortasında panel kutusu çizer ve iç alanı döner (eski API).</summary>
    public static Rect BeginCenteredPanel(float w, float h)
    {
        Rect panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        Panel(panel);
        Rect inner = new Rect(panel.x + 22f, panel.y + 16f, panel.width - 44f, panel.height - 32f);
        GUILayout.BeginArea(inner);
        return panel;
    }

    public static void EndCenteredPanel() => GUILayout.EndArea();

    public static void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>Yuvarlatılmış dolu dikdörtgen (yalnızca Repaint).</summary>
    public static void Round(Rect r, Color c, float radius = RadiusSmall)
    {
        if (!IsRepaint || c.a <= 0.001f || r.width <= 0f || r.height <= 0f) return;
        radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, 0f, radius);
    }

    /// <summary>Yuvarlatılmış kenar çizgisi (yalnızca Repaint).</summary>
    public static void RoundLine(Rect r, Color c, float width = 1f, float radius = RadiusSmall)
    {
        if (!IsRepaint || c.a <= 0.001f || r.width <= 0f || r.height <= 0f) return;
        radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f);
        GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, width, radius);
    }

    /// <summary>Koyu kart (eski API: Plaque). accent artık çizilmez (sade görünüm).</summary>
    public static void Panel(Rect r, float alpha = 1f) => Panel(r, PanelKind.Plaque, alpha);

    public static void Panel(Rect r, Color accent, float alpha = 1f) => Panel(r, PanelKind.Plaque, alpha);

    /// <summary>
    /// Kart: Plaque = koyu yarı saydam (menü / pencere), Catalog / Sketch / Label = düz açık kart (belge, tezgah notu).
    /// Süs, doku ya da ızgara yoktur.
    /// </summary>
    public static void Panel(Rect r, PanelKind kind, float alpha = 1f)
    {
        if (!IsRepaint) return;
        float rad = r.width >= 120f && r.height >= 60f ? RadiusBig : RadiusSmall;
        if (kind == PanelKind.Plaque)
        {
            Round(r, UITheme.WithAlpha(SurfaceDark, SurfaceDark.a * alpha), rad);
            RoundLine(r, UITheme.WithAlpha(SurfaceDarkEdge, SurfaceDarkEdge.a * alpha), 1f, rad);
        }
        else
        {
            Round(r, UITheme.WithAlpha(SurfaceLight, SurfaceLight.a * alpha), rad);
            RoundLine(r, UITheme.WithAlpha(SurfaceLightEdge, SurfaceLightEdge.a * alpha), 1f, rad);
        }
    }

    /// <summary>HUD kartı: oyun dünyasının üstünde hafif, saydam koyu zemin (kenarsız).</summary>
    public static void HudCard(Rect r, float alpha = 1f)
    {
        Round(r, UITheme.WithAlpha(SurfaceHud, SurfaceHud.a * alpha), RadiusSmall);
    }

    /// <summary>Gölgeli yazı: oyun dünyası üstünde kart olmadan okunur (HUD).</summary>
    public static void ShadowText(Rect r, string text, TextRole role, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        if (string.IsNullOrEmpty(text)) return;
        GUIStyle s = UITheme.Style(role, new Color(0f, 0f, 0f, 0.65f * color.a), anchor);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, s);
        s = UITheme.Style(role, color, anchor);
        GUI.Label(r, text, s);
    }

    /// <summary>Eski süs (köşe bandı): artık çizilmez.</summary>
    public static void Tape(Rect r, float alpha = 1f) { }

    /// <summary>Eski süs (boya lekesi): artık çizilmez.</summary>
    public static void Splat(Rect r, Color tint) { }

    /// <summary>Yuva zemini (eski API). selected: açık kenar.</summary>
    public static void Slot(Rect r, float alpha = 1f) => Slot(r, SlotState.Filled, alpha);

    public static void Slot(Rect r, bool selected, float alpha = 1f) => Slot(r, selected ? SlotState.Selected : SlotState.Filled, alpha);

    /// <summary>İlerleme çubuğu (eski imza) → ProgressBar.</summary>
    public static void Bar(Rect r, float ratio, Color fill) => ProgressBar(r, ratio, fill);

    // ---------------- HUD köşeleri: aynı köşedeki kutular üst üste dizilir ----------------

    public enum HudCorner { TopLeft, TopRight, BottomLeft, BottomCenter, BottomRight }

    /// <summary>Köşe öncelikleri (küçük sayı köşeye en yakın).</summary>
    public static class HudOrder
    {
        // Sol üst
        public const int SessionCode = 0, Mission = 10, Lobby = 20;
        // Sağ üst
        public const int Ping = 0, Money = 10, Objective = 20;
        // Sol alt (aşağıdan yukarı)
        public const int GadgetHints = 0, TruckHint = 10, CamBattery = 15, Chat = 30;
        // Alt orta (aşağıdan yukarı)
        public const int Voice = 0, Hotbar = 10, Brush = 20, KeywordStatus = 30, Tutorial = 40;
        // Sağ alt (aşağıdan yukarı)
        public const int Weight = 0, BagHints = 10;
    }

    public const float HudMargin = 12f, HudGap = 8f;
    private const int CornerCount = 5;
    private static readonly Dictionary<int, float>[] hudCur = NewCorners();
    private static Dictionary<int, float>[] hudPrev = NewCorners();
    private static int hudFrame = -1;

    private static Dictionary<int, float>[] NewCorners()
    {
        var a = new Dictionary<int, float>[CornerCount];
        for (int i = 0; i < CornerCount; i++) a[i] = new Dictionary<int, float>();
        return a;
    }

    private static void RollHud()
    {
        if (hudFrame == Time.frameCount) return;
        hudFrame = Time.frameCount;
        for (int i = 0; i < CornerCount; i++)
        {
            hudPrev[i].Clear();
            foreach (var kv in hudCur[i]) hudPrev[i][kv.Key] = kv.Value;
            hudCur[i].Clear();
        }
    }

    /// <summary>
    /// Köşeye yerleşen HUD kutusunun yeri. Aynı köşede daha küçük öncelikli kutular (önceki karede çizilmişse)
    /// köşeye daha yakın durur, bu kutu onların üstüne / altına dizilir — çizim sırasından bağımsız, çakışma yok.
    /// Her çizimde (her karede) çağrılmalı; çağrılmayan kutu yer kaplamaz.
    /// </summary>
    public static Rect HudRect(HudCorner corner, int priority, float w, float h)
    {
        RollHud();
        int c = (int)corner;
        hudCur[c][priority] = h;
        float off = 0f;
        foreach (var kv in hudPrev[c]) if (kv.Key < priority) off += kv.Value + HudGap;

        float W = Screen.width, H = Screen.height;
        switch (corner)
        {
            case HudCorner.TopLeft: return new Rect(HudMargin, HudMargin + off, w, h);
            case HudCorner.TopRight: return new Rect(W - w - HudMargin, HudMargin + off, w, h);
            case HudCorner.BottomLeft: return new Rect(HudMargin, H - HudMargin - off - h, w, h);
            case HudCorner.BottomCenter: return new Rect((W - w) * 0.5f, H - HudMargin - off - h, w, h);
            default: return new Rect(W - w - HudMargin, H - HudMargin - off - h, w, h);
        }
    }

    // ---------------- Yazı yuvaları: aynı karede çizilen uyarılar üst üste binmesin ----------------
    private static int slotFrame = -1;
    private static EventType slotEvent;
    private static float topY, centerY;

    private static void ResetSlots()
    {
        EventType t = Event.current != null ? Event.current.type : EventType.Repaint;
        if (slotFrame == Time.frameCount && slotEvent == t) return;
        slotFrame = Time.frameCount;
        slotEvent = t;
        topY = 18f;
        centerY = Screen.height / 2f + 34f;
    }

    /// <summary>Ekranın üst ortasında sıradaki boş yer (şüphe çubuğu, alarm, hapis, görev uyarıları alt alta dizilir).</summary>
    public static float TopSlot(float height)
    {
        ResetSlots();
        float y = topY;
        topY += height + 6f;
        return y;
    }

    /// <summary>Nişangahın altında sıradaki boş yer (etkileşim yazısı, taşıma yazısı, bildirimler alt alta).</summary>
    public static float CenterSlot(float height = 30f)
    {
        ResetSlots();
        float y = centerY;
        centerY += height + 2f;
        return y;
    }

    /// <summary>
    /// Ekran ortasında etkileşim istemi (eski imza). Koyu plaket şerit üstünde yazı; metin "[E] ..." ile
    /// başlıyorsa tuş otomatik olarak tuş kapağına çevrilir. Yükseklik 30 px (yuva sistemiyle uyumlu).
    /// </summary>
    public static void DrawPrompt(string text, float y, Color color, int fontSize = 18)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            promptStyle.padding = new RectOffset(0, 0, 0, 0);
        }
        int fs = Mathf.Min(fontSize, 20);
        promptStyle.fontSize = fs;

        SplitKey(text, out string key, out string label);
        float keyW = key != null ? KeyCapWidth(key, fs) : 0f;
        float gap = key != null && label.Length > 0 ? 8f : 0f;
        float labelW = promptStyle.CalcSize(new GUIContent(label)).x;
        float h = 30f;
        float w = Mathf.Min(Screen.width - 20f, keyW + gap + labelW + 28f);
        Rect strip = new Rect((Screen.width - w) * 0.5f, y, w, h);

        if (IsRepaint) Panel(strip, 0.9f);
        float x = strip.x + 14f;
        if (key != null)
        {
            KeyCap(new Rect(x, strip.y + 4f, keyW, h - 8f), key, fs);
            x += keyW + gap;
        }
        promptStyle.normal.textColor = color;
        GUI.Label(new Rect(x, strip.y, strip.xMax - x - 10f, h), label, promptStyle);
    }

    /// <summary>"[E] Al" → key "E", label "Al". Köşeli parantez yoksa key null.</summary>
    public static void SplitKey(string text, out string key, out string label)
    {
        key = null; label = text ?? "";
        if (label.Length < 3 || label[0] != '[') return;
        int close = label.IndexOf(']');
        if (close <= 1 || close > 16) return;
        key = label.Substring(1, close - 1);
        label = label.Substring(close + 1).TrimStart(' ', ':', '-', '—');
    }

    /// <summary>Basılı tutma halkası: progress 0–1 (noktalar).</summary>
    public static void DrawRing(Vector2 center, float radius, float progress, Color fill)
    {
        const int segments = 48;
        int active = Mathf.RoundToInt(segments * Mathf.Clamp01(progress));

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * 360f - 90f;
            float rad = angle * Mathf.Deg2Rad;
            float x = center.x + Mathf.Cos(rad) * radius;
            float y = center.y + Mathf.Sin(rad) * radius;

            if (i < active) DrawRect(new Rect(x - 2.5f, y - 2.5f, 5f, 5f), fill);
            else DrawRect(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), new Color(0f, 0f, 0f, 0.45f));
        }
    }

    // =====================================================================================
    //  Bileşenler
    // =====================================================================================

    private static readonly Dictionary<Texture2D, GUIStyle> slicedStyles = new Dictionary<Texture2D, GUIStyle>();

    /// <summary>Dokuyu 9 dilim çizer (yalnızca Repaint). tint GUI.color ile çarpılır.</summary>
    public static void DrawSliced(Rect r, Texture2D tex, RectOffset border, Color tint)
    {
        if (!IsRepaint || tex == null) return;
        if (!slicedStyles.TryGetValue(tex, out GUIStyle st))
        {
            st = new GUIStyle();
            st.normal.background = tex;
            slicedStyles[tex] = st;
        }
        st.border = border;
        Color old = GUI.color;
        GUI.color = new Color(old.r * tint.r, old.g * tint.g, old.b * tint.b, old.a * tint.a);
        st.Draw(r, false, false, false, false);
        GUI.color = old;
    }

    /// <summary>Dokuyu dikdörtgen içine döşer.</summary>
    public static void Tile(Rect r, Texture2D tex, Color tint)
    {
        if (!IsRepaint || tex == null) return;
        Color old = GUI.color;
        GUI.color = new Color(old.r * tint.r, old.g * tint.g, old.b * tint.b, old.a * tint.a);
        GUI.DrawTextureWithTexCoords(r, tex, new Rect(0f, 0f, r.width / tex.width, r.height / tex.height));
        GUI.color = old;
    }

    /// <summary>Yumuşak gölge (diyalog, açık kartlar).</summary>
    public static void Shadow(Rect r, float alpha = 1f)
    {
        Round(new Rect(r.x - 2f, r.y + 3f, r.width + 4f, r.height + 6f), new Color(0f, 0f, 0f, 0.22f * alpha), RadiusBig + 2f);
    }

    /// <summary>Görüntü / yayın / plan çevresi: ince açık hat (eskiden pirinç çerçeve).</summary>
    public static void BrassFrame(Rect r, float alpha = 1f)
    {
        RoundLine(r, new Color(1f, 1f, 1f, 0.14f * alpha), 1f, 4f);
    }

    /// <summary>Eski süs (köşe süsleri): artık çizilmez.</summary>
    public static void Corners(Rect r, float alpha = 1f) { }

    /// <summary>Klavye odak halkası: r'nin 3 px dışında açık hat.</summary>
    public static void FocusRing(Rect r)
    {
        Rect o = new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f);
        RoundLine(o, new Color(1f, 1f, 1f, 0.55f), 1.5f, RadiusSmall + 2f);
    }

    /// <summary>Keskin dikdörtgen hat.</summary>
    public static void Outline(Rect r, float t, Color c)
    {
        DrawRect(new Rect(r.x, r.y, r.width, t), c);
        DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        DrawRect(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
        DrawRect(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
    }

    /// <summary>Bölüm ayracı: tam genişlikte çok ince, soluk hat (ornament yok sayılır).</summary>
    public static void Divider(float x, float y, float w, Color c, bool ornament = false)
    {
        DrawRect(new Rect(x, y, w, 1f), new Color(c.r, c.g, c.b, c.a * 0.5f));
    }

    /// <summary>Ekranı kaplayan perde.</summary>
    public static void Scrim(Color c) => DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), c);

    /// <summary>Kenar vinyeti (menü / duraklat: gerçek oyun sahnesinin üstüne).</summary>
    public static void Vignette(float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Backgrounds/bg_vignette");
        if (t == null || !IsRepaint) return;
        Color old = GUI.color; GUI.color = new Color(1f, 1f, 1f, old.a * alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), t, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    /// <summary>Soldan sağa koyu perde (ana menü sütunu). width: perdenin bittiği x.</summary>
    public static void ScrimLeft(float width, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Backgrounds/bg_scrim_left");
        if (!IsRepaint) return;
        if (t == null) { DrawRect(new Rect(0f, 0f, width * 0.6f, Screen.height), UITheme.WithAlpha(UITheme.Charcoal, 0.8f * alpha)); return; }
        Color old = GUI.color; GUI.color = new Color(1f, 1f, 1f, old.a * alpha);
        GUI.DrawTexture(new Rect(0f, 0f, width, Screen.height), t, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    /// <summary>Aşağıdan yukarı koyu perde.</summary>
    public static void ScrimBottom(float height, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Backgrounds/bg_scrim_bottom");
        if (t == null || !IsRepaint) return;
        Color old = GUI.color; GUI.color = new Color(1f, 1f, 1f, old.a * alpha);
        GUI.DrawTexture(new Rect(0f, Screen.height - height, Screen.width, height), t, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    /// <summary>Kilitli / devre dışı alan: düz soluk örtü (eskiden çapraz tarama).</summary>
    public static void Hatch(Rect r, Color c) => DrawRect(r, new Color(c.r, c.g, c.b, c.a * 0.35f));

    // ---------------- Yazı ----------------

    /// <summary>Rol stilinde yazı.</summary>
    public static void Text(Rect r, string text, TextRole role, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        GUI.Label(r, text, UITheme.Style(role, color, anchor));
    }

    /// <summary>Rolde tek satır yazının boyutu.</summary>
    public static Vector2 Measure(string text, TextRole role) => UITheme.Style(role).CalcSize(new GUIContent(text));

    /// <summary>Rolde, verilen genişlikte sarılmış yazının yüksekliği.</summary>
    public static float MeasureHeight(string text, TextRole role, float width)
    {
        GUIStyle s = UITheme.Style(role);
        bool wrap = s.wordWrap; s.wordWrap = true;
        float h = s.CalcHeight(new GUIContent(text), width);
        s.wordWrap = wrap;
        return h;
    }

    /// <summary>Bölüm başlığı: küçük kalın soluk yazı (alt çizgi yok). Kullandığı yüksekliği döner.</summary>
    public static float SectionHeader(Rect r, string text, PanelKind on = PanelKind.Plaque)
    {
        Color c = UITheme.SectionOn(on);
        Text(new Rect(r.x, r.y, r.width, 20f * UITheme.Scale), text, TextRole.Section, c);
        return 24f * UITheme.Scale;
    }

    // ---------------- Simge ----------------

    /// <summary>Simge (64x64 beyaz çizgi) verilen renkte. Doku yoksa küçük kare yer tutucu.</summary>
    public static void Icon(Rect r, IconId id, Color color)
    {
        if (!IsRepaint) return;
        Texture2D t = UITheme.IconTex(id);
        if (t == null) { RoundLine(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.2f, r.width * 0.6f, r.height * 0.6f), color, 2f, 3f); return; }
        Color old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
        GUI.color = old;
    }

    // ---------------- Düğme ----------------

    private static Color ButtonFill(ButtonKind kind, bool hover, bool pressed, bool enabled, bool selected)
    {
        if (!enabled)
            return kind == ButtonKind.Secondary ? new Color(0.62f, 0.6f, 0.56f, 0.45f) : new Color(0.25f, 0.24f, 0.22f, 0.55f);
        switch (kind)
        {
            case ButtonKind.Primary:
                return pressed ? UITheme.Hex(0x5E1721) : hover ? UITheme.Hex(0x93303D) : UITheme.Bordeaux;
            case ButtonKind.Secondary:
                return pressed ? new Color(0.8f, 0.77f, 0.7f, 1f) : hover ? new Color(0.97f, 0.95f, 0.9f, 1f) : new Color(0.88f, 0.85f, 0.78f, 1f);
            case ButtonKind.Danger:
                return pressed ? UITheme.Hex(0x8E2A22) : hover ? UITheme.Danger : new Color(0f, 0f, 0f, 0f);
            default: // Tool
                if (selected) return UITheme.Ivory;
                return pressed ? new Color(0.1f, 0.095f, 0.09f, 0.95f) : hover ? WellHover : Well;
        }
    }

    private static Color ButtonTextColor(ButtonKind kind, bool hover, bool enabled, bool selected)
    {
        if (!enabled) return kind == ButtonKind.Secondary ? UITheme.WithAlpha(UITheme.Charcoal, 0.5f) : UITheme.WithAlpha(UITheme.IvoryDim, 0.5f);
        switch (kind)
        {
            case ButtonKind.Secondary: return UITheme.Charcoal;
            case ButtonKind.Danger: return hover ? UITheme.Ivory : UITheme.DangerLight;
            case ButtonKind.Tool: return selected ? UITheme.Charcoal : hover ? Color.white : UITheme.Ivory;
            default: return hover ? Color.white : UITheme.Ivory;
        }
    }

    /// <summary>
    /// Düz düğme. Durumlar: normal / hover / pressed (yazı 1 px aşağı) / disabled / focus (ince halka; Enter ile tetiklenir)
    /// / selected (yalnızca Tool). icon verilirse yazının soluna çizilir (Tool türünde yazı boşsa yalnızca simge, ortada).
    /// </summary>
    public static bool Button(Rect r, string text, ButtonKind kind = ButtonKind.Primary, bool enabled = true,
                              bool focused = false, IconId? icon = null, bool selected = false)
    {
        Event e = Event.current;
        bool hover = enabled && GUI.enabled && e != null && r.Contains(e.mousePosition);
        bool pressed = hover && GUIUtility.hotControl != 0;

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && enabled;
        bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
        GUI.enabled = wasEnabled;

        if (IsRepaint)
        {
            float rad = Mathf.Min(RadiusSmall, r.height * 0.5f);
            Color fill = ButtonFill(kind, hover || focused, pressed, enabled, selected);
            Round(r, fill, rad);
            if (kind == ButtonKind.Danger && enabled && !(hover || focused || pressed)) RoundLine(r, UITheme.WithAlpha(UITheme.DangerLight, 0.75f), 1.5f, rad);
            if (kind == ButtonKind.Tool && !selected) RoundLine(r, new Color(1f, 1f, 1f, hover ? 0.18f : 0.08f), 1f, rad);
            if (focused && enabled) FocusRing(r);
        }

        // İçerik
        Color tc = ButtonTextColor(kind, hover || focused, enabled, selected);
        float dy = pressed ? 1f : 0f;
        bool hasText = !string.IsNullOrEmpty(text);
        float iconSize = Mathf.Round(Mathf.Min(r.height * 0.56f, 26f * UITheme.Scale));
        if (icon.HasValue && !hasText)
        {
            Icon(new Rect(r.center.x - iconSize * 0.5f, r.center.y - iconSize * 0.5f + dy, iconSize, iconSize), icon.Value, tc);
        }
        else if (hasText)
        {
            GUIStyle ts = UITheme.Style(TextRole.Button, tc, TextAnchor.MiddleCenter);
            int oldSize = ts.fontSize;
            ts.fontSize = Mathf.Min(oldSize, Mathf.RoundToInt(r.height * 0.5f));
            float tw = ts.CalcSize(new GUIContent(text)).x;
            float total = tw + (icon.HasValue ? iconSize + 8f : 0f);
            float x = r.center.x - Mathf.Min(total, r.width - 20f) * 0.5f;
            if (icon.HasValue)
            {
                Icon(new Rect(x, r.center.y - iconSize * 0.5f + dy, iconSize, iconSize), icon.Value, tc);
                x += iconSize + 8f;
                ts.alignment = TextAnchor.MiddleLeft;
                GUI.Label(new Rect(x, r.y + dy, r.xMax - x - 8f, r.height), text, ts);
            }
            else GUI.Label(new Rect(r.x + 8f, r.y + dy, r.width - 16f, r.height), text, ts);
            ts.fontSize = oldSize;
        }

        if (focused && enabled && e != null && e.type == EventType.KeyDown &&
            (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
        {
            e.Use();
            return true;
        }
        return clicked;
    }

    /// <summary>GUILayout içinde Button.</summary>
    public static bool LayoutButton(string text, ButtonKind kind, float height, bool enabled = true, bool focused = false,
                                    IconId? icon = null, params GUILayoutOption[] options)
    {
        var opts = new List<GUILayoutOption>(options ?? new GUILayoutOption[0]) { GUILayout.Height(height) };
        Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, opts.ToArray());
        return Button(r, text, kind, enabled, focused, icon);
    }

    /// <summary>
    /// Sekme / parçalı seçici: yan yana eşit düğmeler, seçili olan açık dolgulu. on: hangi yüzeyin üstünde.
    /// Tıklanan sekmenin indeksini döner (tıklanmadıysa selected).
    /// </summary>
    public static int Tabs(Rect r, string[] labels, int selected, PanelKind on = PanelKind.Plaque)
    {
        if (labels == null || labels.Length == 0) return selected;
        bool light = on != PanelKind.Plaque;
        Round(r, light ? new Color(0f, 0f, 0f, 0.08f) : new Color(1f, 1f, 1f, 0.06f), RadiusSmall + 2f);
        float pad = 3f, w = (r.width - pad * 2f) / labels.Length;
        int result = selected;
        Event e = Event.current;
        for (int i = 0; i < labels.Length; i++)
        {
            Rect t = new Rect(r.x + pad + i * w, r.y + pad, w, r.height - pad * 2f);
            bool sel = i == selected;
            bool hover = GUI.enabled && e != null && t.Contains(e.mousePosition);
            if (sel) Round(t, light ? UITheme.Charcoal : UITheme.Ivory, RadiusSmall);
            else if (hover) Round(t, light ? new Color(0f, 0f, 0f, 0.07f) : new Color(1f, 1f, 1f, 0.08f), RadiusSmall);
            Color tc = sel ? (light ? UITheme.Ivory : UITheme.Charcoal) : (light ? UITheme.Graphite : UITheme.IvoryDim);
            GUIStyle ts = UITheme.Style(TextRole.Button, tc, TextAnchor.MiddleCenter);
            int old = ts.fontSize;
            ts.fontSize = Mathf.Min(old, Mathf.RoundToInt(t.height * 0.48f));
            GUI.Label(t, labels[i], ts);
            ts.fontSize = old;
            if (GUI.Button(t, GUIContent.none, GUIStyle.none) && !sel) result = i;
        }
        return result;
    }

    /// <summary>Renk kutucuğu: yuvarlatılmış renk + seçiliyse dış halka. Tıklandıysa true.</summary>
    public static bool Swatch(Rect r, Color c, bool selected, bool enabled = true, bool onLight = false)
    {
        Round(r, enabled ? c : new Color(c.r * 0.35f + 0.1f, c.g * 0.35f + 0.1f, c.b * 0.35f + 0.1f, 1f), RadiusSmall);
        RoundLine(r, onLight ? new Color(0f, 0f, 0f, 0.25f) : new Color(1f, 1f, 1f, 0.18f), 1f, RadiusSmall);
        if (selected) RoundLine(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), onLight ? UITheme.Charcoal : UITheme.Ivory, 2f, RadiusSmall + 3f);
        return enabled && GUI.enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    // ---------------- Tuş kapağı + istem ----------------

    private static GUIStyle keyStyle;

    public static float KeyCapWidth(string key, int fontSize = 14)
    {
        if (keyStyle == null) MakeKeyStyle();
        keyStyle.fontSize = Mathf.Clamp(fontSize - 2, 10, 18);
        return Mathf.Max(keyStyle.CalcSize(new GUIContent(key)).x + 12f, 22f);
    }

    private static void MakeKeyStyle()
    {
        keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0) };
        if (UITheme.BodyFont != null) keyStyle.font = UITheme.BodyFont;
        keyStyle.normal.textColor = UITheme.Ivory;
    }

    /// <summary>Tuş kapağı: küçük koyu yuvarlak kutu + açık harf.</summary>
    public static void KeyCap(Rect r, string key, int fontSize = 14)
    {
        if (keyStyle == null) MakeKeyStyle();
        keyStyle.fontSize = Mathf.Clamp(fontSize - 2, 10, 18);
        Round(r, new Color(0.06f, 0.055f, 0.05f, 0.85f), 4f);
        RoundLine(r, new Color(1f, 1f, 1f, 0.28f), 1f, 4f);
        GUI.Label(r, key, keyStyle);
    }

    /// <summary>
    /// İstem: koyu kart şerit, solda tuş kapağı, sağda etiket; hold 0–1 ise şeridin altında dolum çizgisi.
    /// topCenter: şeridin üst-orta noktası. Çizilen dikdörtgeni döner.
    /// </summary>
    public static Rect Prompt(Vector2 topCenter, string key, string label, Color labelColor, float hold = -1f)
    {
        int fs = Mathf.RoundToInt(16f * UITheme.Scale);
        GUIStyle ls = UITheme.Style(TextRole.BodyStrong, labelColor, TextAnchor.MiddleLeft);
        float keyW = string.IsNullOrEmpty(key) ? 0f : KeyCapWidth(key, fs);
        float labelW = ls.CalcSize(new GUIContent(label)).x;
        float h = Mathf.Round(34f * UITheme.Scale);
        float w = Mathf.Min(Screen.width - 20f, keyW + (keyW > 0f ? 10f : 0f) + labelW + 32f);
        Rect r = new Rect(topCenter.x - w * 0.5f, topCenter.y, w, h);
        HudCard(r, 1.2f);
        float x = r.x + 16f;
        if (keyW > 0f) { KeyCap(new Rect(x, r.y + 6f, keyW, h - 12f), key, fs); x += keyW + 10f; }
        GUI.Label(new Rect(x, r.y, r.xMax - x - 10f, h), label, ls);
        if (hold >= 0f)
        {
            DrawRect(new Rect(r.x + 6f, r.yMax - 3f, (r.width - 12f), 2f), new Color(0f, 0f, 0f, 0.5f));
            DrawRect(new Rect(r.x + 6f, r.yMax - 3f, (r.width - 12f) * Mathf.Clamp01(hold), 2f), UITheme.Ivory);
        }
        return r;
    }

    // ---------------- İlerleme / yuva / rozet ----------------

    /// <summary>İlerleme çubuğu: yuvarlak oluk + dolgu. marker 0–1: hedef çizgisi. ticks: çeyrek çentikleri.</summary>
    public static void ProgressBar(Rect r, float ratio, Color fill, float marker = -1f, bool ticks = false)
    {
        if (!IsRepaint) return;
        float rad = r.height * 0.5f;
        Round(r, new Color(0f, 0f, 0f, 0.32f), rad);
        float w = r.width * Mathf.Clamp01(ratio);
        if (w > 0.5f) Round(new Rect(r.x, r.y, Mathf.Max(w, Mathf.Min(r.height, r.width)), r.height), fill, rad);
        if (ticks)
            for (int i = 1; i < 4; i++)
                DrawRect(new Rect(r.x + r.width * i / 4f, r.y + 1f, 1f, r.height - 2f), new Color(0f, 0f, 0f, 0.3f));
        if (marker >= 0f && marker <= 1f)
            DrawRect(new Rect(r.x + r.width * marker - 1f, r.y - 2f, 2f, r.height + 4f), UITheme.WithAlpha(UITheme.Ivory, 0.9f));
    }

    /// <summary>Durumlu yuva: Empty (soluk), Filled, Hover, Selected (açık kenar), Disabled (soluk).</summary>
    public static void Slot(Rect r, SlotState state, float alpha = 1f)
    {
        if (!IsRepaint) return;
        Color c = state == SlotState.Hover ? WellHover : Well;
        float a = state == SlotState.Disabled ? 0.45f : state == SlotState.Empty ? 0.7f : 1f;
        Round(r, UITheme.WithAlpha(c, c.a * a * alpha), RadiusSmall);
        if (state == SlotState.Selected) RoundLine(r, UITheme.WithAlpha(UITheme.Ivory, 0.9f * alpha), 2f, RadiusSmall);
        else RoundLine(r, new Color(1f, 1f, 1f, (state == SlotState.Hover ? 0.16f : 0.06f) * alpha), 1f, RadiusSmall);
    }

    /// <summary>Rozet: küçük yuvarlak etiket. pos: sol-üst. Çizilen dikdörtgeni döner.</summary>
    public static Rect Badge(Vector2 pos, string text, Color fill, Color textColor)
    {
        GUIStyle s = UITheme.Style(TextRole.Caption, textColor, TextAnchor.MiddleCenter);
        FontStyle oldFs = s.fontStyle; s.fontStyle = FontStyle.Bold;
        float h = Mathf.Round(20f * UITheme.Scale);
        float w = s.CalcSize(new GUIContent(text)).x + 16f;
        Rect r = new Rect(pos.x, pos.y, w, h);
        Round(r, fill, 4f);
        GUI.Label(r, text, s);
        s.fontStyle = oldFs;
        return r;
    }

    // ---------------- Diyalog ----------------

    /// <summary>
    /// Modal onay diyaloğu (koyu kart + perde). Enter = onay, Esc = vazgeç. Her karede çağrılır;
    /// seçim yapılana dek None döner. Çağıran, diyalog açıkken arkadaki kontrolleri çizmemeli (ya da GUI.enabled=false).
    /// </summary>
    public static DialogChoice Dialog(string title, string body, string confirm, string cancel, bool danger = false, float width = 460f)
    {
        float sc = UITheme.Scale;
        width = Mathf.Min(width * sc, Screen.width - 32f);
        float pad = UITheme.S5 * sc;
        float bodyH = string.IsNullOrEmpty(body) ? 0f : MeasureHeight(body, TextRole.Body, width - pad * 2f);
        float titleH = 34f * sc, btnH = 40f * sc;
        float h = pad + titleH + 8f + bodyH + pad + btnH + pad;
        Rect r = new Rect((Screen.width - width) * 0.5f, (Screen.height - h) * 0.5f, width, h);

        Scrim(UITheme.Scrim);
        Shadow(r);
        Panel(r, PanelKind.Plaque);
        Text(new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, titleH), title, TextRole.Title, UITheme.Ivory);
        if (bodyH > 0f)
        {
            GUIStyle bs = UITheme.Style(TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperLeft);
            bs.wordWrap = true;
            GUI.Label(new Rect(r.x + pad, r.y + pad + titleH + 8f, r.width - pad * 2f, bodyH), body, bs);
        }

        DialogChoice result = DialogChoice.None;
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { e.Use(); result = DialogChoice.Confirm; }
            else if (e.keyCode == KeyCode.Escape) { e.Use(); result = DialogChoice.Cancel; }
        }

        float bw = Mathf.Max(120f * sc, Measure(confirm, TextRole.Button).x + 40f);
        float cw = Mathf.Max(110f * sc, Measure(cancel ?? "", TextRole.Button).x + 40f);
        float by = r.yMax - pad - btnH;
        if (Button(new Rect(r.xMax - pad - bw, by, bw, btnH), confirm, danger ? ButtonKind.Danger : ButtonKind.Primary, true, true))
            result = DialogChoice.Confirm;
        if (!string.IsNullOrEmpty(cancel) && Button(new Rect(r.xMax - pad - bw - 10f - cw, by, cw, btnH), cancel, ButtonKind.Secondary))
            result = DialogChoice.Cancel;
        return result;
    }

    // ---------------- Bildirim (toast) ----------------

    private struct ToastItem { public string text; public ToastKind kind; public float start, life; public IconId? icon; }
    private static readonly List<ToastItem> toasts = new List<ToastItem>();
    private const int MaxToasts = 4;

    /// <summary>
    /// Bildirim kuyruğa ekler (her yerden çağrılabilir, OnGUI dışından da). Sağ kenarda, ekranın %28'inden aşağı dizilir;
    /// 0.22 sn kayarak gelir, 'seconds' kalır, 0.3 sn'de söner. Aynı metin zaten görünüyorsa süresi tazelenir.
    /// </summary>
    public static void ShowToast(string text, ToastKind kind = ToastKind.Info, float seconds = UITheme.ToastHold, IconId? icon = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        float now = Time.unscaledTime;
        for (int i = 0; i < toasts.Count; i++)
        {
            if (toasts[i].text != text) continue;
            ToastItem t = toasts[i]; t.start = Mathf.Min(t.start, now - 0.22f); t.life = (now - t.start) + seconds; toasts[i] = t;
            UITheme.Wake();
            return;
        }
        if (toasts.Count >= MaxToasts) toasts.RemoveAt(0);
        toasts.Add(new ToastItem { text = text, kind = kind, start = now, life = seconds + 0.22f, icon = icon });
        UITheme.Wake();
    }

    /// <summary>Kuyruktaki bildirimleri çizer (UITheme çağırır). Kuyruk boşsa false.</summary>
    public static bool DrawToasts()
    {
        if (toasts.Count == 0) return false;
        float now = Time.unscaledTime, sc = UITheme.Scale;
        float w = Mathf.Min(340f * sc, Screen.width - 32f), x0 = Screen.width - w - HudMargin, y = Screen.height * 0.28f;
        for (int i = toasts.Count - 1; i >= 0; i--)
            if (now - toasts[i].start > toasts[i].life + 0.3f) toasts.RemoveAt(i);

        foreach (ToastItem t in toasts)
        {
            float age = now - t.start;
            float inT = Ease.OutCubic(Mathf.Clamp01(age / 0.22f));
            float outT = 1f - Mathf.Clamp01((age - t.life) / 0.3f);
            float a = Mathf.Min(inT, outT);
            Color accent = UITheme.ToastColor(t.kind);
            GUIStyle s = UITheme.Style(TextRole.Body, UITheme.WithAlpha(UITheme.Ivory, a), TextAnchor.MiddleLeft);
            float textX = 14f + (t.icon.HasValue ? 28f * sc : 0f);
            float h = Mathf.Max(40f * sc, s.CalcHeight(new GUIContent(t.text), w - textX - 14f) + 18f);
            Rect r = new Rect(x0 + (1f - inT) * 24f, y, w, h);
            Panel(r, PanelKind.Plaque, a);
            if (t.icon.HasValue) Icon(new Rect(r.x + 12f, r.center.y - 10f * sc, 20f * sc, 20f * sc), t.icon.Value, UITheme.WithAlpha(accent, a));
            GUI.Label(new Rect(r.x + textX, r.y, r.width - textX - 14f, r.height), t.text, s);
            y += h + 6f;
        }
        return true;
    }

    // ---------------- Hareket ----------------

    /// <summary>Easing eğrileri (t: 0–1).</summary>
    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathf.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float InOutSine(float t) => -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(t)) - 1f) * 0.5f;
        /// <summary>Hafif taşma (yalnızca rozet / küçük "pop"). </summary>
        public static float OutBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.2f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
    }

    /// <summary>0–1 arası yumuşak nabız (Time.unscaledTime tabanlı). speed: rad/sn.</summary>
    public static float Pulse(float speed = 2f) => 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * speed);

    private struct TweenState { public float value; public int frame; }
    private static readonly Dictionary<int, TweenState> tweens = new Dictionary<int, TweenState>();

    /// <summary>
    /// Anahtar başına 0↔1 geçiş: on ise 1'e, değilse 0'a 'duration' sn'de doğrusal yaklaşır (karede bir kez ilerler).
    /// Dönen değeri Ease ile şekillendir. key: ekran + öğe için sabit bir sayı (ör. "menu.item".GetHashCode() + i).
    /// </summary>
    public static float Tween(int key, bool on, float duration = UITheme.MotionBase)
    {
        tweens.TryGetValue(key, out TweenState s);
        if (s.frame != Time.frameCount)
        {
            float step = duration <= 0f ? 1f : Time.unscaledDeltaTime / duration;
            s.value = Mathf.MoveTowards(s.value, on ? 1f : 0f, step);
            s.frame = Time.frameCount;
            tweens[key] = s;
        }
        return s.value;
    }

    // ---------------- Klavye gezintisi ----------------

    /// <summary>
    /// Dikey menü odağı: Yukarı/Aşağı (ve W/S) ile index'i döngüsel değiştirir, olayı tüketir. count &lt;= 0 ise dokunmaz.
    /// Yazı alanı klavye odağındayken çalışmaz.
    /// </summary>
    public static int NavigateVertical(int index, int count)
    {
        Event e = Event.current;
        if (count <= 0 || e == null || e.type != EventType.KeyDown) return index;
        if (GUIUtility.keyboardControl != 0) return index;
        if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W) { e.Use(); return (index - 1 + count) % count; }
        if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S) { e.Use(); return (index + 1) % count; }
        return index;
    }

    /// <summary>Sol/Sağ ok: -1 / +1 (kaydırıcı ayarı), yoksa 0. Olayı tüketir.</summary>
    public static int NavigateHorizontal()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return 0;
        if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.A) { e.Use(); return -1; }
        if (e.keyCode == KeyCode.RightArrow || e.keyCode == KeyCode.D) { e.Use(); return 1; }
        return 0;
    }

    /// <summary>Bu olayda Esc'e basıldı mı (tüketir).</summary>
    public static bool EscapePressed()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown || e.keyCode != KeyCode.Escape) return false;
        e.Use();
        return true;
    }
}
