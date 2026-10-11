using System;
using Unity.Netcode;

/// <summary>
/// Çantadaki bir satır. Ağ üzerinden gönderildiği için küçük tutuldu.
/// </summary>
public struct InventorySlot : INetworkSerializable, IEquatable<InventorySlot>
{
    public const byte NoSimilarity = 255;

    public ushort ItemIndex;  // ItemDatabase'deki sıra numarası
    public ushort Count;      // birikebilen eşyalarda adet
    public byte Similarity;   // sahte eserlerde benzerlik yüzdesi (0–100), yoksa 255
    public ushort ForgeryId;  // atölyede yapılan sahtenin kaydı (ForgeryRegistry), yoksa 0
    public int LookKey;       // eserin ait olduğu kaide (ArtworkDisplay.LookKey), yoksa 0

    public bool HasSimilarity => Similarity != NoSimilarity;

    /// <summary>Aynı eşya mı? (adet hariç her şey aynı)</summary>
    public bool SameItem(InventorySlot other)
    {
        return ItemIndex == other.ItemIndex && Similarity == other.Similarity && ForgeryId == other.ForgeryId && LookKey == other.LookKey;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemIndex);
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Similarity);
        serializer.SerializeValue(ref ForgeryId);
        serializer.SerializeValue(ref LookKey);
    }

    public bool Equals(InventorySlot other)
    {
        return Count == other.Count && SameItem(other);
    }

    public override bool Equals(object obj) => obj is InventorySlot other && Equals(other);
    public override int GetHashCode() => (ItemIndex << 16) ^ (Count << 8) ^ Similarity ^ (ForgeryId << 4) ^ LookKey;
}
