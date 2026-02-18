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
        var parent = buildingParents.TryGetValue(room.buildingIndex, out var p) ? p : transform;

        // Place at center of room on "table height"
        Vector3 pos = new Vector3(
            room.position.x + room.size.x * 0.5f,
            room.position.y + markTableHeight,
            room.position.z + room.size.z * 0.5f
        );

        var mark = Instantiate(ritualMarkPrefab, pos, Quaternion.identity, parent);
        mark.name = "RitualMark";

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

        // Pick random rooms (ground floor only for simplicity)
        var candidateRooms = rooms
            .Select((r, idx) => (room: r, idx))
            .Where(x => x.room.floorIndex == 0 && x.idx != markRoomIndex)
            .ToList();

        if (candidateRooms.Count == 0)
        {
            Debug.LogWarning("[ObjectiveSpawner] No suitable rooms for candles!");
            return;
        }

        // Shuffle and take up to candleCount rooms
        Shuffle(candidateRooms, rng);
        int roomsToUse = Mathf.Min(candleCount, candidateRooms.Count);

        for (int i = 0; i < candleCount; i++)
        {
            // Cycle through available rooms if we need more candles than rooms
            var (room, idx) = candidateRooms[i % roomsToUse];
            var parent = buildingParents.TryGetValue(room.buildingIndex, out var p) ? p : transform;

            // 50% chance on floor, 50% on a "prop" (raised height)
            bool onFloor = rng.Next(2) == 0;
            float yPos = onFloor
                ? room.position.y + candleFloorHeight
                : room.position.y + candleHeightOnProp + (float)rng.NextDouble() * 1.2f;

            // Random XZ in room (with margin)
            float margin = 0.5f;
            float x = room.position.x + margin + (float)rng.NextDouble() * (room.size.x - margin * 2f);
            float z = room.position.z + margin + (float)rng.NextDouble() * (room.size.z - margin * 2f);

            Vector3 pos = new Vector3(x, yPos, z);
            var candle = Instantiate(candlePrefab, pos, Quaternion.identity, parent);
            candle.name = $"Candle_{i + 1}";
        }
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