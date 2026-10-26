using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hırsız aletleri (PlayerStealth'in parçası; ağ nesnesi eklemeye gerek kalmasın diye aynı sınıfta):
///  - X: bozuk para fırlat → düştüğü yere gürültü, güvenlikler bakmaya gider
///  - J: önüne muz kabuğu bırak → üstüne basan güvenlik / ziyaretçi kayıp düşer
///  - Ziyaretçinin arkasından E basılı tut → cebinden para çal (PlayerInteractor tarafı; sunucu doğrulaması burada)
/// Hepsi görev başına sınırlıdır; kalan sayılar herkese NetworkVariable ile görünür.
/// </summary>
public partial class PlayerStealth
{
    public const Key CoinKey = Key.X;
    public const Key BananaKey = Key.J;
    public const int CoinsPerMission = 5;
    public const int BananasPerMission = 2;

    private const float CoinNoiseRadius = 14f;
    private const float CoinThrowRange = 11f;
    private const float GadgetCooldown = 0.45f;

    private readonly NetworkVariable<byte> coinsLeft = new NetworkVariable<byte>(CoinsPerMission);
    private readonly NetworkVariable<byte> bananasLeft = new NetworkVariable<byte>(BananasPerMission);

    private float nextGadgetTime;      // sahibi
    private float serverNextGadgetTime; // sunucu

    public int CoinsLeft => coinsLeft.Value;
    public int BananasLeft => bananasLeft.Value;

    // ---------------- Sahibi: tuşlar ----------------

    /// <summary>PlayerInteractor çağırır (yalnızca yerel oyuncu, girdi engelli değilken).</summary>
    public void TickGadgetInput(Keyboard kb, Transform cam)
    {
        if (!IsOwner || !IsSpawned || kb == null || cam == null || IsJailed) return;
        if (Time.time < nextGadgetTime) return;

        if (kb[CoinKey].wasPressedThisFrame)
        {
            nextGadgetTime = Time.time + GadgetCooldown;
            if (coinsLeft.Value > 0) ThrowCoinRpc(cam.position, cam.forward);
            else if (inventory != null) GameAudio.Play2D(SoundId.UiClick, 0.3f);
        }
        else if (kb[BananaKey].wasPressedThisFrame)
        {
            nextGadgetTime = Time.time + GadgetCooldown;
            if (bananasLeft.Value > 0) DropBananaRpc();
            else GameAudio.Play2D(SoundId.UiClick, 0.3f);
        }
    }

    /// <summary>PlayerInteractor: ziyaretçi soyma basılı tutması tamamlandı.</summary>
    public void RequestPickpocket(byte visitorId)
    {
        if (IsOwner && IsSpawned) PickpocketRpc(visitorId);
    }

    // ---------------- Sunucu ----------------

    /// <summary>Sunucu: yeni görev / sıfırlama: aletler yenilenir.</summary>
    public void ServerResetGadgets()
    {
        if (!IsServer) return;
        coinsLeft.Value = CoinsPerMission;
        bananasLeft.Value = BananasPerMission;
    }

    private bool ServerGadgetOk(RpcParams p)
    {
        if (!IsServer || p.Receive.SenderClientId != OwnerClientId || IsJailed) return false;
        if (MuseumSession.Instance != null && MuseumSession.Instance.SequenceActive) return false;
        if (Time.time < serverNextGadgetTime) return false;
        serverNextGadgetTime = Time.time + GadgetCooldown * 0.8f;
        return true;
    }

    [Rpc(SendTo.Server)]
    private void ThrowCoinRpc(Vector3 origin, Vector3 dir, RpcParams rpcParams = default)
    {
        if (!ServerGadgetOk(rpcParams) || coinsLeft.Value == 0) return;
        if ((origin - transform.position).sqrMagnitude > 16f || dir.sqrMagnitude < 0.01f) return;
        coinsLeft.Value--;

        Vector3 d = dir.normalized;
        // Kendi kapsülümüzün dışından başla (kamera kapsülün içinde)
        Vector3 start = origin + d * 0.6f;
        Vector3 end;
        if (Physics.Raycast(start, d, out RaycastHit hit, CoinThrowRange, ~0, QueryTriggerInteraction.Ignore))
            end = hit.point - d * 0.15f;
        else
            end = start + d * CoinThrowRange;

        // Yere indir
        if (Physics.Raycast(end + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 20f, ~0, QueryTriggerInteraction.Ignore))
            end = ground.point;

        float flight = Mathf.Clamp(Vector3.Distance(start, end) / 14f, 0.25f, 0.9f);
        CoinFlyRpc(start, end, flight);
        StartCoroutine(ServerCoinLands(end, flight));
    }

    private IEnumerator ServerCoinLands(Vector3 at, float delay)
    {
        yield return new WaitForSeconds(delay);

        // Gürültü: yarıçaptaki güvenlikler oraya bakmaya gider (GuardAI.OnNoise → ServerInvestigate)
        NoiseSystem.Emit(at, CoinNoiseRadius);

        // Yakında kimse duymadıysa en yakın güvenlik yine de bakmaya gelsin (menzil dışı kalan biri)
        bool anyHeard = false;
        GuardAI nearest = null;
        float best = float.MaxValue;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            float dist = Vector3.Distance(g.transform.position, at);
            if (dist <= CoinNoiseRadius) anyHeard = true;
            if (dist < best) { best = dist; nearest = g; }
        }
        if (!anyHeard && nearest != null && best <= CoinNoiseRadius * 2f &&
            (SecurityManager.Instance == null || !SecurityManager.Instance.AlarmActive))
        {
            nearest.ServerInvestigate(at, false);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void CoinFlyRpc(Vector3 from, Vector3 to, float flightTime)
    {
        GameAudio.PlayAt(SoundId.CoinToss, from, 0.7f);
        GadgetWorld.ClientCoin(from, to, flightTime);
    }

    [Rpc(SendTo.Server)]
    private void DropBananaRpc(RpcParams rpcParams = default)
    {
        if (!ServerGadgetOk(rpcParams) || bananasLeft.Value == 0) return;
        bananasLeft.Value--;

        Vector3 pos = transform.position + transform.forward * 1.1f;
        if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 6f, ~0, QueryTriggerInteraction.Ignore))
            pos = ground.point;
        GadgetWorld.ServerAddBanana(this, pos);
        if (inventory != null) inventory.ServerPlaySound(SoundId.Drop, pos);
    }

    /// <summary>Sunucu: muz kabuğu görselini herkese yayınlar (mode 1 ekle, 0 kaldır, 2 hepsini temizle).</summary>
    public void ServerBananaVisual(int id, Vector3 pos, int mode)
    {
        if (IsServer && IsSpawned) BananaVisualRpc(id, pos, mode);
    }

    [Rpc(SendTo.Everyone)]
    private void BananaVisualRpc(int id, Vector3 pos, int mode)
    {
        GadgetWorld.ClientBanana(id, pos, mode);
    }

    // ---------------- Yankesicilik ----------------

    [Rpc(SendTo.Server)]
    private void PickpocketRpc(byte visitorId, RpcParams rpcParams = default)
    {
        if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId || IsJailed) return;
        VisitorManager vm = VisitorManager.Instance;
        if (vm == null) return;

        if (!vm.ServerTryPickpocket(visitorId, transform.position, out Vector3 visitorPos))
        {
            if (inventory != null) inventory.ServerNotify("pick.fail");
            return;
        }

        int amount = Random.Range(10, 41);
        if (TeamBank.Instance != null) TeamBank.Instance.ServerAdd(amount);
        if (inventory != null)
        {
            inventory.ServerNotify("pick.got", amount.ToString());
            inventory.ServerPlaySound(SoundId.Pickpocket, visitorPos);
        }
        PickpocketDoneRpc(visitorId);
    }

    [Rpc(SendTo.Everyone)]
    private void PickpocketDoneRpc(byte visitorId)
    {
        if (VisitorManager.Instance != null) VisitorManager.Instance.ClientMarkRobbed(visitorId);
    }
}
