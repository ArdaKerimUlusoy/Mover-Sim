using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player prefab'ına ekle. Ağda doğduğunda:
///  - Bu oyuncu BENİMSE: kontroller, kamera ve ses dinleyici açık kalır, doğma noktasına ışınlanır.
///  - BAŞKASININSA: kontroller/kamera kapatılır, sadece görünen bir kapsül olarak kalır.
/// </summary>
public class PlayerNetworkSetup : NetworkBehaviour
{
    public static event Action<PlayerNetworkSetup> LocalPlayerSpawned;
    public static event Action LocalPlayerDespawned;

    [Tooltip("Sadece sahibinde açık kalması gereken ekstra bileşenler (isteğe bağlı).")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours = Array.Empty<Behaviour>();

    [Tooltip("Uzaktaki oyuncuların rengi (kendi kapsülünü zaten görmüyorsun).")]
    [SerializeField] private Color remotePlayerColor = new Color(0.95f, 0.55f, 0.15f);

    public override void OnNetworkSpawn()
    {
        bool mine = IsOwner;

        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null) controller.enabled = mine;

        ObjectCarrier carrier = GetComponent<ObjectCarrier>();
        if (carrier != null) carrier.enabled = mine;

        foreach (Camera cam in GetComponentsInChildren<Camera>(true)) cam.enabled = mine;
        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = mine;
        foreach (Behaviour b in ownerOnlyBehaviours)
        {
            if (b != null) b.enabled = mine;
        }

        gameObject.name = mine ? $"Player_{OwnerClientId} (Sen)" : $"Player_{OwnerClientId}";

        if (mine)
        {
            MoveToSpawnPoint();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            LocalPlayerSpawned?.Invoke(this);
        }
        else
        {
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null) r.material.color = remotePlayerColor;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            LocalPlayerDespawned?.Invoke();
        }
    }

    private void MoveToSpawnPoint()
    {
        Vector3 position;
        Quaternion rotation;

        if (SpawnPoint.All.Count > 0)
        {
            SpawnPoint sp = SpawnPoint.All[(int)(OwnerClientId % (ulong)SpawnPoint.All.Count)];
            position = sp.transform.position + Vector3.up;
            rotation = Quaternion.Euler(0f, sp.transform.eulerAngles.y, 0f);
        }
        else
        {
            // Doğma noktası yoksa oyuncuları yan yana diz.
            position = transform.position + Vector3.right * (1.5f * OwnerClientId);
            rotation = transform.rotation;
        }

        // CharacterController açıkken pozisyonu değiştirmek işe yaramaz, kısa süre kapat.
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;
    }
}
