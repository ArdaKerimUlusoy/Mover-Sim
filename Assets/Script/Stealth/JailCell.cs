using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Müzenin hapsi (GDD Bölüm 6'nın basit hali): güvenliğe yakalanan oyuncu buraya kapatılır.
///  - Yakalanan oyuncunun çantasındaki orijinaller kaidelerine geri döner (PlayerStealth.ServerCatch).
///  - Hapisteki oyuncu parmaklıkların arkasında bekler; bir arkadaşı dışarıdan E basılı tutarak kurtarabilir.
///    Kurtarırken bir güvenlik ya da kamera GÖRÜRSE alarm çalar ve kurtaran aranır; kimse görmezse sessiz kaçış (alarm yok).
///  - TEK KİŞİLİK oyunda süre dolunca kendiliğinden çıkar (oyun kilitlenmesin). Birden fazla oyuncu varken otomatik çıkış YOKTUR:
///    yalnızca arkadaşı kurtarabilir (hepsi yakalanırsa koşu biter: MuseumSession.ServerCheckAllCaught).
/// Kurulum: hapis kapısı objesine NetworkObject + bu bileşen. Kapının "ileri" yönü hücrenin DIŞINA bakar.
/// İsteğe bağlı "Inside Point" / "Release Point"; verilmezse kapının arkası / önü kullanılır.
/// </summary>
public class JailCell : Interactable
{
    public static JailCell Instance { get; private set; }

    [SerializeField] private Transform insidePoint;
    [SerializeField] private Transform releasePoint;
    [Tooltip("Yalnızca tek kişilik oyunda: kimse kurtarmazsa kaç saniye sonra serbest kalır.")]
    [SerializeField] private float jailSeconds = 30f;
    [SerializeField] private float rescueHoldSeconds = 2.5f;
    [Tooltip("Kurtarma girişimi güvenlikleri en fazla bu sıklıkla uyarır (saniye).")]
    [SerializeField] private float attemptAlertCooldown = 4f;

    private float nextAttemptAlert;
    private float rescueSeenAt = -100f; // kurtaran en son ne zaman güvenlik / kameraya görüldü (kurtarma sırasında)

    /// <summary>Sunucu: bu oyuncuyu şu an bir güvenlik ya da kamera görüyor mu?</summary>
    public static bool SeenBySecurity(PlayerStealth p)
    {
        if (p == null) return false;
        foreach (GuardAI g in SecurityManager.Guards)
            if (g != null && g.ServerCanSee(p)) return true;
        foreach (RoomCamera c in RoomCamera.All)
            if (c != null && c.ServerCanSee(p)) return true;
        return false;
    }

    /// <summary>Kat planında hapis simgesinin konacağı dünya konumu. Hapis bodrumdaysa üst kattaki planda MERDİVENİN üstü işaretlenir
    /// (BodyCam da bunu kullanır); gerçek hücre konumu için TryGetJailPosition.</summary>
    public static bool TryGetPlanPosition(out Vector3 pos)
    {
        pos = Vector3.zero;
        if (Instance == null) return false;
        MuseumLayout l = MuseumLayout.Instance;
        if (l != null && l.HasBasement) { pos = l.StairsTopWorld; return true; }
        pos = Instance.InsidePos;
        return true;
    }

    /// <summary>Hücrenin içinin gerçek dünya konumu (bodrum dahil). Hapis yoksa false.</summary>
    public static bool TryGetJailPosition(out Vector3 pos)
    {
        pos = Vector3.zero;
        if (Instance == null) return false;
        pos = Instance.InsidePos;
        return true;
    }

    private void OnEnable() => Instance = this;
    private void OnDisable() { if (Instance == this) Instance = null; }

    private Vector3 InsidePos => insidePoint != null ? insidePoint.position : transform.position - transform.forward * 1.6f;
    private Vector3 ReleasePos => releasePoint != null ? releasePoint.position : transform.position + transform.forward * 1.6f;

    private static bool AnyPrisoner()
    {
        foreach (PlayerStealth p in PlayerStealth.All)
            if (p != null && p.IsSpawned && p.IsJailed) return true;
        return false;
    }

    private static bool IsJailed(GameObject player)
    {
        PlayerStealth s = player != null ? player.GetComponent<PlayerStealth>() : null;
        return s != null && s.IsJailed;
    }

    public override float GetHoldDuration(GameObject player) => rescueHoldSeconds;

    public override bool CanInteract(GameObject player) => base.CanInteract(player) && AnyPrisoner() && !IsJailed(player);

    public override string GetPrompt(PlayerInteractor player)
    {
        if (!AnyPrisoner()) return Loc.T("jail.empty");
        return IsJailed(player != null ? player.gameObject : null) ? Loc.T("jail.inside") : Loc.T("jail.rescue");
    }

    // ---------------- Kurtarma ----------------

    /// <summary>Arkadaşın kapıda E'ye basılı tutmaya başladı: kilidi kurcalıyor → güvenlikler hapse koşar.</summary>
    public override void ClientHoldStarted(PlayerInteractor player)
    {
        if (IsSpawned && CanInteract(player != null ? player.gameObject : null)) RescueAttemptRpc();
    }

    [Rpc(SendTo.Server)]
    private void RescueAttemptRpc(RpcParams rpcParams = default)
    {
        if (!AnyPrisoner() || Time.time < nextAttemptAlert) return;
        if (!TryGetNearbyPlayer(rpcParams.Receive.SenderClientId, out NetworkObject playerObj, 2f)) return;

        PlayerStealth rescuer = playerObj.GetComponent<PlayerStealth>();
        // Kimse görmüyorsa kilidi sessizce kurcalar: güvenlikler gelmez, alarm yok
        if (!SeenBySecurity(rescuer)) return;
        rescueSeenAt = Time.time;
        nextAttemptAlert = Time.time + attemptAlertCooldown;
        if (SecurityManager.Instance != null) SecurityManager.Instance.ServerReportJailbreakAttempt(transform.position, rescuer);
        PlayerNetworkSetup ps = playerObj.GetComponent<PlayerNetworkSetup>();
        if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("jail.attempt", ps != null ? ps.PlayerName : "", "");
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        string rescuer = "";
        PlayerNetworkSetup ps = player.GetComponent<PlayerNetworkSetup>();
        if (ps != null) rescuer = ps.PlayerName;
        PlayerStealth rescuerStealth = player.GetComponent<PlayerStealth>();

        float wantedFor = SecurityManager.Instance != null ? SecurityManager.Instance.AlarmSeconds : 15f;
        foreach (PlayerStealth p in PlayerStealth.All.ToArray())
        {
            if (p == null || !p.IsSpawned || !p.IsJailed) continue;
            PlayerNetworkSetup freed = p.GetComponent<PlayerNetworkSetup>();
            if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("chat.rescued", rescuer, freed != null ? freed.PlayerName : "");
            ServerRelease(p); // hapisten çıkan normal ziyaretçiye döner (aranmaz, tanınmaz)
        }

        // Hapisten kaçırma: SADECE bir güvenlik ya da kamera gördüyse (şimdi ya da kilidi kurcalarken) alarm çalar ve kurtaran
        // aranır. Kimse görmeden açtıysa sessiz kaçış: alarm yok, polis peşinize düşmez.
        bool seen = SeenBySecurity(rescuerStealth) || Time.time - rescueSeenAt < rescueHoldSeconds + 1f;
        rescueSeenAt = -100f;
        if (!seen) return;
        if (rescuerStealth != null) rescuerStealth.ServerMarkOffender();
        if (SecurityManager.Instance != null) SecurityManager.Instance.ServerTriggerAlarm(transform.position, "alarm.jailbreak", rescuerStealth);
    }

    // ---------------- Sunucu ----------------

    /// <summary>Sunucu: oyuncuyu hücreye koyar.</summary>
    public void ServerImprison(PlayerStealth prisoner)
    {
        if (!IsServer || prisoner == null) return;

        // Aynı anda birden fazla kişi varsa üst üste binmesinler
        int index = 0;
        foreach (PlayerStealth p in PlayerStealth.All)
            if (p != null && p != prisoner && p.IsSpawned && p.IsJailed) index++;
        Vector3 pos = InsidePos + transform.right * ((index % 3 - 1) * 0.9f) + Vector3.up * 0.1f;

        prisoner.ServerSetJailed(NetworkManager.ServerTime.Time + jailSeconds);
        prisoner.ServerTeleport(pos, transform.eulerAngles.y);

        // Suçlu içeride: alarm susar, güvenlikler devriyeye döner (kurtarmaya gelen olursa yeniden tetiklenir)
        if (SecurityManager.Instance != null) SecurityManager.Instance.ServerStopAlarm("alarm.jailed");
    }

    /// <summary>Sunucu: oyuncuyu hücrenin önüne bırakır.</summary>
    public void ServerRelease(PlayerStealth prisoner, bool remember = true, bool teleport = true)
    {
        if (!IsServer || prisoner == null || !prisoner.IsJailed) return;
        prisoner.ServerSetJailed(0);
        // Hapisten çıkan oyuncu normal ziyaretçi moduna döner: güvenlikler onu artık aramaz / tanımaz
        prisoner.ServerClearHeat();
        if (teleport) prisoner.ServerTeleport(ReleasePos + Vector3.up * 0.1f, transform.eulerAngles.y);
        if (prisoner.TryGetComponent(out PlayerInventory inv)) inv.ServerPlaySound(SoundId.DoorOpen, transform.position);
    }

    /// <summary>Sunucu: herkesi serbest bırakır (yeni görev başlarken). teleport=false: çağıran oyuncuları kendisi ışınlar.</summary>
    public static void ServerReleaseAll(bool teleport = true)
    {
        if (Instance == null || !Instance.IsServer) return;
        foreach (PlayerStealth p in PlayerStealth.All)
            if (p != null && p.IsSpawned && p.IsJailed) Instance.ServerRelease(p, false, teleport);
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;
        if (!PlayerStealth.SoloGame) return; // çok oyunculu: kendiliğinden çıkış YOK, yalnızca arkadaş kurtarabilir
        double now = NetworkManager.ServerTime.Time;
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p != null && p.IsSpawned && p.IsJailed && now >= p.JailedUntil)
            {
                ServerRelease(p);
                return; // liste değişmiş olabilir; kalanlar sonraki karede
            }
        }
    }
}
