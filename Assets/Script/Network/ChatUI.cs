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

    private GUIStyle lineStyle, inputStyle;

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
        float sc = UITheme.Scale;
        lineStyle.fontSize = Mathf.RoundToInt(15f * sc);
        float W = Mathf.Min(width * sc, Screen.width - 24f);
        float inputH = Mathf.Round(32f * sc);
        float padX = 10f * sc, padY = 6f * sc, gap = 3f * sc;

        // Görünecek satırları topla (en yeniden eskiye) ve sarılmış yüksekliklerini ölç (metin kırpılmasın)
        var all = chat.Lines;
        int count = 0;
        for (int i = all.Count - 1; i >= 0 && count < visibleLines && count < measured.Length; i--)
        {
            if (!IsTyping && Time.unscaledTime - all[i].Time > fadeAfterSeconds + 1f) break;
            measured[count] = all[i];
            heights[count] = Mathf.Max(Mathf.Round(20f * sc), lineStyle.CalcHeight(new GUIContent(Format(all[i])), W - padX * 2f));
            count++;
        }

        bool showHint = !IsTyping && !SessionMenuUI.IsPaused && count == 0;
        float linesH = 0f;
        for (int i = 0; i < count; i++) linesH += heights[i] + gap;
        float blockH = count > 0 ? linesH + padY * 2f - gap : 0f;
        float hintH = showHint ? Mathf.Round(22f * sc) : 0f;
        float totalH = (IsTyping ? inputH + 6f * sc : 0f) + blockH + hintH;

        // Sol alt köşe: ipucu satırı ve kamera pilinin ÜSTÜNE dizilir (çakışmaz)
        Rect area = UIUtil.HudRect(UIUtil.HudCorner.BottomLeft, UIUtil.HudOrder.Chat, W, Mathf.Max(1f, totalH));
        float x = area.x;
        float bottom = area.yMax;

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

            Rect inputRect = new Rect(x, bottom - inputH, W, inputH);
            UIUtil.Panel(inputRect, PanelKind.Plaque, 0.95f);
            Rect fieldRect = new Rect(inputRect.x + 4f, inputRect.y + 3f, inputRect.width - 8f, inputRect.height - 6f);
            inputStyle.fontSize = Mathf.RoundToInt(16f * sc);
            GUI.SetNextControlName(InputControlName);
            input = GUI.TextField(fieldRect, input, ChatManager.MaxMessageLength, inputStyle);
            if (GUI.GetNameOfFocusedControl() != InputControlName) GUI.FocusControl(InputControlName);
            bottom -= inputH + 6f * sc;
        }

        // --- Mesajlar: tek plaket + satırlar (eskiden yeniye, yukarıdan aşağı) ---
        if (count > 0)
        {
            float now = Time.unscaledTime;
            float panelAlpha = 1f;
            if (!IsTyping) panelAlpha = Mathf.Clamp01(fadeAfterSeconds + 1f - (now - measured[0].Time));
            Rect block = new Rect(x, bottom - blockH, W, blockH);
            UIUtil.Panel(block, PanelKind.Plaque, 0.88f * panelAlpha);

            float y = block.y + padY;
            for (int i = count - 1; i >= 0; i--)
            {
                float alpha = 1f;
                if (!IsTyping) alpha = Mathf.Clamp01(fadeAfterSeconds + 1f - (now - measured[i].Time));
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(x + padX, y, W - padX * 2f, heights[i]), Format(measured[i]), lineStyle);
                GUI.color = old;
                y += heights[i] + gap;
            }
        }
        else if (showHint)
        {
            // Boşta ipucu: "[Enter] Sohbet" → tuş kapağı + etiket
            UIUtil.SplitKey(Loc.T("chat.hint"), out string key, out string label);
            float kw = string.IsNullOrEmpty(key) ? 0f : UIUtil.KeyCapWidth(key, 13);
            float lx = x;
            if (kw > 0f)
            {
                UIUtil.KeyCap(new Rect(x, bottom - hintH + 2f, kw, hintH - 4f), key, 13);
                lx += kw + 6f;
            }
            UIUtil.Text(new Rect(lx, bottom - hintH, W - (lx - x), hintH), label, TextRole.Caption, UITheme.IvoryDim);
        }
    }

    private readonly ChatManager.ChatLine[] measured = new ChatManager.ChatLine[32];
    private readonly float[] heights = new float[32];

    // Satırın zengin metni: sistem mesajı Ochre, oyuncu adı kendi renginde kalın
    private static string Format(ChatManager.ChatLine line)
    {
        if (line.IsSystem)
            return "<color=#" + ColorUtility.ToHtmlStringRGB(UITheme.Ochre) + ">- " + Escape(line.Display) + "</color>";
        Color c = UITheme.BrassLight;
        var list = PlayerNetworkSetup.All;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].PlayerName == line.Sender)
            {
                c = Color.Lerp(list[i].PlayerColor, UITheme.Ivory, 0.25f); // koyu zeminde okunsun
                break;
            }
        return "<b><color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + Escape(line.Sender) + "</color></b>  " + Escape(line.Text);
    }

    // Kullanıcının yazdığı < > işaretleri renk kodu gibi yorumlanmasın.
    private static string Escape(string s)
    {
        return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "‹").Replace(">", "›");
    }

    private void EnsureStyles()
    {
        if (lineStyle != null) return;

        UIUtil.ApplySkin();

        lineStyle = new GUIStyle(UITheme.Style(TextRole.Body, UITheme.Ivory, TextAnchor.UpperLeft))
        {
            richText = true, wordWrap = true, clipping = TextClipping.Clip
        };

        inputStyle = UIUtil.ThemeField(new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
        inputStyle.padding = new RectOffset(8, 8, 4, 4);
    }
}
