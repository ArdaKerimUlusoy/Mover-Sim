using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Staff Only" (personel) alanı: burada görülen oyuncu şüphe çeker.
/// Bir BoxCollider'ın sınırlarını kullanır (collider tetikleyici olmalı ya da sadece alan olarak durur).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RestrictedArea : MonoBehaviour
{
    public static readonly List<RestrictedArea> All = new List<RestrictedArea>();

    private BoxCollider box;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true;
    }

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    public bool Contains(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y + 1f && Mathf.Abs(local.z) <= half.z;
    }

    public static bool IsInside(Vector3 worldPos)
    {
        foreach (RestrictedArea a in All)
        {
            if (a != null && a.Contains(worldPos)) return true;
        }
        return false;
    }

    private void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(b.center, b.size);
    }
}
