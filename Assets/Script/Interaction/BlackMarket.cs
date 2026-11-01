using System.Collections.Generic;
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

    private int focus;                                   // klavye odağı: satırlar + "Hepsini Sat"
    private readonly System.Collections.Generic.List<int> rows = new System.Collections.Generic.List<int>();

    public void DrawPanel(PlayerInventory inv)
    {
        ItemDatabase db = ItemDatabase.Instance;
        TeamBank bank = TeamBank.Instance;
        float sc = EconomyPanelUI.Sc;

        int sharePct = Mathf.RoundToInt(PriceRules.SellerShare * 100f);
        string help = bank != null ? Loc.T("market.help", 100 - sharePct, sharePct) : Loc.T("market.helpnobank");
        EconomyPanelUI.PageRects pg = EconomyPanelUI.BeginPage(680f, 660f, Loc.T("market.title"), IconId.Money, help);
        Rect c = pg.content;

        // Satılabilir satırlar + toplamlar
        rows.Clear();
        int total = 0, totalPts = 0;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            InventorySlot s = inv.GetSlot(i);
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null || def.type != ItemType.Original) continue;
            bool small = def.IsSmallLoot; // küçük ganimet: para değil yetenek puanı
            int price = small ? PriceRules.SkillPointsFor(def, s) : PriceRules.SalePrice(def, s);
            if (small) totalPts += price * s.Count; else total += price;
            rows.Add(i);
        }
        int count = rows.Count;

        // Bakiye: cüzdan (para) + ortak yetenek puanı
        float y = c.y, balH = 30f * sc;
        EconomyPanelUI.Balance(new Rect(c.x, y, c.width * 0.5f, balH), IconId.Money, Loc.T("money.personal", inv.PersonalMoney.ToString("N0")), false);
        if (bank != null)
            EconomyPanelUI.Balance(new Rect(c.x + c.width * 0.5f, y, c.width * 0.5f, balH), IconId.Gear, Loc.T("skill.points", bank.SkillPoints.ToString("N0")), true);
        y += balH + 2f;
        UIUtil.DrawRect(new Rect(c.x, y, c.width, 1f), new Color(1f, 1f, 1f, 0.06f));
        y += 6f;

        // Alt kısım: Hepsini Sat (Primary) + tuş ipucu
        float hintH = 0f, btnH = 42f * sc; // tuş ipucu satırı yok (sağ üstte kapat düğmesi)
        Rect hintR = new Rect(c.x, c.yMax - hintH, c.width, hintH);
        Rect allR = new Rect(c.x, hintR.y - 8f - btnH, c.width, btnH);
        Rect view = new Rect(c.x, y, c.width, Mathf.Max(60f, allR.y - 8f - y));

        // Klavye: ↑/↓ odak, Enter odaktaki düğmeyi tetikler
        int items = count + (count > 0 ? 1 : 0);
        if (items > 0)
        {
            focus = Mathf.Clamp(focus, 0, items - 1);
            focus = UIUtil.NavigateVertical(focus, items);
        }

        int sellIndex = -1;
        float rowH = 66f * sc;
        if (count == 0)
        {
            EconomyPanelUI.EmptyState(view, Loc.T("market.none"), IconId.Bag);
        }
        else
        {
            bool overflow = count * rowH > view.height;
            float cw = view.width - (overflow ? 16f : 0f);
            if (focus < count && Event.current.type == EventType.KeyDown) scroll.y = EconomyPanelUI.EnsureVisible(scroll.y, focus, rowH, view.height);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, cw, count * rowH));
            for (int k = 0; k < count; k++)
            {
                int i = rows[k];
                InventorySlot s = inv.GetSlot(i);
                ItemDefinition def = db.Get(s.ItemIndex);
                bool small = def.IsSmallLoot;
                int price = small ? PriceRules.SkillPointsFor(def, s) : PriceRules.SalePrice(def, s);

                Rect row = new Rect(0f, k * rowH, cw, rowH);
                EconomyPanelUI.RowBack(row, focus == k);
                Rect slot = EconomyPanelUI.RowSlot(row);
                EconomyPanelUI.DrawItemIcon(slot, def, s);

                string quality = s.HasSimilarity
                    ? Loc.T("card.swapquality", s.Similarity, ItemDefinition.GradeName(s.Similarity))
                    : Loc.T("market.hot");
                string name = s.Count > 1 ? def.DisplayName + "  x" + s.Count : def.DisplayName;
                float priceW = 104f * sc, btnW = 92f * sc;
                EconomyPanelUI.RowText(row, slot, name, quality, priceW + btnW + 22f, null);

                string priceText = small ? Loc.T("market.sellpts", price.ToString("N0")) : "$" + price.ToString("N0");
                if (EconomyPanelUI.PriceButton(row, priceText, EconomyPanelUI.Tx("ui.sell", "Sell"), ButtonKind.Secondary, true, focus == k, priceW, btnW))
                {
                    focus = k;
                    sellIndex = i;
                }
            }
            GUI.EndScrollView();
        }

        // Hepsini Sat: tek Primary eylem
        string allLabel = totalPts > 0 && total > 0 ? Loc.T("market.sellallboth", total.ToString("N0"), totalPts.ToString("N0"))
                        : totalPts > 0 ? Loc.T("market.sellallpts", totalPts.ToString("N0"))
                        : Loc.T("market.sellall", total.ToString("N0"));
        bool sellAll = UIUtil.Button(allR, allLabel, ButtonKind.Primary, count > 0, focus == count && count > 0, IconId.Money);



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

