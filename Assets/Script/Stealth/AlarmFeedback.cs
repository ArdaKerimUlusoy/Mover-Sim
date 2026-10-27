using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Yerel oyuncuya durumunu net gösteren ekran geri bildirimleri (saf MonoBehaviour, kendiliğinden kurulur):
///  - şüphe artarken kenarlarda sarı vinyet (şüphe çubuğunu PlayerStealth zaten çiziyor, burada tekrar yok)
///  - alarmda kırmızı yanıp sönen kenar + üst ortada durum metni (kovalıyor / arıyor); "ALARM" bandını SecurityManager çiziyor
///  - alarm başlarken sting sesi
///  - birisi fark edilince herkese "{isim} FARK EDİLDİ!" bildirimi (SecurityManager.SpottedRpc → PushSpotted)
///  - sol altta alet tuş ipuçları (sadece müzede)
/// </summary>
public class AlarmFeedback : MonoBehaviour
{
    private const float SpottedSeconds = 2.5f;
    private const float HintRowHeight = 24f;

    private struct Spotted { public string name; public Color color; public float start; }

    private static AlarmFeedback inst;
    private static readonly List<Spotted> spotted = new List<Spotted>();

    private Texture2D vignette;
    private bool prevAlarm;
    private float guardScanAt;
    private GuardAI[] guards = new GuardAI[0];
    private bool anyChase;
    private GUIStyle hintStyle, hintShadow, bigStyle, bigShadow;

    /// <summary>Sol alttaki ipucu satırı görünüyorsa yüksekliği (ChatUI bunun üstüne dizilir).</summary>
    public static float HintHeight => HintsVisible ? HintRowHeight : 0f;

