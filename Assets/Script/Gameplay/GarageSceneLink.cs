using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Garaj AYRI bir Unity sahnesinde ("Garage"). Oda açılınca sunucu bu sahneyi ağ üzerinden EK (additive) olarak yükler;
/// istemciler (sonradan katılanlar dahil) NGO sahne eşitlemesiyle otomatik yükler. Yüklenince herkes garaja gelir.
/// Oturum kapanınca sahne boşaltılır (bir sonraki odada temiz yüklensin).
/// Sahne Build Settings'te yoksa hiçbir şey yapmaz: eski düzen (garaj ana sahnede) aynen çalışır.
/// Kendini kurar; sahneye bir şey eklemek gerekmez.
/// </summary>
public class GarageSceneLink : MonoBehaviour
{
    public const string SceneName = "Garage";
    public const string ScenePath = "Assets/Scenes/Garage.unity";

    private static GarageSceneLink instance;

    private NetworkManager hookedNm;
    private bool requested;
    private float nextTry;
    private bool wasListening;

    /// <summary>Garaj sahnesi oyunun build listesinde mi (araç çalıştırılmış mı)?</summary>
    public static bool Available => SceneUtility.GetBuildIndexByScenePath(ScenePath) >= 0;

    public static bool Loaded => SceneManager.GetSceneByName(SceneName).isLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("GarageSceneLink");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GarageSceneLink>();
    }

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        bool listening = nm != null && nm.IsListening;

        if (!listening)
        {
            if (wasListening) OnSessionEnded();
            wasListening = false;
            return;
        }
        wasListening = true;

        if (!nm.IsServer || requested || Time.unscaledTime < nextTry) return;
        if (!Available || nm.SceneManager == null) { requested = true; return; }
        if (Loaded) { requested = true; return; }

        Hook(nm);
        SceneEventProgressStatus st = nm.SceneManager.LoadScene(SceneName, LoadSceneMode.Additive);
        if (st == SceneEventProgressStatus.Started) requested = true;
        else nextTry = Time.unscaledTime + 1f; // başka sahne olayı sürüyorsa biraz sonra tekrar dene
    }

    private void Hook(NetworkManager nm)
    {
        if (hookedNm == nm) return;
        Unhook();
        hookedNm = nm;
        nm.SceneManager.OnLoadEventCompleted += OnLoadCompleted;
    }

    private void Unhook()
    {
        if (hookedNm != null && hookedNm.SceneManager != null) hookedNm.SceneManager.OnLoadEventCompleted -= OnLoadCompleted;
        hookedNm = null;
    }

    private void OnDestroy() => Unhook();

    private void OnLoadCompleted(string sceneName, LoadSceneMode mode, System.Collections.Generic.List<ulong> done, System.Collections.Generic.List<ulong> timedOut)
    {
        if (sceneName != SceneName) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        ServerSendEveryoneToGarage();
    }

    /// <summary>Sunucu: herkesi garajdaki doğma noktalarına ışınlar. Garaj yoksa false (çağıran kamyona gönderir).</summary>
    public static bool ServerSendEveryoneToGarage()
    {
        if (GarageSpawnPoint.All.Count == 0) return false;
        foreach (PlayerStealth p in PlayerStealth.All)
        {
            if (p == null || !p.IsSpawned) continue;
            GarageSpawnPoint gp = GarageSpawnPoint.For(p.OwnerClientId);
            if (gp != null) p.ServerTeleport(gp.transform.position + Vector3.up, gp.transform.eulerAngles.y);
        }
        return true;
    }

    private void OnSessionEnded()
    {
        Unhook();
        requested = false;
        nextTry = 0f;
        Scene s = SceneManager.GetSceneByName(SceneName);
        if (s.isLoaded && SceneManager.sceneCount > 1) SceneManager.UnloadSceneAsync(s);
    }
}
