using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OnGUI arayüzleri için ortak çizim yardımcıları + oyunun arayüz TEMASI ("ressam-hırsız").
/// Görseller Assets/Resources/UI altındaki dokulardan gelir (9 dilim): koyu tuval zeminli, el boyaması altın
/// çerçeveli ve yaldızlı köşeli paneller, bordo fırça darbesi düğmeler, palet çukuru yuvalar, ipucu yazılarının
/// arkasında koyu fırça darbesi, maskeleme bandı süsü. Dokular yoksa aynı renklerde yedek dokular üretilir.
/// HUD köşe yerleşimi (HudRect): aynı köşedeki kutular önceliğe göre üst üste DİZİLİR, asla çakışmaz.
/// </summary>
public static class UIUtil
{
    // ---------------- Tema renkleri ----------------
    public static readonly Color Ink = new Color(0.13f, 0.095f, 0.115f, 0.93f);       // tuval zemini (koyu mürdüm)
    public static readonly Color Frame = new Color(0.86f, 0.66f, 0.38f, 0.95f);       // altın çerçeve
    public static readonly Color Gold = new Color(1f, 0.82f, 0.45f);                  // başlık / vurgu
    public static readonly Color Cream = new Color(1f, 0.95f, 0.86f);                 // ana yazı
    public static readonly Color Muted = new Color(0.86f, 0.79f, 0.72f);              // ikincil yazı
    public static readonly Color Wine = new Color(0.52f, 0.16f, 0.19f, 0.97f);        // düğme
    public static readonly Color Mint = new Color(0.56f, 0.92f, 0.62f);               // olumlu
    public static readonly Color Coral = new Color(1f, 0.47f, 0.42f);                 // olumsuz / tehlike

    private static GUIStyle promptStyle, promptShadow;
    private static bool texturesReady;
    private static Texture2D panelTex, panelSmallTex, btnTex, btnHoverTex, btnActiveTex, fieldTex, slotTex, slotSelTex, strokeTex, tapeTex, splatTex;
    private static Texture2D iconBrush, iconLockpick, iconPaint;
    private static GUIStyle panelDrawStyle, panelSmallDrawStyle, slotDrawStyle, slotSelDrawStyle, strokeDrawStyle;
    private static RectOffset panelBorder, panelSmallBorder, buttonBorder, fieldBorder, slotBorder;

    /// <summary>Büyük panelin altın köşe süsleri ancak bu boydan büyük kutularda kullanılır (altında sade küçük panel).</summary>
    private const float BigPanelMin = 64f;

    /// <summary>Temanın panel dokusu (9 dilim; kenar payı PanelBorder).</summary>
    public static Texture2D PanelTexture { get { EnsureTextures(); return panelTex; } }
    public static RectOffset PanelBorder { get { EnsureTextures(); return new RectOffset(panelBorder.left, panelBorder.right, panelBorder.top, panelBorder.bottom); } }

    /// <summary>Hızlı envanter / alet rozetleri için simgeler (Resources/UI). Yoksa null.</summary>
    public static Texture2D IconBrush { get { EnsureTextures(); return iconBrush; } }
    public static Texture2D IconLockpick { get { EnsureTextures(); return iconLockpick; } }
    public static Texture2D IconPaint { get { EnsureTextures(); return iconPaint; } }

    // ---- Panel stilleri (karaborsa, mağaza vb. ortak kullanır) ----
    private static GUIStyle box, title, row, small, button, hint, accent;

    /// <summary>Küçük / satır kutuları için güvenli (sade) kutu stili. Büyük pencereler Panel() / BigBoxStyle kullanır.</summary>
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

        title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
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

        // Varlıklar (Resources/UI); yoksa aynı renklerde üretilen yedekler
        panelTex = Res("ui_panel");
        panelBorder = RO(28, 28, 28, 28);
        if (panelTex == null) { panelTex = MakeRounded(10, 2f, Ink, Frame); panelBorder = RO(12, 12, 12, 12); }

        panelSmallTex = Res("ui_panel_small");
        panelSmallBorder = RO(12, 12, 12, 12);
        if (panelSmallTex == null) { panelSmallTex = MakeRounded(6, 1.5f, Ink, Frame); panelSmallBorder = RO(8, 8, 8, 8); }

