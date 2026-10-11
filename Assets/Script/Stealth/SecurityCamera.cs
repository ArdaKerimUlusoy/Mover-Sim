using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dönen güvenlik kamerası (GDD Bölüm 5). Sağa sola tarar; görüş alanı yerde koni olarak görünür.
/// Şüpheli davranan oyuncuyu görünce şüphesi dolar ve en yakın güvenliği oraya yollar.
/// Dönüş açısı sunucu saatinden hesaplandığı için ağdan ekstra veri gönderilmez.
/// Kurulum: kamera objesine NetworkObject + bu bileşen; altına VisionConeVisual'lı bir çocuk.
/// </summary>
public class SecurityCamera : NetworkBehaviour
{
    [SerializeField] private Transform head;
    [SerializeField] private float sweepAngle = 70f;
    [SerializeField] private float sweepSpeed = 0.5f;
    [SerializeField] private float viewRange = 9f;
    [SerializeField] private float viewAngle = 50f;
    [SerializeField] private float suspicionRate = 0.7f;
    [SerializeField] private float reportCooldown = 4f;
    [SerializeField] private VisionConeVisual cone;

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
