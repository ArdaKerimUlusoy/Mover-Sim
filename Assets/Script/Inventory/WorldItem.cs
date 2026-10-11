using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sahnede duran, çantaya alınabilen eşya (eser, alet, bozuk para...).
/// Prefab'ında: NetworkObject + NetworkTransform + (isteğe bağlı) Rigidbody + NetworkRigidbody + WorldItem.
/// Fiziği sunucu hesaplar. Oyuncu E'ye basınca sunucu kontrol eder, sığarsa çantaya ekler ve objeyi siler.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class WorldItem : NetworkBehaviour
{
    [SerializeField] private ItemDefinition item;

    [Tooltip("Sadece sahte eserler için: benzerlik yüzdesi (0–100). -1 = yok.")]
    [SerializeField, Range(-1, 100)] private int similarityPercent = -1;

    private readonly NetworkVariable<byte> similarity = new NetworkVariable<byte>(InventorySlot.NoSimilarity);

    private bool hasPendingSimilarity;
    private byte pendingSimilarity;

    public ItemDefinition Item => item;
    public byte Similarity => IsSpawned ? similarity.Value : ToByte(similarityPercent);
    public bool IsConsumed { get; private set; }

    /// <summary>Sunucu: çantadan bırakılan eşyayı spawn etmeden önce benzerliğini ayarlar.</summary>
    public void SetPendingSimilarity(byte value)
    {
        hasPendingSimilarity = true;
        pendingSimilarity = value;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            similarity.Value = hasPendingSimilarity ? pendingSimilarity : ToByte(similarityPercent);
        }
    }

    /// <summary>Sunucu: eşya çantaya alındı, sahneden kaldır.</summary>
    public void ServerConsume()
    {
        if (!IsServer || IsConsumed) return;
        IsConsumed = true;
        NetworkObject.Despawn(true);
    }

    private static byte ToByte(int percent)
    {
        return percent < 0 ? InventorySlot.NoSimilarity : (byte)Mathf.Clamp(percent, 0, 100);
    }
}
