using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Oyuncunun çantası. Player prefab'ına ekle.
///  - Kilo sistemi: başlangıç kapasitesi baseCapacityKg (upgrade ile ServerSetCapacity ile artar).
///    Sığmayan eşya alınamaz. Çanta doldukça yürüme yavaşlar.
///  - E: baktığın eşyayı çantaya al.   Tab: çanta panelini aç/kapat (panelden eşya bırakılır).
///  - Çantanın içeriği SUNUCUDA tutulur (hile yapılamaz; ileride arkadaştan çalma için de gerekli).
/// </summary>
public class PlayerInventory : NetworkBehaviour
{
    public static PlayerInventory Local { get; private set; }
    public static bool IsPanelOpen { get; private set; }

    [Header("Kapasite")]
    [SerializeField] private float baseCapacityKg = 10f;
    [Tooltip("Çanta tamamen doluyken yürüme hızı çarpanı (0.6 = %60 hız).")]
    [Range(0.2f, 1f)]
    [SerializeField] private float minSpeedAtFullLoad = 0.6f;

    [Header("Eşya Alma")]
    [SerializeField] private float pickupRange = 3f;
    [Tooltip("Sunucunun kabul ettiği en uzak alma mesafesi (hile koruması).")]
    [SerializeField] private float serverMaxPickupDistance = 5f;
    [SerializeField] private LayerMask pickupMask = ~0;
    [SerializeField] private Transform playerCamera;

    private NetworkList<InventorySlot> slots;
    private readonly NetworkVariable<float> capacityKg = new NetworkVariable<float>(10f);

    private ObjectCarrier carrier;
    private Collider[] ownColliders;
    private WorldItem lookTarget;

    private string toastKey;
    private float toastUntil;
    private Vector2 scroll;

    private GUIStyle titleStyle, rowStyle, smallStyle, hintStyle, promptStyle, promptShadow, buttonStyle, boxStyle, hudStyle;
    private Texture2D panelTex;

    // ---------------- Okuma ----------------

    public float CapacityKg => capacityKg.Value;
    public int SlotCount => slots != null ? slots.Count : 0;
    public InventorySlot GetSlot(int i) => slots[i];

