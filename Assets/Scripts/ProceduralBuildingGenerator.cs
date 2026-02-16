using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

// ?????????????????????????????????????????????????????????????????????????????
//  DATA STRUCTURES
// ?????????????????????????????????????????????????????????????????????????????

[System.Serializable]
public class BuildingRoom
{
    public Vector3 position;
    public Vector3 size;
    public RoomType roomType = RoomType.General;
    public int floorIndex = 0;
    public int buildingIndex = 0;
    public List<int> connectedDoorIndices = new List<int>();
}

[System.Serializable]
public class BuildingDoor
{
    public Vector3 position;
    public Vector3 size;
    public int roomA = -1;
    public int roomB = -1;
    public int wallIndex = -1;       // Direct reference into walls list � used for O(1) splitting
    public bool wallFacingX = false; // Cached from source wall (wall is removed after splitting)
}

[System.Serializable]
public class BuildingWall
{
    public Vector3 position;
    public Vector3 size;
    // facingX = true  ? wall normal points along X; wall runs along Z (left/right walls)
    //                    size = (wallThickness, height, length)
    // facingX = false ? wall normal points along Z; wall runs along X (front/back walls)
    //                    size = (length, height, wallThickness)
    public bool facingX;
    public int roomA = -1; // -1 = exterior
    public int roomB = -1; // -1 = exterior
}

[System.Serializable]
public class BuildingStairs
{
    public Vector3 position;
    public Vector3 size;
    public int floorA;
    public int floorB;
    public bool dim;
}

[System.Serializable]
public class BuildingSection
{
    public Vector3 position;
    public Vector3 size;
    public int buildingIndex;
    public int roomsInSection;
}

public enum RoomType
{
    General, Bathroom, Kitchen, Hallway, LivingRoom, Bedroom, Storage, Stairwell
}

// ?????????????????????????????????????????????????????????????????????????????
//  GENERATOR
// ?????????????????????????????????????????????????????????????????????????????

public class ProceduralBuildingGenerator : MonoBehaviour
{
    [Header("Seed")]
    [SerializeField] private int seed = 12345;

    [Header("Building Parameters")]
    [SerializeField] private int targetNumRooms = 20;
    [SerializeField] private int numFloors = 2;
    [SerializeField] private float floorHeight = 3.5f;

    [Header("Building Layout")]
    [SerializeField] private int buildingsPerRow = 2;
    [SerializeField] private float buildingSpacingX = 0.5f;
    [SerializeField] private float buildingSpacingZ = 0.5f;
    [SerializeField] private Vector3 minBuildingSize = new Vector3(12, 12, 12);
    [SerializeField] private Vector3 maxBuildingSize = new Vector3(25, 12, 25);

    [Header("Room Generation")]
    [SerializeField] private float minRoomSize = 3f;
    [SerializeField] private float maxRoomSize = 8f;
    [SerializeField] private float doorWidth = 1f;
    [SerializeField] private float doorHeight = 2.2f;
    [SerializeField] private float wallThickness = 0.2f;
    [SerializeField] private float floorCeilThickness = 0.15f;

    [Header("Stairs")]
    [SerializeField] private float stairWidth = 1.2f;
    [SerializeField] private float stairDepth = 2f;
    [SerializeField] public GameObject stairPrefab;

    [Header("Prefabs")]
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject doorPrefab;
    [SerializeField] private GameObject doorwayPrefab;
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject ceilingPrefab;

    [Header("Minimap")]
    [Tooltip("Assign the FloorplanRenderer component. Leave null to skip minimap generation.")]
    [SerializeField] private FloorplanRenderer floorplanRenderer;
    [Tooltip("Assign the MinimapController component. Can be on any GameObject.")]
    [SerializeField] private MinimapController minimapController;

    [Header("Props & Materials")]
    [Tooltip("Assign the PropSpawner component. Leave null to skip prop generation.")]
    [SerializeField] private PropSpawner propSpawner;

    private List<BuildingRoom>    rooms     = new List<BuildingRoom>();
    private List<BuildingWall>    walls     = new List<BuildingWall>();
    private List<BuildingDoor>    doors     = new List<BuildingDoor>();
    private List<BuildingStairs>  stairs    = new List<BuildingStairs>();
    private List<BuildingSection> buildings = new List<BuildingSection>();

    // Parallel to rooms[] — floor/ceiling GOs per room for PropSpawner
    private List<(GameObject floor, GameObject ceiling)> roomGeometry
        = new List<(GameObject, GameObject)>();
    // Building parent Transforms by buildingIndex for PropSpawner
    private Dictionary<int, Transform> buildingParentMap = new Dictionary<int, Transform>();

    [Header("Player")]
    [Tooltip("For spawning")]
    [SerializeField] private Transform player;
    [SerializeField] private float playerHeightOffset = 1.1f;

    // (min(roomA,roomB), max(roomA,roomB)) ? list of wall indices for those two rooms
    private Dictionary<(int, int), List<int>> sharedWallLookup;

    private System.Random random;

    // ?????????????????????????????????????????????????????????????????????????
    //  ENTRY POINT
    // ?????????????????????????????????????????????????????????????????????????

    void Start() => GenerateBuilding();

