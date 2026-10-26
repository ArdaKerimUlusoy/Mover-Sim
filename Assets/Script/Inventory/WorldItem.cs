using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sahnede duran, çantaya alınabilen eşya (eser, alet, bozuk para...).
/// Prefab'ında: NetworkObject + NetworkTransform + (isteğe bağlı) Rigidbody + NetworkRigidbody + WorldItem.
/// Fiziği sunucu hesaplar. Oyuncu E'ye basınca sunucu kontrol eder, sığarsa çantaya ekler ve objeyi siler.
/// Bir yere çarpınca ses çıkarır ve "gürültü" yayar (bozuk para güvenliklerin dikkatini dağıtmak için).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class WorldItem : NetworkBehaviour
{
    [SerializeField] private ItemDefinition item;

    [Tooltip("Sadece sahte eserler için: benzerlik yüzdesi (0–100). -1 = yok.")]
    [SerializeField, Range(-1, 100)] private int similarityPercent = -1;

    [Header("Çarpma")]
    [SerializeField] private float minImpactSpeed = 1.2f;
    [Tooltip("Bozuk para gibi dikkat dağıtma eşyalarının gürültü yarıçapı (metre).")]
    [SerializeField] private float distractionNoiseRadius = 10f;

    private readonly NetworkVariable<byte> similarity = new NetworkVariable<byte>(InventorySlot.NoSimilarity);
    private readonly NetworkVariable<ushort> forgeryId = new NetworkVariable<ushort>(0);
    private readonly NetworkVariable<int> lookKey = new NetworkVariable<int>(0);

    private bool hasPendingSimilarity;
    private byte pendingSimilarity;
    private ushort pendingForgeryId;
    private int pendingLookKey;
    private GameObject artVisual;
    private bool artPending;
    private bool thrown;
    private float ignoreImpactsUntil;
    private float nextImpactTime;
    private float expireAt = -1f;

    public ItemDefinition Item => item;
    public byte Similarity => IsSpawned ? similarity.Value : ToByte(similarityPercent);
    public ushort ForgeryId => IsSpawned ? forgeryId.Value : pendingForgeryId;
    public int LookKey => IsSpawned ? lookKey.Value : pendingLookKey;
    /// <summary>Çantaya girerken taşınacak satır bilgisi.</summary>
    public InventorySlot AsSlot() => new InventorySlot { Count = 1, Similarity = Similarity, ForgeryId = ForgeryId, LookKey = LookKey };
    public bool IsConsumed { get; private set; }

    /// <summary>Sunucu: çantadan bırakılan eşyayı spawn etmeden önce benzerliğini ayarlar.</summary>
    public void SetPendingSimilarity(byte value)
    {
        hasPendingSimilarity = true;
        pendingSimilarity = value;
    }

    /// <summary>Sunucu: bırakılan eserin kimliğini (yapılan sahte / ait olduğu kaide) spawn etmeden önce ayarlar.</summary>
    public void SetPendingIdentity(ushort forgery, int look)
    {
        pendingForgeryId = forgery;
        pendingLookKey = look;
    }

    /// <summary>Sunucu: atıldı/bırakıldı olarak işaretle (ilk çarpmada hemen ses çıkarsın).</summary>
    public void MarkThrown()
    {
        thrown = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            similarity.Value = hasPendingSimilarity ? pendingSimilarity : ToByte(similarityPercent);
            forgeryId.Value = pendingForgeryId;
            lookKey.Value = pendingLookKey;
            // Sahneye konmuş eşyalar oyun başında yere otururken ses çıkarmasın.
            ignoreImpactsUntil = Time.time + (thrown ? 0.05f : 1.5f);
            ServerEnsurePhysics();
            nextWake = Time.time + 1f;
            if (!thrown && NetworkObject.IsSceneObject == true) { hasHome = true; homePos = transform.position; homeRot = transform.rotation; }
        }
        All.Add(this);
        forgeryId.OnValueChanged += (a, b) => RefreshArtVisual();
        lookKey.OnValueChanged += (a, b) => RefreshArtVisual();
        RefreshArtVisual();
    }

    public override void OnNetworkDespawn()
    {
        All.Remove(this);
        // Sahneye konmuş eşya oturum kapanınca yok olmaz: NetworkRigidbody onu kinematik bırakır, yeniden
        // açılınca havada asılı kalırdı. Sunucuda fiziği geri aç.
        if (IsServer && this != null && !IsConsumed) ServerEnsurePhysics();
    }

    /// <summary>Yaşayan tüm yerdeki eşyalar (sunucu temizliği ve uyandırma için).</summary>
    public static readonly System.Collections.Generic.List<WorldItem> All = new System.Collections.Generic.List<WorldItem>();

    private float nextWake;
    private bool physicsByDesign; // prefab'da kinematik değil (fizikle düşer) → hep böyle kalmalı

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        physicsByDesign = rb != null && !rb.isKinematic;
    }

    /// <summary>Sunucu: Rigidbody kinematik kalmasın, uyanık olsun ve yerçekimi çalışsın (havada asılı kalmayı önler).</summary>
    private void ServerEnsurePhysics()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null || !physicsByDesign) return;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.detectCollisions = true;
        rb.WakeUp();
    }

    /// <summary>Pivot noktasından eşyanın en alt noktasına olan mesafe (masaya / zemine oturturken).</summary>
    public float PivotToBottom()
    {
        Collider[] cols = GetComponentsInChildren<Collider>();
        float minY = float.MaxValue;
        foreach (Collider c in cols) if (c != null && !c.isTrigger) minY = Mathf.Min(minY, c.bounds.min.y);
        return minY == float.MaxValue ? 0.05f : Mathf.Max(0f, transform.position.y - minY);
    }

    /// <summary>Sunucu: eşyayı belirtilen yere ışınlar (ağ konumu ve fizik birlikte): hız sıfırlanır, NetworkTransform.Teleport kullanılır,
    /// kinematik kaldıysa fizik yeniden açılır. Yeni haritada masa ganimetlerinin havada / yanlış yerde kalmasını önler.</summary>
    public void ServerPlaceAt(Vector3 pos, Quaternion rot)
    {
        if (!IsServer || !IsSpawned || IsConsumed) return;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        transform.SetPositionAndRotation(pos, rot);
        if (rb != null) { rb.position = pos; rb.rotation = rot; }
        Unity.Netcode.Components.NetworkTransform nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, rot, transform.localScale);
        ServerEnsurePhysics();
        ignoreImpactsUntil = Time.time + 1.5f;
        nextWake = Time.time + 0.5f;
    }

    /// <summary>Sunucu: eşyanın altındaki en yakın zemini bulur ve eşyayı oraya oturtur (yeni haritada havada kalan / zeminin altına düşen
    /// sahne eşyaları için). Zemin bulunamazsa sahnede ilk görülen yerine döner.</summary>
    public void ServerSettleOnGround()
    {
        if (!IsServer || !IsSpawned || IsConsumed) return;
        Vector3 p = transform.position;
        float bottom = PivotToBottom();
        RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 0.6f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.collider == null || h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<PlayerStealth>() != null) continue;
            bestY = Mathf.Max(bestY, h.point.y);
        }
        if (bestY > float.NegativeInfinity)
        {
            // Zemine zaten oturmuşsa (±0.15 m) dokunma; havada ya da gömülüyse oturt
            if (Mathf.Abs(p.y - bottom - bestY) > 0.15f) ServerPlaceAt(new Vector3(p.x, bestY + bottom + 0.02f, p.z), transform.rotation);
        }
        else if (hasHome)
        {
            ServerPlaceAt(homePos, homeRot);
        }
    }

    private bool hasHome;
    private Vector3 homePos;
    private Quaternion homeRot;

    /// <summary>Sahneye baştan konmuş eşya mı (oyuncu atmamış)? Yeni görevde yerine döndürülebilir.</summary>
    public bool IsSceneItem => hasHome;

    /// <summary>Sunucu: oturum kapanırken / görev sıfırlanırken oyuncunun bıraktığı (atılan) eşyaları siler.
    /// Sahneye baştan konmuş eşyalara dokunmaz.</summary>
    public static void ServerClearDropped()
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            WorldItem w = All[i];
            if (w == null || !w.IsServer || !w.IsSpawned || !w.thrown || w.NetworkObject.IsSceneObject == true) continue;
            w.ServerConsume();
        }
    }

    // ---------------- Görünüm: yerdeki eser de gerçek haliyle görünür ----------------

    private void OnEnable() => ForgeryRegistry.Arrived += OnForgeryArrived;
    private void OnDisable() => ForgeryRegistry.Arrived -= OnForgeryArrived;

    private void OnForgeryArrived(ushort id)
    {
        if (artPending && IsSpawned && id == forgeryId.Value) RefreshArtVisual();
    }

    private void Update()
    {
        if (expireAt > 0f && IsServer && IsSpawned && Time.time >= expireAt)
        {
            ServerConsume();
            return;
        }

        // Uyuyan fizik: zemin değişince (oda yeniden kuruldu) altı boşalan eşya havada kalmasın → ara ara uyandır
        if (IsServer && IsSpawned && Time.time >= nextWake)
        {
            nextWake = Time.time + 1f;
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && rb.IsSleeping()) rb.WakeUp();
        }

        // Veri istenemediyse (oyuncumuz henüz doğmadıysa) ara ara tekrar dene
        if (artPending && IsSpawned && Time.frameCount % 30 == 0 && ForgeryRegistry.TryGet(forgeryId.Value, out _)) RefreshArtVisual();
    }

    /// <summary>Görev değişince (eser dokuları yenilenince) yerdeki eserin görselini yeniden kurar.</summary>
    public void RebuildArtVisual()
    {
        if (artVisual != null)
        {
            artVisual.transform.SetParent(null, false); // Destroy kare sonunda: ölçümlere karışmasın
            Destroy(artVisual);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = true; // prefab şekli ölçülebilsin
        }
        artVisual = null;
        if (IsSpawned) RefreshArtVisual();
    }

    private void RefreshArtVisual()
    {
        if (item == null || (forgeryId.Value == 0 && lookKey.Value == 0)) return;
        if (artVisual != null) return;

        // Prefab'ın kendi (kaba) şeklinin kapladığı hacim: gerçek görsel buna sığdırılır
        Renderer[] own = GetComponentsInChildren<Renderer>();
        Bounds ob = new Bounds(transform.position, Vector3.one * 0.3f);
        for (int i = 0; i < own.Length; i++) { if (i == 0) ob = own[i].bounds; else ob.Encapsulate(own[i].bounds); }

        GameObject v = ItemVisuals.BuildFor(item, AsSlot(), out artPending);
        if (artPending || v == null || ItemVisuals.IsPrefabFallback(v)) { if (v != null) Destroy(v); return; }

        foreach (Renderer r in own) r.enabled = false;
        ItemVisuals.Fit(v, Mathf.Max(ob.size.x, ob.size.y, ob.size.z));
        ItemVisuals.SetShadows(v, true);
        v.transform.SetPositionAndRotation(ob.center, transform.rotation);
        // Ölçekli prefab'ın çocuğu olunca şekil bozulmasın: dünya ölçeğini koru
        v.transform.SetParent(transform, true);
        artVisual = v;
    }

    /// <summary>Sunucu: bu eşya belirtilen süre sonra kendiliğinden silinir (fırlatılan bozuk para).</summary>
    public void ServerExpireAfter(float seconds)
    {
        if (seconds > 0f) expireAt = Time.time + seconds;
    }

    /// <summary>Sunucu: eşya çantaya alındı, sahneden kaldır.</summary>
    public void ServerConsume()
    {
        if (!IsServer || IsConsumed) return;
        IsConsumed = true;
        NetworkObject.Despawn(true);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !IsSpawned || IsConsumed || item == null) return;
        if (Time.time < ignoreImpactsUntil || Time.time < nextImpactTime) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < minImpactSpeed) return;

        nextImpactTime = Time.time + 0.25f;
        Vector3 pos = transform.position;
        float loudness = Mathf.Clamp01(speed / 8f);

        if (item.type == ItemType.Distraction)
        {
            NoiseSystem.Emit(pos, distractionNoiseRadius * Mathf.Lerp(0.6f, 1f, loudness));
            ImpactSoundRpc(SoundId.CoinLand, pos, Mathf.Lerp(0.5f, 1f, loudness));
        }
        else
        {
            NoiseSystem.Emit(pos, 2f + item.weightKg * 0.5f * loudness);
            ImpactSoundRpc(SoundId.ItemThud, pos, Mathf.Lerp(0.3f, 1f, loudness));
        }
    }

    [Rpc(SendTo.Everyone)]
    private void ImpactSoundRpc(SoundId id, Vector3 pos, float volume)
    {
        GameAudio.PlayAt(id, pos, volume);
    }

    private static byte ToByte(int percent)
    {
        return percent < 0 ? InventorySlot.NoSimilarity : (byte)Mathf.Clamp(percent, 0, 100);
    }
}
