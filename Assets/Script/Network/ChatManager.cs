using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Oyun içi sohbet + "X katıldı / X ayrıldı" mesajları.
/// Sahnede boş bir objeye ekle: NetworkObject + ChatManager + ChatUI.
///
/// Akış: Oyuncu mesaj yazar → sunucuya gider → sunucu gönderenin adını ekleyip herkese dağıtır.
/// Böylece kimse başkasının adıyla mesaj atamaz.
/// </summary>
public class ChatManager : NetworkBehaviour
{
    public const int MaxMessageLength = 120;
    public const int MaxStoredLines = 50;

    public static ChatManager Instance { get; private set; }

    public struct ChatLine
    {
        public string Sender;   // sistem mesajında boş
        public string Text;     // oyuncu mesajı ya da sistem mesajının çeviri anahtarı
        public string Arg0, Arg1; // sistem mesajı parametreleri (oyuncu adları)
        public bool IsSystem;

        /// <summary>Ekranda gösterilecek metin (sistem mesajları o anki dile çevrilir).</summary>
        public string Display => IsSystem ? Loc.T(Text, Arg0, Arg1) : Text;
        public float Time;
    }

    private readonly List<ChatLine> lines = new List<ChatLine>();
    public IReadOnlyList<ChatLine> Lines => lines;

    /// <summary>Yeni satır geldiğinde (UI için).</summary>
    public event Action LineAdded;

    // Sunucu tarafı: hangi client hangi isimde (ayrılınca adını yazabilmek için).
    private readonly Dictionary<ulong, string> serverNames = new Dictionary<ulong, string>();

    private void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        lines.Clear();
        serverNames.Clear();

        if (IsServer)
        {
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        serverNames.Clear();
        lines.Clear();
        LineAdded?.Invoke();
    }

    // ---------------- İstemci tarafı ----------------

    /// <summary>ChatUI çağırır.</summary>
    public void Send(string text)
    {
        if (!IsSpawned) return;
        text = Clean(text);
        if (text.Length == 0) return;
        SubmitRpc(text);
    }

    // ---------------- Sunucu tarafı ----------------

    [Rpc(SendTo.Server)]
    private void SubmitRpc(string text, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        text = Clean(text);
        if (text.Length == 0) return;

        ChatRpc(GetServerName(sender), text);
    }

    /// <summary>
    /// PlayerNetworkSetup, oyuncunun adı sunucuya ulaşınca çağırır.
    /// İlk kez geliyorsa "katıldı" mesajı yayınlar. Chat henüz hazır değilse false döner (tekrar denenir).
    /// </summary>
    public bool ServerRegisterPlayer(ulong clientId, string playerName)
    {
        if (!IsSpawned || !IsServer) return false;

        bool isNew = !serverNames.ContainsKey(clientId);
        string oldName = isNew ? null : serverNames[clientId];
        serverNames[clientId] = playerName;

        if (isNew)
        {
            string key = clientId == NetworkManager.ServerClientId ? "chat.created" : "chat.joined";
            SystemRpc(key, playerName, "");
        }
        else if (oldName != playerName)
        {
            SystemRpc("chat.renamed", oldName, playerName);
        }
        return true;
    }

    /// <summary>Host birini atmadan hemen önce çağırır: "X odadan atıldı" yazar, "ayrıldı" mesajını engeller.</summary>
    public void ServerAnnounceKick(ulong clientId, string playerName)
    {
        if (!IsSpawned || !IsServer) return;
        serverNames.Remove(clientId);
        SystemRpc("chat.kicked", playerName, "");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || clientId == NetworkManager.ServerClientId) return;

        if (serverNames.TryGetValue(clientId, out string playerName))
        {
            serverNames.Remove(clientId);
            SystemRpc("chat.left", playerName, "");
        }
    }

    private string GetServerName(ulong clientId)
    {
        return serverNames.TryGetValue(clientId, out string n) ? n : $"Player{clientId}";
    }

    // ---------------- Herkese ----------------

    [Rpc(SendTo.ClientsAndHost)]
    private void ChatRpc(string sender, string text)
    {
        AddLine(new ChatLine { Sender = sender, Text = text, IsSystem = false });
    }

    /// <summary>Sistem mesajı: metin değil çeviri anahtarı gönderilir, her oyuncu kendi dilinde görür.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    private void SystemRpc(string key, string arg0, string arg1)
    {
        AddLine(new ChatLine { Sender = "", Text = key, Arg0 = arg0, Arg1 = arg1, IsSystem = true });
    }

    private void AddLine(ChatLine line)
    {
        line.Time = UnityEngine.Time.unscaledTime;
        lines.Add(line);
        if (lines.Count > MaxStoredLines) lines.RemoveAt(0);
        LineAdded?.Invoke();
    }

    private static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        text = text.Replace("\n", " ").Replace("\r", " ").Trim();
        if (text.Length > MaxMessageLength) text = text.Substring(0, MaxMessageLength);
        return text;
    }
}
