using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basit oda menüsü (OnGUI ile, sahnede Canvas kurmaya gerek yok).
///  - Ana menü: isim, renk, [Oda Kur]  /  kod kutusu + [Katıl]
///  - Oyun içi: sol üstte oda kodu, sağ üstte ping.
///  - Esc menüsü: oyuncu listesi (host için [At]), renk, ses ve fare hassasiyeti, ayrıl.
/// Sahnede herhangi bir objeye (ör. NetworkManager objesine) ekle.
/// </summary>
public class SessionMenuUI : MonoBehaviour
{
    [Tooltip("Menüdeyken sahneyi gösteren kamera. Oyuncu doğunca kapatılır.")]
    [SerializeField] private GameObject menuCamera;

    [Tooltip("Oyunun adı (menü başlığı). Çevrilmez.")]
    [SerializeField] private string gameTitle = "SAHTE ŞAHESER";

    /// <summary>Esc menüsü açık mı? (Sohbet bu sırada açılmaz.)</summary>
    public static bool IsPaused { get; private set; }

    private string joinCode = "";
    private string nameInput;
    private bool localPlayerSpawned;
    private PlayerNetworkSetup localPlayer;

    // Ping
    private float nextPingUpdate;
    private string pingText = "";
    private Color pingColor = Color.white;

    // Atılacak oyuncu (liste çizilirken listeyi değiştirmemek için çizimden sonra uygulanır)
    private ulong? pendingKick;

