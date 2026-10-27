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
                // Kırmızı yanıp sönen kenar + durum metni
                if (repaint)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f));
                    DrawVignette(new Color(0.95f, 0.05f, 0.05f, Mathf.Lerp(0.3f, 0.8f, pulse)));
                }
                string status = anyChase ? Loc.T("fx.chased") : Loc.T("fx.searching");
                Color sc = anyChase ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 0.75f, 0.35f);
                UIUtil.DrawPrompt(status, UIUtil.TopSlot(30f), sc, 20);
            }
            else if (me.Suspicion > 0.04f && repaint)
            {
                // Şüphe arttıkça sarıdan turuncuya dolan kenar
                float s = me.Suspicion;
                DrawVignette(new Color(1f, Mathf.Lerp(0.85f, 0.45f, s), 0.1f, Mathf.Lerp(0.08f, 0.6f, s)));
            }

            DrawHints(me);
        }

        DrawSpotted(repaint);
    }

    private void DrawHints(PlayerStealth me)
    {
        if (!HintsVisible) return;
        string text = Loc.T("gadget.hints", me.CoinsLeft, me.BananasLeft);
        float tw = Mathf.Min(hintStyle.CalcSize(new GUIContent(text)).x + 26f, Screen.width * 0.5f);
        // Sol alt köşe (en altta): kamera pili ve sohbet bunun üstüne dizilir
        Rect box = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.GadgetHints, tw, HintRowHeight + 4f);
        UIUtil.Panel(box, 0.8f);
        Rect r = new Rect(box.x + 13f, box.y + 4f, box.width - 18f, HintRowHeight - 4f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, hintShadow);
        GUI.Label(r, text, hintStyle);
    }

    private void DrawSpotted(bool repaint)
    {
        if (!repaint || spotted.Count == 0) return;
        for (int i = spotted.Count - 1; i >= 0; i--)
            if (Time.unscaledTime - spotted[i].start > SpottedSeconds) spotted.RemoveAt(i);

        float baseY = Screen.height * 0.26f; // üst ortadaki şüphe / alarm yazılarının altında kalsın
        for (int i = 0; i < spotted.Count; i++)
        {
            Spotted s = spotted[i];
            float t = Time.unscaledTime - s.start;
            float fade = Mathf.Clamp01(t / 0.15f) * Mathf.Clamp01((SpottedSeconds - t) / 0.4f);
            float pop = 1f + 0.12f * Mathf.Clamp01(1f - t / 0.25f);

            string text = Loc.T("fx.spotted", s.name);
            float w = Mathf.Clamp(text.Length * 17f + 90f, 300f, Screen.width - 24f) * pop;
            float h = 52f * pop;
            Rect box = new Rect((Screen.width - w) / 2f, baseY + i * 60f - (h - 52f) / 2f, w, h);

            UIUtil.Panel(box, new Color(1f, 0.3f, 0.25f), fade);
            UIUtil.DrawRect(new Rect(box.x + 3f, box.y + 3f, box.width - 6f, box.height - 6f), new Color(0.45f, 0.05f, 0.05f, 0.55f * fade));

            // Simge: kırmızı rozet içinde "!"
            Rect icon = new Rect(box.x + 14f, box.y + (box.height - 34f) / 2f, 34f, 34f);
            UIUtil.DrawRect(icon, new Color(1f, 0.85f, 0.2f, fade));
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.Label(new Rect(icon.x + 1f, icon.y + 1f, icon.width, icon.height), "!", MakeIconStyle(new Color(0f, 0f, 0f, 0.9f)));
            GUI.Label(icon, "!", MakeIconStyle(new Color(0.55f, 0.05f, 0.05f, 1f)));

            Rect tr = new Rect(icon.xMax + 8f, box.y, box.xMax - icon.xMax - 14f, box.height);
            bigShadow.normal.textColor = new Color(0f, 0f, 0f, 0.9f * fade);
            bigStyle.normal.textColor = new Color(s.color.r, s.color.g, s.color.b, fade);
            GUI.Label(new Rect(tr.x + 1f, tr.y + 1f, tr.width, tr.height), text, bigShadow);
            GUI.Label(tr, text, bigStyle);
            GUI.color = old;
        }
    }

    private GUIStyle iconStyle;
    private GUIStyle MakeIconStyle(Color c)
    {
        if (iconStyle == null)
            iconStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        iconStyle.normal.textColor = c;
        return iconStyle;
    }
}
