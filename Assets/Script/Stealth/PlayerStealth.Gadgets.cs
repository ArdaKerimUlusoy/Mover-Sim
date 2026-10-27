using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hırsız aletleri (PlayerStealth'in parçası; ağ nesnesi eklemeye gerek kalmasın diye aynı sınıfta):
///  - Q: bozuk para fırlat → düştüğü yere gürültü, güvenlikler bakmaya gider. Güvenliğin kafasına isabet ederse
///    güvenlik CoinDazeSeconds sn olduğu yerde sersemler (göremez, yürüyemez). (Q: elinde eşya varsa önce onu bırakır.)
///  - Muz kabuğu kaldırıldı (istek üzerine). X ve J tuşları kullanılmıyor.
///  - Ziyaretçinin arkasından E basılı tut → cebinden para çal (PlayerInteractor tarafı; sunucu doğrulaması burada)
/// Hepsi görev başına sınırlıdır; kalan sayılar herkese NetworkVariable ile görünür.
/// </summary>
public partial class PlayerStealth
{
    public const Key CoinKey = Key.Q; // PlayerInventory'deki Q ile aynı tuş (elde eşya yoksa para atılır)
    public const int CoinsPerMission = 0; // görev başı bedava para yok: paralar kamyondaki dolaptan [E] ile alınır (Q çantadakini atar)
    public const int BananasPerMission = 0; // muz kabuğu kaldırıldı
    public const float CoinDazeSeconds = 2.5f;  // para kafasına gelen güvenlik bu kadar sersemler
    private const float CoinHitRadius = 0.55f;  // nişan toleransı (kafaya / gövdeye)

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

    /// <summary>PlayerInteractor çağırır (eski X / J girişi). Artık para Q ile atılır (PlayerInventory → TryThrowGadgetCoin).</summary>
    public void TickGadgetInput(Keyboard kb, Transform cam) { }

    /// <summary>Yerel oyuncu: Q ile alet parası at. Atıldıysa (ya da bekleme süresindeyse) true; alet parası bittiyse false
    /// (PlayerInventory çantadaki bozuk parayı dener).</summary>
    public bool TryThrowGadgetCoin(Transform cam)
    {
        if (!IsOwner || !IsSpawned || cam == null || IsJailed) return false;
        if (coinsLeft.Value == 0) return false;
        if (Time.time < nextGadgetTime) return true;
        nextGadgetTime = Time.time + GadgetCooldown;
        ThrowCoinRpc(cam.position, cam.forward);
        return true;
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
        float wallDist = CoinThrowRange;
        GuardAI struck = null;
        if (Physics.Raycast(start, d, out RaycastHit hit, CoinThrowRange, ~0, QueryTriggerInteraction.Ignore))
        {
            end = hit.point - d * 0.15f;
            wallDist = hit.distance;
            GuardAI hg = hit.collider.GetComponentInParent<GuardAI>(); // ışın doğrudan güvenliğin gövdesine çarptı
            if (hg != null && hg.Online) struck = hg;
        }
        else
            end = start + d * CoinThrowRange;

        // Güvenliğe isabet mi? (gövde ekseni atış çizgisine yakın ve duvardan önde; kapsülün yüzeyi duvar sayılmasın diye +0.6 m pay)
        if (struck == null)
        {
            float struckDist = wallDist + 0.6f;
            foreach (GuardAI g in SecurityManager.Guards)
            {
                if (g == null || !g.Online) continue;
                for (int k = 0; k < 3; k++)
                {
                    Vector3 c = g.transform.position + Vector3.up * (k == 0 ? 1.6f : k == 1 ? 1.15f : 0.7f); // kafa, gövde, bel
                    float along = Vector3.Dot(c - start, d);
                    if (along < 0f || along > struckDist) continue;
                    if (Vector3.Distance(start + d * along, c) > CoinHitRadius) continue;
                    struck = g;
                    struckDist = along;
                    break;
                }
            }
        }
        if (struck != null) end = struck.transform.position + Vector3.up * 1.55f;

        // Yere indir
        if (Physics.Raycast(end + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 20f, ~0, QueryTriggerInteraction.Ignore))
            end = ground.point;

        float flight = Mathf.Clamp(Vector3.Distance(start, end) / 14f, 0.25f, 0.9f);
        CoinFlyRpc(start, end, flight);
        StartCoroutine(ServerCoinLands(end, flight, struck));
    }

    private IEnumerator ServerCoinLands(Vector3 at, float delay, GuardAI struck)
    {
        yield return new WaitForSeconds(delay);

        // Kafaya isabet: güvenlik olduğu yerde sersemler (gürültü yapmaz; para ayağının dibine düşer)
        if (struck != null && struck.Online)
        {
            struck.ServerDaze(CoinDazeSeconds);
            yield break;
        }

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
