using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Müzedeki eser kaidesi (GDD Bölüm 5 – Değiştirme işlemi).
/// Çantanda bu eserin sahtesi varsa E'ye basılı tutarsın: sahte kaideye konur, orijinal çantana girer.
/// Süre eserin ağırlığına göre uzar, "Swift Gloves" upgrade'i kısaltır.
/// Kaidede artık sahte durur; bırakılan sahtenin benzerliği kaydedilir (güvenlikler ileride buna bakacak).
/// Kurulum: kaide objesine (ör. küp) NetworkObject + bu bileşen, "Artwork" alanına orijinal eseri sürükleyin.
/// </summary>
public class ArtworkDisplay : Interactable
{
    [Header("Eser")]
    [SerializeField] private ItemDefinition artwork;
    [SerializeField] private float baseSwapSeconds = 2.5f;
    [SerializeField] private float extraSecondsPerKg = 0.2f;
    [SerializeField] private float visualMaxSize = 0.8f;
    [SerializeField] private float swapNoiseRadius = 3f;

    [Header("Görev (MuseumSession)")]
    [Tooltip("Bu kaide hangi görev seviyesinden itibaren aktif? (1. görev 1 vazo, 2. görev 2 vazo...)")]
    [SerializeField, Min(1)] private int requiredLevel = 1;

    public static readonly System.Collections.Generic.List<ArtworkDisplay> All = new System.Collections.Generic.List<ArtworkDisplay>();

    private readonly NetworkVariable<int> shownItem = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<bool> swapped = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<byte> leftSimilarity = new NetworkVariable<byte>(InventorySlot.NoSimilarity);
    private readonly NetworkVariable<ushort> leftForgeryId = new NetworkVariable<ushort>(0);

    private GameObject visual;
    private int visualItem = -2;
    private int visualLookVersion = -1;
    private bool visualActive = true;
    private byte visualSimilarity = InventorySlot.NoSimilarity;
    private ushort visualForgeryId;
    private bool visualPending;

    public int RequiredLevel => requiredLevel;

    /// <summary>MuseumLayout: tablo duvara asılı (obje ince bir arka pano, ön yüzü transform.forward).</summary>
    public bool WallMounted { get; set; }

    /// <summary>Staff Only odasındaki eser her haritada diğerlerinden daha değerli.</summary>
    public bool IsStaffOnlyPiece => name == "Display_Painting4";
    public float ValueMultiplier => IsStaffOnlyPiece ? 2.5f : 1f;
    public int Value => artwork != null ? Mathf.RoundToInt(artwork.baseValue * ValueMultiplier) : 0;
    /// <summary>Bu görevde aktif mi? (Seviyesi gelmemiş kaideler boş durur, etkileşime kapalıdır.)</summary>
    public bool IsActiveForMission => MuseumSession.IsDisplayActive(this);

    public ItemDefinition Artwork => artwork;
    /// <summary>Bu kaidenin kimliği: sahteler ve çalınan orijinaller hangi kaideye ait olduklarını bununla taşır.</summary>
    public int LookKey => ArtworkLook.Hash(name);
    public ArtKind Kind => ArtworkLook.KindOf(artwork);