/// <summary>
/// Çanta + ekonomi panelleri (karaborsa, garaj, çöp kutusu, envanter) için ortak çizim yardımcıları.
/// Yalnızca sunum: Catalog sayfası, satır parçaları, kırpmasız yazı, tuş kapaklı ipucu satırı, eşya ikonu.
/// </summary>
public static class EconomyPanelUI
{
    public static float Sc => UITheme.Scale;

    /// <summary>Loc anahtarı henüz eklenmediyse yedek metin.</summary>
    public static string Tx(string key, string fallback) => Loc.Has(key) ? Loc.T(key) : fallback;

    // ---------------- Eşya ikonu ----------------

    /// <summary>Gerçek ikon yokken türe göre tasarım sistemi simgesi (tablo/vazo/büst/yüzük/fırça/para/maymuncuk).</summary>
    public static IconId IconIdFor(ItemDefinition def)
    {
        if (def == null) return IconId.Bag;
        if (PaintBrush.IsBrush(def)) return IconId.Brush;
        switch (ArtworkLook.KindOf(def))
        {
            case ArtKind.Vase: return IconId.Vase;
            case ArtKind.Painting: return IconId.Painting;
            case ArtKind.Bust: return IconId.Bust;
            case ArtKind.Jewelry: return IconId.Ring;
        }
        if (def.type == ItemType.Tool) return IconId.Lockpick;
        if (def.type == ItemType.Distraction) return IconId.Money;
        return IconId.Bag;
    }

    /// <summary>
    /// Yuva içine eşya ikonu: eşyanın OYUN İÇİ görüntüsü (ItemIconRenderer — sahte / orijinal eser, fırça, prefab).
    /// Görüntü hazırlanırken (ilk bir iki kare) soluk tür simgesi görünür.
    /// </summary>
    public static void DrawItemIcon(Rect slot, ItemDefinition def, InventorySlot s)
    {
        if (def == null || Event.current.type != EventType.Repaint) return;
        float inset = slot.width * 0.1f;
        Rect ir = new Rect(slot.x + inset, slot.y + inset, slot.width - inset * 2f, slot.height - inset * 2f);
        Texture2D tex = ItemIconRenderer.For(def, s);
        if (tex != null) GUI.DrawTexture(ir, tex, ScaleMode.ScaleToFit, true);
        else
        {
            float i2 = slot.width * 0.26f;
            UIUtil.Icon(new Rect(slot.x + i2, slot.y + i2, slot.width - i2 * 2f, slot.height - i2 * 2f), IconIdFor(def), UITheme.WithAlpha(UITheme.Ivory, 0.25f));
        }
    }

    private static GUIStyle countStyle;