        btnTex = Res("ui_button"); btnHoverTex = Res("ui_button_hover"); btnActiveTex = Res("ui_button_active");
        buttonBorder = RO(22, 22, 14, 14);
        if (btnTex == null || btnHoverTex == null || btnActiveTex == null)
        {
            btnTex = MakeRounded(8, 1.5f, Wine, new Color(0.93f, 0.72f, 0.42f, 0.9f));
            btnHoverTex = MakeRounded(8, 1.5f, new Color(0.66f, 0.22f, 0.25f, 0.98f), Gold);
            btnActiveTex = MakeRounded(8, 1.5f, new Color(0.38f, 0.11f, 0.14f, 0.98f), Gold);
            buttonBorder = RO(10, 10, 10, 10);
        }

        fieldTex = Res("ui_field");
        fieldBorder = RO(12, 12, 12, 12);
        if (fieldTex == null) { fieldTex = MakeRounded(6, 1.5f, new Color(0.06f, 0.045f, 0.055f, 0.96f), Frame); fieldBorder = RO(8, 8, 8, 8); }

        slotTex = Res("ui_slot"); slotSelTex = Res("ui_slot_sel");
        slotBorder = RO(18, 18, 18, 18);
        if (slotTex == null || slotSelTex == null)
        {
            slotTex = MakeRounded(8, 1.5f, new Color(0.08f, 0.06f, 0.07f, 0.82f), new Color(0.86f, 0.66f, 0.38f, 0.55f));
            slotSelTex = MakeRounded(8, 3f, new Color(0.08f, 0.06f, 0.07f, 0.88f), Gold);
            slotBorder = RO(10, 10, 10, 10);
        }

        strokeTex = Res("ui_stroke");
        tapeTex = Res("ui_tape");
        splatTex = Res("ui_splat");
        iconBrush = Res("icon_brush");
        iconLockpick = Res("icon_lockpick");
        iconPaint = Res("icon_paint");

