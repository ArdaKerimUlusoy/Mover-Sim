using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OnGUI arayüzleri için ortak çizim yardımcıları — "Sahte Şaheser" tasarım sistemi (müze sergisi, katalog,
/// eskiz defteri, sergi etiketi). Renk / yazı / doku token'ları UITheme'de; kurallar UI_DESIGN.md'de.
/// Dokular Assets/Resources/UI/{Panels,Buttons,Frames,Icons,Backgrounds,Textures} altından gelir (9 dilim,
/// GUIStyle.border). Doku yoksa aynı renklerde yedek dokular üretilir; hiçbir ekran dokusuz kırılmaz.
/// ESKİ API (Panel, DrawRect, BoxStyle, TitleStyle, ..., Gold, Cream) imzasıyla korunur; görünümü yeni sisteme yönlenir.
/// HUD köşe yerleşimi (HudRect): aynı köşedeki kutular önceliğe göre üst üste DİZİLİR, asla çakışmaz.
/// </summary>
public static class UIUtil
{
    // ---------------- Eski tema renkleri (imza korunur, değerler yeni paletten) ----------------
    public static readonly Color Ink = UITheme.WithAlpha(UITheme.Charcoal, 0.93f);   // panel zemini
    public static readonly Color Frame = UITheme.WithAlpha(UITheme.Brass, 0.95f);     // pirinç çerçeve
    public static readonly Color Gold = UITheme.BrassLight;                           // başlık / vurgu
    public static readonly Color Cream = UITheme.Ivory;                               // ana yazı (koyu üstünde)
    public static readonly Color Muted = UITheme.IvoryDim;                            // ikincil yazı
    public static readonly Color Wine = UITheme.Bordeaux;                             // düğme
    public static readonly Color Mint = UITheme.Sage;                                 // olumlu
    public static readonly Color Coral = UITheme.DangerLight;                         // olumsuz / tehlike (koyu üstünde)

    private static GUIStyle promptStyle;
    private static bool texturesReady;
    private static Texture2D panelTex, panelSmallTex, btnTex, btnHoverTex, btnActiveTex, fieldTex, fieldFocusTex, slotTex, slotSelTex, tapeTex, splatTex;
    private static Texture2D iconBrush, iconLockpick, iconPaint;
    private static GUIStyle panelDrawStyle, panelSmallDrawStyle, slotDrawStyle, slotSelDrawStyle;
    private static RectOffset panelBorder, panelSmallBorder, buttonBorder, fieldBorder, slotBorder;

    /// <summary>Büyük panel dokusu ancak bu boydan büyük kutularda kullanılır (altında küçük plaket).</summary>
    private const float BigPanelMin = 32f;

    private static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

    /// <summary>Temanın panel dokusu (9 dilim; kenar payı PanelBorder).</summary>
    public static Texture2D PanelTexture { get { EnsureTextures(); return panelTex; } }
    public static RectOffset PanelBorder { get { EnsureTextures(); return new RectOffset(panelBorder.left, panelBorder.right, panelBorder.top, panelBorder.bottom); } }

    /// <summary>Hızlı envanter / alet rozetleri için simgeler. Yoksa null.</summary>
    public static Texture2D IconBrush { get { EnsureTextures(); return iconBrush; } }
    public static Texture2D IconLockpick { get { EnsureTextures(); return iconLockpick; } }
    public static Texture2D IconPaint { get { EnsureTextures(); return iconPaint; } }

    // ---- Panel stilleri (karaborsa, mağaza vb. ortak kullanır) ----
    private static GUIStyle box, title, row, small, button, hint, accent;

    /// <summary>Küçük / satır kutuları için kutu stili (koyu plaket). Büyük pencereler Panel() kullanır.</summary>
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

        // Eski stiller: boyutlar AYNI (yerleşimler kaymasın), yazı tipi / renk yeni sistemden
        title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        if (UITheme.DisplayFont != null) title.font = UITheme.DisplayFont;
        title.normal.textColor = Gold;

        row = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        row.normal.textColor = Cream;

        small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        small.normal.textColor = Muted;

        hint = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, wordWrap = false };

        accent = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        accent.normal.textColor = Gold;

        button = ThemeButton(new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold });
    }

    // ---------------- Tema dokuları ----------------

    private static Texture2D Res(string name)
    {
        Texture2D t = null;
        try { t = Resources.Load<Texture2D>("UI/" + name); } catch (System.Exception) { }
        return t;
    }

    private static RectOffset RO(int l, int r, int t, int b) => new RectOffset(l, r, t, b);

    private static void EnsureTextures()
    {
        if (texturesReady) return;
        texturesReady = true;

        // Yeni sistem (Resources/UI/<Klasör>) → eski kök dokular → aynı renklerde üretilen yedekler
        panelTex = UITheme.Tex("Panels/panel_plaque"); panelBorder = RO(12, 12, 12, 12);
        if (panelTex == null) { panelTex = MakeRounded(3, 1.5f, Ink, Frame); panelBorder = RO(6, 6, 6, 6); }

        panelSmallTex = UITheme.Tex("Panels/panel_plaque_small"); panelSmallBorder = RO(6, 6, 6, 6);
        if (panelSmallTex == null) { panelSmallTex = MakeRounded(2, 1f, Ink, Frame); panelSmallBorder = RO(4, 4, 4, 4); }

        btnTex = UITheme.Tex("Buttons/btn_primary_normal");
        btnHoverTex = UITheme.Tex("Buttons/btn_primary_hover");
        btnActiveTex = UITheme.Tex("Buttons/btn_primary_pressed");
        buttonBorder = RO(10, 10, 10, 10);
        if (btnTex == null || btnHoverTex == null || btnActiveTex == null)
        {
            btnTex = MakeRounded(2, 1f, Wine, new Color(0.16f, 0.04f, 0.06f, 1f));
            btnHoverTex = MakeRounded(2, 1f, UITheme.Hex(0x8E2633), Gold);
            btnActiveTex = MakeRounded(2, 1f, UITheme.Hex(0x5E1721), Frame);
            buttonBorder = RO(4, 4, 4, 4);
        }

        fieldTex = UITheme.Tex("Panels/field"); fieldFocusTex = UITheme.Tex("Panels/field_focus");
        fieldBorder = RO(8, 8, 8, 8);
        if (fieldTex == null) { fieldTex = MakeRounded(2, 1f, UITheme.WithAlpha(UITheme.CharDeep, 0.96f), UITheme.Graphite); fieldBorder = RO(4, 4, 4, 4); }
        if (fieldFocusTex == null) fieldFocusTex = fieldTex;

        slotTex = UITheme.Tex("Panels/slot"); slotSelTex = UITheme.Tex("Panels/slot_selected");
        slotBorder = RO(12, 12, 12, 12);
        if (slotTex == null || slotSelTex == null)
        {
            slotTex = MakeRounded(2, 1f, UITheme.WithAlpha(UITheme.CharSoft, 0.85f), UITheme.Graphite);
            slotSelTex = MakeRounded(2, 2f, UITheme.WithAlpha(UITheme.CharSoft, 0.92f), Gold);
            slotBorder = RO(5, 5, 5, 5);
        }

        tapeTex = UITheme.Tex("Frames/frame_corners");
        splatTex = Res("ui_splat");
        iconBrush = UITheme.IconTex(IconId.Brush) ?? Res("icon_brush");
        iconLockpick = UITheme.IconTex(IconId.Lockpick) ?? Res("icon_lockpick");
        iconPaint = Res("icon_paint") ?? UITheme.IconTex(IconId.Painting);

        panelDrawStyle = new GUIStyle { border = panelBorder };
        panelDrawStyle.normal.background = panelTex;
        panelSmallDrawStyle = new GUIStyle { border = panelSmallBorder };
        panelSmallDrawStyle.normal.background = panelSmallTex;
        slotDrawStyle = new GUIStyle { border = slotBorder };
        slotDrawStyle.normal.background = slotTex;
        slotSelDrawStyle = new GUIStyle { border = slotBorder };
        slotSelDrawStyle.normal.background = slotSelTex;
    }

    /// <summary>Az yuvarlatılmış, kenarı çizgili doku (yumuşatılmış). 9 dilim için orta 1 piksel uzar. (Yedek)</summary>
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

    /// <summary>Kutu stilini temaya çevirir (koyu plaket). big: büyük kenarlı doku.</summary>
    public static GUIStyle ThemeBox(GUIStyle s, bool big = false)
    {
        EnsureTextures();
        s.normal.background = big ? panelTex : panelSmallTex;
        RectOffset b = big ? panelBorder : panelSmallBorder;
        s.border = new RectOffset(b.left, b.right, b.top, b.bottom);
        s.normal.textColor = Cream;
        return s;
    }

    /// <summary>Düğme stilini temaya çevirir (birincil: bordo, üzerine gelince pirinç iç hat).</summary>
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
        s.onNormal.textColor = Gold;
        s.hover.textColor = Color.white;
        s.onHover.textColor = Color.white;
        s.active.textColor = Gold;
        s.onActive.textColor = Gold;
        return s;
    }

    /// <summary>Yazı kutusu stilini temaya çevirir (derin kömür, pirinç alt çizgi; odakta tam pirinç hat).</summary>
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
    /// Varsayılan GUI kutusu / düğmesi / yazı kutusu / kaydırıcı / kaydırma çubuğu / onay kutusunu temaya çevirir (bir kez).
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
        s.settings.selectionColor = UITheme.WithAlpha(UITheme.Brass, 0.55f);
        s.settings.cursorColor = UITheme.BrassLight;

        Texture2D track = UITheme.Tex("Buttons/slider_track");
        Texture2D thumb = UITheme.Tex("Buttons/slider_thumb");
        Texture2D thumbHi = UITheme.Tex("Buttons/slider_thumb_hover");
        if (track != null && thumb != null)
        {
            s.horizontalSlider.normal.background = track;
            s.horizontalSlider.border = RO(4, 4, 0, 0);
            foreach (GUIStyle th in new[] { s.horizontalSliderThumb })
            {
                th.normal.background = thumb;
                th.hover.background = thumbHi != null ? thumbHi : thumb;
                th.active.background = thumbHi != null ? thumbHi : thumb;
                th.focused.background = thumb;
                th.border = RO(0, 0, 0, 0);
            }
        }

        Texture2D sTrack = UITheme.Tex("Panels/prog_track");
        if (sTrack != null && thumb != null)
        {
            s.verticalScrollbar.normal.background = sTrack;
            s.verticalScrollbar.border = RO(4, 4, 4, 4);
            s.verticalScrollbarThumb.normal.background = thumb;
            s.verticalScrollbarThumb.hover.background = thumbHi != null ? thumbHi : thumb;
            s.verticalScrollbarThumb.active.background = thumbHi != null ? thumbHi : thumb;
            s.verticalScrollbarThumb.border = RO(2, 2, 3, 3);
            s.horizontalScrollbar.normal.background = sTrack;
            s.horizontalScrollbar.border = RO(4, 4, 4, 4);
            s.horizontalScrollbarThumb.normal.background = thumb;
            s.horizontalScrollbarThumb.hover.background = thumbHi != null ? thumbHi : thumb;
            s.horizontalScrollbarThumb.border = RO(3, 3, 2, 2);
        }

        Texture2D tOff = UITheme.Tex("Buttons/toggle_off");
        Texture2D tOn = UITheme.Tex("Buttons/toggle_on");
        if (tOff != null && tOn != null)
        {
            GUIStyle t = s.toggle;
            t.normal.background = tOff; t.hover.background = tOff; t.active.background = tOn; t.focused.background = tOff;
            t.onNormal.background = tOn; t.onHover.background = tOn; t.onActive.background = tOff; t.onFocused.background = tOn;
            t.border = RO(16, 0, 16, 0);
            t.padding = new RectOffset(Mathf.Max(t.padding.left, 22), t.padding.right, t.padding.top, t.padding.bottom);
            t.normal.textColor = Cream; t.hover.textColor = Color.white; t.onNormal.textColor = Gold; t.onHover.textColor = Gold;
        }
    }

    // ---------------- Çizim (eski API) ----------------

    /// <summary>Ekran ortasında panel kutusu çizer ve iç alanı döner.</summary>
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

    /// <summary>Temalı panel zemini (koyu plaket). accent verilirse solda ince renk şeridi.</summary>
    public static void Panel(Rect r, float alpha = 1f) => Panel(r, Color.clear, alpha);

    public static void Panel(Rect r, Color accent, float alpha = 1f)
    {
        if (!IsRepaint) return;
        EnsureTextures();
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        bool big = r.width >= BigPanelMin && r.height >= BigPanelMin;
        (big ? panelDrawStyle : panelSmallDrawStyle).Draw(r, false, false, false, false);
        GUI.color = old;
        if (accent.a > 0f)
        {
            float inset = Mathf.Min(big ? 8f : 4f, r.height * 0.25f);
            DrawRect(new Rect(r.x + (big ? 7f : 4f), r.y + inset, 3f, r.height - inset * 2f), new Color(accent.r, accent.g, accent.b, accent.a * alpha));
        }
    }

    /// <summary>Önemli paneller için süs: dört köşeye pirinç arşiv köşeleri (eskiden maskeleme bandı).</summary>
    public static void Tape(Rect r, float alpha = 1f)
    {
        if (!IsRepaint) return;
        EnsureTextures();
        if (tapeTex == null) return;
        DrawSliced(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), tapeTex, RO(10, 10, 10, 10), new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>Küçük boya lekesi süsü (yalnızca eskiz / tezgah ekranlarında).</summary>
    public static void Splat(Rect r, Color tint)
    {
        if (!IsRepaint) return;
        EnsureTextures();
        if (splatTex == null) { DrawRect(new Rect(r.x + r.width * 0.25f, r.y + r.height * 0.25f, r.width * 0.5f, r.height * 0.5f), tint); return; }
        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(r, splatTex);
        GUI.color = old;
    }

    /// <summary>Temalı yuva zemini (hızlı envanter vb.). selected: açık pirinç hat + arşiv köşeleri.</summary>
    public static void Slot(Rect r, float alpha = 1f) => Slot(r, false, alpha);

    public static void Slot(Rect r, bool selected, float alpha = 1f)
    {
        if (!IsRepaint) return;
        EnsureTextures();
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        (selected ? slotSelDrawStyle : slotDrawStyle).Draw(r, false, false, false, false);
        GUI.color = old;
    }

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

    /// <summary>Basılı tutma halkası: progress 0–1 (pirinç noktalar).</summary>
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
            else DrawRect(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), new Color(UITheme.CharDeep.r, UITheme.CharDeep.g, UITheme.CharDeep.b, 0.6f));
        }
    }

    // =====================================================================================
    //  TASARIM SİSTEMİ v2 — yeni ekranlar bunları kullanır (UI_DESIGN.md)
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

    /// <summary>Dokuyu dikdörtgen içine döşer (Textures/tex_* — eskiz ızgarası, kâğıt dokusu, tarama).</summary>
    public static void Tile(Rect r, Texture2D tex, Color tint)
    {
        if (!IsRepaint || tex == null) return;
        Color old = GUI.color;
        GUI.color = new Color(old.r * tint.r, old.g * tint.g, old.b * tint.b, old.a * tint.a);
        GUI.DrawTextureWithTexCoords(r, tex, new Rect(0f, 0f, r.width / tex.width, r.height / tex.height));
        GUI.color = old;
    }

    /// <summary>Türüne göre panel: Plaque (koyu, oyun içi), Catalog (fildişi belge), Sketch (eskiz kâğıdı + ızgara), Label (sergi etiketi).</summary>
    public static void Panel(Rect r, PanelKind kind, float alpha = 1f)
    {
        if (!IsRepaint) return;
        Texture2D tex = UITheme.PanelTex(kind, r, out RectOffset border);
        if (tex == null)
        {
            DrawRect(r, UITheme.WithAlpha(UITheme.PanelFallback(kind), UITheme.PanelFallback(kind).a * alpha));
            Outline(r, 1f, UITheme.WithAlpha(kind == PanelKind.Plaque ? UITheme.Brass : UITheme.Graphite, alpha));
        }
        else DrawSliced(r, tex, border, new Color(1f, 1f, 1f, alpha));

        if (kind == PanelKind.Sketch)
        {
            Rect inner = new Rect(r.x + 20f, r.y + 6f, r.width - 26f, r.height - 12f);
            Tile(inner, UITheme.Tex("Textures/tex_sketch_grid"), new Color(1f, 1f, 1f, alpha));
            Tile(new Rect(r.x + 1f, r.y + 14f, r.width - 2f, r.height - 15f), UITheme.Tex("Textures/tex_paper_grain"), new Color(1f, 1f, 1f, alpha));
        }
    }

    /// <summary>Hafif gölge (YALNIZCA Dialog ve ana menünün açık kartları — sergi etiketi, yapımcılar sayfası — altında).</summary>
    public static void Shadow(Rect r, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Panels/panel_shadow");
        if (t == null) return;
        DrawSliced(new Rect(r.x - 12f, r.y - 8f, r.width + 24f, r.height + 22f), t, RO(16, 16, 16, 16), new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>İnce pirinç çerçeve (görüntü / canlı yayın / kat planı). Çerçeve r'nin İÇİNE çizilir (4 px).</summary>
    public static void BrassFrame(Rect r, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Frames/frame_brass");
        if (t == null) { Outline(r, 2f, UITheme.WithAlpha(UITheme.Brass, alpha)); return; }
        DrawSliced(r, t, RO(6, 6, 6, 6), new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>Dört köşeye arşiv köşeleri (seçili eser, hedef kartı).</summary>
    public static void Corners(Rect r, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Frames/frame_corners");
        if (t == null) return;
        DrawSliced(r, t, RO(10, 10, 10, 10), new Color(1f, 1f, 1f, alpha));
    }

    /// <summary>Klavye odak halkası: r'nin 3 px dışında, hafif nabızlı açık pirinç hat.</summary>
    public static void FocusRing(Rect r)
    {
        Texture2D t = UITheme.Tex("Frames/frame_focus");
        float a = 0.75f + 0.25f * Pulse(2.2f);
        Rect o = new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f);
        if (t == null) { Outline(o, 2f, UITheme.WithAlpha(UITheme.BrassLight, a)); return; }
        DrawSliced(o, t, RO(5, 5, 5, 5), new Color(1f, 1f, 1f, a));
    }

    /// <summary>Keskin dikdörtgen hat.</summary>
    public static void Outline(Rect r, float t, Color c)
    {
        DrawRect(new Rect(r.x, r.y, r.width, t), c);
        DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        DrawRect(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
        DrawRect(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
    }

    /// <summary>Bölüm çizgisi: ince hat; ornament true ise ortada pirinç baklava (katalog başlıkları).</summary>
    public static void Divider(float x, float y, float w, Color c, bool ornament = false)
    {
        if (!ornament) { DrawRect(new Rect(x, y, w, 1f), c); return; }
        float mid = x + w * 0.5f;
        DrawRect(new Rect(x, y, w * 0.5f - 10f, 1f), c);
        DrawRect(new Rect(mid + 10f, y, w * 0.5f - 10f, 1f), c);
        Texture2D d = UITheme.Tex("Textures/ornament_diamond");
        if (d != null && IsRepaint) GUI.DrawTexture(new Rect(mid - 6f, y - 5.5f, 12f, 12f), d);
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

    /// <summary>Soldan sağa kömür perde (ana menü sütunu). width: perdenin bittiği x.</summary>
    public static void ScrimLeft(float width, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Backgrounds/bg_scrim_left");
        if (!IsRepaint) return;
        if (t == null) { DrawRect(new Rect(0f, 0f, width * 0.6f, Screen.height), UITheme.WithAlpha(UITheme.Charcoal, 0.8f * alpha)); return; }
        Color old = GUI.color; GUI.color = new Color(1f, 1f, 1f, old.a * alpha);
        GUI.DrawTexture(new Rect(0f, 0f, width, Screen.height), t, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    /// <summary>Aşağıdan yukarı kömür perde (görev sonu / alt şerit).</summary>
    public static void ScrimBottom(float height, float alpha = 1f)
    {
        Texture2D t = UITheme.Tex("Backgrounds/bg_scrim_bottom");
        if (t == null || !IsRepaint) return;
        Color old = GUI.color; GUI.color = new Color(1f, 1f, 1f, old.a * alpha);
        GUI.DrawTexture(new Rect(0f, Screen.height - height, Screen.width, height), t, ScaleMode.StretchToFill);
        GUI.color = old;
    }

    /// <summary>Çapraz tarama (kilitli / devre dışı / ıslak boya alanı).</summary>
    public static void Hatch(Rect r, Color c) => Tile(r, UITheme.Tex("Textures/tex_hatch"), c);

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

    /// <summary>Bölüm başlığı: BÜYÜK HARF küçük kalın yazı + altında ince çizgi. Kullandığı yüksekliği döner.</summary>
    public static float SectionHeader(Rect r, string text, PanelKind on = PanelKind.Plaque)
    {
        Color c = UITheme.SectionOn(on);
        Text(new Rect(r.x, r.y, r.width, 18f * UITheme.Scale), UITheme.Caps(text), TextRole.Section, c);
        DrawRect(new Rect(r.x, r.y + 20f * UITheme.Scale, r.width, 1f), UITheme.WithAlpha(c, 0.45f));
        return 26f * UITheme.Scale;
    }

    // ---------------- Simge ----------------

    /// <summary>Simge (64x64 beyaz çizgi) verilen renkte. Doku yoksa küçük kare yer tutucu.</summary>
    public static void Icon(Rect r, IconId id, Color color)
    {
        if (!IsRepaint) return;
        Texture2D t = UITheme.IconTex(id);
        if (t == null) { Outline(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.2f, r.width * 0.6f, r.height * 0.6f), 2f, color); return; }
        Color old = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
        GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
        GUI.color = old;
    }

    // ---------------- Düğme ----------------

    private static GUIStyle[] btnStyles;      // tür başına etkileşim stili (zemin dokuları)
    private static GUIStyle[] btnFocusStyles; // klavye odağında: hover dokusu kalıcı
    private static GUIStyle[] btnDisabledStyles;
    private static GUIStyle toolSelectedStyle;

    private static string KindName(ButtonKind k) =>
        k == ButtonKind.Primary ? "primary" : k == ButtonKind.Secondary ? "secondary" : k == ButtonKind.Danger ? "danger" : "tool";

    private static void EnsureButtonStyles()
    {
        if (btnStyles != null) return;
        EnsureTextures();
        int n = 4;
        btnStyles = new GUIStyle[n]; btnFocusStyles = new GUIStyle[n]; btnDisabledStyles = new GUIStyle[n];
        for (int i = 0; i < n; i++)
        {
            string k = KindName((ButtonKind)i);
            Texture2D nrm = UITheme.Tex("Buttons/btn_" + k + "_normal") ?? btnTex;
            Texture2D hov = UITheme.Tex("Buttons/btn_" + k + "_hover") ?? btnHoverTex;
            Texture2D prs = UITheme.Tex("Buttons/btn_" + k + "_pressed") ?? btnActiveTex;
            Texture2D dis = UITheme.Tex("Buttons/btn_" + k + "_disabled") ?? btnTex;
            RectOffset b = RO(10, 10, 10, 10);

            var s = new GUIStyle { border = b };
            s.normal.background = nrm; s.hover.background = hov; s.active.background = prs; s.focused.background = nrm;
            btnStyles[i] = s;

            var f = new GUIStyle { border = b };
            f.normal.background = hov; f.hover.background = hov; f.active.background = prs; f.focused.background = hov;
            btnFocusStyles[i] = f;

            var d = new GUIStyle { border = b };
            d.normal.background = dis; d.hover.background = dis; d.active.background = dis; d.focused.background = dis;
            btnDisabledStyles[i] = d;
        }
        toolSelectedStyle = new GUIStyle { border = RO(10, 10, 10, 10) };
        Texture2D sel = UITheme.Tex("Buttons/btn_tool_selected") ?? btnActiveTex;
        toolSelectedStyle.normal.background = sel; toolSelectedStyle.hover.background = sel; toolSelectedStyle.active.background = sel;
    }

    private static Color ButtonTextColor(ButtonKind kind, bool hover, bool enabled, bool selected)
    {
        if (!enabled) return kind == ButtonKind.Secondary ? UITheme.WithAlpha(UITheme.Charcoal, 0.45f) : UITheme.WithAlpha(UITheme.IvoryDim, 0.5f);
        switch (kind)
        {
            case ButtonKind.Secondary: return UITheme.Charcoal;
            case ButtonKind.Danger: return hover ? UITheme.Ivory : UITheme.DangerLight;
            case ButtonKind.Tool: return selected ? UITheme.Charcoal : hover ? UITheme.BrassLight : UITheme.Ivory;
            default: return hover ? Color.white : UITheme.Ivory;
        }
    }

    /// <summary>
    /// Tasarım sistemi düğmesi. Durumlar: normal / hover / pressed (yazı 1 px aşağı) / disabled / focus (pirinç halka +
    /// kalıcı hover; Enter ile tetiklenir) / selected (yalnızca Tool). icon verilirse yazının soluna çizilir
    /// (Tool türünde yazı boşsa yalnızca simge, ortada). Tıklandıysa true.
    /// </summary>
    public static bool Button(Rect r, string text, ButtonKind kind = ButtonKind.Primary, bool enabled = true,
                              bool focused = false, IconId? icon = null, bool selected = false)
    {
        EnsureButtonStyles();
        int ki = (int)kind;
        Event e = Event.current;
        bool hover = enabled && e != null && r.Contains(e.mousePosition);
        bool pressed = hover && GUIUtility.hotControl != 0;

        GUIStyle st = !enabled ? btnDisabledStyles[ki]
                    : (kind == ButtonKind.Tool && selected) ? toolSelectedStyle
                    : focused ? btnFocusStyles[ki] : btnStyles[ki];

        bool wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && enabled;
        bool clicked = GUI.Button(r, GUIContent.none, st);
        GUI.enabled = wasEnabled;

        if (focused && enabled && IsRepaint) FocusRing(r);

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

    // ---------------- Tuş kapağı + istem ----------------

    private static GUIStyle keyStyle;

    public static float KeyCapWidth(string key, int fontSize = 14)
    {
        if (keyStyle == null) MakeKeyStyle();
        keyStyle.fontSize = Mathf.Clamp(fontSize - 2, 10, 18);
        return Mathf.Max(keyStyle.CalcSize(new GUIContent(key)).x + 14f, 22f);
    }

    private static void MakeKeyStyle()
    {
        keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 2) };
        if (UITheme.MonoFont != null) keyStyle.font = UITheme.MonoFont;
        keyStyle.normal.textColor = UITheme.Charcoal;
    }

    /// <summary>Tuş kapağı (fildişi, alt dudak, kömür harf).</summary>
    public static void KeyCap(Rect r, string key, int fontSize = 14)
    {
        if (keyStyle == null) MakeKeyStyle();
        keyStyle.fontSize = Mathf.Clamp(fontSize - 2, 10, 18);
        Texture2D t = UITheme.Tex("Panels/keycap");
        if (t != null) DrawSliced(r, t, RO(7, 7, 6, 9), Color.white);
        else { DrawRect(r, UITheme.Ivory); Outline(r, 1f, UITheme.Charcoal); }
        GUI.Label(r, key, keyStyle);
    }

    /// <summary>
    /// Yeni istem: koyu plaket şerit, solda tuş kapağı, sağda etiket; hold 0–1 ise şeridin altında pirinç dolum çizgisi.
    /// center: şeridin üst-orta noktası. Çizilen dikdörtgeni döner.
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
        Panel(r, PanelKind.Plaque, 0.92f);
        float x = r.x + 16f;
        if (keyW > 0f) { KeyCap(new Rect(x, r.y + 5f, keyW, h - 10f), key, fs); x += keyW + 10f; }
        GUI.Label(new Rect(x, r.y, r.xMax - x - 10f, h), label, ls);
        if (hold >= 0f)
        {
            DrawRect(new Rect(r.x + 4f, r.yMax - 3f, (r.width - 8f), 2f), UITheme.WithAlpha(UITheme.CharDeep, 0.9f));
            DrawRect(new Rect(r.x + 4f, r.yMax - 3f, (r.width - 8f) * Mathf.Clamp01(hold), 2f), UITheme.BrassLight);
        }
        return r;
    }

    // ---------------- İlerleme / yuva / rozet ----------------

    /// <summary>İlerleme çubuğu: oluk + renklendirilmiş dolgu. marker 0–1: hedef çizgisi (ör. %75 eşiği). ticks: çeyrek çentikleri.</summary>
    public static void ProgressBar(Rect r, float ratio, Color fill, float marker = -1f, bool ticks = false)
    {
        if (!IsRepaint) return;
        Texture2D track = UITheme.Tex("Panels/prog_track");
        Texture2D f = UITheme.Tex("Panels/prog_fill");
        bool sliced = r.height >= 8f && track != null && f != null;
        if (sliced) DrawSliced(r, track, RO(4, 4, 4, 4), Color.white);
        else DrawRect(r, new Color(0f, 0f, 0f, 0.42f));

        float w = r.width * Mathf.Clamp01(ratio);
        if (w > 0.5f)
        {
            Rect fr = new Rect(r.x, r.y, w, r.height);
            if (sliced && w >= 8f) DrawSliced(new Rect(fr.x + 1f, fr.y + 1f, fr.width - 2f, fr.height - 2f), f, RO(4, 4, 3, 3), fill);
            else DrawRect(fr, fill);
        }
        if (ticks)
            for (int i = 1; i < 4; i++)
                DrawRect(new Rect(r.x + r.width * i / 4f, r.y + 1f, 1f, r.height - 2f), UITheme.WithAlpha(UITheme.Graphite, 0.9f));
        if (marker >= 0f && marker <= 1f)
            DrawRect(new Rect(r.x + r.width * marker - 1f, r.y - 2f, 2f, r.height + 4f), UITheme.BrassLight);
    }

    /// <summary>Durumlu yuva: Empty/Filled (kömür + arşiv köşeleri), Hover, Selected (açık pirinç), Disabled (soluk + tarama).</summary>
    public static void Slot(Rect r, SlotState state, float alpha = 1f)
    {
        if (!IsRepaint) return;
        string name = state == SlotState.Selected ? "slot_selected" : state == SlotState.Hover ? "slot_hover" : "slot";
        Texture2D t = UITheme.Tex("Panels/" + name);
        float a = state == SlotState.Disabled ? alpha * 0.45f : state == SlotState.Empty ? alpha * 0.8f : alpha;
        if (t != null) DrawSliced(r, t, RO(12, 12, 12, 12), new Color(1f, 1f, 1f, a));
        else Slot(r, state == SlotState.Selected, a);
        if (state == SlotState.Disabled) Hatch(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), UITheme.WithAlpha(UITheme.Graphite, 0.35f * alpha));
    }

    /// <summary>Rozet (envanter etiketi biçimi): sol tarafı sivri, delikli. pos: sol-üst. Çizilen dikdörtgeni döner.</summary>
    public static Rect Badge(Vector2 pos, string text, Color fill, Color textColor)
    {
        GUIStyle s = UITheme.Style(TextRole.Caption, textColor, TextAnchor.MiddleCenter);
        FontStyle oldFs = s.fontStyle; s.fontStyle = FontStyle.Bold;
        float h = Mathf.Round(20f * UITheme.Scale);
        float w = s.CalcSize(new GUIContent(text)).x + 20f;
        Rect r = new Rect(pos.x, pos.y, w, h);
        Texture2D t = UITheme.Tex("Panels/badge");
        if (t != null) DrawSliced(r, t, RO(8, 4, 4, 4), fill);
        else DrawRect(r, fill);
        GUI.Label(new Rect(r.x + 8f, r.y, r.width - 10f, r.height), text, s);
        s.fontStyle = oldFs;
        return r;
    }

    // ---------------- Diyalog ----------------

    /// <summary>
    /// Modal onay diyaloğu (katalog sayfası + gölge + perde). Enter = onay, Esc = vazgeç. Her karede çağrılır;
    /// seçim yapılana dek None döner. Çağıran, diyalog açıkken arkadaki kontrolleri çizmemeli (ya da GUI.enabled=false).
    /// </summary>
    public static DialogChoice Dialog(string title, string body, string confirm, string cancel, bool danger = false, float width = 460f)
    {
        float sc = UITheme.Scale;
        width = Mathf.Min(width * sc, Screen.width - 32f);
        float pad = UITheme.S5 * sc;
        float bodyH = string.IsNullOrEmpty(body) ? 0f : MeasureHeight(body, TextRole.Body, width - pad * 2f);
        float titleH = 38f * sc, btnH = 40f * sc;
        float h = pad + titleH + 14f + bodyH + pad + btnH + pad;
        Rect r = new Rect((Screen.width - width) * 0.5f, (Screen.height - h) * 0.5f, width, h);

        Scrim(UITheme.Scrim);
        Shadow(r);
        Panel(r, PanelKind.Catalog);
        Text(new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, titleH), title, TextRole.Title, UITheme.Charcoal);
        DrawRect(new Rect(r.x + pad, r.y + pad + titleH + 4f, 48f * sc, 2f), danger ? UITheme.Danger : UITheme.Bordeaux);
        if (bodyH > 0f)
        {
            GUIStyle bs = UITheme.Style(TextRole.Body, UITheme.Graphite, TextAnchor.UpperLeft);
            bs.wordWrap = true;
            GUI.Label(new Rect(r.x + pad, r.y + pad + titleH + 14f, r.width - pad * 2f, bodyH), body, bs);
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
            float textX = 16f + (t.icon.HasValue ? 30f * sc : 0f);
            float h = Mathf.Max(44f * sc, s.CalcHeight(new GUIContent(t.text), w - textX - 14f) + 20f);
            Rect r = new Rect(x0 + (1f - inT) * 24f, y, w, h);
            Panel(r, PanelKind.Plaque, a);
            DrawRect(new Rect(r.x + 5f, r.y + 6f, 3f, r.height - 12f), UITheme.WithAlpha(accent, a));
            if (t.icon.HasValue) Icon(new Rect(r.x + 14f, r.center.y - 11f * sc, 22f * sc, 22f * sc), t.icon.Value, UITheme.WithAlpha(accent, a));
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
