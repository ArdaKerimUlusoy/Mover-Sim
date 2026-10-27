using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kamyondaki çöp kutusu: kötü olan sahteleri (tuval, vazo, büst, yüzük) çantadan atmak için.
/// Bakıp E → çöp paneli açılır; her sahtenin yanında "Çöpe at" düğmesi + "Bütün sahteleri at".
/// Ağ nesnesi DEĞİLDİR: TruckZone tarafından her bilgisayarda aynı yere kurulur (sahneye bir şey eklemek gerekmez).
/// Silme işlemini sunucu PlayerInventory.TrashRpc ile yapar ve oyuncunun kutunun yanında olduğunu doğrular.
/// </summary>
public class TrashBin : MonoBehaviour, IInventoryPanel
{
    public const float UseRange = 2.6f;

    private static readonly List<TrashBin> all = new List<TrashBin>();
    private readonly List<Material> mats = new List<Material>();
    private Vector2 scroll;
    private Transform lid;
    private float lidOpen, lidOpenUntil;

    /// <summary>Kamyonun içine (TruckZone'un sol arka köşesine) bir çöp kutusu kurar.</summary>
    public static void CreateFor(TruckZone zone)
    {
        if (zone == null || zone.transform.Find("TrashBin") != null) return;
        GameObject go = new GameObject("TrashBin");
        go.transform.SetParent(zone.transform, false);
        // TruckZone kamyon tabanından 1.5 m yukarıda, 8 x 5 m; kutu batı duvarının dibinde, kuzey köşede
        go.transform.localPosition = new Vector3(-3.4f, -1.47f, 1.9f);
        go.AddComponent<TrashBin>();
    }

    public static TrashBin Nearest(Vector3 pos, float maxDistance)
    {
        TrashBin best = null;
        float bestD = maxDistance;
        foreach (TrashBin b in all)
        {
            if (b == null) continue;
            float d = Vector3.Distance(pos, b.transform.position);
            if (d <= bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private void Awake()
    {
        Color body = new Color(0.22f, 0.42f, 0.3f), rim = new Color(0.14f, 0.16f, 0.17f), stripe = new Color(0.95f, 0.85f, 0.3f);
        Part("Body", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.56f, 0.42f, 0.56f), body);
        Part("Base", PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.6f, 0.03f, 0.6f), rim);
        Part("Rim", PrimitiveType.Cylinder, new Vector3(0f, 0.85f, 0f), new Vector3(0.62f, 0.03f, 0.62f), rim);
        Part("Stripe", PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.57f, 0.04f, 0.57f), stripe);
        // Kapak: menteşesi arkada, kullanılınca açılır
        lid = new GameObject("LidHinge").transform;
        lid.SetParent(transform, false);
        lid.localPosition = new Vector3(-0.3f, 0.89f, 0f);
        Transform l = Part("Lid", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.6f, 0.03f, 0.6f), body * 0.85f);
        l.SetParent(lid, false);
        l.localPosition = new Vector3(0.3f, 0f, 0f);
        Transform h = Part("Handle", PrimitiveType.Cube, Vector3.zero, new Vector3(0.18f, 0.05f, 0.05f), rim);
        h.SetParent(lid, false);
        h.localPosition = new Vector3(0.3f, 0.05f, 0f);
        // Sahte tuval parçaları kenardan taşsın (ne işe yaradığı anlaşılsın)
        Part("Scrap1", PrimitiveType.Cube, new Vector3(0.08f, 0.86f, 0.1f), new Vector3(0.28f, 0.02f, 0.2f), new Color(0.9f, 0.85f, 0.7f)).localRotation = Quaternion.Euler(0f, 25f, 18f);
        Part("Scrap2", PrimitiveType.Cube, new Vector3(-0.06f, 0.88f, -0.08f), new Vector3(0.22f, 0.02f, 0.16f), new Color(0.75f, 0.35f, 0.25f)).localRotation = Quaternion.Euler(-14f, -40f, 0f);

