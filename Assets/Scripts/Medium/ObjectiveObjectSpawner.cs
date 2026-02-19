using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns Floor 1 objective objects (candles and ritual mark) in the procedurally
/// generated building after PropSpawner finishes.
/// 
/// Called by ProceduralBuildingGenerator after BuildProps().
/// </summary>
public class ObjectiveObjectSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject candlePrefab;
    [SerializeField] private GameObject ritualMarkPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private int candleCount = 5;
    [SerializeField] private float candleHeightOnProp = 0.05f; // offset above prop surface
    [SerializeField] private float candleFloorHeight  = 0.3f;  // height when on floor

    [Header("Ritual Mark")]
    [SerializeField] private RoomType preferredMarkRoom = RoomType.LivingRoom;
    [SerializeField] private float markTableHeight = 0.75f; // standard table height

    [Header("References")]
    [SerializeField] private GhostInteraction ghostInteraction; // refresh cache after spawning

    // ?? Public API ?????????????????????????????????????????????????????????

    /// <summary>
    /// Call this after PropSpawner.Furnish() completes.
    /// </summary>
    public void SpawnObjectives(
        List<BuildingRoom> rooms,
        Dictionary<int, Transform> buildingParents,
        int seed)
    {
        if (candlePrefab == null || ritualMarkPrefab == null)
        {
            Debug.LogWarning("[ObjectiveSpawner] Missing prefabs — skipping objective spawn.");
            return;
        }

        var rng = new System.Random(seed ^ 0xCAFE);

        // Spawn ritual mark first (so we know which room it's in)
        Transform markTransform = SpawnRitualMark(rooms, buildingParents, rng);

        // Spawn candles scattered across other rooms
        SpawnCandles(rooms, buildingParents, rng, markTransform);

        // Refresh GhostInteraction cache so ritual mark is detected
        if (ghostInteraction != null)
        {
            ghostInteraction.RefreshInteractableCache();
            Debug.Log("[ObjectiveSpawner] Refreshed ghost interaction cache after spawning objectives.");
        }

        Debug.Log($"[ObjectiveSpawner] Spawned {candleCount} candles and 1 ritual mark.");
    }

    // ?? Ritual Mark ????????????????????????????????????????????????????????

    private Transform SpawnRitualMark(
        List<BuildingRoom> rooms,
        Dictionary<int, Transform> buildingParents,
        System.Random rng)
    {
        // Find candidate rooms (preferred type, ground floor, reasonable size)
        var candidates = rooms
            .Where(r => r.floorIndex == 0 &&
                        r.roomType == preferredMarkRoom &&
                        r.size.x * r.size.z >= 16f) // at least 4x4
            .ToList();

        // Fallback: any large ground floor room
        if (candidates.Count == 0)
        {
            candidates = rooms
                .Where(r => r.floorIndex == 0 && r.size.x * r.size.z >= 16f)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[ObjectiveSpawner] No suitable room for ritual mark!");
            return null;
        }

        var room = candidates[rng.Next(candidates.Count)];
        //var parent = buildingParents.TryGetValue(room.buildingIndex, out var p) ? p : transform;

        // Place at center of room on "table height"
        Vector3 pos = new Vector3(
            room.position.x + room.size.x * 0.5f,
            room.position.y + markTableHeight,
            room.position.z + room.size.z * 0.5f
        );

        var mark = Instantiate(ritualMarkPrefab, pos, Quaternion.identity);
        mark.name = "RitualMark";
        LevelObjectiveManager objectiveManager = FindObjectOfType<LevelObjectiveManager>();
        objectiveManager.SetMark(mark.gameObject);

        Debug.Log($"[ObjectiveSpawner] Ritual mark placed in {room.roomType} at {pos}");
        return mark.transform;
    }

    // ?? Candles ????????????????????????????????????????????????????????????

    private void SpawnCandles(
        List<BuildingRoom> rooms,
        Dictionary<int, Transform> buildingParents,
        System.Random rng,
        Transform ritualMarkTransform)
    {
        // Avoid spawning candles in the same room as the mark
        int markRoomIndex = -1;
        if (ritualMarkTransform != null)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                if (IsInRoom(rooms[i], ritualMarkTransform.position))
                {
                    markRoomIndex = i;
                    break;
                }
            }
        }

        // Collect all furniture pieces (not individual points - we'll query dynamically)
        var furniturePieces = new List<(FurnitureSpawnPoints furniture, int roomIdx)>();
        
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].floorIndex != 0 || i == markRoomIndex) continue;
            
            var parent = buildingParents.TryGetValue(rooms[i].buildingIndex, out var p) ? p : null;
            if (parent == null) continue;

            var furniture = parent.GetComponentsInChildren<FurnitureSpawnPoints>();
            foreach (var piece in furniture)
            {
                if (piece.TotalPoints > 0)
                    furniturePieces.Add((piece, i));
            }
        }

        Debug.Log($"[ObjectiveSpawner] Found {furniturePieces.Count} furniture pieces with spawn points.");

        // Try to place candles on furniture - query available points dynamically
        int candlesPlaced = 0;
        int attempts = 0;
        int maxAttempts = candleCount * 10; // safety limit

        while (candlesPlaced < candleCount && attempts < maxAttempts)
        {
            attempts++;

            // Filter to furniture that still has available points
            var availableFurniture = furniturePieces
                .Where(f => f.furniture.AvailableCount > 0)
                .ToList();

            if (availableFurniture.Count == 0)
                break; // no more furniture points available

            // Pick random furniture piece
            var (furniture, roomIdx) = availableFurniture[rng.Next(availableFurniture.Count)];
            
            // Get a random available point from this piece
            var point = furniture.GetRandomAvailablePoint();
            if (point == null) continue; // shouldn't happen but safety check

            var parent = buildingParents.TryGetValue(rooms[roomIdx].buildingIndex, out var p) ? p : transform;

            var candle = Instantiate(candlePrefab, point.position, Quaternion.identity, parent);
            candle.name = $"Candle_{candlesPlaced + 1}";
            
            furniture.MarkOccupied(point); // mark IMMEDIATELY so next iteration won't pick it
            candlesPlaced++;
        }

        // If we need more candles than furniture points, spawn remaining on floor
        if (candlesPlaced < candleCount)
        {
            var candidateRooms = rooms
                .Select((r, idx) => (room: r, idx))
                .Where(x => x.room.floorIndex == 0 && x.idx != markRoomIndex)
                .ToList();

            if (candidateRooms.Count > 0)
            {
                Shuffle(candidateRooms, rng);
                int roomsToUse = Mathf.Min(candleCount - candlesPlaced, candidateRooms.Count);

                for (int i = candlesPlaced; i < candleCount; i++)
                {
                    var (room, idx) = candidateRooms[i % roomsToUse];
                    var parent = buildingParents.TryGetValue(room.buildingIndex, out var p) ? p : transform;

                    // Floor placement
                    float yPos = room.position.y + candleFloorHeight;
                    float margin = 0.5f;
                    float x = room.position.x + margin + (float)rng.NextDouble() * (room.size.x - margin * 2f);
                    float z = room.position.z + margin + (float)rng.NextDouble() * (room.size.z - margin * 2f);

                    Vector3 pos = new Vector3(x, yPos, z);
                    var candle = Instantiate(candlePrefab, pos, Quaternion.identity, parent);
                    candle.name = $"Candle_{i + 1}";
                }
            }
        }

        Debug.Log($"[ObjectiveSpawner] Placed {candlesPlaced} candles on furniture, " +
                  $"{candleCount - candlesPlaced} on floor.");
    }

    // ?? Utility ????????????????????????????????????????????????????????????

    private bool IsInRoom(BuildingRoom room, Vector3 worldPos)
    {
        return worldPos.x >= room.position.x &&
               worldPos.x <= room.position.x + room.size.x &&
               worldPos.z >= room.position.z &&
               worldPos.z <= room.position.z + room.size.z;
    }

    private void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}