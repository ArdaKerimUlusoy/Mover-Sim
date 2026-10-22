using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Garaj mağazası (GDD Bölüm 7). E ile açılır; upgrade'ler satın alınır.
/// Kişisel upgrade'ler kişisel cüzdandan, ekip upgrade'leri ekip kasasından ödenir.
/// Fiyatlar ve etkiler: UpgradeCatalog. (İleride: her ziyarette rastgele 6–8 ürün.)
/// Kurulum: bir objeye NetworkObject + bu bileşen.
/// </summary>
public class GarageShop : Interactable, IInventoryPanel
{
    public override bool HandleLocally => true;

    public override string GetPrompt(PlayerInteractor player) => Loc.T("shop.prompt");

    public override void ClientInteract(PlayerInteractor player)
    {
        PlayerInventory inv = player != null ? player.GetComponent<PlayerInventory>() : null;
        if (inv != null) inv.OpenExternal(this);
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player) { }

    // ---------------- Panel ----------------

    public bool StillValid(PlayerInventory inv)
    {
        return inv != null && IsSpawned &&
               Vector3.Distance(inv.transform.position, GetClosestPoint(inv.transform.position)) <= maxDistance + 1f;
    }

    public void DrawPanel(PlayerInventory inv)
    {
        TeamBank bank = TeamBank.Instance;
        UIUtil.BeginCenteredPanel(560f, 590f);

        GUILayout.Label(Loc.T("shop.title"), UIUtil.TitleStyle);
        GUILayout.BeginHorizontal();
        if (bank != null) GUILayout.Label(Loc.T("money.team", bank.Money.ToString("N0")), UIUtil.AccentStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("money.personal", inv.PersonalMoney.ToString("N0")), UIUtil.AccentStyle);
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        int buy = -1;

        for (int i = 0; i < UpgradeCatalog.All.Length; i++)
        {
            UpgradeType t = UpgradeCatalog.All[i];
            int level = inv.GetUpgradeLevel(t);
            int max = UpgradeCatalog.MaxLevel(t);
            bool maxed = level >= max;
            int price = UpgradeCatalog.Price(t, level);
            PaidFrom payer = UpgradeCatalog.Payer(t);
            // Maymuncuk: kişisel cüzdan yetmezse ekip kasasından ödenir (Staff Only erişimi cebe bağlı kalmasın)
            if (UpgradeCatalog.IsConsumable(t) && !maxed && inv.PersonalMoney < price && bank != null && bank.Money >= price) payer = PaidFrom.Team;
            int available = payer == PaidFrom.Team ? (bank != null ? bank.Money : 0) : inv.PersonalMoney;
            bool canAfford = !maxed && available >= price && (payer == PaidFrom.Personal || bank != null);

            GUILayout.BeginHorizontal(GUILayout.Height(56f));
            GUILayout.BeginVertical();
            string lv = UpgradeCatalog.IsConsumable(t) ? Loc.T("shop.count", level, max) : Loc.T("shop.level", level, max); // maymuncuk adetle satılır
            GUILayout.Label(Loc.T(UpgradeCatalog.NameKey(t)) + "   " + lv, UIUtil.RowStyle);
            GUILayout.Label(Loc.T(UpgradeCatalog.DescKey(t)) + "  •  " +
                            Loc.T(payer == PaidFrom.Team ? "shop.payteam" : "shop.payyou"), UIUtil.SmallStyle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            GUI.enabled = canAfford;
            string label = maxed ? Loc.T("shop.max") : Loc.T("shop.buy", price.ToString("N0"));
            if (GUILayout.Button(label, UIUtil.ButtonStyle, GUILayout.Width(130f), GUILayout.Height(30f)))
            {
                buy = i;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("panel.close"), UIUtil.HintStyle);
        UIUtil.EndCenteredPanel();

        if (buy >= 0) BuyRpc(UpgradeCatalog.All[buy]);
    }

    // ---------------- Sunucu ----------------

    [Rpc(SendTo.Server)]
    private void BuyRpc(UpgradeType type, RpcParams rpcParams = default)
    {
        if (!TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out NetworkObject player)) return;
        PlayerInventory inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        int level = inv.GetUpgradeLevel(type);
        if (level >= UpgradeCatalog.MaxLevel(type)) return;

        int price = UpgradeCatalog.Price(type, level);
        TeamBank bank = TeamBank.Instance;

        if (UpgradeCatalog.IsConsumable(type))
        {
            // Önce kişisel cüzdan, yetmezse ekip kasası
            if (inv.ServerTrySpendMoney(price))
            {
                if (!inv.ServerIncreaseUpgrade(type)) { inv.ServerAddMoney(price); return; }
            }
            else if (bank != null && bank.ServerTrySpend(price))
            {
                if (!inv.ServerIncreaseUpgrade(type)) { bank.ServerRefund(price); return; }
            }
            else
            {
                inv.ServerNotify("shop.notenough");
                return;
            }
        }
        else if (UpgradeCatalog.Payer(type) == PaidFrom.Team)
        {
            if (bank == null || !bank.ServerTrySpend(price))
            {
                inv.ServerNotify("shop.notenoughteam");
                return;
            }
            if (!bank.ServerIncreaseTeamBag())
            {
                bank.ServerRefund(price); // iade (kazanç sayılmaz)
                return;
            }
        }
        else
        {
            if (!inv.ServerTrySpendMoney(price))
            {
                inv.ServerNotify("shop.notenough");
                return;
            }
            if (!inv.ServerIncreaseUpgrade(type))
            {
                inv.ServerAddMoney(price); // iade
                return;
            }
        }

        inv.ServerNotify("shop.bought", UpgradeCatalog.NameKey(type));
        inv.ServerPlaySound(SoundId.Purchase, transform.position);
    }
}
