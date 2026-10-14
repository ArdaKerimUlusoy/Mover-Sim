using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Görev / müze oturumu (sunucu belirler, herkese senkronlanır):
///  - Her oda kurulduğunda ve her yeni görevde rastgele bir MÜZE TEMASI seçilir (bir öncekiyle asla aynı olmaz).
///    Tema: duvar/zemin renkleri, eser görünümleri ve adları.
///  - Tohum (seed): kaidelerdeki eserler her bilgisayarda bu tohumdan AYNI üretilir (ArtworkLook).
///  - Seviye (GDD Bölüm 8): eserler, güvenlikler ve kameralar seviyeyle açılır (requiredLevel).
///      1: basit tablolar, 2 güvenlik   2: + vazo, 3. güvenlik, kameralar   3: + yüzük   4+: + büst (başyapıt)
///  - Kota: bu görevde ekip kasasına girmesi gereken para.
///  - Görev, kota dolduktan sonra TÜM oyuncular kamyona dönünce tamamlanır (TruckZone); host N ile sonraki göreve geçer.
///  - Yeni görevde eski sahteler silinir: eski müzenin eserlerine göre yapılmışlardı.
/// Kurulum: ChatManager objesine (NetworkObject zaten var) bu bileşen. Create Test Museum otomatik ekler.
/// </summary>
public class MuseumSession : NetworkBehaviour
{
    public static MuseumSession Instance { get; private set; }

    /// <summary>Tema/tohum her değiştiğinde artar; kaideler görselini yeniden kurar.</summary>
    public static int LookVersion { get; private set; }

    [Tooltip("Kotayı hesaplarken ana eserlerin karaborsa değerinden ekibe kalan pay (İyi sahte ×0.85, ekip payı ×0.8).")]
    [SerializeField] private float quotaFactor = 0.68f;
    [Tooltip("Ana eserlerin üstüne eklenen miktar: her görevde biraz küçük ganimet / ekstra eser de çalmak gerekir.")]
    [SerializeField] private int quotaExtra = 100;
    [SerializeField] private string museumRootName = "TestMuseum";

    private readonly NetworkVariable<int> themeIndex = new NetworkVariable<int>(-1);
    private readonly NetworkVariable<int> seed = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> level = new NetworkVariable<int>(1);
    private readonly NetworkVariable<int> quota = new NetworkVariable<int>(0);
    private readonly NetworkVariable<int> baseline = new NetworkVariable<int>(int.MinValue);
    private readonly NetworkVariable<bool> staffDoorOpen = new NetworkVariable<bool>(false); // Staff Only kapısı maymuncukla açıldı mı

    private static int lastTheme = -1; // aynı uygulama açıkken bir sonraki oda/görev farklı müze olsun

    private GUIStyle hudStyle, hudBig;
    private MaterialPropertyBlock themeBlock;

    public bool Ready => IsSpawned && themeIndex.Value >= 0;
    public int Level => level.Value;
    public int Seed => seed.Value;
    public int ThemeIndex => Mathf.Max(0, themeIndex.Value);
    public MuseumTheme Theme => ArtworkLook.Theme(ThemeIndex);
    public int Quota => quota.Value;
    /// <summary>Staff Only kapısı bu görevde açıldı mı? (LockpickSystem açar, her yeni müzede yeniden kilitlenir.)</summary>
    public bool StaffDoorOpen => staffDoorOpen.Value;

    public void ServerOpenStaffDoor()
    {
        if (IsServer) staffDoorOpen.Value = true;
    }
    public int Earned => TeamBank.Instance != null && baseline.Value != int.MinValue ? TeamBank.Instance.Money - baseline.Value : 0;

    /// <summary>Para kotası doldu mu? (Görevin bitmesi için ekibin kamyona da dönmesi gerekir.)</summary>
    public bool QuotaMet => quota.Value > 0 && Earned >= quota.Value;

    /// <summary>Görev tamam: kota doldu VE bütün oyuncular kamyonda.</summary>
    public bool Complete
    {
        get
        {
            if (!QuotaMet) return false;
            TruckZone.Count(out int inside, out int total);
            return total > 0 && inside >= total;
        }
    }

    private void Awake()
    {
        Instance = this;
        if (GetComponent<VisitorManager>() == null) gameObject.AddComponent<VisitorManager>(); // müzeyi gezen ziyaretçiler
    }

    // ---------------- Ziyaretçiler (VisitorManager konumları bu objenin RPC'siyle gönderir) ----------------

