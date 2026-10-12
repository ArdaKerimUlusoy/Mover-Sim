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

    public void DrawPanel(PlayerInventory inv)
    {
        PeekOpen(0.3f);
        ItemDatabase db = ItemDatabase.Instance;
        UIUtil.BeginCenteredPanel(520f, 460f);
        GUILayout.Label(Loc.T("trash.title"), UIUtil.TitleStyle);
        GUILayout.Label(Loc.T("trash.help"), UIUtil.SmallStyle);
        GUILayout.Space(8f);

        int trashIndex = -1, count = 0;
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300f));
        for (int i = 0; i < inv.SlotCount; i++)
        {
            InventorySlot s = inv.GetSlot(i);
            ItemDefinition def = db != null ? db.Get(s.ItemIndex) : null;
            if (def == null || def.type != ItemType.Forgery) continue;
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
            GUILayout.Label(s.Count > 1 ? def.DisplayName + " x" + s.Count : def.DisplayName, UIUtil.RowStyle);
            string quality = s.HasSimilarity
                ? Loc.T("card.swapquality", s.Similarity, ItemDefinition.GradeName(s.Similarity))
                : "";
            GUILayout.Label(quality, UIUtil.SmallStyle);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Loc.T("trash.one"), UIUtil.ButtonStyle, GUILayout.Width(120f), GUILayout.Height(28f))) trashIndex = i;
            GUILayout.EndHorizontal();
        }
        if (count == 0) GUILayout.Label(Loc.T("trash.none"), UIUtil.RowStyle);
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        GUI.enabled = count > 0;
        bool trashAll = GUILayout.Button(Loc.T("trash.all", count), UIUtil.ButtonStyle, GUILayout.Height(34f));
        GUI.enabled = true;
        GUILayout.Label(Loc.T("panel.close"), UIUtil.HintStyle);
        UIUtil.EndCenteredPanel();

        if (trashIndex >= 0) { inv.RequestTrash(trashIndex, inv.GetSlot(trashIndex)); PeekOpen(); }
        else if (trashAll) { inv.RequestTrashAllForgeries(); PeekOpen(); }
    }
}
