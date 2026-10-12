using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Oyuncunun E tuşu ile yaptığı her şey (sadece kendi oyuncusunda çalışır):
///  - Yerdeki eşyaya bakıp E → çantaya al (anında)
///  - Etkileşim noktasına (kapı, dolap...) bakıp E / E basılı tut
///  - Başka bir oyuncuya ARKASINDAN bakıp E basılı tut → çantasını aç, bir eşya çal
/// Basılı tutarken ekranın ortasında dolan bir halka çıkar; bırakırsan veya başka yere bakarsan iptal olur.
/// PlayerNetworkSetup tarafından otomatik eklenir.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float range = 3f;
    [SerializeField] private float stealHoldDuration = 1.5f;
    [SerializeField] private LayerMask mask = ~0;

    private enum TargetKind { None, Item, Interactable, Player, Trash }

    private PlayerNetworkSetup netSetup;
    private PlayerStealth stealth;
    private PlayerInventory inventory;
    private ObjectCarrier carrier;
    private Transform cam;
    private Collider[] ownColliders;

    private TargetKind kind;
    private WorldItem targetItem;
    private Interactable targetInteractable;
    private PlayerInventory targetPlayer;
    private TrashBin targetTrash;

    private Object holdTarget;
    private float holdTime;
    private float holdDuration;
    private bool waitForRelease;

    private void Awake()
    {
        netSetup = GetComponent<PlayerNetworkSetup>();
        stealth = GetComponent<PlayerStealth>();
        inventory = GetComponent<PlayerInventory>();
        carrier = GetComponent<ObjectCarrier>();
        ownColliders = GetComponentsInChildren<Collider>();
        Camera c = GetComponentInChildren<Camera>(true);
        if (c != null) cam = c.transform;
    }

    private bool IsLocalOwner => netSetup != null && netSetup.IsSpawned && netSetup.IsOwner;

    private bool InputBlocked =>
        ChatUI.IsTyping || SessionMenuUI.IsPaused || PlayerInventory.IsPanelOpen ||
        Cursor.lockState != CursorLockMode.Locked || (carrier != null && carrier.IsHolding);

    private void Update()
    {
        if (!IsLocalOwner) return;

        UpdateCrimeFlag();

        // Dolapta saklanıyorsa: E = dışarı çık (içeriden ışın dolabı göremez)
        if (stealth != null && stealth.CurrentHidingSpot != null)
        {
            ClearTarget();
            CancelHold();
            if (!ChatUI.IsTyping && !SessionMenuUI.IsPaused && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                stealth.CurrentHidingSpot.RequestExit();
            }
            return;
        }

        if (InputBlocked)
        {
            ClearTarget();
            CancelHold();
            return;
        }

        FindTarget();

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        switch (kind)
        {
            case TargetKind.Item:
                CancelHold();
                if (kb.eKey.wasPressedThisFrame && inventory != null) inventory.RequestPickup(targetItem);
                break;

            case TargetKind.Interactable:
                if (!targetInteractable.CanInteract(gameObject)) { CancelHold(); break; }
                float duration = targetInteractable.GetHoldDuration(gameObject);
                if (duration <= 0f)
                {
                    CancelHold();
                    if (kb.eKey.wasPressedThisFrame)
                    {
                        if (targetInteractable.HandleLocally) targetInteractable.ClientInteract(this);
                        else targetInteractable.RequestInteract();
                    }
                }
                else if (UpdateHold(kb, targetInteractable, duration))
                {
                    GameAudio.Play2D(SoundId.HoldComplete, 0.6f);
                    targetInteractable.RequestInteract();
                }
                break;

            case TargetKind.Trash:
                CancelHold();
                if (kb.eKey.wasPressedThisFrame && inventory != null)
                {
                    targetTrash.PeekOpen();
                    inventory.OpenExternal(targetTrash);
                }
                break;

            case TargetKind.Player:
                if (inventory == null || !inventory.CanStealFrom(targetPlayer)) { CancelHold(); break; }
                if (UpdateHold(kb, targetPlayer, stealHoldDuration))
                {
                    inventory.RequestStartSteal(targetPlayer);
                }
                break;

            default:
                CancelHold();
                break;
        }
    }

    /// <summary>Basılı tutmayı ilerletir; tamamlandığı karede true döner.
    /// Tamamlandıktan sonra tekrar başlamak için E bırakılmalıdır.</summary>
    private bool UpdateHold(Keyboard kb, Object target, float duration)
    {
        if (holdTarget != target)
        {
            holdTarget = target;
            holdTime = 0f;
        }
        holdDuration = duration;

        if (!kb.eKey.isPressed)
        {
            holdTime = 0f;
            waitForRelease = false;
            return false;
        }

        if (waitForRelease) return false;

        if (holdTime <= 0f && target is Interactable started) started.ClientHoldStarted(this);
        holdTime += Time.deltaTime;
        if (holdTime >= duration)
        {
            holdTime = 0f;
            waitForRelease = true;
            return true;
        }
        return false;
    }

    /// <summary>Eser değiştirirken / çalarken güvenliklere "suç işliyor" görünürsün.</summary>
    private void UpdateCrimeFlag()
    {
        if (stealth == null) return;
        bool crime = holdTime > 0f && holdTarget != null &&
                     ((kind == TargetKind.Player && holdTarget == targetPlayer) ||
                      (kind == TargetKind.Interactable && holdTarget == targetInteractable &&
                       (targetInteractable is ArtworkDisplay || targetInteractable is JailCell || targetInteractable is SecurityCamera))); // kilit / konsol kurcalamak da suç
        stealth.SetDoingCrime(crime);
    }

    private void CancelHold()
    {
        holdTarget = null;
        holdTime = 0f;
    }

    private void ClearTarget()
    {
        kind = TargetKind.None;
        targetItem = null;
        targetInteractable = null;
        targetPlayer = null;
        targetTrash = null;
    }

    private void FindTarget()
    {
        ClearTarget();
        if (cam == null) return;

        RaycastHit[] hits = Physics.RaycastAll(new Ray(cam.position, cam.forward), range, mask, QueryTriggerInteraction.Collide);
        float closest = float.MaxValue;
        Collider best = null;

        foreach (RaycastHit hit in hits)
        {
            if (IsOwn(hit.collider) || hit.distance >= closest) continue;
            // Tetikleyici (trigger) çarpıştırıcılar sadece etkileşim noktasıysa sayılır
            if (hit.collider.isTrigger && hit.collider.GetComponentInParent<Interactable>() == null) continue;
            closest = hit.distance;
            best = hit.collider;
        }

        if (best == null) return;

        Interactable inter = best.GetComponentInParent<Interactable>();
        if (inter != null && inter.IsSpawned && closest <= inter.MaxDistance)
        {
            kind = TargetKind.Interactable;
            targetInteractable = inter;
            return;
        }

        WorldItem wi = best.GetComponentInParent<WorldItem>();
        if (wi != null && wi.IsSpawned && wi.Item != null)
        {
            kind = TargetKind.Item;
            targetItem = wi;
            return;
        }

        TrashBin bin = best.GetComponentInParent<TrashBin>();
        if (bin != null && closest <= TrashBin.UseRange)
        {
            kind = TargetKind.Trash;
            targetTrash = bin;
            return;
        }

        PlayerInventory other = best.GetComponentInParent<PlayerInventory>();
        if (other != null && other != inventory && other.IsSpawned)
        {
            kind = TargetKind.Player;
            targetPlayer = other;
        }
    }

    private bool IsOwn(Collider c)
    {
        foreach (Collider own in ownColliders)
        {
            if (own == c) return true;
        }
        return false;
    }

    // ---------------- Arayüz ----------------

    private void OnGUI()
    {
        if (!IsLocalOwner) return;

        if (stealth != null && stealth.CurrentHidingSpot != null)
        {
            UIUtil.DrawPrompt(Loc.T("hide.leave"), Screen.height / 2f + 34f, Color.white);
            return;
        }

        if (kind == TargetKind.None) return;

        float y = Screen.height / 2f + 34f;
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);

        switch (kind)
        {
            case TargetKind.Item:
            {
                ItemDefinition def = targetItem.Item;
                string kg = def.weightKg.ToString("0.#");
                bool fits = inventory != null && inventory.CanFit(def);
                string text = fits ? Loc.T("inv.pickup", def.DisplayName, kg) : Loc.T("inv.toofull", def.DisplayName, kg);
                if (def.type == ItemType.Forgery && targetItem.Similarity != InventorySlot.NoSimilarity)
                {
                    text += "   " + Loc.T("inv.similarity", targetItem.Similarity);
                }
                UIUtil.DrawPrompt(text, y, fits ? Color.white : new Color(1f, 0.45f, 0.4f));
                break;
            }

            case TargetKind.Interactable:
            {
                bool can = targetInteractable.CanInteract(gameObject);
                UIUtil.DrawPrompt(targetInteractable.GetPrompt(this), y, can ? Color.white : new Color(0.75f, 0.75f, 0.75f));
                float duration = targetInteractable.GetHoldDuration(gameObject);
                if (can && duration > 0f && holdTarget == targetInteractable && holdTime > 0f)
                {
                    UIUtil.DrawRing(center, 22f, holdTime / duration, new Color(1f, 0.8f, 0.3f));
                }
                break;
            }

            case TargetKind.Trash:
                UIUtil.DrawPrompt(Loc.T("trash.prompt"), y, Color.white);
                break;

            case TargetKind.Player:
            {
                PlayerNetworkSetup ps = targetPlayer.GetComponent<PlayerNetworkSetup>();
                string name = ps != null ? ps.PlayerName : "?";

                if (targetPlayer.SlotCount == 0)
                {
                    UIUtil.DrawPrompt(Loc.T("steal.emptyprompt", name), y, new Color(0.8f, 0.8f, 0.8f), 16);
                }
                else if (inventory != null && !inventory.IsBehind(targetPlayer))
                {
                    UIUtil.DrawPrompt(Loc.T("steal.getbehind", name), y, new Color(0.8f, 0.8f, 0.8f), 16);
                }
                else
                {
                    UIUtil.DrawPrompt(Loc.T("steal.prompt", name), y, new Color(1f, 0.85f, 0.4f));
                    if (holdTarget == targetPlayer && holdTime > 0f)
                    {
                        UIUtil.DrawRing(center, 22f, holdTime / stealHoldDuration, new Color(1f, 0.45f, 0.35f));
                    }
                }
                break;
            }
        }
    }
}
