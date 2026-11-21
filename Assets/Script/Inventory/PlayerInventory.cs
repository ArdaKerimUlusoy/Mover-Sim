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

        // Sunucu oturumu kapanıyor: yerde kalan (atılan) eşyaları temizle
        if (IsServer && NetworkManager != null && NetworkManager.ShutdownInProgress) WorldItem.ServerClearDropped();

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

        if (IsServer && IsSpawned && serverStealUntil > 0f) ServerStealTick();

        if (!IsOwner || !IsSpawned) return;
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // Alma paneli açıkken hedeften uzaklaşırsan iptal olur (sunucu da ayrıca doğrular)
        if (IsPanelOpen && stealTarget != null &&
            (!stealTarget.IsSpawned || Vector3.Distance(transform.position, stealTarget.transform.position) > StealCancelRange))
        {
            SetPanelOpen(false);
            ShowToast(Loc.T("steal.cancel"), Bad);
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
            // Elinde tablo / büst / vazo / yüzük (bozuk para dışında bir şey) varsa Q onu önüne bırakır;
            // yoksa bozuk para atar: önce görev başına verilen alet paraları (güvenliğin kafasına gelirse sersemletir),
            // bitince çantadaki bozuk paralar
            if (!TryDropHeld())
            {
                PlayerStealth st = GetComponent<PlayerStealth>();
                if (st == null || !st.TryThrowGadgetCoin(playerCamera != null ? playerCamera : transform)) TryThrowDistraction();
            }
        }

        // G: elindekini çantaya kaldır / son tuttuğunu yeniden ele al
        if (canAct && kb.gKey.wasPressedThisFrame) ToggleLastHeld();

        // 1 / 2 / 3: hızlı envanter yuvaları (alt ortadaki 3 kutu) — doğrudan ele al
        if (canAct && !WorkbenchUI.IsOpen) TickHotbarKeys(kb);

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
        ServerBagAdded(wi.Item, wi.Similarity, wi.ForgeryId, wi.LookKey);
        // Müzedeki küçük ganimeti (masadaki eşya) almak da hırsızlık: gören ziyaretçi / güvenlik şüphelenir
        if (wi.Item.IsSmallLoot && GameFlow.PhaseAt(transform.position) == GamePhase.Museum)
        {
            PlayerStealth st = GetComponent<PlayerStealth>();
            if (st != null) st.ServerPettyTheft();
        }
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
            if (coin != null)
            {
                coin.ServerExpireAfter(coinVanishSeconds); // atılan para geri toplanamadan kaybolur
                coin.gameObject.AddComponent<CoinDazer>(); // güvenliğin kafasına / gövdesine gelirse sersemletir
            }
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
        if (rb != null && wi != null && rb.isKinematic)
        {
            rb.isKinematic = false; // atılan eşya havada asılı kalmasın
            rb.WakeUp();
        }
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
        target.ServerNotify("steal.browse", MyName);
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
        if (Vector3.Distance(transform.position, target.transform.position) > StealCancelRange + 0.3f) return;

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
        ServerBagAdded(def, s.Similarity, s.ForgeryId, s.LookKey);

        target.ServerRemoveAt(slotIndex, 1);
        serverStealUntil = -1f; // tek seferlik

        // Aynı takım: bu "çalma" değil "arkadaştan alma". Gürültü / alarm / şüphe YOK.
        ServerNotify("steal.taken", target.MyName, def);
        target.ServerNotify("steal.takenfrom", MyName, def);

        CloseStealRpc();
    }

    /// <summary>Sahnedeki alma işlemi bu mesafeden (ve görüşten) çıkarsa iptal edilir.</summary>
    private const float StealCancelRange = 3f;
    private float nextStealCheck;

    /// <summary>Sunucu: devam eden "arkadaştan alma" sırasında mesafeyi / görüşü tekrar doğrular, bozulduysa iptal eder.</summary>
    private void ServerStealTick()
    {
        if (Time.time > serverStealUntil) { serverStealUntil = -1f; return; }
        if (Time.time < nextStealCheck) return;
        nextStealCheck = Time.time + 0.2f;

        NetworkObject no = null;
        bool ok = NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(serverStealTargetId, out no) && no != null;
        if (ok)
        {
            Vector3 a = transform.position;
            Vector3 b = no.transform.position;
            if (Vector3.Distance(a, b) > StealCancelRange) ok = false;
            else if (Physics.Linecast(a + Vector3.up * 1.4f, b + Vector3.up * 1.4f, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore) &&
                     !hit.collider.transform.IsChildOf(transform) && !hit.collider.transform.IsChildOf(no.transform))
                ok = false; // arada duvar / engel: görüş dışı
        }
        if (ok) return;

        serverStealUntil = -1f;
        CancelStealRpc();
    }

    [Rpc(SendTo.Owner)]
    private void CancelStealRpc()
    {
        if (stealTarget == null) return;
        SetPanelOpen(false);
        ShowToast(Loc.T("steal.cancel"), Bad);
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

    /// <summary>Sunucu: sahibine "çantaya eklendi" kartı (eşyanın resmiyle, sağ altta).</summary>
    private void ServerBagAdded(ItemDefinition def, byte similarity, ushort forgeryId, int lookKey)
    {
        if (!IsServer || def == null || ItemDatabase.Instance == null) return;
        int idx = ItemDatabase.Instance.IndexOf(def);
        if (idx >= 0) BagAddedRpc(idx, similarity, forgeryId, lookKey);
    }

    [Rpc(SendTo.Owner)]
    private void BagAddedRpc(int itemIndex, byte similarity, ushort forgeryId, int lookKey)
    {
        InventorySlot s = new InventorySlot { ItemIndex = (ushort)itemIndex, Count = 1, Similarity = similarity, ForgeryId = forgeryId, LookKey = lookKey };
        // Aynı eşya art arda gelirse yeni kart açma, sayıyı artır
        if (bagFeed.Count > 0)
        {
            BagFeedEntry last = bagFeed[bagFeed.Count - 1];
            if (last.Slot.ItemIndex == s.ItemIndex && last.Slot.LookKey == lookKey && last.Slot.ForgeryId == forgeryId && Time.unscaledTime - last.Start < BagFeedHold)
            {
                last.Count++;
                last.Start = Time.unscaledTime - 0.25f; // giriş animasyonunu tekrar oynatma
                bagFeed[bagFeed.Count - 1] = last;
                return;
            }
        }
        bagFeed.Add(new BagFeedEntry { Slot = s, Count = 1, Start = Time.unscaledTime });
        if (bagFeed.Count > BagFeedMax) bagFeed.RemoveAt(0);
    }

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

        bool bad = key == "inv.full" || key == "inv.cantdrop" || key == "steal.cancel" ||
                   key.StartsWith("shop.not") || key.StartsWith("truck.") || key.StartsWith("cam.") || key == "display.badfake" || key == "display.toobad" ||
                   key == "spray.none" || key == "spray.already" || key == "spray.badwall" || key == "spray.toonear" || key == "spray.nodoorslot";
        ShowToast(Loc.T(key, arg, itemName), bad ? Bad : Good);

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

    private static readonly Color Good = UITheme.Sage;
    private static readonly Color Bad = UITheme.DangerLight;

    /// <summary>Kendi ekranında kısa uyarı (ör. itildin — PushSystem).</summary>
    public void ShowLocalToast(string text) => ShowToast(text, Bad);

    /// <summary>Eski imza korunur: iyi renk = Success, diğer her şey = Danger. Bildirim yeni toast kuyruğuna gider.</summary>
    private void ShowToast(string text, Color color)
    {
        bool good = color == Good;
        UIUtil.ShowToast(text, good ? ToastKind.Success : ToastKind.Danger, UITheme.ToastHold, good ? IconId.Check : IconId.Warning);
    }

    private void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;
        EnsureStyles();

        if (!WorkbenchUI.IsOpen) // atölyede sol üst / sağ üst / sağ alt kutular atölye arayüzüyle çakışmasın
        {
            DrawWeightHud();
            DrawBagFeed();
            DrawMoneyHud();
            DrawHotbar();
        }

        if (IsPanelOpen)
        {
            if (externalPanel != null) externalPanel.DrawPanel(this);
            else DrawPanel(stealTarget != null ? stealTarget : this, stealTarget != null);
        }
    }

    // ---------------- "Çantaya eklendi" kartları (sağ alt, kilo kartının üstünde) ----------------

    private struct BagFeedEntry { public InventorySlot Slot; public int Count; public float Start; }
    private readonly List<BagFeedEntry> bagFeed = new List<BagFeedEntry>();
    private const float BagFeedHold = 3.2f, BagFeedFade = 0.4f;
    private const int BagFeedMax = 4;

    /// <summary>Alınan her eşya için kısa kart: solda eşyanın resmi, sağda "Çantaya eklendi" + adı. Kayarak girer, solarak çıkar.</summary>
    private void DrawBagFeed()
    {
        float now = Time.unscaledTime;
        for (int i = bagFeed.Count - 1; i >= 0; i--)
            if (now - bagFeed[i].Start > BagFeedHold + BagFeedFade) bagFeed.RemoveAt(i);
        if (bagFeed.Count == 0 || !HotbarVisible) return;

        float sc = UITheme.Scale;
        float cardH = Mathf.Round(54f * sc), gap = Mathf.Round(6f * sc), w = Mathf.Round(236f * sc);
        float h = bagFeed.Count * cardH + (bagFeed.Count - 1) * gap;
        Rect area = UIUtil.HudRect(UIUtil.HudCorner.BottomRight, UIUtil.HudOrder.BagFeed, w, h);
        if (Event.current.type != EventType.Repaint) return;

        ItemDatabase db = ItemDatabase.Instance;
        Color oldColor = GUI.color;
        for (int i = 0; i < bagFeed.Count; i++) // en yenisi en altta (kilo kartına yakın)
        {
            BagFeedEntry f = bagFeed[i];
            ItemDefinition def = db != null ? db.Get(f.Slot.ItemIndex) : null;
            if (def == null) continue;
            float t = now - f.Start;
            float a = Mathf.Clamp01(t / 0.15f) * (1f - Mathf.Clamp01((t - BagFeedHold) / BagFeedFade));
            float slide = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.22f))) * 48f * sc;
            Rect r = new Rect(area.x + slide, area.y + i * (cardH + gap), w, cardH);

            GUI.color = oldColor;
            UIUtil.HudCard(r, a);
            // İlk anda ince yeşil şerit: yeni geldiği belli olsun
            UIUtil.Round(new Rect(r.x, r.y + 6f * sc, 3f * sc, r.height - 12f * sc), UITheme.WithAlpha(UITheme.Sage, a), 1.5f * sc);

            GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, oldColor.a * a);
            float pad = 5f * sc;
            Rect icon = new Rect(r.x + pad + 4f * sc, r.y + pad, cardH - pad * 2f, cardH - pad * 2f);
            UIUtil.Slot(icon, SlotState.Filled, 0.9f * a);
            EconomyPanelUI.DrawItemIcon(icon, def, f.Slot);

            float tx = icon.xMax + 9f * sc, tw = r.xMax - tx - 8f * sc;
            UIUtil.Text(new Rect(tx, r.y + 6f * sc, tw, 18f * sc), Loc.T("bag.added"), TextRole.Caption, UITheme.Sage);
            string nm = def.DisplayName + (f.Count > 1 ? "  x" + f.Count : "");
            UIUtil.Text(new Rect(tx, r.y + 24f * sc, tw, 22f * sc), EconomyPanelUI.Fit(nm, TextRole.BodyStrong, tw), TextRole.BodyStrong, UITheme.Ivory);
        }
        GUI.color = oldColor;
    }

    /// <summary>Sağ alt: çanta doluluğu (küçük kart: kilo + ince çubuk). Tuş ipucu yazılmaz.</summary>
    private void DrawWeightHud()
    {
        float sc = UITheme.Scale;
        float w = 150f * sc, h = 34f * sc;
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.BottomRight, UIUtil.HudOrder.Weight, w, h);
        if (Event.current.type != EventType.Repaint) return;
        UIUtil.HudCard(r);
        float ratio = LoadRatio;
        Color c = ratio < 0.6f ? UITheme.Sage : ratio < 0.9f ? UITheme.Ochre : UITheme.DangerLight;
        string txt = Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#"));
        UIUtil.Text(new Rect(r.x + 10f, r.y + 2f, r.width - 20f, 20f * sc), txt, TextRole.Caption, UITheme.Ivory);
        UIUtil.ProgressBar(new Rect(r.x + 10f, r.yMax - 9f * sc, r.width - 20f, 3f * sc), ratio, c);
    }

    /// <summary>Sağ üst: ekip kasası ve kişisel para (küçük kart, simgesiz).</summary>
    private void DrawMoneyHud()
    {
        TeamBank bank = TeamBank.Instance;
        float sc = UITheme.Scale;
        float lh = 20f * sc;
        float w = 170f * sc;
        float h = 10f * sc + (bank != null ? lh * 2f : lh);
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopRight, UIUtil.HudOrder.Money, w, h);
        if (Event.current.type != EventType.Repaint) return;
        UIUtil.HudCard(r);
        float y = r.y + 5f * sc, tw = r.width - 24f;
        if (bank != null)
        {
            string t = Loc.T("money.team", bank.Money.ToString("N0"));
            UIUtil.Text(new Rect(r.x + 12f, y, tw, lh), EconomyPanelUI.Fit(t, TextRole.BodyStrong, tw), TextRole.BodyStrong, UITheme.Ivory);
            y += lh;
        }
        string p = Loc.T("money.personal", PersonalMoney.ToString("N0"));
        UIUtil.Text(new Rect(r.x + 12f, y, tw, lh), EconomyPanelUI.Fit(p, TextRole.Body, tw), TextRole.Body, UITheme.IvoryDim);
    }

    // ---------------- Hızlı envanter (3 yuva): ses kutusunun hemen üstünde ----------------

    public const int HotbarSlots = 3;

    /// <summary>Küçük ganimet (masadaki ufak eşyalar) hızlı yuvalarda görünmez; doğrudan çantada durur.</summary>
    private static bool BagOnly(InventorySlot s)
    {
        ItemDefinition d = Def(s);
        return d != null && d.IsSmallLoot;
    }

    /// <summary>Hızlı yuva numarasının (0..2) çantadaki satırı: küçük ganimet atlanarak sıradaki eşya. Yoksa -1.</summary>
    public int HotbarSlotIndex(int hotbar)
    {
        if (slots == null) return -1;
        int n = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (BagOnly(slots[i])) continue;
            if (n == hotbar) return i;
            n++;
        }
        return -1;
    }

    /// <summary>Çanta satırının hızlı yuva numarası (0..2); hızlı yuvada değilse -1.</summary>
    public int HotbarNumberOf(int slotIndex)
    {
        for (int h = 0; h < HotbarSlots; h++) if (HotbarSlotIndex(h) == slotIndex) return h;
        return -1;
    }

    /// <summary>1 / 2 / 3: hızlı yuvadaki eşyayı (küçük ganimet hariç ilk üç eşya) doğrudan ele al / elden kaldır.</summary>
    private void TickHotbarKeys(Keyboard kb)
    {
        for (int i = 0; i < HotbarSlots; i++)
        {
            if (!kb[Key.Digit1 + i].wasPressedThisFrame && !kb[Key.Numpad1 + i].wasPressedThisFrame) continue;
            int si = HotbarSlotIndex(i);
            if (si >= 0) ToggleHeld(si);
            else if (heldSlot.Value != NoHeld) { SetHeld(NoHeld); GameAudio.Play2D(SoundId.BagClose, 0.5f); } // boş yuva = boş el
            return;
        }
    }

    private bool HotbarVisible =>
        !SessionMenuUI.IsPaused && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen && !BodyCam.PanelOpen &&
        (PlayerStealth.Local == null || !PlayerStealth.Local.IsJailed);

    /// <summary>
    /// Alt orta: 3 yuva (eşyanın oyun içi görüntüsüyle), sağında aletler (maymuncuk, boya kutusu, bozuk para — adetleriyle).
    /// Elde tutulanın adı yuvaların üstünde gölgeli küçük yazı.
    /// </summary>
    private void DrawHotbar()
    {
        if (!HotbarVisible) return;
        float sc = UITheme.Scale;
        float size = Mathf.Round(58f * sc), gap = Mathf.Round(6f * sc);
        float toolS = Mathf.Round(44f * sc);
        PlayerStealth me = PlayerStealth.Local;
        int coins = me != null && me.IsSpawned ? me.CoinsLeft : 0;
        int tools = (LockpickCount > 0 ? 1 : 0) + (SprayCount > 0 ? 1 : 0) + (coins > 0 ? 1 : 0);
        float slotsW = HotbarSlots * size + (HotbarSlots - 1) * gap;
        float w = slotsW + (tools > 0 ? 14f * sc + tools * (toolS + gap) - gap : 0f);
        float nameH = Mathf.Round(24f * sc); // üstte elde tutulanın adı için yer
        Rect full = UIUtil.HudRect(UIUtil.HudCorner.BottomCenter, UIUtil.HudOrder.Hotbar, w, size + nameH);
        Rect area = new Rect(full.x, full.y + nameH, slotsW, size);
        Event e = Event.current;
        int held = heldSlot.Value;
        bool cursorFree = Cursor.lockState != CursorLockMode.Locked;

        for (int i = 0; i < HotbarSlots; i++)
        {
            Rect r = new Rect(area.x + i * (size + gap), area.y, size, size);
            int si = HotbarSlotIndex(i);
            bool has = si >= 0;
            ItemDefinition def = has ? Def(slots[si]) : null;
            bool isHeld = has && held == si;

            // Panel açıkken (imleç serbest) yuvaya tıklayarak da ele alınır; boş yuva = boş el
            bool hover = cursorFree && r.Contains(e.mousePosition);
            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                if (has) ToggleHeld(si);
                else if (held != NoHeld) SetHeld(NoHeld);
                e.Use();
            }
            if (e.type != EventType.Repaint) continue;

            SlotState st = !has ? SlotState.Empty : isHeld ? SlotState.Selected : hover ? SlotState.Hover : SlotState.Filled;
            UIUtil.Slot(r, st);
            if (def != null)
            {
                InventorySlot s = slots[si];
                EconomyPanelUI.DrawItemIcon(r, def, s);
                if (s.Count > 1) EconomyPanelUI.Count(r, "x" + s.Count, UITheme.Ivory);
            }
            // Kısayol numarası: sol üstte küçük ve soluk
            UIUtil.Text(new Rect(r.x + 5f, r.y + 2f, 18f, 16f), (i + 1).ToString(), TextRole.Caption, isHeld ? UITheme.Ivory : UITheme.WithAlpha(UITheme.IvoryDim, 0.7f));
        }

        // Aletler (çantada yer kaplamaz): yuvaların sağında, oyun içi görüntüleriyle
        if (e.type == EventType.Repaint && tools > 0)
        {
            float bx = area.xMax + 14f * sc, by = area.y + (size - toolS) * 0.5f;
            if (LockpickCount > 0) { DrawToolBadge(new Rect(bx, by, toolS, toolS), ItemIconRenderer.Prop.Lockpick, IconId.Lockpick, LockpickCount); bx += toolS + gap; }
            if (SprayCount > 0) { DrawToolBadge(new Rect(bx, by, toolS, toolS), ItemIconRenderer.Prop.PaintPot, IconId.Brush, SprayCount); bx += toolS + gap; }
            if (coins > 0) DrawToolBadge(new Rect(bx, by, toolS, toolS), ItemIconRenderer.Prop.Coin, IconId.Money, coins);
        }

        // Elde tutulan eşyanın adı yuvaların hemen üstünde (gölgeli yazı, kutusuz)
        if (e.type == EventType.Repaint && held >= 0 && held < slots.Count)
        {
            ItemDefinition hd = Def(slots[held]);
            if (hd != null)
            {
                string nm = EconomyPanelUI.Fit(hd.DisplayName, TextRole.BodyStrong, 320f * sc);
                UIUtil.ShadowText(new Rect(full.x - 80f, full.y, full.width + 160f, nameH - 2f), nm, TextRole.BodyStrong, UITheme.Ivory, TextAnchor.MiddleCenter);
            }
        }
    }

    /// <summary>Alet rozeti: küçük yuva + aletin oyun içi görüntüsü, sağ altta adet.</summary>
    private void DrawToolBadge(Rect r, ItemIconRenderer.Prop prop, IconId fallback, int count)
    {
        UIUtil.Slot(r, SlotState.Filled, 0.9f);
        EconomyPanelUI.SlotTexture(r, ItemIconRenderer.ForProp(prop), fallback);
        EconomyPanelUI.Count(r, "x" + count, UITheme.Ivory);
    }

    // ---------------- Çanta paneli ----------------

    private int bagSel = -1;        // çanta ızgarasında seçili yuva
    private const int BagCols = 5;

    /// <summary>
    /// Çanta: koyu kart (solda ızgara, sağda seçili eşyanın kartı). Eşyalar oyun içi görüntüleriyle çizilir.
    /// Kapat: sağ üstteki düğme, Tab ya da Esc. Çalarken başlıkta çantanın sahibi yazar.
    /// </summary>
    private void DrawPanel(PlayerInventory bag, bool stealing)
    {
        ItemDatabase db = ItemDatabase.Instance;
        Event e = Event.current;
        float sc = UITheme.Scale;
        float pad = 20f * sc;
        int n = bag.SlotCount;

        float panelW = Mathf.Min(440f * sc, Screen.width - 32f);
        float cardW = Mathf.Clamp(Screen.width - panelW - 48f, 0f, 290f * sc);
        float totalW = panelW + (cardW > 0f ? 10f + cardW : 0f);
        float h = Mathf.Min(560f * sc, Screen.height - 32f);
        Rect panel = new Rect((Screen.width - totalW) * 0.5f, (Screen.height - h) * 0.5f, panelW, h);
        UIUtil.Panel(panel, PanelKind.Plaque);

        float x = panel.x + pad, cw = panel.width - pad * 2f, y = panel.y + pad;

        // Başlık (çalarken sahibin adı) + kapat
        string title;
        if (stealing)
        {
            PlayerNetworkSetup ps = bag.GetComponent<PlayerNetworkSetup>();
            title = Loc.T("steal.title", ps != null ? ps.PlayerName : "?");
        }
        else title = Loc.T("inv.title");
        float th = 32f * sc, cb = 30f * sc;
        if (cardW <= 0f && UIUtil.Button(new Rect(panel.xMax - pad - cb + 6f, y, cb, cb), "", ButtonKind.Tool, true, false, IconId.Close)) { ClosePanel(); return; }
        UIUtil.Text(new Rect(x, y, cw - cb - 10f, th), EconomyPanelUI.Fit(title, TextRole.Title, cw - cb - 10f), TextRole.Title, UITheme.Ivory);
        y += th + 8f * sc;
        if (stealing) y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 200f), Loc.T("steal.help"), TextRole.Body, UITheme.IvoryDim) + 8f;

        // Kilo (çalarken KENDİ çantanın doluluğu önemli)
        float ratio = LoadRatio;
        Color wc = ratio < 0.6f ? UITheme.Sage : ratio < 0.9f ? UITheme.Ochre : UITheme.DangerLight;
        string wtxt = Loc.T("inv.weight", CurrentWeightKg.ToString("0.#"), CapacityKg.ToString("0.#"));
        UIUtil.Text(new Rect(x, y, cw, 22f * sc), wtxt, TextRole.BodyStrong, UITheme.Ivory);
        if (!stealing && LoadRatio > 0.01f)
            UIUtil.Text(new Rect(x, y, cw, 22f * sc), Loc.T("inv.speed", Mathf.RoundToInt(SpeedMultiplier * 100f)), TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight);
        y += 24f * sc;
        UIUtil.ProgressBar(new Rect(x, y, cw, 4f * sc), ratio, wc);
        y += 4f * sc + 14f * sc;

        Rect view = new Rect(x, y, cw, Mathf.Max(60f, panel.yMax - pad - y));

        // Seçim + klavye (yön tuşları: ızgarada gezin, Enter: ele al / çal)
        if (n == 0) bagSel = -1;
        else if (bagSel < 0 || bagSel >= n) bagSel = Mathf.Clamp(bagSel, 0, n - 1);
        int actIdx = -1, actKind = 0; // 1: ele al / çal, 2: bırak
        bool keyNav = false;
        if (e.type == EventType.KeyDown && n > 0 && GUIUtility.keyboardControl == 0)
        {
            int move = 0;
            if (e.keyCode == KeyCode.LeftArrow) move = -1;
            else if (e.keyCode == KeyCode.RightArrow) move = 1;
            else if (e.keyCode == KeyCode.UpArrow) move = -BagCols;
            else if (e.keyCode == KeyCode.DownArrow) move = BagCols;
            if (move != 0) { bagSel = Mathf.Clamp(bagSel + move, 0, n - 1); keyNav = true; e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { actIdx = bagSel; actKind = 1; e.Use(); }
        }

        // Izgara ölçüleri
        float gap = 8f * sc;
        float avail = view.width;
        int rowsN = Mathf.Max(1, (n + BagCols - 1) / BagCols);
        float cell = Mathf.Floor((avail - gap * (BagCols - 1)) / BagCols);
        if (rowsN * (cell + gap) > view.height)
        {
            avail -= 16f;
            cell = Mathf.Floor((avail - gap * (BagCols - 1)) / BagCols);
        }
        float contentH = rowsN * (cell + gap);

        int hoverNow = -1;
        if (n == 0)
        {
            EconomyPanelUI.Wrapped(new Rect(view.x + 8f, view.y + view.height * 0.35f, view.width - 16f, 60f), Loc.T(stealing ? "steal.emptybag" : "inv.empty"), TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperCenter);
        }
        else
        {
            if (keyNav) scroll.y = EconomyPanelUI.EnsureVisible(scroll.y, bagSel / BagCols, cell + gap, view.height);
            bool mouseIn = view.Contains(e.mousePosition);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, avail, contentH));
            for (int i = 0; i < n; i++)
            {
                InventorySlot s = bag.GetSlot(i);
                ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
                Rect cr = new Rect((i % BagCols) * (cell + gap), (i / BagCols) * (cell + gap), cell, cell);
                bool hov = mouseIn && cr.Contains(e.mousePosition);
                if (hov) hoverNow = i;
                if (e.type == EventType.MouseDown && e.button == 0 && hov)
                {
                    bagSel = i;
                    if (e.clickCount >= 2) { actIdx = i; actKind = 1; } // çift tık: ele al / çal
                    e.Use();
                }
                if (e.type != EventType.Repaint) continue;

                bool isHeld = !stealing && heldSlot.Value == i;
                bool fits = !stealing || def == null || CanFit(def); // sığmayan çalma hedefi: soluk
                SlotState st = def == null ? SlotState.Empty : !fits ? SlotState.Disabled : bagSel == i ? SlotState.Selected : hov ? SlotState.Hover : SlotState.Filled;
                UIUtil.Slot(cr, st);
                if (def == null) continue;
                EconomyPanelUI.DrawItemIcon(cr, def, s);
                if (s.Count > 1) EconomyPanelUI.Count(cr, "x" + s.Count, UITheme.Ivory);
                int hb = stealing ? -1 : HotbarNumberOf(i);
                if (hb >= 0) UIUtil.Text(new Rect(cr.x + 5f, cr.y + 2f, 18f, 16f), (hb + 1).ToString(), TextRole.Caption, UITheme.WithAlpha(UITheme.IvoryDim, 0.7f));
                if (isHeld) UIUtil.Round(new Rect(cr.xMax - 12f, cr.y + 6f, 6f, 6f), UITheme.Sage, 3f); // elde: küçük yeşil nokta
            }
            GUI.EndScrollView();
        }
        if (e.type == EventType.Repaint) hoveredSlot = hoverNow;

        // Bilgi kartı (panelin sağında): fare üstündeki, yoksa seçili eşya; eylemler yalnızca seçili eşya için
        if (cardW > 0f)
        {
            int show = hoveredSlot >= 0 && hoveredSlot < n ? hoveredSlot : bagSel;
            Rect card = new Rect(panel.xMax + 10f, panel.y, cardW, h);
            int kind = DrawInfoCard(card, bag, show, stealing, show == bagSel && bagSel >= 0);
            if (kind == 3) { ClosePanel(); return; }
            if (kind != 0) { actIdx = bagSel; actKind = kind; }
        }

        if (actIdx >= 0 && actIdx < n)
        {
            if (actKind == 2)
            {
                if (heldSlot.Value == actIdx) SetHeld(NoHeld);
                DropRpc(actIdx);
            }
            else if (stealing)
            {
                ItemDefinition d = db != null ? db.Get(bag.GetSlot(actIdx).ItemIndex) : null;
                if (d != null && CanFit(d)) TakeFromRpc(bag.NetworkObject, actIdx, bag.GetSlot(actIdx));
            }
            else ToggleHeld(actIdx);
        }
    }

    /// <summary>
    /// Bilgi kartı (koyu): büyük görüntü, tür, ad, ağırlık, değer, benzerlik, bağlamsal eylemler; sağ üstte kapat.
    /// Dönüş: 0 eylem yok, 1 ele al / kaldır / çal, 2 bırak, 3 kapat.
    /// </summary>
    private int DrawInfoCard(Rect r, PlayerInventory bag, int idx, bool stealing, bool actions)
    {
        float sc = UITheme.Scale, pad = 18f * sc;
        UIUtil.Panel(r, PanelKind.Plaque);
        float x = r.x + pad, cw = r.width - pad * 2f, y = r.y + pad;
        float cb = 30f * sc;
        if (UIUtil.Button(new Rect(r.xMax - pad - cb + 6f, y - 2f, cb, cb), "", ButtonKind.Tool, true, false, IconId.Close)) return 3;

        InventorySlot s = default;
        ItemDefinition def = null;
        if (idx >= 0 && idx < bag.SlotCount)
        {
            s = bag.GetSlot(idx);
            def = Def(s);
        }
        if (def == null)
        {
            EconomyPanelUI.Wrapped(new Rect(x, r.center.y - 10f, cw, 60f), Loc.T(stealing ? "steal.emptybag" : "inv.empty"), TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperCenter);
            return 0;
        }

        bool held = !stealing && heldSlot.Value == idx;

        // Üst: büyük görüntü (oyun içi), altında tür ve ad
        y += cb + 4f;
        float big = Mathf.Min(cw, 150f * sc);
        Rect slotR = new Rect(r.center.x - big * 0.5f, y, big, big);
        UIUtil.Slot(slotR, SlotState.Filled);
        EconomyPanelUI.DrawItemIcon(slotR, def, s);
        y += big + 12f * sc;
        UIUtil.Text(new Rect(x, y, cw, 18f * sc), ItemDefinition.TypeName(def.type), TextRole.Caption, UITheme.IvoryDim);
        y += 18f * sc;
        y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 60f), def.DisplayName, TextRole.BodyStrong, UITheme.Ivory) + 10f * sc;

        y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.weight", (def.weightKg * s.Count).ToString("0.#")), TextRole.Caption, UITheme.IvoryDim) + 2f;
        if (s.Count > 1) y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.count", s.Count), TextRole.Caption, UITheme.IvoryDim) + 2f;
        if (def.baseValue > 0)
            y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.value", Mathf.RoundToInt(def.baseValue * PriceRules.DisplayMultiplier(s.LookKey) * s.Count).ToString("N0")), TextRole.Caption, UITheme.IvoryDim) + 2f;

        if (s.HasSimilarity)
        {
            string simKey = def.type == ItemType.Original ? "card.swapquality" : "card.similarity";
            y += 4f;
            y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T(simKey, s.Similarity, ItemDefinition.GradeName(s.Similarity)), TextRole.Caption, UITheme.Ivory) + 4f;
            UIUtil.ProgressBar(new Rect(x, y, cw, 4f * sc), s.Similarity / 100f, ItemDefinition.GradeColor(s.Similarity), 0.75f);
            y += 10f * sc;
        }
        else if (def.type == ItemType.Original)
        {
            y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.hot"), TextRole.Caption, UITheme.Ochre) + 2f;
        }

        if (def.type == ItemType.Original)
            y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.price", PriceRules.SalePrice(def, s).ToString("N0")), TextRole.BodyStrong, UITheme.Ivory) + 2f;
        if (def.type == ItemType.Forgery && def.forgeryOf != null)
            y += EconomyPanelUI.Wrapped(new Rect(x, y, cw, 80f), Loc.T("card.forgeryof", def.forgeryOf.DisplayName), TextRole.Caption, UITheme.IvoryDim) + 2f;
        if (held)
            UIUtil.Badge(new Vector2(x, y + 4f), Loc.T("inv.inhand").Trim('(', ')'), UITheme.Sage, UITheme.Charcoal);

        // Bağlamsal eylemler (kartın altında)
        int result = 0;
        if (actions)
        {
            float bh = 38f * sc, by = r.yMax - pad - bh;
            if (stealing)
            {
                bool fits = CanFit(def);
                if (UIUtil.Button(new Rect(x, by, cw, bh), fits ? Loc.T("steal.take") : Loc.T("steal.nofit"), ButtonKind.Primary, fits)) result = 1;
            }
            else
            {
                float w1 = Mathf.Floor(cw * 0.58f), w2 = cw - w1 - 8f;
                if (UIUtil.Button(new Rect(x, by, w1, bh), held ? Loc.T("inv.putaway") : Loc.T("inv.hold"), ButtonKind.Primary)) result = 1;
                if (UIUtil.Button(new Rect(x + w1 + 8f, by, w2, bh), Loc.T("inv.drop"), ButtonKind.Danger)) result = 2;
            }
        }
        return result;
    }

    private void EnsureStyles()
    {
        UIUtil.ApplySkin(); // tema yüklenmeden başka OnGUI stili kopyalanmasın (kendini bir kez kurar)
    }
}
