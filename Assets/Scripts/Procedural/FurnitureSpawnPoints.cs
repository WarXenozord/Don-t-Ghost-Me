using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Attach to furniture prefabs (tables, shelves, desks, counters, etc.)
/// Defines spawn points where small items can be placed on top.
/// 
/// Setup:
///   1. Add this script to your table prefab
///   2. Create empty GameObjects as children at placement positions
///   3. Add them to the Spawn Points list
///   4. PropSpawner will automatically populate these with items
/// </summary>
public class FurnitureSpawnPoints : MonoBehaviour
{
    [Header("Spawn Configuration")]
    [Tooltip("List of transforms where items can spawn. Create empty child GameObjects.")]
    public List<Transform> spawnPoints = new List<Transform>();

    [Tooltip("If true, items will rotate to match furniture's rotation. If false, items face world forward.")]
    public bool inheritRotation = true;

    [Tooltip("If true, spawned items become children of this furniture. Good for moving furniture.")]
    public bool parentItems = false;

    [Header("Runtime State (Read-Only)")]
    [SerializeField] private List<bool> _occupied; // parallel to spawnPoints

    // ?? Initialization ?????????????????????????????????????????????????????

    private void Awake()
    {
        // Initialize occupation tracking
        _occupied = new List<bool>(new bool[spawnPoints.Count]);
    }

    // ?? Spawn Point Access ?????????????????????????????????????????????????

    /// <summary>
    /// Returns a random available spawn point, or null if all occupied.
    /// </summary>
    public Transform GetRandomAvailablePoint()
    {
        var available = GetAvailablePoints();
        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }

    /// <summary>
    /// Returns all spawn points that aren't occupied yet.
    /// </summary>
    public List<Transform> GetAvailablePoints()
    {
        var result = new List<Transform>();
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] != null && !_occupied[i])
                result.Add(spawnPoints[i]);
        }
        return result;
    }

    /// <summary>
    /// Marks a spawn point as occupied so nothing else spawns there.
    /// </summary>
    public void MarkOccupied(Transform point)
    {
        int idx = spawnPoints.IndexOf(point);
        if (idx >= 0 && idx < _occupied.Count)
            _occupied[idx] = true;
    }

    /// <summary>
    /// Total number of spawn points (occupied + available).
    /// </summary>
    public int TotalPoints => spawnPoints.Count;

    /// <summary>
    /// Number of spawn points still available.
    /// </summary>
    public int AvailableCount => GetAvailablePoints().Count;

    // ?? Gizmos ?????????????????????????????????????????????????????????????

    private void OnDrawGizmosSelected()
    {
        if (spawnPoints == null) return;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            if (spawnPoints[i] == null) continue;

            // Green if available, red if occupied (in play mode)
            bool occupied = Application.isPlaying && i < _occupied.Count && _occupied[i];
            Gizmos.color = occupied ? Color.red : Color.green;

            Gizmos.DrawWireSphere(spawnPoints[i].position, 0.05f);
            Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[i].position + Vector3.up * 0.2f);
        }
    }
}