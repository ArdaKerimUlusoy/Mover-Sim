using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Garajdaki BRİFİNG TERMİNALİ (GDD: "Brifing — hedef eserleri gör").
/// Üstünde bu görevin hedef eserleri sırayla dönen hologram olarak görünür.
/// E → panel: müze, görev seviyesi, kota ve hedef eserlerin resimleri / değerleri / durumu.
/// Kurulum: obje + NetworkObject + bu bileşen.
/// </summary>
public class BriefingTerminal : Interactable
{
    [SerializeField] private float hologramHeight = 0.35f;

    private GameObject hologram;
    private int builtVersion = -1;
    private int holoIndex;
    private float holoTimer;
    private readonly List<ArtworkDisplay> holoTargets = new List<ArtworkDisplay>();

    public override bool HandleLocally => true;

    public override string GetPrompt(PlayerInteractor player) => Loc.T("brief.prompt");

    public override void ClientInteract(PlayerInteractor player) => BriefingUI.Open(player != null ? player.gameObject : null);

    protected override void ServerInteract(ulong clientId, NetworkObject player) { }

    private void Update()
    {
        MuseumSession ms = MuseumSession.Instance;
        if (ms == null || !ms.Ready) return;

        if (builtVersion != MuseumSession.LookVersion)
        {
            builtVersion = MuseumSession.LookVersion;
            holoTargets.Clear();
            foreach (ArtworkDisplay d in ArtworkDisplay.All)
                if (d != null && d.Artwork != null && MuseumSession.IsDisplayActive(d) && d.Kind != ArtKind.None)
                    holoTargets.Add(d);
            holoIndex = 0;
            BuildHologram();
        }

        if (hologram != null)
        {
            // Kesintisiz döner: her eser tam bir tur (360°) atar, sonra sıradaki esere geçilir
            holoTimer += HologramSpinSpeed * Time.deltaTime;
            if (holoTimer >= 360f)
            {
                holoTimer -= 360f;
                if (holoTargets.Count > 1)
                {
                    holoIndex = (holoIndex + 1) % holoTargets.Count;
                    BuildHologram();
                }
            }
            if (hologram != null) hologram.transform.rotation = Quaternion.Euler(0f, holoTimer, 0f);
        }
    }

    private const float HologramSpinSpeed = 72f; // derece / saniye: bir tur 5 saniye

    private void BuildHologram()
    {
        if (hologram != null) Destroy(hologram);
        if (holoTargets.Count == 0) return;
        MuseumSession ms = MuseumSession.Instance;
        ArtworkDisplay d = holoTargets[holoIndex];
        if (ms == null || d == null) return;
        hologram = ArtworkLook.BuildOriginalVisual(d.Kind, ms.Seed, ms.ThemeIndex, d.name);
        if (hologram == null) return;

        // Tablo dönerken arkası da resmi göstersin (tuvalin bir kopyası arka yüze)
        if (d.Kind == ArtKind.Painting)
        {
            Transform canvas = hologram.transform.Find("Canvas");
            if (canvas != null)
            {
                Transform back = Instantiate(canvas.gameObject, hologram.transform).transform;
                Vector3 p = canvas.localPosition;
                back.localPosition = new Vector3(p.x, p.y, -p.z);
                back.localRotation = Quaternion.Euler(0f, 180f, 0f);
                back.localScale = canvas.localScale;
            }
        }

        ItemVisuals.Fit(hologram, 0.5f);
        hologram.transform.rotation = Quaternion.Euler(0f, holoTimer, 0f);
        Collider c = GetComponentInChildren<Collider>();
        float top = c != null ? c.bounds.max.y : transform.position.y + 0.5f;
        hologram.transform.position = new Vector3(transform.position.x, top + hologramHeight * 0.2f, transform.position.z);
    }

    public override void OnDestroy()
    {
        if (hologram != null) Destroy(hologram);
        base.OnDestroy();
    }
}

/// <summary>Brifing paneli (yerel).</summary>
public class BriefingUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private PlayerController controller;
    private int openedFrame;
    private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private Vector2 scroll;

    public static void Open(GameObject player)
    {
        if (IsOpen || WorkbenchUI.IsOpen) return;
        var go = new GameObject("BriefingUI");
        var ui = go.AddComponent<BriefingUI>();
        ui.controller = player != null ? player.GetComponent<PlayerController>() : null;
        ui.openedFrame = Time.frameCount;
        if (ui.controller != null) ui.controller.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsOpen = true;
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || Time.frameCount == openedFrame) return;
        if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) Close();
    }

    private void Close()
    {
        if (controller != null) controller.enabled = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        IsOpen = false;
        Destroy(gameObject);
    }

    private void OnDestroy() { IsOpen = false; }

    private Texture2D IconFor(MuseumSession ms, ArtworkDisplay d, ArtKind kind)
    {
        string key = ms.Seed + "|" + ms.ThemeIndex + "|" + d.name;
        if (icons.TryGetValue(key, out Texture2D t)) return t;
        t = ArtworkLook.OriginalIcon(kind, ms.Seed, ms.ThemeIndex, d.name);
        if (t == null) t = d.Artwork.icon;
        icons[key] = t;
        return t;
    }

    private void OnGUI()
    {
        MuseumSession ms = MuseumSession.Instance;
        UIUtil.BeginCenteredPanel(720f, 560f);

        if (ms == null || !ms.Ready)
        {
            GUILayout.Label(Loc.T("brief.waiting"), UIUtil.TitleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.T("brief.close"), UIUtil.HintStyle);
            UIUtil.EndCenteredPanel();
            return;
        }

        MuseumTheme th = ms.Theme;
        GUILayout.Label(Loc.T("mission.title", ms.Level, th.name, th.City), UIUtil.TitleStyle);
        GUILayout.Label(Loc.T("brief.quota", ms.Quota, Mathf.Max(0, ms.Earned)), UIUtil.AccentStyle);
        GUILayout.Space(6f);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(300f));
        foreach (ArtworkDisplay d in ArtworkDisplay.All)
        {
            if (d == null || d.Artwork == null || !MuseumSession.IsDisplayActive(d)) continue;
            ArtKind kind = ArtworkLook.KindOf(d.Artwork);

            GUILayout.BeginHorizontal(UIUtil.BoxStyle);
            Rect img = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(kind == ArtKind.Painting ? 96f : 72f), GUILayout.Height(72f));
            Texture2D iconTex = kind != ArtKind.None ? IconFor(ms, d, kind) : d.Artwork.icon;
            if (iconTex != null) GUI.DrawTexture(img, iconTex, ScaleMode.ScaleToFit);

            GUILayout.BeginVertical();
            GUILayout.Label(ms.ArtworkName(d), UIUtil.RowStyle);
            string how = Loc.T(WorkbenchUI.MethodKey(kind));
            GUILayout.Label(Loc.T("brief.value", d.Value, d.Artwork.weightKg.ToString("0.#"), how), UIUtil.SmallStyle);
            if (d.IsSwapped) GUILayout.Label(Loc.T("brief.swapped", d.LeftSimilarity), UIUtil.SmallStyle);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        GUILayout.Label(Loc.T("brief.plan"), UIUtil.SmallStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(Loc.T("brief.close"), UIUtil.HintStyle);
        UIUtil.EndCenteredPanel();
    }
}
