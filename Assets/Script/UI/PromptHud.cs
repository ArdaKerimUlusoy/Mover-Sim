using UnityEngine;

/// <summary>Etkileşim isteminin tonu (etiket rengini belirler).</summary>
public enum PromptTone { Normal, Dim, Warn, Danger }

/// <summary>
/// Ekran ortası bağlama duyarlı etkileşim istemi (UIUtil.Prompt üstüne ince katman).
///  - "[E] Al", "[Hold E] ...", "[E Basılı Tut] ..." metninden gerçek tuşu çıkarır, tuş kapağına çevirir.
///  - Durumlar: mevcut (Normal), kullanılamaz (Dim / Danger), aktif (hold 0–1 dolum), tamamlandı (hold >= 1 → Sage).
///  - Hedef değişince / kaybolunca yumuşak giriş-çıkış (UIUtil.Tween); görüşü az kapatır (tek şerit, CenterSlot).
/// Her kaynak kendi kanalını kullanır (0: etkileşim, 1: taşıma, 2: maymuncuk, 3: garaj kapısı, 4: fırça, 5: sprey), böylece
/// birbirinin geçişini bozmaz (aynı kanalı paylaşan biri her karede Idle çağırınca istem yanıp sönüyordu).
/// Yalnızca sunum; oyun mantığı çağıranda kalır.
/// </summary>
public static class PromptHud
{
    private const int TweenBase = 0x50726D00;   // "Prm"
    private const int Channels = 6; // 0 etkileşim, 1 taşıma, 2 maymuncuk, 3 garaj kapısı, 4 fırça (duvar), 5 sprey

    private static readonly bool[] has = new bool[Channels];
    private static readonly string[] lastKey = new string[Channels], lastLabel = new string[Channels];
    private static readonly PromptTone[] lastTone = new PromptTone[Channels];
    private static readonly float[] lastHold = new float[Channels];
    private static readonly bool[] lastHoldable = new bool[Channels];

    /// <summary>Metni çıkar: tuşu ve etiketi ayır; "Hold E" / "E Basılı Tut" gibi ifadelerden tek harfli tuşu bul.</summary>
    public static void Parse(string text, out string key, out string label, out bool holdable)
    {
        holdable = false;
        UIUtil.SplitKey(text, out key, out label);
        if (key == null) return;
        string[] tokens = key.Split(' ');
        if (tokens.Length > 1)
        {
            string letter = null;
            foreach (string t in tokens) if (t.Length == 1) { letter = t; break; }
            string low = key.ToLowerInvariant();
            holdable = low.Contains("hold") || low.Contains("basılı") || low.Contains("basili");
            if (letter != null) key = letter;
            else if (key.Length > 8) { label = key + " " + label; key = null; }
        }
    }

    /// <summary>Bu karede gösterilecek istemi bildirir ve çizer. hold: -1 = tutma yok, 0–1 = ilerleme.</summary>
    public static void Show(string text, PromptTone tone = PromptTone.Normal, float hold = -1f, int channel = 0)
    {
        if (string.IsNullOrEmpty(text)) { Idle(channel); return; }
        Parse(text, out string key, out string label, out bool holdable);
        Show(key, label, tone, hold, holdable, channel);
    }

    public static void Show(string key, string label, PromptTone tone, float hold, bool holdable, int channel = 0)
    {
        int c = Mathf.Clamp(channel, 0, Channels - 1);
        has[c] = true;
        lastKey[c] = key; lastLabel[c] = label; lastTone[c] = tone; lastHold[c] = hold; lastHoldable[c] = holdable;
        Draw(c, UIUtil.Tween(TweenBase + c, true));
    }

    /// <summary>İstem yoksa çağır: son istem yumuşakça sönerek çizilir.</summary>
    public static void Idle(int channel = 0)
    {
        int c = Mathf.Clamp(channel, 0, Channels - 1);
        if (!has[c]) return;
        float t = UIUtil.Tween(TweenBase + c, false);
        if (t <= 0.001f) { has[c] = false; return; }
        Draw(c, t);
    }

    private static Color ToneColor(PromptTone tone, bool done)
    {
        if (done) return UITheme.Sage;
        switch (tone)
        {
            case PromptTone.Dim: return UITheme.WithAlpha(UITheme.IvoryDim, 0.8f);
            case PromptTone.Warn: return UITheme.BrassLight;
            case PromptTone.Danger: return UITheme.DangerLight;
            default: return UITheme.Ivory;
        }
    }

