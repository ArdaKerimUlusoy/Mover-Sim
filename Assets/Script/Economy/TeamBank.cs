using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Ekip kasası (GDD Bölüm 6–7): kamyon ve ortak upgrade'ler bu paradan alınır.
/// Kişisel cüzdan ise her oyuncunun PlayerInventory'sinde (PersonalMoney).
/// Kurulum: sahnedeki ChatManager objesine (NetworkObject zaten var) bu bileşeni ekleyin.
/// </summary>
public class TeamBank : NetworkBehaviour
{
    public static TeamBank Instance { get; private set; }

    [SerializeField, Min(0)] private int startingMoney = 0;

    private readonly NetworkVariable<int> money = new NetworkVariable<int>(0);
    private readonly NetworkVariable<byte> teamBagLevel = new NetworkVariable<byte>(0);
    // Kasaya giren toplam gelir (harcamalar düşmez): görev kotası buna göre ölçülür, kasadan harcama kotayı geri düşürmesin
    private readonly NetworkVariable<int> earnedTotal = new NetworkVariable<int>(0);

    public int Money => money.Value;
    public int TeamBagLevel => teamBagLevel.Value;
    public int TotalEarned => earnedTotal.Value;

    /// <summary>Sunucu: ekip çantası upgrade'i (herkese +kg).</summary>
    public bool ServerIncreaseTeamBag()
    {
        if (!IsServer || teamBagLevel.Value >= UpgradeCatalog.MaxLevel(UpgradeType.TeamBackpacks)) return false;
        teamBagLevel.Value++;
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            PlayerInventory inv = c.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null) inv.ServerApplyCapacity();
        }
        return true;
    }

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) money.Value = startingMoney;
    }

    /// <summary>Sunucu: kasaya para ekler (ör. karaborsada satış).</summary>
    public void ServerAdd(int amount)
    {
        if (!IsServer || amount <= 0) return;
        money.Value += amount;
        earnedTotal.Value += amount;
    }

    /// <summary>Sunucu: başarısız satın almanın iadesi. Yalnızca kasaya eklenir, görev kazancına (kota) SAYILMAZ.</summary>
    public void ServerRefund(int amount)
    {
        if (!IsServer || amount <= 0) return;
        money.Value += amount;
    }

    /// <summary>Sunucu: yeterli para varsa harcar ve true döner (ör. ortak upgrade).</summary>
    public bool ServerTrySpend(int amount)
    {
        if (!IsServer || amount < 0 || money.Value < amount) return false;
        money.Value -= amount;
        return true;
    }

    /// <summary>Sunucu: kasayı sıfırlar (ör. run başarısız olunca).</summary>
    public void ServerReset()
    {
        if (!IsServer) return;
        money.Value = startingMoney;
        earnedTotal.Value = 0;
    }

    /// <summary>Sunucu: ortak upgrade seviyelerini sıfırlar (koşu sıfırlanırken, MuseumSession.ResetKeepsPersonalProgress false ise).</summary>
    public void ServerResetUpgrades()
    {
        if (!IsServer) return;
        teamBagLevel.Value = 0;
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            PlayerInventory inv = c.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null) inv.ServerApplyCapacity();
        }
    }
}
