using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Oyuncunun çantası + kişisel cüzdanı. Player prefab'ına ekli olmalı.
///  - Kilo sistemi: başlangıç kapasitesi baseCapacityKg (upgrade ile ServerSetCapacity). Doldukça yavaşlarsın.
///  - Tab: çantayı aç/kapat. Panelde eşyayı ELE AL / BIRAK. Üzerine gelince bilgi kartı çıkar.
///  - Q: çantadaki bozuk parayı baktığın yöne fırlat (yere düşünce ses + gürültü).
///  - Başka oyuncunun arkasından E basılı tut (PlayerInteractor) → onun çantası açılır, bir eşya seçip çalarsın.
/// Çantanın içeriği SUNUCUDA tutulur ve herkese senkronlanır.
/// </summary>
public partial class PlayerInventory : NetworkBehaviour
{
    public const int NoHeld = -1;

    public static PlayerInventory Local { get; private set; }
    public static bool IsPanelOpen { get; private set; }

    [Header("Kapasite")]
    [SerializeField] private float baseCapacityKg = 10f;
    [Tooltip("Çanta tamamen doluyken yürüme hızı çarpanı (0.6 = %60 hız).")]
    [Range(0.2f, 1f)]
    [SerializeField] private float minSpeedAtFullLoad = 0.6f;

    [Header("Eşya Alma")]
    [Tooltip("Sunucunun kabul ettiği en uzak alma mesafesi (hile koruması).")]
    [SerializeField] private float serverMaxPickupDistance = 5f;

    [Header("Fırlatma (Q)")]
    [SerializeField] private float throwForce = 9f;
    [Tooltip("Fırlatılan bozuk para kaç saniye sonra yerden silinsin (sürekli para atılmasın diye).")]
    [SerializeField] private float coinVanishSeconds = 6f; // eski adı thrownCoinLifetime (12 sn) — yeni ad, prefab'daki eski değer kullanılmasın diye

    [Header("Çalma")]
    [SerializeField] private float stealRange = 2.5f;
    [Tooltip("Arkasından yaklaşmak şart mı? (GDD: arkasından yaklaşıp...)")]
    [SerializeField] private bool requireBehind = true;
    [Tooltip("Hedefin baktığı yön ile senin konumun arasındaki en küçük açı (derece). 100+ = arkasında.")]
    [SerializeField] private float behindAngle = 100f;
    [SerializeField] private float stealWindowSeconds = 12f;
    [SerializeField] private float stealNoiseRadius = 6f;

    [Header("Elde Tutma")]
    [Tooltip("Elde süzülen eşyanın en büyük kenarı (metre). Daha büyük eserler buna sığacak kadar küçültülür.")]
    [SerializeField] private float heldArtSize = 0.55f;

    // Upgrade seviyeleri (garaj mağazası, sunucu yazar)
    private readonly NetworkVariable<byte> bagLevel = new NetworkVariable<byte>(0);
    private readonly NetworkVariable<byte> shoesLevel = new NetworkVariable<byte>(0);
    private readonly NetworkVariable<byte> glovesLevel = new NetworkVariable<byte>(0);
    private readonly NetworkVariable<byte> lockpickLevel = new NetworkVariable<byte>(0); // maymuncuk adedi (Staff Only kapısı)
    // Boya kutusu ve kamera pili sayaçları: PlayerInventory.Tools.cs (sprayCount, batteryLevel) — fırça ve BodyCam de onları kullanır

    // Çanta yerine açık olan başka panel (karaborsa, mağaza...)
    private IInventoryPanel externalPanel;

    private NetworkList<InventorySlot> slots;
    private readonly NetworkVariable<float> capacityKg = new NetworkVariable<float>(10f);
    private readonly NetworkVariable<int> personalMoney = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> heldSlot = new NetworkVariable<int>(
        NoHeld, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // Elde tutulan eşyanın uzaklığı (cm) ve çevrilme adımı (45°): sahibi yazar, herkes aynı yerde görür
    private readonly NetworkVariable<byte> holdDistance = new NetworkVariable<byte>(
        110, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<byte> holdYawStep = new NetworkVariable<byte>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private ObjectCarrier carrier;
    private Transform playerCamera;

    // Panel durumu (sahibi)
    private PlayerInventory stealTarget; // null = kendi çantam
    private int hoveredSlot = -1;
    private int pendingHoveredSlot = -1; // IMGUI: fare değişikliği bir sonraki Layout'ta uygulanır
    private Vector2 scroll;
    private InventorySlot heldLocal;      // elimde tuttuğum eşya (satır kaysa da bununla bulunur)
    private InventorySlot lastHeld;       // G ile geri almak için
    private bool hasLastHeld;

    // Bildirim
    private string toastText;
    private Color toastColor;
    private float toastUntil;

    // Sunucu: çalma oturumu
    private ulong serverStealTargetId;
    private float serverStealUntil = -1f;

    // Elde tutulan eşyanın görüntüsü
    private HeldItemView heldView;
    private InventorySlot heldVisualSlot;
    private bool heldVisualShown, heldVisualPending;

    // İstemci: sunucudan parça parça gelen sahte verileri
    private readonly Dictionary<ushort, byte[]> incomingForgeries = new Dictionary<ushort, byte[]>();
    private readonly Dictionary<ushort, int> incomingReceived = new Dictionary<ushort, int>();
    // Sunucunun herkese yolladığı (istenmeden gelen) sahte verileri — istek cevaplarıyla karışmasın diye ayrı
    private readonly Dictionary<ushort, byte[]> pushedForgeries = new Dictionary<ushort, byte[]>();
    private readonly Dictionary<ushort, int> pushedReceived = new Dictionary<ushort, int>();

    private GUIStyle titleStyle, rowStyle, smallStyle, hintStyle, buttonStyle, boxStyle, hudStyle, cardTitleStyle, moneyStyle;

    // ---------------- Okuma ----------------

    public float CapacityKg => capacityKg.Value;
    public int SlotCount => slots != null ? slots.Count : 0;
    public InventorySlot GetSlot(int i) => slots[i];
    public int PersonalMoney => personalMoney.Value;
    public int HeldSlot => heldSlot.Value;
    public float HoldDistance => holdDistance.Value / 100f;
    public float HoldYaw => holdYawStep.Value * 45f;
    public float HeldMaxSize => heldArtSize;
    public bool HasHeldItem => heldSlot.Value >= 0 && slots != null && heldSlot.Value < slots.Count;

    public int GetUpgradeLevel(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.BiggerBag: return bagLevel.Value;
            case UpgradeType.QuietShoes: return shoesLevel.Value;
            case UpgradeType.SwiftGloves: return glovesLevel.Value;
            case UpgradeType.Lockpick: return lockpickLevel.Value;
            case UpgradeType.SprayPaint: return sprayCount.Value;
            case UpgradeType.CameraBattery: return batteryLevel.Value;
            case UpgradeType.TeamBackpacks: return TeamBank.Instance != null ? TeamBank.Instance.TeamBagLevel : 0;
            default: return 0;
        }
    }

    /// <summary>Eser değiştirme süresi çarpanı (eldiven upgrade'i).</summary>
    /// <summary>Elinde maymuncuk var mı? (LockpickSystem: Staff Only kapısını açar.)</summary>
    public bool HasLockpick => lockpickLevel.Value > 0;
    public int LockpickCount => lockpickLevel.Value;

    /// <summary>Sunucu: maymuncuk kırıldı, bir adet düşer.</summary>
    /// <summary>Sunucu: bir sprey boya kutusu harcanır.</summary>
    public bool ServerUseSpray()
    {
        if (!IsServer || sprayCount.Value == 0) return false;
        sprayCount.Value--;
        return true;
    }

    public bool ServerBreakLockpick()
    {
        if (!IsServer || lockpickLevel.Value == 0) return false;
        lockpickLevel.Value--;
        return true;
    }

    public float SwapTimeMultiplier => Mathf.Max(0.4f, 1f - UpgradeCatalog.GlovesFasterPerLevel * glovesLevel.Value);

    /// <summary>Ayak sesi şiddeti çarpanı (sessiz ayakkabı upgrade'i).</summary>
    public float FootstepVolumeMultiplier => Mathf.Max(0.25f, 1f - UpgradeCatalog.ShoesQuietPerLevel * shoesLevel.Value);

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

    private static ItemDefinition Def(InventorySlot s)
    {
        ItemDatabase db = ItemDatabase.Instance;
        return db != null ? db.Get(s.ItemIndex) : null;
    }

    private string MyName
    {
        get
        {
            PlayerNetworkSetup ps = GetComponent<PlayerNetworkSetup>();
            return ps != null ? ps.PlayerName : $"Player{OwnerClientId}";
        }
    }

    // ---------------- Yaşam döngüsü ----------------

    private void Awake()
    {
        slots = new NetworkList<InventorySlot>();
        carrier = GetComponent<ObjectCarrier>();
        heldView = GetComponent<HeldItemView>();
        if (heldView == null) heldView = gameObject.AddComponent<HeldItemView>();
        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam != null) playerCamera = cam.transform;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) ServerApplyCapacity();
        if (IsOwner) Local = this;

