using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ana menü + oda menüsü + duraklat menüsü (OnGUI; tasarım sisteminin REFERANS uygulaması — UI_DESIGN.md §Ana menü).
///  - Ana menü: oyunun gerçek sahnesi (menü kamerası yavaşça salınır) + vinyet + soldan kömür perde;
///    solda katalog dizini gibi numaralı menü (Tek Oyunculu, Çok Oyunculu, Öğretici, Ayarlar, Yapımcılar, Çıkış),
///    sağ altta "sanatçı" sergi etiketi (isim, renk, kıyafet). Klavye: ↑/↓ seç, Enter onayla, Esc geri.
///  - Çok oyunculu: oda kur / koda göre katıl.  Ayarlar: ses, fare hassasiyeti, dil, öğretici.  Yapımcılar: katalog sayfası.
///  - Oyun içi: sol üstte oda kodu, sağ üstte ping.
///  - Esc menüsü: oyuncu listesi (host için [At]), renk, kıyafet, ses ve fare hassasiyeti, dil, öğretici, sesli sohbet ayarları, ayrıl.
/// Sahnede herhangi bir objeye (ör. NetworkManager objesine) ekle.
/// </summary>
public class SessionMenuUI : MonoBehaviour
{
    [Tooltip("Menüdeyken sahneyi gösteren kamera. Oyuncu doğunca kapatılır.")]
    [SerializeField] private GameObject menuCamera;

    [Tooltip("Oyunun adı (menü başlığı). Çevrilmez.")]
    [SerializeField] private string gameTitle = "SAHTE ŞAHESER";

    [Tooltip("Yapımcılar ekranındaki ad (çevrilmez).")]
    [SerializeField] private string creditsName = "Mustafa";

    [Tooltip("Menü kamerasının yavaş salınımı (derece). 0 = sabit kamera.")]
    [SerializeField] private float menuCameraSway = 3f;

    /// <summary>Esc menüsü açık mı? (Sohbet bu sırada açılmaz.)</summary>
    public static bool IsPaused { get; private set; }

    private string joinCode = "";
    private Vector2 pauseScroll;
    private string nameInput;
    private bool localPlayerSpawned;
    private PlayerNetworkSetup localPlayer;

    // Ping
    private float nextPingUpdate;
    private string pingText = "";
    private Color pingColor = Color.white;

    // Atılacak oyuncu (liste çizilirken listeyi değiştirmemek için çizimden sonra uygulanır)
    private ulong? pendingKick;

    // Ana menü durumu
    private enum MenuScreen { Home, Multi, Settings, Credits }
    private MenuScreen screen = MenuScreen.Home;
    private float screenShownAt;
    private int focus;                 // klavye odağı (ekrandaki öğe sırası)
    private bool quitConfirm;
    private bool focusCodeField;
    private Vector2 lastMouse = new Vector2(-1f, -1f);
    private bool mouseMoved;

    // Menü kamerası salınımı
    private Quaternion menuCamBase;
    private bool menuCamBaseSet;

    private GUIStyle labelStyle, smallStyle, sectionStyle, smallButtonStyle, fieldStyle, codeFieldStyle, codeStyle, pingStyle;

    private static readonly int NavHash = "SessionMenuUI.home".GetHashCode();
    private static readonly string[] HomeKeys = { "menu.single", "menu.multi", "menu.tutorial", "menu.settings", "menu.credits", "menu.quit" };
    private static readonly string[] HomeDesc = { "menu.single.desc", "menu.multi.desc", "menu.tutorial.desc", "menu.settings.desc", "menu.credits.desc", "menu.quit.desc" };
    private const string NameControl = "SessionMenu.Name", CodeControl = "SessionMenu.Code";

