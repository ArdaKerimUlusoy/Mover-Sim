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

        float sc = UITheme.Scale;
        bool solo = GameFlow.Solo;
        var players = PlayerNetworkSetup.All;

        float w = Mathf.Min(Screen.width - 24f, 340f * sc);
        float padX = UITheme.S4 * sc, padY = UITheme.S3 * sc;
        float innerW = w - padX * 2f - 3f;
        float rowH = Mathf.Round(28f * sc);

        // Yerleşim yüksekliği: başlık + oyuncu satırları + bilgi (sarılmış) + yapılacak satırı
        string info = Loc.T(solo ? "lobby.info.solo" : "lobby.info.team");
        int readyPre = 0;
        if (!solo) foreach (PlayerNetworkSetup q in players) if (q != null && ready.Contains(q.OwnerClientId)) readyPre++;
        string last;
        Color lastCol = UITheme.BrassLight;
        if (solo) last = Loc.T("lobby.todo.solo");
        else if (players.Count > 0 && readyPre >= players.Count) { last = Loc.T("lobby.allready"); lastCol = UITheme.Sage; }
        else last = Loc.T("lobby.todo.ready", ReadyKey.ToString());
        float lastH = last.IndexOf('[') >= 0 ? 26f * sc : Mathf.Max(26f * sc, Mathf.Ceil(UIUtil.MeasureHeight(last, TextRole.Body, innerW)));
        float infoH = Mathf.Ceil(UIUtil.MeasureHeight(info, TextRole.Caption, innerW));
        int shown = solo ? 0 : players.Count;
        float h = padY + 26f * sc + shown * rowH + (shown > 0 ? UITheme.S2 * sc : 0f) + infoH + UITheme.S2 * sc + lastH + padY;

        // Sol üst köşe: oda kodu kutusunun altına dizilir (görev paneliyle çakışmaz)
        Rect r = UIUtil.HudRect(UIUtil.HudCorner.TopLeft, UIUtil.HudOrder.Lobby, w, h);
        UIUtil.Panel(r, PanelKind.Plaque, 0.94f);

        float x = r.x + padX + 3f;
        float y = r.y + padY;
        y += UIUtil.SectionHeader(new Rect(x, y, innerW, 20f * sc), Loc.T("lobby.title"));

        if (!solo)
        {
            foreach (PlayerNetworkSetup p in players)
            {
                if (p == null) continue;
                bool isReady = ready.Contains(p.OwnerClientId);

                Rect row = new Rect(x, y, innerW, rowH);
                if (p.IsOwner) UIUtil.DrawRect(row, UITheme.WithAlpha(UITheme.Brass, 0.12f));

                // Oyuncu rengi: dolu kare + ince fildişi hat
                float sw = 14f * sc;
                Rect swatch = new Rect(row.x + 4f, row.y + (rowH - sw) * 0.5f, sw, sw);
                UIUtil.DrawRect(swatch, p.PlayerColor);
                UIUtil.Outline(swatch, 1f, UITheme.WithAlpha(UITheme.Ivory, 0.7f));

                // Sağdan sola: durum rozeti, konum; kalan genişlik ad
                string badgeText = Loc.T(isReady ? "lobby.ready" : "lobby.notready");
                float badgeW = UIUtil.Measure(badgeText, TextRole.Caption).x + 24f;
                float badgeX = row.xMax - badgeW - 2f;
                UIUtil.Badge(new Vector2(badgeX, row.y + (rowH - 20f * sc) * 0.5f), badgeText,
                    isReady ? UITheme.Sage : UITheme.Graphite, isReady ? UITheme.Charcoal : UITheme.IvoryDim);

                string place = Loc.T(PlaceKey(GameFlow.PhaseAt(p.transform.position)));
                float placeW = Mathf.Min(UIUtil.Measure(place, TextRole.Caption).x + 4f, innerW * 0.28f);
                Rect placeR = new Rect(badgeX - placeW - 8f, row.y, placeW, rowH);
                UIUtil.Text(placeR, place, TextRole.Caption, UITheme.IvoryDim, TextAnchor.MiddleRight);

                string name = p.PlayerName + (p.IsOwner ? " " + Loc.T("pause.tag.you") : "");
                float nameX = swatch.xMax + 8f;
                UIUtil.Text(new Rect(nameX, row.y, placeR.x - nameX - 4f, rowH), name, TextRole.BodyStrong, UITheme.Ivory);

                y += rowH;
            }
            y += UITheme.S2 * sc;
        }

        // Rol bilgisi
        UIUtil.Text(new Rect(x, y, innerW, infoH), info, TextRole.Caption, UITheme.IvoryDim, TextAnchor.UpperLeft);
        y += infoH + UITheme.S2 * sc;

        // Yapılacak: tuş kapaklı ipucu; herkes hazırsa Sage
        UIUtil.Divider(x, y - 4f * sc, innerW, UITheme.WithAlpha(UITheme.Brass, 0.35f));
        if (last.IndexOf('[') >= 0) DrawHintLine(new Rect(x, y, innerW, 24f * sc), last, lastCol);
        else UIUtil.Text(new Rect(x, y, innerW, lastH), last, TextRole.Body, lastCol, TextAnchor.UpperLeft);
    }

    // "Hazır durumu için [F3] bas" → [..] parçaları tuş kapağı, kalanı BodyStrong/Caption yazı
    private static void DrawHintLine(Rect r, string text, Color textColor)
    {
        float x = r.x;
        int i = 0;
        while (i < text.Length && x < r.xMax)
        {
            int open = text.IndexOf('[', i);
            int close = open >= 0 ? text.IndexOf(']', open) : -1;
            string plain = open >= 0 && close > open ? text.Substring(i, open - i) : text.Substring(i);
            if (plain.Length > 0)
            {
                float pw = Mathf.Min(UIUtil.Measure(plain, TextRole.BodyStrong).x + 2f, r.xMax - x);
                UIUtil.Text(new Rect(x, r.y, pw, r.height), plain, TextRole.BodyStrong, textColor);
                x += pw;
            }
            if (open < 0 || close < open) break;
            string key = text.Substring(open + 1, close - open - 1);
            float kw = UIUtil.KeyCapWidth(key, 14);
            UIUtil.KeyCap(new Rect(x + 2f, r.y + 2f, kw, r.height - 4f), key, 14);
            x += kw + 6f;
            i = close + 1;
        }
    }
}
