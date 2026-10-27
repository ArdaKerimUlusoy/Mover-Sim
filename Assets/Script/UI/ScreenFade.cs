using UnityEngine;

/// <summary>
/// Aşama geçişlerinde (garaj <-> kamyon <-> müze) kısa ekran kararması + aşama başlığı ("MÜZE — Seviye 2").
/// Geçişleri mevcut durumlardan algılar: TruckZone (FloorPlanUI.LocalInTruck ile aynı mantık), müze kat planı sınırı,
/// MuseumSession.Level (yeni görev) ve kaçış sekansı. Kendini çalışma anında kurar; sahneye bir şey eklemek gerekmez.
/// Kamyon kalkış / polis sekanslarının kendi kararması (TruckEscapeUI) varsa üstüne ikinci bir kararma eklenmez, sadece başlık gösterilir.
/// </summary>
public class ScreenFade : MonoBehaviour
{
    private const float DebounceSeconds = 0.35f;  // kapı eşiğinde gidip gelince titremesin
    private const float DipPeakAlpha = 0.9f;
    private const float DipIn = 0.18f, DipHold = 0.1f, DipOut = 0.5f;
    private const float OpenFadeSeconds = 1.4f;   // oyuna ilk girişte karanlıktan açılış
    private const float TitleIn = 0.45f, TitleHold = 2.2f, TitleOut = 0.9f;
    private const float AfterSequenceQuiet = 6f;  // sekanstan sonra bu süre ek karartma yok

    private static ScreenFade instance;

    private GamePhase shownPhase = GamePhase.Menu;
    private GamePhase candidate = GamePhase.Menu;
    private float candidateSince;
    private int lastLevel = -1;
    private float lastSequenceTime = -100f;

