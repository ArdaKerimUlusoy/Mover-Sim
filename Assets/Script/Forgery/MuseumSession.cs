using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Görev / müze oturumu (sunucu belirler, herkese senkronlanır):
///  - Her oda kurulduğunda ve her yeni görevde rastgele bir MÜZE TEMASI seçilir (bir öncekiyle asla aynı olmaz).
///    Tema: duvar/zemin renkleri, vazo ve tablo görünümleri, eser adları.
///  - Tohum (seed): kaidelerdeki vazolar/tablolar her bilgisayarda bu tohumdan AYNI üretilir (ArtworkLook).
///  - Seviye: 1. görev 1 vazo, 2. görev 2 vazo, 3+ 3 vazo (ArtworkDisplay.requiredLevel).
///  - Kota: bu görevde ekip kasasına girmesi gereken para. Dolunca host N ile sonraki göreve geçer.
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

    private static int lastTheme = -1; // aynı uygulama açıkken bir sonraki oda/görev farklı müze olsun

    private GUIStyle hudStyle, hudBig;

    public bool Ready => IsSpawned && themeIndex.Value >= 0;
    public int Level => level.Value;
    public int Seed => seed.Value;
    public int ThemeIndex => Mathf.Max(0, themeIndex.Value);
    public MuseumTheme Theme => ArtworkLook.Theme(ThemeIndex);
    public int Quota => quota.Value;
    public int Earned => TeamBank.Instance != null && baseline.Value != int.MinValue ? TeamBank.Instance.Money - baseline.Value : 0;
    public bool Complete => quota.Value > 0 && Earned >= quota.Value;

    private void Awake() { Instance = this; }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        themeIndex.OnValueChanged += OnLookChanged;
        seed.OnValueChanged += OnLookChanged;
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
        seed.OnValueChanged -= OnLookChanged;
        level.OnValueChanged -= OnLookChanged;
    }

    private void OnLookChanged(int a, int b) => ApplyTheme();

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

            // Host: görev tamamsa N / Enter ile sonraki görev
            Keyboard kb = Keyboard.current;
            if (IsHost && Complete && kb != null && !ChatUI.IsTyping && kb.nKey.wasPressedThisFrame)
                ServerNextMission();
        }
    }

    private int ComputeQuota()
    {
        int sum = 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !IsDisplayActive(d)) continue;
            if (ArtworkLook.KindOf(d.Artwork) == ArtKind.None) continue; // ana hedefler: vazolar ve tablolar
            sum += d.Artwork.baseValue;
        }
        int q = Mathf.RoundToInt(sum * quotaFactor) + quotaExtra;
        return Mathf.Max(50, Mathf.CeilToInt(q / 50f) * 50);
    }

    public void ServerNextMission()
    {
        if (!IsServer) return;
        level.Value = level.Value + 1;
        PickNewMuseum();
        baseline.Value = TeamBank.Instance != null ? TeamBank.Instance.Money : 0;
        foreach (ArtworkDisplay d in ArtworkDisplay.All) if (d != null) d.ServerResetForMission();
        if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("mission.next", Theme.name, level.Value.ToString());
    }

    // ---------------- Herkes ----------------

    public static bool IsDisplayActive(ArtworkDisplay d)
    {
        return Instance == null || !Instance.Ready || Instance.Level >= d.RequiredLevel;
    }

    /// <summary>Bir kaidedeki eserin vazo görünümü (tohum + kaide adı).</summary>
    public VaseLook VaseFor(ArtworkDisplay d) => ArtworkLook.Vase(Seed, ThemeIndex, d.name);
    public PaintingLook PaintingFor(ArtworkDisplay d) => ArtworkLook.Painting(Seed, ThemeIndex, d.name);

    /// <summary>Kaide için görünür eser adı (temaya göre).</summary>
    public string ArtworkName(ArtworkDisplay d)
    {
        if (!Ready || d == null) return d != null && d.Artwork != null ? d.Artwork.DisplayName : "";
        switch (ArtworkLook.KindOf(d.Artwork))
        {
            case ArtKind.Vase: return VaseFor(d).Name;
            case ArtKind.Painting: return PaintingFor(d).Name;
            default: return d.Artwork != null ? d.Artwork.DisplayName : "";
        }
    }

    private void ApplyTheme()
    {
        if (!Ready) return;
        LookVersion++;
        MuseumTheme th = Theme;

        // Müze yapısını boya (malzeme örneği: paylaşılan asset'ler değişmez)
        GameObject root = GameObject.Find(museumRootName);
        if (root != null)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                Color? c = null;
                if (n.StartsWith("Wall_") || n.StartsWith("Divider") || n.StartsWith("Gallery_Partition")) c = th.wall;
                else if (n == "Floor") c = th.floor;
                else if (n.StartsWith("Display_")) c = th.trim;
                else if (n.StartsWith("LootTable")) c = th.trim * 0.6f;
                if (c == null) continue;
                Material m = r.material; // örnek
                Color col = c.Value; col.a = 1f;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                else m.color = col;
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
        Rect r = new Rect(12f, 84f, 330f, 70f);
        UIUtil.DrawRect(r, new Color(0.05f, 0.06f, 0.08f, 0.75f));
        GUI.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 22f), Loc.T("mission.title", Level, th.name, th.City), hudStyle);
        int earned = Mathf.Max(0, Earned);
        GUI.Label(new Rect(r.x + 10f, r.y + 26f, r.width - 20f, 20f), Loc.T("mission.quota", earned, Quota), hudStyle);
        Rect bar = new Rect(r.x + 10f, r.y + 50f, r.width - 20f, 8f);
        UIUtil.DrawRect(bar, new Color(1f, 1f, 1f, 0.15f));
        float ratio = Quota > 0 ? Mathf.Clamp01(earned / (float)Quota) : 0f;
        UIUtil.DrawRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Complete ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.8f, 0.3f));

        if (Complete)
        {
            UIUtil.DrawPrompt(Loc.T("mission.complete"), Screen.height * 0.2f, new Color(0.55f, 1f, 0.6f), 26);
            UIUtil.DrawPrompt(IsHost ? Loc.T("mission.nexthost") : Loc.T("mission.nextwait"), Screen.height * 0.2f + 34f, Color.white, 16);
        }
    }
}
