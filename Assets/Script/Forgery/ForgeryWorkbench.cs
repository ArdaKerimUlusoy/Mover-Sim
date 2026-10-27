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

    // ---------------- Masa kaydı (birden fazla masa olabilir) ----------------
    private static readonly List<ForgeryWorkbench> all = new List<ForgeryWorkbench>();
    public static IReadOnlyList<ForgeryWorkbench> All => all;

    /// <summary>Tek masaya inildi: eski kurulumdan kalan ikinci masa ("ForgeryWorkbench_2") oyunda etkisizdir
    /// (iki kişi aynı masada aynı anda, her biri kendi seçtiği eseri / kaydı çizer). Editörde sahneden de silinir.</summary>
    public bool Retired => name == "ForgeryWorkbench_2";

    private void OnEnable()
    {
        if (Retired)
        {
            // Ağ nesnesi olduğu için yok edilmez: görünmez ve etkileşimsiz kalır
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return;
        }
        if (!all.Contains(this)) all.Add(this);
    }
    private void OnDisable() { all.Remove(this); }

    /// <summary>Verilen noktaya en yakın masa (yoksa null). Masalar sahnede sabit; çalışma anında eklenmez.</summary>
    public static ForgeryWorkbench Nearest(Vector3 pos)
    {
        ForgeryWorkbench best = null;
        float bd = float.MaxValue;
        for (int i = all.Count - 1; i >= 0; i--)
        {
            ForgeryWorkbench b = all[i];
            if (b == null || b.Retired) { all.RemoveAt(i); continue; }
            float d = (b.transform.position - pos).sqrMagnitude;
            if (d < bd) { bd = d; best = b; }
        }
        if (best == null) best = Object.FindFirstObjectByType<ForgeryWorkbench>(); // kayıt henüz dolmadıysa
        return best;
    }

    /// <summary>Yerel oyuncuya (ana kameraya) en yakın masa.</summary>
    public static ForgeryWorkbench NearestToLocal()
    {
        Camera c = Camera.main;
        return Nearest(c != null ? c.transform.position : Vector3.zero);
    }

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

    public override bool CanInteract(GameObject player) => base.CanInteract(player) && !Retired && !WorkbenchUI.IsOpen && WorkbenchUI.HasTargets();

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
        public ushort replaceId;
    }

    // Sunucu: oyuncu başına süren yükleme ve son üretim zamanı
    private readonly Dictionary<ulong, Upload> uploads = new Dictionary<ulong, Upload>();
    private readonly Dictionary<ulong, float> lastCraft = new Dictionary<ulong, float>();

    /// <summary>İstemci: yapılan sahtenin VERİSİNİ sunucuya gönder (parça parça). Benzerliği sunucu hesaplar.</summary>
    /// <param name="replaceForgeryId">!= 0: çantadaki bu yarım sahte sürdürüldü; yenisi girince eskisi çıkar.</param>
    public void RequestCraft(ArtworkDisplay display, ForgeryData data, ushort replaceForgeryId = 0)
    {
        if (!IsSpawned || display == null || data == null) return;
        byte[] blob = data.Encode();
        if (blob.Length > ForgeryData.MaxBlobBytes) return;

        CraftBeginRpc(display.NetworkObject, blob.Length, replaceForgeryId);
        for (int offset = 0; offset < blob.Length; offset += ChunkSize)
        {
            byte[] chunk = new byte[Mathf.Min(ChunkSize, blob.Length - offset)];
            System.Array.Copy(blob, offset, chunk, 0, chunk.Length);
            CraftChunkRpc(chunk);
        }
    }

    [Rpc(SendTo.Server)]
    private void CraftBeginRpc(NetworkObjectReference displayRef, int totalBytes, ushort replaceId, RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        uploads.Remove(id);
        if (totalBytes <= 0 || totalBytes > ForgeryData.MaxBlobBytes) return;
        if (lastCraft.TryGetValue(id, out float t) && Time.time - t < 1f) return; // art arda üretim yok
        if (!TryGetNearbyPlayer(id, out _, 3f)) return;
        uploads[id] = new Upload { display = displayRef, buffer = new byte[totalBytes], replaceId = replaceId };
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

        // "Devam et": sürdürülen yarım sahte çantadan çıkar (yerine yenisi girecek)
        bool hadOld = false;
        InventorySlot old = default;
        if (up.replaceId != 0)
        {
            for (int i = 0; i < inv.SlotCount; i++)
            {
                InventorySlot s = inv.GetSlot(i);
                if (s.ForgeryId != up.replaceId || s.LookKey != display.LookKey) continue;
                old = s;
                hadOld = true;
                inv.ServerRemoveAt(i, 1);
                break;
            }
        }

        if (!inv.CanFit(forgery))
        {
            RestoreOld(inv, hadOld, old);
            inv.ServerNotify("inv.full");
            return;
        }

        ushort forgeryId = ForgeryRegistry.ServerAdd(up.buffer, data);
        if (!inv.ServerAddItem(forgery, sim, forgeryId, display.LookKey))
        {
            RestoreOld(inv, hadOld, old);
            inv.ServerNotify("inv.full");
            return;
        }
        // Herkes bu sahteyi yapanın gördüğü şekliyle görsün (silindir/hazır şekil yerine): veriyi hemen yolla
        inv.ServerBroadcastForgery(forgeryId, up.buffer);

        inv.ServerNotify("bench.done", sim.ToString(), forgery);
        inv.ServerPlaySound(SoundId.HoldComplete, transform.position);
    }

    /// <summary>Yeni sahte çantaya girmediyse sürdürülen eski yarım sahteyi geri koy.</summary>
    private static void RestoreOld(PlayerInventory inv, bool hadOld, InventorySlot old)
    {
        if (!hadOld || ItemDatabase.Instance == null) return;
        ItemDefinition def = ItemDatabase.Instance.Get(old.ItemIndex);
        if (def != null) inv.ServerAddItem(def, old.Similarity, old.ForgeryId, old.LookKey);
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