        panelDrawStyle = new GUIStyle { border = panelBorder };
        panelDrawStyle.normal.background = panelTex;
        panelSmallDrawStyle = new GUIStyle { border = panelSmallBorder };
        panelSmallDrawStyle.normal.background = panelSmallTex;
        slotDrawStyle = new GUIStyle { border = slotBorder };
        slotDrawStyle.normal.background = slotTex;
        slotSelDrawStyle = new GUIStyle { border = slotBorder };
        slotSelDrawStyle.normal.background = slotSelTex;
        if (strokeTex != null)
        {
            strokeDrawStyle = new GUIStyle { border = RO(40, 40, 0, 0) };
            strokeDrawStyle.normal.background = strokeTex;
        }
    }

    /// <summary>Yuvarlak köşeli, kenarı çizgili doku (yumuşatılmış). 9 dilim için orta 1 piksel uzar. (Yedek)</summary>
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

    /// <summary>Kutu stilini temaya çevirir. big: yaldız köşeli büyük panel (yalnızca 64 px'ten büyük pencereler için).</summary>
    public static GUIStyle ThemeBox(GUIStyle s, bool big = false)
    {
        EnsureTextures();
        s.normal.background = big ? panelTex : panelSmallTex;
        RectOffset b = big ? panelBorder : panelSmallBorder;
        s.border = new RectOffset(b.left, b.right, b.top, b.bottom);
        s.normal.textColor = Cream;
        return s;
    }

    /// <summary>Düğme stilini temaya çevirir (bordo fırça darbesi, altın kenar, üzerine gelince açılır).</summary>
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
        s.padding = new RectOffset(Mathf.Max(s.padding.left, 14), Mathf.Max(s.padding.right, 14), s.padding.top, s.padding.bottom);
        s.normal.textColor = Cream;
        s.focused.textColor = Cream;
        s.onNormal.textColor = Gold;
        s.hover.textColor = Color.white;
        s.onHover.textColor = Color.white;
        s.active.textColor = Gold;
        s.onActive.textColor = Gold;
        return s;
    }

    /// <summary>Yazı kutusu stilini temaya çevirir.</summary>
    public static GUIStyle ThemeField(GUIStyle s)
    {
        EnsureTextures();
        s.normal.background = fieldTex;
        s.hover.background = fieldTex;
        s.focused.background = fieldTex;
        s.active.background = fieldTex;
        s.border = new RectOffset(fieldBorder.left, fieldBorder.right, fieldBorder.top, fieldBorder.bottom);
        s.normal.textColor = Cream;
        s.hover.textColor = Cream;
        s.focused.textColor = Color.white;
        s.active.textColor = Color.white;
        return s;
    }

    private static GUISkin skinApplied;

    /// <summary>
    /// Varsayılan GUI kutusu / düğmesi / yazı kutusunu temaya çevirir (bir kez). Stiller GUI.skin'den
    /// kopyalanmadan önce çağrılmalı; UITheme bunu her şeyden önce yapar, stil kuran betikler de ayrıca çağırır.
    /// Sadece OnGUI içinde çağrılabilir.
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
    }

    // ---------------- Çizim ----------------

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

    /// <summary>Temalı panel zemini (tuval + altın çerçeve; büyükse yaldızlı köşeler). accent verilirse solda ince renk şeridi.</summary>
    public static void Panel(Rect r, float alpha = 1f) => Panel(r, Color.clear, alpha);

    public static void Panel(Rect r, Color accent, float alpha = 1f)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        EnsureTextures();
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        bool big = r.width >= BigPanelMin && r.height >= BigPanelMin;
        (big ? panelDrawStyle : panelSmallDrawStyle).Draw(r, false, false, false, false);
        GUI.color = old;
        if (accent.a > 0f)
        {
            float inset = Mathf.Min(big ? 14f : 6f, r.height * 0.25f);
            DrawRect(new Rect(r.x + (big ? 9f : 5f), r.y + inset, 3f, r.height - inset * 2f), new Color(accent.r, accent.g, accent.b, accent.a * alpha));
        }
    }

    /// <summary>Panelin sağ üst köşesine maskeleme bandı (tuvali duvara yapıştırmış gibi) — önemli paneller için süs.</summary>
    public static void Tape(Rect r, float alpha = 1f)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        EnsureTextures();
        if (tapeTex == null) return;
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        GUI.DrawTexture(new Rect(r.xMax - tapeTex.width * 0.8f, r.y - tapeTex.height * 0.45f, tapeTex.width, tapeTex.height), tapeTex);
        GUI.color = old;
    }

    /// <summary>Küçük boya lekesi süsü (başlık yanında).</summary>
    public static void Splat(Rect r, Color tint)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        EnsureTextures();
        if (splatTex == null) { DrawRect(new Rect(r.x + r.width * 0.25f, r.y + r.height * 0.25f, r.width * 0.5f, r.height * 0.5f), tint); return; }
        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(r, splatTex);
        GUI.color = old;
    }

    /// <summary>Temalı yuva zemini (hızlı envanter vb.). selected: altın boya halkası.</summary>
    public static void Slot(Rect r, float alpha = 1f) => Slot(r, false, alpha);

    public static void Slot(Rect r, bool selected, float alpha = 1f)
    {
        if (Event.current == null || Event.current.type != EventType.Repaint) return;
        EnsureTextures();
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        (selected ? slotSelDrawStyle : slotDrawStyle).Draw(r, false, false, false, false);
        GUI.color = old;
    }

    /// <summary>İlerleme çubuğu (koyu oluk + dolgu + üstte ince parlaklık).</summary>
    public static void Bar(Rect r, float ratio, Color fill)
    {
        DrawRect(r, new Color(0f, 0f, 0f, 0.38f));
        float w = r.width * Mathf.Clamp01(ratio);
        if (w <= 0.5f) return;
        DrawRect(new Rect(r.x, r.y, w, r.height), fill);
        DrawRect(new Rect(r.x, r.y, w, 1f), new Color(1f, 1f, 1f, 0.25f));
    }

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

    /// <summary>Ekran ortasında gölgeli yazı (etkileşim yazıları için).</summary>
    public static void DrawPrompt(string text, float y, Color color, int fontSize = 18)
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            promptShadow = new GUIStyle(promptStyle);
            promptShadow.normal.textColor = new Color(0.08f, 0.04f, 0.05f, 0.9f);
        }

        promptStyle.fontSize = fontSize;
        promptShadow.fontSize = fontSize;

        Rect r = new Rect(0f, y, Screen.width, 30f);
        // Okunurluk + tema: yazının arkasında koyu, yarı saydam fırça darbesi
        if (Event.current != null && Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(text))
        {
            EnsureTextures();
            if (strokeDrawStyle != null)
            {
                float tw = Mathf.Min(Screen.width - 20f, promptStyle.CalcSize(new GUIContent(text)).x + 76f);
                float th = fontSize + 18f;
                Color old = GUI.color;
                GUI.color = new Color(old.r, old.g, old.b, old.a * 0.82f);
                strokeDrawStyle.Draw(new Rect((Screen.width - tw) * 0.5f, y + 15f - th * 0.5f, tw, th), false, false, false, false);
                GUI.color = old;
            }
        }
        GUI.Label(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), text, promptShadow);
        promptStyle.normal.textColor = color;
        GUI.Label(r, text, promptStyle);
    }

    /// <summary>Basılı tutma halkası: progress 0–1.</summary>
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
            else DrawRect(new Rect(x - 1.5f, y - 1.5f, 3f, 3f), new Color(0.3f, 0.3f, 0.3f, 0.45f));
        }
    }
}
