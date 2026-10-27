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

    private int focus;      // klavye odağı (satır)
    private Vector2 scroll;

    /// <summary>Geliştirme türüne göre yuva simgesi.</summary>
    private static IconId UpgradeIcon(UpgradeType t)
    {
        switch (t)
        {
            case UpgradeType.Lockpick: return IconId.Lockpick;
            case UpgradeType.SprayPaint: return IconId.Brush;
            case UpgradeType.CameraBattery: return IconId.Camera;
            case UpgradeType.TeamBackpacks: return IconId.Players;
            case UpgradeType.SwiftGloves: return IconId.Interact;
            default: return IconId.Bag;
        }
    }

    public void DrawPanel(PlayerInventory inv)
    {
        TeamBank bank = TeamBank.Instance;
        float sc = EconomyPanelUI.Sc;
        EconomyPanelUI.PageRects pg = EconomyPanelUI.BeginPage(700f, 680f, Loc.T("shop.title"), IconId.Truck, null);
        Rect c = pg.content;

        // Bakiye: ekip kasası + kişisel cüzdan, altında ortak yetenek puanı
        float y = c.y, balH = 30f * sc;
        if (bank != null) EconomyPanelUI.Balance(new Rect(c.x, y, c.width * 0.5f, balH), IconId.Players, Loc.T("money.team", bank.Money.ToString("N0")), false);
        EconomyPanelUI.Balance(new Rect(c.x + c.width * 0.5f, y, c.width * 0.5f, balH), IconId.Money, Loc.T("money.personal", inv.PersonalMoney.ToString("N0")), true);
        y += balH;
        if (bank != null)
        {
            EconomyPanelUI.Balance(new Rect(c.x, y, c.width, balH), IconId.Gear, Loc.T("skill.points", bank.SkillPoints.ToString("N0")), false);
            y += balH;
        }
        y += 2f;
        UIUtil.DrawRect(new Rect(c.x, y, c.width, 1f), UITheme.WithAlpha(UITheme.Graphite, 0.45f));
        y += 6f;

        float hintH = 24f * sc;
        Rect hintR = new Rect(c.x, c.yMax - hintH, c.width, hintH);
        Rect view = new Rect(c.x, y, c.width, Mathf.Max(60f, hintR.y - 8f - y));

        int n = UpgradeCatalog.All.Length;
        focus = Mathf.Clamp(focus, 0, n - 1);
        focus = UIUtil.NavigateVertical(focus, n);

        int buy = -1;
        float rowH = 78f * sc;
        bool overflow = n * rowH > view.height;
        float cw = view.width - (overflow ? 16f : 0f);
        if (Event.current.type == EventType.KeyDown) scroll.y = EconomyPanelUI.EnsureVisible(scroll.y, focus, rowH, view.height);
        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, cw, n * rowH));

        for (int i = 0; i < n; i++)
        {
            UpgradeType t = UpgradeCatalog.All[i];
            int level = inv.GetUpgradeLevel(t);
            int max = UpgradeCatalog.MaxLevel(t);
            bool maxed = level >= max;
            int price = UpgradeCatalog.Price(t, level);
            PaidFrom payer = UpgradeCatalog.Payer(t);
            // Maymuncuk: kişisel cüzdan yetmezse ekip kasasından ödenir (Staff Only erişimi cebe bağlı kalmasın)
            if (UpgradeCatalog.IsConsumable(t) && !maxed && inv.PersonalMoney < price && bank != null && bank.Money >= price) payer = PaidFrom.Team;
            bool usePts = UpgradeCatalog.UsesSkillPoints(t); // kalıcı geliştirmeler yetenek puanıyla açılır
            if (usePts) price = UpgradeCatalog.SkillPrice(t, level);
            int available = usePts ? (bank != null ? bank.SkillPoints : 0) : payer == PaidFrom.Team ? (bank != null ? bank.Money : 0) : inv.PersonalMoney;
            bool canAfford = !maxed && available >= price && (usePts ? bank != null : (payer == PaidFrom.Personal || bank != null));

            Rect row = new Rect(0f, i * rowH, cw, rowH);
            EconomyPanelUI.RowBack(row, focus == i);
            Rect slot = EconomyPanelUI.RowSlot(row, !maxed);
            EconomyPanelUI.SlotIcon(slot, UpgradeIcon(t), maxed ? UITheme.Graphite : UITheme.BrassLight);

            string lv = UpgradeCatalog.IsConsumable(t) ? Loc.T("shop.count", level, max) : Loc.T("shop.level", level, max); // maymuncuk adetle satılır
            string sub = Loc.T(UpgradeCatalog.DescKey(t)) + "  •  " +
                         Loc.T(usePts ? "shop.paypoints" : payer == PaidFrom.Team ? "shop.payteam" : "shop.payyou");
            float priceW = 104f * sc, btnW = 100f * sc;
            EconomyPanelUI.RowText(row, slot, Loc.T(UpgradeCatalog.NameKey(t)), sub, priceW + btnW + 22f, lv);

            // Fiyat: para ($) ya da yetenek puanı; tükendiyse fiyat yok, düğme "MAKS"
            string priceText = maxed ? null : (usePts ? Loc.T("shop.buypts", price.ToString("N0")) : "$" + price.ToString("N0"));
            string label = maxed ? Loc.T("shop.max") : EconomyPanelUI.Tx("ui.buy", "Buy");
            if (EconomyPanelUI.PriceButton(row, priceText, label, ButtonKind.Primary, canAfford, focus == i, priceW, btnW))
            {
                focus = i;
                buy = i;
            }
        }
        GUI.EndScrollView();

        EconomyPanelUI.KeyHints(hintR, Loc.T("panel.close"), UITheme.Graphite, false, false);

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

        if (UpgradeCatalog.UsesSkillPoints(type))
        {
            // Yetenek puanı (küçük ganimet satışından): ortak havuz
            int pts = UpgradeCatalog.SkillPrice(type, level);
            if (bank == null || !bank.ServerTrySpendSkillPoints(pts))
            {
                inv.ServerNotify("shop.notenoughpts");
                return;
            }
            bool ok = type == UpgradeType.TeamBackpacks ? bank.ServerIncreaseTeamBag() : inv.ServerIncreaseUpgrade(type);
            if (!ok)
            {
                bank.ServerAddSkillPoints(pts); // iade
                return;
            }
        }
        else if (UpgradeCatalog.IsConsumable(type))
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
