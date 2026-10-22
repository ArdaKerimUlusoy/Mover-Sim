using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Müzenin alarm durumu (GDD Bölüm 5: Devriye → Şüphelenme → Alarm).
/// Alarm çalınca tüm güvenlikler kovalamaya geçer, ekranda kırmızı uyarı ve siren çıkar.
/// Kameralar şüpheli bir şey görünce en yakın güvenliği oraya yollar.
/// Kurulum: sahnedeki ChatManager objesine eklenir (Test Museum oluşturucu otomatik ekler).
/// </summary>
public class SecurityManager : NetworkBehaviour
{
    public static SecurityManager Instance { get; private set; }
    public static readonly List<GuardAI> Guards = new List<GuardAI>();

    [Tooltip("Alarm kaç saniye çalar. (Eski adı alarmDuration = 25; sahnedeki eski değer kullanılmasın diye yeni ad.)")]
    [SerializeField] private float alarmSeconds = 15f;

    private readonly NetworkVariable<bool> alarmActive = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<double> alarmEndsAt = new NetworkVariable<double>(0);
    // Güvenlik odasındaki konsol: kameralar şu ana kadar kapalı / konsol şu andan sonra tekrar kullanılabilir
    private readonly NetworkVariable<double> camerasOffUntil = new NetworkVariable<double>(0);
    private readonly NetworkVariable<double> consoleReadyAt = new NetworkVariable<double>(0);

    private double Now => IsSpawned ? NetworkManager.ServerTime.Time : 0;
    public bool CamerasDisabled => IsSpawned && Now < camerasOffUntil.Value;
    public float CamerasOffRemaining => Mathf.Max(0f, (float)(camerasOffUntil.Value - Now));
    public bool CanDisableCameras => IsSpawned && Now >= consoleReadyAt.Value;
    public float ConsoleCooldownRemaining => Mathf.Max(0f, (float)(consoleReadyAt.Value - Now));

    /// <summary>Sunucu: bütün oda kameralarını birkaç saniye kapatır.</summary>
    public void ServerDisableCameras(float seconds, float cooldown, string who)
    {
        if (!IsServer || !CanDisableCameras) return;
        camerasOffUntil.Value = Now + seconds;
        consoleReadyAt.Value = Now + seconds + cooldown;
        if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("console.used", who, Mathf.RoundToInt(seconds).ToString());
    }

    // ---------------- Boya spreyiyle kör edilen oda kameraları ----------------
    // Oda kameraları ağ nesnesi değil (herkes aynı adla kurar): adı + sunucu saatine göre bitiş anı herkese RPC ile gider.
    private readonly Dictionary<string, double> blindedCameras = new Dictionary<string, double>();

    /// <summary>Bu oda kamerasının lensi boyalı mı? (Herkeste aynı.)</summary>
    public bool IsCameraBlinded(string cameraName) =>
        IsSpawned && cameraName != null && blindedCameras.TryGetValue(cameraName, out double until) && Now < until;

    public float CameraBlindRemaining(string cameraName) =>
        IsSpawned && cameraName != null && blindedCameras.TryGetValue(cameraName, out double until) ? Mathf.Max(0f, (float)(until - Now)) : 0f;

    /// <summary>Sunucu: kameranın lensine boya sıkıldı, bir süre kör.</summary>
    public void ServerBlindCamera(string cameraName, float seconds, string who)
    {
        if (!IsServer || string.IsNullOrEmpty(cameraName)) return;
        CameraBlindedRpc(cameraName, Now + seconds);
    }

    [Rpc(SendTo.Everyone)]
    private void CameraBlindedRpc(string cameraName, double until)
    {
        blindedCameras[cameraName] = until;
        RoomCamera cam = RoomCamera.Find(cameraName);
        if (cam != null) GameAudio.PlayAt(SoundId.UiClick, cam.transform.position, 0.8f);
    }

    [Rpc(SendTo.Everyone)]
    private void ClearBlindedRpc() => blindedCameras.Clear();

    private float nextSirenTime;

    public bool AlarmActive => alarmActive.Value;
    public float AlarmSeconds => alarmSeconds;
    public Vector3 AlarmPosition { get; private set; }

    private double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    // ---------------- Sunucu ----------------