    public static ArtworkDisplay FindByKey(int key)
    {
        if (key == 0) return null;
        foreach (ArtworkDisplay d in All) if (d != null && d.LookKey == key) return d;
        return null;
    }
    public bool IsSwapped => swapped.Value;
    /// <summary>Kaideye bırakılan sahtenin benzerliği (güvenlik yapay zekası kullanacak).</summary>
    public byte LeftSimilarity => leftSimilarity.Value;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); ForgeryRegistry.Arrived += OnForgeryArrived; }
    private void OnDisable() { All.Remove(this); ForgeryRegistry.Arrived -= OnForgeryArrived; }

    private void OnForgeryArrived(ushort id)
    {
        if (visualPending && IsSpawned && id == leftForgeryId.Value) ForceRefreshVisual();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && ItemDatabase.Instance != null)
        {
            shownItem.Value = ItemDatabase.Instance.IndexOf(artwork);
        }
        shownItem.OnValueChanged += OnShownChanged;
        leftSimilarity.OnValueChanged += OnSimilarityChanged;
        leftForgeryId.OnValueChanged += OnForgeryIdChanged;
        RefreshVisual();
    }

    public override void OnNetworkDespawn()
    {
        shownItem.OnValueChanged -= OnShownChanged;
        leftSimilarity.OnValueChanged -= OnSimilarityChanged;
        leftForgeryId.OnValueChanged -= OnForgeryIdChanged;
        DestroyVisual();
    }

    private void OnShownChanged(int oldValue, int newValue) => RefreshVisual();
    private void OnSimilarityChanged(byte oldValue, byte newValue) => RefreshVisual();
    private void OnForgeryIdChanged(ushort oldValue, ushort newValue) => RefreshVisual();

    private void Update()
    {
        if (IsServer && IsSpawned) ServerTickBadFake();

        // Verisi henüz gelmemiş sahte: ara ara yeniden dene
        if (visualPending && IsSpawned && Time.frameCount % 30 == 0 && ForgeryRegistry.TryGet(leftForgeryId.Value, out _)) ForceRefreshVisual();
    }

    /// <summary>Müze teması / görev seviyesi değişince (MuseumSession) görseli yeniden kur.</summary>
    public void ForceRefreshVisual()
    {
        visualItem = -2;
        RefreshVisual();
    }

    /// <summary>Sunucu: yeni görev başlarken kaideyi orijinal haline döndür.</summary>
    public void ServerResetForMission()
    {
        if (!IsServer) return;
        swapped.Value = false;
        badFakeAlarmAt = -1f;
        leftSimilarity.Value = InventorySlot.NoSimilarity;
        leftForgeryId.Value = 0;
        if (ItemDatabase.Instance != null) shownItem.Value = ItemDatabase.Instance.IndexOf(artwork);
        ForceRefreshVisual();
    }

    /// <summary>Sunucu: çalınan orijinal geri geldi (hırsız yakalandı). Kaidedeki sahte kaldırılır, orijinal yerine konur.</summary>
    public void ServerRestoreOriginal()
    {
        if (!IsServer || !swapped.Value) return;
        ServerResetForMission();
    }

    /// <summary>Ekranda görünen ad: temalı müzede vazo/tablonun kendi adı.</summary>
    private string ShownName => ArtNames.MuseumName(this); // müzede: bazı eserlerin tabelası soluk (tam ad kamyondaki planda)

    // ---------------- Kalitesiz sahte: birkaç saniye sonra fark edilir ----------------
    /// <summary>Bu benzerliğin altındaki sahteler değiştirildikten birkaç sn sonra NPC'lerce fark edilir, alarm çalar.</summary>
    public const byte BadFakeThreshold = 45;
    /// <summary>Bu benzerliğin altındaki (rastgele çizim) sahte ile tablo DEĞİŞTİRİLEMEZ; swap reddedilir.</summary>
    public const byte MinSwapScore = 35;
    private float badFakeAlarmAt = -1f;
    private ulong badFakeCulprit = ulong.MaxValue;

    private void ServerTickBadFake()
    {
        if (badFakeAlarmAt < 0f || Time.time < badFakeAlarmAt) return;
        badFakeAlarmAt = -1f;
        if (!swapped.Value || leftSimilarity.Value >= BadFakeThreshold) return; // orijinal geri kondu / iyi sahte
        SecurityManager sm = SecurityManager.Instance;
        if (sm == null) return;

        PlayerStealth culprit = null;
        foreach (PlayerStealth p in PlayerStealth.All)
            if (p != null && p.IsSpawned && p.OwnerClientId == badFakeCulprit && !p.IsJailed) { culprit = p; break; }

        if (MuseumSession.Instance != null && MuseumSession.Instance.SequenceActive) return;
        if (culprit != null && (TruckZone.Instance != null && TruckZone.Instance.Contains(culprit.transform.position))) culprit = null; // kamyona kaçtı: kimse aranmaz
        sm.ServerTriggerAlarm(transform.position, "alarm.badfake", culprit);
    }

    // ---------------- Etkileşim ----------------

    private static PlayerInventory InvOf(GameObject player) => player != null ? player.GetComponent<PlayerInventory>() : null;

    public override bool CanInteract(GameObject player)
    {
        if (!base.CanInteract(player) || swapped.Value || artwork == null || !IsActiveForMission) return false;
        PlayerInventory inv = InvOf(player);
        return inv != null && inv.FindForgeryFor(this) >= 0;
        // Not: izlenirken de değiştirmeye izin var; görülürsen şüphe hızla dolar ve alarm çalar.
    }

    public override float GetHoldDuration(GameObject player)
    {
        float seconds = baseSwapSeconds + (artwork != null ? artwork.weightKg * extraSecondsPerKg : 0f);
        PlayerInventory inv = InvOf(player);
        return seconds * (inv != null ? inv.SwapTimeMultiplier : 1f);
    }

    public override string GetPrompt(PlayerInteractor player)
    {
        if (artwork == null || !IsActiveForMission) return "";
        string name = ShownName;

        if (swapped.Value) return Loc.T("display.swapped", name);

        PlayerInventory inv = InvOf(player != null ? player.gameObject : null);
        int slot = inv != null ? inv.FindForgeryFor(this) : -1;
        if (slot < 0) return Loc.T("display.needforgery", name);

        InventorySlot s = inv.GetSlot(slot);
        int sim = s.HasSimilarity ? s.Similarity : 0;
        return Loc.T("display.swap", name, sim);
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        if (swapped.Value || artwork == null) return;

        PlayerInventory inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        int slot = inv.FindForgeryFor(this);
        if (slot < 0) return;

        ItemDefinition forgery = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(inv.GetSlot(slot).ItemIndex) : null;

        // Çok düşük benzerlikli sahte (rastgele çizim) ile swap yapılamaz
        InventorySlot fs = inv.GetSlot(slot);
        if (fs.HasSimilarity && fs.Similarity < MinSwapScore)
        {
            inv.ServerNotify("display.toobad", MinSwapScore.ToString());
            return;
        }

        if (!inv.ServerSwapForgery(slot, artwork, LookKey, out byte sim, out ushort forgeryId))
        {
            inv.ServerNotify("inv.full");
            return;
        }

        swapped.Value = true;
        leftSimilarity.Value = sim;
        leftForgeryId.Value = forgeryId;
        if (forgery != null && ItemDatabase.Instance != null) shownItem.Value = ItemDatabase.Instance.IndexOf(forgery);

        inv.ServerPlaySound(SoundId.Swap, transform.position);
        inv.ServerNotify("display.done", artwork);
        NoiseSystem.Emit(transform.position, swapNoiseRadius);

        // BadFakeThreshold altı kalitesiz sahte: benzerliğe göre artan şansla fark edilir → alarm (kaçış zorlaşır)
        if (sim == InventorySlot.NoSimilarity || (sim < BadFakeThreshold && Random.value < FakeSpotter.NoticeChance(sim)))
        {
            badFakeAlarmAt = Time.time + Random.Range(3.5f, 6f);
            badFakeCulprit = clientId;
            inv.ServerNotify("display.badfake");
        }

        // Değiştirmeyi tam bir güvenliğin / kameranın gözü önünde bitirdiyse: anında alarm
        PlayerStealth stealth = player.GetComponent<PlayerStealth>();
        if (stealth != null && stealth.IsWatched && SecurityManager.Instance != null)
        {
            SecurityManager.Instance.ServerTriggerAlarm(transform.position, "alarm.seenswap", stealth);
        }
    }

    // ---------------- Görüntü ----------------

    private void RefreshVisual()
    {
        int index = shownItem.Value;
        bool active = IsActiveForMission;
        byte sim = leftSimilarity.Value;
        ushort fid = leftForgeryId.Value;
        if (index == visualItem && visualLookVersion == MuseumSession.LookVersion && visualActive == active && visualSimilarity == sim && visualForgeryId == fid) return;

        DestroyVisual();
        visualItem = index;
        visualLookVersion = MuseumSession.LookVersion;
        visualActive = active;
        visualSimilarity = sim;
        visualForgeryId = fid;
        visualPending = false;
        if (!active) return; // bu görevde kaide boş

        ItemDefinition def = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(index) : null;
        if (def == null) return;

        // Temalı müze: eserler tohumdan üretilir. Kaidede sahte varsa OYUNCUNUN YAPTIĞI eser görünür.
        MuseumSession ms = MuseumSession.Instance;
        ArtKind kind = ArtworkLook.KindOf(def);
        if (ms != null && ms.Ready && kind != ArtKind.None)
        {
            if (def.type == ItemType.Forgery)
            {
                if (fid != 0 && ForgeryRegistry.TryGet(fid, out ForgeryData data)) visual = ArtworkLook.BuildForgeryVisual(data);
                else visualPending = fid != 0; // veri sunucudan gelince yeniden kurulur
                if (visual == null)
                    visual = ArtworkLook.BuildOriginalVisual(kind, ms.Seed, ms.ThemeIndex, name, sim == InventorySlot.NoSimilarity ? (byte)50 : sim);
            }
            else visual = ArtworkLook.BuildOriginalVisual(kind, ms.Seed, ms.ThemeIndex, name);
        }
        else
        {
            if (def.worldPrefab == null) return;
            visual = ItemVisuals.Build(def.worldPrefab, visualMaxSize, true);
        }
        if (visual == null) return;

        if (WallMounted)
        {
            // Duvara asılı: resmin ön yüzü (görselde -z) odaya (transform.forward) baksın, panonun önünde ortalansın
            visual.transform.rotation = Quaternion.LookRotation(-transform.forward, Vector3.up);
            visual.transform.position = transform.position;
            if (ItemVisuals.TryGetBounds(visual, out Bounds wb))
            {
                visual.transform.position += transform.position - wb.center;
                Vector3 f = transform.forward;
                float depth = Mathf.Abs(f.x) * wb.extents.x + Mathf.Abs(f.y) * wb.extents.y + Mathf.Abs(f.z) * wb.extents.z;
                visual.transform.position += f * (transform.lossyScale.z * 0.5f + depth + 0.005f);
            }
            return;
        }

        // Kaidenin üst yüzeyine oturt (kaidenin ölçeği bozmasın diye çocuk yapmıyoruz)
        Collider c = GetComponentInChildren<Collider>();
        float topY = c != null ? c.bounds.max.y : transform.position.y + 0.5f;
        visual.transform.position = new Vector3(transform.position.x, topY, transform.position.z);
        visual.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (ItemVisuals.TryGetBounds(visual, out Bounds b))
        {
            visual.transform.position += Vector3.up * (topY - b.min.y + 0.01f);
        }
    }

    private void DestroyVisual()
    {
        if (visual != null) Destroy(visual);
        visual = null;
        visualItem = -2;
    }

    public override void OnDestroy()
    {
        DestroyVisual();
        base.OnDestroy();
    }
}