    /// <summary>Yuvanın sağ altına küçük adet (Numeric).</summary>
    public static void Count(Rect slot, string text, Color c)
    {
        if (countStyle == null) countStyle = new GUIStyle(UITheme.Style(TextRole.Numeric));
        countStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(13f * Sc));
        countStyle.alignment = TextAnchor.LowerRight;
        countStyle.wordWrap = false;
        countStyle.normal.textColor = c;
        GUI.Label(new Rect(slot.x, slot.y, slot.width - 5f, slot.height - 2f), text, countStyle);
    }

    // ---------------- Yazı ----------------

    /// <summary>Tek satıra sığmazsa sonuna "..." koyar (yazı kutudan taşmaz).</summary>
    public static string Fit(string text, TextRole role, float width)
    {
        if (string.IsNullOrEmpty(text) || width <= 8f) return text;
        GUIStyle st = UITheme.Style(role);
        bool wrap = st.wordWrap; st.wordWrap = false;
        string t = text;
        if (st.CalcSize(new GUIContent(t)).x > width)
        {
            int n = t.Length;
            while (n > 1 && st.CalcSize(new GUIContent(t.Substring(0, n) + "...")).x > width) n--;
            t = t.Substring(0, n).TrimEnd() + "...";
        }
        st.wordWrap = wrap;
        return t;
    }

    /// <summary>Sarılan yazı: r.width'e göre yüksekliği ölçer, çizer, kullandığı yüksekliği döner.</summary>
    public static float Wrapped(Rect r, string text, TextRole role, Color c, TextAnchor anchor = TextAnchor.UpperLeft)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        GUIStyle st = UITheme.Style(role, c, anchor);
        bool wrap = st.wordWrap; st.wordWrap = true;
        float h = st.CalcHeight(new GUIContent(text), r.width);
        GUI.Label(new Rect(r.x, r.y, r.width, h), text, st);
        st.wordWrap = wrap;
        return h;
    }

    // ---------------- Tuş kapaklı ipucu satırı ----------------

    private static readonly List<string> hintParts = new List<string>();
    private static readonly List<bool> hintIsKey = new List<bool>();

    private static void AddHint(string s, bool key)
    {
        s = s.Trim();
        if (s.Length == 0) return;
        hintParts.Add(s); hintIsKey.Add(key);
    }

    /// <summary>
    /// "[Tab] Aç   [Q] At" biçimli metni tuş kapakları + etiket olarak çizer (köşeli parantez = tuş kapağı).
    /// right: r'nin sağına yaslı. backing: arkasına küçük plaket (oyun dünyası üstünde okunsun). Toplam genişliği döner.
    /// </summary>
    public static float KeyHints(Rect r, string text, Color textColor, bool right, bool backing)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        hintParts.Clear(); hintIsKey.Clear();
        int i = 0;
        while (i < text.Length)
        {
            int o = text.IndexOf('[', i);
            int c = o >= 0 ? text.IndexOf(']', o) : -1;
            if (o < 0 || c < 0) { AddHint(text.Substring(i), false); break; }
            if (o > i) AddHint(text.Substring(i, o - i), false);
            AddHint(text.Substring(o + 1, c - o - 1), true);
            i = c + 1;
        }

        float sc = Sc;
        int fs = Mathf.Max(11, Mathf.RoundToInt(13f * sc));
        float keyH = Mathf.Min(r.height - 2f, 22f * sc);
        float total = 0f;
        for (int k = 0; k < hintParts.Count; k++)
        {
            if (k > 0) total += (hintIsKey[k] && !hintIsKey[k - 1]) ? 16f : 6f;
            total += hintIsKey[k] ? UIUtil.KeyCapWidth(hintParts[k], fs) : UIUtil.Measure(hintParts[k], TextRole.Caption).x + 2f;
        }
        if (Event.current.type != EventType.Repaint) return total;

        float x = right ? r.xMax - total : r.x;
        if (backing) UIUtil.Panel(new Rect(x - 8f, r.y, total + 16f, r.height), PanelKind.Plaque, 0.85f);
        for (int k = 0; k < hintParts.Count; k++)
        {
            if (k > 0) x += (hintIsKey[k] && !hintIsKey[k - 1]) ? 16f : 6f;
            if (hintIsKey[k])
            {
                float kw = UIUtil.KeyCapWidth(hintParts[k], fs);
                UIUtil.KeyCap(new Rect(x, r.y + (r.height - keyH) * 0.5f, kw, keyH), hintParts[k], fs);
                x += kw;
            }
            else
            {
                float tw = UIUtil.Measure(hintParts[k], TextRole.Caption).x + 2f;
                UIUtil.Text(new Rect(x, r.y, tw + 4f, r.height), hintParts[k], TextRole.Caption, textColor);
                x += tw;
            }
        }
        return total;
    }

    // ---------------- Catalog sayfası + satır parçaları ----------------

    public struct PageRects { public Rect page, content; }

    /// <summary>
    /// Ortada koyu sayfa: başlık (sol üst) + sağ üstte kapat düğmesi + (varsa) açıklama. İçerik alanını döner.
    /// Kapat: çantadaki açık paneli kapatır (Tab / Esc de kapatır; ekranda tuş ipucu yazılmaz).
    /// </summary>
    public static PageRects BeginPage(float w, float h, string title, IconId icon, string help)
    {
        float sc = Sc;
        w = Mathf.Min(w * sc, Screen.width - 32f);
        h = Mathf.Min(h * sc, Screen.height - 32f);
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        float pad = UITheme.S5 * sc;
        UIUtil.Panel(r, PanelKind.Plaque);

        float ts = 34f * sc, cb = 32f * sc;
        Rect close = new Rect(r.xMax - pad - cb + 6f, r.y + pad - 2f, cb, cb);
        if (UIUtil.Button(close, "", ButtonKind.Tool, true, false, IconId.Close)) PlayerInventory.ClosePanel();
        float tw = close.x - 10f - (r.x + pad);
        UIUtil.Text(new Rect(r.x + pad, r.y + pad, tw, ts), Fit(title, TextRole.Title, tw), TextRole.Title, UITheme.Ivory);
        float y = r.y + pad + ts + 10f * sc;
        if (!string.IsNullOrEmpty(help))
            y += Wrapped(new Rect(r.x + pad, y, r.width - pad * 2f, 400f), help, TextRole.Body, UITheme.IvoryDim) + 10f;

        PageRects pr;
        pr.page = r;
        pr.content = new Rect(r.x + pad, y, r.width - pad * 2f, Mathf.Max(40f, r.yMax - pad - y));
        return pr;
    }

    /// <summary>Bakiye satırı: kalın açık yazı (simgesiz).</summary>
    public static void Balance(Rect r, IconId icon, string text, bool right)
    {
        string t = Fit(text, TextRole.BodyStrong, r.width);
        UIUtil.Text(r, t, TextRole.BodyStrong, UITheme.Ivory, right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
    }

    /// <summary>Satır zemini: odaklıysa hafif açık zemin, altta çok ince ayraç.</summary>
    public static void RowBack(Rect row, bool focused)
    {
        if (Event.current.type != EventType.Repaint) return;
        if (focused) UIUtil.Round(row, new Color(1f, 1f, 1f, 0.05f), UIUtil.RadiusSmall);
        UIUtil.DrawRect(new Rect(row.x + 8f, row.yMax - 1f, row.width - 16f, 1f), new Color(1f, 1f, 1f, 0.05f));
    }

    /// <summary>Satırın solundaki yuva (kare). Çizilen yuva dikdörtgenini döner.</summary>
    public static Rect RowSlot(Rect row, bool enabled = true)
    {
        float s = row.height - 12f * Sc;
        Rect r = new Rect(row.x + 4f, row.y + (row.height - s) * 0.5f, s, s);
        UIUtil.Slot(r, enabled ? SlotState.Filled : SlotState.Disabled);
        return r;
    }

    /// <summary>Yuva içine düz simge.</summary>
    public static void SlotIcon(Rect slot, IconId id, Color c)
    {
        float inset = slot.width * 0.2f;
        UIUtil.Icon(new Rect(slot.x + inset, slot.y + inset, slot.width - inset * 2f, slot.height - inset * 2f), id, c);
    }

    /// <summary>Yuva içine doku (oyun içi görüntü ikonu). Doku yoksa soluk simge.</summary>
    public static void SlotTexture(Rect slot, Texture tex, IconId fallback, bool enabled = true)
    {
        if (Event.current.type != EventType.Repaint) return;
        float inset = slot.width * 0.08f;
        Rect ir = new Rect(slot.x + inset, slot.y + inset, slot.width - inset * 2f, slot.height - inset * 2f);
        if (tex != null)
        {
            Color old = GUI.color;
            if (!enabled) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.45f);
            GUI.DrawTexture(ir, tex, ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }
        else SlotIcon(slot, fallback, UITheme.WithAlpha(UITheme.Ivory, 0.25f));
    }

    /// <summary>Satır metni: ad (BodyStrong, isteğe bağlı rozet) + altında sarılan açıklama (Caption). Dikey ortalı.</summary>
    public static void RowText(Rect row, Rect slot, string name, string sub, float rightReserve, string badge = null)
    {
        float sc = Sc;
        float x = slot.xMax + 14f * sc;
        float w = Mathf.Max(40f, row.xMax - rightReserve - x);
        float badgeW = string.IsNullOrEmpty(badge) ? 0f : UIUtil.Measure(badge, TextRole.Caption).x + 28f;
        float nameH = 22f * sc;
        float subH = string.IsNullOrEmpty(sub) ? 0f : UIUtil.MeasureHeight(sub, TextRole.Caption, w);
        float top = row.y + Mathf.Max(2f, (row.height - nameH - subH) * 0.5f);

        string n = Fit(name, TextRole.BodyStrong, w - badgeW);
        UIUtil.Text(new Rect(x, top, w - badgeW, nameH), n, TextRole.BodyStrong, UITheme.Ivory);
        if (badgeW > 0f)
        {
            float nw = UIUtil.Measure(n, TextRole.BodyStrong).x;
            UIUtil.Badge(new Vector2(x + nw + 8f, top + (nameH - 20f * sc) * 0.5f), badge, new Color(1f, 1f, 1f, 0.1f), UITheme.IvoryDim);
        }
        if (subH > 0f) Wrapped(new Rect(x, top + nameH, w, subH), sub, TextRole.Caption, UITheme.IvoryDim);
    }

    /// <summary>Satırın sağında: fiyat (kalın, sağa yaslı) + eylem düğmesi. Tıklandıysa true.</summary>
    public static bool PriceButton(Rect row, string price, string label, ButtonKind kind, bool enabled, bool focused, float priceW, float btnW)
    {
        float sc = Sc, bh = 38f * sc;
        Rect b = new Rect(row.xMax - 6f - btnW, row.y + (row.height - bh) * 0.5f, btnW, bh);
        if (!string.IsNullOrEmpty(price))
            UIUtil.Text(new Rect(b.x - 10f - priceW, row.y, priceW, row.height), Fit(price, TextRole.Numeric, priceW), TextRole.Numeric,
                enabled ? UITheme.Ivory : UITheme.WithAlpha(UITheme.IvoryDim, 0.6f), TextAnchor.MiddleRight);
        return UIUtil.Button(b, label, kind, enabled, focused);
    }

    /// <summary>Odaktaki satır kaydırma alanında görünür kalsın.</summary>
    public static float EnsureVisible(float scrollY, int index, float rowH, float viewH)
    {
        float top = index * rowH, bottom = top + rowH;
        if (top < scrollY) return top;
        if (bottom > scrollY + viewH) return bottom - viewH;
        return scrollY;
    }

    /// <summary>Boş durum: ortada soluk simge + açıklama.</summary>
    public static void EmptyState(Rect view, string text, IconId icon)
    {
        float sc = Sc, ic = 40f * sc;
        UIUtil.Icon(new Rect(view.center.x - ic * 0.5f, view.y + view.height * 0.5f - ic - 6f, ic, ic), icon, UITheme.WithAlpha(UITheme.IvoryDim, 0.5f));
        Wrapped(new Rect(view.x + 16f, view.y + view.height * 0.5f + 4f, view.width - 32f, 80f), text, TextRole.Body, UITheme.IvoryDim, TextAnchor.UpperCenter);
    }
}
