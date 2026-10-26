using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Karaborsa (GDD Bölüm 7). E ile açılır; çantadaki ORİJİNAL eserler satılır.
/// Fiyat = taban değer × benzerlik çarpanı (PriceRules). Paranın %80'i ekip kasasına, %20'si satana.
/// Kurulum: bir objeye NetworkObject + bu bileşen.
/// </summary>
public class BlackMarket : Interactable, IInventoryPanel
{
    private Vector2 scroll;

    public override bool HandleLocally => true;

    public override string GetPrompt(PlayerInteractor player) => Loc.T("market.prompt");

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
        ItemDatabase db = ItemDatabase.Instance;
        UIUtil.BeginCenteredPanel(520f, 460f);

        GUILayout.Label(Loc.T("market.title"), UIUtil.TitleStyle);
        int sharePct = Mathf.RoundToInt(PriceRules.SellerShare * 100f);
        GUILayout.Label(TeamBank.Instance != null ? Loc.T("market.help", 100 - sharePct, sharePct) : Loc.T("market.helpnobank"), UIUtil.SmallStyle);
        GUILayout.Space(8f);

        int total = 0, totalPts = 0, count = 0;
        int sellIndex = -1;
        if (TeamBank.Instance != null) GUILayout.Label(Loc.T("skill.points", TeamBank.Instance.SkillPoints.ToString("N0")), UIUtil.AccentStyle);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300f));
        for (int i = 0; i < inv.SlotCount; i++)
        {
            InventorySlot s = inv.GetSlot(i);
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null || def.type != ItemType.Original) continue;

            bool small = def.IsSmallLoot; // küçük ganimet: para değil yetenek puanı
            int price = small ? PriceRules.SkillPointsFor(def, s) : PriceRules.SalePrice(def, s);
            if (small) totalPts += price * s.Count; else total += price;
            count++;

            GUILayout.BeginHorizontal(GUILayout.Height(40f));
            Rect icon = GUILayoutUtility.GetRect(34f, 34f, GUILayout.Width(34f), GUILayout.Height(34f));
            icon.y += 3f;
            UIUtil.DrawRect(icon, new Color(1f, 1f, 1f, 0.06f));
            Texture2D iconTex = ItemVisuals.IconFor(def, s);
            if (iconTex != null) GUI.DrawTexture(icon, iconTex, ScaleMode.ScaleToFit);
            else UIUtil.DrawRect(new Rect(icon.x + 4f, icon.y + 4f, icon.width - 8f, icon.height - 8f), def.uiColor);

            GUILayout.Space(8f);
            GUILayout.BeginVertical();
            GUILayout.Label(def.DisplayName, UIUtil.RowStyle);
            string quality = s.HasSimilarity
                ? Loc.T("card.swapquality", s.Similarity, ItemDefinition.GradeName(s.Similarity))
                : Loc.T("market.hot");
            GUILayout.Label(quality, UIUtil.SmallStyle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(small ? Loc.T("market.sellpts", price.ToString("N0")) : Loc.T("market.sell", price.ToString("N0")), UIUtil.ButtonStyle, GUILayout.Width(130f), GUILayout.Height(28f)))
            {
                sellIndex = i;
            }
            GUILayout.EndHorizontal();
        }

        if (count == 0) GUILayout.Label(Loc.T("market.none"), UIUtil.RowStyle);
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        GUI.enabled = count > 0;
        string allLabel = totalPts > 0 && total > 0 ? Loc.T("market.sellallboth", total.ToString("N0"), totalPts.ToString("N0"))
                        : totalPts > 0 ? Loc.T("market.sellallpts", totalPts.ToString("N0"))
                        : Loc.T("market.sellall", total.ToString("N0"));
        bool sellAll = GUILayout.Button(allLabel, UIUtil.ButtonStyle, GUILayout.Height(34f));
        GUI.enabled = true;

        GUILayout.Label(Loc.T("panel.close"), UIUtil.HintStyle);
        UIUtil.EndCenteredPanel();

        if (sellIndex >= 0) SellRpc(sellIndex, inv.GetSlot(sellIndex));
        else if (sellAll) SellAllRpc();
    }

    // ---------------- Sunucu ----------------

    [Rpc(SendTo.Server)]
    private void SellRpc(int slotIndex, InventorySlot expected, RpcParams rpcParams = default)
    {
        if (!TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out NetworkObject player)) return;
        PlayerInventory inv = player.GetComponent<PlayerInventory>();
        if (inv == null || slotIndex < 0 || slotIndex >= inv.SlotCount) return;
        if (!inv.GetSlot(slotIndex).SameItem(expected)) return; // bu arada çanta değişmiş

        int price = ServerSellSlot(inv, slotIndex, out ItemDefinition def, out bool isPts);
        if (price <= 0) return;

        inv.ServerNotify(isPts ? "market.soldpts" : "market.sold", price.ToString("N0"), def);
        inv.ServerPlaySound(SoundId.Sell, transform.position);
    }

    [Rpc(SendTo.Server)]
    private void SellAllRpc(RpcParams rpcParams = default)
    {
        if (!TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out NetworkObject player)) return;
        PlayerInventory inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        int total = 0, totalPts = 0, sold = 0;
        // Sondan başa: satır silindikçe indeksler kaymasın
        for (int i = inv.SlotCount - 1; i >= 0; i--)
        {
            int price = ServerSellSlot(inv, i, out _, out bool isPts);
            if (price <= 0) continue;
            if (isPts) totalPts += price; else total += price;
            sold++;
        }

        if (sold == 0) return;
        if (total > 0) inv.ServerNotify("market.soldall", total.ToString("N0"));
        if (totalPts > 0) inv.ServerNotify("market.soldallpts", totalPts.ToString("N0"));
        inv.ServerPlaySound(SoundId.Sell, transform.position);
    }

    /// <summary>Bir satırdaki orijinal eserlerin hepsini satar, kazancı paylaştırır. Satılmadıysa 0.</summary>
    private static int ServerSellSlot(PlayerInventory inv, int slotIndex, out ItemDefinition def, out bool isPts)
    {
        isPts = false;
        InventorySlot s = inv.GetSlot(slotIndex);
        def = ItemDatabase.Instance != null ? ItemDatabase.Instance.Get(s.ItemIndex) : null;
        if (def == null || def.type != ItemType.Original) return 0;

        // Küçük ganimet: para/kota değil, yalnızca ortak yetenek puanı (kasa yoksa satılamaz)
        if (def.IsSmallLoot)
        {
            TeamBank pb = TeamBank.Instance;
            if (pb == null) return 0;
            int pts = PriceRules.SkillPointsFor(def, s) * s.Count;
            inv.ServerRemoveAt(slotIndex, s.Count);
            pb.ServerAddSkillPoints(pts);
            isPts = true;
            return pts;
        }

        int price = PriceRules.SalePrice(def, s) * s.Count;
        inv.ServerRemoveAt(slotIndex, s.Count);

        TeamBank bank = TeamBank.Instance;
        PriceRules.Split(price, bank != null, out int team, out int personal);
        if (bank != null) bank.ServerAdd(team);
        inv.ServerAddMoney(personal);
        return price;
    }
}
