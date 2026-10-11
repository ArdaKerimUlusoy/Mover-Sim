using UnityEngine;

/// <summary>Görüş hesapları (güvenlik görevlisi ve kamera ortak kullanır). Sunucuda çalışır.</summary>
public static class Vision
{
    /// <summary>
    /// Göz noktasından hedef oyuncu görülüyor mu?
    /// Yatay düzlemde mesafe + açı kontrolü, sonra arada duvar var mı diye ışın atılır.
    /// </summary>
    public static bool CanSee(Vector3 eye, Vector3 forward, float range, float fullAngle, PlayerStealth target)
    {
        if (target == null || target.IsHidden) return false;

        Vector3 chest = target.transform.position + Vector3.up * 0.4f;
        Vector3 to = chest - eye;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);

        if (flat.magnitude > range) return false;
        if (flatForward.sqrMagnitude > 0.0001f && Vector3.Angle(flatForward, flat) > fullAngle * 0.5f) return false;

        float dist = to.magnitude;
        if (dist < 0.01f) return true;

        if (Physics.Raycast(eye, to / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            // İlk çarpılan şey hedefin kendisi değilse arada engel var
            return hit.collider.GetComponentInParent<PlayerStealth>() == target;
        }
        return true;
    }

    /// <summary>Bir noktanın görüş alanında olup olmadığı (sahte eser kontrolü için).</summary>
    public static bool CanSeePoint(Vector3 eye, Vector3 forward, float range, float fullAngle, Vector3 point, Transform ignoreRoot = null)
    {
        Vector3 to = point - eye;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
        if (flat.magnitude > range) return false;
        if (flatForward.sqrMagnitude > 0.0001f && Vector3.Angle(flatForward, flat) > fullAngle * 0.5f) return false;

        float dist = to.magnitude;
        if (dist < 0.01f) return true;
        if (Physics.Raycast(eye, to / dist, out RaycastHit hit, dist - 0.3f, ~0, QueryTriggerInteraction.Ignore))
        {
            return ignoreRoot != null && hit.collider.transform.IsChildOf(ignoreRoot);
        }
        return true;
    }
}
