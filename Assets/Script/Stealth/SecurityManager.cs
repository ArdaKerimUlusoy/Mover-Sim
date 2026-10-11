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

    [SerializeField] private float alarmDuration = 25f;

    private readonly NetworkVariable<bool> alarmActive = new NetworkVariable<bool>(false);
    private readonly NetworkVariable<double> alarmEndsAt = new NetworkVariable<double>(0);

    private float nextSirenTime;

    public bool AlarmActive => alarmActive.Value;
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
        alarmEndsAt.Value = ServerNow + alarmDuration;

        if (culprit != null) culprit.ServerMarkWanted(Time.time + alarmDuration);

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

    /// <summary>Sunucu: kamera şüpheli bir şey gördü → en yakın güvenlik araştırmaya gider.</summary>
    public void ServerReportSighting(Vector3 position)
    {
        if (!IsServer || alarmActive.Value) return;

        GuardAI best = null;
        float bestDist = float.MaxValue;
        foreach (GuardAI g in Guards)
        {
            if (g == null) continue;
            float d = Vector3.Distance(g.transform.position, position);
            if (d < bestDist) { bestDist = d; best = g; }
        }
        if (best != null) best.ServerInvestigate(position, false);
    }

    private void Update()
    {
        if (alarmActive.Value)
        {
            // Siren (herkeste)
            if (Time.unscaledTime >= nextSirenTime)
            {
                nextSirenTime = Time.unscaledTime + 1.4f;
                GameAudio.Play2D(SoundId.Alarm, 0.35f, 0f);
            }

            if (IsServer && ServerNow >= alarmEndsAt.Value)
            {
                alarmActive.Value = false;
                foreach (GuardAI g in Guards)
                {
                    if (g != null) g.ServerOnAlarmEnded();
                }
                if (ChatManager.Instance != null) ChatManager.Instance.ServerSystemMessage("alarm.ended", "", "");
            }
        }
    }

    // ---------------- Arayüz ----------------

    private void OnGUI()
    {
        if (!alarmActive.Value || !IsSpawned) return;

        double remaining = alarmEndsAt.Value - NetworkManager.ServerTime.Time;
        if (remaining < 0) remaining = 0;

        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
        Rect r = new Rect((Screen.width - 340f) / 2f, 60f, 340f, 34f);
        UIUtil.DrawRect(r, new Color(0.8f, 0.1f, 0.1f, 0.75f * pulse));
        UIUtil.DrawPrompt(Loc.T("alarm.banner", Mathf.CeilToInt((float)remaining)), r.y + 2f, Color.white, 20);
    }
}
