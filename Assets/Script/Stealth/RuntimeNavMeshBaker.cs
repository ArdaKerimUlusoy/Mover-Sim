using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Oyun başlarken NavMesh'i (güvenliklerin yürüyebileceği alan) oluşturur.
/// Aynı objede NavMeshSurface olmalı. Böylece NavMesh dosyasını elle bake edip commit'lemeye gerek kalmaz.
/// </summary>
[DefaultExecutionOrder(-500)]
[RequireComponent(typeof(NavMeshSurface))]
public class RuntimeNavMeshBaker : MonoBehaviour
{
    public static bool Ready { get; private set; }
    private static RuntimeNavMeshBaker instance;

    /// <summary>Müze düzeni değişince (MuseumLayout) NavMesh'i yeniden oluşturur. Sadece sunucuda gerekir.</summary>
    public static void Rebake()
    {
        if (instance == null) return;
        Physics.SyncTransforms();
        instance.GetComponent<NavMeshSurface>().BuildNavMesh();
        Ready = true;
    }

    private void Awake()
    {
        instance = this;
        Ready = false;
        NavMeshSurface surface = GetComponent<NavMeshSurface>();
        surface.BuildNavMesh();
        Ready = true;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        Ready = false;
    }
}