    public float CurrentWeightKg
    {
        get
        {
            ItemDatabase db = ItemDatabase.Instance;
            if (db == null || slots == null) return 0f;
            float w = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                ItemDefinition d = db.Get(slots[i].ItemIndex);
                if (d != null) w += d.weightKg * slots[i].Count;
            }
            return w;
        }
    }

    public float LoadRatio => CapacityKg <= 0f ? 1f : Mathf.Clamp01(CurrentWeightKg / CapacityKg);

    /// <summary>PlayerController hızı bununla çarpar.</summary>
    public float SpeedMultiplier => Mathf.Lerp(1f, minSpeedAtFullLoad, LoadRatio);

    public bool CanFit(ItemDefinition def, int count = 1)
    {
        return def != null && CurrentWeightKg + def.weightKg * count <= CapacityKg + 0.0001f;
    }

    // ---------------- Yaşam döngüsü ----------------

    private void Awake()
    {
        slots = new NetworkList<InventorySlot>();
        carrier = GetComponent<ObjectCarrier>();
        ownColliders = GetComponentsInChildren<Collider>();
        if (playerCamera == null)
        {
            Camera cam = GetComponentInChildren<Camera>(true);
            if (cam != null) playerCamera = cam.transform;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) capacityKg.Value = baseCapacityKg;
        if (IsOwner) Local = this;
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this)
        {
            Local = null;
            IsPanelOpen = false;
        }
    }

    // ---------------- Girdi (sadece sahibi) ----------------

    private void Update()
    {
        if (!IsOwner || !IsSpawned) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        bool blocked = ChatUI.IsTyping || SessionMenuUI.IsPaused;

        if (!blocked && kb.tabKey.wasPressedThisFrame)
        {
            SetPanelOpen(!IsPanelOpen);
        }

        bool canLook = !blocked && !IsPanelOpen && (carrier == null || !carrier.IsHolding)
                       && Cursor.lockState == CursorLockMode.Locked;
        lookTarget = canLook ? FindWorldItem() : null;

        if (lookTarget != null && kb.eKey.wasPressedThisFrame)
        {
            if (CanFit(lookTarget.Item)) TryPickupRpc(lookTarget.NetworkObject);
            else ShowToast("inv.full");
        }
    }

    public static void ClosePanel()
    {
        if (Local != null) Local.SetPanelOpen(false);
        else IsPanelOpen = false;
    }

    private void SetPanelOpen(bool open)
    {
        IsPanelOpen = open;
        if (open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!SessionMenuUI.IsPaused)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private WorldItem FindWorldItem()
    {
        if (playerCamera == null) return null;

        Ray ray = new Ray(playerCamera.position, playerCamera.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, pickupRange, pickupMask, QueryTriggerInteraction.Ignore);

        float closest = float.MaxValue;
        Collider closestCol = null;
        foreach (RaycastHit hit in hits)
        {
            if (IsOwnCollider(hit.collider)) continue;
            if (hit.distance < closest)
            {
                closest = hit.distance;
                closestCol = hit.collider;
            }
        }

        if (closestCol == null) return null;
        WorldItem wi = closestCol.GetComponentInParent<WorldItem>();
        return wi != null && wi.IsSpawned && wi.Item != null ? wi : null;
    }

    private bool IsOwnCollider(Collider c)
    {
        foreach (Collider own in ownColliders)
        {
            if (own == c) return true;
        }
        return false;
    }

    private void ShowToast(string key)
    {
        toastKey = key;
        toastUntil = Time.unscaledTime + 2.5f;
    }

    // ---------------- Sunucu ----------------

    [Rpc(SendTo.Server)]
    private void TryPickupRpc(NetworkObjectReference itemRef, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!itemRef.TryGet(out NetworkObject no) || no == null || !no.IsSpawned) return;

        WorldItem wi = no.GetComponent<WorldItem>();
        if (wi == null || wi.IsConsumed || wi.Item == null) return; // başkası daha önce aldı

        if (Vector3.Distance(transform.position, no.transform.position) > serverMaxPickupDistance) return;

        if (!ServerAddItem(wi.Item, wi.Similarity))
        {
            NotifyRpc("inv.full");
            return;
        }

        wi.ServerConsume();
    }

    [Rpc(SendTo.Server)]
    private void DropRpc(int slotIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (slotIndex < 0 || slotIndex >= slots.Count) return;

        InventorySlot slot = slots[slotIndex];
        ItemDefinition def = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(slot.ItemIndex) : null;
        if (def == null) return;

        if (def.worldPrefab == null)
        {
            NotifyRpc("inv.cantdrop");
            return;
        }

        Vector3 forward = transform.forward;
        Vector3 pos = transform.position + forward * 0.9f + Vector3.up * 0.5f;
        GameObject go = Instantiate(def.worldPrefab, pos, Quaternion.LookRotation(forward));

        NetworkObject no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError($"[Inventory] {def.name} worldPrefab'ında NetworkObject yok.");
            Destroy(go);
            return;
        }

        WorldItem wi = go.GetComponent<WorldItem>();
        if (wi != null) wi.SetPendingSimilarity(slot.Similarity);

        no.Spawn(true);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic) rb.linearVelocity = forward * 2f + Vector3.up;

        ServerRemoveAt(slotIndex, 1);
    }

    [Rpc(SendTo.Owner)]
    private void NotifyRpc(string key)
    {
        ShowToast(key);
    }

    // ---------------- Sunucu API (garaj, atölye, müze sistemleri kullanacak) ----------------

    /// <summary>Sunucu: çantaya eşya ekler. Sığmazsa false.</summary>
    public bool ServerAddItem(ItemDefinition def, byte similarity = InventorySlot.NoSimilarity)
    {
        if (!IsServer || def == null) return false;

        ItemDatabase db = ItemDatabase.Instance;
        int index = db != null ? db.IndexOf(def) : -1;
        if (index < 0)
        {
            Debug.LogError($"[Inventory] '{def.name}' ItemDatabase'de yok. Tools → Sahte Şaheser → Rebuild Item Database.");
            return false;
        }

        if (!CanFit(def)) return false;

        if (def.stackable)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlot s = slots[i];
                if (s.ItemIndex == index && s.Similarity == similarity && s.Count < def.MaxStack)
                {
                    s.Count++;
                    slots[i] = s;
                    return true;
                }
            }
        }

        slots.Add(new InventorySlot { ItemIndex = (ushort)index, Count = 1, Similarity = similarity });
        return true;
    }

    /// <summary>Sunucu: bir satırdan adet düşer, biterse satırı siler.</summary>
    public void ServerRemoveAt(int slotIndex, int count = 1)
    {
        if (!IsServer || slotIndex < 0 || slotIndex >= slots.Count) return;

        InventorySlot s = slots[slotIndex];
        if (s.Count > count)
        {
            s.Count = (ushort)(s.Count - count);
            slots[slotIndex] = s;
        }
        else
        {
            slots.RemoveAt(slotIndex);
        }
    }

    /// <summary>Sunucu: çanta kapasitesini ayarlar (garaj upgrade'i).</summary>
    public void ServerSetCapacity(float kg)
    {
        if (IsServer) capacityKg.Value = Mathf.Max(0f, kg);
    }

    // ---------------- Arayüz ----------------

    private void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;
        EnsureStyles();

        DrawWeightHud();

        if (lookTarget != null && !IsPanelOpen) DrawPickupPrompt();

        if (!string.IsNullOrEmpty(toastKey) && Time.unscaledTime < toastUntil)
        {
            DrawCentered(Loc.T(toastKey), Screen.height / 2f + 70f, new Color(1f, 0.45f, 0.4f));
        }

        if (IsPanelOpen) DrawPanel();
    }

    private void DrawWeightHud()
    {
        float w = 220f, h = 46f;
        Rect r = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
        GUI.Box(r, GUIContent.none, boxStyle);

        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f),
            $"{Loc.T("inv.hud")}  {Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#"))}", hudStyle);

        Rect bar = new Rect(r.x + 10f, r.y + 28f, r.width - 20f, 8f);
        DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        float ratio = LoadRatio;
        Color c = ratio < 0.6f ? new Color(0.55f, 0.85f, 0.5f) : ratio < 0.9f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.4f);
        DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), c);

        GUI.Label(new Rect(r.x, r.y - 20f, r.width - 10f, 18f), Loc.T("inv.open"), hintStyle);
    }

    private void DrawPickupPrompt()
    {
        ItemDefinition def = lookTarget.Item;
        string kg = def.weightKg.ToString("0.#");
        bool fits = CanFit(def);

        string text = fits
            ? Loc.T("inv.pickup", def.DisplayName, kg)
            : Loc.T("inv.toofull", def.DisplayName, kg);

        if (def.type == ItemType.Forgery && lookTarget.Similarity != InventorySlot.NoSimilarity)
        {
            text += "   " + Loc.T("inv.similarity", lookTarget.Similarity);
        }

        DrawCentered(text, Screen.height / 2f + 34f, fits ? Color.white : new Color(1f, 0.45f, 0.4f));
    }

    private void DrawPanel()
    {
        ItemDatabase db = ItemDatabase.Instance;

        float w = 480f, h = 440f;
        Rect panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, panel.height - 28f));

        GUILayout.Label(Loc.T("inv.title"), titleStyle);

        // Kilo çubuğu
        GUILayout.Label(Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#")), rowStyle);
        Rect bar = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.Height(10f));
        DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        DrawRect(new Rect(bar.x, bar.y, bar.width * LoadRatio, bar.height),
            LoadRatio < 0.9f ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.45f, 0.4f));

        if (LoadRatio > 0.01f)
        {
            GUILayout.Label(Loc.T("inv.speed", Mathf.RoundToInt(SpeedMultiplier * 100f)), smallStyle);
        }

        GUILayout.Space(8f);

        if (slots.Count == 0)
        {
            GUILayout.Label(Loc.T("inv.empty"), rowStyle);
        }

        scroll = GUILayout.BeginScrollView(scroll);
        int dropIndex = -1;

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot s = slots[i];
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null) continue;

            GUILayout.BeginHorizontal(GUILayout.Height(40f));

            Rect iconRect = GUILayoutUtility.GetRect(32f, 32f, GUILayout.Width(32f), GUILayout.Height(32f));
            iconRect.y += 4f;
            if (def.icon != null) GUI.DrawTexture(iconRect, def.icon, ScaleMode.ScaleToFit);
            else DrawRect(iconRect, def.uiColor);

            GUILayout.Space(8f);
            GUILayout.BeginVertical();
            string title = s.Count > 1 ? $"{def.DisplayName}  x{s.Count}" : def.DisplayName;
            GUILayout.Label(title, rowStyle);

            string info = $"{ItemDefinition.TypeName(def.type)}  •  {(def.weightKg * s.Count).ToString("0.#")} kg";
            if (s.HasSimilarity) info += "  •  " + Loc.T("inv.similarity", s.Similarity);
            GUILayout.Label(info, smallStyle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Loc.T("inv.drop"), buttonStyle, GUILayout.Width(80f), GUILayout.Height(28f)))
            {
                dropIndex = i;
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        GUILayout.Label(Loc.T("inv.close"), hintStyle);
        GUILayout.EndArea();

        if (dropIndex >= 0) DropRpc(dropIndex);
    }

    private void DrawCentered(string text, float y, Color color)
    {
        Rect r = new Rect(0f, y, Screen.width, 30f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, promptShadow);
        promptStyle.normal.textColor = color;
        GUI.Label(r, text, promptStyle);
    }

    private static void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        panelTex = new Texture2D(1, 1);
        panelTex.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.11f, 0.9f));
        panelTex.Apply();

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = panelTex;

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Color.white;

        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        rowStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        hintStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight };

        hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        hudStyle.normal.textColor = Color.white;

        promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
        promptShadow = new GUIStyle(promptStyle);
        promptShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);

        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }
}
