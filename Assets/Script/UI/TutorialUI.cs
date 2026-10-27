using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ÖĞRETİCİ: ilk oyunda otomatik başlayan, adım adım görev anlatımı (kamyon, kayıt, çizim, müzeye giriş, eser değiştirme, kaçış;
/// fark edilme / alarm / hapis; çok oyunculu roller; bekleme önerileri). Her adım mevcut durumlardan okunan bir koşulla tamamlanır;
/// F2 ile her adım geçilebilir (bilgi adımları yalnızca F2 ile geçilir). Atlama: Esc'yi basılı tut ya da Esc menüsündeki düğme.
/// Bitince PlayerPrefs'e yazılır; ana menüden / Esc menüsünden yeniden başlatılabilir.
/// Kendini kurar (sahneye bir şey eklemek gerekmez). Metinler Loc anahtarlarıdır: tut.&lt;id&gt;.t / tut.&lt;id&gt;.b
/// </summary>
public class TutorialUI : MonoBehaviour
{
    public const Key NextKey = Key.F2;
    private const string DonePref = "MoverSim_TutorialDone";
    private const float SkipHoldSeconds = 1.4f;

    private static TutorialUI instance;

    /// <summary>Öğretici şu an ekranda mı?</summary>
    public static bool Active => instance != null && instance.running;

    /// <summary>Öğretici daha önce bitirildi / atlandı mı?</summary>
    public static bool Completed
    {
        get => PlayerPrefs.GetInt(DonePref, 0) == 1;
        private set { PlayerPrefs.SetInt(DonePref, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // Adım kimlikleri: sırayla oynar. solo/mp: yalnızca o modda gösterilir.
    private enum Only { Always, Solo, Team }
    private struct Step
    {
        public string id; public Only only;
        public Step(string id, Only only = Only.Always) { this.id = id; this.only = only; }
    }

    private static readonly Step[] Steps =
    {
        new Step("welcome"), new Step("goal"), new Step("brief"), new Step("truck"), new Step("plan"),
        new Step("roles", Only.Team), new Step("stealth"), new Step("alarm"),
        new Step("museum"), new Step("record"), new Step("back", Only.Solo), new Step("wait", Only.Team),
        new Step("draw"), new Step("drawdone"), new Step("swapback"), new Step("swap"),
        new Step("quota"), new Step("escape"), new Step("done"),
    };

    private bool running;
    private bool startPending;
    private bool wasSpawned;
    private float startAt = -1f;
    private int index;
    private bool latch;
    private int entrySwapped, entryLevel;
    private float advanceAt = -1f;     // koşul sağlandı: kısa "tamam" göstergesinden sonra geç
    private float escHeld;
    private GUIStyle titleStyle, bodyStyle, footStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("TutorialUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<TutorialUI>();
    }

    /// <summary>Öğreticiyi baştan başlat. Oyunda değilsek oyuna girince başlar.</summary>
    public static void Restart()
    {
        if (instance == null) return;
        Completed = false;
        if (GameFlow.LocalSpawned) instance.Begin(); else instance.startPending = true;
    }

    /// <summary>Öğreticiyi bitir (atla). Bir daha otomatik açılmaz.</summary>
    public static void Skip()
    {
        if (instance == null) return;
        instance.running = false;
        instance.startPending = false;
        Completed = true;
    }

    private void Begin()
    {
        running = true;
        startPending = false;
        index = -1;
        AdvanceStep();
    }

    private bool Applies(Step s)
    {
        if (s.only == Only.Solo) return GameFlow.Solo;
        if (s.only == Only.Team) return !GameFlow.Solo;
        return true;
    }

    private void AdvanceStep()
    {
        do { index++; } while (index < Steps.Length && !Applies(Steps[index]));
        latch = false;
        advanceAt = -1f;
        entrySwapped = CountSwapped();
        MuseumSession ms = MuseumSession.Instance;
        entryLevel = ms != null ? ms.Level : 0;
        if (index >= Steps.Length) { running = false; Completed = true; }
    }

    private static int CountSwapped()
    {
        int n = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null && d.IsSwapped) n++;
        return n;
    }

    // Koşul: her adımın "tamamlandı" testi (bilgi adımları false döner, F3 ile geçilir)
    private bool CheckDone(string id)
    {
        MuseumSession ms = MuseumSession.Instance;
        switch (id)
        {
            case "brief": latch |= BriefingUI.IsOpen; return latch && !BriefingUI.IsOpen;
            case "truck": return GameFlow.Phase == GamePhase.Truck;
            case "plan": latch |= FloorPlanUI.IsOpen; return latch && !FloorPlanUI.IsOpen;
            case "museum": return GameFlow.Phase == GamePhase.Museum;
            case "record": latch |= BodyCam.LocalRecording; return latch;
            case "back": return GameFlow.Phase == GamePhase.Truck;
            case "draw": latch |= WorkbenchUI.IsOpen; return latch;
            case "drawdone": return !WorkbenchUI.IsOpen;
            case "swapback": return GameFlow.Phase == GamePhase.Museum;
            case "swap": return CountSwapped() > entrySwapped;
            case "quota": return ms != null && ms.Ready && ms.QuotaMet;
            case "escape": return ms != null && (ms.DoorClosed || ms.SequenceActive || (ms.Ready && ms.Level != entryLevel));
            default: return false;
        }
    }

    private void Update()
    {
        bool spawned = GameFlow.LocalSpawned;
        if (spawned && !wasSpawned)
        {
            // Oyuna yeni girildi: ilk oyunda (ya da yeniden başlat istendiyse) öğretici başlar
            if (startPending || !Completed) startAt = Time.unscaledTime + 3.5f;
        }
        if (!spawned && wasSpawned) { running = false; startAt = -1f; }
        wasSpawned = spawned;

        if (!running && startAt > 0f && Time.unscaledTime >= startAt && spawned)
        {
            startAt = -1f;
            Begin();
        }
        if (!running || !spawned) return;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            // Esc'yi basılı tut: atla
            if (kb.escapeKey.isPressed) escHeld += Time.unscaledDeltaTime; else escHeld = 0f;
            if (escHeld >= SkipHoldSeconds) { escHeld = 0f; Skip(); return; }

            bool typing = ChatUI.IsTyping;
            if (!typing && kb[NextKey].wasPressedThisFrame && !SessionMenuUI.IsPaused) { AdvanceStep(); return; }
        }

        if (index < 0 || index >= Steps.Length) return;
        if (advanceAt < 0f)
        {
            if (CheckDone(Steps[index].id)) advanceAt = Time.unscaledTime + 1.1f;
        }
        else if (Time.unscaledTime >= advanceAt)
        {
            AdvanceStep();
        }
    }

    private void OnGUI()
    {
        if (!running || index < 0 || index >= Steps.Length || !GameFlow.LocalSpawned) return;
        if (SessionMenuUI.IsPaused) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            bodyStyle.normal.textColor = UIUtil.Cream;
            footStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            footStyle.normal.textColor = UIUtil.Muted;
        }

        Step s = Steps[index];
        bool done = advanceAt >= 0f;

        // İlerleme: geçerli adımlar içindeki sıra
        int total = 0, pos = 0;
        for (int i = 0; i < Steps.Length; i++)
        {
            if (!Applies(Steps[i])) continue;
            total++;
            if (i <= index) pos = total;
        }

        float w = Mathf.Min(Screen.width - 24f, 600f);
        string body = Loc.T("tut." + s.id + ".b", NextKey.ToString());
        float bodyH = bodyStyle.CalcHeight(new GUIContent(body), w - 24f);
        float h = 30f + bodyH + 34f;
        // Alt orta köşe: ses kutusu, hızlı envanter ve fırça yazılarının ÜSTÜNE dizilir (çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomCenter, UIUtil.HudOrder.Tutorial, w, h);

        GUI.depth = -20;
        Color accent = done ? UIUtil.Mint : UIUtil.Gold;
        UIUtil.Panel(r, accent);
        UIUtil.Tape(r);

        string head = Loc.T("tut.header", pos, total, Loc.T("tut." + s.id + ".t"));
        if (done) head = "✓ " + head;
        titleStyle.normal.textColor = accent;
        GUI.Label(new Rect(r.x + 14f, r.y + 6f, w - 24f, 24f), head, titleStyle);
        GUI.Label(new Rect(r.x + 14f, r.y + 30f, w - 24f, bodyH), body, bodyStyle);

        string foot = Loc.T("tut.footer", NextKey.ToString());
        GUI.Label(new Rect(r.x + 14f, r.yMax - 26f, w - 28f, 20f), foot, footStyle);

        // Esc basılı tutma göstergesi
        if (escHeld > 0.15f)
        {
            Rect bar = new Rect(r.x + 14f, r.yMax - 6f, (w - 28f) * Mathf.Clamp01(escHeld / SkipHoldSeconds), 3f);
            UIUtil.DrawRect(bar, new Color(1f, 0.5f, 0.4f));
        }
    }
}
