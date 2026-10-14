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

    private void OnGUI()
    {
        MuseumSession s = MuseumSession.Instance;
        if (s == null || !s.IsSpawned || !s.Ready) return;
        GUI.depth = -1000; // diğer arayüzlerin üstüne çiz
        if (Event.current.type != EventType.Repaint) return;

        float black = 0f;
        MuseumSession.EscapeSequence seq = s.ActiveSequence;
        float cx = Screen.height * 0.38f;

        if (seq != MuseumSession.EscapeSequence.None)
        {
            float t = s.SequenceTime;
            switch (seq)
            {
                case MuseumSession.EscapeSequence.Departure:
                    black = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 4.5f) / 2.2f));
                    if (t > 1.6f)
                        UIUtil.DrawPrompt(Loc.T("truck.depart"), cx, new Color(0.6f, 1f, 0.65f, 1f), 32);
                    break;
                case MuseumSession.EscapeSequence.Police:
                    DrawPoliceLights(t);
                    black = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 6.4f) / 1.7f));
                    if (t > 4.2f)
                    {
                        UIUtil.DrawPrompt(Loc.T("truck.police"), cx, new Color(1f, 0.55f, 0.5f), 34);
                        UIUtil.DrawPrompt(Loc.T("truck.police2"), cx + 42f, Color.white, 20);
                    }
                    break;
                case MuseumSession.EscapeSequence.AllCaught:
                    black = Mathf.Max(Mathf.Clamp01(t / 0.8f) * 0.55f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 3.4f) / 1f)));
                    UIUtil.DrawPrompt(Loc.T("truck.allcaught"), cx, new Color(1f, 0.6f, 0.45f), 36);
                    UIUtil.DrawPrompt(Loc.T("truck.allcaught2"), cx + 44f, Color.white, 20);
                    break;
            }
        }
        else
        {
            float f = (Time.unscaledTime - fadeInStart) / FadeInSeconds;
            if (f >= 0f && f < 1f) black = 1f - Mathf.SmoothStep(0f, 1f, f);
            DrawDoorHint(s);
        }

        if (black > 0.001f) UIUtil.DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.03f, 0.03f, 0.05f, black));
    }

    /// <summary>Kırmızı-mavi yanıp sönen polis ışıkları: ekranın iki kenarında yumuşak şeritler.</summary>
    private static void DrawPoliceLights(float t)
    {
        if (t < 1.6f || t > 7.4f) return;
        float ph = t * 1.8f;
        float k = ph - Mathf.Floor(ph);
        bool red = k < 0.5f;
        float local = (k % 0.5f) / 0.5f;
        float a = 0.34f * (1f - local * 0.6f) * Mathf.Clamp01((t - 1.6f) / 0.4f);
        Color c = red ? new Color(1f, 0.15f, 0.2f, a) : new Color(0.2f, 0.4f, 1f, a);
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
            UIUtil.DrawPrompt(Loc.T("truck.quotawarn"), UIUtil.TopSlot(34f), new Color(1f, 0.5f, 0.4f), 22);
            return;
        }
        if (now <= outsideWarnUntil)
        {
            UIUtil.DrawPrompt(Loc.T("truck.outside", outsideWarnName), UIUtil.TopSlot(34f), new Color(1f, 0.75f, 0.35f), 22);
            return;
        }

        if (s.QuotaMet)
        {
            UIUtil.DrawPrompt(Loc.T("mission.complete"), UIUtil.TopSlot(34f), new Color(0.55f, 1f, 0.6f), 26);
        }
        if (st.outsideCount > 0)
            UIUtil.DrawPrompt(Loc.T("truck.closehint.wait", st.firstOutside ?? ""), UIUtil.TopSlot(26f), new Color(1f, 0.85f, 0.4f), 16);
        else
            UIUtil.DrawPrompt(Loc.T("truck.closehint"), UIUtil.TopSlot(26f), Color.white, 16);
    }
}
