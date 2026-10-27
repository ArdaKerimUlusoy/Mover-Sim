using System.Collections.Generic;
using UnityEngine;

/// <summary>Garaj sahnesindeki doğma noktası: oyuna giriş ve her yeni görev başı oyuncular buraya gelir.</summary>
public class GarageSpawnPoint : MonoBehaviour
{
    public static readonly List<GarageSpawnPoint> All = new List<GarageSpawnPoint>();

    private void OnEnable() => All.Add(this);
    private void OnDisable() => All.Remove(this);

    /// <summary>İstemci kimliğine göre sabit nokta (aynı anda gelenler üst üste binmesin).</summary>
    public static GarageSpawnPoint For(ulong clientId) => All.Count == 0 ? null : All[(int)(clientId % (ulong)All.Count)];

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.5f);
        Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward);
    }
}
