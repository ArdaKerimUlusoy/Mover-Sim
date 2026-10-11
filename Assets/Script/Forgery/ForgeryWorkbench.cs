using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Kamyon/garajdaki SAHTE ATÖLYESİ (GDD Bölüm 3).
/// E → hangi eserin sahtesini yapacağını seç → vazo: çömlek çarkı + boya, tablo: tuval.
/// Yeşil saydam rehber eşliğinde çalışılır; bitince YAPTIĞIN eser (şekli, boyası, çizimi) sunucuya gönderilir,
/// benzerliği sunucu hesaplar ve sahte çantana girer. Elde, yerde ve kaidede senin yaptığın haliyle görünür.
/// Orijinali almaya gerek yok: hedefler müzedeki kaidelerden (ArtworkDisplay) okunur → önce sahteyi yap,
/// sonra müzede E basılı tutup değiştir.
/// Çalışma yerel yapılır (kil, kamera, arayüz sadece senin ekranında); sonuç sunucuya tek RPC ile gider.
/// Kurulum: obje + NetworkObject + bu bileşen. İsteğe bağlı çocuklar: "CameraPoint", "WorkPoint".
/// </summary>
public class ForgeryWorkbench : Interactable
{
    [SerializeField] private Transform cameraPoint;
    [SerializeField] private Transform workPoint;

    public override bool HandleLocally => true;

    /// <summary>Kameranın duracağı yer (yoksa tezgahın önü-üstü).</summary>
    public Transform CameraPoint
    {
        get
        {
            if (cameraPoint == null)
            {
                cameraPoint = transform.Find("CameraPoint");
                if (cameraPoint == null)
                {
                    cameraPoint = new GameObject("CameraPoint").transform;
                    cameraPoint.SetParent(transform, true);
                    cameraPoint.position = WorkPosition + transform.forward * -1.1f + Vector3.up * 0.75f;
                }
            }
            return cameraPoint;
        }
    }

    /// <summary>Kilin / tuvalin durduğu nokta (tezgahın üst yüzeyi).</summary>
    public Vector3 WorkPosition
    {
        get
        {
            if (workPoint == null) workPoint = transform.Find("WorkPoint");
            if (workPoint != null) return workPoint.position;
            Collider c = GetComponentInChildren<Collider>();
            float top = c != null ? c.bounds.max.y : transform.position.y + 0.5f;
            return new Vector3(transform.position.x, top, transform.position.z);
        }
    }

    public override string GetPrompt(PlayerInteractor player) => Loc.T(WorkbenchUI.HasTargets() ? "bench.prompt" : "bench.notargets");

    public override bool CanInteract(GameObject player) => base.CanInteract(player) && !WorkbenchUI.IsOpen && WorkbenchUI.HasTargets();

    public override void ClientInteract(PlayerInteractor player)
    {
        WorkbenchUI.Open(this, player);
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player) { }

    // ---------------- Sahteyi çantaya koy ----------------

    private const int ChunkSize = 1000;

    private sealed class Upload
    {
        public NetworkObjectReference display;
        public byte[] buffer;
        public int received;
    }

    // Sunucu: oyuncu başına süren yükleme ve son üretim zamanı
    private readonly Dictionary<ulong, Upload> uploads = new Dictionary<ulong, Upload>();
    private readonly Dictionary<ulong, float> lastCraft = new Dictionary<ulong, float>();

    /// <summary>İstemci: yapılan sahtenin VERİSİNİ sunucuya gönder (parça parça). Benzerliği sunucu hesaplar.</summary>
    public void RequestCraft(ArtworkDisplay display, ForgeryData data)
    {
        if (!IsSpawned || display == null || data == null) return;
        byte[] blob = data.Encode();
        if (blob.Length > ForgeryData.MaxBlobBytes) return;

        CraftBeginRpc(display.NetworkObject, blob.Length);
        for (int offset = 0; offset < blob.Length; offset += ChunkSize)
        {
            byte[] chunk = new byte[Mathf.Min(ChunkSize, blob.Length - offset)];
            System.Array.Copy(blob, offset, chunk, 0, chunk.Length);
            CraftChunkRpc(chunk);
        }
    }

    [Rpc(SendTo.Server)]
    private void CraftBeginRpc(NetworkObjectReference displayRef, int totalBytes, RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        uploads.Remove(id);
        if (totalBytes <= 0 || totalBytes > ForgeryData.MaxBlobBytes) return;
        if (lastCraft.TryGetValue(id, out float t) && Time.time - t < 1f) return; // art arda üretim yok
        if (!TryGetNearbyPlayer(id, out _, 3f)) return;
        uploads[id] = new Upload { display = displayRef, buffer = new byte[totalBytes] };
    }

    [Rpc(SendTo.Server)]
    private void CraftChunkRpc(byte[] chunk, RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        if (!uploads.TryGetValue(id, out Upload up) || chunk == null) return;
        if (chunk.Length == 0 || up.received + chunk.Length > up.buffer.Length)
        {
            uploads.Remove(id);
            return;
        }
        System.Array.Copy(chunk, 0, up.buffer, up.received, chunk.Length);
        up.received += chunk.Length;
        if (up.received < up.buffer.Length) return;

        uploads.Remove(id);
        lastCraft[id] = Time.time;
        ServerFinishCraft(id, up);
    }

    private void ServerFinishCraft(ulong clientId, Upload up)
    {
        if (!TryGetNearbyPlayer(clientId, out NetworkObject playerObj, 3f)) return;
        PlayerInventory inv = playerObj.GetComponent<PlayerInventory>();
        MuseumSession ms = MuseumSession.Instance;
        if (inv == null || ms == null || !ms.Ready) return;

        if (!up.display.TryGet(out NetworkObject displayObj)) return;
        ArtworkDisplay display = displayObj.GetComponent<ArtworkDisplay>();
        if (display == null || display.Artwork == null || !MuseumSession.IsDisplayActive(display)) return;

        // Veri gerçekten bu görevin bu eseri için mi yapılmış?
        if (!ForgeryData.TryDecode(up.buffer, out ForgeryData data)) return;
        if (data.kind != display.Kind || data.targetKey != display.LookKey || data.seed != ms.Seed) return;

        ItemDefinition forgery = FindForgeryOf(display.Artwork);
        if (forgery == null)
        {
            inv.ServerNotify("bench.nodef", display.Artwork);
            return;
        }

        byte sim = ForgeryScorer.Score(data, ms.Seed, ms.ThemeIndex, display.name);
        if (!inv.CanFit(forgery))
        {
            inv.ServerNotify("inv.full");
            return;
        }

        ushort forgeryId = ForgeryRegistry.ServerAdd(up.buffer, data);
        if (!inv.ServerAddItem(forgery, sim, forgeryId, display.LookKey))
        {
            inv.ServerNotify("inv.full");
            return;
        }

        inv.ServerNotify("bench.done", sim.ToString(), forgery);
        inv.ServerPlaySound(SoundId.HoldComplete, transform.position);
    }

    /// <summary>Bu orijinalin sahte tanımı (ItemDefinition.forgeryOf ile bağlı olan).</summary>
    public static ItemDefinition FindForgeryOf(ItemDefinition original)
    {
        ItemDatabase db = ItemDatabase.Instance;
        if (db == null || original == null) return null;
        for (int i = 0; i < db.Count; i++)
        {
            ItemDefinition d = db.Get(i);
            if (d != null && d.type == ItemType.Forgery && d.forgeryOf == original) return d;
        }
        return null;
    }
}