        BoxCollider col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.45f, 0f);
        col.size = new Vector3(0.62f, 0.9f, 0.62f);

        all.Add(this);
    }

    private void OnDestroy()
    {
        all.Remove(this);
        foreach (Material m in mats) if (m != null) Destroy(m);
    }

    private Transform Part(string n, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = n;
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        c.a = 1f;
        Material m = ArtworkLook.MakeLit(c);
        mats.Add(m);
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    private void Update()
    {
        float target = Time.time < lidOpenUntil ? 1f : 0f;
        lidOpen = Mathf.MoveTowards(lidOpen, target, Time.deltaTime * 4f);
        if (lid != null) lid.localRotation = Quaternion.Euler(0f, 0f, lidOpen * 70f);
    }

    /// <summary>Kapak bir süre açık kalır (panel açıkken / bir şey atılınca).</summary>
    public void PeekOpen(float seconds = 1.2f) => lidOpenUntil = Mathf.Max(lidOpenUntil, Time.time + seconds);

    // ---------------- Panel ----------------

    public bool StillValid(PlayerInventory inv)
    {
        return inv != null && this != null && Vector3.Distance(inv.transform.position, transform.position) <= UseRange + 1f;
    }

    private int focus;                                   // klavye odağı: satırlar + "Bütün sahteleri at"
    private readonly List<int> rows = new List<int>();

    public void DrawPanel(PlayerInventory inv)
    {
        PeekOpen(0.3f);
        ItemDatabase db = ItemDatabase.Instance;
        float sc = EconomyPanelUI.Sc;
        EconomyPanelUI.PageRects pg = EconomyPanelUI.BeginPage(640f, 620f, Loc.T("trash.title"), IconId.Eraser, Loc.T("trash.help"));
        Rect c = pg.content;

        rows.Clear();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            InventorySlot s = inv.GetSlot(i);
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null || def.type != ItemType.Forgery) continue;
            rows.Add(i);
        }
        int count = rows.Count;

        float hintH = 24f * sc, btnH = 42f * sc;
        Rect hintR = new Rect(c.x, c.yMax - hintH, c.width, hintH);
        Rect allR = new Rect(c.x, hintR.y - 8f - btnH, c.width, btnH);
        Rect view = new Rect(c.x, c.y, c.width, Mathf.Max(60f, allR.y - 8f - c.y));

        int items = count + (count > 0 ? 1 : 0);
        if (items > 0)
        {
            focus = Mathf.Clamp(focus, 0, items - 1);
            focus = UIUtil.NavigateVertical(focus, items);
        }

        int trashIndex = -1;
        float rowH = 66f * sc;
        if (count == 0)
        {
            EconomyPanelUI.EmptyState(view, Loc.T("trash.none"), IconId.Bag);
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

                Rect row = new Rect(0f, k * rowH, cw, rowH);
                EconomyPanelUI.RowBack(row, focus == k);
                Rect slot = EconomyPanelUI.RowSlot(row);
                EconomyPanelUI.DrawItemIcon(slot, def, s);

                string quality = s.HasSimilarity
                    ? Loc.T("card.swapquality", s.Similarity, ItemDefinition.GradeName(s.Similarity))
                    : "";
                string name = s.Count > 1 ? def.DisplayName + "  x" + s.Count : def.DisplayName;
                float btnW = 130f * sc;
                EconomyPanelUI.RowText(row, slot, name, quality, btnW + 12f, null);
                if (EconomyPanelUI.PriceButton(row, null, Loc.T("trash.one"), ButtonKind.Danger, true, focus == k, 0f, btnW))
                {
                    focus = k;
                    trashIndex = i;
                }
            }
            GUI.EndScrollView();
        }

        // Geri alınamaz toplu eylem: Danger
        bool trashAll = UIUtil.Button(allR, Loc.T("trash.all", count), ButtonKind.Danger, count > 0, focus == count && count > 0);
        EconomyPanelUI.KeyHints(hintR, Loc.T("panel.close"), UITheme.Graphite, false, false);

        if (trashIndex >= 0) { inv.RequestTrash(trashIndex, inv.GetSlot(trashIndex)); PeekOpen(); }
        else if (trashAll) { inv.RequestTrashAllForgeries(); PeekOpen(); }
    }
}
