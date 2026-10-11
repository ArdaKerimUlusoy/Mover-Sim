using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Etkileşim noktası taban sınıfı. Kapı, dolap, vitrin, kilit... hepsi bundan türetilir.
///  - holdDuration = 0  → E'ye basınca anında çalışır
///  - holdDuration > 0  → E'ye basılı tutmak gerekir (ekranda dolan halka), bırakınca iptal
/// Oyuncu tarafında PlayerInteractor bakılan objeyi bulur, yazıyı gösterir ve tutmayı yönetir.
/// Etkileşim sunucuda gerçekleşir (ServerInteract) → herkes aynı sonucu görür.
///
/// Objede NetworkObject olmalı. Sahneye konan objelerde sahneyi kaydedip commit'lemeyi unutmayın.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public abstract class Interactable : NetworkBehaviour
{
    [Header("Etkileşim")]
    [Tooltip("0 = anında. Örn. 2 = E'ye 2 saniye basılı tut.")]
    [SerializeField, Min(0f)] protected float holdDuration = 0f;
    [SerializeField, Min(0.5f)] protected float maxDistance = 3f;

    public float HoldDuration => holdDuration;
    public float MaxDistance => maxDistance;

    /// <summary>Basılı tutma süresi oyuncuya göre değişebilir (ör. hızlı değiştirme eldiveni).</summary>
    public virtual float GetHoldDuration(GameObject player) => holdDuration;

    /// <summary>true ise etkileşim sunucuya gitmez, sadece oyuncunun ekranında bir şey açar (ör. mağaza paneli).</summary>
    public virtual bool HandleLocally => false;

    /// <summary>HandleLocally = true ise E'ye basınca bu çağrılır (oyuncunun kendi bilgisayarında).</summary>
    public virtual void ClientInteract(PlayerInteractor player) { }

    /// <summary>Sunucu: RPC gönderen oyuncunun objesini bulur ve mesafeyi doğrular.</summary>
    protected bool TryGetNearbyPlayer(ulong clientId, out NetworkObject playerObj, float extraDistance = 1.5f)
    {
        playerObj = null;
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;
        playerObj = client.PlayerObject;
        if (playerObj == null) return false;
        return Vector3.Distance(playerObj.transform.position, GetClosestPoint(playerObj.transform.position)) <= maxDistance + extraDistance;
    }

    /// <summary>Ekranda görünecek yazı (ör. "[E] Open"). Çeviri anahtarlarıyla döndürün.</summary>
    public abstract string GetPrompt(PlayerInteractor player);

    /// <summary>Şu an etkileşime açık mı? (İstemcide yazıyı gizlemek, sunucuda doğrulamak için.)</summary>
    public virtual bool CanInteract(GameObject player) => IsSpawned;

    /// <summary>İstemci: etkileşim tamamlandı, sunucuya bildir.</summary>
    public void RequestInteract()
    {
        if (IsSpawned) InteractRpc();
    }

    [Rpc(SendTo.Server)]
    private void InteractRpc(RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return;
        NetworkObject playerObj = client.PlayerObject;
        if (playerObj == null) return;

        // Hile koruması: gerçekten yakında mı?
        if (Vector3.Distance(playerObj.transform.position, GetClosestPoint(playerObj.transform.position)) > maxDistance + 1.5f) return;
        if (!CanInteract(playerObj.gameObject)) return;

        ServerInteract(clientId, playerObj);
    }

    /// <summary>Sunucuda çalışır: etkileşimin asıl işi burada yapılır.</summary>
    protected abstract void ServerInteract(ulong clientId, NetworkObject player);

    protected Vector3 GetClosestPoint(Vector3 from)
    {
        Collider c = GetComponentInChildren<Collider>();
        return c != null ? c.ClosestPoint(from) : transform.position;
    }
}