    private static readonly int[] drawFrame = { -1, -1, -1, -1, -1, -1 };
    private static readonly EventType[] drawEvent = new EventType[Channels];

    private static void Draw(int c, float t)
    {
        EventType ev = Event.current != null ? Event.current.type : EventType.Repaint;
        if (drawFrame[c] == Time.frameCount && drawEvent[c] == ev) return;   // aynı karede bir kez (birden çok örnek çağırabilir)
        drawFrame[c] = Time.frameCount; drawEvent[c] = ev;
        float e = UIUtil.Ease.OutCubic(t);
        float h = Mathf.Round(34f * UITheme.Scale);
        float y = UIUtil.CenterSlot(h) + (1f - e) * 10f * UITheme.Scale;
        float hold = lastHold[c] >= 0f ? lastHold[c] : (lastHoldable[c] ? 0f : -1f);
        bool done = lastHold[c] >= 1f;

        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * e);
        UIUtil.Prompt(new Vector2(Screen.width * 0.5f, y), lastKey[c], lastLabel[c], ToneColor(lastTone[c], done), hold);
        GUI.color = old;
    }

    // ---------------- İpucu satırı: "[E] Bırak    [R] Döndür" → tuş kapakları ----------------

    private struct HintUnit { public string key, text; public float w; }
    private static readonly System.Collections.Generic.List<HintUnit> hintUnits = new System.Collections.Generic.List<HintUnit>();
    private static readonly System.Collections.Generic.List<int> hintLineEnds = new System.Collections.Generic.List<int>();
    private static readonly System.Collections.Generic.List<float> hintLineW = new System.Collections.Generic.List<float>();

    private const float HintGap = 18f;

    private static int HintFont => Mathf.RoundToInt(15f * UITheme.Scale);
    private static float HintRowH => Mathf.Round(26f * UITheme.Scale);

    /// <summary>"[Tuş] etiket" dizisini birimlere ayırır (uzun köşeli ifadeler tuş değil düz metin sayılır) ve satırlara sarar.</summary>
    private static void LayoutHint(string text, float maxW, Color color)
    {
        hintUnits.Clear(); hintLineEnds.Clear(); hintLineW.Clear();
        if (string.IsNullOrEmpty(text)) return;
        GUIStyle st = UITheme.Style(TextRole.Body, color, TextAnchor.MiddleLeft);
        int fs = HintFont;
        string pendingKey = null;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int i = 0;
        while (i <= text.Length)
        {
            bool atKey = false; int close = -1;
            if (i < text.Length && text[i] == '[')
            {
                close = text.IndexOf(']', i);
                atKey = close > i + 1;
            }
            if (i == text.Length || atKey)
            {
                string t = sb.ToString().Trim(' ', '|', '—', '-', ':');
                if (pendingKey != null || t.Length > 0)
                    AddHintUnit(pendingKey, t, st, fs);
                sb.Length = 0; pendingKey = null;
                if (i == text.Length) break;
                string inner = text.Substring(i + 1, close - i - 1);
                string nk = NormalizeKey(inner);
                if (nk.Length > 9) { sb.Append(inner).Append(' '); }   // uzun ifade: düz metin
                else pendingKey = nk;
                i = close + 1;
                continue;
            }
            sb.Append(text[i]);
            i++;
        }

        // Greedy satır sarma
        float lineW = 0f;
        for (int u = 0; u < hintUnits.Count; u++)
        {
            float add = hintUnits[u].w + (lineW > 0f ? HintGap : 0f);
            if (lineW > 0f && lineW + add > maxW)
            {
                hintLineEnds.Add(u); hintLineW.Add(lineW);
                lineW = hintUnits[u].w;
            }
            else lineW += add;
        }
        hintLineEnds.Add(hintUnits.Count); hintLineW.Add(lineW);
    }

    private static string NormalizeKey(string inner)
    {
        string k = inner.Trim();
        string low = k.ToLowerInvariant();
        if (low.StartsWith("hold ")) k = k.Substring(5);
        else if (low.EndsWith(" basılı tut")) k = k.Substring(0, k.Length - 11);
        return k.Trim();
    }

    private static void AddHintUnit(string key, string text, GUIStyle st, int fs)
    {
        float w = 0f;
        if (!string.IsNullOrEmpty(key)) w += UIUtil.KeyCapWidth(key, fs) + (text.Length > 0 ? 6f : 0f);
        if (text.Length > 0) w += st.CalcSize(new GUIContent(text)).x;
        hintUnits.Add(new HintUnit { key = key, text = text, w = w });
    }

    /// <summary>İpucu satırının (sarılmış) boyutu.</summary>
    public static Vector2 HintSize(string text, float maxW = -1f)
    {
        if (maxW <= 0f) maxW = Screen.width - 40f;
        LayoutHint(text, maxW, UITheme.Ivory);
        float w = 0f;
        foreach (float lw in hintLineW) w = Mathf.Max(w, lw);
        return new Vector2(w, hintLineW.Count * HintRowH);
    }

    /// <summary>İpucu satırını r içine ortalayarak çizer (panelsiz). Satırlar sığmazsa alt alta sarılır.</summary>
    public static void HintDraw(Rect r, string text, Color color)
    {
        LayoutHint(text, r.width, color);
        GUIStyle st = UITheme.Style(TextRole.Body, color, TextAnchor.MiddleLeft);
        int fs = HintFont;
        float rowH = HintRowH;
        int u = 0;
        for (int line = 0; line < hintLineEnds.Count; line++)
        {
            float x = r.center.x - hintLineW[line] * 0.5f;
            float y = r.y + line * rowH;
            for (; u < hintLineEnds[line]; u++)
            {
                HintUnit h = hintUnits[u];
                if (!string.IsNullOrEmpty(h.key))
                {
                    float kw = UIUtil.KeyCapWidth(h.key, fs);
                    UIUtil.KeyCap(new Rect(x, y + 3f, kw, rowH - 6f), h.key, fs);
                    x += kw + (h.text.Length > 0 ? 6f : 0f);
                }
                if (h.text.Length > 0)
                {
                    float tw = st.CalcSize(new GUIContent(h.text)).x;
                    GUI.Label(new Rect(x, y, tw + 2f, rowH), h.text, st);
                    x += tw;
                }
                x += HintGap;
            }
        }
    }

    /// <summary>Plaket şerit içinde ipucu satırı. y &lt; 0 ise ekran ortası yuvasına (CenterSlot) yerleşir. alpha: geçiş için.</summary>
    public static Rect HintLine(string text, Color color, float y = -1f, float alpha = 1f)
    {
        Vector2 sz = HintSize(text, Screen.width - 60f);
        float pad = 14f;
        Rect box = new Rect(0f, 0f, sz.x + pad * 2f, sz.y + 8f);
        box.x = (Screen.width - box.width) * 0.5f;
        box.y = y >= 0f ? y : UIUtil.CenterSlot(box.height);
        Color old = GUI.color;
        GUI.color = new Color(old.r, old.g, old.b, old.a * alpha);
        UIUtil.Panel(box, PanelKind.Plaque, 0.9f);
        HintDraw(new Rect(box.x + pad, box.y + 4f, sz.x, sz.y), text, color);
        GUI.color = old;
        return box;
    }

    // ---------------- Kafa üstü tepki işareti (güvenlik / ziyaretçi) ----------------

    /// <summary>Koyu plaket kare + renkli 2 px çerçeve + ikon. pop: giriş büyütmesi (1 = normal). fill 0–1: altında dolum çubuğu (-1 = yok).</summary>
    public static void ReactionBadge(Vector2 center, IconId icon, Color color, float pop = 1f, float fill = -1f, string tinyText = null)
    {
        float sc = UITheme.Scale;
        float s = Mathf.Round(34f * sc * pop);
        Rect r = new Rect(Mathf.Round(center.x - s * 0.5f), Mathf.Round(center.y - s * 0.5f), s, s);
        UIUtil.DrawRect(r, UITheme.WithAlpha(UITheme.CharDeep, 0.88f));
        UIUtil.Outline(r, 2f, color);
        if (tinyText != null)
            UIUtil.Text(r, tinyText, TextRole.Mono, color, TextAnchor.MiddleCenter);
        else
        {
            float i = s * 0.62f;
            UIUtil.Icon(new Rect(r.center.x - i * 0.5f, r.center.y - i * 0.5f, i, i), icon, color);
        }
        if (fill >= 0f)
        {
            Rect bar = new Rect(r.x, r.yMax + 3f, s, Mathf.Round(4f * sc));
            UIUtil.DrawRect(bar, UITheme.WithAlpha(UITheme.CharDeep, 0.88f));
            UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fill), bar.height), color);
        }
    }
}