    private GUIStyle titleStyle, labelStyle, smallStyle, buttonStyle, smallButtonStyle, fieldStyle,
        codeStyle, statusStyle, boxStyle, sectionStyle, pingStyle;
    private Texture2D panelTex, whiteTex;

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
    }

    private void OnLocalPlayerSpawned(PlayerNetworkSetup player)
    {
        localPlayer = player;
        localPlayerSpawned = true;
        IsPaused = false;
        SetMenuCamera(false);
    }

    private void OnLocalPlayerDespawned()
    {
        localPlayer = null;
        localPlayerSpawned = false;
        IsPaused = false;
        SetMenuCamera(true);
        ShowCursor(true);
    }

    private void Update()
    {
        UpdatePing();

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
    }

    private void SetMenuCamera(bool active)
    {
        if (menuCamera != null) menuCamera.SetActive(active);
    }

    private static void ShowCursor(bool show)
    {
        Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = show;
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
            pingColor = new Color(0.55f, 0.9f, 0.55f);
            return;
        }

        if (!nm.IsConnectedClient)
        {
            pingText = Loc.T("hud.connecting");
            pingColor = Color.white;
            return;
        }

        ulong rtt = nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        pingText = Loc.T("ping.value", rtt);
        pingColor = rtt < 80 ? new Color(0.55f, 0.9f, 0.55f)
                  : rtt < 150 ? new Color(1f, 0.85f, 0.35f)
                  : new Color(1f, 0.45f, 0.4f);
    }

    // ---------------- Çizim ----------------

    private void OnGUI()
    {
        SessionManager sm = SessionManager.Instance;
        if (sm == null) return;

        EnsureStyles();

        if (sm.ActiveSession == null)
        {
            DrawMainMenu(sm);
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

    private void DrawMainMenu(SessionManager sm)
    {
        Rect panel = CenteredRect(400f, 640f);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));

        GUILayout.Label(gameTitle, titleStyle);
        GUILayout.Space(14f);

        GUI.enabled = !sm.IsBusy;

        GUILayout.Label(Loc.T("menu.name"), labelStyle);
        string newName = GUILayout.TextField(nameInput ?? "", 20, fieldStyle, GUILayout.Height(38f));
        if (newName != nameInput)
        {
            nameInput = newName;
            PlayerNetworkSetup.SavedName = newName;
        }

        GUILayout.Space(8f);
        GUILayout.Label(Loc.T("menu.color"), labelStyle);
        int picked = DrawColorPicker(PlayerNetworkSetup.SavedColorIndex);
        if (picked != PlayerNetworkSetup.SavedColorIndex) PlayerNetworkSetup.SavedColorIndex = picked;

        GUILayout.Space(8f);
        GUILayout.Label(Loc.T("menu.outfit"), labelStyle);
        int outfit = DrawOutfitPicker(PlayerNetworkSetup.SavedOutfit);
        if (outfit != PlayerNetworkSetup.SavedOutfit) PlayerNetworkSetup.SavedOutfit = outfit;

        GUILayout.Space(14f);

        if (GUILayout.Button(Loc.T("menu.create"), buttonStyle, GUILayout.Height(44f)))
        {
            _ = sm.CreateRoomAsync();
        }

        GUILayout.Space(14f);
        GUILayout.Label(Loc.T("menu.joincode"), labelStyle);
        joinCode = GUILayout.TextField(joinCode, 12, fieldStyle, GUILayout.Height(38f)).ToUpperInvariant();

        GUILayout.Space(6f);
        bool enterPressed = Event.current.type == EventType.KeyDown &&
                            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        if (GUILayout.Button(Loc.T("menu.join"), buttonStyle, GUILayout.Height(44f)) || (enterPressed && joinCode.Length > 0))
        {
            _ = sm.JoinRoomAsync(joinCode);
        }

        GUI.enabled = true;

        GUILayout.Space(8f);
        if (!string.IsNullOrEmpty(sm.Status))
        {
            GUILayout.Label(sm.Status, statusStyle);
        }

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.T("menu.language"), labelStyle, GUILayout.Width(90f), GUILayout.Height(28f));
        DrawLanguagePicker();
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private void DrawHud(SessionManager sm)
    {
        if (WorkbenchUI.IsOpen) return; // atölye panelinin üstüne binmesin
        string code = sm.ActiveSession.Code;
        int count = PlayerNetworkSetup.All.Count > 0 ? PlayerNetworkSetup.All.Count
                  : (sm.ActiveSession.Players != null ? sm.ActiveSession.Players.Count : 1);

        Rect r = new Rect(12f, 12f, 270f, 64f);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width, 28f), Loc.T("hud.code", code), codeStyle);
        GUI.Label(new Rect(r.x + 12f, r.y + 36f, r.width, 22f),
            Loc.T("hud.players", count, sm.MaxPlayers), labelStyle);

        if (!string.IsNullOrEmpty(pingText))
        {
            Rect pr = new Rect(Screen.width - 152f, 12f, 140f, 30f);
            GUI.Box(pr, GUIContent.none, boxStyle);
            pingStyle.normal.textColor = pingColor;
            GUI.Label(pr, pingText, pingStyle);
        }
    }

    private void DrawPauseMenu(SessionManager sm)
    {
        Rect panel = CenteredRect(440f, 720f);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 16f, panel.width - 48f, panel.height - 32f));
        GUILayout.Label(Loc.T("pause.title"), titleStyle);
        GUILayout.Space(6f);

        if (GUILayout.Button(Loc.T("pause.resume"), buttonStyle, GUILayout.Height(38f)))
        {
            SetPaused(false);
        }

        // --- Oyuncular ---
        GUILayout.Space(10f);
        GUILayout.Label(Loc.T("pause.players", PlayerNetworkSetup.All.Count, sm.MaxPlayers), sectionStyle);

        foreach (PlayerNetworkSetup p in PlayerNetworkSetup.All)
        {
            if (p == null) continue;

            GUILayout.BeginHorizontal(GUILayout.Height(28f));

            Rect sw = GUILayoutUtility.GetRect(16f, 16f, GUILayout.Width(16f), GUILayout.Height(16f));
            sw.y += 6f;
            DrawColorRect(sw, p.PlayerColor);
            GUILayout.Space(8f);

            string tags = "";
            if (p.IsHostPlayer) tags += "  " + Loc.T("pause.tag.host");
            if (p.IsOwner) tags += "  " + Loc.T("pause.tag.you");
            GUILayout.Label(p.PlayerName + tags, labelStyle, GUILayout.Height(28f));

            GUILayout.FlexibleSpace();

            if (IsHost && !p.IsHostPlayer)
            {
                if (GUILayout.Button(Loc.T("pause.kick"), smallButtonStyle, GUILayout.Width(56f), GUILayout.Height(24f)))
                {
                    pendingKick = p.OwnerClientId;
                }
            }

            GUILayout.EndHorizontal();
        }

        // --- Renk ---
        GUILayout.Space(10f);
        GUILayout.Label(Loc.T("pause.color"), sectionStyle);
        int current = PlayerNetworkSetup.SavedColorIndex;
        int picked = DrawColorPicker(current);
        if (picked != current && localPlayer != null) localPlayer.SetColor(picked);

        // --- Kıyafet ---
        GUILayout.Space(8f);
        GUILayout.Label(Loc.T("pause.outfit"), sectionStyle);
        int currentOutfit = PlayerNetworkSetup.SavedOutfit;
        int pickedOutfit = DrawOutfitPicker(currentOutfit);
        if (pickedOutfit != currentOutfit && localPlayer != null) localPlayer.SetOutfit(pickedOutfit);

        // --- Ayarlar ---
        GUILayout.Space(10f);
        GUILayout.Label(Loc.T("pause.settings"), sectionStyle);

        GUILayout.Label(Loc.T("pause.volume", Mathf.RoundToInt(GameSettings.MasterVolume * 100f)), smallStyle);
        float vol = GUILayout.HorizontalSlider(GameSettings.MasterVolume, 0f, 1f);
        if (!Mathf.Approximately(vol, GameSettings.MasterVolume)) GameSettings.MasterVolume = vol;

        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("pause.sensitivity", GameSettings.MouseSensitivity.ToString("0.0")), smallStyle);
        float sens = GUILayout.HorizontalSlider(GameSettings.MouseSensitivity,
            GameSettings.MinSensitivity, GameSettings.MaxSensitivity);
        if (!Mathf.Approximately(sens, GameSettings.MouseSensitivity)) GameSettings.MouseSensitivity = sens;

        GUILayout.Space(10f);
        GUILayout.Label(Loc.T("pause.language"), sectionStyle);
        GUILayout.BeginHorizontal();
        DrawLanguagePicker();
        GUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("pause.copycode", sm.ActiveSession.Code), smallButtonStyle, GUILayout.Height(36f)))
        {
            GUIUtility.systemCopyBuffer = sm.ActiveSession.Code;
        }

        GUI.enabled = !sm.IsBusy;
        if (GUILayout.Button(IsHost ? Loc.T("pause.close") : Loc.T("pause.leave"), smallButtonStyle, GUILayout.Height(36f)))
        {
            GameSettings.Save();
            _ = sm.LeaveRoomAsync();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    /// <summary>Dil butonları (seçili olan vurgulu).</summary>
    private void DrawLanguagePicker()
    {
        for (int i = 0; i < Loc.LanguageNames.Length; i++)
        {
            bool selected = (int)Loc.Current == i;
            Color old = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
            if (GUILayout.Button(Loc.LanguageNames[i], smallButtonStyle, GUILayout.Height(28f)) && !selected)
            {
                Loc.Current = (Language)i;
            }
            GUI.backgroundColor = old;
        }
    }

    /// <summary>Kıyafet düğmeleri: her tıklama o parçanın bir sonraki seçeneğine geçer. Yeni paketi döner.</summary>
    private int DrawOutfitPicker(int packed)
    {
        int hat = PlayerNetworkSetup.OutfitHat(packed);
        int face = PlayerNetworkSetup.OutfitFace(packed);
        int build = PlayerNetworkSetup.OutfitBuild(packed);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(Loc.T("outfit.hat." + hat), smallButtonStyle, GUILayout.Height(30f))) hat = (hat + 1) % PlayerNetworkSetup.HatCount;
        if (GUILayout.Button(Loc.T("outfit.face." + face), smallButtonStyle, GUILayout.Height(30f))) face = (face + 1) % PlayerNetworkSetup.FaceCount;
        if (GUILayout.Button(Loc.T("outfit.build." + build), smallButtonStyle, GUILayout.Height(30f))) build = (build + 1) % PlayerNetworkSetup.BuildCount;
        GUILayout.EndHorizontal();
        return PlayerNetworkSetup.PackOutfit(hat, face, build);
    }

    /// <summary>Renk kutucukları çizer, tıklanan rengin numarasını döner.</summary>
    private int DrawColorPicker(int selected)
    {
        int result = selected;
        GUILayout.BeginHorizontal();
        for (int i = 0; i < PlayerNetworkSetup.Colors.Length; i++)
        {
            Rect r = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));

            if (i == selected) DrawColorRect(r, Color.white);
            DrawColorRect(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), PlayerNetworkSetup.Colors[i]);

            if (GUI.enabled && GUI.Button(r, GUIContent.none, GUIStyle.none)) result = i;
            GUILayout.Space(4f);
        }
        GUILayout.EndHorizontal();
        return result;
    }

    private void DrawColorRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, whiteTex);
        GUI.color = old;
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

    private void DrawCenteredMessage(string message)
    {
        Rect r = CenteredRect(300f, 60f);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Label(r, message, titleStyle);
    }

    private static bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    private static Rect CenteredRect(float w, float h)
    {
        return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        panelTex = new Texture2D(1, 1);
        panelTex.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.88f));
        panelTex.Apply();

        whiteTex = Texture2D.whiteTexture;

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = panelTex;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
        smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);

        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        codeStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);

        statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        statusStyle.normal.textColor = new Color(1f, 0.75f, 0.45f);

        pingStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };

        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };
        smallButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };

        fieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
        };
    }
}
