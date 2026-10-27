﻿using UnityEngine;

/// <summary>
/// Sağ üstte o anki HEDEF (aşamaya ve role göre) ve bağlama göre tuş ipuçları.
/// Mevcut durumları okur: GameFlow.Phase, BodyCam, MuseumSession, SecurityManager, PlayerStealth, WorkbenchUI.
/// Kendini kurar; sahneye bir şey eklemek gerekmez.
/// Sunum: koyu plaket, Section başlık + Body hedef; tuş ipuçları yalnızca hedef değiştikten sonra kısa süre görünür.
/// </summary>
public class ObjectiveHUD : MonoBehaviour
{
    private const float HintShowSeconds = 22f, HintFadeSeconds = 1.2f;

    private static ObjectiveHUD instance;
    private string lastKey = "";
    private float keyChangedAt = -100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("ObjectiveHUD");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ObjectiveHUD>();
    }

    /// <summary>Hedef metninin çeviri anahtarı + varsa argümanlar; ipucu anahtarı.</summary>
    private void Evaluate(out string objKey, out object[] objArgs, out string hintKey)
    {
        objArgs = null;
        PlayerStealth me = PlayerStealth.Local;
        MuseumSession ms = MuseumSession.Instance;
        GamePhase phase = GameFlow.Phase;
        bool solo = GameFlow.Solo;

        if (me != null && me.IsJailed) { objKey = "obj.jail"; hintKey = "hint.jail"; return; }
        if (ms != null && ms.SequenceActive) { objKey = "obj.sequence"; hintKey = ""; return; }
        if (ms == null || !ms.Ready) { objKey = "obj.wait"; hintKey = ""; return; }

        SecurityManager sec = SecurityManager.Instance;
        bool alarm = sec != null && sec.AlarmActive;

        if (ms.QuotaMet)
        {
            if (phase == GamePhase.Truck) { objKey = "obj.close"; hintKey = "hint.truck"; }
            else { objKey = "obj.return"; hintKey = "hint.museum"; }
            return;
        }

        objArgs = new object[] { Mathf.Max(0, ms.Earned), ms.Quota };

        switch (phase)
        {
            case GamePhase.Garage:
                objKey = "obj.garage"; hintKey = "hint.garage";
                break;
            case GamePhase.Truck:
                if (WorkbenchUI.IsOpen) { objKey = "obj.draw"; hintKey = "hint.workbench"; }
                else if (BodyCam.Live && !BodyCam.LocalRecording) { objKey = "obj.truck.live"; hintKey = "hint.truck"; }
                else { objKey = solo ? "obj.truck.solo" : "obj.truck.team"; hintKey = "hint.truck"; }
                break;
            case GamePhase.Museum:
                hintKey = "hint.museum";
                if (alarm) objKey = "obj.alarm";
                else if (BodyCam.LocalRecording) objKey = "obj.recording";
                else objKey = solo ? "obj.museum.solo" : "obj.museum.team";
                break;
            default:
                objKey = "obj.wait"; hintKey = "";
                break;
        }
    }

    private void OnGUI()
    {
        if (!GameFlow.LocalSpawned || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BriefingUI.IsOpen || FloorPlanUI.IsOpen) return;
        if (Event.current.type != EventType.Repaint) return;

        Evaluate(out string objKey, out object[] args, out string hintKey);
        string obj = args != null ? Loc.T(objKey, args) : Loc.T(objKey);
        string hint = string.IsNullOrEmpty(hintKey) ? "" : Loc.T(hintKey);

        // Hedef değişince ipuçları kısa süre görünür, sonra HUD sadeleşir
        if (objKey != lastKey) { lastKey = objKey; keyChangedAt = Time.unscaledTime; }
        float since = Time.unscaledTime - keyChangedAt;
        float hintA = hint.Length == 0 ? 0f : Mathf.Clamp01((HintShowSeconds + HintFadeSeconds - since) / HintFadeSeconds);

        float sc = UITheme.Scale;
        float pad = UITheme.S4 * sc;
        float w = 300f * sc;
        float innerW = w - pad * 2f;

        float headH = 26f * sc;
        float bodyH = UIUtil.MeasureHeight(obj, TextRole.BodyStrong, innerW);
        float hintH = hintA > 0.01f ? HudKit.KeyLine(new Rect(0f, 0f, innerW, 0f), hint, UITheme.IvoryDim, false) : 0f;
        float h = pad * 0.75f + headH + bodyH + (hintH > 0f ? 6f * sc + hintH : 0f) + pad * 0.75f;

        Color accent = objKey == "obj.alarm" || objKey == "obj.jail" ? UITheme.Danger
                     : objKey == "obj.close" || objKey == "obj.return" ? UITheme.Sage : UITheme.Brass;
        Color bodyCol = accent == UITheme.Danger ? UITheme.DangerLight : accent == UITheme.Sage ? UITheme.Sage : UITheme.Ivory;

        // Sağ üst köşe: ping ve para kutularının altına dizilir (çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopRight, UIUtil.HudOrder.Objective, w, h);
        UIUtil.Panel(r, accent);

        float x = r.x + pad, y = r.y + pad * 0.75f;
        y += UIUtil.SectionHeader(new Rect(x, y, innerW, 0f), Loc.T("obj.title"));
        GUIStyle bs = UITheme.Style(TextRole.BodyStrong, bodyCol, TextAnchor.UpperLeft);
        bool ww = bs.wordWrap; bs.wordWrap = true;
        GUI.Label(new Rect(x, y, innerW, bodyH + 2f), obj, bs);
        bs.wordWrap = ww;
        y += bodyH;
        if (hintH > 0f)
        {
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hintA);
            HudKit.KeyLine(new Rect(x, y + 6f * sc, innerW, hintH), hint, UITheme.IvoryDim, true);
            GUI.color = old;
        }
    }
}