    private float dipStart = -100f;
    private float openStart = -100f;
    private float titleStart = -100f;
    private string title = "", subtitle = "";


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("ScreenFade");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenFade>();
    }

    /// <summary>Elle geçiş göster (başka sistemler için): kısa kararma + başlık.</summary>
    public static void Show(string titleText, string subtitleText = "", bool dip = true)
    {
        if (instance == null) return;
        instance.Trigger(titleText, subtitleText, dip);
    }

    private void Trigger(string titleText, string subtitleText, bool dip)
    {
        title = titleText ?? "";
        subtitle = subtitleText ?? "";
        titleStart = Time.unscaledTime + (dip ? 0.2f : 0f);
        if (dip) dipStart = Time.unscaledTime;
    }

    private void Update()
    {
        if (!GameFlow.LocalSpawned)
        {
            // Menüdeyiz: bir sonraki girişte başlangıç animasyonu çalışsın
            shownPhase = GamePhase.Menu;
            candidate = GamePhase.Menu;
            lastLevel = -1;
            return;
        }

        MuseumSession ms = MuseumSession.Instance;
        if (ms != null && ms.SequenceActive) lastSequenceTime = Time.unscaledTime;
        bool quiet = Time.unscaledTime - lastSequenceTime < AfterSequenceQuiet;

        // ---- Aşama değişimi (debounce) ----
        GamePhase now = GameFlow.Phase;
        if (now != candidate)
        {
            candidate = now;
            candidateSince = Time.unscaledTime;
        }

        if (candidate != shownPhase && Time.unscaledTime - candidateSince >= DebounceSeconds)
        {
            GamePhase previous = shownPhase;
            shownPhase = candidate;

            if (previous == GamePhase.Menu)
            {
                // Oyuna ilk giriş: karanlıktan açıl + "GARAJ"
                openStart = Time.unscaledTime;
                Trigger(GameFlow.StageTitle(shownPhase), GameFlow.StageSubtitle(shownPhase), false);
            }
            else
            {
                Trigger(GameFlow.StageTitle(shownPhase), GameFlow.StageSubtitle(shownPhase), !quiet);
            }
        }

        // ---- Yeni görev / seviye (müze yeniden kuruldu) ----
        if (ms != null && ms.Ready)
        {
            int level = ms.Level;
            if (lastLevel != -1 && level != lastLevel)
            {
                // Kalkış sekansı kendi kararmasını yapar: sadece başlık göster
                Trigger(Loc.T("stage.museum", level), GameFlow.StageSubtitle(GamePhase.Museum), false);
            }
            lastLevel = level;
        }
    }

    private void OnGUI()
    {
        if (!GameFlow.LocalSpawned) return;
        if (Event.current.type != EventType.Repaint) return;
        float t = Time.unscaledTime;
        GUI.depth = -1000;

        // ---- Karartma (kömür rengi) ----
        float a = 0f;
        float dt = t - dipStart;
        if (dt >= 0f && dt < DipIn + DipHold + DipOut)
        {
            if (dt < DipIn) a = dt / DipIn * DipPeakAlpha;
            else if (dt < DipIn + DipHold) a = DipPeakAlpha;
            else a = DipPeakAlpha * (1f - (dt - DipIn - DipHold) / DipOut);
        }
        float ot = t - openStart;
        if (ot >= 0f && ot < OpenFadeSeconds)
        {
            // Açılış: kısa süre tam kömür, sonra açılır
            float oa = ot < 0.3f ? 1f : 1f - (ot - 0.3f) / (OpenFadeSeconds - 0.3f);
            a = Mathf.Max(a, oa);
        }
        if (a > 0.003f) UIUtil.Scrim(UITheme.WithAlpha(UITheme.Charcoal, Mathf.Clamp01(a)));

        // ---- Aşama başlığı: Section üst satır + Display ad + süslü ayraç ----
        float tt = t - titleStart;
        float total = TitleIn + TitleHold + TitleOut;
        if (tt < 0f || tt > total || string.IsNullOrEmpty(title)) return;
        float ta = tt < TitleIn ? tt / TitleIn : tt < TitleIn + TitleHold ? 1f : 1f - (tt - TitleIn - TitleHold) / TitleOut;
        ta = Mathf.Clamp01(ta);
        float ease = UIUtil.Ease.OutCubic(tt / TitleIn);

        float sc = UITheme.Scale;
        float bandH = 190f * sc;
        float cy = Screen.height * 0.34f - (1f - ease) * 12f * sc;
        Rect band = new Rect(0f, cy - bandH * 0.5f, Screen.width, bandH);
        // Karartma zaten ağırsa bant ayrıca çizilmez
        if (a < 0.6f)
        {
            UIUtil.DrawRect(band, UITheme.WithAlpha(UITheme.Charcoal, 0.62f * ta));
            UIUtil.DrawRect(new Rect(0f, band.y, Screen.width, 1f), UITheme.WithAlpha(UITheme.Brass, 0.5f * ta));
            UIUtil.DrawRect(new Rect(0f, band.yMax - 1f, Screen.width, 1f), UITheme.WithAlpha(UITheme.Brass, 0.5f * ta));
        }

        bool hasSub = !string.IsNullOrEmpty(subtitle);
        float y = cy - (hasSub ? 52f : 36f) * sc;
        if (hasSub)
        {
            UIUtil.Text(new Rect(0f, y, Screen.width, 20f * sc), UITheme.Caps(subtitle), TextRole.Section, UITheme.WithAlpha(UITheme.BrassLight, ta), TextAnchor.MiddleCenter);
            y += 26f * sc;
        }
        UIUtil.Text(new Rect(0f, y, Screen.width, 66f * sc), UITheme.Caps(title), TextRole.Display, UITheme.WithAlpha(UITheme.Ivory, ta), TextAnchor.MiddleCenter);
        y += 74f * sc;
        float dw = Mathf.Min(Screen.width * 0.4f, 360f * sc);
        UIUtil.Divider((Screen.width - dw) * 0.5f, y, dw, UITheme.WithAlpha(UITheme.Brass, ta), true);
    }
}
