using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Oda (session) kurma / koda göre katılma / ayrılma işlerini yönetir.
/// Unity Multiplayer Services + Relay kullanır: port açmaya veya aynı ağda olmaya gerek yok.
/// NetworkManager ile AYNI GameObject üzerine ekle.
/// </summary>
public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance { get; private set; }

    [SerializeField] private int maxPlayers = 4;

    [Tooltip("Açıkken her çalıştırmada ayrı bir anonim kimlik alınır. " +
             "Aynı bilgisayarda iki kopya (iki build ya da Multiplayer Play Mode) ile test ederken AÇIK kalmalı.")]
    [SerializeField] private bool uniqueProfilePerRun = true;

    public ISession ActiveSession { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "";
    public int MaxPlayers => maxPlayers;

    /// <summary>Oda kurulduğunda, katılındığında veya ayrılındığında tetiklenir.</summary>
    public event Action SessionChanged;

    private Task signInTask;
    private bool isLeaving;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped += OnNetworkStopped;
        }

        // Menü açılırken arka planda giriş yap, butona basınca beklemesin.
        _ = WarmUpAsync();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= OnNetworkStopped;
        }
    }

    private void OnApplicationQuit()
    {
        // Odadan düzgünce çık (beklemeden).
        if (ActiveSession != null)
        {
            _ = ActiveSession.LeaveAsync();
        }
    }

    // ---------------- Giriş ----------------

    private async Task WarmUpAsync()
    {
        try
        {
            await EnsureSignedInAsync();
        }
        catch (Exception e)
        {
            Status = "Servislere bağlanılamadı (internet / Unity Cloud bağlantısını kontrol et).";
            Debug.LogException(e);
        }
    }

    private Task EnsureSignedInAsync()
    {
        if (signInTask == null || signInTask.IsFaulted || signInTask.IsCanceled)
        {
            signInTask = SignInAsync();
        }
        return signInTask;
    }

    private async Task SignInAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            InitializationOptions options = new InitializationOptions();
            if (uniqueProfilePerRun)
            {
                // Profil adı: harf/rakam, en fazla 30 karakter.
                options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 16));
            }
            await UnityServices.InitializeAsync(options);
        }

        while (UnityServices.State == ServicesInitializationState.Initializing)
        {
            await Task.Yield();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    // ---------------- Oda kur / katıl / ayrıl ----------------

    public async Task CreateRoomAsync()
    {
        if (IsBusy || ActiveSession != null) return;

        IsBusy = true;
        Status = "Oda kuruluyor...";
        try
        {
            await EnsureSignedInAsync();

            SessionOptions options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = true // listede görünmez, sadece kodla katılınır
            }.WithRelayNetwork();

            // Bu çağrı Relay'i ayarlar ve NetworkManager'ı otomatik olarak HOST başlatır.
            ActiveSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            Status = "";
            Debug.Log($"[Session] Oda kuruldu. Kod: {ActiveSession.Code}");
        }
        catch (Exception e)
        {
            ActiveSession = null;
            Status = "Oda kurulamadı: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            IsBusy = false;
            SessionChanged?.Invoke();
        }
    }

    public async Task JoinRoomAsync(string code)
    {
        if (IsBusy || ActiveSession != null) return;

        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            Status = "Önce oda kodunu yaz.";
            return;
        }

        IsBusy = true;
        Status = "Odaya katılınıyor...";
        try
        {
            await EnsureSignedInAsync();

            // Bu çağrı NetworkManager'ı otomatik olarak CLIENT başlatır.
            ActiveSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            Status = "";
            Debug.Log($"[Session] Odaya katılındı. Kod: {ActiveSession.Code}");
        }
        catch (Exception e)
        {
            ActiveSession = null;
            Status = "Katılınamadı. Kod yanlış olabilir ya da oda dolu/kapalı.";
            Debug.LogException(e);
        }
        finally
        {
            IsBusy = false;
            SessionChanged?.Invoke();
        }
    }

    public async Task LeaveRoomAsync()
    {
        if (isLeaving) return;
        isLeaving = true;

        ISession session = ActiveSession;
        ActiveSession = null;

        try
        {
            if (session != null) await session.LeaveAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Session] Ayrılırken hata: " + e.Message);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        isLeaving = false;
        SessionChanged?.Invoke();
    }

    /// <summary>Host oyunu kapatırsa / bağlantı koparsa menüye dön.</summary>
    private void OnNetworkStopped(bool wasHost)
    {
        if (ActiveSession == null || isLeaving) return;

        Status = wasHost ? "Oda kapandı." : "Host ile bağlantı koptu.";
        _ = LeaveRoomAsync();
    }
}
