using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player prefab'ına ekle. Ağda doğduğunda:
///  - Bu oyuncu BENİMSE: kontroller, kamera ve ses dinleyici açık kalır, doğma noktasına ışınlanır,
///    menüde yazdığım isim herkese gönderilir.
///  - BAŞKASININSA: kontroller/kamera kapatılır, kafasının üstünde adı görünür.
/// </summary>
public class PlayerNetworkSetup : NetworkBehaviour
{
    public static event Action<PlayerNetworkSetup> LocalPlayerSpawned;
    public static event Action LocalPlayerDespawned;

    /// <summary>Odadaki tüm oyuncular (Esc menüsündeki liste için).</summary>
    public static readonly List<PlayerNetworkSetup> All = new List<PlayerNetworkSetup>();

    /// <summary>Seçilebilir oyuncu renkleri (cozy palet).</summary>
    public static readonly Color[] Colors =
    {
        new Color(1.00f, 0.47f, 0.42f), // mercan
        new Color(0.97f, 0.62f, 0.25f), // turuncu
        new Color(0.98f, 0.84f, 0.32f), // sarı
        new Color(0.48f, 0.80f, 0.45f), // yeşil
        new Color(0.32f, 0.76f, 0.78f), // turkuaz
        new Color(0.42f, 0.57f, 0.95f), // mavi
        new Color(0.66f, 0.48f, 0.90f), // mor
        new Color(0.95f, 0.52f, 0.76f), // pembe
    };

    private const string NamePrefKey = "MoverSim_PlayerName";
    private const string ColorPrefKey = "MoverSim_PlayerColor";
    private const int MaxNameBytes = 29; // FixedString32Bytes kapasitesi

    [Tooltip("Sadece sahibinde açık kalması gereken ekstra bileşenler (isteğe bağlı).")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours = Array.Empty<Behaviour>();

    [Header("İsim Etiketi")]
    [SerializeField] private float nameTagHeight = 1.35f;
    [SerializeField] private float nameTagMaxDistance = 30f;

    // Herkes okuyabilir, sadece sahibi yazabilir.
    private readonly NetworkVariable<FixedString32Bytes> playerName = new NetworkVariable<FixedString32Bytes>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Renk numarası (Colors dizisinde), sadece sahibi değiştirebilir.
    private readonly NetworkVariable<byte> colorIndex = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public Color PlayerColor => Colors[colorIndex.Value % Colors.Length];
    public bool IsHostPlayer => OwnerClientId == NetworkManager.ServerClientId;

    /// <summary>Oyun içinden renk değiştirmek için (sadece kendi oyuncun).</summary>
    public void SetColor(int index)
    {
        SavedColorIndex = index;
        if (IsOwner && IsSpawned) colorIndex.Value = (byte)SavedColorIndex;
        ApplyColor();
    }

    public string PlayerName
    {
        get
        {
            string n = playerName.Value.ToString();
            return string.IsNullOrWhiteSpace(n) ? $"Player{OwnerClientId}" : n;
        }
    }

    private string serverRegisteredName;
    private GUIStyle nameStyle, nameShadowStyle;

    private void Awake()
    {
        // Ekstra kurulum gerektirmesin diye etkileşim ve ayak sesi bileşenleri otomatik eklenir.
        if (GetComponent<PlayerInteractor>() == null) gameObject.AddComponent<PlayerInteractor>();
        if (GetComponent<PlayerFootsteps>() == null) gameObject.AddComponent<PlayerFootsteps>();
    }

    // ---------------- Kayıtlı isim (menüdeki kutu) ----------------

    public static string SavedName
    {
        get
        {
            string n = PlayerPrefs.GetString(NamePrefKey, "");
            if (string.IsNullOrWhiteSpace(n))
            {
                n = "Player" + UnityEngine.Random.Range(100, 1000);
                PlayerPrefs.SetString(NamePrefKey, n);
            }
            return n;
        }
        set
        {
            PlayerPrefs.SetString(NamePrefKey, SanitizeName(value));
            PlayerPrefs.Save();
        }
    }

    public static int SavedColorIndex
    {
        get
        {
            if (!PlayerPrefs.HasKey(ColorPrefKey))
            {
                PlayerPrefs.SetInt(ColorPrefKey, UnityEngine.Random.Range(0, Colors.Length));
            }
            return Mathf.Clamp(PlayerPrefs.GetInt(ColorPrefKey), 0, Colors.Length - 1);
        }
        set
        {
            PlayerPrefs.SetInt(ColorPrefKey, Mathf.Clamp(value, 0, Colors.Length - 1));
            PlayerPrefs.Save();
        }
    }

    public static string SanitizeName(string n)
    {
        n = (n ?? "").Replace("<", "").Replace(">", "").Replace("\n", "").Trim();

        // UTF-8'de Türkçe karakterler 2 bayt; 29 bayta sığana kadar kısalt.
        while (n.Length > 0 && Encoding.UTF8.GetByteCount(n) > MaxNameBytes)
        {
            n = n.Substring(0, n.Length - 1);
        }
        return n;
    }

    // ---------------- Ağ ----------------

    public override void OnNetworkSpawn()
    {
        bool mine = IsOwner;

        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null) controller.enabled = mine;

        ObjectCarrier carrier = GetComponent<ObjectCarrier>();
        if (carrier != null) carrier.enabled = mine;

        foreach (Camera cam in GetComponentsInChildren<Camera>(true)) cam.enabled = mine;
        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = mine;
        foreach (Behaviour b in ownerOnlyBehaviours)
        {
            if (b != null) b.enabled = mine;
        }

        if (mine)
        {
            playerName.Value = new FixedString32Bytes(SanitizeName(SavedName));
            colorIndex.Value = (byte)SavedColorIndex;

            MoveToSpawnPoint();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            LocalPlayerSpawned?.Invoke(this);
        }

        if (!All.Contains(this)) All.Add(this);
        All.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        UpdateObjectName();
        ApplyColor();
        playerName.OnValueChanged += OnNameChanged;
        colorIndex.OnValueChanged += OnColorChanged;
    }