    public void GenerateBuilding()
    {
        ClearBuilding();
        Debug.Log($"Starting building generation with seed: {seed}");
        random = new System.Random(seed);

        int calculatedFloors = Mathf.Max(1, Mathf.FloorToInt(floorHeight * numFloors));
        numFloors = Mathf.Min(numFloors, calculatedFloors);
        Debug.Log($"Generating {numFloors} floors, targeting ~{targetNumRooms} rooms total");

        // ?? Phase 1: generate room layout ???????????????????????????????????
        int buildingIndex = 0, buildingCountInRow = 0;
        Vector3 currentOffset = Vector3.zero;
        float maxDepthInRow = 0f;

        while (rooms.Count < targetNumRooms)
        {
            Vector3 sectionSize = new Vector3(
                (float)(minBuildingSize.x + (maxBuildingSize.x - minBuildingSize.x) * random.NextDouble()),
                floorHeight * numFloors,
                (float)(minBuildingSize.z + (maxBuildingSize.z - minBuildingSize.z) * random.NextDouble())
            );

            int roomsBefore = rooms.Count;
            GenerateBuildingSection(buildingIndex, currentOffset, sectionSize);
            Debug.Log($"Building {buildingIndex}: +{rooms.Count - roomsBefore} rooms at {currentOffset} (total {rooms.Count}/{targetNumRooms})");

            maxDepthInRow = Mathf.Max(maxDepthInRow, sectionSize.z);
            buildingCountInRow++;

            if (buildingCountInRow >= buildingsPerRow)
            {
                currentOffset.x = 0;
                currentOffset.z += maxDepthInRow + buildingSpacingZ;
                buildingCountInRow = 0;
                maxDepthInRow = 0f;
            }
            else
            {
                currentOffset.x += sectionSize.x + buildingSpacingX;
            }
            buildingIndex++;
        }
        Debug.Log($"\n? {rooms.Count} rooms across {buildingIndex} buildings");

        // ?? Phase 2: derive all walls from room adjacency (no duplicates) ???
        DeriveWallsFromRooms();
        Debug.Log($"? {walls.Count} walls derived from adjacency");

        // ?? Phase 3: place doors (stores direct wallIndex) ??????????????????
        ConnectRoomsWithinBuildings();
        ConnectAdjacentBuildings();
        Debug.Log($"? {doors.Count} doors placed");

        // ?? Phase 4: cut door openings in walls (O(1) per door) ?????????????
        SplitWallsForAllDoors();
        Debug.Log($"? {walls.Count} wall segments after splitting");

        // ?? Phase 5: stairs ??????????????????????????????????????????????????
        if (numFloors > 1) GenerateStairs();
        Debug.Log($"? {stairs.Count} stairs");

        // ?? Phase 6: instantiate ?????????????????????????????????????????????
        InstantiateGeometry();
        SpawnPlayerInRandomRoom();
        BuildMinimap();
        BuildProps();
        Debug.Log("Building generation complete!");
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  ROOM GENERATION  (BSP split � same logic as before, split-position bug fixed)
    // ?????????????????????????????????????????????????????????????????????????

    private void GenerateBuildingSection(int buildingIndex, Vector3 offset, Vector3 sectionSize)
    {
        var section = new BuildingSection
        {
            position = offset, size = sectionSize, buildingIndex = buildingIndex
        };
        buildings.Add(section);

        int before = rooms.Count;
        for (int f = 0; f < numFloors; f++)
            GenerateFloor(offset.y + f * floorHeight, f, buildingIndex, offset, sectionSize);
        section.roomsInSection = rooms.Count - before;
    }

    private void GenerateFloor(float floorY, int floorIndex, int buildingIndex,
                               Vector3 offset, Vector3 sectionSize)
    {
        var queue = new Queue<(Vector3 pos, Vector3 size)>();
        queue.Enqueue((new Vector3(offset.x, floorY, offset.z),
                       new Vector3(sectionSize.x, floorHeight, sectionSize.z)));

        for (int iter = 0; queue.Count > 0 && iter < 1000; iter++)
        {
            var (pos, sz) = queue.Dequeue();
            if (sz.x <= 0.1f || sz.z <= 0.1f) continue;

            float minTotal = minRoomSize * 2 + wallThickness * 2;
            bool canX = sz.x >= minTotal;
            bool canZ = sz.z >= minTotal;

            if (!canX && !canZ) { CreateRoom(pos, sz, floorIndex, buildingIndex); continue; }

            bool split = false;
            if (canX && canZ)
            {
                split = random.Next(2) == 0 ? TrySplitX(pos, sz, queue) : TrySplitZ(pos, sz, queue);
                if (!split) split = TrySplitX(pos, sz, queue) || TrySplitZ(pos, sz, queue);
            }
            else if (canX) split = TrySplitX(pos, sz, queue);
            else            split = TrySplitZ(pos, sz, queue);

            if (!split) CreateRoom(pos, sz, floorIndex, buildingIndex);
        }
    }

    // Returns a split offset in [0, length] relative to the cell's own origin.
    // BUG FIX: the original returned a value relative to world 0, breaking rooms at non-zero offsets.
    private float GetRelativeSplitOffset(float length)
    {
        float lo = Mathf.Max(minRoomSize + wallThickness, maxRoomSize);
        float hi = Mathf.Min(length - minRoomSize - wallThickness, length - maxRoomSize);
        if (hi <= lo) return -1f;
        return (float)(lo + (hi - lo) * random.NextDouble());
    }

    private bool TrySplitX(Vector3 pos, Vector3 sz, Queue<(Vector3, Vector3)> q)
    {
        float offset = GetRelativeSplitOffset(sz.x);
        if (offset < 0) return false;
        float splitX = pos.x + offset;
        float lw = splitX - pos.x, rw = pos.x + sz.x - splitX;
        if (lw <= 0.1f || rw <= 0.1f) return false;
        q.Enqueue((pos,                                       new Vector3(lw, sz.y, sz.z)));
        q.Enqueue((new Vector3(splitX, pos.y, pos.z),         new Vector3(rw, sz.y, sz.z)));
        return true;
    }

    private bool TrySplitZ(Vector3 pos, Vector3 sz, Queue<(Vector3, Vector3)> q)
    {
        float offset = GetRelativeSplitOffset(sz.z);
        if (offset < 0) return false;
        float splitZ = pos.z + offset;
        float fd = splitZ - pos.z, bd = pos.z + sz.z - splitZ;
        if (fd <= 0.1f || bd <= 0.1f) return false;
        q.Enqueue((pos,                                       new Vector3(sz.x, sz.y, fd)));
        q.Enqueue((new Vector3(pos.x, pos.y, splitZ),         new Vector3(sz.x, sz.y, bd)));
        return true;
    }

    private void CreateRoom(Vector3 pos, Vector3 sz, int floorIndex, int buildingIndex)
    {
        if (sz.x <= 0.1f || sz.z <= 0.1f) return;
        rooms.Add(new BuildingRoom
        {
            position = pos, size = sz,
            roomType = ClassifyRoom(sz),
            floorIndex = floorIndex, buildingIndex = buildingIndex
        });
    }

    private RoomType ClassifyRoom(Vector3 sz)
    {
        float mn = Mathf.Min(sz.x, sz.z), mx = Mathf.Max(sz.x, sz.z), ratio = mx / mn;
        if (mn < 1.5f && ratio > 3f)         return RoomType.Hallway;
        if (mn < 2.5f && mx < 4f)             return RoomType.Bathroom;
        if (mn > 2f  && mx < 7f && ratio < 2f) return RoomType.Kitchen;
        if (mn > 5f)                           return RoomType.LivingRoom;
        if (mn > 3f  && mx < 8f)               return RoomType.Bedroom;
        return RoomType.General;
    }

    private void SpawnPlayerInRandomRoom()
    {
        if (rooms.Count == 0 || player == null)
            return;

        int index = random.Next(rooms.Count);
        BuildingRoom room = rooms[index];

        Vector3 spawnPos = new Vector3(
            room.position.x + room.size.x / 2f,
            room.position.y + playerHeightOffset,
            room.position.z + room.size.z / 2f
        );

        player.position = spawnPos;
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  WALL DERIVATION  (the core refactor)
    //
    //  Instead of 4 walls per room (doubles every shared wall), we:
    //   1. Iterate all room pairs � if adjacent, create ONE shared wall.
    //   2. For each room side not fully covered by shared walls, create an exterior wall.
    //
    //  Result: no duplicate walls, each wall knows its two neighbouring rooms.
    // ?????????????????????????????????????????????????????????????????????????

    private void DeriveWallsFromRooms()
    {
        sharedWallLookup = new Dictionary<(int, int), List<int>>();

        // side coverage: (roomIdx, side 0=x- 1=x+ 2=z- 3=z+) ? covered intervals
        var coverage = new Dictionary<(int, int), List<(float, float)>>();
        for (int i = 0; i < rooms.Count; i++)
            for (int s = 0; s < 4; s++)
                coverage[(i, s)] = new List<(float, float)>();

        const float tol = 0.05f;

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                var a = rooms[i]; var b = rooms[j];

                // a-right touches b-left
                if (Mathf.Abs((a.position.x + a.size.x) - b.position.x) < tol)
                    TryMakeSharedWallX(i, j, a, b, coverage);
                // b-right touches a-left
                else if (Mathf.Abs((b.position.x + b.size.x) - a.position.x) < tol)
                    TryMakeSharedWallX(j, i, b, a, coverage);
                // a-back touches b-front
                else if (Mathf.Abs((a.position.z + a.size.z) - b.position.z) < tol)
                    TryMakeSharedWallZ(i, j, a, b, coverage);
                // b-back touches a-front
                else if (Mathf.Abs((b.position.z + b.size.z) - a.position.z) < tol)
                    TryMakeSharedWallZ(j, i, b, a, coverage);
            }
        }

        // Exterior walls for sides that have no (or partial) neighbour coverage
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            float halfY = r.position.y + r.size.y * 0.5f;
            float h = r.size.y;
            float zMin = r.position.z, zMax = r.position.z + r.size.z;
            float xMin = r.position.x, xMax = r.position.x + r.size.x;

            // x- side  (facingX wall, runs along Z)
            foreach (var (lo, hi) in UncoveredSegments(coverage[(i, 0)], zMin, zMax))
                walls.Add(MakeWall(new Vector3(xMin, halfY, (lo + hi) * 0.5f),
                                   new Vector3(wallThickness, h, hi - lo), facingX: true, i, -1));

            // x+ side
            foreach (var (lo, hi) in UncoveredSegments(coverage[(i, 1)], zMin, zMax))
                walls.Add(MakeWall(new Vector3(xMax, halfY, (lo + hi) * 0.5f),
                                   new Vector3(wallThickness, h, hi - lo), facingX: true, i, -1));

            // z- side  (facingX=false wall, runs along X)
            foreach (var (lo, hi) in UncoveredSegments(coverage[(i, 2)], xMin, xMax))
                walls.Add(MakeWall(new Vector3((lo + hi) * 0.5f, halfY, zMin),
                                   new Vector3(hi - lo, h, wallThickness), facingX: false, i, -1));

            // z+ side
            foreach (var (lo, hi) in UncoveredSegments(coverage[(i, 3)], xMin, xMax))
                walls.Add(MakeWall(new Vector3((lo + hi) * 0.5f, halfY, zMax),
                                   new Vector3(hi - lo, h, wallThickness), facingX: false, i, -1));
        }
    }

    // leftIdx's right edge touches rightIdx's left edge
    private void TryMakeSharedWallX(int leftIdx, int rightIdx,
                                    BuildingRoom left, BuildingRoom right,
                                    Dictionary<(int, int), List<(float, float)>> coverage)
    {
        float z1 = Mathf.Max(left.position.z, right.position.z);
        float z2 = Mathf.Min(left.position.z + left.size.z, right.position.z + right.size.z);
        if (z2 - z1 < 0.05f) return;

        int wIdx = walls.Count;
        walls.Add(MakeWall(
            new Vector3(left.position.x + left.size.x, left.position.y + left.size.y * 0.5f, (z1 + z2) * 0.5f),
            new Vector3(wallThickness, left.size.y, z2 - z1),
            facingX: true, leftIdx, rightIdx));

        RegisterSharedWall(leftIdx, rightIdx, wIdx);
        coverage[(leftIdx, 1)].Add((z1, z2));
        coverage[(rightIdx, 0)].Add((z1, z2));
    }

    // frontIdx's back edge touches backIdx's front edge
    private void TryMakeSharedWallZ(int frontIdx, int backIdx,
                                    BuildingRoom front, BuildingRoom back,
                                    Dictionary<(int, int), List<(float, float)>> coverage)
    {
        float x1 = Mathf.Max(front.position.x, back.position.x);
        float x2 = Mathf.Min(front.position.x + front.size.x, back.position.x + back.size.x);
        if (x2 - x1 < 0.05f) return;

        int wIdx = walls.Count;
        walls.Add(MakeWall(
            new Vector3((x1 + x2) * 0.5f, front.position.y + front.size.y * 0.5f, front.position.z + front.size.z),
            new Vector3(x2 - x1, front.size.y, wallThickness),
            facingX: false, frontIdx, backIdx));

        RegisterSharedWall(frontIdx, backIdx, wIdx);
        coverage[(frontIdx, 3)].Add((x1, x2));
        coverage[(backIdx, 2)].Add((x1, x2));
    }

    private BuildingWall MakeWall(Vector3 pos, Vector3 sz, bool facingX, int roomA, int roomB) =>
        new BuildingWall { position = pos, size = sz, facingX = facingX, roomA = roomA, roomB = roomB };

    private void RegisterSharedWall(int a, int b, int wallIdx)
    {
        var key = (Math.Min(a, b), Math.Max(a, b));
        if (!sharedWallLookup.ContainsKey(key)) sharedWallLookup[key] = new List<int>();
        sharedWallLookup[key].Add(wallIdx);
    }

    // Returns segments of [rangeMin, rangeMax] NOT covered by any interval in `covered`.
    private static List<(float, float)> UncoveredSegments(
        List<(float min, float max)> covered, float rangeMin, float rangeMax)
    {
        var result = new List<(float, float)>();
        float cur = rangeMin;
        foreach (var (mn, mx) in covered.OrderBy(c => c.min))
        {
            if (mn > cur + 0.05f) result.Add((cur, mn));
            cur = Mathf.Max(cur, mx);
        }
        if (cur < rangeMax - 0.05f) result.Add((cur, rangeMax));
        return result;
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  DOOR PLACEMENT
    // ?????????????????????????????????????????????????????????????????????????

    private void ConnectRoomsWithinBuildings()
    {
        int count = 0;
        // Group by (floor, building), pass room indices directly � no IndexOf needed
        var groups = rooms
            .Select((r, i) => (r, i))
            .GroupBy(x => (x.r.floorIndex, x.r.buildingIndex));

        foreach (var group in groups)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (TryAddDoor(list[i].i, list[j].i)) count++;
        }
        Debug.Log($"Added {count} internal doors");
    }

    private void ConnectAdjacentBuildings()
    {
        int count = 0;
        for (int i = 0; i < buildings.Count; i++)
            for (int j = i + 1; j < buildings.Count; j++)
                count += TryConnectBuildingPair(i, j);
        Debug.Log($"Added {count} doors between adjacent buildings");
    }

    private int TryConnectBuildingPair(int aIdx, int bIdx)
    {
        var a = buildings[aIdx]; var b = buildings[bIdx];
        int doors = 0;

        if (Mathf.Abs((a.position.x + a.size.x) - b.position.x) < 0.1f)
            doors += TryConnectBuildingsAlongAxis(aIdx, bIdx, isXAxis: true)  ? 1 : 0;
        else if (Mathf.Abs((b.position.x + b.size.x) - a.position.x) < 0.1f)
            doors += TryConnectBuildingsAlongAxis(bIdx, aIdx, isXAxis: true)  ? 1 : 0;

        if (Mathf.Abs((a.position.z + a.size.z) - b.position.z) < 0.1f)
            doors += TryConnectBuildingsAlongAxis(aIdx, bIdx, isXAxis: false) ? 1 : 0;
        else if (Mathf.Abs((b.position.z + b.size.z) - a.position.z) < 0.1f)
            doors += TryConnectBuildingsAlongAxis(bIdx, aIdx, isXAxis: false) ? 1 : 0;

        return doors;
    }

    // nearIdx's far edge (right/back) touches farIdx's near edge (left/front)
    private bool TryConnectBuildingsAlongAxis(int nearIdx, int farIdx, bool isXAxis)
    {
        float boundary = isXAxis
            ? buildings[nearIdx].position.x + buildings[nearIdx].size.x
            : buildings[nearIdx].position.z + buildings[nearIdx].size.z;

        // Rooms whose edge touches the shared boundary, ground floor only
        var edgesNear = rooms
            .Select((r, i) => (r, i))
            .Where(x => x.r.buildingIndex == nearIdx && x.r.floorIndex == 0 &&
                        (isXAxis
                            ? Mathf.Abs((x.r.position.x + x.r.size.x) - boundary) < 0.1f
                            : Mathf.Abs((x.r.position.z + x.r.size.z) - boundary) < 0.1f))
            .ToList();

        float farEdge = isXAxis ? buildings[farIdx].position.x : buildings[farIdx].position.z;
        var edgesFar = rooms
            .Select((r, i) => (r, i))
            .Where(x => x.r.buildingIndex == farIdx && x.r.floorIndex == 0 &&
                        (isXAxis
                            ? Mathf.Abs(x.r.position.x - farEdge) < 0.1f
                            : Mathf.Abs(x.r.position.z - farEdge) < 0.1f))
            .ToList();

        // Collect overlapping pairs wide enough for a door
        var candidates = new List<(int, int)>();
        foreach (var (rA, iA) in edgesNear)
        {
            foreach (var (rB, iB) in edgesFar)
            {
                float overlapLen = isXAxis
                    ? Mathf.Min(rA.position.z + rA.size.z, rB.position.z + rB.size.z)
                      - Mathf.Max(rA.position.z, rB.position.z)
                    : Mathf.Min(rA.position.x + rA.size.x, rB.position.x + rB.size.x)
                      - Mathf.Max(rA.position.x, rB.position.x);
                if (overlapLen >= doorWidth + 0.2f)
                    candidates.Add((iA, iB));
            }
        }

        if (candidates.Count == 0) return false;

        var (roomA, roomB) = candidates[random.Next(candidates.Count)];
        bool placed = TryAddDoor(roomA, roomB);
        if (placed)
            Debug.Log($"? Connected Building {nearIdx} ? Building {farIdx} ({(isXAxis ? "X" : "Z")} axis)");
        return placed;
    }

    // Core door-placement method. Finds the shared wall directly via lookup, no searching.
    private bool TryAddDoor(int r1Idx, int r2Idx)
    {
        var key = (Math.Min(r1Idx, r2Idx), Math.Max(r1Idx, r2Idx));
        if (!sharedWallLookup.TryGetValue(key, out var wallIndices)) return false;

        foreach (int wIdx in wallIndices)
        {
            var wall = walls[wIdx];
            float wallLen = wall.facingX ? wall.size.z : wall.size.x;
            if (wallLen < doorWidth + 0.2f) continue;

            float doorY = rooms[r1Idx].position.y + 1f;
            // Door is centred on the wall
            Vector3 doorPos  = new Vector3(wall.position.x, doorY, wall.position.z);
            Vector3 doorSize = wall.facingX
                ? new Vector3(wallThickness * 2f, doorHeight, doorWidth)   // gap along Z
                : new Vector3(doorWidth, doorHeight, wallThickness * 2f);  // gap along X

            int doorIdx = doors.Count;
            doors.Add(new BuildingDoor
            {
                position    = doorPos,
                size        = doorSize,
                roomA       = r1Idx,
                roomB       = r2Idx,
                wallIndex   = wIdx,
                wallFacingX = wall.facingX
            });

            rooms[r1Idx].connectedDoorIndices.Add(doorIdx);
            rooms[r2Idx].connectedDoorIndices.Add(doorIdx);
            return true;
        }
        return false;
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  WALL SPLITTING  (O(1) per door � direct wallIndex reference)
    //
    //  Process doors in descending wallIndex order so that RemoveAt on a higher
    //  index never invalidates a lower index we still need to process.
    // ?????????????????????????????????????????????????????????????????????????

    private void SplitWallsForAllDoors()
    {
        foreach (var door in doors.OrderByDescending(d => d.wallIndex))
        {
            if (door.wallIndex < 0 || door.wallIndex >= walls.Count) continue;
            SplitWallAtDoor(door);
        }
    }

    private void SplitWallAtDoor(BuildingDoor door)
    {
        var wall = walls[door.wallIndex];

        // Transom: the solid strip of wall above the door opening.
        //   wallBottomY      = bottom of the full wall
        //   wallBottomY + doorHeight = top of the door opening
        //   wallTopY         = top of the full wall
        float wallBottomY   = wall.position.y - wall.size.y * 0.5f;
        float wallTopY      = wall.position.y + wall.size.y * 0.5f;
        float transomHeight = wallTopY - (wallBottomY + doorHeight);
        float transomCenterY = wallTopY - transomHeight * 0.5f;
        bool hasTransom = transomHeight > 0.05f;

        if (door.wallFacingX)
        {
            // Wall runs along Z � cut a doorWidth slot on the Z axis
            float wallZMin = wall.position.z - wall.size.z * 0.5f;
            float wallZMax = wall.position.z + wall.size.z * 0.5f;
            float gapMin   = door.position.z  - doorWidth * 0.5f;
            float gapMax   = door.position.z  + doorWidth * 0.5f;

            // Bottom of wall cut to doorHeight only (the opening height, not the full wall height)
            float openingHeight       = doorHeight;
            float openingCenterY      = wallBottomY + openingHeight * 0.5f;
            Vector3 openingSize       = new Vector3(wall.size.x, openingHeight, wall.size.z);

            walls.RemoveAt(door.wallIndex);

            // Left segment (full height)
            if (gapMin > wallZMin + 0.05f)
                walls.Add(WallSegment(wall,
                    new Vector3(wall.position.x, wall.position.y, wallZMin + (gapMin - wallZMin) * 0.5f),
                    new Vector3(wall.size.x, wall.size.y, gapMin - wallZMin)));

            // Right segment (full height)
            if (gapMax < wallZMax - 0.05f)
                walls.Add(WallSegment(wall,
                    new Vector3(wall.position.x, wall.position.y, gapMax + (wallZMax - gapMax) * 0.5f),
                    new Vector3(wall.size.x, wall.size.y, wallZMax - gapMax)));

            // Transom (above door, same Z span as the opening)
            if (hasTransom)
                walls.Add(WallSegment(wall,
                    new Vector3(wall.position.x, transomCenterY, door.position.z),
                    new Vector3(wall.size.x, transomHeight, doorWidth)));
        }
        else
        {
            // Wall runs along X � cut a doorWidth slot on the X axis
            float wallXMin = wall.position.x - wall.size.x * 0.5f;
            float wallXMax = wall.position.x + wall.size.x * 0.5f;
            float gapMin   = door.position.x  - doorWidth * 0.5f;
            float gapMax   = door.position.x  + doorWidth * 0.5f;

            walls.RemoveAt(door.wallIndex);

            // Left segment (full height)
            if (gapMin > wallXMin + 0.05f)
                walls.Add(WallSegment(wall,
                    new Vector3(wallXMin + (gapMin - wallXMin) * 0.5f, wall.position.y, wall.position.z),
                    new Vector3(gapMin - wallXMin, wall.size.y, wall.size.z)));

            // Right segment (full height)
            if (gapMax < wallXMax - 0.05f)
                walls.Add(WallSegment(wall,
                    new Vector3(gapMax + (wallXMax - gapMax) * 0.5f, wall.position.y, wall.position.z),
                    new Vector3(wallXMax - gapMax, wall.size.y, wall.size.z)));

            // Transom (above door, same X span as the opening)
            if (hasTransom)
                walls.Add(WallSegment(wall,
                    new Vector3(door.position.x, transomCenterY, wall.position.z),
                    new Vector3(doorWidth, transomHeight, wall.size.z)));
        }
    }

    private BuildingWall WallSegment(BuildingWall src, Vector3 pos, Vector3 sz) =>
        new BuildingWall { position = pos, size = sz, facingX = src.facingX, roomA = src.roomA, roomB = src.roomB };

    // ?????????????????????????????????????????????????????????????????????????
    //  STAIRS
    // ?????????????????????????????????????????????????????????????????????????

    private void GenerateStairs()
    {
        foreach (var building in buildings)
        {
            if (building.roomsInSection == 0) continue;
            var bRooms  = rooms.Where(r => r.buildingIndex == building.buildingIndex).ToList();
            var floors  = bRooms.Select(r => r.floorIndex).Distinct().OrderBy(f => f).ToList();

            for (int i = 0; i < floors.Count - 1; i++)
            {
                var floorRooms = bRooms.Where(r => r.floorIndex == floors[i]).ToList();
                var stairRoom  = FindBestStairRoom(floorRooms);
                if (stairRoom == null) continue;

                float pad = 0.2f;
                float sx = Mathf.Clamp(stairRoom.position.x + pad,
                                       stairRoom.position.x,
                                       stairRoom.position.x + stairRoom.size.x - stairDepth - pad);
                float sz = Mathf.Clamp(stairRoom.position.z + pad,
                                       stairRoom.position.z,
                                       stairRoom.position.z + stairRoom.size.z - stairWidth - pad);
                bool dim = random.Next(2) == 0;

                stairs.Add(new BuildingStairs
                {
                    position = new Vector3(sx, stairRoom.position.y, sz),
                    size     = dim ? new Vector3(stairDepth, floorHeight, stairWidth)
                                   : new Vector3(stairWidth, floorHeight, stairDepth),
                    floorA   = floors[i],
                    floorB   = floors[i + 1],
                    dim      = dim
                });
            }
        }
    }

    private BuildingRoom FindBestStairRoom(List<BuildingRoom> list)
    {
        var hall  = list.Where(r => r.roomType == RoomType.Hallway).ToList();
        if (hall.Count  > 0) return hall[random.Next(hall.Count)];
        var large = list.Where(r => r.size.x > stairDepth + 1 && r.size.z > stairWidth + 1).ToList();
        if (large.Count > 0) return large[random.Next(large.Count)];
        return list.Count > 0 ? list[random.Next(list.Count)] : null;
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  INSTANTIATION
    // ?????????????????????????????????????????????????????????????????????????

    private void InstantiateGeometry()
    {
        Transform parent = transform;

        buildingParentMap.Clear();
        var buildingParents = buildingParentMap; // alias so rest of method unchanged
        foreach (var b in buildings)
        {
            var go = new GameObject($"Building_{b.buildingIndex} ({b.roomsInSection} rooms)");
            go.transform.parent   = parent;
            go.transform.position = b.position;
            buildingParents[b.buildingIndex] = go.transform;
        }

        roomGeometry.Clear();
        foreach (var room in rooms)
        {
            var bp = buildingParents[room.buildingIndex];

            var floorPos = room.position + new Vector3(room.size.x * 0.5f, floorCeilThickness * 0.5f, room.size.z * 0.5f);
            var floor = Instantiate(floorPrefab, floorPos, Quaternion.identity, bp);
            floor.name = $"Floor_{room.roomType}";
            floor.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);

            var ceilPos = new Vector3(
                room.position.x + room.size.x * 0.5f,
                room.position.y + room.size.y - floorCeilThickness * 0.5f,
                room.position.z + room.size.z * 0.5f);
            var ceil = Instantiate(ceilingPrefab, ceilPos, Quaternion.identity, bp);
            ceil.name = $"Ceiling_{room.roomType}";
            ceil.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);

            // Store for PropSpawner
            roomGeometry.Add((floor, ceil));
        }

        foreach (var wall in walls)
        {
            var w = Instantiate(wallPrefab, wall.position, Quaternion.identity, parent);
            w.transform.localScale = wall.size;
        }

        foreach (var door in doors)
        {
            var d = Instantiate(doorPrefab, door.position, Quaternion.identity, parent);
            d.transform.localScale = door.size;
        }

        int stairCount = 0;
        foreach (var s in stairs)
        {
            var go = stairPrefab != null
                ? Instantiate(stairPrefab, s.position, Quaternion.identity, parent)
                : CreateDefaultStairs(s, parent);
            go.name = $"Stairs_{stairCount++}";
            go.transform.localScale = s.size;
        }
    }

    private GameObject CreateDefaultStairs(BuildingStairs s, Transform parent)
    {
        var go = new GameObject("Stairs");
        go.transform.parent   = parent;
        go.transform.position = s.position;
        go.AddComponent<MeshFilter>().mesh = CreateStairMesh();
        go.AddComponent<MeshRenderer>().material =
            new Material(Shader.Find("Standard")) { color = Color.gray };
        go.AddComponent<BoxCollider>();
        return go;
    }

    private Mesh CreateStairMesh()
    {
        var mesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(0,1,0),
                new Vector3(0,0,1), new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1)
            },
            triangles = new[]
            {
                0,2,1, 0,3,2,  4,5,6, 4,6,7,
                0,1,5, 0,5,4,  2,3,7, 2,7,6,
                0,4,7, 0,7,3,  1,2,6, 1,6,5
            }
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    // ?????????????????????????????????????????????????????????????????????????
    //  CLEANUP
    // ?????????????????????????????????????????????????????????????????????????

    private void ClearBuilding()
    {
        rooms.Clear(); walls.Clear(); doors.Clear(); stairs.Clear(); buildings.Clear();
        roomGeometry.Clear(); buildingParentMap.Clear();
        sharedWallLookup = null;

        // Destroy previously generated geometry
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
    }

    public void GenerateBuildingFromSeed(int newSeed)
    {
        seed = newSeed;
        GenerateBuilding();
    }
    public bool TryGetSafeSpawnPoint(Vector3 requestedPos, out Vector3 safePos, int preferredFloor = 0, float margin = 0.35f)
    {
        safePos = requestedPos;
        if (rooms == null || rooms.Count == 0) return false;

        var bestIndex = -1;
        var bestScore = float.MaxValue;
        var foundPreferredFloor = false;

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            if (room.floorIndex == preferredFloor) foundPreferredFloor = true;
        }

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            if (foundPreferredFloor && room.floorIndex != preferredFloor) continue;

            var minX = room.position.x + margin;
            var maxX = room.position.x + room.size.x - margin;
            var minZ = room.position.z + margin;
            var maxZ = room.position.z + room.size.z - margin;

            if (minX > maxX) { minX = room.position.x; maxX = room.position.x + room.size.x; }
            if (minZ > maxZ) { minZ = room.position.z; maxZ = room.position.z + room.size.z; }

            var clampedX = Mathf.Clamp(requestedPos.x, minX, maxX);
            var clampedZ = Mathf.Clamp(requestedPos.z, minZ, maxZ);
            var dx = requestedPos.x - clampedX;
            var dz = requestedPos.z - clampedZ;
            var score = dx * dx + dz * dz;

            if (score < bestScore) { bestScore = score; bestIndex = i; }
        }

        if (bestIndex < 0) return false;

        var bestRoom = rooms[bestIndex];
        var rMinX = bestRoom.position.x + margin;
        var rMaxX = bestRoom.position.x + bestRoom.size.x - margin;
        var rMinZ = bestRoom.position.z + margin;
        var rMaxZ = bestRoom.position.z + bestRoom.size.z - margin;

        if (rMinX > rMaxX) { rMinX = bestRoom.position.x; rMaxX = bestRoom.position.x + bestRoom.size.x; }
        if (rMinZ > rMaxZ) { rMinZ = bestRoom.position.z; rMaxZ = bestRoom.position.z + bestRoom.size.z; }

        safePos = new Vector3(
            Mathf.Clamp(requestedPos.x, rMinX, rMaxX),
            bestRoom.position.y + 0.5f,
            Mathf.Clamp(requestedPos.z, rMinZ, rMaxZ)
        );
        return true;
    }
    public bool TryGetSafeSpawnPoint(Vector3 requestedPos, out Vector3 safePos, int preferredFloor = 0, float margin = 0.35f, bool allowDoors = false)
    {
        safePos = requestedPos;
        if (rooms == null || rooms.Count == 0) return false;
        if (allowDoors && doors != null && doors.Count > 0)
        {
            var bestDoorScore = float.MaxValue;
            var bestDoorPos = requestedPos;
            var bestDoorY = requestedPos.y;

            for (var i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                if (door == null) continue;

                var floorIdx = preferredFloor;
                if (door.roomA >= 0 && door.roomA < rooms.Count && rooms[door.roomA] != null)
                {
                    floorIdx = rooms[door.roomA].floorIndex;
                }
                else if (door.roomB >= 0 && door.roomB < rooms.Count && rooms[door.roomB] != null)
                {
                    floorIdx = rooms[door.roomB].floorIndex;
                }

                if (preferredFloor >= 0 && floorIdx != preferredFloor) continue;

                var halfX = Mathf.Max(0.05f, door.size.x * 0.5f);
                var halfZ = Mathf.Max(0.05f, door.size.z * 0.5f);

                // Expand walkable zone around door so movement can cross threshold from both sides.
                var approachDepth = Mathf.Max(0.9f, margin * 2.5f);
                var sidePadding = Mathf.Max(0.25f, margin * 0.75f);
                if (door.size.x < door.size.z)
                {
                    // Thin on X axis => wall thickness along X, opening across Z.
                    halfX += approachDepth;
                    halfZ += sidePadding;
                }
                else
                {
                    // Thin on Z axis => wall thickness along Z, opening across X.
                    halfZ += approachDepth;
                    halfX += sidePadding;
                }

                var clampedX = Mathf.Clamp(requestedPos.x, door.position.x - halfX, door.position.x + halfX);
                var clampedZ = Mathf.Clamp(requestedPos.z, door.position.z - halfZ, door.position.z + halfZ);
                var dx = requestedPos.x - clampedX;
                var dz = requestedPos.z - clampedZ;
                var score = dx * dx + dz * dz;

                if (score < bestDoorScore)
                {
                    bestDoorScore = score;
                    bestDoorPos = new Vector3(clampedX, requestedPos.y, clampedZ);
                    if (door.roomA >= 0 && door.roomA < rooms.Count && rooms[door.roomA] != null)
                    {
                        bestDoorY = rooms[door.roomA].position.y + 0.5f;
                    }
                    else if (door.roomB >= 0 && door.roomB < rooms.Count && rooms[door.roomB] != null)
                    {
                        bestDoorY = rooms[door.roomB].position.y + 0.5f;
                    }
                    else
                    {
                        bestDoorY = floorIdx * floorHeight + 0.5f;
                    }
                }
            }

            var doorCaptureRadius = Mathf.Max(1.6f, margin * 4f);
            if (bestDoorScore <= doorCaptureRadius * doorCaptureRadius)
            {
                safePos = new Vector3(bestDoorPos.x, bestDoorY, bestDoorPos.z);
                return true;
            }
        }

        var bestIndex = -1;
        var bestScore = float.MaxValue;
        var foundPreferredFloor = false;

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            if (room.floorIndex == preferredFloor) foundPreferredFloor = true;
        }

        for (var i = 0; i < rooms.Count; i++)
        {
            var room = rooms[i];
            if (room == null) continue;
            if (foundPreferredFloor && room.floorIndex != preferredFloor) continue;

            var minX = room.position.x + margin;
            var maxX = room.position.x + room.size.x - margin;
            var minZ = room.position.z + margin;
            var maxZ = room.position.z + room.size.z - margin;

            if (minX > maxX)
            {
                minX = room.position.x;
                maxX = room.position.x + room.size.x;
            }
            if (minZ > maxZ)
            {
                minZ = room.position.z;
                maxZ = room.position.z + room.size.z;
            }

            var clampedX = Mathf.Clamp(requestedPos.x, minX, maxX);
            var clampedZ = Mathf.Clamp(requestedPos.z, minZ, maxZ);
            var dx = requestedPos.x - clampedX;
            var dz = requestedPos.z - clampedZ;
            var score = dx * dx + dz * dz;

            if (score < bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;

        var bestRoom = rooms[bestIndex];
        var rMinX = bestRoom.position.x + margin;
        var rMaxX = bestRoom.position.x + bestRoom.size.x - margin;
        var rMinZ = bestRoom.position.z + margin;
        var rMaxZ = bestRoom.position.z + bestRoom.size.z - margin;

        if (rMinX > rMaxX)
        {
            rMinX = bestRoom.position.x;
            rMaxX = bestRoom.position.x + bestRoom.size.x;
        }
        if (rMinZ > rMaxZ)
        {
            rMinZ = bestRoom.position.z;
            rMaxZ = bestRoom.position.z + bestRoom.size.z;
        }

        safePos = new Vector3(
            Mathf.Clamp(requestedPos.x, rMinX, rMaxX),
            bestRoom.position.y + 0.5f,
            Mathf.Clamp(requestedPos.z, rMinZ, rMaxZ)
        );
        return true;
    }
    
    public int GetContainingRoomIndex(Vector3 worldPos, int preferredFloor = 0)
    {
        if (rooms == null || rooms.Count == 0) return -1;

        var hasPreferredFloor = false;
        for (var i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r != null && r.floorIndex == preferredFloor)
            {
                hasPreferredFloor = true;
                break;
            }
        }

        var bestIdx = -1;
        var bestDist2 = float.MaxValue;
        for (var i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r == null) continue;
            if (hasPreferredFloor && r.floorIndex != preferredFloor) continue;

            var minX = r.position.x;
            var maxX = r.position.x + r.size.x;
            var minZ = r.position.z;
            var maxZ = r.position.z + r.size.z;
            var inside = worldPos.x >= minX && worldPos.x <= maxX && worldPos.z >= minZ && worldPos.z <= maxZ;
            if (!inside) continue;

            var cx = r.position.x + r.size.x * 0.5f;
            var cz = r.position.z + r.size.z * 0.5f;
            var dx = worldPos.x - cx;
            var dz = worldPos.z - cz;
            var d2 = dx * dx + dz * dz;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0) return bestIdx;

        for (var i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r == null) continue;
            if (hasPreferredFloor && r.floorIndex != preferredFloor) continue;

            var cx = r.position.x + r.size.x * 0.5f;
            var cz = r.position.z + r.size.z * 0.5f;
            var dx = worldPos.x - cx;
            var dz = worldPos.z - cz;
            var d2 = dx * dx + dz * dz;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    public bool TryBuildDoorWaypointPath(Vector3 fromWorldPos, Vector3 toWorldPos, List<Vector3> outWaypoints, int preferredFloor = 0, float margin = 0.35f)
    {
        if (outWaypoints == null) return false;
        outWaypoints.Clear();

        if (rooms == null || doors == null || rooms.Count == 0 || doors.Count == 0) return false;

        var startRoom = GetContainingRoomIndex(fromWorldPos, preferredFloor);
        var endRoom = GetContainingRoomIndex(toWorldPos, preferredFloor);
        if (startRoom < 0 || endRoom < 0) return false;
        if (startRoom == endRoom) return true;

        var q = new Queue<int>();
        var visited = new HashSet<int>();
        var prevRoom = new Dictionary<int, int>();
        var prevDoor = new Dictionary<int, int>();

        visited.Add(startRoom);
        prevRoom[startRoom] = -1;
        q.Enqueue(startRoom);

        var found = false;
        while (q.Count > 0)
        {
            var r = q.Dequeue();
            if (r == endRoom)
            {
                found = true;
                break;
            }

            var room = rooms[r];
            if (room == null || room.connectedDoorIndices == null) continue;

            for (var i = 0; i < room.connectedDoorIndices.Count; i++)
            {
                var doorIdx = room.connectedDoorIndices[i];
                if (doorIdx < 0 || doorIdx >= doors.Count) continue;
                var d = doors[doorIdx];
                if (d == null) continue;

                var next = -1;
                if (d.roomA == r) next = d.roomB;
                else if (d.roomB == r) next = d.roomA;
                if (next < 0 || next >= rooms.Count) continue;
                if (rooms[next] == null) continue;
                if (rooms[next].floorIndex != rooms[startRoom].floorIndex) continue;
                if (visited.Contains(next)) continue;

                visited.Add(next);
                prevRoom[next] = r;
                prevDoor[next] = doorIdx;
                q.Enqueue(next);
            }
        }

        if (!found) return false;

        var doorPath = new List<int>();
        var cur = endRoom;
        while (cur != startRoom)
        {
            if (!prevDoor.TryGetValue(cur, out var dIdx)) break;
            doorPath.Add(dIdx);
            if (!prevRoom.TryGetValue(cur, out cur)) break;
            if (cur < 0) break;
        }

        doorPath.Reverse();
        for (var i = 0; i < doorPath.Count; i++)
        {
            var dIdx = doorPath[i];
            if (dIdx < 0 || dIdx >= doors.Count) continue;
            var d = doors[dIdx];
            if (d == null) continue;

            var doorPos = d.position;
            var requested = new Vector3(doorPos.x, fromWorldPos.y, doorPos.z);
            if (TryGetSafeSpawnPoint(requested, out var safe, preferredFloor, margin, allowDoors: true))
            {
                outWaypoints.Add(safe);
            }
            else
            {
                outWaypoints.Add(requested);
            }
        }

        return true;
    }
    
    public bool TryBuildDoorTransitionPath(Vector3 fromWorldPos, Vector3 toWorldPos, List<Vector3> outWaypoints, int preferredFloor = 0, float approachDistance = 0.9f, int fallbackNearestDoors = 3, float margin = 0.35f, int blockedDoorIdx = -1)
    {
        if (outWaypoints == null) return false;
        outWaypoints.Clear();

        if (rooms == null || doors == null || rooms.Count == 0 || doors.Count == 0) return false;

        int GetDoorFloor(BuildingDoor d)
        {
            if (d.roomA >= 0 && d.roomA < rooms.Count && rooms[d.roomA] != null) return rooms[d.roomA].floorIndex;
            if (d.roomB >= 0 && d.roomB < rooms.Count && rooms[d.roomB] != null) return rooms[d.roomB].floorIndex;
            return preferredFloor;
        }

        Vector3 RoomCenter2D(int roomIdx)
        {
            if (roomIdx < 0 || roomIdx >= rooms.Count || rooms[roomIdx] == null) return Vector3.zero;
            var r = rooms[roomIdx];
            return new Vector3(r.position.x + r.size.x * 0.5f, 0f, r.position.z + r.size.z * 0.5f);
        }

        bool PushPoint(Vector3 p)
        {
            if (TryGetSafeSpawnPoint(p, out var safe, preferredFloor, margin, allowDoors: true))
            {
                if (outWaypoints.Count == 0 || Vector3.Distance(outWaypoints[outWaypoints.Count - 1], safe) > 0.05f)
                {
                    outWaypoints.Add(safe);
                    return true;
                }
                return false;
            }

            if (outWaypoints.Count == 0 || Vector3.Distance(outWaypoints[outWaypoints.Count - 1], p) > 0.05f)
            {
                outWaypoints.Add(p);
                return true;
            }
            return false;
        }

        void AddDoorCrossing(int doorIdx, int fromRoom, int toRoom)
        {
            if (doorIdx < 0 || doorIdx >= doors.Count) return;
            var d = doors[doorIdx];
            if (d == null) return;

            var normal = d.wallFacingX ? Vector3.right : Vector3.forward;
            var dir = normal;

            if (fromRoom >= 0 && toRoom >= 0)
            {
                var cFrom = RoomCenter2D(fromRoom);
                var cTo = RoomCenter2D(toRoom);
                if (Vector3.Dot(cTo - cFrom, normal) < 0f) dir = -normal;
            }
            else
            {
                var desired = new Vector3(toWorldPos.x - fromWorldPos.x, 0f, toWorldPos.z - fromWorldPos.z);
                if (Vector3.Dot(desired, normal) < 0f) dir = -normal;
            }

            var center = new Vector3(d.position.x, fromWorldPos.y, d.position.z);
            var approach = center - dir * approachDistance;
            var across = center + dir * approachDistance;

            PushPoint(approach);
            PushPoint(across);
        }

        var startRoom = GetContainingRoomIndex(fromWorldPos, preferredFloor);
        var endRoom = GetContainingRoomIndex(toWorldPos, preferredFloor);

        if (startRoom >= 0 && endRoom >= 0 && startRoom != endRoom)
        {
            var q = new Queue<int>();
            var visited = new HashSet<int>();
            var prevRoom = new Dictionary<int, int>();
            var prevDoor = new Dictionary<int, int>();

            visited.Add(startRoom);
            prevRoom[startRoom] = -1;
            q.Enqueue(startRoom);

            var found = false;
            while (q.Count > 0)
            {
                var r = q.Dequeue();
                if (r == endRoom)
                {
                    found = true;
                    break;
                }

                var room = rooms[r];
                if (room == null || room.connectedDoorIndices == null) continue;

                for (var i = 0; i < room.connectedDoorIndices.Count; i++)
                {
                    var dIdx = room.connectedDoorIndices[i];
                    if (dIdx < 0 || dIdx >= doors.Count) continue;
                    var d = doors[dIdx];
                    if (d == null) continue;
                    if (GetDoorFloor(d) != preferredFloor) continue;

                    var next = -1;
                    if (d.roomA == r) next = d.roomB;
                    else if (d.roomB == r) next = d.roomA;
                    if (next < 0 || next >= rooms.Count) continue;
                    if (rooms[next] == null || rooms[next].floorIndex != preferredFloor) continue;
                    if (visited.Contains(next)) continue;

                    visited.Add(next);
                    prevRoom[next] = r;
                    prevDoor[next] = dIdx;
                    q.Enqueue(next);
                }
            }

            if (found)
            {
                var transitions = new List<(int fromRoom, int toRoom, int doorIdx)>();
                var cur = endRoom;
                while (cur != startRoom)
                {
                    if (!prevDoor.TryGetValue(cur, out var dIdx)) break;
                    if (!prevRoom.TryGetValue(cur, out var prev)) break;
                    if (prev < 0) break;
                    transitions.Add((prev, cur, dIdx));
                    cur = prev;
                }

                transitions.Reverse();
                for (var i = 0; i < transitions.Count; i++)
                {
                    var t = transitions[i];
                    AddDoorCrossing(t.doorIdx, t.fromRoom, t.toRoom);
                }

                if (outWaypoints.Count > 0) return true;
            }
        }

        // Fallback: pick up to N nearest doors on this floor and use their crossing pairs as preferred waypoints.
        var candidates = new List<int>();
        var scored = new List<(int doorIdx, float score)>();
        for (var i = 0; i < doors.Count; i++)
        {
            if (i == blockedDoorIdx) continue;
            var d = doors[i];
            if (d == null) continue;
            if (GetDoorFloor(d) != preferredFloor) continue;

            var dx = d.position.x - fromWorldPos.x;
            var dz = d.position.z - fromWorldPos.z;
            var s = dx * dx + dz * dz;
            scored.Add((i, s));
        }

        scored.Sort((a, b) => a.score.CompareTo(b.score));
        var take = Mathf.Min(Mathf.Max(1, fallbackNearestDoors), scored.Count);
        for (var i = 0; i < take; i++)
        {
            candidates.Add(scored[i].doorIdx);
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            AddDoorCrossing(candidates[i], -1, -1);
        }

        return outWaypoints.Count > 0;
    }

    public int GetNearestDoorIndex(Vector3 worldPos, int preferredFloor = 0)
    {
        if (doors == null || doors.Count == 0) return -1;

        var bestIdx = -1;
        var bestScore = float.MaxValue;

        for (var i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d == null) continue;

            var floorIdx = preferredFloor;
            if (d.roomA >= 0 && d.roomA < rooms.Count && rooms[d.roomA] != null)
            {
                floorIdx = rooms[d.roomA].floorIndex;
            }
            else if (d.roomB >= 0 && d.roomB < rooms.Count && rooms[d.roomB] != null)
            {
                floorIdx = rooms[d.roomB].floorIndex;
            }

            if (floorIdx != preferredFloor) continue;

            var dx = d.position.x - worldPos.x;
            var dz = d.position.z - worldPos.z;
            var score = dx * dx + dz * dz;
            if (score < bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        return bestIdx;
    }

    public void CollectRoomCenterNodes(List<Vector3> outCenters, int preferredFloor = 0, float margin = 0.35f)
    {
        if (outCenters == null) return;
        outCenters.Clear();
        if (rooms == null || rooms.Count == 0) return;

        for (var i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r == null) continue;
            if (r.floorIndex != preferredFloor) continue;

            var center = new Vector3(r.position.x + r.size.x * 0.5f, r.position.y + 0.5f, r.position.z + r.size.z * 0.5f);
            if (TryGetSafeSpawnPoint(center, out var safe, preferredFloor, margin, allowDoors: false))
            {
                outCenters.Add(safe);
            }
            else
            {
                outCenters.Add(center);
            }
        }
    }

    public bool TryBuildRoomDoorNodePath(Vector3 fromWorldPos, Vector3 toWorldPos, List<Vector3> outNodes, int preferredFloor = 0, float margin = 0.35f)
    {
        if (outNodes == null) return false;
        outNodes.Clear();

        if (rooms == null || doors == null || rooms.Count == 0) return false;

        var startRoom = GetContainingRoomIndex(fromWorldPos, preferredFloor);
        var endRoom = GetContainingRoomIndex(toWorldPos, preferredFloor);
        if (startRoom < 0 || endRoom < 0) return false;

        bool TryGetRoomCenter(int roomIdx, out Vector3 center)
        {
            center = Vector3.zero;
            if (roomIdx < 0 || roomIdx >= rooms.Count || rooms[roomIdx] == null) return false;
            var r = rooms[roomIdx];
            center = new Vector3(r.position.x + r.size.x * 0.5f, r.position.y + 0.5f, r.position.z + r.size.z * 0.5f);
            if (TryGetSafeSpawnPoint(center, out var safe, preferredFloor, margin, allowDoors: false))
            {
                center = safe;
            }
            return true;
        }

        if (!TryGetRoomCenter(startRoom, out var startCenter)) return false;
        outNodes.Add(startCenter);

        if (startRoom == endRoom)
        {
            if (TryGetRoomCenter(endRoom, out var sameCenter))
            {
                if (Vector3.Distance(sameCenter, outNodes[outNodes.Count - 1]) > 0.05f)
                {
                    outNodes.Add(sameCenter);
                }
            }
            return outNodes.Count > 0;
        }

        var q = new Queue<int>();
        var visited = new HashSet<int>();
        var prevRoom = new Dictionary<int, int>();
        var prevDoor = new Dictionary<int, int>();

        visited.Add(startRoom);
        prevRoom[startRoom] = -1;
        q.Enqueue(startRoom);

        var found = false;
        while (q.Count > 0)
        {
            var r = q.Dequeue();
            if (r == endRoom)
            {
                found = true;
                break;
            }

            var room = rooms[r];
            if (room == null || room.connectedDoorIndices == null) continue;

            for (var i = 0; i < room.connectedDoorIndices.Count; i++)
            {
                var dIdx = room.connectedDoorIndices[i];
                if (dIdx < 0 || dIdx >= doors.Count) continue;
                var d = doors[dIdx];
                if (d == null) continue;

                var next = -1;
                if (d.roomA == r) next = d.roomB;
                else if (d.roomB == r) next = d.roomA;
                if (next < 0 || next >= rooms.Count) continue;
                if (rooms[next] == null || rooms[next].floorIndex != preferredFloor) continue;
                if (visited.Contains(next)) continue;

                visited.Add(next);
                prevRoom[next] = r;
                prevDoor[next] = dIdx;
                q.Enqueue(next);
            }
        }

        if (!found) return outNodes.Count > 0;

        var transitions = new List<(int fromRoom, int toRoom, int doorIdx)>();
        var cur = endRoom;
        while (cur != startRoom)
        {
            if (!prevDoor.TryGetValue(cur, out var dIdx)) break;
            if (!prevRoom.TryGetValue(cur, out var prev)) break;
            if (prev < 0) break;
            transitions.Add((prev, cur, dIdx));
            cur = prev;
        }

        transitions.Reverse();
        for (var i = 0; i < transitions.Count; i++)
        {
            var t = transitions[i];
            if (t.doorIdx >= 0 && t.doorIdx < doors.Count && doors[t.doorIdx] != null)
            {
                var d = doors[t.doorIdx];
                var doorCenter = new Vector3(d.position.x, fromWorldPos.y, d.position.z);
                if (TryGetSafeSpawnPoint(doorCenter, out var safeDoor, preferredFloor, margin, allowDoors: true))
                {
                    doorCenter = safeDoor;
                }
                if (Vector3.Distance(doorCenter, outNodes[outNodes.Count - 1]) > 0.05f)
                {
                    outNodes.Add(doorCenter);
                }
            }

            if (TryGetRoomCenter(t.toRoom, out var roomCenter))
            {
                if (Vector3.Distance(roomCenter, outNodes[outNodes.Count - 1]) > 0.05f)
                {
                    outNodes.Add(roomCenter);
                }
            }
        }

        return outNodes.Count > 0;
    }

    public bool TryBuildExplicitRoomDoorGraphPath(Vector3 fromWorldPos, Vector3 toWorldPos, List<Vector3> outNodes, int preferredFloor = 0, float margin = 0.35f)
    {
        if (outNodes == null) return false;
        outNodes.Clear();

        if (rooms == null || doors == null || rooms.Count == 0) return false;

        var startRoom = GetContainingRoomIndex(fromWorldPos, preferredFloor);
        var endRoom = GetContainingRoomIndex(toWorldPos, preferredFloor);
        if (startRoom < 0 || endRoom < 0) return false;

        // Graph nodes are only room centers and door centers.
        var nodePositions = new List<Vector3>();
        var adjacency = new List<List<int>>();

        var roomNodeByRoomIdx = new Dictionary<int, int>();
        for (var r = 0; r < rooms.Count; r++)
        {
            var room = rooms[r];
            if (room == null) continue;
            if (room.floorIndex != preferredFloor) continue;

            var center = new Vector3(room.position.x + room.size.x * 0.5f, room.position.y + 0.5f, room.position.z + room.size.z * 0.5f);

            var nodeIdx = nodePositions.Count;
            nodePositions.Add(center);
            adjacency.Add(new List<int>());
            roomNodeByRoomIdx[r] = nodeIdx;
        }

        if (!roomNodeByRoomIdx.TryGetValue(startRoom, out var startNode)) return false;
        if (!roomNodeByRoomIdx.TryGetValue(endRoom, out var endNode)) return false;

        for (var d = 0; d < doors.Count; d++)
        {
            var door = doors[d];
            if (door == null) continue;
            if (!roomNodeByRoomIdx.ContainsKey(door.roomA) || !roomNodeByRoomIdx.ContainsKey(door.roomB)) continue;

            var doorCenter = new Vector3(door.position.x, fromWorldPos.y, door.position.z);

            var doorNode = nodePositions.Count;
            nodePositions.Add(doorCenter);
            adjacency.Add(new List<int>());

            var roomANode = roomNodeByRoomIdx[door.roomA];
            var roomBNode = roomNodeByRoomIdx[door.roomB];

            adjacency[roomANode].Add(doorNode);
            adjacency[doorNode].Add(roomANode);

            adjacency[roomBNode].Add(doorNode);
            adjacency[doorNode].Add(roomBNode);
        }

        // BFS shortest path on unweighted graph.
        var q = new Queue<int>();
        var visited = new HashSet<int>();
        var prev = new Dictionary<int, int>();

        visited.Add(startNode);
        prev[startNode] = -1;
        q.Enqueue(startNode);

        var found = false;
        while (q.Count > 0)
        {
            var n = q.Dequeue();
            if (n == endNode)
            {
                found = true;
                break;
            }

            var neigh = adjacency[n];
            for (var i = 0; i < neigh.Count; i++)
            {
                var nn = neigh[i];
                if (visited.Contains(nn)) continue;
                visited.Add(nn);
                prev[nn] = n;
                q.Enqueue(nn);
            }
        }

        if (!found)
        {
            outNodes.Add(nodePositions[startNode]);
            if (startNode != endNode) outNodes.Add(nodePositions[endNode]);
            return outNodes.Count > 0;
        }

        var pathNodeIndices = new List<int>();
        var cur = endNode;
        while (cur >= 0)
        {
            pathNodeIndices.Add(cur);
            if (!prev.TryGetValue(cur, out cur)) break;
        }
        pathNodeIndices.Reverse();

        for (var i = 0; i < pathNodeIndices.Count; i++)
        {
            var p = nodePositions[pathNodeIndices[i]];
            if (outNodes.Count > 0 && Vector3.Distance(outNodes[outNodes.Count - 1], p) <= 0.05f) continue;
            outNodes.Add(p);
        }

        return outNodes.Count > 0;
    }

    public bool TryGetDoorPassAxisAtPosition(Vector3 worldPos, out Vector3 axis, float maxDistance = 1.5f, int preferredFloor = 0)
    {
        axis = Vector3.zero;
        if (doors == null || rooms == null || doors.Count == 0 || rooms.Count == 0) return false;

        var bestIdx = -1;
        var bestScore = maxDistance * maxDistance;

        for (var i = 0; i < doors.Count; i++)
        {
            var d = doors[i];
            if (d == null) continue;
            if (d.roomA < 0 || d.roomA >= rooms.Count || rooms[d.roomA] == null) continue;
            if (d.roomB < 0 || d.roomB >= rooms.Count || rooms[d.roomB] == null) continue;

            if (rooms[d.roomA].floorIndex != preferredFloor || rooms[d.roomB].floorIndex != preferredFloor) continue;

            var dx = d.position.x - worldPos.x;
            var dz = d.position.z - worldPos.z;
            var score = dx * dx + dz * dz;
            if (score < bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        if (bestIdx < 0) return false;

        var door = doors[bestIdx];
        var a = rooms[door.roomA];
        var b = rooms[door.roomB];
        var centerA = new Vector3(a.position.x + a.size.x * 0.5f, 0f, a.position.z + a.size.z * 0.5f);
        var centerB = new Vector3(b.position.x + b.size.x * 0.5f, 0f, b.position.z + b.size.z * 0.5f);

        var dir = centerB - centerA;
        if (dir.sqrMagnitude <= 0.0001f)
        {
            dir = door.wallFacingX ? Vector3.right : Vector3.forward;
        }

        axis = new Vector3(dir.x, 0f, dir.z).normalized;
        return axis.sqrMagnitude > 0.0001f;
    }

    public bool TryGetNearestDoorCenter(Vector3 worldPos, out Vector3 doorCenter, int preferredFloor = 0)
    {
        doorCenter = worldPos;
        var doorIdx = GetNearestDoorIndex(worldPos, preferredFloor);
        if (doorIdx < 0 || doors == null || doorIdx >= doors.Count || doors[doorIdx] == null) return false;

        var d = doors[doorIdx];
        var y = worldPos.y;
        if (d.roomA >= 0 && d.roomA < rooms.Count && rooms[d.roomA] != null)
        {
            y = rooms[d.roomA].position.y + 0.5f;
        }
        else if (d.roomB >= 0 && d.roomB < rooms.Count && rooms[d.roomB] != null)
        {
            y = rooms[d.roomB].position.y + 0.5f;
        }

        doorCenter = new Vector3(d.position.x, y, d.position.z);
        return true;
    }
    public bool TryGetNearestDoorHop(Vector3 worldPos, out Vector3 doorCenter, out Vector3 oppositeRoomCenter, int preferredFloor = 0)
    {
        doorCenter = worldPos;
        oppositeRoomCenter = worldPos;

        var doorIdx = GetNearestDoorIndex(worldPos, preferredFloor);
        if (doorIdx < 0 || doors == null || doorIdx >= doors.Count || doors[doorIdx] == null) return false;

        var d = doors[doorIdx];
        if (d.roomA < 0 || d.roomA >= rooms.Count || rooms[d.roomA] == null) return false;
        if (d.roomB < 0 || d.roomB >= rooms.Count || rooms[d.roomB] == null) return false;

        var roomA = rooms[d.roomA];
        var roomB = rooms[d.roomB];
        if (roomA.floorIndex != preferredFloor || roomB.floorIndex != preferredFloor) return false;

        var currentRoom = GetContainingRoomIndex(worldPos, preferredFloor);
        var targetRoomIdx = d.roomA;
        if (currentRoom == d.roomA) targetRoomIdx = d.roomB;
        else if (currentRoom == d.roomB) targetRoomIdx = d.roomA;
        else
        {
            var centerA = new Vector3(roomA.position.x + roomA.size.x * 0.5f, 0f, roomA.position.z + roomA.size.z * 0.5f);
            var centerB = new Vector3(roomB.position.x + roomB.size.x * 0.5f, 0f, roomB.position.z + roomB.size.z * 0.5f);
            var toA = (new Vector3(worldPos.x, 0f, worldPos.z) - centerA).sqrMagnitude;
            var toB = (new Vector3(worldPos.x, 0f, worldPos.z) - centerB).sqrMagnitude;
            targetRoomIdx = toA <= toB ? d.roomB : d.roomA;
        }

        var y = rooms[targetRoomIdx].position.y + 0.5f;
        doorCenter = new Vector3(d.position.x, y, d.position.z);

        var tr = rooms[targetRoomIdx];
        oppositeRoomCenter = new Vector3(tr.position.x + tr.size.x * 0.5f, tr.position.y + 0.5f, tr.position.z + tr.size.z * 0.5f);
        return true;
    }
    [ContextMenu("Generate Building")]
    public void RegenerateBuilding() => GenerateBuilding();
    // ?????????????????????????????????????????????????????????????????????????
    //  MINIMAP
    // ?????????????????????????????????????????????????????????????????????????

    private void BuildMinimap()
    {
        if (floorplanRenderer == null) return;
        floorplanRenderer.Build(walls, doors, rooms, floorHeight);

        if (minimapController != null)
            minimapController.SetMapBounds(rooms);
        else
            Debug.LogWarning("[Minimap] MinimapController not assigned � " +
                             "drag it into the MinimapController field on the generator.");

        Debug.Log("[Minimap] Floorplan sprites built.");
    }
    private void BuildProps()
    {
        if (propSpawner == null) return;
        propSpawner.Furnish(rooms, walls, buildingParentMap, roomGeometry, seed);
        Debug.Log("[Props] Furnishing complete.");
    }
}