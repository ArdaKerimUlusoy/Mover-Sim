using UnityEngine;

/// <summary>
/// Atmosfer sesleri: müzede alçak havalandırma uğultusu + ara sıra uzaktan yankılı adımlar,
/// alarmda gerilim döngüsü, kamyon/garajda farklı hafif ortam.
/// Sahneye eklemek gerekmez, oyun başlarken kendini kurar. Sesler SoundSynth ile bir kez üretilir.
/// Ses düzeyi GameSettings.MasterVolume ile zaten AudioListener.volume üzerinden uygulanır;
/// burada ayrıca çarpılmaz (çift düşmesin).
/// </summary>
public class Ambience : MonoBehaviour
{
    private const float MuseumVol = 0.22f;
    private const float TensionVol = 0.3f;
    private const float TruckVol = 0.2f;
    private const float FadeSpeed = 0.6f; // saniyede ses değişimi

    private static Ambience instance;

    private AudioSource museum, tension, truck, far;
    private float museumLevel, tensionLevel, truckLevel;
    private float nextFarStep;
    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("[Ambience]");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Ambience>();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private AudioSource MakeLoopSource(string n, SoundSynth.AmbienceKind kind)
    {
        GameObject go = new GameObject(n);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = SoundSynth.CreateAmbience(kind);
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.volume = 0f;
        s.dopplerLevel = 0f;
        return s;
    }

    /// <summary>Üretim (yaklaşık yüzlerce ms) yalnızca oyuna girilince, bir kez yapılır.</summary>
    private void Build()
    {
        built = true;
        museum = MakeLoopSource("Museum", SoundSynth.AmbienceKind.MuseumHum);
        tension = MakeLoopSource("Tension", SoundSynth.AmbienceKind.Tension);
        truck = MakeLoopSource("Truck", SoundSynth.AmbienceKind.Truck);

        GameObject fg = new GameObject("FarSteps");
        fg.transform.SetParent(transform, false);
        far = fg.AddComponent<AudioSource>();
        far.clip = SoundSynth.CreateFarSteps();
        far.playOnAwake = false;
        far.spatialBlend = 0f;
        far.dopplerLevel = 0f;
        nextFarStep = Time.time + Random.Range(8f, 16f);
    }

    private void Update()
    {
        PlayerStealth me = PlayerStealth.Local;
        bool inGame = me != null && me.IsSpawned;

        if (!built)
        {
            if (!inGame) return;
            Build();
        }

        bool inTruck = inGame && FloorPlanUI.LocalInTruck;
        bool alarm = inGame && SecurityManager.Instance != null && SecurityManager.Instance.AlarmActive;

        float museumTarget = inGame && !inTruck ? 1f : 0f;
        float truckTarget = inTruck ? 1f : 0f;
        float tensionTarget = alarm ? (inTruck ? 0.45f : 1f) : 0f;
        // Alarmda uğultu biraz kısılsın ki gerilim öne çıksın
        if (alarm) museumTarget *= 0.55f;

        float step = FadeSpeed * Time.unscaledDeltaTime;
        museumLevel = Mathf.MoveTowards(museumLevel, museumTarget, step);
        truckLevel = Mathf.MoveTowards(truckLevel, truckTarget, step);
        tensionLevel = Mathf.MoveTowards(tensionLevel, tensionTarget, step * 1.5f);

        Apply(museum, museumLevel * MuseumVol);
        Apply(tension, tensionLevel * TensionVol);
        Apply(truck, truckLevel * TruckVol);

        // Müzede ara sıra uzaktan yankılı adımlar (alarmda çalmaz)
        if (museumLevel > 0.9f && !alarm && Time.time >= nextFarStep)
        {
            nextFarStep = Time.time + Random.Range(12f, 28f);
            far.panStereo = Random.Range(-0.8f, 0.8f);
            far.volume = Random.Range(0.35f, 0.6f);
            far.pitch = Random.Range(0.92f, 1.05f);
            far.Play();
        }
    }

    private static void Apply(AudioSource s, float volume)
    {
        if (s == null) return;
        s.volume = volume;
        if (volume > 0.001f)
        {
            if (!s.isPlaying) s.Play();
        }
        else if (s.isPlaying)
        {
            s.Stop();
        }
    }
}
