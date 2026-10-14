using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Sohbet kutusu (sol alt).
///  - Enter: sohbeti aç
///  - Enter: gönder (boşsa sadece kapatır),  Esc: vazgeç
/// Yazarken karakter hareket etmez (PlayerController / ObjectCarrier IsTyping'e bakar).
/// ChatManager ile aynı objeye ekle.
/// </summary>
public class ChatUI : MonoBehaviour
{
    /// <summary>Sohbete yazı yazılıyor mu? Oyun girdileri bu sırada kapalı.</summary>
    public static bool IsTyping { get; private set; }

    [SerializeField] private int visibleLines = 8;
    [SerializeField] private float fadeAfterSeconds = 10f;
    [SerializeField] private float width = 460f;

    private const string InputControlName = "ChatInput";

    private string input = "";
    private int openedFrame = -1;
    private bool localPlayerSpawned;

    private GUIStyle lineStyle, inputStyle, hintStyle;
    private Texture2D bgTex;

    private void OnEnable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned += OnLocalSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned += OnLocalDespawned;
    }

    private void OnDisable()
    {
        PlayerNetworkSetup.LocalPlayerSpawned -= OnLocalSpawned;
        PlayerNetworkSetup.LocalPlayerDespawned -= OnLocalDespawned;
        IsTyping = false;
    }

    private void OnLocalSpawned(PlayerNetworkSetup p) => localPlayerSpawned = true;

    private void OnLocalDespawned()
    {
        localPlayerSpawned = false;
        Close();
    }

    /// <summary>
    /// Sohbet az önce kapandıysa true. Aynı Esc/Enter basışının başka bir şeyi
    /// (ör. Esc menüsünü) de tetiklemesini engellemek için SessionMenuUI kullanır.
    /// </summary>
    public static bool ClosedRecently => Time.unscaledTime - lastCloseTime < 0.2f;
    private static float lastCloseTime = -10f;

    /// <summary>Yazma kutusu açıksa kapatır (kamyon sekansı başlarken).</summary>
    public static void ForceCloseTyping()
    {
        if (!IsTyping) return;
        lastCloseTime = Time.unscaledTime;
        IsTyping = false;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;

        if (IsTyping)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                Close(); // gönderme, sadece kapat
            }
            else if (enter && Time.frameCount != openedFrame)
            {
                // Boşsa sadece kapanır, doluysa gönderir ve kapanır.
                if (!string.IsNullOrWhiteSpace(input) && ChatManager.Instance != null)
                {
                    ChatManager.Instance.Send(input);
                }
                Close();
            }
            return;
        }

        if (!localPlayerSpawned || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen || ClosedRecently) return;

        if (enter)
        {
            IsTyping = true;
            input = "";
            openedFrame = Time.frameCount;
        }
    }

    private void Close()
    {
        if (IsTyping) lastCloseTime = Time.unscaledTime;
        IsTyping = false;
        input = "";
    }

    private void OnGUI()
    {
        ChatManager chat = ChatManager.Instance;
        if (chat == null || !localPlayerSpawned) return;

        EnsureStyles();

        float x = 12f;
        float lineH = 22f;
        float inputH = 30f;
        float bottom = Screen.height - 12f;

        // --- Yazma kutusu ---
        // Enter / Esc tuşlarını Update yönetiyor; burada kutunun bu tuşları işlemesini engelliyoruz.
        if (IsTyping)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape))
            {
                e.Use();
            }
        }

        if (IsTyping)
        {
            Rect inputRect = new Rect(x, bottom - inputH, width, inputH);
            GUI.DrawTexture(inputRect, bgTex);
            GUI.SetNextControlName(InputControlName);
            input = GUI.TextField(inputRect, input, ChatManager.MaxMessageLength, inputStyle);
            if (GUI.GetNameOfFocusedControl() != InputControlName) GUI.FocusControl(InputControlName);
            bottom -= inputH + 4f;
        }

        // --- Mesajlar ---
        var lines = chat.Lines;
        int shown = 0;
        float now = Time.unscaledTime;

        for (int i = lines.Count - 1; i >= 0 && shown < visibleLines; i--)
        {
            ChatManager.ChatLine line = lines[i];

            float alpha = 1f;
            if (!IsTyping)
            {
                float age = now - line.Time;
                if (age > fadeAfterSeconds + 1f) break; // daha eskiler de görünmez
                alpha = Mathf.Clamp01(fadeAfterSeconds + 1f - age);
            }

            string text = line.IsSystem
                ? $"<color=#FFD166>• {Escape(line.Display)}</color>"
                : $"<b><color=#7FD1FF>{Escape(line.Sender)}:</color></b> {Escape(line.Text)}";

            Rect r = new Rect(x, bottom - lineH * (shown + 1), width, lineH);

            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.55f);
            GUI.DrawTexture(r, bgTex);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(r.x + 8f, r.y, r.width - 12f, r.height), text, lineStyle);
            GUI.color = old;

            shown++;
        }

        if (!IsTyping && !SessionMenuUI.IsPaused)
        {
            GUI.Label(new Rect(x, Screen.height - 12f - 18f - lineH * shown, width, 18f),
                shown == 0 ? Loc.T("chat.hint") : "", hintStyle);
        }
    }

    // Kullanıcının yazdığı < > işaretleri renk kodu gibi yorumlanmasın.
    private static string Escape(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");
    }

    private void EnsureStyles()
    {
        if (lineStyle != null) return;

        bgTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
        bgTex.Apply();

        lineStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, richText = true, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip
        };
        lineStyle.normal.textColor = Color.white;

        inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
        inputStyle.normal.background = null;
        inputStyle.focused.background = null;
        inputStyle.normal.textColor = Color.white;
        inputStyle.focused.textColor = Color.white;
        inputStyle.padding = new RectOffset(8, 8, 4, 4);

        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
    }
}