        slots.OnListChanged += OnSlotsChanged;
        heldSlot.OnValueChanged += OnHeldChanged;
        ForgeryRegistry.Arrived += OnForgeryArrived;
        RefreshHeldVisual();
    }

    public override void OnNetworkDespawn()
    {
        slots.OnListChanged -= OnSlotsChanged;
        heldSlot.OnValueChanged -= OnHeldChanged;
        ForgeryRegistry.Arrived -= OnForgeryArrived;

        if (Local == this)
        {
            Local = null;
            IsPanelOpen = false;
        }
        DestroyHeldVisual();
    }

    private void OnSlotsChanged(NetworkListEvent<InventorySlot> e)
    {
        if (IsOwner)
        {
            // Elimdeki eşya çantadan çıktıysa / yeri kaydıysa: aynı eşyayı bul, yoksa elimi boşalt
            int h = heldSlot.Value;
            if (h != NoHeld && (h >= slots.Count || !slots[h].SameItem(heldLocal)))
            {
                int found = NoHeld;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].SameItem(heldLocal)) { found = i; break; }
                }
                SetHeld(found);
            }

            // R.E.P.O gibi: yeni alınan / yapılan eser doğrudan ele gelir
            if (e.Type == NetworkListEvent<InventorySlot>.EventType.Add && e.Index >= 0 && e.Index < slots.Count)
            {
                ItemDefinition d = Def(slots[e.Index]);
                if (d != null && (d.type == ItemType.Original || d.type == ItemType.Forgery)) SetHeld(e.Index);
            }
        }
        RefreshHeldVisual();
    }

    private void OnForgeryArrived(ushort id)
    {
        if (heldVisualPending && heldVisualSlot.ForgeryId == id) RefreshHeldVisual(true);
    }

    private void OnHeldChanged(int oldValue, int newValue) => RefreshHeldVisual();

    // ---------------- Girdi (sadece sahibi) ----------------

    private void Update()
    {
        // Verisi henüz gelmemiş sahte: ara ara yeniden dene (herkeste)
        if (heldVisualPending && IsSpawned && Time.frameCount % 30 == 0 && ForgeryRegistry.TryGet(heldVisualSlot.ForgeryId, out _)) RefreshHeldVisual(true);

        if (!IsOwner || !IsSpawned) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // Çalma paneli açıkken hedeften uzaklaşırsan kapanır
        if (IsPanelOpen && stealTarget != null &&
            (!stealTarget.IsSpawned || Vector3.Distance(transform.position, stealTarget.transform.position) > stealRange + 1.5f))
        {
            SetPanelOpen(false);
        }

        if (IsPanelOpen && externalPanel != null && !externalPanel.StillValid(this))
        {
            SetPanelOpen(false);
        }

        bool blocked = ChatUI.IsTyping || SessionMenuUI.IsPaused || WorkbenchUI.IsOpen || BriefingUI.IsOpen || PlayerController.InputLocked;
        if (blocked) return;

        if (kb.tabKey.wasPressedThisFrame)
        {
            if (IsPanelOpen) SetPanelOpen(false);
            else OpenOwnPanel();
        }

        bool canAct = !IsPanelOpen && Cursor.lockState == CursorLockMode.Locked && (carrier == null || !carrier.IsHolding);

        if (canAct && kb.qKey.wasPressedThisFrame)
        {
            // Elinde tablo / büst / vazo / yüzük (bozuk para dışında bir şey) varsa Q onu önüne bırakır
            if (!TryDropHeld()) TryThrowDistraction();
        }

        // G: elindekini çantaya kaldır / son tuttuğunu yeniden ele al
        if (canAct && kb.gKey.wasPressedThisFrame) ToggleLastHeld();

        // Elde tutulan eşya: tekerlek = yaklaştır / uzaklaştır, R = çevir
        if (canAct && HasHeldItem)
        {
            Mouse mouse = Mouse.current;
            float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
            if (Mathf.Abs(wheel) > 0.01f)
                holdDistance.Value = (byte)Mathf.Clamp(holdDistance.Value + (wheel > 0f ? 10 : -10), 70, 220);
            // R: eşyayı ön / arka çevir (karşındakine göster) — yanlara değil (0° ↔ 180°)
            if (kb.rKey.wasPressedThisFrame) holdYawStep.Value = (byte)(holdYawStep.Value == 4 ? 0 : 4);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Test için para: F7 ekip kasası +100, F8 kişisel +100 (sadece editör / development build)
        if (kb.f7Key.wasPressedThisFrame) DebugMoneyRpc(true, 100);
        if (kb.f8Key.wasPressedThisFrame) DebugMoneyRpc(false, 100);
#endif
    }

    public static void ClosePanel()
    {
        if (Local != null) Local.SetPanelOpen(false);
        else IsPanelOpen = false;
    }

    private void OpenOwnPanel()
    {
        stealTarget = null;
        externalPanel = null;
        SetPanelOpen(true);
    }

    /// <summary>Karaborsa, mağaza gibi panelleri çanta paneli yerine açar.</summary>
    public void OpenExternal(IInventoryPanel panel)
    {
        if (!IsOwner || panel == null) return;
        stealTarget = null;
        externalPanel = panel;
        IsPanelOpen = false; // açma işleminin (imleç, ses) çalışması için
        SetPanelOpen(true);
    }

    private void SetPanelOpen(bool open)
    {
        if (IsPanelOpen == open && (open || (stealTarget == null && externalPanel == null))) return;

        bool wasStealing = stealTarget != null;
        bool wasExternal = externalPanel != null;
        IsPanelOpen = open;
        pendingHoveredSlot = -1;

        if (open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            GameAudio.Play2D(externalPanel != null ? SoundId.UiClick : SoundId.BagOpen, 0.7f);
        }
        else
        {
            stealTarget = null;
            externalPanel = null;
            if (!SessionMenuUI.IsPaused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (!wasStealing && !wasExternal) GameAudio.Play2D(SoundId.BagClose, 0.6f);
        }
    }

    private void SetHeld(int slotIndex)
    {
        if (!IsOwner) return;
        bool valid = slotIndex >= 0 && slotIndex < slots.Count;
        heldSlot.Value = valid ? slotIndex : NoHeld;
        heldLocal = valid ? slots[slotIndex] : default;
        if (valid) { lastHeld = heldLocal; hasLastHeld = true; }
    }

    private void ToggleLastHeld()
    {
        if (heldSlot.Value != NoHeld)
        {
            SetHeld(NoHeld);
            GameAudio.Play2D(SoundId.BagClose, 0.5f);
            return;
        }

        int found = NoHeld;
        for (int i = 0; i < slots.Count && hasLastHeld; i++)
        {
            if (slots[i].SameItem(lastHeld)) { found = i; break; }
        }
        if (found == NoHeld)
        {
            // Son tutulan artık yoksa çantadaki ilk eseri al
            for (int i = 0; i < slots.Count; i++)
            {
                ItemDefinition d = Def(slots[i]);
                if (d != null && (d.type == ItemType.Original || d.type == ItemType.Forgery)) { found = i; break; }
            }
        }
        if (found == NoHeld) return;
        SetHeld(found);
        GameAudio.Play2D(SoundId.BagOpen, 0.5f);
    }

    /// <summary>Sahibi: bu satırı ele al / elden kaldır (PaintBrush: B tuşu).</summary>
    public void ToggleHoldSlot(int slotIndex) => ToggleHeld(slotIndex);

    private void ToggleHeld(int slotIndex)
    {
        SetHeld(heldSlot.Value == slotIndex ? NoHeld : slotIndex);
        GameAudio.Play2D(SoundId.UiClick);
    }

    // ---------------- İstemci → Sunucu istekleri ----------------

    /// <summary>PlayerInteractor çağırır: yerdeki eşyayı al.</summary>
    public void RequestPickup(WorldItem wi)
    {
        if (wi == null) return;
        if (!CanFit(wi.Item)) { ShowToast(Loc.T("inv.full"), Bad); return; }
        TryPickupRpc(wi.NetworkObject);
    }

    public bool IsBehind(PlayerInventory target)
    {
        if (!requireBehind) return true;
        Vector3 toMe = transform.position - target.transform.position;
        toMe.y = 0f;
        Vector3 fwd = target.transform.forward;
        fwd.y = 0f;
        if (toMe.sqrMagnitude < 0.0001f) return false;
        return Vector3.Angle(fwd, toMe) >= behindAngle;
    }

    public bool CanStealFrom(PlayerInventory target)
    {
        return target != null && target != this && target.IsSpawned && target.SlotCount > 0 &&
               Vector3.Distance(transform.position, target.transform.position) <= stealRange && IsBehind(target);
    }

    /// <summary>PlayerInteractor çağırır: basılı tutma bitti, çalmaya başla.</summary>
    public void RequestStartSteal(PlayerInventory target)
    {
        if (target != null) StartStealRpc(target.NetworkObject);
    }

    /// <summary>Q: elde tutulan eşya (bozuk para hariç) önüne, yere bırakılır. Bıraktıysa true.</summary>
    private bool TryDropHeld()
    {
        int h = heldSlot.Value;
        if (h < 0 || h >= slots.Count) return false;
        ItemDefinition d = Def(slots[h]);
        if (d == null || d.type == ItemType.Distraction) return false;
        if (d.worldPrefab == null)
        {
            ShowToast(Loc.T("inv.cantdrop"), Bad);
            return true;
        }
        SetHeld(NoHeld);
        DropRpc(h);
        return true;
    }

    private void TryThrowDistraction()
    {
        int index = -1;
        int h = heldSlot.Value;
        if (h >= 0 && h < slots.Count && Def(slots[h]) is ItemDefinition hd && hd.type == ItemType.Distraction)
        {
            index = h;
        }
        else
        {
            for (int i = 0; i < slots.Count; i++)
            {
                ItemDefinition d = Def(slots[i]);
                if (d != null && d.type == ItemType.Distraction) { index = i; break; }
            }
        }

        if (index < 0)
        {
            ShowToast(Loc.T("throw.none"), Bad);
            return;
        }

        Transform from = playerCamera != null ? playerCamera : transform;
        ThrowRpc(index, from.position, from.forward);
    }

    // ---------------- Sunucu RPC'leri ----------------

    [Rpc(SendTo.Server)]
    private void TryPickupRpc(NetworkObjectReference itemRef, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!itemRef.TryGet(out NetworkObject no) || no == null || !no.IsSpawned) return;

        WorldItem wi = no.GetComponent<WorldItem>();
        if (wi == null || wi.IsConsumed || wi.Item == null) return; // başkası daha önce aldı

        if (Vector3.Distance(transform.position, no.transform.position) > serverMaxPickupDistance) return;

        if (!ServerAddItem(wi.Item, wi.Similarity, wi.ForgeryId, wi.LookKey))
        {
            ServerNotify("inv.full");
            return;
        }

        ServerPlaySound(SoundId.Pickup, no.transform.position);
        wi.ServerConsume();
    }

    [Rpc(SendTo.Server)]
    private void DropRpc(int slotIndex, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (slotIndex < 0 || slotIndex >= slots.Count) return;

        Vector3 forward = transform.forward;
        Vector3 pos = transform.position + forward * 0.9f + Vector3.up * 0.5f;
        if (ServerSpawnFromSlot(slotIndex, pos, forward * 2f + Vector3.up, out _))
        {
            ServerPlaySound(SoundId.Drop, pos);
        }
    }

    [Rpc(SendTo.Server)]
    private void ThrowRpc(int slotIndex, Vector3 origin, Vector3 direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (slotIndex < 0 || slotIndex >= slots.Count) return;

        ItemDefinition def = Def(slots[slotIndex]);
        if (def == null || def.type != ItemType.Distraction) return;

        // Hile koruması: atış noktası oyuncunun yakınında olmalı
        if (Vector3.Distance(origin, transform.position) > 2.5f) origin = transform.position + Vector3.up * 0.6f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;

        Vector3 spawnPos = origin + direction * 0.6f;
        if (ServerSpawnFromSlot(slotIndex, spawnPos, direction * throwForce + Vector3.up * 1.5f, out WorldItem coin))
        {
            if (coin != null) coin.ServerExpireAfter(coinVanishSeconds); // atılan para geri toplanamadan kaybolur
            ServerPlaySound(SoundId.CoinThrow, origin);
        }
    }

    /// <summary>Sunucu: bir satırdan 1 adet eşyayı sahneye çıkarır.</summary>
    private bool ServerSpawnFromSlot(int slotIndex, Vector3 pos, Vector3 velocity, out WorldItem spawned)
    {
        spawned = null;
        InventorySlot slot = slots[slotIndex];
        ItemDefinition def = Def(slot);
        if (def == null) return false;

        if (def.worldPrefab == null)
        {
            ServerNotify("inv.cantdrop");
            return false;
        }

        GameObject go = Instantiate(def.worldPrefab, pos, Quaternion.LookRotation(velocity.sqrMagnitude > 0.01f ? velocity : transform.forward));
        NetworkObject no = go.GetComponent<NetworkObject>();
        if (no == null)
        {
            Debug.LogError($"[Inventory] {def.name} worldPrefab'ında NetworkObject yok.");
            Destroy(go);
            return false;
        }

        WorldItem wi = go.GetComponent<WorldItem>();
        if (wi != null)
        {
            wi.SetPendingSimilarity(slot.Similarity);
            wi.SetPendingIdentity(slot.ForgeryId, slot.LookKey);
            wi.MarkThrown();
        }

        no.Spawn(true);

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 4f;
        }

        ServerRemoveAt(slotIndex, 1);
        spawned = wi;
        return true;
    }

    // ---------------- Çöp kutusu (kamyon) ----------------

    /// <summary>Sahibi: çantadaki bir sahteyi kamyondaki çöp kutusuna at.</summary>
    public void RequestTrash(int slotIndex, InventorySlot expected) => TrashRpc(slotIndex, expected);

    /// <summary>Sahibi: çantadaki bütün sahteleri çöpe at.</summary>
    public void RequestTrashAllForgeries() => TrashAllRpc();

    [Rpc(SendTo.Server)]
    private void TrashRpc(int slotIndex, InventorySlot expected, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (TrashBin.Nearest(transform.position, TrashBin.UseRange + 1.5f) == null) return;
        if (slotIndex < 0 || slotIndex >= slots.Count || !slots[slotIndex].SameItem(expected)) return;
        ItemDefinition def = Def(slots[slotIndex]);
        if (def == null || def.type != ItemType.Forgery) return;
        ServerRemoveAt(slotIndex, slots[slotIndex].Count);
        ServerNotify("trash.done", def);
        ServerPlaySound(SoundId.Drop, transform.position);
    }

    [Rpc(SendTo.Server)]
    private void TrashAllRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (TrashBin.Nearest(transform.position, TrashBin.UseRange + 1.5f) == null) return;
        int n = 0;
        for (int i = slots.Count - 1; i >= 0; i--) // sondan başa: silindikçe indeksler kaymasın
        {
            ItemDefinition def = Def(slots[i]);
            if (def == null || def.type != ItemType.Forgery) continue;
            n += slots[i].Count;
            ServerRemoveAt(i, slots[i].Count);
        }
        if (n == 0) return;
        ServerNotify("trash.doneall", n.ToString());
        ServerPlaySound(SoundId.Drop, transform.position);
    }

    [Rpc(SendTo.Server)]
    private void StartStealRpc(NetworkObjectReference targetRef, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (!targetRef.TryGet(out NetworkObject no)) return;

        PlayerInventory target = no.GetComponent<PlayerInventory>();
        if (target == null || target == this) return;

        // Sunucu doğrulaması (biraz toleranslı)
        if (Vector3.Distance(transform.position, target.transform.position) > stealRange + 1f) return;
        if (requireBehind)
        {
            Vector3 toMe = transform.position - target.transform.position; toMe.y = 0f;
            Vector3 fwd = target.transform.forward; fwd.y = 0f;
            if (Vector3.Angle(fwd, toMe) < behindAngle - 20f) return;
        }

        serverStealTargetId = no.NetworkObjectId;
        serverStealUntil = Time.time + stealWindowSeconds;

        OpenStealRpc(targetRef);
        target.ServerNotify("steal.rummage", MyName);
    }

    [Rpc(SendTo.Owner)]
    private void OpenStealRpc(NetworkObjectReference targetRef)
    {
        if (!targetRef.TryGet(out NetworkObject no)) return;
        PlayerInventory target = no.GetComponent<PlayerInventory>();
        if (target == null) return;

        stealTarget = target;
        IsPanelOpen = false; // SetPanelOpen'ın açma işlemini çalıştırması için
        SetPanelOpen(true);
    }

    [Rpc(SendTo.Server)]
    private void TakeFromRpc(NetworkObjectReference targetRef, int slotIndex, InventorySlot expected, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        if (Time.time > serverStealUntil) return;
        if (!targetRef.TryGet(out NetworkObject no) || no.NetworkObjectId != serverStealTargetId) return;

        PlayerInventory target = no.GetComponent<PlayerInventory>();
        if (target == null || slotIndex < 0 || slotIndex >= target.slots.Count) return;
        if (Vector3.Distance(transform.position, target.transform.position) > stealRange + 2f) return;

        InventorySlot s = target.slots[slotIndex];
        if (!s.SameItem(expected)) return; // bu arada çanta değişmiş

        ItemDefinition def = Def(s);
        if (def == null) return;
        if (PaintBrush.IsBrush(def)) return; // fırça çalınamaz (herkesin kendi zorunlu aleti)

        if (!ServerAddItem(def, s.Similarity, s.ForgeryId, s.LookKey))
        {
            ServerNotify("inv.full");
            return;
        }

        target.ServerRemoveAt(slotIndex, 1);
        serverStealUntil = -1f; // tek seferlik

        ServerNotify("steal.success", target.MyName, def);
        target.ServerNotify("steal.stolen", MyName, def);
        ServerPlaySound(SoundId.Steal, target.transform.position);
        NoiseSystem.Emit(target.transform.position, stealNoiseRadius);

        CloseStealRpc();
    }

    [Rpc(SendTo.Owner)]
    private void CloseStealRpc()
    {
        if (stealTarget != null) SetPanelOpen(false);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Rpc(SendTo.Server)]
    private void DebugMoneyRpc(bool team, int amount)
    {
        if (team)
        {
            if (TeamBank.Instance != null) TeamBank.Instance.ServerAdd(amount);
        }
        else
        {
            ServerAddMoney(amount);
        }
    }
