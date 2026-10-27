using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Kamyonun arka kapısını kapatma tuşu (K) + kalkış / polis / "hepiniz yakalandınız" sekanslarının görünüm ve sesleri.
/// Ağ nesnesi DEĞİLDİR (MuseumSession.Awake ekler); durumu MuseumSession'daki NetworkVariable'lardan okur:
/// sekans türü + sunucu saatiyle başlangıç → herkeste aynı anda oynar, geç katılan doğru yerden devam eder.
/// Sekans boyunca PlayerController.InputLocked açıktır (hareket, bakış, etkileşim durur).
/// Kapatma isteğini sunucu doğrular (MuseumSession.CloseDoorRpc).
/// </summary>
public class TruckEscapeUI : MonoBehaviour
{
    public const Key CloseKey = Key.K;

    private const float ConfirmWindow = 3f;   // kota dolmadan gitmek için ikinci basış süresi
    private const float FadeInSeconds = 1.2f; // yeni görev / koşu başlayınca karanlıktan açılış

    private float confirmUntil;   // bu ana kadar ikinci basış onaydır
    private float outsideWarnUntil;
    private string outsideWarnName = "";

    // Sekans izleme (sesler / sarsıntı / geçiş)
    private MuseumSession.EscapeSequence lastSeq;
    private float lastT;
    private float nextSiren;
    private float fadeInStart = -10f;

    private void OnDisable()
    {
        PlayerController.InputLocked = false;
        PlayerController.ShakeAmount = 0f;
    }

    private bool LocalInTruck
    {
        get
        {
            PlayerStealth me = PlayerStealth.Local;
            if (me == null || !me.IsSpawned || me.IsJailed) return false;
            return TruckZone.IsInside(me.GetComponent<PlayerNetworkSetup>());
        }
    }

    private static bool InputBlocked =>
        ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || WorkbenchUI.IsOpen || BriefingUI.IsOpen ||
        Cursor.lockState != CursorLockMode.Locked || PushSystem.LocalKnockedOut;

    private void Update()
    {
        MuseumSession s = MuseumSession.Instance;
        if (s == null || !s.IsSpawned || !s.Ready)
        {
            PlayerController.InputLocked = false;
            PlayerController.ShakeAmount = 0f;
            lastSeq = MuseumSession.EscapeSequence.None;
            return;
        }

        MuseumSession.EscapeSequence seq = s.ActiveSequence;
        if (seq != MuseumSession.EscapeSequence.None)
        {
            float t = s.SequenceTime;
            if (seq != lastSeq)
            {
                lastSeq = seq;
                lastT = t < 0.5f ? -0.01f : t; // yeni başladıysa baştan; geç katıldıysa geçmiş sesleri atla
                nextSiren = 0f;
                confirmUntil = 0f;
                CloseLocalUi(); // açık paneller sekansın altında çalışmaya devam etmesin
            }
            RunSequence(seq, lastT, t);
            lastT = t;
            PlayerController.InputLocked = true;
            PlayerController.ShakeAmount = ShakeFor(seq, t);
            return;
        }

        // Sekans yok
        if (lastSeq != MuseumSession.EscapeSequence.None)
        {
            lastSeq = MuseumSession.EscapeSequence.None;
            fadeInStart = Time.unscaledTime; // ekran karanlıktan açılır (yeni müze / yeni koşu)
        }
        PlayerController.InputLocked = false;
        PlayerController.ShakeAmount = 0f;

        if (Keyboard.current != null && Keyboard.current[CloseKey].wasPressedThisFrame) TryClose(s);
    }

    /// <summary>Sekans başlarken yerel oyuncudaki çanta / dış paneller, atölye, brifing ve sohbet yazımı kapanır.</summary>
    private static void CloseLocalUi()
    {
        PlayerInventory.ClosePanel();
        WorkbenchUI.ForceClose();
        BriefingUI.ForceClose();
        ChatUI.ForceCloseTyping();
    }