/// <summary>
/// Grup A'nın ortak HUD çizim yardımcıları (UIUtil'e dokunmadan): üst orta bant ve satır içi tuş kapaklı yazı.
/// </summary>
internal static class HudKit
{
    /// <summary>
    /// Üst ortada (TopSlot) plaket bant: solda 3 px vurgu şeridi + simge + yazı. Kritik / kalıcı durumlar için (toast değil).
    /// Yazıda [TUŞ] varsa tuş kapağına çevrilir. Çizilen dikdörtgeni döner. alpha: bant solukluğu; iconColor: simge rengi (varsayılan accent).
    /// </summary>
    public static Rect Banner(string text, IconId icon, Color accent, Color textColor, float alpha = 1f, Color? iconColor = null)
    {
        float sc = UITheme.Scale;
        float pad = UITheme.S4 * sc, gap = UITheme.S2 * sc, iconS = 22f * sc;
        const TextRole role = TextRole.BodyStrong;
        Color tc = UITheme.WithAlpha(textColor, alpha);
        GUIStyle s = UITheme.Style(role, tc, TextAnchor.MiddleLeft);
        bool keys = text.IndexOf('[') >= 0 && text.IndexOf(']') > text.IndexOf('[');
        bool ww = s.wordWrap; s.wordWrap = true;
        float avail = Screen.width * 0.86f - pad * 2f - iconS - gap;
        float natural = keys ? KeyLineWidth(text, role) : s.CalcSize(new GUIContent(text)).x;
        float tw = Mathf.Min(natural + 2f, avail);
        float th = keys ? KeyLine(new Rect(0f, 0f, tw, 0f), text, tc, false, role) : s.CalcHeight(new GUIContent(text), tw);
        float h = Mathf.Max(34f * sc, th + 12f * sc);
        float w = pad * 2f + iconS + gap + tw;

        float y = UIUtil.TopSlot(h);
        Rect r = new Rect((Screen.width - w) * 0.5f, y, w, h);
        UIUtil.Panel(r, accent, alpha * 0.96f);
        UIUtil.Icon(new Rect(r.x + pad, r.y + (h - iconS) * 0.5f, iconS, iconS), icon, UITheme.WithAlpha(iconColor ?? accent, alpha));
        float tx = r.x + pad + iconS + gap;
        if (keys) KeyLine(new Rect(tx, r.y + (h - th) * 0.5f, tw + 2f, th), text, tc, true, role);
        else GUI.Label(new Rect(tx, r.y, tw + 2f, h), text, s);
        s.wordWrap = ww;
        return r;
    }

