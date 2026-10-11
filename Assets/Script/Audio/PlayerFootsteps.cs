using UnityEngine;

/// <summary>
/// Ayak sesleri. Her oyuncu objesinde çalışır (kendi ve diğer oyuncular);
/// hızı pozisyon değişiminden hesapladığı için ağdan ekstra veri gerekmez.
/// PlayerNetworkSetup tarafından otomatik eklenir.
/// </summary>
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private float strideLength = 1.7f;
    [SerializeField] private float minSpeed = 0.6f;
    [SerializeField] private float quietVolume = 0.25f;
    [SerializeField] private float loudVolume = 0.8f;
    [SerializeField] private float runSpeed = 7f;

    private PlayerInventory inventory;
    private Vector3 lastPos;
    private float distanceSinceStep;
    private bool initialized;

    private void OnEnable()
    {
        initialized = false;
        inventory = GetComponent<PlayerInventory>();
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

        distanceSinceStep += horizontal.magnitude;
        if (distanceSinceStep >= strideLength)
        {
            distanceSinceStep = 0f;
            float volume = Mathf.Lerp(quietVolume, loudVolume, Mathf.InverseLerp(minSpeed, runSpeed, speed));
            if (inventory != null && inventory.IsSpawned) volume *= inventory.FootstepVolumeMultiplier; // sessiz ayakkabı
            GameAudio.PlayAt(SoundId.Footstep, pos + Vector3.down * 0.9f, volume, 0.1f);
            // İleride: güvenlikler için NoiseSystem.Emit(pos, yarıçap) buradan (sunucuda) çağrılacak.
        }
    }
}
