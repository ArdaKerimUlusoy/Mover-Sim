using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sahneye boş bir obje koyup bunu ekle: oyuncular bu noktalarda doğar.
/// Oyuncu sayısı kadar (ör. 4) nokta koymak yeterli.
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    public static readonly List<SpawnPoint> All = new List<SpawnPoint>();

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward);
    }
}
