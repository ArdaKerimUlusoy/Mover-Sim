using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Taşınabilir objelerin ağ tarafı. Her kutuya şunlar eklenmeli:
///   NetworkObject + OwnerNetworkTransform + NetworkRigidbody + NetworkPickupable
///
/// Mantık: Bir oyuncu E'ye basınca sunucuya "ben alıyorum" der. Obje boştaysa sunucu
/// objenin sahipliğini (ownership) o oyuncuya verir; fizik artık onun bilgisayarında
/// hesaplanır ve diğerlerine senkronlanır. Böylece aynı anda iki kişi aynı objeyi alamaz
/// ve taşıyan kişi gecikme hissetmez.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class NetworkPickupable : NetworkBehaviour
{
    private const ulong NoHolder = ulong.MaxValue;
    private const float PendingTimeout = 1.5f;

    private readonly NetworkVariable<ulong> holderId = new NetworkVariable<ulong>(NoHolder);

    private Rigidbody body;
    private ObjectCarrier pendingCarrier;
    private float pendingSince;

    public bool IsHeld => IsSpawned && holderId.Value != NoHolder;
    public bool IsHeldByMe => IsSpawned && holderId.Value == NetworkManager.LocalClientId;
    public bool IsHeldByOther => IsHeld && !IsHeldByMe;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        // Objeyi son tutan oyuncu oyundan çıkarsa obje YOK OLMASIN, sahipliği sunucuya dönsün.
        GetComponent<NetworkObject>().DontDestroyWithOwner = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        pendingCarrier = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (holderId.Value == clientId) holderId.Value = NoHolder;
    }

    // ---------------- ObjectCarrier'ın çağırdıkları ----------------

    /// <summary>Kaldırma isteği gönderir. Sunucu onaylayıp sahiplik gelince ObjectCarrier.PickUp çağrılır.</summary>
    public void RequestPickup(ObjectCarrier carrier)
    {
        if (IsHeldByOther) return;
        pendingCarrier = carrier;
        pendingSince = Time.time;
        RequestPickupRpc();
    }

    /// <summary>Bırakınca / fırlatınca çağrılır.</summary>
    public void NotifyReleased()
    {
        pendingCarrier = null;
        if (IsSpawned) ReleaseRpc();
    }

    // ---------------- Sunucu tarafı ----------------

    [Rpc(SendTo.Server)]
    private void RequestPickupRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;

        // Başkası tutuyorsa reddet.
        if (holderId.Value != NoHolder && holderId.Value != sender) return;

        holderId.Value = sender;
        if (NetworkObject.OwnerClientId != sender)
        {
            NetworkObject.ChangeOwnership(sender);
        }
    }

    [Rpc(SendTo.Server)]
    private void ReleaseRpc(RpcParams rpcParams = default)
    {
        // Sahiplik bırakan oyuncuda kalır: obje düşerken/uçarken fiziği o hesaplamaya devam eder.
        if (holderId.Value == rpcParams.Receive.SenderClientId)
        {
            holderId.Value = NoHolder;
        }
    }

    // ---------------- İsteği bekleme ----------------

    private void Update()
    {
        if (pendingCarrier == null) return;

        if (Time.time - pendingSince > PendingTimeout || !pendingCarrier.isActiveAndEnabled)
        {
            pendingCarrier = null; // reddedildi ya da zaman aşımı
            return;
        }

        // Sahiplik bize geçti ve NetworkRigidbody fiziği bizde açtı mı?
        if (IsOwner && IsHeldByMe && !body.isKinematic)
        {
            ObjectCarrier carrier = pendingCarrier;
            pendingCarrier = null;
            carrier.PickUp(body);
        }
    }
}