    private static float LineHeight(TextRole role) => (role == TextRole.Caption ? 22f : 28f) * UITheme.Scale;
    private static int KeyFont(TextRole role) => Mathf.RoundToInt((role == TextRole.Caption ? 14f : 17f) * UITheme.Scale);

    /// <summary>[TUŞ] parçalı yazının tek satırdaki doğal genişliği.</summary>
    public static float KeyLineWidth(string text, TextRole role = TextRole.Caption)
    {
        GUIStyle cs = UITheme.Style(role, Color.white, TextAnchor.MiddleLeft);
        bool ww = cs.wordWrap; cs.wordWrap = false;
        float space = Mathf.Max(3f, cs.fontSize * 0.3f);
        int fs = KeyFont(role);
        float x = 0f;
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == ' ') { x += space; i++; continue; }
            if (ch == '[')
            {
                int e = text.IndexOf(']', i);
                if (e > i) { x += UIUtil.KeyCapWidth(text.Substring(i + 1, e - i - 1), fs) + space; i = e + 1; continue; }
            }
            int j = i;
            while (j < text.Length && text[j] != ' ' && text[j] != '[') j++;
            if (j == i) j = i + 1;
            x += cs.CalcSize(new GUIContent(text.Substring(i, j - i))).x;
            i = j;
        }
        cs.wordWrap = ww;
        return x;
    }

    /// <summary>
    /// Yazıdaki [TUŞ] parçalarını satır içi tuş kapağına çeviren, sarmalı satır. draw=false ise yalnızca yükseklik ölçer.
    /// Kullanılan yüksekliği döner.
    /// </summary>
    public static float KeyLine(Rect r, string text, Color color, bool draw, TextRole role = TextRole.Caption)
    {
        float sc = UITheme.Scale;
        float lh = LineHeight(role);
        int fs = KeyFont(role);
        GUIStyle cs = UITheme.Style(role, color, TextAnchor.MiddleLeft);
        bool ww = cs.wordWrap; cs.wordWrap = false;
        float space = Mathf.Max(3f, cs.fontSize * 0.3f);
        float x = r.x, y = r.y;
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch == '\n') { x = r.x; y += lh; i++; continue; }
            if (ch == ' ') { x += space; i++; continue; }
            if (ch == '[')
            {
                int e = text.IndexOf(']', i);
                if (e > i)
                {
                    string key = text.Substring(i + 1, e - i - 1);
                    float kw = UIUtil.KeyCapWidth(key, fs);
                    if (x + kw > r.xMax && x > r.x) { x = r.x; y += lh; }
                    if (draw) UIUtil.KeyCap(new Rect(x, y + 2f * sc, kw, lh - 4f * sc), key, fs);
                    x += kw + space;
                    i = e + 1;
                    continue;
                }
            }
            int j = i;
            while (j < text.Length && text[j] != ' ' && text[j] != '[' && text[j] != '\n') j++;
            if (j == i) j = i + 1;
            string word = text.Substring(i, j - i);
            float wwid = cs.CalcSize(new GUIContent(word)).x;
            if (x + wwid > r.xMax && x > r.x) { x = r.x; y += lh; }
            if (draw) GUI.Label(new Rect(x, y, wwid + 2f, lh), word, cs);
            x += wwid;
            i = j;
        }
        cs.wordWrap = ww;
        return y - r.y + lh;
    }
}
