using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Açılıp kapanan kapı / dolap kapağı (anında etkileşim örneği).
/// Kurulum: boş bir obje ("DoorHinge") kapının MENTEŞE kenarına koyun, kapı modelini (ör. küp)
/// onun içine çocuk olarak koyun. Bu bileşeni + NetworkObject'i DoorHinge'e ekleyin.
/// Kapı menteşe etrafında Y ekseninde döner.
/// </summary>
public class DoorInteractable : Interactable
{
    [Header("Kapı")]
    [SerializeField] private float openAngle = 95f;
    [SerializeField] private float speed = 4f;

    private readonly NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);

    private Quaternion closedRotation;

    private void Awake()
    {
        closedRotation = transform.localRotation;
    }

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += OnOpenChanged;
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnOpenChanged;
    }

    private void OnOpenChanged(bool oldValue, bool newValue)
    {
        GameAudio.PlayAt(newValue ? SoundId.DoorOpen : SoundId.DoorClose, transform.position);
    }

    private void Update()
    {
        Quaternion target = isOpen.Value ? closedRotation * Quaternion.Euler(0f, openAngle, 0f) : closedRotation;
        transform.localRotation = Quaternion.Slerp(transform.localRotation, target, Time.deltaTime * speed);
    }

    public override string GetPrompt(PlayerInteractor player)
    {
        return Loc.T(isOpen.Value ? "interact.close" : "interact.open");
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        isOpen.Value = !isOpen.Value;
    }
}