    /// <summary>Sunucu: alarmı başlatır (zaten çalıyorsa süresini uzatır).</summary>
    public void ServerTriggerAlarm(Vector3 position, string reasonKey, PlayerStealth culprit)
    {
        if (!IsServer) return;

        AlarmPosition = position;
        bool wasActive = alarmActive.Value;
        alarmActive.Value = true;
        alarmEndsAt.Value = ServerNow + alarmSeconds;

        if (culprit != null) culprit.ServerMarkWanted(Time.time + alarmSeconds);

        if (!wasActive)
        {
            string name = "";
            if (culprit != null)
            {
                PlayerNetworkSetup ps = culprit.GetComponent<PlayerNetworkSetup>();
                name = ps != null ? ps.PlayerName : "";
            }
            if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage(reasonKey, name, "");
        }

        foreach (GuardAI g in Guards)
        {
            if (g != null) g.ServerOnAlarm(position, culprit);
        }
    }

    /// <summary>Sunucu: alarmı hemen susturur (ör. aranan kişi hapse girdi). Güvenlikler devriyeye döner.</summary>
    public void ServerStopAlarm(string reasonKey = "alarm.ended")
    {
        if (!IsServer || !alarmActive.Value) return;
        alarmActive.Value = false;
        foreach (GuardAI g in Guards)
        {
            if (g != null) g.ServerOnAlarmEnded();
        }
        if (ChatManager.Instance != null && !string.IsNullOrEmpty(reasonKey)) ChatManager.Instance.ServerSystemMessage(reasonKey, "", "");
    }

    /// <summary>Sunucu: biri hapisten kaçırılmaya çalışılıyor → bütün güvenlikler hapse koşar.</summary>
    public void ServerReportJailbreakAttempt(Vector3 jailPos, PlayerStealth rescuer)
    {
        if (!IsServer) return;
        foreach (GuardAI g in Guards)
        {
            if (g != null) g.ServerRespondToJailbreak(jailPos, rescuer);
        }
    }

    /// <summary>Sunucu: kamera şüpheli bir şey gördü → en yakın güvenlik araştırmaya gider.</summary>
    public void ServerReportSighting(Vector3 position)
    {
        if (!IsServer || alarmActive.Value) return;

        GuardAI best = null;
        float bestDist = float.MaxValue;
        foreach (GuardAI g in Guards)
        {
            if (g == null || !g.Online) continue;
            float d = Vector3.Distance(g.transform.position, position);
            if (d < bestDist) { bestDist = d; best = g; }
        }
        if (best != null) best.ServerInvestigate(position, false);
    }

    /// <summary>Sunucu: yeni göreve geçerken her şey sıfırlanır: alarm sessizce susar, kameralar açılır, kimse aranmaz,
    /// güvenlikler devriyeye döner.</summary>
    public void ServerResetForMission()
    {
        if (!IsServer) return;
        alarmActive.Value = false;
        alarmEndsAt.Value = 0;
        camerasOffUntil.Value = 0;
        consoleReadyAt.Value = 0;
        ClearBlindedRpc(); // yeni müzede kameraların lensleri temiz
        foreach (PlayerStealth p in PlayerStealth.All) if (p != null && p.IsSpawned) p.ServerClearHeat();
        foreach (GuardAI g in Guards) if (g != null) g.ServerResetForLayout();
    }

    public override void OnNetworkDespawn()
    {
        nextSirenTime = 0f; // bağlantı kopunca siren susar (Update de IsSpawned bakar)
    }

    private void Update()
    {
        if (IsSpawned && alarmActive.Value)
        {
            // Siren (herkeste)
            if (Time.unscaledTime >= nextSirenTime)
            {
                nextSirenTime = Time.unscaledTime + 1.4f;
                GameAudio.Play2D(SoundId.Alarm, 0.35f, 0f);
            }

            if (IsServer && ServerNow >= alarmEndsAt.Value) ServerStopAlarm();
        }
    }

    // ---------------- Arayüz ----------------

    private void OnGUI()
    {
        if (CamerasDisabled)
        {
            Rect cr = new Rect((Screen.width - 300f) / 2f, UIUtil.TopSlot(28f), 300f, 28f);
            UIUtil.DrawRect(cr, new Color(0.1f, 0.3f, 0.6f, 0.75f));
            UIUtil.DrawPrompt(Loc.T("console.banner", Mathf.CeilToInt(CamerasOffRemaining)), cr.y + 1f, Color.white, 17);
        }
        if (!alarmActive.Value || !IsSpawned) return;

        double remaining = alarmEndsAt.Value - NetworkManager.ServerTime.Time;
        if (remaining < 0) remaining = 0;

        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
        Rect r = new Rect((Screen.width - 340f) / 2f, UIUtil.TopSlot(34f), 340f, 34f);
        UIUtil.DrawRect(r, new Color(0.8f, 0.1f, 0.1f, 0.75f * pulse));
        UIUtil.DrawPrompt(Loc.T("alarm.banner", Mathf.CeilToInt((float)remaining)), r.y + 2f, Color.white, 20);
    }
}
