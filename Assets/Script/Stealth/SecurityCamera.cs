using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dönen güvenlik kamerası (GDD Bölüm 5). Sağa sola tarar; görüş alanı yerde koni olarak görünür.
/// Şüpheli davranan oyuncuyu görünce şüphesi dolar ve en yakın güvenliği oraya yollar.
/// Dönüş açısı sunucu saatinden hesaplandığı için ağdan ekstra veri gönderilmez.
/// Kurulum: kamera objesine NetworkObject + bu bileşen; altına VisionConeVisual'lı bir çocuk.
///
/// Artık odalardaki kameralar RoomCamera; sahnedeki bu eski kameralar "emekli". Bunlardan biri (ağ kimliği
/// olduğu için) güvenlik odasındaki KONSOL olur: E basılı tut → bütün kameralar 4 sn kapanır (sonra bekleme süresi).
/// </summary>
public class SecurityCamera : Interactable
{
    [SerializeField] private Transform head;
    [SerializeField] private float sweepAngle = 70f;
    [SerializeField] private float sweepSpeed = 0.5f;
    [SerializeField] private float viewRange = 9f;
    [SerializeField] private float viewAngle = 50f;
    [SerializeField] private float suspicionRate = 0.7f;
    [SerializeField] private float reportCooldown = 4f;
    [SerializeField] private VisionConeVisual cone;
    [Tooltip("Bu kamera hangi görev seviyesinden itibaren açık? (GDD: kameralar 2. seviyede gelir)")]
    [SerializeField, Min(1)] private int requiredLevel = 1;

    private bool shownOnline = true;

    /// <summary>MuseumLayout her odaya kendi kamerasını (RoomCamera) kurunca sahnedeki bu eski sabit kameralar emekliye ayrılır.</summary>
    public static bool Retired;
    private bool retiredApplied;

    private static readonly System.Collections.Generic.List<SecurityCamera> all = new System.Collections.Generic.List<SecurityCamera>();
    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() => all.Remove(this);

    [Header("Konsol")]
    [SerializeField] private float disableSeconds = 4f;
    [SerializeField] private float consoleCooldown = 20f;

    /// <summary>Bu obje güvenlik odasındaki konsol mu? (MuseumLayout her bilgisayarda aynı objeyi seçer.)</summary>
    public bool IsConsole { get; private set; }

    /// <summary>Konsol olacak eski kamera: adına göre ilk (her bilgisayarda aynı).</summary>
    public static SecurityCamera PickConsole()
    {
        SecurityCamera best = null;
        foreach (SecurityCamera c in all)
            if (c != null && (best == null || string.CompareOrdinal(c.name, best.name) < 0)) best = c;
        return best;
    }

    /// <summary>MuseumLayout: masanın üstüne taşındıktan sonra tıklanabilir konsol yap.</summary>
    public void SetupAsConsole()
    {
        IsConsole = true;
        BoxCollider col = GetComponent<BoxCollider>();
        if (col == null) col = gameObject.AddComponent<BoxCollider>();
        col.center = Vector3.zero;
        col.size = new Vector3(0.9f, 0.35f, 0.6f);
        col.isTrigger = false;
        maxDistance = 2.5f;
    }

    public override float GetHoldDuration(GameObject player) => 1.2f;

    public override bool CanInteract(GameObject player)
    {
        SecurityManager sm = SecurityManager.Instance;
        return base.CanInteract(player) && IsConsole && sm != null && sm.CanDisableCameras;
    }

    public override string GetPrompt(PlayerInteractor player)
    {
        if (!IsConsole) return "";
        SecurityManager sm = SecurityManager.Instance;
        if (sm == null) return "";
        if (sm.CamerasDisabled) return Loc.T("console.off", Mathf.CeilToInt(sm.CamerasOffRemaining));
        if (!sm.CanDisableCameras) return Loc.T("console.cooldown", Mathf.CeilToInt(sm.ConsoleCooldownRemaining));
        return Loc.T("console.prompt", Mathf.RoundToInt(disableSeconds));
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        if (!IsConsole || SecurityManager.Instance == null) return;
        PlayerNetworkSetup ps = player.GetComponent<PlayerNetworkSetup>();
        SecurityManager.Instance.ServerDisableCameras(disableSeconds, consoleCooldown, ps != null ? ps.PlayerName : "");
        if (player.TryGetComponent(out PlayerInventory inv)) inv.ServerPlaySound(SoundId.HoldComplete, transform.position);
    }

    private float baseYaw;
    private float nextReport;
    private bool alerted;

    private void Awake()
    {
        baseYaw = transform.eulerAngles.y;
        if (head == null) head = transform;
        if (cone == null) cone = GetComponentInChildren<VisionConeVisual>();
    }

    private Vector3 Forward
    {
        get
        {
            float t = NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.TimeAsFloat : Time.time;
            float yaw = baseYaw + Mathf.Sin(t * sweepSpeed) * sweepAngle * 0.5f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }
    }

    private void Update()
    {
        if (Retired)
        {
            if (!retiredApplied)
            {
                retiredApplied = true;
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                if (cone != null) cone.enabled = false;
            }
            return;
        }

        bool online = MuseumSession.IsLevelActive(requiredLevel);
        if (online != shownOnline)
        {
            shownOnline = online;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = online;
        }
        if (!online) return; // bu görevde kamera yok

        Vector3 fwd = Forward;
        if (head != null) head.rotation = Quaternion.LookRotation(fwd + Vector3.down * 0.5f);

        alerted = false;

        if (IsServer && IsSpawned)
        {
            foreach (PlayerStealth p in PlayerStealth.All)
            {
                if (p == null || !p.IsSpawned) continue;
                if (!Vision.CanSee(transform.position, fwd, viewRange, viewAngle, p)) continue;

                p.ServerReportSeen(suspicionRate, transform.position);

                if (p.SuspiciousLevel > 0f)
                {
                    alerted = true;
                    if (Time.time >= nextReport && SecurityManager.Instance != null)
                    {
                        nextReport = Time.time + reportCooldown;
                        SecurityManager.Instance.ServerReportSighting(p.transform.position);
                    }
                }
            }
        }

        if (cone != null)
        {
            bool alarm = SecurityManager.Instance != null && SecurityManager.Instance.AlarmActive;
            Color c = alarm ? new Color(1f, 0.2f, 0.15f, 0.28f)
                    : alerted ? new Color(1f, 0.6f, 0.15f, 0.28f)
                    : new Color(0.4f, 0.8f, 1f, 0.18f);
            cone.Set(viewRange, viewAngle, fwd, c);
        }
    }
}
