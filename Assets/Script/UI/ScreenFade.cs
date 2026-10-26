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

    private GUIStyle titleStyle, subStyle;

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
        float t = Time.unscaledTime;
        GUI.depth = -1000;

        // ---- Karartma ----
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
            // Açılış: kısa süre tam siyah, sonra açılır
            float oa = ot < 0.3f ? 1f : 1f - (ot - 0.3f) / (OpenFadeSeconds - 0.3f);
            a = Mathf.Max(a, oa);
        }
        if (a > 0.003f)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(a));
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // ---- Aşama başlığı ----
        float tt = t - titleStart;
        float total = TitleIn + TitleHold + TitleOut;
        if (tt < 0f || tt > total || string.IsNullOrEmpty(title)) return;
        float ta = tt < TitleIn ? tt / TitleIn : tt < TitleIn + TitleHold ? 1f : 1f - (tt - TitleIn - TitleHold) / TitleOut;
        ta = Mathf.Clamp01(ta);

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 46 };
            subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
        }

        float y = Screen.height * 0.28f;
        DrawShadowed(new Rect(0f, y, Screen.width, 62f), title, titleStyle, new Color(1f, 0.88f, 0.45f, ta), ta);
        if (!string.IsNullOrEmpty(subtitle))
            DrawShadowed(new Rect(0f, y + 60f, Screen.width, 32f), subtitle, subStyle, new Color(0.95f, 0.95f, 0.95f, ta), ta);
    }

    private static void DrawShadowed(Rect r, string text, GUIStyle style, Color color, float alpha)
    {
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
        GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }
}
