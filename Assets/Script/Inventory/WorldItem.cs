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

    private bool hasPendingSimilarity;
    private byte pendingSimilarity;
    private bool thrown;
    private float ignoreImpactsUntil;
    private float nextImpactTime;

    public ItemDefinition Item => item;
    public byte Similarity => IsSpawned ? similarity.Value : ToByte(similarityPercent);
    public bool IsConsumed { get; private set; }

    /// <summary>Sunucu: çantadan bırakılan eşyayı spawn etmeden önce benzerliğini ayarlar.</summary>
    public void SetPendingSimilarity(byte value)
    {
        hasPendingSimilarity = true;
        pendingSimilarity = value;
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
            // Sahneye konmuş eşyalar oyun başında yere otururken ses çıkarmasın.
            ignoreImpactsUntil = Time.time + (thrown ? 0.05f : 1.5f);
        }
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
