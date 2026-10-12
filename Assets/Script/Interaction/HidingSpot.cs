using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Saklanma noktası (GDD Bölüm 5: dolaplar, perdeler...). E ile girilir, E ile çıkılır.
/// İçerideyken güvenlikler ve kameralar seni göremez, diğer oyuncular da görmez.
/// Aynı anda tek kişi saklanabilir.
/// Kurulum: dolap objesine NetworkObject + bu bileşen. İç ve çıkış noktaları otomatik hesaplanır
/// (istersen "Inside Point" / "Exit Point" için boş objeler verebilirsin).
/// </summary>
public class HidingSpot : Interactable
{
    private const ulong Nobody = ulong.MaxValue;

    [SerializeField] private Transform insidePoint;
    [SerializeField] private Transform exitPoint;

    private readonly NetworkVariable<ulong> occupant = new NetworkVariable<ulong>(Nobody);

    public static readonly System.Collections.Generic.List<HidingSpot> All = new System.Collections.Generic.List<HidingSpot>();
    private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    private void OnDisable() => All.Remove(this);

    /// <summary>Sunucu: içinde bu oyuncu varsa dolabı açıp onu dışarı çıkarır (güvenlik saklandığını gördü). Çıkardıysa true.</summary>
    public bool ServerEvict(PlayerStealth stealth)
    {
        if (!IsServer || stealth == null || occupant.Value != stealth.OwnerClientId) return false;
        ServerExit(stealth, stealth.OwnerClientId, stealth.NetworkObject);
        return true;
    }

    /// <summary>Sunucu: bu oyuncunun saklandığı yer (yoksa null).</summary>
    public static HidingSpot FindOccupiedBy(PlayerStealth stealth)
    {
        if (stealth == null) return null;
        foreach (HidingSpot h in All)
            if (h != null && h.IsSpawned && h.occupant.Value == stealth.OwnerClientId) return h;
        return null;
    }

    public bool IsOccupied => occupant.Value != Nobody;

    public override void OnNetworkSpawn()
    {
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong id)
    {
        if (occupant.Value == id) occupant.Value = Nobody;
    }

    private Vector3 InsidePos => insidePoint != null ? insidePoint.position : transform.position + Vector3.up * 0.1f;
    private Vector3 ExitPos => exitPoint != null ? exitPoint.position : transform.position + transform.forward * 1.2f + Vector3.up * 0.2f;
    private float FacingYaw => transform.eulerAngles.y;

    public override bool CanInteract(GameObject player)
    {
        if (!base.CanInteract(player)) return false;
        if (!IsOccupied) return true;
        NetworkObject no = player != null ? player.GetComponent<NetworkObject>() : null;
        return no != null && no.OwnerClientId == occupant.Value;
    }

    public override string GetPrompt(PlayerInteractor player)
    {
        if (IsOccupied) return Loc.T("hide.occupied");
        return Loc.T("hide.enter");
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        PlayerStealth stealth = player.GetComponent<PlayerStealth>();
        if (stealth == null) return;

        if (occupant.Value == Nobody)
        {
            occupant.Value = clientId;
            stealth.ServerSetHidden(true);
            EnterRpc(player, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
        else if (occupant.Value == clientId)
        {
            ServerExit(stealth, clientId, player);
        }
    }

    private void ServerExit(PlayerStealth stealth, ulong clientId, NetworkObject player)
    {
        occupant.Value = Nobody;
        stealth.ServerSetHidden(false);
        ExitRpc(player, RpcTarget.Single(clientId, RpcTargetUse.Temp));
    }

    /// <summary>Saklanan oyuncu içeriden E'ye basınca (dolabın içinden ışın dolabı göremez).</summary>
    public void RequestExit()
    {
        if (IsSpawned) ExitRequestRpc();
    }

    [Rpc(SendTo.Server)]
    private void ExitRequestRpc(RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        if (occupant.Value != id) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out NetworkClient client) || client.PlayerObject == null) return;
        PlayerStealth stealth = client.PlayerObject.GetComponent<PlayerStealth>();
        if (stealth != null) ServerExit(stealth, id, client.PlayerObject);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void EnterRpc(NetworkObjectReference playerRef, RpcParams rpcParams)
    {
        if (!playerRef.TryGet(out NetworkObject no)) return;
        PlayerStealth stealth = no.GetComponent<PlayerStealth>();
        if (stealth == null) return;
        stealth.LocalSetHidingSpot(this, InsidePos, FacingYaw);
        GameAudio.PlayAt(SoundId.DoorClose, transform.position, 0.6f);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void ExitRpc(NetworkObjectReference playerRef, RpcParams rpcParams)
    {
        if (!playerRef.TryGet(out NetworkObject no)) return;
        PlayerStealth stealth = no.GetComponent<PlayerStealth>();
        if (stealth == null) return;
        stealth.LocalSetHidingSpot(null, ExitPos, FacingYaw);
        GameAudio.PlayAt(SoundId.DoorOpen, transform.position, 0.6f);
    }
}
