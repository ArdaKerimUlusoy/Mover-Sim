using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Ana menü + oda menüsü + duraklat menüsü (OnGUI; sade arayüz — UI_DESIGN.md §Ana menü).
///  - Ana menü: oyunun gerçek sahnesi (menü kamerası yavaşça salınır) + vinyet + soldan koyu perde; solda sade menü
///    (Tek Oyunculu, Çok Oyunculu, Öğretici, Ayarlar, Yapımcılar, Çıkış), sağ altta oyuncu kartı (isim + renk).
///    Klavye: ↑/↓, Enter, Esc (ekranda tuş ipucu yazılmaz).
///  - Çok oyunculu: oda kur / koda göre katıl.  Ayarlar: sekmeli ekran (SettingsUI).  Yapımcılar: kart.
///  - Oyun içi: çok oyunculuda sol üstte oda kodu, katılan oyuncuda sağ üstte ping (kutusuz küçük yazı).
///  - Esc menüsü: Oyuna dön / Ekip (oyuncular, host için At) / Ayarlar (aynı sekmeli ekran) / Kodu kopyala / Ana menüye dön.
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
        if (EmoteWheel.EatsEsc) return;                      // Esc emote çarkını kapatıyor

        // Öğretici açıkken Esc'yi BASILI TUTMAK öğreticiyi atlar: menü basınca değil, kısa basıp BIRAKINCA açılır
        // (basılı tutarken ayarlar açılmasın). Menü zaten açıksa Esc her zamanki gibi basınca çalışır.
        if (TutorialUI.Active && !IsPaused && !PlayerInventory.IsPanelOpen)
        {
            // Kat planı / monitör paneli açıkken Esc onları kapatır: menü açılmasın
            if (Keyboard.current.escapeKey.wasPressedThisFrame) escDownAt = FloorPlanUI.IsOpen || BodyCam.PanelOpen ? -1f : Time.unscaledTime;
            if (Keyboard.current.escapeKey.wasReleasedThisFrame && escDownAt >= 0f)
            {
                bool shortTap = Time.unscaledTime - escDownAt < TutorialUI.EscTapMax;
                escDownAt = -1f;
                if (shortTap && TutorialUI.Active) SetPaused(true);
            }
            return;
        }
        escDownAt = -1f;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // Çanta açıksa Esc önce çantayı kapatır.
            if (PlayerInventory.IsPanelOpen)
            {
                PlayerInventory.ClosePanel();
                return;
            }
            // Alt sayfadaysa (Ekip / Ayarlar) Esc önce ana sayfaya döner
            if (IsPaused && pausePage != PausePage.Main) { GameSettings.Save(); pausePage = PausePage.Main; return; }
            SetPaused(!IsPaused);
        }
    }

    private float escDownAt = -1f;

    private void SetPaused(bool paused)
    {
        IsPaused = paused;
        ShowCursor(paused);
        if (!paused) GameSettings.Save();
        pauseScroll = Vector2.zero;
        pausePage = PausePage.Main;
    }

    private void SetMenuCamera(bool active)
    {
        if (active) EnsureMenuMuseum();
        SetCeilingsVisible(!active); // menüde yukarıdan bakılır: iç tavanlar gizli (oyunda geri açılır)
        if (menuCamera == null) return;
        if (!active && menuCamBaseSet) menuCamera.transform.localRotation = menuCamBase;
        menuCamera.SetActive(active);
    }

    /// <summary>
    /// Menünün arkasındaki müze: sahnedeki ESKİ sabit test müzesi yerine, oyunda gerçekten kurulan yeni düzen (odalar, cephe, ışık)
    /// rastgele bir tohum + temayla bir kez kurulur. Oyun başlayınca görev kendi tohumuyla yeniden kurar; oyundan menüye dönünce
    /// son oynanan müze kalır. Hata olursa menü etkilenmesin diye yutulur.
    /// </summary>
    private static void EnsureMenuMuseum()
    {
        try
        {
            MuseumLayout l = MuseumLayout.Instance;
            if (l != null && l.AppliedSeed != int.MinValue) return; // zaten bir düzen kurulu
            MuseumSession ms = FindFirstObjectByType<MuseumSession>(FindObjectsInactive.Include);
            GameObject root = GameObject.Find(ms != null ? ms.MuseumRootName : "TestMuseum");
            if (root == null) return;
            int themes = Mathf.Max(1, ArtworkLook.Themes.Length);
            MuseumLayout.Apply(root, Random.Range(1, int.MaxValue), Random.Range(0, themes));
        }
        catch (System.Exception e) { Debug.LogWarning("[Menu] Menü müzesi kurulamadı: " + e.Message); }
    }

    /// <summary>Kurulan müzenin iç tavanlarını (ve tavan lambalarını) göster / gizle. Sadece görüntü; çarpışma değişmez.</summary>
    private static void SetCeilingsVisible(bool visible)
    {
        MuseumLayout l = MuseumLayout.Instance;
        if (l == null) return;
        foreach (Renderer r in l.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Ceiling_R") || n == "CeilingLamp") r.enabled = visible;
        }
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

        // 1) Arka plan: oyunun gerçek sahnesi (menü kamerası) + vinyet + soldan perde. Kamera yoksa düz koyu zemin.
        bool sceneVisible = menuCamera != null && menuCamera.activeInHierarchy;
        if (!sceneVisible) UIUtil.DrawRect(new Rect(0f, 0f, W, H), UITheme.Charcoal);
        UIUtil.Vignette(sceneVisible ? 0.9f : 0.6f);

        float colX = Mathf.Round(Mathf.Max(28f, W * 0.065f));
        float colW = Mathf.Round(Mathf.Min(430f * sc, W - colX * 2f));
        UIUtil.ScrimLeft(Mathf.Min(W, Mathf.Max(colX + colW + 260f * sc, W * 0.58f)));

        bool wasEnabled = GUI.enabled;
        GUI.enabled = !quitConfirm;

        // 2) Başlık: oyunun adı + slogan (süs çizgisi yok)
        float y = Mathf.Round(Mathf.Max(40f * sc, H * 0.1f));
        DrawLanguageTabs(W - colX, Mathf.Round(Mathf.Max(24f * sc, H * 0.05f)));
        y += DrawFitted(new Rect(colX, y, Mathf.Min(W - colX * 2f, 900f * sc), 66f * sc), gameTitle, TextRole.Display, UITheme.Ivory);
        y += 4f * sc;
        UIUtil.Text(new Rect(colX, y, colW, 24f * sc), Loc.T("menu.tagline"), TextRole.Body, UITheme.IvoryDim);
        y += 48f * sc;

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
            case MenuScreen.Settings: DrawSettingsScreen(cx, y, Mathf.Min(W - colX * 2f, 680f * sc), H - y - 40f * sc); break;
            case MenuScreen.Credits: DrawCredits(cx, y, colW); break;
        }
        GUI.color = oldColor;
        GUI.enabled = !quitConfirm;

        if (screen == MenuScreen.Home) DrawPlayerCard(W - colX, H - 40f * sc, colX + colW + 32f * sc);

        // 4) Bağlantı geri bildirimi
        DrawStatusLine(sm, colX, colW, H - 96f * sc);

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

    /// <summary>Ana ekran: sade dikey menü; odaklı öğenin kısa açıklaması altında.</summary>
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

        float rowH = Mathf.Round(48f * sc);
        for (int i = 0; i < HomeKeys.Length; i++)
        {
            Rect row = new Rect(x - 14f * sc, y + i * rowH, w + 14f * sc, rowH - 4f * sc);
            if (interactive && mouseMoved && row.Contains(e.mousePosition)) focus = i;
            bool f = focus == i;
            bool danger = i == HomeKeys.Length - 1;
            float ft = UIUtil.Ease.OutCubic(UIUtil.Tween(NavHash + i, f, UITheme.MotionFast));

            UIUtil.Round(row, new Color(1f, 1f, 1f, 0.07f * ft), UIUtil.RadiusSmall + 2f);
            float tx = x + 4f * sc * ft;
            Color rest = UITheme.WithAlpha(UITheme.IvoryDim, interactive ? 0.75f : 0.4f);
            Color lc = Color.Lerp(rest, danger ? UITheme.DangerLight : UITheme.Ivory, ft);
            UIUtil.Text(new Rect(tx, row.y, w - 40f * sc, row.height), Loc.T(HomeKeys[i]), TextRole.Title, lc);
            if (ft > 0.01f)
                UIUtil.Icon(new Rect(row.xMax - 30f * sc, row.center.y - 8f * sc, 16f * sc, 16f * sc), IconId.Chevron, UITheme.WithAlpha(UITheme.Ivory, 0.6f * ft));

            if (interactive && GUI.Button(row, GUIContent.none, GUIStyle.none))
            {
                focus = i;
                ActivateHome(sm, i);
            }
        }

        y += HomeKeys.Length * rowH + 14f * sc;
        UIUtil.Text(new Rect(x, y, w, 44f * sc), Loc.T(HomeDesc[Mathf.Clamp(focus, 0, HomeDesc.Length - 1)]),
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

        float bh = Mathf.Round(48f * sc);
        if (UIUtil.Button(new Rect(x, y, w, bh), Loc.T("menu.create"), ButtonKind.Primary, interactive, focus == 0))
            _ = sm.CreateRoomAsync();
        y += bh + 6f * sc;
        UIUtil.Text(new Rect(x, y, w, 36f * sc), Loc.T("menu.create.desc"), TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += 46f * sc;

        UIUtil.Text(new Rect(x, y, w, 20f * sc), Loc.T("menu.joincode"), TextRole.Section, UITheme.IvoryDim);
        y += 24f * sc;
        Rect field = new Rect(x, y, w, Mathf.Round(48f * sc));
        GUI.SetNextControlName(CodeControl);
        joinCode = GUI.TextField(field, joinCode, 12, codeFieldStyle).ToUpperInvariant();
        if (focusCodeField && interactive) { GUI.FocusControl(CodeControl); focusCodeField = false; }
        if (focus == 1 && !codeFocused && interactive) UIUtil.FocusRing(field);
        y += field.height + 10f * sc;

        if (UIUtil.Button(new Rect(x, y, w, bh), Loc.T("menu.join"), ButtonKind.Primary, interactive && joinCode.Length > 0, focus == 2))
            _ = sm.JoinRoomAsync(joinCode);
        y += bh + 18f * sc;

        if (UIUtil.Button(new Rect(x, y, Mathf.Min(w, 160f * sc), Mathf.Round(38f * sc)), Loc.T("menu.back"), ButtonKind.Tool, interactive, focus == 3))
            GoTo(MenuScreen.Home);
    }

    /// <summary>Ayarlar: sekmeli ekran (Ses / Görüntü / Kontroller / Oyun) — SettingsUI. Esc ya da Geri: ana ekran.</summary>
    private void DrawSettingsScreen(float x, float y, float w, float h)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        if (interactive && UIUtil.EscapePressed()) { GameSettings.Save(); GoTo(MenuScreen.Home); return; }

        float btnH = Mathf.Round(38f * sc);
        float cardH = Mathf.Max(260f, h - btnH - 16f * sc);
        Rect card = new Rect(x, y - 16f * sc, w, cardH);
        UIUtil.Panel(card, PanelKind.Plaque);
        float pad = Mathf.Round(20f * sc);
        SettingsUI.Draw(new Rect(card.x + pad, card.y + pad, card.width - pad * 2f, card.height - pad * 2f), false, null);

        if (UIUtil.Button(new Rect(x, card.yMax + 12f * sc, Mathf.Min(w, 160f * sc), btnH), Loc.T("menu.back"), ButtonKind.Tool, interactive))
        {
            GameSettings.Save();
            GoTo(MenuScreen.Home);
        }
    }

    /// <summary>Yapımcılar: koyu kart.</summary>
    private void DrawCredits(float x, float y, float w)
    {
        float sc = UITheme.Scale;
        bool interactive = GUI.enabled;
        if (interactive && (UIUtil.EscapePressed() || Submit())) { GoTo(MenuScreen.Home); return; }

        float pad = 22f * sc, iw = w - pad * 2f;
        string[] roles = { Loc.T("credits.made"), Loc.T("credits.tech"), Loc.T("credits.thanks") };
        string[] values = { creditsName, "Unity 6  ·  Netcode for GameObjects  ·  Unity Relay", Loc.T("credits.thanks.body") };
        float h = pad + 36f * sc + 10f * sc;
        for (int i = 0; i < roles.Length; i++) h += 20f * sc + UIUtil.MeasureHeight(values[i], TextRole.Body, iw) + 12f * sc;
        string disc = Loc.T("credits.disclaimer");
        h += 10f * sc + UIUtil.MeasureHeight(disc, TextRole.Caption, iw) + pad;

        Rect page = new Rect(x, y, w, h);
        UIUtil.Panel(page, PanelKind.Plaque);
        float py = page.y + pad;
        UIUtil.Text(new Rect(page.x + pad, py, iw, 36f * sc), Loc.T("credits.title"), TextRole.Title, UITheme.Ivory);
        py += 46f * sc;
        for (int i = 0; i < roles.Length; i++)
        {
            UIUtil.Text(new Rect(page.x + pad, py, iw, 18f * sc), roles[i], TextRole.Section, UITheme.IvoryDim);
            py += 20f * sc;
            float vh = UIUtil.MeasureHeight(values[i], TextRole.Body, iw);
            UIUtil.Text(new Rect(page.x + pad, py, iw, vh), values[i], TextRole.Body, UITheme.Ivory, TextAnchor.UpperLeft);
            py += vh + 12f * sc;
        }
        py += 10f * sc;
        GUI.Label(new Rect(page.x + pad, py, iw, UIUtil.MeasureHeight(disc, TextRole.Caption, iw)), disc, UITheme.Style(TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft));

        if (UIUtil.Button(new Rect(x, page.yMax + 18f * sc, Mathf.Min(w, 160f * sc), Mathf.Round(38f * sc)), Loc.T("menu.back"), ButtonKind.Tool, interactive, true))
            GoTo(MenuScreen.Home);
    }

    /// <summary>
    /// Sağ altta oyuncu kartı: isim ve renk (tek karakter modeli var; oyuncular renkle ayrılır).
    /// minX: menü sütununun bittiği yer (dar ekranda kart sütunun üstüne binmesin diye gizlenir).
    /// </summary>
    private void DrawPlayerCard(float right, float bottom, float minX)
    {
        float sc = UITheme.Scale;
        float w = Mathf.Round(320f * sc), pad = Mathf.Round(16f * sc);
        float sw = Mathf.Round(26f * sc);
        float h = pad + 20f * sc + 40f * sc + 12f * sc + 20f * sc + sw + 4f + pad;
        Rect r = new Rect(right - w, bottom - h, w, h);
        if (r.x < minX) return; // yer yok: renk Ayarlar > Oyun'dan da değiştirilebilir
        bool interactive = GUI.enabled;

        UIUtil.Panel(r, PanelKind.Plaque);
        float ix = r.x + pad, iw = r.width - pad * 2f, y = r.y + pad;

        UIUtil.Text(new Rect(ix, y, iw, 18f * sc), Loc.T("menu.name"), TextRole.Section, UITheme.IvoryDim);
        y += 20f * sc;
        Rect nameRect = new Rect(ix, y, iw, Mathf.Round(36f * sc));
        GUI.SetNextControlName(NameControl);
        string newName = GUI.TextField(nameRect, nameInput ?? "", 20, fieldStyle);
        if (newName != nameInput)
        {
            nameInput = newName;
            PlayerNetworkSetup.SavedName = newName;
        }
        y += 40f * sc + 12f * sc;

        UIUtil.Text(new Rect(ix, y, iw, 18f * sc), Loc.T("menu.color"), TextRole.Section, UITheme.IvoryDim);
        y += 20f * sc + 4f;
        int colorIndex = PlayerNetworkSetup.SavedColorIndex;
        GUI.enabled = interactive;
        int picked = SettingsUI.ColorRow(new Rect(ix + 4f, y, iw - 8f, sw), colorIndex);
        if (picked != colorIndex) PlayerNetworkSetup.SavedColorIndex = picked;
    }

    /// <summary>Sağ üstte dil seçici (parçalı). right: sağ kenar.</summary>
    private void DrawLanguageTabs(float right, float y)
    {
        float sc = UITheme.Scale;
        float h = Mathf.Round(32f * sc);
        float w = 0f;
        for (int i = 0; i < Loc.LanguageNames.Length; i++) w += UIUtil.Measure(Loc.LanguageNames[i], TextRole.Button).x + 28f;
        int pick = UIUtil.Tabs(new Rect(right - w, y, w, h), Loc.LanguageNames, (int)Loc.Current);
        if (pick != (int)Loc.Current) Loc.Current = (Language)pick;
    }

    /// <summary>Bağlantı / işlem geri bildirimi: koyu kart + simge (meşgulken saat, nabızlı).</summary>
    private void DrawStatusLine(SessionManager sm, float x, float w, float y)
    {
        string status = sm.Status;
        if (sm.IsBusy && string.IsNullOrEmpty(status)) status = Loc.T("menu.busy");
        if (string.IsNullOrEmpty(status)) return;

        float sc = UITheme.Scale;
        float textX = 40f * sc;
        float th = UIUtil.MeasureHeight(status, TextRole.Body, w - textX - 12f);
        Rect r = new Rect(x, y - Mathf.Max(0f, th - 20f * sc), w, Mathf.Max(40f * sc, th + 16f));
        UIUtil.Panel(r, PanelKind.Plaque, 0.95f);
        Color accent = sm.IsBusy ? UITheme.WithAlpha(UITheme.Ivory, 0.55f + 0.45f * UIUtil.Pulse(4f)) : UITheme.Ochre;
        UIUtil.Icon(new Rect(r.x + 12f * sc, r.center.y - 10f * sc, 20f * sc, 20f * sc), sm.IsBusy ? IconId.Clock : IconId.Warning, accent);
        GUIStyle s = UITheme.Style(TextRole.Body, UITheme.Ivory);
        bool wrap = s.wordWrap; s.wordWrap = true;
        GUI.Label(new Rect(r.x + textX, r.y, r.width - textX - 12f, r.height), status, s);
        s.wordWrap = wrap;
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

    /// <summary>Oyun içi köşe bilgisi: çok oyunculuda oda kodu (sol üst) ve ping (sağ üst) — kutusuz, gölgeli küçük yazı.</summary>
    private void DrawHud(SessionManager sm)
    {
        if (WorkbenchUI.IsOpen) return; // atölye panelinin üstüne binmesin
        if (Event.current.type != EventType.Repaint) return;
        float sc = UITheme.Scale;

        if (sm.ActiveSession != null)
        {
            string code = sm.ActiveSession.Code;
            int count = PlayerNetworkSetup.All.Count > 0 ? PlayerNetworkSetup.All.Count
                      : (sm.ActiveSession.Players != null ? sm.ActiveSession.Players.Count : 1);
            string t = Loc.T("hud.code", code) + "   ·   " + Loc.T("hud.players", count, sm.MaxPlayers);
            float w = UIUtil.Measure(t, TextRole.Caption).x + 8f;
            Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.SessionCode, w, 20f * sc);
            UIUtil.ShadowText(r, t, TextRole.Caption, UITheme.WithAlpha(UITheme.Ivory, 0.8f));
        }

        // Ping yalnızca katılan oyuncuda anlamlı (host kendi bilgisayarında: 0 ms)
        NetworkManager nm = NetworkManager.Singleton;
        if (!string.IsNullOrEmpty(pingText) && nm != null && !nm.IsHost)
        {
            float w = UIUtil.Measure(pingText, TextRole.Caption).x + 8f;
            Rect pr = UIUtil.HudRect(UIUtil.HudCorner.TopRight, UIUtil.HudOrder.Ping, w, 20f * sc);
            UIUtil.ShadowText(pr, pingText, TextRole.Caption, pingColor, TextAnchor.MiddleRight);
        }
    }

    // ---------------- Duraklat menüsü: ana sayfa, ekip, ayarlar ----------------

    private enum PausePage { Main, Team, Settings }
    private PausePage pausePage = PausePage.Main;

    private void DrawPauseMenu(SessionManager sm)
    {
        UIUtil.Scrim(UITheme.WithAlpha(UITheme.CharDeep, 0.55f));
        UIUtil.Vignette(0.8f);
        switch (pausePage)
        {
            case PausePage.Team: DrawPauseTeam(sm); break;
            case PausePage.Settings: DrawPauseSettings(); break;
            default: DrawPauseMain(sm); break;
        }
    }

    /// <summary>Ana sayfa: Oyuna dön, Ekip (çok oyunculu), Ayarlar, Kodu kopyala, Ana menüye dön / ayrıl.</summary>
    private void DrawPauseMain(SessionManager sm)
    {
        float sc = UITheme.Scale;
        bool multi = sm.ActiveSession != null;
        float bh = Mathf.Round(46f * sc), gap = Mathf.Round(8f * sc), pad = Mathf.Round(24f * sc);
        int buttons = 3 + (multi ? 2 : 0);
        float w = Mathf.Min(Mathf.Round(380f * sc), Screen.width - 32f);
        float h = pad + 40f * sc + 14f * sc + buttons * bh + (buttons - 1) * gap + 10f * sc + pad;
        Rect panel = CenteredRect(w, h);
        UIUtil.Panel(panel, PanelKind.Plaque);

        float x = panel.x + pad, iw = w - pad * 2f, y = panel.y + pad;
        UIUtil.Text(new Rect(x, y, iw, 40f * sc), Loc.T("pause.title"), TextRole.Title, UITheme.Ivory);
        y += 40f * sc + 14f * sc;

        if (UIUtil.Button(new Rect(x, y, iw, bh), Loc.T("pause.resume"), ButtonKind.Primary, true, true)) SetPaused(false);
        y += bh + gap;
        if (multi)
        {
            if (UIUtil.Button(new Rect(x, y, iw, bh), Loc.T("pause.team", PlayerNetworkSetup.All.Count, sm.MaxPlayers), ButtonKind.Tool)) pausePage = PausePage.Team;
            y += bh + gap;
        }
        if (UIUtil.Button(new Rect(x, y, iw, bh), Loc.T("menu.settings"), ButtonKind.Tool)) pausePage = PausePage.Settings;
        y += bh + gap;
        if (multi)
        {
            if (UIUtil.Button(new Rect(x, y, iw, bh), Loc.T("pause.copycode", sm.ActiveSession.Code), ButtonKind.Tool))
            {
                GUIUtility.systemCopyBuffer = sm.ActiveSession.Code;
                UIUtil.ShowToast(Loc.T("toast.copied", sm.ActiveSession.Code), ToastKind.Success, 2f, IconId.Check);
            }
            y += bh + gap;
        }
        y += 10f * sc - gap;
        string leaveText = sm.IsSinglePlayer ? Loc.T("pause.tomenu") : IsHost ? Loc.T("pause.close") : Loc.T("pause.leave");
        if (UIUtil.Button(new Rect(x, y, iw, bh), leaveText, ButtonKind.Danger, !sm.IsBusy))
        {
            GameSettings.Save();
            _ = sm.LeaveRoomAsync();
        }
    }

    /// <summary>Ekip: oyuncular (renk, ad, host için [At]).</summary>
    private void DrawPauseTeam(SessionManager sm)
    {
        float sc = UITheme.Scale;
        float pad = Mathf.Round(24f * sc), rowH = Mathf.Round(40f * sc), bh = Mathf.Round(40f * sc);
        int n = PlayerNetworkSetup.All.Count;
        float w = Mathf.Min(Mathf.Round(460f * sc), Screen.width - 32f);
        float h = pad + 40f * sc + 12f * sc + Mathf.Max(1, n) * rowH + 16f * sc + bh + pad;
        Rect panel = CenteredRect(w, h);
        UIUtil.Panel(panel, PanelKind.Plaque);
        float x = panel.x + pad, iw = w - pad * 2f, y = panel.y + pad;
        UIUtil.Text(new Rect(x, y, iw, 40f * sc), Loc.T("pause.team", n, sm.MaxPlayers), TextRole.Title, UITheme.Ivory);
        y += 40f * sc + 12f * sc;

        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null) continue;
            Rect row = new Rect(x, y, iw, rowH - 4f);
            UIUtil.Round(row, new Color(1f, 1f, 1f, 0.04f), UIUtil.RadiusSmall);
            float s = 16f * sc;
            UIUtil.Round(new Rect(row.x + 12f, row.center.y - s * 0.5f, s, s), p.PlayerColor, 4f);
            string tags = "";
            if (p.IsHostPlayer) tags += "  " + Loc.T("pause.tag.host");
            if (p.IsOwner) tags += "  " + Loc.T("pause.tag.you");
            float kickW = IsHost && !p.IsHostPlayer ? 70f * sc : 0f;
            UIUtil.Text(new Rect(row.x + 12f + s + 12f, row.y, row.width - s - 36f - kickW, row.height), p.PlayerName + tags, TextRole.BodyStrong, UITheme.Ivory);
            if (kickW > 0f && UIUtil.Button(new Rect(row.xMax - kickW - 6f, row.y + 5f, kickW, row.height - 10f), Loc.T("pause.kick"), ButtonKind.Danger))
                pendingKick = p.OwnerClientId;
            y += rowH;
        }
        y += 16f * sc;
        if (UIUtil.Button(new Rect(x, panel.yMax - pad - bh, Mathf.Min(iw, 160f * sc), bh), Loc.T("menu.back"), ButtonKind.Tool)) pausePage = PausePage.Main;
    }

    /// <summary>Ayarlar: ana menüdeki ile aynı sekmeli ekran (renk değişikliği oyuncuna uygulanır).</summary>
    private void DrawPauseSettings()
    {
        float sc = UITheme.Scale;
        float pad = Mathf.Round(22f * sc), bh = Mathf.Round(40f * sc);
        float w = Mathf.Min(Mathf.Round(680f * sc), Screen.width - 32f);
        float h = Mathf.Min(Mathf.Round(640f * sc), Screen.height - 40f);
        Rect panel = CenteredRect(w, h);
        UIUtil.Panel(panel, PanelKind.Plaque);
        float x = panel.x + pad, iw = w - pad * 2f, y = panel.y + pad;
        UIUtil.Text(new Rect(x, y, iw, 36f * sc), Loc.T("menu.settings"), TextRole.Title, UITheme.Ivory);
        y += 36f * sc + 12f * sc;
        SettingsUI.Draw(new Rect(x, y, iw, panel.yMax - pad - bh - 12f * sc - y), true, localPlayer);
        if (UIUtil.Button(new Rect(x, panel.yMax - pad - bh, Mathf.Min(iw, 160f * sc), bh), Loc.T("menu.back"), ButtonKind.Tool))
        {
            GameSettings.Save();
            pausePage = PausePage.Main;
        }
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

    /// <summary>Bağlanırken: ortada kart + başlık + nabızlı üç nokta.</summary>
    private void DrawCenteredMessage(string message)
    {
        float sc = UITheme.Scale;
        Rect r = CenteredRect(Mathf.Round(340f * sc), Mathf.Round(84f * sc));
        UIUtil.Panel(r, PanelKind.Plaque);
        UIUtil.Text(new Rect(r.x, r.y + 8f * sc, r.width, 44f * sc), message, TextRole.Title, UITheme.Ivory, TextAnchor.MiddleCenter);
        for (int i = 0; i < 3; i++)
        {
            float a = 0.3f + 0.7f * Mathf.Clamp01(Mathf.Sin(Time.unscaledTime * 4f - i * 0.7f));
            UIUtil.Round(new Rect(r.center.x - 18f + i * 14f, r.yMax - 20f * sc, 6f, 6f), UITheme.WithAlpha(UITheme.Ivory, a), 3f);
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
