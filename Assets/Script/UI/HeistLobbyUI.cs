using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GARAJ / LOBİ: oyuncuların buluştuğu ve soyguna hazırlandığı aşama. Garajdayken (kamyon ve müze dışında) sol tarafta
/// "SOYGUN HAZIRLIĞI" paneli çıkar: oyuncular (renk, ad, nerede), HAZIR durumu (F3), rol bilgisi ve yapılacaklar.
/// Hazır durumu yalnızca bilgidir (oyunu kilitlemez): ekip kimin hazır olduğunu görür.
/// Ağ: CustomMessagingManager named message ("SS_ReadyReq" istemci -> sunucu, "SS_ReadyState" sunucu -> herkes);
/// çalışma anında NetworkBehaviour eklenemediği için böyle. Kendini kurar, sahneye bir şey eklemek gerekmez.
/// </summary>
public class HeistLobbyUI : MonoBehaviour
{
    public const Key ReadyKey = Key.F3;
    private const string ReqMsg = "SS_ReadyReq", StateMsg = "SS_ReadyState";

    private static HeistLobbyUI instance;

    /// <summary>Hazır olan istemciler (sunucu ve istemcilerde aynı kümeye yakınsar).</summary>
    private static readonly HashSet<ulong> ready = new HashSet<ulong>();

    private NetworkManager regNm;
    private CustomMessagingManager regCmm;
    private bool stateDirty;
    private float nextBroadcast;
    private int serverLevel = -1;

