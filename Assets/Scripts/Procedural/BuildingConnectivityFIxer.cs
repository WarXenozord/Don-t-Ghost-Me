using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Checks if all rooms in a procedurally generated building are connected.
/// If disconnected sections are found, creates emergency hallways to connect them.
/// </summary>
public class BuildingConnectivityFixer : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Layer for room floors (used to detect rooms)")]
    public LayerMask roomFloorLayer;

    [Header("Emergency Hallway")]
    [Tooltip("Prefab to spawn for emergency connecting hallways")]
    public GameObject emergencyHallwayPrefab;

    [Tooltip("Width of emergency hallways")]
    public float hallwayWidth = 2f;

    [Tooltip("Height of emergency hallways")]
    public float hallwayHeight = 3f;

    [Header("Debug")]
    public bool visualizeConnections = true;
    public bool autoFixOnGenerate = true;
    public Color connectedColor = Color.green;
    public Color disconnectedColor = Color.red;

    private List<HashSet<Vector3Int>> _islands; // Groups of connected rooms
    private Dictionary<Vector3Int, int> _roomToIsland; // Which island each room belongs to

    /// <summary>
    /// Checks building connectivity and optionally fixes disconnections
    /// </summary>
    public bool CheckAndFixConnectivity(ProceduralBuildingGenerator generator)
    {
        if (generator == null)
        {
            Debug.LogError("[ConnectivityFixer] No generator provided!");
            return false;
        }

        Debug.Log("[ConnectivityFixer] Checking building connectivity...");

        // Get all room centers
        var roomCenters = new List<Vector3>();
        generator.CollectRoomCenterNodes(roomCenters);

        if (roomCenters.Count == 0)
        {
            Debug.LogWarning("[ConnectivityFixer] No rooms found!");
            return true;
        }

        // Convert to grid positions (rounded to integers)
        var gridRooms = roomCenters.Select(v => new Vector3Int(
            Mathf.RoundToInt(v.x),
            Mathf.RoundToInt(v.y),
            Mathf.RoundToInt(v.z)
        )).ToList();

        // Find connected islands using flood fill
        FindConnectedIslands(gridRooms);

        if (_islands.Count == 1)
        {
            Debug.Log($"[ConnectivityFixer] ✓ All {roomCenters.Count} rooms are connected!");
            return true;
        }

        // Disconnected sections found!
        Debug.LogWarning($"[ConnectivityFixer] ✗ Found {_islands.Count} disconnected sections!");
        
        for (int i = 0; i < _islands.Count; i++)
        {
            Debug.LogWarning($"  Island {i}: {_islands[i].Count} rooms");
        }

        if (autoFixOnGenerate)
        {
            ConnectIslands(generator);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds connected groups of rooms using flood fill
    /// </summary>
    private void FindConnectedIslands(List<Vector3Int> rooms)
    {
        _islands = new List<HashSet<Vector3Int>>();
        _roomToIsland = new Dictionary<Vector3Int, int>();

        var visited = new HashSet<Vector3Int>();

        foreach (var room in rooms)
        {
            if (visited.Contains(room)) continue;

            // Start new island
            var island = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(room);
            visited.Add(room);

            // Flood fill to find all connected rooms
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                island.Add(current);
                _roomToIsland[current] = _islands.Count;

                // Check neighbors (up/down/left/right/forward/back)
                var neighbors = GetConnectedNeighbors(current, rooms);
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            _islands.Add(island);
        }
    }

    /// <summary>
    /// Gets rooms that are connected to this room via hallways
    /// </summary>
    private List<Vector3Int> GetConnectedNeighbors(Vector3Int room, List<Vector3Int> allRooms)
    {
        var connected = new List<Vector3Int>();
        var roomWorldPos = new Vector3(room.x, room.y, room.z);

        // Check if there's a hallway connecting to nearby rooms
        foreach (var other in allRooms)
        {
            if (other == room) continue;

            var otherWorldPos = new Vector3(other.x, other.y, other.z);
            
            // Check if connected via hallway (raycast between room centers)
            if (IsConnectedViaHallway(roomWorldPos, otherWorldPos))
            {
                connected.Add(other);
            }
        }

        return connected;
    }

    /// <summary>
    /// Checks if two rooms are connected by a hallway (no obstructions)
    /// </summary>
    private bool IsConnectedViaHallway(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;

        // Too far apart to be directly connected
        if (distance > 30f) return false;

        // Raycast to check for hallway floor
        int samples = Mathf.CeilToInt(distance / 2f);
        for (int i = 1; i < samples; i++)
        {
            float t = (float)i / samples;
            Vector3 samplePoint = Vector3.Lerp(from, to, t);
            samplePoint.y -= 1f; // Check floor below

            // Check if there's a floor at this point (hallway exists)
            if (!Physics.CheckSphere(samplePoint, 0.5f, roomFloorLayer))
            {
                return false; // No hallway floor found
            }
        }

        return true; // Hallway exists all the way
    }

    /// <summary>
    /// Creates emergency hallways to connect all islands
    /// </summary>
    private void ConnectIslands(ProceduralBuildingGenerator generator)
    {
        Debug.Log("[ConnectivityFixer] Creating emergency connections...");

        // Connect each island to the next one
        for (int i = 0; i < _islands.Count - 1; i++)
        {
            var islandA = _islands[i];
            var islandB = _islands[i + 1];

            // Find closest pair of rooms between islands
            Vector3Int roomA = Vector3Int.zero;
            Vector3Int roomB = Vector3Int.zero;
            float minDist = float.MaxValue;

            foreach (var a in islandA)
            {
                foreach (var b in islandB)
                {
                    float dist = Vector3Int.Distance(a, b);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        roomA = a;
                        roomB = b;
                    }
                }
            }

            // Create hallway between these rooms
            CreateEmergencyHallway(roomA, roomB, generator);
            
            Debug.Log($"[ConnectivityFixer] Connected island {i} to {i+1} " +
                      $"(distance: {minDist:F1} units)");
        }
    }

    /// <summary>
    /// Creates an emergency hallway between two rooms
    /// </summary>
    private void CreateEmergencyHallway(Vector3Int from, Vector3Int to, ProceduralBuildingGenerator generator)
    {
        Vector3 fromWorld = new Vector3(from.x, from.y, from.z);
        Vector3 toWorld = new Vector3(to.x, to.y, to.z);

        // Create L-shaped hallway (horizontal then vertical, or vice versa)
        Vector3 midpoint = new Vector3(to.x, from.y, from.z); // Corner point

        // Segment 1: from → midpoint
        CreateHallwaySegment(fromWorld, midpoint, generator);

        // Segment 2: midpoint → to
        CreateHallwaySegment(midpoint, toWorld, generator);

        Debug.Log($"[ConnectivityFixer] Created emergency hallway from {from} to {to}");
    }

    /// <summary>
    /// Creates a straight hallway segment
    /// </summary>
    private void CreateHallwaySegment(Vector3 from, Vector3 to, ProceduralBuildingGenerator generator)
    {
        if (Vector3.Distance(from, to) < 0.1f) return; // Too short

        Vector3 direction = (to - from).normalized;
        float length = Vector3.Distance(from, to);
        Vector3 midpoint = (from + to) / 2f;

        // Determine orientation
        Quaternion rotation = Quaternion.LookRotation(direction);

        // Use generator's hallway creation if available
        if (emergencyHallwayPrefab != null)
        {
            var hallway = Instantiate(emergencyHallwayPrefab, midpoint, rotation, generator.transform);
            hallway.transform.localScale = new Vector3(hallwayWidth, hallwayHeight, length);
            hallway.name = "EmergencyHallway";
        }
        else
        {
            // Create simple cube hallway
            var hallway = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hallway.transform.position = midpoint;
            hallway.transform.rotation = rotation;
            hallway.transform.localScale = new Vector3(hallwayWidth, hallwayHeight, length);
            hallway.transform.SetParent(generator.transform);
            hallway.name = "EmergencyHallway";
            
            // Set material/layer to match building
            var renderer = hallway.GetComponent<Renderer>();
            if (renderer != null && generator.transform.childCount > 0)
            {
                var firstChild = generator.transform.GetChild(0);
                var firstRenderer = firstChild.GetComponent<Renderer>();
                if (firstRenderer != null)
                {
                    renderer.sharedMaterial = firstRenderer.sharedMaterial;
                }
            }
        }
    }

    /// <summary>
    /// Visualizes connectivity in scene view
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!visualizeConnections || _islands == null) return;

        // Draw each island in a different color
        Color[] colors = new Color[] { Color.green, Color.red, Color.blue, Color.yellow, Color.cyan, Color.magenta };

        for (int i = 0; i < _islands.Count; i++)
        {
            Gizmos.color = colors[i % colors.Length];
            
            foreach (var room in _islands[i])
            {
                Vector3 worldPos = new Vector3(room.x, room.y, room.z);
                Gizmos.DrawWireSphere(worldPos, 2f);
                
                // Draw label
                #if UNITY_EDITOR
                UnityEditor.Handles.Label(worldPos + Vector3.up * 3f, $"Island {i}");
                #endif
            }
        }
    }

    /// <summary>
    /// Manual trigger for checking connectivity
    /// </summary>
    [ContextMenu("Check Connectivity")]
    public void ManualCheck()
    {
        var generator = GetComponent<ProceduralBuildingGenerator>();
        if (generator == null)
        {
            generator = FindObjectOfType<ProceduralBuildingGenerator>();
        }

        if (generator != null)
        {
            CheckAndFixConnectivity(generator);
        }
        else
        {
            Debug.LogError("[ConnectivityFixer] No ProceduralBuildingGenerator found!");
        }
    }
}