    public override void OnNetworkDespawn()
    {
        playerName.OnValueChanged -= OnNameChanged;
        colorIndex.OnValueChanged -= OnColorChanged;
        All.Remove(this);

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            LocalPlayerDespawned?.Invoke();
        }
    }

    private void OnNameChanged(FixedString32Bytes oldValue, FixedString32Bytes newValue)
    {
        UpdateObjectName();
    }

    private void OnColorChanged(byte oldValue, byte newValue)
    {
        ApplyColor();
    }

    private void ApplyColor()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            r.material.color = PlayerColor;
        }
    }

    public override void OnDestroy()
    {
        All.Remove(this);
        base.OnDestroy();
    }

    private void UpdateObjectName()
    {
        gameObject.name = IsOwner ? $"Player_{OwnerClientId} ({PlayerName}, You)" : $"Player_{OwnerClientId} ({PlayerName})";
    }

    private void Update()
    {
        // Sunucu: ismi gelen oyuncuyu sohbete kaydet ("X katıldı" mesajı).
        if (!IsServer || !IsSpawned) return;

        string current = playerName.Value.ToString();
        if (string.IsNullOrWhiteSpace(current) || current == serverRegisteredName) return;

        ChatManager chat = ChatManager.Instance;
        if (chat != null && chat.ServerRegisterPlayer(OwnerClientId, current))
        {
            serverRegisteredName = current;
        }
    }

    private void MoveToSpawnPoint()
    {
        Vector3 position;
        Quaternion rotation;

        if (SpawnPoint.All.Count > 0)
        {
            SpawnPoint sp = SpawnPoint.All[(int)(OwnerClientId % (ulong)SpawnPoint.All.Count)];
            position = sp.transform.position + Vector3.up;
            rotation = Quaternion.Euler(0f, sp.transform.eulerAngles.y, 0f);
        }
        else
        {
            // Doğma noktası yoksa oyuncuları yan yana diz.
            position = transform.position + Vector3.right * (1.5f * OwnerClientId);
            rotation = transform.rotation;
        }

        // CharacterController açıkken pozisyonu değiştirmek işe yaramaz, kısa süre kapat.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;
    }

    // ---------------- İsim etiketi (sadece diğer oyuncular) ----------------

    private void OnGUI()
    {
        if (!IsSpawned || IsOwner) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 worldPos = transform.position + Vector3.up * nameTagHeight;
        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z <= 0f || screen.z > nameTagMaxDistance) return;

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold
            };
            nameStyle.normal.textColor = Color.white;
            nameShadowStyle = new GUIStyle(nameStyle);
            nameShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        }

        Rect r = new Rect(screen.x - 100f, Screen.height - screen.y - 12f, 200f, 24f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), PlayerName, nameShadowStyle);
        GUI.Label(r, PlayerName, nameStyle);
    }
}
