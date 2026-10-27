using UnityEngine;

/// <summary>
/// Fırlatılan bozuk paraya (sunucuda) eklenir: uçarken bir güvenliğin kafasına / gövdesine değerse
/// güvenlik PlayerStealth.CoinDazeSeconds kadar olduğu yerde sersemler (GuardAI.ServerDaze). Bir kez çalışır,
/// para yere düşünce / 2.5 sn sonra kendini kaldırır. Yalnızca sunucuda ve uçan paralarda iş yapar (ucuz).
/// </summary>
public class CoinDazer : MonoBehaviour
{
    private const float ArmSeconds = 2.5f;
    private const float MinSpeed = 2.2f;     // yerde yuvarlanan / duran para sersemletmez
    private const float HitRadius = 0.5f;    // gövde ekseninden uzaklık

    private Rigidbody rb;
    private float until;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        until = Time.time + ArmSeconds;
    }

    private void FixedUpdate()
    {
        if (Time.time > until) { Destroy(this); return; }
        if (rb != null && rb.linearVelocity.sqrMagnitude < MinSpeed * MinSpeed) return;

        Vector3 p = transform.position;
        foreach (GuardAI g in SecurityManager.Guards)
        {
            if (g == null || !g.Online) continue;
            Vector3 b = g.transform.position;
            float y = Mathf.Clamp(p.y, b.y + 0.4f, b.y + 1.9f);           // kafa ile bel arası eksen
            Vector3 axis = new Vector3(b.x, y, b.z);
            if ((p - axis).sqrMagnitude > HitRadius * HitRadius) continue;
            Hit(g);
            return;
        }
    }

    private void OnCollisionEnter(Collision c)
    {
        GuardAI g = c.collider != null ? c.collider.GetComponentInParent<GuardAI>() : null;
        if (g != null && g.Online) { Hit(g); return; }
        // İlk çarpmadan sonra (duvar / yer) artık sersemletmez; ses / dikkat dağıtma WorldItem'da
        if (Time.time > until - ArmSeconds + 0.1f) Destroy(this);
    }

    private void Hit(GuardAI g)
    {
        g.ServerDaze(PlayerStealth.CoinDazeSeconds);
        if (rb != null) rb.linearVelocity = rb.linearVelocity * -0.2f; // kafadan sekip düşer
        Destroy(this);
    }
}