    private void TryClose(MuseumSession s)
    {
        if (InputBlocked || s.DoorClosed || !LocalInTruck) return;
        PlayerStealth me = PlayerStealth.Local;
        if (me.IsHidden || me.CurrentHidingSpot != null || me.IsKnockedOutNet) return;

        TruckZone.Status st = TruckZone.GetStatus();
        if (st.outsideCount > 0)
        {
            outsideWarnName = st.firstOutside ?? "";
            outsideWarnUntil = Time.unscaledTime + 3f;
            confirmUntil = 0f;
            return;
        }

        if (!s.QuotaMet)
        {
            // Kazara olmasın: ilk basış uyarır, 3 sn içinde ikinci basış onaylar
            if (Time.unscaledTime <= confirmUntil)
            {
                confirmUntil = 0f;
                s.RequestCloseDoor(true);
            }
            else
            {
                confirmUntil = Time.unscaledTime + ConfirmWindow;
                s.RequestCloseDoor(false); // sunucu uyarıyı kaydeder: onay ancak bundan sonra geçerli
            }
            return;
        }

        s.RequestCloseDoor(false);
    }

    // ---------------- Sekanslar: sesler ve sarsıntı ----------------

    private static bool Crossed(float from, float to, float at) => from < at && to >= at;

    private void RunSequence(MuseumSession.EscapeSequence seq, float from, float to)
    {
        switch (seq)
        {
            case MuseumSession.EscapeSequence.Departure:
                if (Crossed(from, to, 1.0f)) GameAudio.Play2D(SoundId.TruckEngine, 0.7f, 0.02f);
                break;
            case MuseumSession.EscapeSequence.Police:
                if (Crossed(from, to, 1.0f)) GameAudio.Play2D(SoundId.TruckEngine, 0.6f, 0.02f);
                if (Crossed(from, to, 4.0f)) GameAudio.Play2D(SoundId.TruckBrake, 0.9f, 0.02f);
                if (to >= 1.6f && to < 7.2f && Time.unscaledTime >= nextSiren)
                {
                    nextSiren = Time.unscaledTime + 1.6f;
                    GameAudio.Play2D(SoundId.PoliceSiren, 0.5f, 0f);
                }
                break;
            case MuseumSession.EscapeSequence.AllCaught:
                if (Crossed(from, to, 0f) || from < 0f) GameAudio.Play2D(SoundId.Caught, 0.8f, 0.02f);
                break;
        }
    }

    private static float ShakeFor(MuseumSession.EscapeSequence seq, float t)
    {
        switch (seq)
        {
            case MuseumSession.EscapeSequence.Departure:
                if (t < 1.0f || t > 6.0f) return 0f;
                return Mathf.Min(0.35f, (t - 1.0f) / 0.6f * 0.35f) * Mathf.Clamp01((6.0f - t) / 0.6f);
            case MuseumSession.EscapeSequence.Police:
                if (t < 1.0f) return 0f;
                if (t < 4.0f) return Mathf.Min(0.35f, (t - 1.0f) / 0.6f * 0.35f);   // gidiyoruz
                if (t < 4.5f) return 1f;                                            // sert fren
                return Mathf.Lerp(0.5f, 0f, Mathf.Clamp01((t - 4.5f) / 1.6f));      // sönümlenir
            default:
                return 0f;
        }
    }

    // ---------------- Görünüm ----------------

    private enum EndKind { Departure, Police, AllCaught }