#endif

    [Rpc(SendTo.Owner)]
    private void NotifyRpc(string key, string arg, int itemIndex)
    {
        string itemName = "";
        if (itemIndex >= 0 && ItemDatabase.Instance != null)
        {
            ItemDefinition d = ItemDatabase.Instance.Get(itemIndex);
            if (d != null) itemName = d.DisplayName;
        }

        if (Loc.Has(arg)) arg = Loc.T(arg); // ör. upgrade adı anahtarı → oyuncunun dilinde

        bool bad = key == "inv.full" || key == "inv.cantdrop" || key == "steal.stolen" || key == "steal.rummage" ||
                   key.StartsWith("shop.not") || key.StartsWith("truck.") || key.StartsWith("cam.") || key == "display.badfake" ||
                   key == "spray.none" || key == "spray.already" || key == "spray.badwall" || key == "spray.toonear" || key == "spray.nodoorslot";
        ShowToast(Loc.T(key, arg, itemName), bad ? Bad : Good);

        if (key == "steal.stolen" || key == "steal.rummage") GameAudio.Play2D(SoundId.Steal, 0.8f);
    }

    [Rpc(SendTo.Everyone)]
    private void SoundRpc(SoundId id, Vector3 pos)
    {
        GameAudio.PlayAt(id, pos);
    }

    // ---------------- Sunucu API (garaj, atölye, müze sistemleri kullanacak) ----------------

    /// <summary>Sunucu: çantaya eşya ekler. Sığmazsa false.</summary>
    public bool ServerAddItem(ItemDefinition def, byte similarity = InventorySlot.NoSimilarity, ushort forgeryId = 0, int lookKey = 0)
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
                if (s.ItemIndex == index && s.Similarity == similarity && s.ForgeryId == forgeryId && s.LookKey == lookKey && s.Count < def.MaxStack)
                {
                    s.Count++;
                    slots[i] = s;
                    return true;
                }
            }
        }

        slots.Add(new InventorySlot { ItemIndex = (ushort)index, Count = 1, Similarity = similarity, ForgeryId = forgeryId, LookKey = lookKey });
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

    /// <summary>Sunucu: kapasiteyi upgrade'lere göre yeniden hesaplar.</summary>
    public void ServerApplyCapacity()
    {
        if (!IsServer) return;
        int teamLevel = TeamBank.Instance != null ? TeamBank.Instance.TeamBagLevel : 0;
        capacityKg.Value = baseCapacityKg + bagLevel.Value * UpgradeCatalog.BagKgPerLevel + teamLevel * UpgradeCatalog.TeamBagKgPerLevel;
    }

    /// <summary>Sunucu: kişisel cüzdan ve kişisel upgrade'leri sıfırlar (koşu sıfırlanırken, yalnızca MuseumSession.ResetKeepsPersonalProgress false ise).</summary>
    public void ServerResetPersonalProgress()
    {
        if (!IsServer) return;
        personalMoney.Value = 0;
        bagLevel.Value = 0;
        shoesLevel.Value = 0;
        glovesLevel.Value = 0;
        lockpickLevel.Value = 0;
        sprayCount.Value = 0;
        batteryLevel.Value = 0;
        ServerApplyCapacity();
    }

    /// <summary>Sunucu: kişisel upgrade seviyesini bir artırır (ödeme GarageShop'ta yapılır).</summary>
    public bool ServerIncreaseUpgrade(UpgradeType t)
    {
        if (!IsServer) return false;
        int max = UpgradeCatalog.MaxLevel(t);
        switch (t)
        {
            case UpgradeType.BiggerBag:
                if (bagLevel.Value >= max) return false;
                bagLevel.Value++;
                ServerApplyCapacity();
                return true;
            case UpgradeType.QuietShoes:
                if (shoesLevel.Value >= max) return false;
                shoesLevel.Value++;
                return true;
            case UpgradeType.SwiftGloves:
                if (glovesLevel.Value >= max) return false;
                glovesLevel.Value++;
                return true;
            case UpgradeType.Lockpick:
                if (lockpickLevel.Value >= max) return false;
                lockpickLevel.Value++;
                return true;
            case UpgradeType.SprayPaint:
                if (sprayCount.Value >= max) return false;
                sprayCount.Value++;
                return true;
            case UpgradeType.CameraBattery:
                if (batteryLevel.Value >= max) return false;
                batteryLevel.Value++;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Bu KAİDEDEKİ eser için yapılmış sahte çantada var mı? En yüksek benzerlikli satırı döner, yoksa -1.
    /// Atölyede yapılan sahteler yalnızca yapıldıkları kaideye uyar (benzerlikleri o esere göre hesaplandı).</summary>
    public int FindForgeryFor(ArtworkDisplay target)
    {
        if (target == null || target.Artwork == null) return -1;
        int key = target.LookKey;
        int best = -1;
        int bestSim = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            InventorySlot s = slots[i];
            ItemDefinition d = Def(s);
            if (d == null || d.type != ItemType.Forgery) continue;
            if (d.forgeryOf != null && d.forgeryOf != target.Artwork) continue;
            if (s.LookKey != 0 && s.LookKey != key) continue; // başka bir eserin sahtesi

            int sim = s.HasSimilarity ? s.Similarity : 0;
            if (sim > bestSim) { bestSim = sim; best = i; }
        }
        return best;
    }

    /// <summary>Sunucu: çantadaki sahteyi verir, orijinali alır. Orijinal, bırakılan sahtenin benzerliğini taşır.</summary>
    public bool ServerSwapForgery(int forgerySlot, ItemDefinition original, int lookKey, out byte similarity, out ushort forgeryId)
    {
        similarity = InventorySlot.NoSimilarity;
        forgeryId = 0;
        if (!IsServer || forgerySlot < 0 || forgerySlot >= slots.Count || original == null) return false;

        InventorySlot s = slots[forgerySlot];
        ItemDefinition forgery = Def(s);
        if (forgery == null) return false;

        if (CurrentWeightKg - forgery.weightKg + original.weightKg > CapacityKg + 0.0001f) return false;

        similarity = s.Similarity;
        forgeryId = s.ForgeryId;
        ServerRemoveAt(forgerySlot, 1);
        if (!ServerAddItem(original, similarity, 0, lookKey))
        {
            ServerAddItem(forgery, similarity, s.ForgeryId, s.LookKey); // geri koy
            return false;
        }
        return true;
    }

    /// <summary>Sunucu: yeni görev başlarken çantadaki sahteleri siler (eski müzenin eserlerine göre yapılmışlardı).</summary>
    public void ServerRemoveForgeries()
    {
        if (!IsServer) return;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            ItemDefinition d = Def(slots[i]);
            if (d != null && d.type == ItemType.Forgery) slots.RemoveAt(i);
        }
    }

    // ---------------- Sahte verisi (ForgeryRegistry): istemci ister, sunucu parça parça yollar ----------------

    private const int ForgeryChunkSize = 1000;

    /// <summary>Sahibi: bu bilgisayarda olmayan bir sahtenin verisini sunucudan iste.</summary>
    public void RequestForgeryData(ushort id)
    {
        if (IsOwner && IsSpawned && id != 0) RequestForgeryRpc(id);
    }

    [Rpc(SendTo.Server)]
    private void RequestForgeryRpc(ushort id, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        byte[] blob = ForgeryRegistry.GetBlob(id);
        if (blob == null) return;

        ForgeryBeginRpc(id, blob.Length);
        for (int offset = 0; offset < blob.Length; offset += ForgeryChunkSize)
        {
            byte[] chunk = new byte[Mathf.Min(ForgeryChunkSize, blob.Length - offset)];
            System.Array.Copy(blob, offset, chunk, 0, chunk.Length);
            ForgeryChunkRpc(id, chunk);
        }
    }

    [Rpc(SendTo.Owner)]
    private void ForgeryBeginRpc(ushort id, int totalBytes)
    {
        if (totalBytes <= 0 || totalBytes > ForgeryData.MaxBlobBytes) return;
        incomingForgeries[id] = new byte[totalBytes];
        incomingReceived[id] = 0;
    }

    [Rpc(SendTo.Owner)]
    private void ForgeryChunkRpc(ushort id, byte[] chunk)
    {
        if (chunk == null || !incomingForgeries.TryGetValue(id, out byte[] buffer)) return;
        int got = incomingReceived[id];
        if (got + chunk.Length > buffer.Length) { incomingForgeries.Remove(id); incomingReceived.Remove(id); return; }
        System.Array.Copy(chunk, 0, buffer, got, chunk.Length);
        got += chunk.Length;
        incomingReceived[id] = got;
        if (got < buffer.Length) return;

        incomingForgeries.Remove(id);
        incomingReceived.Remove(id);
        ForgeryRegistry.Store(id, buffer);
    }

    /// <summary>
    /// Sunucu: yeni yapılan sahtenin verisini HERKESE hemen yollar (bu oyuncunun objesi üzerinden).
    /// Böylece diğer oyuncular vazoyu / tabloyu elde, yerde ve kaidede yapanın gördüğü şekliyle görür;
    /// istemcinin ayrıca istemesine gerek kalmaz (istek yolu sonradan katılanlar için duruyor).
    /// </summary>
    public void ServerBroadcastForgery(ushort id, byte[] blob)
    {
        if (!IsServer || !IsSpawned || id == 0 || blob == null || blob.Length == 0 || blob.Length > ForgeryData.MaxBlobBytes) return;
        ForgeryPushBeginRpc(id, blob.Length);
        for (int offset = 0; offset < blob.Length; offset += ForgeryChunkSize)
        {
            byte[] chunk = new byte[Mathf.Min(ForgeryChunkSize, blob.Length - offset)];
            System.Array.Copy(blob, offset, chunk, 0, chunk.Length);
            ForgeryPushChunkRpc(id, chunk);
        }
    }

    [Rpc(SendTo.NotServer)]
    private void ForgeryPushBeginRpc(ushort id, int totalBytes)
    {
        if (totalBytes <= 0 || totalBytes > ForgeryData.MaxBlobBytes) return;
        pushedForgeries[id] = new byte[totalBytes];
        pushedReceived[id] = 0;
    }

    [Rpc(SendTo.NotServer)]
    private void ForgeryPushChunkRpc(ushort id, byte[] chunk)
    {
        if (chunk == null || !pushedForgeries.TryGetValue(id, out byte[] buffer)) return;
        int got = pushedReceived[id];
        if (got + chunk.Length > buffer.Length) { pushedForgeries.Remove(id); pushedReceived.Remove(id); return; }
        System.Array.Copy(chunk, 0, buffer, got, chunk.Length);
        got += chunk.Length;
        pushedReceived[id] = got;
        if (got < buffer.Length) return;

        pushedForgeries.Remove(id);
        pushedReceived.Remove(id);
        ForgeryRegistry.Store(id, buffer);
    }

    /// <summary>Sunucu: yakalanınca çantadaki orijinal eserlere el konur. Kaç adet gittiğini döner.</summary>
    public int ServerConfiscateOriginals(List<int> returnedLookKeys = null)
    {
        if (!IsServer) return 0;
        int lost = 0;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            ItemDefinition d = Def(slots[i]);
            if (d != null && d.type == ItemType.Original)
            {
                lost += slots[i].Count;
                // Müze eseriyse hangi kaideye ait olduğunu bildir: yerine geri konacak
                if (returnedLookKeys != null && slots[i].LookKey != 0) returnedLookKeys.Add(slots[i].LookKey);
                slots.RemoveAt(i);
            }
        }
        return lost;
    }

    /// <summary>Sunucu: kişisel cüzdana para ekler.</summary>
    public void ServerAddMoney(int amount)
    {
        if (IsServer && amount > 0) personalMoney.Value += amount;
    }

    /// <summary>Sunucu: kişisel cüzdandan harcar; yetmezse false.</summary>
    public bool ServerTrySpendMoney(int amount)
    {
        if (!IsServer || amount < 0 || personalMoney.Value < amount) return false;
        personalMoney.Value -= amount;
        return true;
    }

    /// <summary>Sunucu: bu oyuncuya ekranda kısa bir mesaj göster (çeviri anahtarı + parametre).</summary>
    public void ServerNotify(string key, string arg = "")
    {
        if (IsServer) NotifyRpc(key, arg ?? "", -1);
    }

    /// <summary>Sunucu: eşya adıyla mesaj ({1} = eşya adı, her oyuncunun kendi dilinde).</summary>
    public void ServerNotify(string key, ItemDefinition item)
    {
        ServerNotify(key, "", item);
    }

    public void ServerNotify(string key, string arg, ItemDefinition item)
    {
        if (!IsServer) return;
        int idx = item != null && ItemDatabase.Instance != null ? ItemDatabase.Instance.IndexOf(item) : -1;
        NotifyRpc(key, arg ?? "", idx);
    }

    /// <summary>Sunucu: herkesin duyacağı 3D ses.</summary>
    public void ServerPlaySound(SoundId id, Vector3 pos)
    {
        if (IsServer) SoundRpc(id, pos);
    }

    // ---------------- Elde tutulan eşyanın görüntüsü ----------------

    /// <summary>Görev değişince (eser dokuları yenilenince) elde tutulan görseli yeniden kurar.</summary>
    public void RebuildHeldVisual() => RefreshHeldVisual(true);

    /// <summary>Elde tutulan eşya ele yapışık durmaz; oyuncunun önünde süzülür (HeldItemView). Herkes görür.</summary>
    private void RefreshHeldVisual(bool force = false)
    {
        if (heldView == null) return;
        int h = heldSlot.Value;
        bool has = h >= 0 && slots != null && h < slots.Count;
        InventorySlot slot = has ? slots[h] : default;
        if (!force && has == heldVisualShown && (!has || slot.SameItem(heldVisualSlot))) return;

        heldVisualShown = has;
        heldVisualSlot = slot;
        heldVisualPending = false;
        if (!has) { heldView.Hide(); return; }

        ItemDefinition def = Def(slot);
        GameObject visual = ItemVisuals.BuildFor(def, slot, out heldVisualPending);
        if (visual == null) { heldView.Hide(); return; }
        ItemVisuals.Fit(visual, heldArtSize);
        heldView.Show(visual, def);
    }

    private void DestroyHeldVisual()
    {
        if (heldView != null) heldView.Hide();
        heldVisualShown = false;
    }

    // ---------------- Arayüz ----------------

    private static readonly Color Good = new Color(0.55f, 0.9f, 0.55f);
    private static readonly Color Bad = new Color(1f, 0.45f, 0.4f);

    /// <summary>Kendi ekranında kısa uyarı (ör. itildin — PushSystem).</summary>
    public void ShowLocalToast(string text) => ShowToast(text, Bad);

    private void ShowToast(string text, Color color)
    {
        toastText = text;
        toastColor = color;
        toastUntil = Time.unscaledTime + 3f;
    }

    private void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;
        EnsureStyles();

        if (!WorkbenchUI.IsOpen) // atölyede sol üst / sağ üst / sağ alt kutular atölye arayüzüyle çakışmasın
        {
            DrawWeightHud();
            DrawMoneyHud();
        }

        if (!string.IsNullOrEmpty(toastText) && Time.unscaledTime < toastUntil)
        {
            UIUtil.DrawPrompt(toastText, UIUtil.CenterSlot(), toastColor);
        }

        if (IsPanelOpen)
        {
            if (externalPanel != null) externalPanel.DrawPanel(this);
            else DrawPanel(stealTarget != null ? stealTarget : this, stealTarget != null);
        }
    }

    private void DrawWeightHud()
    {
        float w = 220f, h = 46f;
        Rect r = new Rect(Screen.width - w - 12f, Screen.height - h - 12f, w, h);
        GUI.Box(r, GUIContent.none, boxStyle);

        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f),
            $"{Loc.T("inv.hud")}  {Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#"))}", hudStyle);

        Rect bar = new Rect(r.x + 10f, r.y + 28f, r.width - 20f, 8f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        float ratio = LoadRatio;
        Color c = ratio < 0.6f ? new Color(0.55f, 0.85f, 0.5f) : ratio < 0.9f ? new Color(1f, 0.8f, 0.3f) : Bad;
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), c);

        string hint = Loc.T("inv.open") + "   " + Loc.T("throw.hint");
        GUI.Label(new Rect(r.x - 100f, r.y - 20f, r.width + 90f, 18f), hint, hintStyle);
        if (HasHeldItem) GUI.Label(new Rect(r.x - 260f, r.y - 40f, r.width + 250f, 18f), Loc.T("held.hint"), hintStyle);
    }

    private void DrawMoneyHud()
    {
        TeamBank bank = TeamBank.Instance;
        float w = 140f;
        float h = bank != null ? 52f : 30f;
        Rect r = new Rect(Screen.width - w - 12f, 48f, w, h);
        GUI.Box(r, GUIContent.none, boxStyle);

        float y = r.y + 4f;
        if (bank != null)
        {
            GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 22f), Loc.T("money.team", bank.Money.ToString("N0")), moneyStyle);
            y += 22f;
        }
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 22f), Loc.T("money.personal", PersonalMoney.ToString("N0")), moneyStyle);
    }

    private void DrawPanel(PlayerInventory bag, bool stealing)
    {
        ItemDatabase db = ItemDatabase.Instance;

        // Unity'nin GUILayout'u her karede önce "Layout", sonra "Repaint" çizer ve ikisinde de
        // AYNI elemanların çizilmesini ister. Bilgi kartının hangi eşya için çizileceği bu yüzden
        // sadece Layout anında değişir; aksi halde "Invalid GUILayout state" hatası olur.
        if (Event.current.type == EventType.Layout) hoveredSlot = pendingHoveredSlot;

        float w = 500f, h = 470f;
        Rect panel = new Rect((Screen.width - w) / 2f - 110f, (Screen.height - h) / 2f, w, h);
        GUI.Box(panel, GUIContent.none, boxStyle);

        GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, panel.height - 28f));

        if (stealing)
        {
            PlayerNetworkSetup ps = bag.GetComponent<PlayerNetworkSetup>();
            GUILayout.Label(Loc.T("steal.title", ps != null ? ps.PlayerName : "?"), titleStyle);
            GUILayout.Label(Loc.T("steal.help"), smallStyle);
        }
        else
        {
            GUILayout.Label(Loc.T("inv.title"), titleStyle);
        }

        // Kilo çubuğu (çalarken KENDİ çantanın doluluğu önemli)
        GUILayout.Label(Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#")), rowStyle);
        Rect bar = GUILayoutUtility.GetRect(10f, 10f, GUILayout.ExpandWidth(true), GUILayout.Height(10f));
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * LoadRatio, bar.height),
            LoadRatio < 0.9f ? new Color(1f, 0.8f, 0.3f) : Bad);

        if (!stealing && LoadRatio > 0.01f)
        {
            GUILayout.Label(Loc.T("inv.speed", Mathf.RoundToInt(SpeedMultiplier * 100f)), smallStyle);
        }

        GUILayout.Space(8f);

        if (bag.SlotCount == 0)
        {
            GUILayout.Label(Loc.T(stealing ? "steal.emptybag" : "inv.empty"), rowStyle);
        }

        scroll = GUILayout.BeginScrollView(scroll);
        int dropIndex = -1, takeIndex = -1;
        bool repaint = Event.current.type == EventType.Repaint;
        int hoverNow = -1;

        for (int i = 0; i < bag.SlotCount; i++)
        {
            InventorySlot s = bag.GetSlot(i);
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null) continue;

            bool held = !stealing && heldSlot.Value == i;

            GUILayout.BeginHorizontal(GUILayout.Height(44f));

            Rect iconRect = GUILayoutUtility.GetRect(36f, 36f, GUILayout.Width(36f), GUILayout.Height(36f));
            iconRect.y += 4f;
            DrawIcon(iconRect, def, s);
            if (held) UIUtil.DrawRect(new Rect(iconRect.x, iconRect.yMax + 1f, iconRect.width, 3f), new Color(1f, 0.8f, 0.3f));

            GUILayout.Space(8f);
            GUILayout.BeginVertical();
            string title = s.Count > 1 ? $"{def.DisplayName}  x{s.Count}" : def.DisplayName;
            if (held) title += "  " + Loc.T("inv.inhand");
            GUILayout.Label(title, rowStyle);

            string info = $"{ItemDefinition.TypeName(def.type)}  •  {(def.weightKg * s.Count).ToString("0.#")} kg";
            if (s.HasSimilarity) info += "  •  " + Loc.T("inv.similarity", s.Similarity);
            GUILayout.Label(info, smallStyle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (stealing)
            {
                bool fits = CanFit(def);
                GUI.enabled = fits;
                if (GUILayout.Button(fits ? Loc.T("steal.take") : Loc.T("steal.nofit"), buttonStyle, GUILayout.Width(110f), GUILayout.Height(28f)))
                {
                    takeIndex = i;
                }
                GUI.enabled = true;
            }
            else
            {
                if (GUILayout.Button(held ? Loc.T("inv.putaway") : Loc.T("inv.hold"), buttonStyle, GUILayout.Width(90f), GUILayout.Height(28f)))
                {
                    ToggleHeld(i);
                }
                if (GUILayout.Button(Loc.T("inv.drop"), buttonStyle, GUILayout.Width(70f), GUILayout.Height(28f)))
                {
                    dropIndex = i;
                }
            }

            GUILayout.EndHorizontal();

            if (repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverNow = i;
        }

        GUILayout.EndScrollView();

        GUILayout.Label(stealing ? Loc.T("steal.close") : Loc.T("inv.close"), hintStyle);
        GUILayout.EndArea();

        if (repaint) pendingHoveredSlot = hoverNow;

        // Bilgi kartı (panelin sağında)
        if (hoveredSlot >= 0 && hoveredSlot < bag.SlotCount)
        {
            DrawInfoCard(new Rect(panel.xMax + 10f, panel.y, 220f, 300f), bag.GetSlot(hoveredSlot),
                !stealing && heldSlot.Value == hoveredSlot);
        }

        if (dropIndex >= 0)
        {
            if (heldSlot.Value == dropIndex) SetHeld(NoHeld);
            DropRpc(dropIndex);
        }

        if (takeIndex >= 0 && takeIndex < bag.SlotCount)
        {
            TakeFromRpc(bag.NetworkObject, takeIndex, bag.GetSlot(takeIndex));
        }
    }

    private void DrawInfoCard(Rect r, InventorySlot s, bool held)
    {
        ItemDefinition def = Def(s);
        if (def == null) return;

        GUI.Box(r, GUIContent.none, boxStyle);
        GUILayout.BeginArea(new Rect(r.x + 14f, r.y + 14f, r.width - 28f, r.height - 28f));

        Rect icon = GUILayoutUtility.GetRect(96f, 96f, GUILayout.Width(r.width - 28f), GUILayout.Height(96f));
        icon.x += (icon.width - 96f) / 2f;
        icon.width = 96f;
        DrawIcon(icon, def, s);

        GUILayout.Space(6f);
        GUILayout.Label(def.DisplayName, cardTitleStyle);
        GUILayout.Label(ItemDefinition.TypeName(def.type), smallStyle);
        GUILayout.Space(6f);

        GUILayout.Label(Loc.T("card.weight", (def.weightKg * s.Count).ToString("0.#")), rowStyle);
        if (s.Count > 1) GUILayout.Label(Loc.T("card.count", s.Count), rowStyle);
        if (def.baseValue > 0) GUILayout.Label(Loc.T("card.value", Mathf.RoundToInt(def.baseValue * PriceRules.DisplayMultiplier(s.LookKey) * s.Count).ToString("N0")), rowStyle);

        if (s.HasSimilarity)
        {
            Color old = GUI.contentColor;
            GUI.contentColor = ItemDefinition.GradeColor(s.Similarity);
            string simKey = def.type == ItemType.Original ? "card.swapquality" : "card.similarity";
            GUILayout.Label(Loc.T(simKey, s.Similarity, ItemDefinition.GradeName(s.Similarity)), rowStyle);
            GUI.contentColor = old;
        }
        else if (def.type == ItemType.Original)
        {
            GUILayout.Label(Loc.T("card.hot"), smallStyle);
        }

        if (def.type == ItemType.Original)
        {
            GUILayout.Label(Loc.T("card.price", PriceRules.SalePrice(def, s).ToString("N0")), rowStyle);
        }
        if (def.type == ItemType.Forgery && def.forgeryOf != null)
        {
            GUILayout.Label(Loc.T("card.forgeryof", def.forgeryOf.DisplayName), smallStyle);
        }

        if (held) GUILayout.Label(Loc.T("inv.inhand"), smallStyle);
        if (def.type == ItemType.Distraction) GUILayout.Label(Loc.T("card.throwhint"), smallStyle);

        GUILayout.EndArea();
    }

    /// <summary>Atölyede yapılan sahtelerde ikon, oyuncunun yaptığı eserin kendisidir.</summary>
    private static void DrawIcon(Rect r, ItemDefinition def, InventorySlot s)
    {
        UIUtil.DrawRect(r, new Color(1f, 1f, 1f, 0.06f));
        Texture2D icon = ItemVisuals.IconFor(def, s);
        if (icon != null) GUI.DrawTexture(r, icon, ScaleMode.ScaleToFit);
        else UIUtil.DrawRect(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), def.uiColor);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null) return;

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.normal.background = UIUtil.PanelTexture;

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Color.white;

        cardTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, wordWrap = true };
        cardTitleStyle.normal.textColor = Color.white;

        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        rowStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);

        hintStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight, wordWrap = false };

        hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        hudStyle.normal.textColor = Color.white;

        moneyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        moneyStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
    }
}
