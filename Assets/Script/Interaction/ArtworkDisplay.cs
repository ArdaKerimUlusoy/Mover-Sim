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

    private GameObject visual;
    private int visualItem = -2;
    private int visualLookVersion = -1;
    private bool visualActive = true;
    private byte visualSimilarity = InventorySlot.NoSimilarity;

    public int RequiredLevel => requiredLevel;
    /// <summary>Bu görevde aktif mi? (Seviyesi gelmemiş kaideler boş durur, etkileşime kapalıdır.)</summary>
    public bool IsActiveForMission => MuseumSession.IsDisplayActive(this);

    public ItemDefinition Artwork => artwork;
    public bool IsSwapped => swapped.Value;
    /// <summary>Kaideye bırakılan sahtenin benzerliği (güvenlik yapay zekası kullanacak).</summary>
    public byte LeftSimilarity => leftSimilarity.Value;

    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    public override void OnNetworkSpawn()
    {
        if (IsServer && ItemDatabase.Instance != null)
        {
            shownItem.Value = ItemDatabase.Instance.IndexOf(artwork);
        }
        shownItem.OnValueChanged += OnShownChanged;
        leftSimilarity.OnValueChanged += OnSimilarityChanged;
        RefreshVisual();
    }

    public override void OnNetworkDespawn()
    {
        shownItem.OnValueChanged -= OnShownChanged;
        leftSimilarity.OnValueChanged -= OnSimilarityChanged;
        DestroyVisual();
    }

    private void OnShownChanged(int oldValue, int newValue) => RefreshVisual();
    private void OnSimilarityChanged(byte oldValue, byte newValue) => RefreshVisual();

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
        leftSimilarity.Value = InventorySlot.NoSimilarity;
        if (ItemDatabase.Instance != null) shownItem.Value = ItemDatabase.Instance.IndexOf(artwork);
        ForceRefreshVisual();
    }

    /// <summary>Ekranda görünen ad: temalı müzede vazo/tablonun kendi adı.</summary>
    private string ShownName => MuseumSession.Instance != null ? MuseumSession.Instance.ArtworkName(this) : artwork.DisplayName;

    // ---------------- Etkileşim ----------------

    private static PlayerInventory InvOf(GameObject player) => player != null ? player.GetComponent<PlayerInventory>() : null;

    public override bool CanInteract(GameObject player)
    {
        if (!base.CanInteract(player) || swapped.Value || artwork == null || !IsActiveForMission) return false;
        PlayerInventory inv = InvOf(player);
        return inv != null && inv.FindForgeryFor(artwork) >= 0;
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
        int slot = inv != null ? inv.FindForgeryFor(artwork) : -1;
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

        int slot = inv.FindForgeryFor(artwork);
        if (slot < 0) return;

        ItemDefinition forgery = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(inv.GetSlot(slot).ItemIndex) : null;

        if (!inv.ServerSwapForgery(slot, artwork, out byte sim))
        {
            inv.ServerNotify("inv.full");
            return;
        }

        swapped.Value = true;
        leftSimilarity.Value = sim;
        if (forgery != null && ItemDatabase.Instance != null) shownItem.Value = ItemDatabase.Instance.IndexOf(forgery);

        inv.ServerPlaySound(SoundId.Swap, transform.position);
        inv.ServerNotify("display.done", artwork);
        NoiseSystem.Emit(transform.position, swapNoiseRadius);

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
        if (index == visualItem && visualLookVersion == MuseumSession.LookVersion && visualActive == active && visualSimilarity == sim) return;

        DestroyVisual();
        visualItem = index;
        visualLookVersion = MuseumSession.LookVersion;
        visualActive = active;
        visualSimilarity = sim;
        if (!active) return; // bu görevde kaide boş

        ItemDefinition def = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(index) : null;
        if (def == null) return;

        // Temalı müze: vazo ve tablolar tohumdan üretilir (sahte ise benzerliğine göre bozuk görünür)
        MuseumSession ms = MuseumSession.Instance;
        ArtKind kind = ArtworkLook.KindOf(def);
        if (ms != null && ms.Ready && kind != ArtKind.None)
        {
            byte shownSim = def.type == ItemType.Forgery ? (sim == InventorySlot.NoSimilarity ? (byte)50 : sim) : InventorySlot.NoSimilarity;
            visual = kind == ArtKind.Vase
                ? ArtworkLook.BuildVaseVisual(ms.VaseFor(this), shownSim)
                : ArtworkLook.BuildPaintingVisual(ms.PaintingFor(this), shownSim);
        }
        else
        {
            if (def.worldPrefab == null) return;
            visual = ItemVisuals.Build(def.worldPrefab, visualMaxSize, true);
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