    private void OnGUI()
    {
        MuseumSession s = MuseumSession.Instance;
        if (s == null || !s.IsSpawned || !s.Ready) return;
        GUI.depth = -1000; // diğer arayüzlerin üstüne çiz
        if (Event.current.type != EventType.Repaint) return;

        float black = 0f;
        MuseumSession.EscapeSequence seq = s.ActiveSequence;

        if (seq != MuseumSession.EscapeSequence.None)
        {
            float t = s.SequenceTime;
            switch (seq)
            {
                case MuseumSession.EscapeSequence.Departure:
                    black = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 4.5f) / 2.2f));
                    if (t > 1.2f) DrawResultCard(s, EndKind.Departure, UIUtil.Ease.OutCubic((t - 1.2f) / 0.45f));
                    break;
                case MuseumSession.EscapeSequence.Police:
                    DrawPoliceLights(t);
                    black = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 6.4f) / 1.7f));
                    if (t > 4.2f) DrawResultCard(s, EndKind.Police, UIUtil.Ease.OutCubic((t - 4.2f) / 0.45f));
                    break;
                case MuseumSession.EscapeSequence.AllCaught:
                    black = Mathf.Max(Mathf.Clamp01(t / 0.8f) * 0.55f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 3.4f) / 1f)));
                    DrawResultCard(s, EndKind.AllCaught, UIUtil.Ease.OutCubic((t - 0.3f) / 0.45f));
                    break;
            }
        }
        else
        {
            float f = (Time.unscaledTime - fadeInStart) / FadeInSeconds;
            if (f >= 0f && f < 1f) black = 1f - Mathf.SmoothStep(0f, 1f, f);
            DrawDoorHint(s);
        }

        if (black > 0.001f) UIUtil.Scrim(UITheme.WithAlpha(UITheme.Charcoal, black));
    }

    /// <summary>
    /// Görev sonu kartı (Catalog sayfası): görev başlığı, sonuç, ekip kazancı / kota ve kamyondakiler.
    /// Tüm sayılar MuseumSession / TruckZone gerçek durumundan gelir.
    /// </summary>
    private static void DrawResultCard(MuseumSession s, EndKind kind, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        if (alpha <= 0.01f) return;
        float sc = UITheme.Scale;
        bool good = kind == EndKind.Departure;
        UIUtil.ScrimBottom(Screen.height * 0.6f, alpha * (good ? 0.7f : 0.9f));

        string title = Loc.T(good ? "mission.complete" : kind == EndKind.Police ? "truck.police" : "truck.allcaught");
        string sub = Loc.T(good ? "truck.depart" : kind == EndKind.Police ? "truck.police2" : "truck.allcaught2");
        TextRole titleRole = good ? TextRole.Display : TextRole.Title;
        Color accent = good ? UITheme.Forest : UITheme.Danger;
        Color titleCol = good ? UITheme.Charcoal : UITheme.Bordeaux;
        bool showStats = kind != EndKind.AllCaught;
        bool showTruck = good;

        float w = Mathf.Min(560f * sc, Screen.width - 40f);
        float pad = UITheme.S5 * sc;
        float innerW = w - pad * 2f;
        float iconS = 40f * sc, gap = UITheme.S3 * sc;
        float rowH = 28f * sc;

        float titleW = innerW - iconS - gap;
        float titleH = Mathf.Max(iconS, UIUtil.MeasureHeight(title, titleRole, titleW));
        float subH = UIUtil.MeasureHeight(sub, TextRole.Body, innerW);
        float h = pad + 26f * sc + titleH + 6f * sc + subH + UITheme.S4 * sc;
        if (showStats) h += UITheme.S3 * sc + rowH + 6f * sc + 10f * sc;
        if (showTruck) h += UITheme.S2 * sc + rowH;
        h += pad;

        float slide = (1f - alpha) * 14f * sc;
        Rect card = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.4f + slide, w, h);
        UIUtil.Panel(card, PanelKind.Catalog, alpha);
        UIUtil.Corners(card, alpha);

        float x = card.x + pad, y = card.y + pad;
        // Görev başlığı (hangi görev, hangi müze)
        MuseumTheme th = s.Theme;
        UIUtil.Text(new Rect(x, y, innerW, 18f * sc), UITheme.Caps(Loc.T("mission.title", s.Level, th.name, th.City)), TextRole.Section,
            UITheme.WithAlpha(UITheme.SectionOn(PanelKind.Catalog), alpha));
        UIUtil.DrawRect(new Rect(x, y + 20f * sc, innerW, 1f), UITheme.WithAlpha(UITheme.Bordeaux, 0.4f * alpha));
        y += 26f * sc;

        UIUtil.Icon(new Rect(x, y + (titleH - iconS) * 0.5f, iconS, iconS), good ? IconId.Truck : IconId.Jail, UITheme.WithAlpha(accent, alpha));
        UIUtil.Text(new Rect(x + iconS + gap, y, titleW, titleH), title, titleRole, UITheme.WithAlpha(titleCol, alpha));
        y += titleH + 6f * sc;

        GUIStyle bs = UITheme.Style(TextRole.Body, UITheme.WithAlpha(UITheme.MutedOn(PanelKind.Catalog), alpha), TextAnchor.UpperLeft);
        GUI.Label(new Rect(x, y, innerW, subH + 2f), sub, bs);
        y += subH + UITheme.S4 * sc;

        if (showStats)
        {
            UIUtil.Divider(x, y, innerW, UITheme.WithAlpha(UITheme.Graphite, 0.5f * alpha), true);
            y += UITheme.S3 * sc;
            int earned = Mathf.Max(0, s.Earned);
            float is2 = 22f * sc;
            UIUtil.Icon(new Rect(x, y + (rowH - is2) * 0.5f, is2, is2), IconId.Money, UITheme.WithAlpha(UITheme.Bordeaux, alpha));
            UIUtil.Text(new Rect(x + is2 + gap, y, innerW - is2 - gap, rowH), Loc.T("mission.quota", earned, s.Quota), TextRole.BodyStrong,
                UITheme.WithAlpha(UITheme.Charcoal, alpha));
            y += rowH + 6f * sc;
            float ratio = s.Quota > 0 ? Mathf.Clamp01(earned / (float)s.Quota) : 0f;
            UIUtil.ProgressBar(new Rect(x, y, innerW, 10f * sc), ratio, good ? UITheme.Forest : UITheme.Danger);
            y += 10f * sc;
        }
        if (showTruck)
        {
            y += UITheme.S2 * sc;
            TruckZone.Status st = TruckZone.GetStatus();
            float is2 = 20f * sc;
            UIUtil.Icon(new Rect(x, y + (rowH - is2) * 0.5f, is2, is2), IconId.Truck, UITheme.WithAlpha(UITheme.Graphite, alpha));
            UIUtil.Text(new Rect(x + is2 + gap, y, innerW - is2 - gap, rowH), Loc.T("mission.truckinfo", st.inside, st.total, st.jailed), TextRole.Caption,
                UITheme.WithAlpha(UITheme.Graphite, alpha));
        }
    }

    /// <summary>Yanıp sönen polis ışıkları: ekranın iki kenarında yumuşak şeritler (Danger / fildişi çakar).</summary>
    private static void DrawPoliceLights(float t)
    {
        if (t < 1.6f || t > 7.4f) return;
        float ph = t * 1.8f;
        float k = ph - Mathf.Floor(ph);
        bool red = k < 0.5f;
        float local = (k % 0.5f) / 0.5f;
        float a = 0.34f * (1f - local * 0.6f) * Mathf.Clamp01((t - 1.6f) / 0.4f);
        Color c = UITheme.WithAlpha(red ? UITheme.Danger : UITheme.Ivory, a);
        const int strips = 14;
        float w = Screen.width * 0.5f / strips;
        for (int i = 0; i < strips; i++)
        {
            float fall = 1f - i / (float)strips;
            Color sc = c; sc.a *= fall * fall;
            float x = red ? i * w : Screen.width - (i + 1) * w;
            UIUtil.DrawRect(new Rect(x, 0f, w + 1f, Screen.height), sc);
        }
    }

    private void DrawDoorHint(MuseumSession s)
    {
        if (SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BriefingUI.IsOpen || PlayerInventory.IsPanelOpen || ChatUI.IsTyping) return;
        if (!LocalInTruck || s.DoorClosed) return;

        TruckZone.Status st = TruckZone.GetStatus();
        float now = Time.unscaledTime;

        if (now <= confirmUntil)
        {
            HudKit.Banner(Loc.T("truck.quotawarn"), IconId.Warning, UITheme.Danger, UITheme.DangerLight);
            return;
        }
        if (now <= outsideWarnUntil)
        {
            HudKit.Banner(Loc.T("truck.outside", outsideWarnName), IconId.Warning, UITheme.Ochre, UITheme.Ivory);
            return;
        }

        if (s.QuotaMet)
            HudKit.Banner(Loc.T("mission.complete"), IconId.Check, UITheme.Sage, UITheme.Sage);
        if (st.outsideCount > 0)
            HudKit.Banner(Loc.T("truck.closehint.wait", st.firstOutside ?? ""), IconId.Clock, UITheme.Ochre, UITheme.IvoryDim);
        else
            HudKit.Banner(Loc.T("truck.closehint"), IconId.Truck, UITheme.Brass, UITheme.Ivory);
    }
}
