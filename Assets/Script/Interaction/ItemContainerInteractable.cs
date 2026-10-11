using Unity.Netcode;
using UnityEngine;

/// <summary>
/// İçinde eşya olan dolap / çekmece / vitrin (basılı tutma örneği).
/// E'ye basılı tutarak aranır; bitince içindeki eşya aranan oyuncunun çantasına girer.
/// Kurulum: herhangi bir objeye (ör. küp) NetworkObject + bu bileşeni ekleyin, "Item" alanına eşyayı sürükleyin.
/// </summary>
public class ItemContainerInteractable : Interactable
{
    [Header("İçerik")]
    [SerializeField] private ItemDefinition item;
    [SerializeField, Min(1)] private int count = 1;
    [Tooltip("Sahte eserler için benzerlik (0–100), -1 = yok.")]
    [SerializeField, Range(-1, 100)] private int similarityPercent = -1;
    [Tooltip("Boşaldıktan sonra kaç saniyede tekrar dolsun (0 = hiç dolmasın).")]
    [SerializeField, Min(0f)] private float refillSeconds = 0f;

    private readonly NetworkVariable<int> remaining = new NetworkVariable<int>(0);
    private float refillAt = -1f;

    private void Reset()
    {
        holdDuration = 2f;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) remaining.Value = item != null ? count : 0;
    }

    public override bool CanInteract(GameObject player) => base.CanInteract(player) && remaining.Value > 0;

    public override string GetPrompt(PlayerInteractor player)
    {
        if (remaining.Value <= 0) return Loc.T("interact.empty");
        return Loc.T(holdDuration > 0f ? "interact.search.hold" : "interact.search");
    }

    protected override void ServerInteract(ulong clientId, NetworkObject player)
    {
        if (remaining.Value <= 0 || item == null) return;

        PlayerInventory inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        byte sim = similarityPercent < 0 ? InventorySlot.NoSimilarity : (byte)similarityPercent;
        if (!inv.ServerAddItem(item, sim))
        {
            inv.ServerNotify("inv.full");
            return;
        }

        remaining.Value--;
        inv.ServerPlaySound(SoundId.Pickup, transform.position);
        inv.ServerNotify("interact.found", item);

        if (remaining.Value <= 0 && refillSeconds > 0f) refillAt = Time.time + refillSeconds;
    }

    private void Update()
    {
        if (!IsServer || refillAt < 0f || Time.time < refillAt) return;
        refillAt = -1f;
        remaining.Value = count;
    }
}
