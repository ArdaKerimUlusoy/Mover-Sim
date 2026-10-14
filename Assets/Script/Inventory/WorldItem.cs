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
        }
        forgeryId.OnValueChanged += (a, b) => RefreshArtVisual();
        lookKey.OnValueChanged += (a, b) => RefreshArtVisual();
        RefreshArtVisual();
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