    public void ServerSendVisitors(byte[] data)
    {
        if (IsServer && IsSpawned && data != null) VisitorSnapshotRpc(data);
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    private void VisitorSnapshotRpc(byte[] data)
    {
        if (VisitorManager.Instance != null) VisitorManager.Instance.ClientApply(data);
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        ForgeryRegistry.Clear(); // yeni oda: önceki oturumun sahteleri kalmasın

        themeIndex.OnValueChanged += OnLookChanged;
        seed.OnValueChanged += OnSeedChanged;
        level.OnValueChanged += OnLookChanged;

        if (IsServer)
        {
            level.Value = 1;
            PickNewMuseum();
        }
        ApplyTheme();
    }

    public override void OnNetworkDespawn()
    {
        themeIndex.OnValueChanged -= OnLookChanged;
        seed.OnValueChanged -= OnSeedChanged;
        level.OnValueChanged -= OnLookChanged;
    }

    private void OnLookChanged(int a, int b) => ApplyTheme();

    private void OnSeedChanged(int a, int b)
    {
        ForgeryRegistry.Clear(); // yeni görev: eski sahtelerin verisi artık gereksiz
        ApplyTheme();
    }

    // ---------------- Sunucu ----------------

    private void PickNewMuseum()
    {
        int n = ArtworkLook.Themes.Length;
        int pick = Random.Range(0, n);
        if (n > 1 && pick == lastTheme) pick = (pick + 1 + Random.Range(0, n - 1)) % n;
        lastTheme = pick;
        themeIndex.Value = pick;
        seed.Value = Random.Range(1, int.MaxValue);
        quota.Value = 0; // Update'te kaideler hazır olunca hesaplanır
        staffDoorOpen.Value = false; // yeni müzede Staff Only kapısı yine kilitli
    }

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsServer)
        {
            if (baseline.Value == int.MinValue && TeamBank.Instance != null && TeamBank.Instance.IsSpawned)
                baseline.Value = TeamBank.Instance.Money;

            if (quota.Value == 0 && ArtworkDisplay.All.Count > 0)
                quota.Value = ComputeQuota();

            // Host: görev tamamsa (kota + herkes kamyonda) N ile sonraki görev
            Keyboard kb = Keyboard.current;
            if (IsHost && kb != null && !ChatUI.IsTyping && kb.nKey.wasPressedThisFrame && Complete)
                ServerNextMission();
        }
    }

    private int ComputeQuota()
    {
        int sum = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !IsDisplayActive(d)) continue;
            if (d.Kind == ArtKind.None) continue; // ana hedefler: sahtesi yapılabilen eserler
            sum += d.Value;
        }
        int q = Mathf.RoundToInt(sum * quotaFactor) + quotaExtra;
        return Mathf.Max(50, Mathf.CeilToInt(q / 50f) * 50);
    }

    public void ServerNextMission()
    {
        if (!IsServer) return;

        // Eski müze için yapılan sahteler yeni müzede işe yaramaz: çantalardan ve yerden kaldır
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
        {
            if (c.PlayerObject == null) continue;
            PlayerInventory inv = c.PlayerObject.GetComponent<PlayerInventory>();
            if (inv != null) inv.ServerRemoveForgeries();
        }
        foreach (WorldItem wi in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if (wi != null && wi.IsSpawned && wi.Item != null && wi.Item.type == ItemType.Forgery) wi.ServerConsume();
        }
        ForgeryRegistry.Clear();

        JailCell.ServerReleaseAll(); // yeni müzeye herkes birlikte gider
        if (SecurityManager.Instance != null) SecurityManager.Instance.ServerResetForMission(); // alarm / aranma / kovalama sıfırlanır

        level.Value = level.Value + 1;
        PickNewMuseum();
        baseline.Value = TeamBank.Instance != null ? TeamBank.Instance.Money : 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null) d.ServerResetForMission();
        if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("mission.next", Theme.name, level.Value.ToString());
    }

    // ---------------- Herkes ----------------

    public static bool IsDisplayActive(ArtworkDisplay d) => IsLevelActive(d.RequiredLevel);

    /// <summary>Bu seviyede açılan şey (eser, güvenlik, kamera) şu anki görevde aktif mi?</summary>
    public static bool IsLevelActive(int requiredLevel)
    {
        return Instance == null || !Instance.Ready || Instance.Level >= requiredLevel;
    }

    public VaseLook VaseFor(ArtworkDisplay d) => ArtworkLook.Vase(Seed, ThemeIndex, d.name);
    public PaintingLook PaintingFor(ArtworkDisplay d) => ArtworkLook.Painting(Seed, ThemeIndex, d.name);
    public BustLook BustFor(ArtworkDisplay d) => ArtworkLook.Bust(Seed, ThemeIndex, d.name);
    public JewelLook JewelFor(ArtworkDisplay d) => ArtworkLook.Jewel(Seed, ThemeIndex, d.name);

    /// <summary>Kaide için görünür eser adı (temaya göre).</summary>
    public string ArtworkName(ArtworkDisplay d)
    {
        if (!Ready || d == null) return d != null && d.Artwork != null ? d.Artwork.DisplayName : "";
        switch (d.Kind)
        {
            case ArtKind.Vase: return VaseFor(d).Name;
            case ArtKind.Painting: return PaintingFor(d).Name;
            case ArtKind.Bust: return BustFor(d).Name;
            case ArtKind.Jewelry: return JewelFor(d).Name;
            default: return d.Artwork != null ? d.Artwork.DisplayName : "";
        }
    }

    private void ApplyTheme()
    {
        if (!Ready) return;
        LookVersion++;
        MuseumTheme th = Theme;

        // Müze yapısını boya. MaterialPropertyBlock: malzeme kopyası üretmez, paylaşılan asset'ler değişmez.
        // Kamyon, sokak ve garaj farklı adlar kullandığı için (TruckWall_, Ground_, GarageWall_) temadan etkilenmez.
        GameObject root = GameObject.Find(museumRootName);

        // Her haritada müzenin içi (kaideler, bölmeler, dekor, kameralar, devriye) ve dışı (cephe, sokak, ışık) değişir
        bool layoutChanged = MuseumLayout.Apply(root, Seed, ThemeIndex);
        if (layoutChanged && IsServer)
        {
            RuntimeNavMeshBaker.Rebake(); // güvenlikler yeni bölmelerin etrafından dolaşsın
            foreach (GuardAI g in SecurityManager.Guards) if (g != null) g.ServerResetForLayout();
        }
        if (root != null)
        {
            if (themeBlock == null) themeBlock = new MaterialPropertyBlock();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                Color? c = null;
                if (n.StartsWith("Wall_") || n.StartsWith("Divider") || n.StartsWith("Gallery_Partition")) c = th.wall;
                else if (n == "Floor" || n.StartsWith("Floor_")) c = th.floor;
                else if (n.StartsWith("Display_")) c = th.trim;
                else if (n.StartsWith("LootTable")) c = th.trim * 0.6f;
                else if (n.StartsWith("Trim_")) c = th.accent;
                if (c == null) continue;
                Color col = c.Value; col.a = 1f;
                r.GetPropertyBlock(themeBlock);
                themeBlock.SetColor("_BaseColor", col);
                themeBlock.SetColor("_Color", col);
                r.SetPropertyBlock(themeBlock);
            }
            foreach (Light l in root.GetComponentsInChildren<Light>(true))
            {
                if (l.gameObject.name.StartsWith("MuseumLight")) l.color = th.light;
            }
        }

        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null) d.ForceRefreshVisual();
    }

    // ---------------- HUD ----------------

    private void OnGUI()
    {
        if (!Ready || SessionMenuUI.IsPaused) return;
        if (WorkbenchUI.IsOpen) return;

        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            hudStyle.normal.textColor = Color.white;
            hudBig = new GUIStyle(hudStyle) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        }

        MuseumTheme th = Theme;
        bool quotaMet = QuotaMet;
        Rect r = new Rect(12f, 84f, 330f, quotaMet ? 92f : 70f);
        UIUtil.DrawRect(r, new Color(0.05f, 0.06f, 0.08f, 0.75f));
        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 22f), Loc.T("mission.title", Level, th.name, th.City), hudStyle);
        int earned = Mathf.Max(0, Earned);
        GUI.Label(new Rect(r.x + 10f, r.y + 26f, r.width - 20f, 20f), Loc.T("mission.quota", earned, Quota), hudStyle);
        Rect bar = new Rect(r.x + 10f, r.y + 50f, r.width - 20f, 8f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        float ratio = Quota > 0 ? Mathf.Clamp01(earned / (float)Quota) : 0f;
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), quotaMet ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.8f, 0.3f));

        if (!quotaMet) return;

        TruckZone.Count(out int inside, out int total);
        bool complete = total > 0 && inside >= total;
        GUI.Label(new Rect(r.x + 10f, r.y + 64f, r.width - 20f, 22f), Loc.T("mission.truck", inside, total), hudStyle);

        if (complete)
        {
            float y = UIUtil.TopSlot(62f);
            UIUtil.DrawPrompt(Loc.T("mission.complete"), y, new Color(0.55f, 1f, 0.6f), 26);
            UIUtil.DrawPrompt(IsHost ? Loc.T("mission.nexthost") : Loc.T("mission.nextwait"), y + 32f, Color.white, 16);
        }
        else
        {
            UIUtil.DrawPrompt(Loc.T("mission.return"), UIUtil.TopSlot(30f), new Color(1f, 0.85f, 0.4f), 22);
        }
    }
}
