using UnityEngine;

/// <summary>
/// Ayak sesleri. Her oyuncu objesinde çalışır (kendi ve diğer oyuncular);
/// hızı pozisyon değişiminden hesapladığı için ağdan ekstra veri gerekmez.
/// Yüzeye (sert / halı / metal-kamyon) ve çömelmeye göre ses, ses seviyesi ve adım aralığı değişir.
/// PlayerNetworkSetup tarafından otomatik eklenir.
/// </summary>
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private float strideLength = 1.7f;
    [SerializeField] private float minSpeed = 0.6f;
    [SerializeField] private float quietVolume = 0.25f;
    [SerializeField] private float loudVolume = 0.8f;
    [SerializeField] private float runSpeed = 7f;

    private enum Surface { Hard, Soft, Metal }

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    private PlayerInventory inventory;
    private PlayerStealth stealth;
    private PlayerNetworkSetup setup;
    private Vector3 lastPos;
    private float distanceSinceStep;
    private bool initialized;
    private bool leftFoot;

    private void OnEnable()
    {
        initialized = false;
        inventory = GetComponent<PlayerInventory>();
        stealth = GetComponent<PlayerStealth>();
        setup = GetComponent<PlayerNetworkSetup>();
    }

    private void Update()
    {
        Vector3 pos = transform.position;
        if (!initialized)
        {
            lastPos = pos;
            initialized = true;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = pos - lastPos;
        lastPos = pos;

        // Işınlanma vb. büyük sıçramaları sayma
        if (delta.sqrMagnitude > 4f) return;

        float vertical = Mathf.Abs(delta.y) / dt;
        Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
        float speed = horizontal.magnitude / dt;

        if (speed < minSpeed || vertical > 2.5f) // duruyor ya da havada
        {
            distanceSinceStep = Mathf.Min(distanceSinceStep, strideLength * 0.6f);
            return;
        }

        bool crouch = stealth != null && stealth.IsSpawned && stealth.IsCrouching;
        if (stealth != null && stealth.IsSpawned && stealth.IsHidden) return; // dolapta ses çıkmaz

        // Çömelirken adımlar daha kısa ve sık değil, daha seyrek-yumuşak olsun
        float stride = crouch ? strideLength * 0.8f : strideLength;

        distanceSinceStep += horizontal.magnitude;
        if (distanceSinceStep >= stride)
        {
            distanceSinceStep = 0f;
            leftFoot = !leftFoot;

            float t = Mathf.InverseLerp(minSpeed, runSpeed, speed);
            float volume = Mathf.Lerp(quietVolume, loudVolume, t);
            float pitch = Mathf.Lerp(0.97f, 1.06f, t) + (leftFoot ? -0.025f : 0.025f); // sol/sağ ayak farkı
            if (crouch) { volume *= 0.4f; pitch *= 0.88f; }
            if (inventory != null && inventory.IsSpawned) volume *= inventory.FootstepVolumeMultiplier; // sessiz ayakkabı

            Surface surface = DetectSurface(pos);
            SoundId id = surface == Surface.Soft ? SoundId.FootstepSoft : surface == Surface.Metal ? SoundId.FootstepMetal : SoundId.Footstep;
            if (surface == Surface.Soft) volume *= 0.9f;

            GameAudio.PlayAt(id, pos + Vector3.down * 0.9f, volume, 0.04f, pitch);
            // İleride: güvenlikler için NoiseSystem.Emit(pos, yarıçap) buradan (sunucuda) çağrılacak.
        }
    }

    /// <summary>Ayağın altındaki zemini malzeme/obje adından tahmin eder (adım başına bir kez).</summary>
    private Surface DetectSurface(Vector3 pos)
    {
        if (setup != null && TruckZone.IsInside(setup)) return Surface.Metal;

        int count = Physics.RaycastNonAlloc(pos, Vector3.down, hitBuffer, 2.2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Collider ground = null;
        for (int i = 0; i < count; i++)
        {
            Collider c = hitBuffer[i].collider;
            if (c == null || c.transform.IsChildOf(transform)) continue;
            if (hitBuffer[i].distance < best) { best = hitBuffer[i].distance; ground = c; }
        }
        if (ground == null) return Surface.Hard;

        string key = ground.name;
        var pm = ground.sharedMaterial;
        if (pm != null) key += " " + pm.name;
        Renderer rend = ground.GetComponent<Renderer>();
        if (rend != null && rend.sharedMaterial != null) key += " " + rend.sharedMaterial.name;
        key = key.ToLowerInvariant();

        if (key.Contains("carpet") || key.Contains("rug") || key.Contains("hali") || key.Contains("halı") || key.Contains("kilim") || key.Contains("grass") || key.Contains("cim") || key.Contains("çim"))
            return Surface.Soft;
        if (key.Contains("metal") || key.Contains("steel") || key.Contains("iron") || key.Contains("truck") || key.Contains("kamyon"))
            return Surface.Metal;
        return Surface.Hard;
    }
}
