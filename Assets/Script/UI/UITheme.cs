using UnityEngine;

/// <summary>Arayüz temasını, başka hiçbir OnGUI stil kopyalamadan önce uygular. Kendini kurar.</summary>
[DefaultExecutionOrder(-10000)]
public class UITheme : MonoBehaviour
{
    private static UITheme instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("UITheme");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UITheme>();
    }

    private void OnGUI()
    {
        UIUtil.ApplySkin();
        enabled = false; // bir kez yeter (deri kalıcı); her karede OnGUI maliyeti olmasın
    }
}
