using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Basit oda menüsü (OnGUI ile, sahnede Canvas kurmaya gerek yok).
///  - Ana menü: [Oda Kur]  /  kod kutusu + [Katıl]
///  - Oyun içi: sol üstte oda kodu ve oyuncu sayısı, Esc ile duraklat menüsü.
/// Sahnede herhangi bir objeye (ör. NetworkManager objesine) ekle.
/// </summary>
public class SessionMenuUI : MonoBehaviour
{
    [Tooltip("Menüdeyken sahneyi gösteren kamera. Oyuncu doğunca kapatılır.")]
    [SerializeField] private GameObject menuCamera;

    private string joinCode = "";
    private bool pauseOpen;
    private bool localPlayerSpawned;

    private GUIStyle titleStyle, labelStyle, buttonStyle, fieldStyle, codeStyle, statusStyle, boxStyle;
    private Texture2D panelTex;

    private void OnEnable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned += OnLocalPlayerSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned += OnLocalPlayerDespawned;
    }

    private void OnDisable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned -= OnLocalPlayerSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned -= OnLocalPlayerDespawned;
    }

    private void Start()
    {
        SetMenuCamera(true);
        ShowCursor(true);
    }

    private void OnLocalPlayerSpawned(PlayerNetworkSetup player)
    {
        localPlayerSpawned = true;
        pauseOpen = false;
        SetMenuCamera(false);
    }

    private void OnLocalPlayerDespawned()
    {
        localPlayerSpawned = false;
        pauseOpen = false;
        SetMenuCamera(true);
        ShowCursor(true);
    }

    private void Update()
    {
        if (!localPlayerSpawned || Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            pauseOpen = !pauseOpen;
            ShowCursor(pauseOpen);
        }
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
            DrawCenteredMessage("Bağlanılıyor...");
        }
        else if (pauseOpen)
        {
            DrawPauseMenu(sm);
        }
    }

    private void DrawMainMenu(SessionManager sm)
    {
        Rect panel = CenteredRect(380f, 330f);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));

        GUILayout.Label("MOVER-SIM", titleStyle);
        GUILayout.Space(14f);

        GUI.enabled = !sm.IsBusy;

        if (GUILayout.Button("Oda Kur", buttonStyle, GUILayout.Height(44f)))
        {
            _ = sm.CreateRoomAsync();
        }

        GUILayout.Space(18f);
        GUILayout.Label("Arkadaşının oda kodu:", labelStyle);
        joinCode = GUILayout.TextField(joinCode, 12, fieldStyle, GUILayout.Height(38f)).ToUpperInvariant();

        GUILayout.Space(6f);
        bool enterPressed = Event.current.type == EventType.KeyDown &&
                            (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

        if (GUILayout.Button("Katıl", buttonStyle, GUILayout.Height(44f)) || (enterPressed && joinCode.Length > 0))
        {
            _ = sm.JoinRoomAsync(joinCode);
        }

        GUI.enabled = true;

        GUILayout.Space(10f);
        if (!string.IsNullOrEmpty(sm.Status))
        {
            GUILayout.Label(sm.Status, statusStyle);
        }

        GUILayout.EndArea();
    }

    private void DrawHud(SessionManager sm)
    {
        string code = sm.ActiveSession.Code;
        int count = sm.ActiveSession.Players != null ? sm.ActiveSession.Players.Count : 1;
        string role = IsHost ? "Host" : "Oyuncu";

        Rect r = new Rect(12f, 12f, 270f, 64f);
        GUI.Box(r, GUIContent.none, boxStyle);
        GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width, 28f), $"Oda Kodu: {code}", codeStyle);
        GUI.Label(new Rect(r.x + 12f, r.y + 36f, r.width, 22f),
            $"{role}  •  Oyuncular: {count}/{sm.MaxPlayers}  •  [Esc] Menü", labelStyle);
    }

    private void DrawPauseMenu(SessionManager sm)
    {
        Rect panel = CenteredRect(320f, 250f);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f));
        GUILayout.Label("Duraklatıldı", titleStyle);
        GUILayout.Space(10f);

        if (GUILayout.Button("Devam Et", buttonStyle, GUILayout.Height(40f)))
        {
            pauseOpen = false;
            ShowCursor(false);
        }

        if (GUILayout.Button($"Kodu Kopyala ({sm.ActiveSession.Code})", buttonStyle, GUILayout.Height(40f)))
        {
            GUIUtility.systemCopyBuffer = sm.ActiveSession.Code;
        }

        GUI.enabled = !sm.IsBusy;
        if (GUILayout.Button(IsHost ? "Odayı Kapat" : "Odadan Ayrıl", buttonStyle, GUILayout.Height(40f)))
        {
            _ = sm.LeaveRoomAsync();
        }
        GUI.enabled = true;

        GUILayout.EndArea();
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

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = panelTex;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = Color.white;

        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        codeStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);

        statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter };
        statusStyle.normal.textColor = new Color(1f, 0.75f, 0.45f);

        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold };

        fieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
        };
    }
}