    private GUIStyle title, row, small, hint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("HeistLobbyUI");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HeistLobbyUI>();
    }

    /// <summary>Bu istemci hazır mı?</summary>
    public static bool IsReady(ulong clientId) => ready.Contains(clientId);

    private static bool PanelVisible =>
        GameFlow.LocalSpawned && GameFlow.Phase == GamePhase.Garage && !SessionMenuUI.IsPaused &&
        !WorkbenchUI.IsOpen && !BriefingUI.IsOpen && !FloorPlanUI.IsOpen;

    private void OnDisable() => Unregister();

    // ---------------- Ağ ----------------

    private void Register(NetworkManager nm)
    {
        Unregister();
        regNm = nm;
        regCmm = nm.CustomMessagingManager;
        if (regCmm == null) { regNm = null; return; }
        regCmm.RegisterNamedMessageHandler(ReqMsg, OnRequest);
        regCmm.RegisterNamedMessageHandler(StateMsg, OnState);
        ready.Clear();
        stateDirty = true;
    }

    private void Unregister()
    {
        if (regCmm != null)
        {
            try
            {
                regCmm.UnregisterNamedMessageHandler(ReqMsg);
                regCmm.UnregisterNamedMessageHandler(StateMsg);
            }
            catch (Exception) { }
        }
        regCmm = null;
        regNm = null;
    }

    private void SendReady(bool on)
    {
        NetworkManager nm = regNm;
        if (nm == null || !nm.IsListening) return;
        if (nm.IsServer) { ServerSet(nm.LocalClientId, on); return; }

        if (on) ready.Add(nm.LocalClientId); else ready.Remove(nm.LocalClientId); // kendi ekranında hemen görünsün
        using (var w = new FastBufferWriter(4, Allocator.Temp))
        {
            w.WriteValueSafe((byte)(on ? 1 : 0));
            regCmm.SendNamedMessage(ReqMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    private void OnRequest(ulong sender, FastBufferReader r)
    {
        if (regNm == null || !regNm.IsServer) return;
        try { r.ReadValueSafe(out byte on); ServerSet(sender, on == 1); } catch (Exception) { }
    }

    private void OnState(ulong sender, FastBufferReader r)
    {
        if (regNm == null || regNm.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            r.ReadValueSafe(out ushort n);
            ready.Clear();
            for (int i = 0; i < n; i++) { r.ReadValueSafe(out ulong id); ready.Add(id); }
        }
        catch (Exception) { }
    }

    private void ServerSet(ulong client, bool on)
    {
        bool changed = on ? ready.Add(client) : ready.Remove(client);
        if (changed) stateDirty = true;
    }

    private void ServerTick(NetworkManager nm)
    {
        // Ayrılan oyuncular listeden çıkar; yeni görevde (seviye değişince) hazır durumları sıfırlanır
        ready.RemoveWhere(id => !nm.ConnectedClients.ContainsKey(id));
        MuseumSession ms = MuseumSession.Instance;
        if (ms != null && ms.Ready && ms.Level != serverLevel)
        {
            if (serverLevel != -1) ready.Clear();
            serverLevel = ms.Level;
            stateDirty = true;
        }

        if (!stateDirty && Time.unscaledTime < nextBroadcast) return;
        stateDirty = false;
        nextBroadcast = Time.unscaledTime + 3f;

        List<ulong> ids = null;
        foreach (ulong id in nm.ConnectedClientsIds)
        {
            if (id == nm.LocalClientId) continue;
            if (ids == null) ids = new List<ulong>();
            ids.Add(id);
        }
        if (ids == null) return;

        using (var w = new FastBufferWriter(256, Allocator.Temp))
        {
            w.WriteValueSafe((ushort)Mathf.Min(ready.Count, 24));
            int written = 0;
            foreach (ulong id in ready)
            {
                if (written++ >= 24) break;
                w.WriteValueSafe(id);
            }
            regCmm.SendNamedMessage(StateMsg, ids, w, NetworkDelivery.Reliable);
        }
    }

    // ---------------- Güncelleme ----------------

    private void Update()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            if (regNm != null) { Unregister(); ready.Clear(); }
            return;
        }
        if (regNm != nm || regCmm != nm.CustomMessagingManager) Register(nm);
        if (regNm == null) return;

        if (nm.IsServer) ServerTick(nm);

        Keyboard kb = Keyboard.current;
        if (kb == null || !PanelVisible || ChatUI.IsTyping || PlayerInventory.IsPanelOpen || GameFlow.Solo) return;
        if (Cursor.lockState == CursorLockMode.Locked && kb[ReadyKey].wasPressedThisFrame)
            SendReady(!ready.Contains(nm.LocalClientId));
    }

    // ---------------- Çizim ----------------

    private void EnsureStyles()
    {
        if (title != null) return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        title.normal.textColor = UIUtil.Gold;
        row = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        row.normal.textColor = UIUtil.Cream;
        small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        small.normal.textColor = UIUtil.Muted;
        hint = new GUIStyle(small) { fontStyle = FontStyle.Bold };
        hint.normal.textColor = new Color(0.6f, 0.95f, 0.65f);
    }

    private static string PlaceKey(GamePhase p)
    {
        switch (p)
        {
            case GamePhase.Truck: return "lobby.place.truck";
            case GamePhase.Museum: return "lobby.place.museum";
            default: return "lobby.place.garage";
        }
    }

    private void OnGUI()
    {
        if (!PanelVisible) return;
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        EnsureStyles();

        bool solo = GameFlow.Solo;
        var players = PlayerNetworkSetup.All;
        float w = 300f;
        float h = 34f + (solo ? 0f : players.Count * 24f + 8f) + 118f;
        // Sol üst köşe: oda kodu kutusunun altına dizilir (görev paneliyle çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.Lobby, w, h);
        UIUtil.Panel(r, UIUtil.Gold);
        r.x += 6f; // sol şeridin sağından başla

        float y = r.y + 6f;
        GUI.Label(new Rect(r.x + 10f, y, w - 20f, 22f), Loc.T("lobby.title"), title);
        y += 26f;

        int readyCount = 0;
        if (!solo)
        {
            foreach (PlayerNetworkSetup p in players)
            {
                if (p == null) continue;
                bool isReady = ready.Contains(p.OwnerClientId);
                if (isReady) readyCount++;
                UIUtil.DrawRect(new Rect(r.x + 10f, y + 5f, 14f, 14f), p.PlayerColor);

                string place = Loc.T(PlaceKey(GameFlow.PhaseAt(p.transform.position)));
                string you = p.IsOwner ? " " + Loc.T("pause.tag.you") : "";
                GUI.Label(new Rect(r.x + 30f, y, 150f, 24f), p.PlayerName + you, row);
                GUI.Label(new Rect(r.x + 150f, y + 2f, 90f, 22f), place, small);
                hint.normal.textColor = isReady ? new Color(0.6f, 0.95f, 0.65f) : new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(new Rect(r.x + 236f, y, 50f, 24f), Loc.T(isReady ? "lobby.ready" : "lobby.notready"), hint);
                y += 24f;
            }
            y += 8f;
        }

        // Rol bilgisi + yapılacaklar
        GUI.Label(new Rect(r.x + 10f, y, w - 20f, 60f), Loc.T(solo ? "lobby.info.solo" : "lobby.info.team"), small);
        y += 62f;

        hint.normal.textColor = new Color(0.6f, 0.95f, 0.65f);
        string last;
        if (solo) last = Loc.T("lobby.todo.solo");
        else if (players.Count > 0 && readyCount >= players.Count) last = Loc.T("lobby.allready");
        else last = Loc.T("lobby.todo.ready", ReadyKey.ToString());
        GUI.Label(new Rect(r.x + 10f, y, w - 20f, 36f), last, hint);
    }
}
