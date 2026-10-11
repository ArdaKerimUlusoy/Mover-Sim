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

    public bool HasSimilarity => Similarity != NoSimilarity;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemIndex);
        serializer.SerializeValue(ref Count);
        serializer.SerializeValue(ref Similarity);
    }

    public bool Equals(InventorySlot other)
    {
        return ItemIndex == other.ItemIndex && Count == other.Count && Similarity == other.Similarity;
    }

    public override bool Equals(object obj) => obj is InventorySlot other && Equals(other);
    public override int GetHashCode() => (ItemIndex << 16) ^ (Count << 8) ^ Similarity;
}