    private static bool HintsVisible
    {
        get
        {
            PlayerStealth me = PlayerStealth.Local;
            // Alet paraları artık görev başı verilmiyor (paralar kamyondaki dolaptan alınır): satır yalnızca alet parası varsa çıkar
            return me != null && me.IsSpawned && !me.IsJailed && me.CoinsLeft > 0 && MuseumSession.Instance != null && MuseumSession.Instance.Ready;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        inst = null;
        spotted.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (inst != null) return;
        GameObject go = new GameObject("AlarmFeedback");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<AlarmFeedback>();
    }

    /// <summary>Herkes: "X FARK EDİLDİ!" bildirimi ekle (SecurityManager çağırır).</summary>
    public static void PushSpotted(string playerName, int colorIndex)
    {
        Color c = PlayerNetworkSetup.Colors[Mathf.Abs(colorIndex) % PlayerNetworkSetup.Colors.Length];
        spotted.Add(new Spotted { name = string.IsNullOrEmpty(playerName) ? "?" : playerName, color = c, start = Time.unscaledTime });
        if (spotted.Count > 3) spotted.RemoveAt(0);
        GameAudio.Play2D(SoundId.Spotted, 0.9f, 0f);
    }

    private void OnDestroy()
    {
        if (inst == this) inst = null;
        if (vignette != null) Destroy(vignette);
    }

    private void Update()
    {
        SecurityManager sm = SecurityManager.Instance;
        bool alarm = sm != null && sm.IsSpawned && sm.AlarmActive;
        if (alarm && !prevAlarm) GameAudio.Play2D(SoundId.AlarmSting, 0.85f, 0f);
        prevAlarm = alarm;

        // İstemcide güvenlik listesi (SecurityManager.Guards sadece sunucuda dolu): yarım saniyede bir tara
        if (alarm && Time.unscaledTime >= guardScanAt)
        {
            guardScanAt = Time.unscaledTime + 0.5f;
            guards = FindObjectsByType<GuardAI>(FindObjectsSortMode.None);
            anyChase = false;
            foreach (GuardAI g in guards) if (g != null && g.Online && g.CurrentState == GuardAI.State.Chase) { anyChase = true; break; }
        }
    }

    private void EnsureAssets()
    {
        if (vignette == null)
        {
            const int n = 64;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            Color[] px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f; // kenarlara doğru koyulaşır
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.15f, d));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }
            vignette.SetPixels(px);
            vignette.Apply(false, true);
        }
        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            hintShadow = new GUIStyle(hintStyle);
            hintShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            bigShadow = new GUIStyle(bigStyle);
            bigShadow.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
        }
    }

    private void DrawVignette(Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    private void OnGUI()
    {
        PlayerStealth me = PlayerStealth.Local;
        bool inGame = me != null && me.IsSpawned;
        if (!inGame && spotted.Count == 0) return;

        EnsureAssets();
        bool repaint = Event.current.type == EventType.Repaint;
        SecurityManager sm = SecurityManager.Instance;
        bool alarm = sm != null && sm.IsSpawned && sm.AlarmActive;

        if (inGame && !me.IsJailed)
        {
            if (alarm)
            {
                // Tehlike renginde nabızlı kenar vinyeti + durum bandı (kovalıyor: Danger, arıyor: Ochre)
                if (repaint)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));
                    DrawVignette(UITheme.WithAlpha(UITheme.Danger, Mathf.Lerp(0.25f, 0.65f, pulse)));
                }
                string status = anyChase ? Loc.T("fx.chased") : Loc.T("fx.searching");
                HudKit.Banner(status, IconId.Alarm, anyChase ? UITheme.Danger : UITheme.Ochre, anyChase ? UITheme.DangerLight : UITheme.Ochre);
            }
            else if (me.Suspicion > 0.04f && repaint)
            {
                // Şüphe arttıkça Ochre -> Danger'a dolan hafif kenar
                float s = me.Suspicion;
                DrawVignette(UITheme.WithAlpha(Color.Lerp(UITheme.Ochre, UITheme.Danger, s), Mathf.Lerp(0.06f, 0.45f, s)));
            }

            DrawHints(me);
        }

        DrawSpotted(repaint);
    }

    private void DrawHints(PlayerStealth me)
    {
        if (!HintsVisible || Event.current.type != EventType.Repaint) return;
        // "Q: bozuk para (x3) — ..." -> tuş kapağı + etiket
        string text = Loc.T("gadget.hints", me.CoinsLeft, me.BananasLeft);
        string key = "", label = text;
        int colon = text.IndexOf(": ");
        if (colon > 0 && colon <= 3) { key = text.Substring(0, colon); label = text.Substring(colon + 2); }

        float sc = UITheme.Scale;
        int fs = Mathf.RoundToInt(14f * sc);
        GUIStyle ls = UITheme.Style(TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleLeft);
        bool ww = ls.wordWrap; ls.wordWrap = false;
        float keyW = key.Length > 0 ? UIUtil.KeyCapWidth(key, fs) : 0f;
        float labelW = ls.CalcSize(new GUIContent(label)).x;
        float h = Mathf.Max(HintRowHeight + 4f, 26f * sc);
        float tw = Mathf.Min(keyW + (keyW > 0f ? 8f : 0f) + labelW + 28f, Screen.width * 0.5f);
        // Sol alt köşe (en altta): kamera pili ve sohbet bunun üstüne dizilir
        Rect box = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.GadgetHints, tw, h);
        UIUtil.Panel(box, PanelKind.Plaque, 0.85f);
        float x = box.x + 14f;
        if (keyW > 0f) { UIUtil.KeyCap(new Rect(x, box.y + 4f, keyW, h - 8f), key, fs); x += keyW + 8f; }
        GUI.Label(new Rect(x, box.y, box.xMax - x - 8f, h), label, ls);
        ls.wordWrap = ww;
    }

    private void DrawSpotted(bool repaint)
    {
        if (!repaint || spotted.Count == 0) return;
        for (int i = spotted.Count - 1; i >= 0; i--)
            if (Time.unscaledTime - spotted[i].start > SpottedSeconds) spotted.RemoveAt(i);

        float sc = UITheme.Scale;
        float baseY = Screen.height * 0.26f; // üst ortadaki şüphe / alarm yazılarının altında kalsın
        float rowH = 52f * sc, pad = UITheme.S4 * sc, iconS = 30f * sc;
        for (int i = 0; i < spotted.Count; i++)
        {
            Spotted s = spotted[i];
            float t = Time.unscaledTime - s.start;
            float fade = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((SpottedSeconds - t) / 0.4f);
            float slide = (1f - UIUtil.Ease.OutCubic(t / UITheme.MotionBase)) * 10f * sc;

            string text = Loc.T("fx.spotted", s.name);
            GUIStyle ts = UITheme.Style(TextRole.Title, UITheme.WithAlpha(UITheme.Ivory, fade), TextAnchor.MiddleLeft);
            float tw = Mathf.Min(ts.CalcSize(new GUIContent(text)).x + 4f, Screen.width - 24f - pad * 2f - iconS - UITheme.S3 * sc);
            float w = pad * 2f + iconS + UITheme.S3 * sc + tw;
            Rect box = new Rect((Screen.width - w) * 0.5f, baseY + i * (rowH + 8f * sc) - slide, w, rowH);

            // Plaket + oyuncu renginde sol şerit; uyarı simgesi tehlike renginde
            UIUtil.Panel(box, s.color, fade);
            UIUtil.Icon(new Rect(box.x + pad, box.y + (rowH - iconS) * 0.5f, iconS, iconS), IconId.Warning, UITheme.WithAlpha(UITheme.DangerLight, fade));
            GUI.Label(new Rect(box.x + pad + iconS + UITheme.S3 * sc, box.y, tw + 2f, rowH), text, ts);
        }
    }
}
