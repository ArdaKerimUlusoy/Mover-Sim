using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Kamyon/garajdaki SAHTE ATÖLYESİ (GDD Bölüm 3).
/// E → hangi eserin sahtesini yapacağını seç → vazo: çömlek çarkı + boya, tablo: tuval.
/// Yeşil saydam rehber eşliğinde çalışılır; ESC ile bitince sahte, gerçek benzerlik yüzdesiyle ÇANTANA girer.
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

    /// <summary>İstemci: yapılan sahteyi sunucuya bildir.</summary>
    public void RequestCraft(int originalItemIndex, byte similarity)
    {
        if (IsSpawned) CraftRpc(originalItemIndex, similarity);
    }

    [Rpc(SendTo.Server)]
    private void CraftRpc(int originalItemIndex, byte similarity, RpcParams rpcParams = default)
    {
        if (!TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out NetworkObject playerObj, 3f)) return;
        PlayerInventory inv = playerObj.GetComponent<PlayerInventory>();
        ItemDatabase db = ItemDatabase.Instance;
        if (inv == null || db == null) return;

        ItemDefinition original = db.Get(originalItemIndex);
        if (original == null || original.type != ItemType.Original) return;

        ItemDefinition forgery = FindForgeryOf(original);
        if (forgery == null)
        {
            inv.ServerNotify("bench.nodef", original);
            return;
        }

        byte sim = (byte)Mathf.Clamp(similarity, 0, 100);
        if (!inv.ServerAddItem(forgery, sim))
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
        ItemDefinition fallback = null;
        for (int i = 0; i < db.Count; i++)
        {
            ItemDefinition d = db.Get(i);
            if (d == null || d.type != ItemType.Forgery) continue;
            if (d.forgeryOf == original) return d;
            if (d.forgeryOf == null && fallback == null) fallback = d; // her esere uyan test sahtesi
        }
        return fallback;
    }
}