    private void OnEnable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned += OnLocalPlayerSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned += OnLocalPlayerDespawned;
    }

    private void OnDisable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned -= OnLocalPlayerDespawned;
        IsPaused = false;
    }

    private void Start()
    {
        nameInput = PlayerNetworkSetup.SavedName;
        SetMenuCamera(true);
        ShowCursor(true);
        screenShownAt = Time.unscaledTime;
    }

    private void OnLocalPlayerSpawned(PlayerNetworkSetup player)
    {
        localPlayer = player;
        localPlayerSpawned = true;
        IsPaused = false;
        quitConfirm = false;
        SetMenuCamera(false);
    }

    private void OnLocalPlayerDespawned()
    {
        localPlayer = null;
        localPlayerSpawned = false;
        IsPaused = false;
        GoTo(MenuScreen.Home);
        SetMenuCamera(true);
        ShowCursor(true);
    }

    private void Update()
    {
        UpdatePing();
        SwayMenuCamera();

        if (!localPlayerSpawned || Keyboard.current == null) return;
        if (ChatUI.IsTyping || ChatUI.ClosedRecently) return; // Esc sohbeti kapatıyor, menüyü açmasın
        if (WorkbenchUI.IsOpen || BriefingUI.IsOpen) return;  // Esc atölyeyi / brifingi kapatıyor

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Çanta açıksa Esc önce çantayı kapatır.
            if (PlayerInventory.IsPanelOpen)
            {
                PlayerInventory.ClosePanel();
                return;
            }
            SetPaused(!IsPaused);
        }
    }

    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        ShowCursor(paused);
        if (!paused) GameSettings.Save();
        pauseScroll = Vector2.zero;
    }

    private void SetMenuCamera(bool active)
    {
        if (menuCamera == null) return;
        if (!active && menuCamBaseSet) menuCamera.transform.localRotation = menuCamBase;
        menuCamera.SetActive(active);
    }

    /// <summary>Menüdeyken arka plandaki gerçek sahne çok yavaş salınır (yalnızca menü kamerasının dönüşü).</summary>
    private void SwayMenuCamera()
    {
        if (localPlayerSpawned || menuCamera == null || menuCameraSway <= 0f || !menuCamera.activeInHierarchy) return;
        Transform t = menuCamera.transform;
        if (!menuCamBaseSet) { menuCamBase = t.localRotation; menuCamBaseSet = true; }
        float tt = Time.unscaledTime;
        float yaw = Mathf.Sin(tt * 0.09f) * menuCameraSway;
        float pitch = Mathf.Sin(tt * 0.061f + 1.3f) * menuCameraSway * 0.2f;
        t.localRotation = menuCamBase * Quaternion.Euler(pitch, yaw, 0f);
    }

    private static void ShowCursor(bool show)
    {
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = show;
    }

    private void GoTo(MenuScreen s)
    {
        screen = s;
        focus = 0;
        screenShownAt = Time.unscaledTime;
        GUIUtility.keyboardControl = 0;
    }

    // ---------------- Ping ----------------

    private void UpdatePing()
    {
        if (Time.unscaledTime < nextPingUpdate) return;
        nextPingUpdate = Time.unscaledTime + 0.5f;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            pingText = "";
            return;
        }

        if (nm.IsHost)
        {
            pingText = Loc.T("ping.host");
            pingColor = UITheme.Sage;
            return;
        }

        if (!nm.IsConnectedClient)
        {
            pingText = Loc.T("hud.connecting");
            pingColor = UITheme.IvoryDim;
            return;
        }

        ulong rtt = nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        pingText = Loc.T("ping.value", rtt);
        pingColor = rtt < 80 ? UITheme.Sage : rtt < 150 ? UITheme.Ochre : UITheme.DangerLight;
    }

    // ---------------- Çizim ----------------

    private void OnGUI()
    {
        SessionManager sm = SessionManager.Instance;
        if (sm == null) return;

        EnsureStyles();

        if (!sm.InGame)
        {
            DrawFrontEnd(sm);
            return;
        }

        DrawHud(sm);

        if (!localPlayerSpawned)
        {
            DrawCenteredMessage(Loc.T("hud.connecting"));
        }
        else if (IsPaused)
        {
            DrawPauseMenu(sm);
        }

        if (pendingKick.HasValue)
        {
            KickPlayer(pendingKick.Value);
            pendingKick = null;
        }
    }

    // =====================================================================================
    //  ANA MENÜ (front-end)
    // =====================================================================================

    private void DrawFrontEnd(SessionManager sm)
    {
        float sc = UITheme.Scale;
        float W = Screen.width, H = Screen.height;
        Event e = Event.current;
        if (e.type == EventType.Repaint)
        {
            mouseMoved = (e.mousePosition - lastMouse).sqrMagnitude > 0.5f;
            lastMouse = e.mousePosition;
        }

        // 1) Arka plan: oyunun gerçek sahnesi (menü kamerası) + vinyet + soldan perde.
        //    Kamera yoksa kömür "atölye duvarı" + eskiz ızgarası.
        bool sceneVisible = menuCamera != null && menuCamera.activeInHierarchy;
        if (!sceneVisible)
        {
            UIUtil.DrawRect(new Rect(0f, 0f, W, H), UITheme.Charcoal);
            UIUtil.Tile(new Rect(0f, 0f, W, H), UITheme.Tex("Textures/tex_sketch_grid"), new Color(1f, 1f, 1f, 0.6f));
        }
        UIUtil.Vignette(sceneVisible ? 0.9f : 0.6f);

        float colX = Mathf.Round(Mathf.Max(28f, W * 0.065f));
        float colW = Mathf.Round(Mathf.Min(430f * sc, W - colX * 2f));
        UIUtil.ScrimLeft(Mathf.Min(W, Mathf.Max(colX + colW + 260f * sc, W * 0.58f)));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = !quitConfirm;

        // 2) Başlık bloğu: katalog numarası, oyun adı (serif), bordo+pirinç çizgi, slogan
        float y = Mathf.Round(Mathf.Max(32f * sc, H * 0.08f));
        UIUtil.Text(new Rect(colX, y, W * 0.5f, 18f * sc), UITheme.Caps(Loc.T("menu.catalogno")), TextRole.Caption, UITheme.WithAlpha(UITheme.BrassLight, 0.85f));
        DrawLanguageTabs(W - colX, y - 6f * sc);
        y += 24f * sc;
        y += DrawFitted(new Rect(colX, y, Mathf.Min(W - colX * 2f, 900f * sc), 66f * sc), gameTitle, TextRole.Display, UITheme.Ivory);
        y += 6f * sc;
        UIUtil.DrawRect(new Rect(colX, y, 72f * sc, 2f), UITheme.Bordeaux);
        UIUtil.DrawRect(new Rect(colX + 72f * sc, y, 24f * sc, 2f), UITheme.Brass);
        y += 12f * sc;
        UIUtil.Text(new Rect(colX, y, colW, 24f * sc), Loc.T("menu.tagline"), TextRole.Body, UITheme.IvoryDim);
        y += 44f * sc;

        // 3) Ekran içeriği (0.3 sn soldan kayarak + belirerek gelir)
        float t = UIUtil.Ease.OutCubic(Mathf.Clamp01((Time.unscaledTime - screenShownAt) / UITheme.MotionSlow));
        Color oldColor = GUI.color;
        GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * t);
        float cx = colX - (1f - t) * 16f;

        GUI.enabled = !quitConfirm && !sm.IsBusy;
        switch (screen)
        {
            case MenuScreen.Home: DrawHome(sm, cx, y, colW); break;
            case MenuScreen.Multi: DrawMulti(sm, cx, y, colW); break;
            case MenuScreen.Settings: DrawSettingsScreen(cx, y, colW); break;
            case MenuScreen.Credits: DrawCredits(cx, y, colW); break;
        }
        GUI.color = oldColor;
        GUI.enabled = !quitConfirm;

        if (screen == MenuScreen.Home) DrawArtistLabel(W - colX, H - 64f * sc, colX + colW + 32f * sc);

        // 4) Bağlantı geri bildirimi + klavye ipuçları
        DrawStatusLine(sm, colX, colW, H - 108f * sc);
        DrawFooterHints(colX, H - 36f * sc);

        GUI.enabled = wasEnabled;

        // 5) Çıkış onayı (en üstte)
        if (quitConfirm)
        {
            DialogChoice c = UIUtil.Dialog(Loc.T("menu.quit.title"), Loc.T("menu.quit.body"), Loc.T("menu.quit"), Loc.T("menu.quit.stay"), true);
            if (c == DialogChoice.Confirm)
            {
                quitConfirm = false;
                GameSettings.Save();
                Application.Quit();
            }
            else if (c == DialogChoice.Cancel) quitConfirm = false;
        }
    }

    /// <summary>Ana ekran: katalog dizini gibi numaralı menü; odaklı öğenin açıklaması altında (aşamalı açıklama).</summary>
    private void DrawHome(SessionManager sm, float x, float y, float w)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        Event e = Event.current;

        if (interactive)
        {
            // İsim alanında Enter: alanı bırak
            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == NameControl &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
            {
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
            focus = UIUtil.NavigateVertical(focus, HomeKeys.Length);
            if (Submit()) ActivateHome(sm, focus);
            else if (GUIUtility.keyboardControl == 0 && UIUtil.EscapePressed()) quitConfirm = true;
        }

        float rowH = Mathf.Round(50f * sc);
        for (int i = 0; i < HomeKeys.Length; i++)
        {
            Rect row = new Rect(x - 18f * sc, y + i * rowH, w + 18f * sc, rowH);
            if (interactive && mouseMoved && row.Contains(e.mousePosition)) focus = i;
            bool f = focus == i;
            bool danger = i == HomeKeys.Length - 1;
            float ft = UIUtil.Ease.OutCubic(UIUtil.Tween(NavHash + i, f, UITheme.MotionFast));

            UIUtil.DrawRect(new Rect(row.x + 4f, row.y + 4f * sc, row.width - 4f, rowH - 8f * sc), UITheme.WithAlpha(UITheme.Charcoal, 0.5f * ft));
            UIUtil.DrawRect(new Rect(row.x, row.y + 8f * sc, 4f, rowH - 16f * sc), UITheme.WithAlpha(danger ? UITheme.Danger : UITheme.Bordeaux, ft));

            float tx = x + 6f * sc * ft;
            UIUtil.Text(new Rect(tx, row.y, 34f * sc, rowH), (i + 1).ToString("00"), TextRole.Mono,
                f ? UITheme.BrassLight : UITheme.WithAlpha(UITheme.Brass, 0.7f));
            Color rest = UITheme.WithAlpha(UITheme.IvoryDim, interactive ? 0.8f : 0.4f);
            Color lc = Color.Lerp(rest, danger ? UITheme.DangerLight : UITheme.Ivory, ft);
            UIUtil.Text(new Rect(tx + 40f * sc, row.y, w - 80f * sc, rowH), Loc.T(HomeKeys[i]), TextRole.Title, lc);
            if (ft > 0.01f)
                UIUtil.Icon(new Rect(row.xMax - 30f * sc, row.center.y - 9f * sc, 18f * sc, 18f * sc), IconId.Chevron, UITheme.WithAlpha(UITheme.BrassLight, ft));

            if (interactive && GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                focus = i;
                ActivateHome(sm, i);
            }
        }

        y += HomeKeys.Length * rowH + 12f * sc;
        UIUtil.DrawRect(new Rect(x, y, 24f * sc, 1f), UITheme.Brass);
        UIUtil.Text(new Rect(x, y + 8f * sc, w, 44f * sc), Loc.T(HomeDesc[Mathf.Clamp(focus, 0, HomeDesc.Length - 1)]),
            TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperLeft);
    }

    private void ActivateHome(SessionManager sm, int i)
    {
        switch (i)
        {
            case 0: sm.StartSinglePlayer(); break;
            case 1: GoTo(MenuScreen.Multi); break;
            case 2:
                // Öğretici tek oyunculu başlar (kayıt -> kamyon -> çizim akışı tek kişiyle de çalışır)
                TutorialUI.Restart();
                sm.StartSinglePlayer();
                break;
            case 3: GoTo(MenuScreen.Settings); break;
            case 4: GoTo(MenuScreen.Credits); break;
            default: quitConfirm = true; break;
        }
    }

    /// <summary>Çok oyunculu: oda kur / koda göre katıl. Odak sırası: 0 Oda Kur, 1 kod alanı, 2 Katıl, 3 Geri.</summary>
    private void DrawMulti(SessionManager sm, float x, float y, float w)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        Event e = Event.current;
        bool codeFocused = GUI.GetNameOfFocusedControl() == CodeControl;
        if (codeFocused) focus = 1;

        if (interactive && e.type == EventType.KeyDown)
        {
            if (codeFocused && (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow))
            {
                focus = e.keyCode == KeyCode.UpArrow ? 0 : 2;
                GUIUtility.keyboardControl = 0;
                e.Use();
            }
            else if (codeFocused && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (joinCode.Length > 0) _ = sm.JoinRoomAsync(joinCode);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                e.Use();
                GoTo(MenuScreen.Home);
                return;
            }
        }
        if (interactive)
        {
            int nf = UIUtil.NavigateVertical(focus, 4);
            if (nf != focus) { focus = nf; focusCodeField = focus == 1; }
        }

        y += UIUtil.SectionHeader(new Rect(x, y, w, 0f), Loc.T("menu.multi"));
        y += 6f * sc;
        float bh = Mathf.Round(48f * sc);
        if (UIUtil.Button(new Rect(x, y, w, bh), Loc.T("menu.create"), ButtonKind.Primary, interactive, focus == 0, IconId.Players))
            _ = sm.CreateRoomAsync();
        y += bh + 6f * sc;
        UIUtil.Text(new Rect(x, y, w, 36f * sc), Loc.T("menu.create.desc"), TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += 40f * sc;

        UIUtil.Divider(x, y, w, UITheme.WithAlpha(UITheme.Brass, 0.55f), true);
        y += 16f * sc;

        UIUtil.Text(new Rect(x, y, w, 18f * sc), UITheme.Caps(Loc.T("menu.joincode")), TextRole.Section, UITheme.Brass);
        y += 22f * sc;
        Rect field = new Rect(x, y, w, Mathf.Round(48f * sc));
        GUI.SetNextControlName(CodeControl);
        joinCode = GUI.TextField(field, joinCode, 12, codeFieldStyle).ToUpperInvariant();
        if (focusCodeField && interactive) { GUI.FocusControl(CodeControl); focusCodeField = false; }
        if (focus == 1 && !codeFocused && interactive) UIUtil.FocusRing(field);
        y += field.height + 10f * sc;

        if (UIUtil.Button(new Rect(x, y, w, bh), Loc.T("menu.join"), ButtonKind.Primary, interactive && joinCode.Length > 0, focus == 2, IconId.Door))
            _ = sm.JoinRoomAsync(joinCode);
        y += bh + 18f * sc;

        if (UIUtil.Button(new Rect(x, y, Mathf.Min(w, 160f * sc), Mathf.Round(38f * sc)), Loc.T("menu.back"), ButtonKind.Secondary, interactive, focus == 3))
            GoTo(MenuScreen.Home);
    }

    /// <summary>Ayarlar (GameSettings + dil + öğretici). Odak: 0 ses, 1 hassasiyet, 2 dil, 3 öğretici, 4 geri. ←/→ ayarlar.</summary>
    private void DrawSettingsScreen(float x, float y, float w)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        if (interactive)
        {
            if (UIUtil.EscapePressed()) { GameSettings.Save(); GoTo(MenuScreen.Home); return; }
            focus = UIUtil.NavigateVertical(focus, 5);
            int h = UIUtil.NavigateHorizontal();
            if (h != 0)
            {
                if (focus == 0) GameSettings.MasterVolume = Mathf.Round((GameSettings.MasterVolume + 0.05f * h) * 20f) / 20f;
                else if (focus == 1) GameSettings.MouseSensitivity = GameSettings.MouseSensitivity + 0.1f * h;
                else if (focus == 2) Loc.Current = (Language)(((int)Loc.Current + h + Loc.LanguageNames.Length) % Loc.LanguageNames.Length);
            }
        }

        float rowH = Mathf.Round(46f * sc);

        y += UIUtil.SectionHeader(new Rect(x, y, w, 0f), Loc.T("settings.sound"));
        Rect volRow = new Rect(x, y, w, rowH);
        UIUtil.Text(new Rect(x, y, w, 22f * sc), Loc.T("pause.volume", Mathf.RoundToInt(GameSettings.MasterVolume * 100f)), TextRole.Body, UITheme.Ivory);
        float vol = GUI.HorizontalSlider(new Rect(x, y + 26f * sc, w, 14f), GameSettings.MasterVolume, 0f, 1f);
        if (!Mathf.Approximately(vol, GameSettings.MasterVolume)) { GameSettings.MasterVolume = vol; focus = 0; }
        if (focus == 0 && interactive) UIUtil.FocusRing(new Rect(volRow.x - 6f, volRow.y - 2f, volRow.width + 12f, volRow.height));
        y += rowH + 10f * sc;

        y += UIUtil.SectionHeader(new Rect(x, y, w, 0f), Loc.T("settings.controls"));
        Rect sensRow = new Rect(x, y, w, rowH);
        UIUtil.Text(new Rect(x, y, w, 22f * sc), Loc.T("pause.sensitivity", GameSettings.MouseSensitivity.ToString("0.0")), TextRole.Body, UITheme.Ivory);
        float sens = GUI.HorizontalSlider(new Rect(x, y + 26f * sc, w, 14f), GameSettings.MouseSensitivity, GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        if (!Mathf.Approximately(sens, GameSettings.MouseSensitivity)) { GameSettings.MouseSensitivity = sens; focus = 1; }
        if (focus == 1 && interactive) UIUtil.FocusRing(new Rect(sensRow.x - 6f, sensRow.y - 2f, sensRow.width + 12f, sensRow.height));
        y += rowH + 10f * sc;

        y += UIUtil.SectionHeader(new Rect(x, y, w, 0f), Loc.T("pause.language"));
        float lbh = Mathf.Round(34f * sc), lx = x;
        Rect langRow = new Rect(x, y, 0f, lbh);
        for (int i = 0; i < Loc.LanguageNames.Length; i++)
        {
            float bw = UIUtil.Measure(Loc.LanguageNames[i], TextRole.Button).x + 32f;
            bool sel = (int)Loc.Current == i;
            if (UIUtil.Button(new Rect(lx, y, bw, lbh), Loc.LanguageNames[i], ButtonKind.Tool, interactive, false, null, sel) && !sel)
                Loc.Current = (Language)i;
            lx += bw + 8f;
        }
        langRow.width = lx - x - 8f;
        if (focus == 2 && interactive) UIUtil.FocusRing(langRow);
        y += lbh + 16f * sc;

        y += UIUtil.SectionHeader(new Rect(x, y, w, 0f), Loc.T("pause.tutorial"));
        UIUtil.Text(new Rect(x, y, w, 22f * sc), Loc.T(TutorialUI.Completed ? "menu.tutorial.done" : "menu.tutorial.pending"), TextRole.Caption, UITheme.IvoryDim);
        y += 24f * sc;
        if (UIUtil.Button(new Rect(x, y, w, Mathf.Round(36f * sc)), Loc.T("menu.tutorial.reset"), ButtonKind.Secondary, interactive, focus == 3))
            TutorialUI.Restart(); // Tek oyunculu ya da çok oyunculu fark etmez: oyuna girince başlar
        y += 36f * sc + 22f * sc;

        if (UIUtil.Button(new Rect(x, y, Mathf.Min(w, 160f * sc), Mathf.Round(38f * sc)), Loc.T("menu.back"), ButtonKind.Secondary, interactive, focus == 4))
        {
            GameSettings.Save();
            GoTo(MenuScreen.Home);
        }
    }

    /// <summary>Yapımcılar: katalog sayfası (açık yüzey, kömür yazı).</summary>
    private void DrawCredits(float x, float y, float w)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        if (interactive && (UIUtil.EscapePressed() || Submit())) { GoTo(MenuScreen.Home); return; }

        float pad = 22f * sc, iw = w - pad * 2f;
        string[] roles = { Loc.T("credits.made"), Loc.T("credits.tech"), Loc.T("credits.thanks") };
        string[] values = { creditsName, "Unity 6  ·  Netcode for GameObjects  ·  Unity Relay", Loc.T("credits.thanks.body") };
        float h = pad + 40f * sc + 16f * sc;
        for (int i = 0; i < roles.Length; i++) h += 18f * sc + UIUtil.MeasureHeight(values[i], TextRole.Body, iw) + 12f * sc;
        string disc = Loc.T("credits.disclaimer");
        h += 18f * sc + UIUtil.MeasureHeight(disc, TextRole.Caption, iw) + pad;

        Rect page = new Rect(x, y, w, h);
        UIUtil.Shadow(page, 0.8f);
        UIUtil.Panel(page, PanelKind.Catalog);
        float py = page.y + pad;
        UIUtil.Text(new Rect(page.x + pad, py, iw, 40f * sc), Loc.T("credits.title"), TextRole.Title, UITheme.Charcoal);
        py += 40f * sc;
        UIUtil.DrawRect(new Rect(page.x + pad, py, 48f * sc, 2f), UITheme.Bordeaux);
        py += 16f * sc;
        for (int i = 0; i < roles.Length; i++)
        {
            UIUtil.Text(new Rect(page.x + pad, py, iw, 16f * sc), UITheme.Caps(roles[i]), TextRole.Section, UITheme.Bordeaux);
            py += 18f * sc;
            float vh = UIUtil.MeasureHeight(values[i], TextRole.Body, iw);
            UIUtil.Text(new Rect(page.x + pad, py, iw, vh), values[i], TextRole.Body, UITheme.Charcoal, TextAnchor.UpperLeft);
            py += vh + 12f * sc;
        }
        UIUtil.Divider(page.x + pad, py + 4f * sc, iw, UITheme.WithAlpha(UITheme.Graphite, 0.5f), true);
        py += 18f * sc;
        GUIStyle cs = UITheme.Style(TextRole.Caption, UITheme.Graphite, TextAnchor.UpperLeft);
        FontStyle oldFs = cs.fontStyle; cs.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(page.x + pad, py, iw, UIUtil.MeasureHeight(disc, TextRole.Caption, iw)), disc, cs);
        cs.fontStyle = oldFs;

        if (UIUtil.Button(new Rect(x, page.yMax + 18f * sc, Mathf.Min(w, 160f * sc), Mathf.Round(38f * sc)), Loc.T("menu.back"), ButtonKind.Secondary, interactive, true))
            GoTo(MenuScreen.Home);
    }

    /// <summary>
    /// Sağ altta "sanatçı" sergi etiketi: isim, renk, kıyafet (oyuncu kimliği). right/bottom: sağ alt köşe.
    /// minX: menü sütununun bittiği yer (dar ekranda etiket sütunun üstüne binmesin diye gizlenir).
    /// </summary>
    private void DrawArtistLabel(float right, float bottom, float minX)
    {
        float sc = UITheme.Scale;
        float w = Mathf.Round(310f * sc), pad = Mathf.Round(16f * sc);
        float h = pad + 18f * sc + 38f * sc + 22f * sc + 18f * sc + 30f * sc + 18f * sc + 32f * sc + pad;
        Rect r = new Rect(right - w, bottom - h, w, h);
        if (r.x < minX) return; // yer yok: kimlik zaten Esc menüsünden de değiştirilebilir
        bool interactive = GUI.enabled;

        UIUtil.Shadow(r, 0.7f);
        UIUtil.Panel(r, PanelKind.Label);
        float ix = r.x + pad, iw = r.width - pad * 2f, y = r.y + pad;

        int colorIndex = PlayerNetworkSetup.SavedColorIndex;
        UIUtil.Text(new Rect(ix, y, iw, 16f * sc), UITheme.Caps(Loc.T("menu.artist")), TextRole.Section, UITheme.Bordeaux);
        UIUtil.Text(new Rect(ix, y, iw, 16f * sc), "No. " + (colorIndex + 1).ToString("00"), TextRole.Mono, UITheme.Graphite, TextAnchor.MiddleRight);
        y += 20f * sc;

        Rect nameRect = new Rect(ix, y, iw, Mathf.Round(34f * sc));
        GUI.SetNextControlName(NameControl);
        string newName = GUI.TextField(nameRect, nameInput ?? "", 20, fieldStyle);
        if (newName != nameInput)
        {
            nameInput = newName;
            PlayerNetworkSetup.SavedName = newName;
        }
        y += 38f * sc;

        GUIStyle med = UITheme.Style(TextRole.Caption, UITheme.Graphite);
        FontStyle oldFs = med.fontStyle; med.fontStyle = FontStyle.Italic;
        GUI.Label(new Rect(ix, y, iw, 18f * sc), (string.IsNullOrEmpty(nameInput) ? Loc.T("menu.artist.unknown") + ", " : "") + Loc.T("menu.artist.medium"), med);
        med.fontStyle = oldFs;
        y += 22f * sc;

        UIUtil.Text(new Rect(ix, y, iw, 16f * sc), Loc.T("menu.color"), TextRole.Caption, UITheme.Graphite);
        y += 18f * sc;
        int picked = DrawSwatches(new Rect(ix, y, iw, 26f * sc), colorIndex, 26f * sc, true, interactive);
        if (picked != colorIndex) PlayerNetworkSetup.SavedColorIndex = picked;
        y += 30f * sc;

        UIUtil.Text(new Rect(ix, y, iw, 16f * sc), Loc.T("menu.outfit"), TextRole.Caption, UITheme.Graphite);
        y += 18f * sc;
        int packed = PlayerNetworkSetup.SavedOutfit;
        int hat = PlayerNetworkSetup.OutfitHat(packed), face = PlayerNetworkSetup.OutfitFace(packed), build = PlayerNetworkSetup.OutfitBuild(packed);
        float bw = (iw - 12f) / 3f, bh = Mathf.Round(30f * sc);
        if (UIUtil.Button(new Rect(ix, y, bw, bh), Loc.T("outfit.hat." + hat), ButtonKind.Secondary, interactive)) hat = (hat + 1) % PlayerNetworkSetup.HatCount;
        if (UIUtil.Button(new Rect(ix + bw + 6f, y, bw, bh), Loc.T("outfit.face." + face), ButtonKind.Secondary, interactive)) face = (face + 1) % PlayerNetworkSetup.FaceCount;
        if (UIUtil.Button(new Rect(ix + (bw + 6f) * 2f, y, bw, bh), Loc.T("outfit.build." + build), ButtonKind.Secondary, interactive)) build = (build + 1) % PlayerNetworkSetup.BuildCount;
        int outfit = PlayerNetworkSetup.PackOutfit(hat, face, build);
        if (outfit != packed) PlayerNetworkSetup.SavedOutfit = outfit;
    }

    /// <summary>Sağ üstte dil sekmeleri (Tool düğmeleri; seçili olan pirinç). right: sağ kenar.</summary>
    private void DrawLanguageTabs(float right, float y)
    {
        float sc = UITheme.Scale;
        float h = Mathf.Round(30f * sc), x = right;
        for (int i = Loc.LanguageNames.Length - 1; i >= 0; i--)
        {
            float bw = UIUtil.Measure(Loc.LanguageNames[i], TextRole.Button).x + 28f;
            x -= bw;
            bool sel = (int)Loc.Current == i;
            if (UIUtil.Button(new Rect(x, y, bw, h), Loc.LanguageNames[i], ButtonKind.Tool, GUI.enabled, false, null, sel) && !sel)
                Loc.Current = (Language)i;
            x -= 6f;
        }
    }

    /// <summary>Bağlantı / işlem geri bildirimi: koyu plaket şerit, solda renk çizgisi + simge (meşgulken saat, nabızlı).</summary>
    private void DrawStatusLine(SessionManager sm, float x, float w, float y)
    {
        string status = sm.Status;
        if (sm.IsBusy && string.IsNullOrEmpty(status)) status = Loc.T("menu.busy");
        if (string.IsNullOrEmpty(status)) return;

        float sc = UITheme.Scale;
        float textX = 44f * sc;
        float th = UIUtil.MeasureHeight(status, TextRole.Body, w - textX - 12f);
        Rect r = new Rect(x, y - Mathf.Max(0f, th - 20f * sc), w, Mathf.Max(40f * sc, th + 16f));
        UIUtil.Panel(r, PanelKind.Plaque, 0.95f);
        Color accent = sm.IsBusy ? UITheme.WithAlpha(UITheme.BrassLight, 0.55f + 0.45f * UIUtil.Pulse(4f)) : UITheme.Ochre;
        UIUtil.DrawRect(new Rect(r.x + 5f, r.y + 6f, 3f, r.height - 12f), accent);
        UIUtil.Icon(new Rect(r.x + 16f * sc, r.center.y - 10f * sc, 20f * sc, 20f * sc), sm.IsBusy ? IconId.Clock : IconId.Warning, accent);
        GUIStyle s = UITheme.Style(TextRole.Body, UITheme.Ivory);
        bool wrap = s.wordWrap; s.wordWrap = true;
        GUI.Label(new Rect(r.x + textX, r.y, r.width - textX - 12f, r.height), status, s);
        s.wordWrap = wrap;
    }

    /// <summary>Alt sol: klavye ipuçları (tuş kapağı + etiket).</summary>
    private void DrawFooterHints(float x, float y)
    {
        float sc = UITheme.Scale;
        int fs = Mathf.RoundToInt(14f * sc);
        float h = Mathf.Round(24f * sc);
        Hint(ref x, y, h, fs, "↑↓", Loc.T("nav.select"));
        if (screen == MenuScreen.Settings) Hint(ref x, y, h, fs, "←→", Loc.T("nav.adjust"));
        Hint(ref x, y, h, fs, "Enter", Loc.T("nav.confirm"));
        if (screen != MenuScreen.Home) Hint(ref x, y, h, fs, "Esc", Loc.T("nav.back"));
    }

    private static void Hint(ref float x, float y, float h, int fs, string key, string label)
    {
        float kw = UIUtil.KeyCapWidth(key, fs);
        UIUtil.KeyCap(new Rect(x, y, kw, h), key, fs);
        x += kw + 6f;
        float lw = UIUtil.Measure(label, TextRole.Caption).x;
        UIUtil.Text(new Rect(x, y, lw + 4f, h), label, TextRole.Caption, UITheme.IvoryDim);
        x += lw + 18f;
    }

    /// <summary>Metni rol boyutunda, sığmazsa küçülterek çizer (en az %60). Kullandığı yüksekliği döner.</summary>
    private static float DrawFitted(Rect r, string text, TextRole role, Color c)
    {
        GUIStyle s = UITheme.Style(role, c);
        int baseSize = s.fontSize;
        float tw = s.CalcSize(new GUIContent(text)).x;
        if (tw > r.width && tw > 0f) s.fontSize = Mathf.Max(Mathf.RoundToInt(baseSize * 0.6f), Mathf.FloorToInt(baseSize * r.width / tw));
        float h = Mathf.Max(r.height * s.fontSize / baseSize, s.fontSize * 1.25f);
        GUI.Label(new Rect(r.x, r.y, r.width, h), text, s);
        s.fontSize = baseSize;
        return h;
    }

    /// <summary>Enter / Space (yazı alanı odakta değilken). Olayı tüketir.</summary>
    private static bool Submit()
    {
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown || GUIUtility.keyboardControl != 0) return false;
        if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter && e.keyCode != KeyCode.Space) return false;
        e.Use();
        return true;
    }

    // =====================================================================================
    //  OYUN İÇİ: HUD + DURAKLAT
    // =====================================================================================

    private void DrawHud(SessionManager sm)
    {
        if (WorkbenchUI.IsOpen) return; // atölye panelinin üstüne binmesin
        // Sol üst köşe (en üstte); görev ve garaj panelleri altına dizilir
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.SessionCode, 270f, 64f);
        UIUtil.Panel(r, PanelKind.Plaque);
        UIUtil.Icon(new Rect(r.x + 12f, r.y + 10f, 20f, 20f), IconId.Players, UITheme.Brass);
        if (sm.ActiveSession == null)
        {
            // Tek oyunculu: oda kodu yok
            GUI.Label(new Rect(r.x + 40f, r.y + 6f, r.width - 46f, 28f), Loc.T("hud.single"), codeStyle);
            GUI.Label(new Rect(r.x + 40f, r.y + 34f, r.width - 46f, 22f), Loc.T("hud.single.menu"), smallStyle);
        }
        else
        {
            string code = sm.ActiveSession.Code;
            int count = PlayerNetworkSetup.All.Count > 0 ? PlayerNetworkSetup.All.Count
                      : (sm.ActiveSession.Players != null ? sm.ActiveSession.Players.Count : 1);
            GUI.Label(new Rect(r.x + 40f, r.y + 6f, r.width - 46f, 28f), Loc.T("hud.code", code), codeStyle);
            GUI.Label(new Rect(r.x + 40f, r.y + 34f, r.width - 46f, 22f), Loc.T("hud.players", count, sm.MaxPlayers), smallStyle);
        }

        if (!string.IsNullOrEmpty(pingText))
        {
            Rect pr = UIUtil.HudRect(UIUtil.HudCorner.TopRight, UIUtil.HudOrder.Ping, 140f, 32f);
            UIUtil.Panel(pr, PanelKind.Plaque);
            pingStyle.normal.textColor = pingColor;
            GUI.Label(pr, pingText, pingStyle);
        }
    }

    private void DrawPauseMenu(SessionManager sm)
    {
        float sc = UITheme.Scale;
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.Charcoal, 0.45f));
        UIUtil.Vignette(0.8f);

        float w = Mathf.Min(Mathf.Round(480f * sc), Screen.width - 32f);
        float h = Mathf.Min(Screen.height - 40f, Mathf.Round(860f * sc));
        Rect panel = new Rect(Mathf.Round((Screen.width - w) / 2f), Mathf.Round((Screen.height - h) / 2f), w, h);
        UIUtil.Panel(panel, PanelKind.Plaque);
        UIUtil.Corners(panel, 0.9f);

        float pad = Mathf.Round(24f * sc);
        GUILayout.BeginArea(new Rect(panel.x + pad, panel.y + 18f * sc, w - pad * 2f, h - 36f * sc));

        Rect hr = GUILayoutUtility.GetRect(10f, 40f * sc, GUILayout.ExpandWidth(true));
        UIUtil.Text(hr, Loc.T("pause.title"), TextRole.Title, UITheme.Ivory);
        Rect rule = GUILayoutUtility.GetRect(10f, 12f * sc, GUILayout.ExpandWidth(true));
        UIUtil.DrawRect(new Rect(rule.x, rule.y + 2f, 48f * sc, 2f), UITheme.Bordeaux);
        UIUtil.DrawRect(new Rect(rule.x + 48f * sc, rule.y + 2f, 16f * sc, 2f), UITheme.Brass);

        if (UIUtil.LayoutButton(Loc.T("pause.resume"), ButtonKind.Primary, Mathf.Round(44f * sc), true, true))
        {
            SetPaused(false);
        }

        // Liste uzun: ekrana sığmazsa kaydırılır (alttaki Kodu kopyala / Ayrıl düğmeleri sabit kalır)
        pauseScroll = GUILayout.BeginScrollView(pauseScroll, false, false);

        // --- Oyuncular ---
        PauseSection(Loc.T("pause.players", PlayerNetworkSetup.All.Count, sm.MaxPlayers));
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null) continue;

            GUILayout.BeginHorizontal(GUILayout.Height(30f * sc));

            Rect sw = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
            sw.y += 7f * sc;
            UIUtil.DrawRect(sw, p.PlayerColor);
            UIUtil.Outline(sw, 1f, UITheme.WithAlpha(UITheme.Ivory, 0.6f));
            GUILayout.Space(10f);

            string tags = "";
            if (p.IsHostPlayer) tags += "  " + Loc.T("pause.tag.host");
            if (p.IsOwner) tags += "  " + Loc.T("pause.tag.you");
            GUILayout.Label(p.PlayerName + tags, labelStyle, GUILayout.Height(30f * sc));

            GUILayout.FlexibleSpace();

            if (IsHost && !p.IsHostPlayer)
            {
                if (UIUtil.LayoutButton(Loc.T("pause.kick"), ButtonKind.Danger, Mathf.Round(26f * sc), true, false, null, GUILayout.Width(64f * sc)))
                {
                    pendingKick = p.OwnerClientId;
                }
            }

            GUILayout.EndHorizontal();
        }

        // --- Renk ---
        PauseSection(Loc.T("pause.color"));
        int current = PlayerNetworkSetup.SavedColorIndex;
        Rect swatchRow = GUILayoutUtility.GetRect(10f, 30f * sc, GUILayout.ExpandWidth(true));
        int picked = DrawSwatches(swatchRow, current, 30f * sc, false, true);
        if (picked != current && localPlayer != null) localPlayer.SetColor(picked);

        // --- Kıyafet ---
        PauseSection(Loc.T("pause.outfit"));
        int currentOutfit = PlayerNetworkSetup.SavedOutfit;
        int pickedOutfit = DrawOutfitPicker(currentOutfit);
        if (pickedOutfit != currentOutfit && localPlayer != null) localPlayer.SetOutfit(pickedOutfit);

        // --- Ayarlar ---
        PauseSection(Loc.T("pause.settings"));

        GUILayout.Label(Loc.T("pause.volume", Mathf.RoundToInt(GameSettings.MasterVolume * 100f)), smallStyle);
        float vol = GUILayout.HorizontalSlider(GameSettings.MasterVolume, 0f, 1f);
        if (!Mathf.Approximately(vol, GameSettings.MasterVolume)) GameSettings.MasterVolume = vol;

        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("pause.sensitivity", GameSettings.MouseSensitivity.ToString("0.0")), smallStyle);
        float sens = GUILayout.HorizontalSlider(GameSettings.MouseSensitivity,
            GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        if (!Mathf.Approximately(sens, GameSettings.MouseSensitivity)) GameSettings.MouseSensitivity = sens;

        PauseSection(Loc.T("pause.language"));
        DrawLanguagePicker();

        // --- Öğretici ---
        PauseSection(Loc.T("pause.tutorial"));
        GUILayout.BeginHorizontal();
        if (UIUtil.LayoutButton(Loc.T("pause.tutorial.restart"), ButtonKind.Secondary, Mathf.Round(30f * sc)))
        {
            TutorialUI.Restart();
            SetPaused(false);
        }
        if (TutorialUI.Active && UIUtil.LayoutButton(Loc.T("pause.tutorial.skip"), ButtonKind.Secondary, Mathf.Round(30f * sc)))
        {
            TutorialUI.Skip();
        }
        GUILayout.EndHorizontal();

        // --- Sesli sohbet ---
        GUILayout.Space(12f * sc);
        VoiceChat.DrawSettings(sectionStyle, smallStyle, smallButtonStyle);
        GUILayout.Space(4f);
        // Yasaklı kelime algılama (model indirme onayı): "Hayır" diyen buradan geri açabilir
        bool detectOn = VoiceKeywordListener.DetectionEnabled;
        if (GUILayout.Button(Loc.T(detectOn ? "crimetalk.toggle.on" : "crimetalk.toggle.off"), smallButtonStyle, GUILayout.Height(28f)))
            VoiceKeywordListener.DownloadConsent = !detectOn;
        GUILayout.Space(6f);

        GUILayout.EndScrollView();
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        if (sm.ActiveSession != null &&
            UIUtil.LayoutButton(Loc.T("pause.copycode", sm.ActiveSession.Code), ButtonKind.Secondary, Mathf.Round(38f * sc)))
        {
            GUIUtility.systemCopyBuffer = sm.ActiveSession.Code;
            UIUtil.ShowToast(Loc.T("toast.copied", sm.ActiveSession.Code), ToastKind.Success, 2f, IconId.Check);
        }

        string leaveText = sm.IsSinglePlayer ? Loc.T("pause.tomenu") : IsHost ? Loc.T("pause.close") : Loc.T("pause.leave");
        if (UIUtil.LayoutButton(leaveText, ButtonKind.Danger, Mathf.Round(38f * sc), !sm.IsBusy))
        {
            GameSettings.Save();
            _ = sm.LeaveRoomAsync();
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    /// <summary>Duraklat menüsünde bölüm başlığı (BÜYÜK HARF pirinç + ince çizgi).</summary>
    private static void PauseSection(string text)
    {
        GUILayout.Space(12f * UITheme.Scale);
        Rect r = GUILayoutUtility.GetRect(10f, 26f * UITheme.Scale, GUILayout.ExpandWidth(true));
        UIUtil.SectionHeader(r, text);
    }

    /// <summary>Dil düğmeleri (seçili olan pirinç araç düğmesi).</summary>
    private void DrawLanguagePicker()
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < Loc.LanguageNames.Length; i++)
        {
            bool selected = (int)Loc.Current == i;
            Rect r = GUILayoutUtility.GetRect(UIUtil.Measure(Loc.LanguageNames[i], TextRole.Button).x + 32f, Mathf.Round(30f * UITheme.Scale), GUILayout.ExpandWidth(false));
            if (UIUtil.Button(r, Loc.LanguageNames[i], ButtonKind.Tool, true, false, null, selected) && !selected)
            {
                Loc.Current = (Language)i;
            }
            GUILayout.Space(6f);
        }
        GUILayout.EndHorizontal();
    }

    /// <summary>Kıyafet düğmeleri: her tıklama o parçanın bir sonraki seçeneğine geçer. Yeni paketi döner.</summary>
    private int DrawOutfitPicker(int packed)
    {
        int hat = PlayerNetworkSetup.OutfitHat(packed);
        int face = PlayerNetworkSetup.OutfitFace(packed);
        int build = PlayerNetworkSetup.OutfitBuild(packed);
        float h = Mathf.Round(30f * UITheme.Scale);

        GUILayout.BeginHorizontal();
        if (UIUtil.LayoutButton(Loc.T("outfit.hat." + hat), ButtonKind.Tool, h)) hat = (hat + 1) % PlayerNetworkSetup.HatCount;
        if (UIUtil.LayoutButton(Loc.T("outfit.face." + face), ButtonKind.Tool, h)) face = (face + 1) % PlayerNetworkSetup.FaceCount;
        if (UIUtil.LayoutButton(Loc.T("outfit.build." + build), ButtonKind.Tool, h)) build = (build + 1) % PlayerNetworkSetup.BuildCount;
        GUILayout.EndHorizontal();
        return PlayerNetworkSetup.PackOutfit(hat, face, build);
    }

    /// <summary>
    /// Renk kutucukları (rect içinde, soldan). Seçili: açık pirinç halka; başkasının rengi: soluk + tarama, seçilemez.
    /// onLight: açık yüzey (sergi etiketi) üstünde kömür hat. Tıklanan rengin numarasını döner.
    /// </summary>
    private static int DrawSwatches(Rect area, int selected, float size, bool onLight, bool interactive)
    {
        int result = selected;
        float gap = Mathf.Max(4f, size * 0.22f);
        int n = PlayerNetworkSetup.Colors.Length;
        if (n > 0 && n * size + (n - 1) * gap > area.width) size = (area.width - (n - 1) * gap) / n;
        for (int i = 0; i < n; i++)
        {
            Rect r = new Rect(area.x + i * (size + gap), area.y, size, size);
            bool taken = i != selected && PlayerNetworkSetup.IsColorTakenByOther(i);
            Color c = PlayerNetworkSetup.Colors[i];
            if (taken) c = new Color(c.r * 0.35f + 0.1f, c.g * 0.35f + 0.1f, c.b * 0.35f + 0.1f, 1f);
            UIUtil.DrawRect(r, c);
            UIUtil.Outline(r, 1f, onLight ? UITheme.Charcoal : UITheme.WithAlpha(UITheme.Ivory, 0.5f));
            if (taken) UIUtil.Hatch(r, UITheme.WithAlpha(UITheme.Charcoal, 0.5f));
            if (i == selected) UIUtil.Outline(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), 2f, onLight ? UITheme.Bordeaux : UITheme.BrassLight);

            if (!taken && interactive && GUI.enabled && GUI.Button(r, GUIContent.none, GUIStyle.none)) result = i;
        }
        return result;
    }

    private static void KickPlayer(ulong clientId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || clientId == NetworkManager.ServerClientId) return;

        string name = $"Player{clientId}";
        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p != null && p.OwnerClientId == clientId) name = p.PlayerName;
        }

        if (ChatManager.Instance != null) ChatManager.Instance.ServerAnnounceKick(clientId, name);
        nm.DisconnectClient(clientId, "status.kicked"); // her oyuncu kendi dilinde görür
    }

    /// <summary>Bağlanırken: ortada plaket + başlık + nabızlı üç nokta.</summary>
    private void DrawCenteredMessage(string message)
    {
        float sc = UITheme.Scale;
        Rect r = CenteredRect(Mathf.Round(340f * sc), Mathf.Round(84f * sc));
        UIUtil.Panel(r, PanelKind.Plaque);
        UIUtil.Text(new Rect(r.x, r.y + 8f * sc, r.width, 44f * sc), message, TextRole.Title, UITheme.Ivory, TextAnchor.MiddleCenter);
        for (int i = 0; i < 3; i++)
        {
            float a = 0.3f + 0.7f * Mathf.Clamp01(Mathf.Sin(Time.unscaledTime * 4f - i * 0.7f));
            UIUtil.DrawRect(new Rect(r.center.x - 18f + i * 14f, r.yMax - 20f * sc, 6f, 6f), UITheme.WithAlpha(UITheme.BrassLight, a));
        }
    }

    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    private static Rect CenteredRect(float w, float h)
    {
        return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
    }

    private void EnsureStyles()
    {
        if (labelStyle != null) return;

        UIUtil.ApplySkin(); // tasarım sistemi (UITheme / UIUtil)

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        labelStyle.normal.textColor = UITheme.Ivory;

        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        smallStyle.normal.textColor = UITheme.IvoryDim;

        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = UITheme.Brass;

        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
        if (UITheme.MonoFont != null) codeStyle.font = UITheme.MonoFont;
        codeStyle.normal.textColor = UITheme.BrassLight;

        pingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        if (UITheme.MonoFont != null) pingStyle.font = UITheme.MonoFont;

        smallButtonStyle = UIUtil.ThemeButton(new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold });

        fieldStyle = UIUtil.ThemeField(new GUIStyle(GUI.skin.textField)
        {
            fontSize = 17, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 4, 4)
        });

        codeFieldStyle = UIUtil.ThemeField(new GUIStyle(GUI.skin.textField)
        {
            fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
        });
        if (UITheme.MonoFont != null) codeFieldStyle.font = UITheme.MonoFont;
    }
}
