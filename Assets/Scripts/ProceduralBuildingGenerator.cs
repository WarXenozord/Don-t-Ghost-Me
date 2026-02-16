using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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
    public bool dim = false;
    public bool dir = false;
}

[System.Serializable]
public class BuildingWall
{
    public Vector3 position;
    public Vector3 size;
    public int dimension;
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
    General,
    Bathroom,
    Kitchen,
    Hallway,
    LivingRoom,
    Bedroom,
    Storage,
    Stairwell
}

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
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject ceilingPrefab;
    
    private List<BuildingRoom> rooms = new List<BuildingRoom>();
    private List<BuildingWall> walls = new List<BuildingWall>();
    private List<BuildingDoor> doors = new List<BuildingDoor>();
    private List<BuildingStairs> stairs = new List<BuildingStairs>();
    private List<BuildingSection> buildings = new List<BuildingSection>();
    
    private System.Random random;

    void Start()
    {
        GenerateBuilding();
    }

    public void GenerateBuilding()
    {
        ClearBuilding();
        Debug.Log($"Starting building generation with seed: {seed}");
        
        random = new System.Random(seed);
        
        int calculatedFloors = Mathf.Max(1, Mathf.FloorToInt(floorHeight * numFloors));
        numFloors = Mathf.Min(numFloors, calculatedFloors);
        Debug.Log($"Generating {numFloors} floors, targeting ~{targetNumRooms} rooms total");
        
        int buildingIndex = 0;
        int buildingCountInRow = 0;
        Vector3 currentOffset = Vector3.zero;
        float maxHeightInRow = 0f;
        
        while (rooms.Count < targetNumRooms)
        {
            Debug.Log($"\n=== Creating Building Section {buildingIndex} ===");
            
            Vector3 sectionSize = new Vector3(
                (float)(minBuildingSize.x + (maxBuildingSize.x - minBuildingSize.x) * random.NextDouble()),
                floorHeight * numFloors,
                (float)(minBuildingSize.z + (maxBuildingSize.z - minBuildingSize.z) * random.NextDouble())
            );
            
            int roomsBefore = rooms.Count;
            GenerateBuildingSection(buildingIndex, currentOffset, sectionSize);
            int roomsAdded = rooms.Count - roomsBefore;
            
            Debug.Log($"Building {buildingIndex}: Added {roomsAdded} rooms at {currentOffset} (Total: {rooms.Count}/{targetNumRooms})");
            
            maxHeightInRow = Mathf.Max(maxHeightInRow, sectionSize.z);
            buildingCountInRow++;
            
            if (buildingCountInRow >= buildingsPerRow)
            {
                currentOffset.x = 0;
                currentOffset.z += maxHeightInRow + buildingSpacingZ;
                buildingCountInRow = 0;
                maxHeightInRow = 0f;
            }
            else
            {
                currentOffset.x += sectionSize.x + buildingSpacingX;
            }
            
            buildingIndex++;
        }
        
        Debug.Log($"\n? Generated {rooms.Count} rooms across {buildingIndex} buildings");
        
        CreateWallsForAllRooms();
        ConnectRoomsWithinBuildings();
        ConnectAdjacentBuildings();
        
        if (numFloors > 1)
        {
            Debug.Log("Generating stairs...");
            GenerateStairs();
        }
        
        Debug.Log($"? Generated {walls.Count} walls, {doors.Count} doors, {stairs.Count} stairs");
        InstantiateGeometry();
        Debug.Log("Building generation complete!");
    }

    private void GenerateBuildingSection(int buildingIndex, Vector3 offset, Vector3 sectionSize)
    {
        var section = new BuildingSection
        {
            position = offset,
            size = sectionSize,
            buildingIndex = buildingIndex,
            roomsInSection = 0
        };
        buildings.Add(section);
        
        int roomsBeforeBuilding = rooms.Count;
        
        for (int floorIdx = 0; floorIdx < numFloors; floorIdx++)
        {
            float floorY = offset.y + (floorIdx * floorHeight);
            GenerateFloor(floorY, floorIdx, buildingIndex, offset, sectionSize);
        }
        
        section.roomsInSection = rooms.Count - roomsBeforeBuilding;
    }

    private void GenerateFloor(float floorY, int floorIndex, int buildingIndex, Vector3 offset, Vector3 sectionSize)
    {
        var toSplit = new Queue<(Vector3 pos, Vector3 size)>();
        
        Vector3 floorOrigin = new Vector3(offset.x, floorY, offset.z);
        Vector3 floorSize = new Vector3(sectionSize.x, floorHeight, sectionSize.z);
        
        toSplit.Enqueue((floorOrigin, floorSize));
        
        int iterations = 0;
        int maxIterations = 1000;
        
        while (toSplit.Count > 0 && iterations < maxIterations)
        {
            iterations++;
            var (position, size) = toSplit.Dequeue();
            
            if (size.x <= 0.1f || size.z <= 0.1f)
                continue;
            
            float minTotalToSplit = (minRoomSize * 2) + (wallThickness * 2);
            bool canSplitX = size.x >= minTotalToSplit;
            bool canSplitZ = size.z >= minTotalToSplit;
            
            if (!canSplitX && !canSplitZ)
            {
                CreateRoom(position, size, floorIndex, buildingIndex);
                continue;
            }
            
            bool splitSuccess = false;
            
            if (canSplitX && canSplitZ)
            {
                if (random.Next(0, 2) == 0)
                {
                    float splitX = GetRandomSplitPosition(size.x);
                    if (splitX > position.x && splitX < position.x + size.x)
                    {
                        SplitRoomOnX(position, size, splitX, toSplit);
                        splitSuccess = true;
                    }
                }
                else
                {
                    float splitZ = GetRandomSplitPosition(size.z);
                    if (splitZ > position.z && splitZ < position.z + size.z)
                    {
                        SplitRoomOnZ(position, size, splitZ, toSplit);
                        splitSuccess = true;
                    }
                }
                
                if (!splitSuccess)
                {
                    float splitX = GetRandomSplitPosition(size.x);
                    if (splitX > position.x && splitX < position.x + size.x)
                    {
                        SplitRoomOnX(position, size, splitX, toSplit);
                        splitSuccess = true;
                    }
                }
            }
            else if (canSplitX)
            {
                float splitX = GetRandomSplitPosition(size.x);
                if (splitX > position.x && splitX < position.x + size.x)
                {
                    SplitRoomOnX(position, size, splitX, toSplit);
                    splitSuccess = true;
                }
            }
            else if (canSplitZ)
            {
                float splitZ = GetRandomSplitPosition(size.z);
                if (splitZ > position.z && splitZ < position.z + size.z)
                {
                    SplitRoomOnZ(position, size, splitZ, toSplit);
                    splitSuccess = true;
                }
            }
            
            if (!splitSuccess)
            {
                CreateRoom(position, size, floorIndex, buildingIndex);
            }
        }
    }

    private float GetRandomSplitPosition(float dimensionLength)
    {
        float minSplit = minRoomSize + wallThickness;
        float maxSplit = dimensionLength - minRoomSize - wallThickness;
        
        float minByMax = maxRoomSize;
        float maxByMin = dimensionLength - maxRoomSize;
        
        minSplit = Mathf.Max(minSplit, minByMax);
        maxSplit = Mathf.Min(maxSplit, maxByMin);
        
        if (maxSplit <= minSplit)
            return -1f;
        
        return (float)(minSplit + (maxSplit - minSplit) * random.NextDouble());
    }

    private void SplitRoomOnX(Vector3 position, Vector3 size, float splitX, Queue<(Vector3, Vector3)> toSplit)
    {
        float leftSize = splitX - position.x;
        float rightSize = position.x + size.x - splitX;
        
        if (leftSize > 0.1f && rightSize > 0.1f)
        {
            Vector3 size1 = size;
            Vector3 size2 = size;
            size1.x = leftSize;
            size2.x = rightSize;
            
            Vector3 pos2 = position;
            pos2.x = splitX;
            
            toSplit.Enqueue((position, size1));
            toSplit.Enqueue((pos2, size2));
        }
    }

    private void SplitRoomOnZ(Vector3 position, Vector3 size, float splitZ, Queue<(Vector3, Vector3)> toSplit)
    {
        float frontSize = splitZ - position.z;
        float backSize = position.z + size.z - splitZ;
        
        if (frontSize > 0.1f && backSize > 0.1f)
        {
            Vector3 size1 = size;
            Vector3 size2 = size;
            size1.z = frontSize;
            size2.z = backSize;
            
            Vector3 pos2 = position;
            pos2.z = splitZ;
            
            toSplit.Enqueue((position, size1));
            toSplit.Enqueue((pos2, size2));
        }
    }

    private void CreateRoom(Vector3 position, Vector3 size, int floorIndex, int buildingIndex)
    {
        if (size.x <= 0.1f || size.z <= 0.1f)
            return;
        
        RoomType type = ClassifyRoom(size);
        
        var room = new BuildingRoom
        {
            position = position,
            size = size,
            roomType = type,
            floorIndex = floorIndex,
            buildingIndex = buildingIndex
        };
        
        rooms.Add(room);
    }

    private RoomType ClassifyRoom(Vector3 size)
    {
        float minDim = Mathf.Min(size.x, size.z);
        float maxDim = Mathf.Max(size.x, size.z);
        float ratio = maxDim / minDim;
        
        if (minDim < 1.5f && ratio > 3f)
            return RoomType.Hallway;
        
        if (minDim < 2.5f && maxDim < 4f)
            return RoomType.Bathroom;
        
        if (minDim > 2f && maxDim < 7f && ratio < 2f)
            return RoomType.Kitchen;
        
        if (minDim > 5f)
            return RoomType.LivingRoom;
        
        if (minDim > 3f && maxDim < 8f)
            return RoomType.Bedroom;
        
        return RoomType.General;
    }

    private void CreateWallsForAllRooms()
    {
        foreach (var room in rooms)
        {
            CreateWallsAroundRoom(room);
        }
    }

    private void CreateWallsAroundRoom(BuildingRoom room)
    {
        float x1 = room.position.x;
        float x2 = room.position.x + room.size.x;
        float z1 = room.position.z;
        float z2 = room.position.z + room.size.z;
        float y1 = room.position.y;
        float height = room.size.y;
        
        CreateWall(new Vector3(x1, y1 + height / 2, (z1 + z2) / 2),
                  new Vector3(wallThickness, height, room.size.z), 0);
        
        CreateWall(new Vector3(x2, y1 + height / 2, (z1 + z2) / 2),
                  new Vector3(wallThickness, height, room.size.z), 0);
        
        CreateWall(new Vector3((x1 + x2) / 2, y1 + height / 2, z1),
                  new Vector3(room.size.x, height, wallThickness), 1);
        
        CreateWall(new Vector3((x1 + x2) / 2, y1 + height / 2, z2),
                  new Vector3(room.size.x, height, wallThickness), 1);
    }

    private void CreateWall(Vector3 position, Vector3 size, int dimension)
    {
        walls.Add(new BuildingWall { position = position, size = size, dimension = dimension });
    }

    private void ConnectRoomsWithinBuildings()
    {
        var groups = rooms.GroupBy(r => (r.floorIndex, r.buildingIndex));
        
        int internalDoorsAdded = 0;
        
        foreach (var floorRooms in groups)
        {
            var roomList = floorRooms.ToList();
            
            for (int i = 0; i < roomList.Count; i++)
            {
                for (int j = i + 1; j < roomList.Count; j++)
                {
                    BuildingRoom r1 = roomList[i];
                    BuildingRoom r2 = roomList[j];
                    
                    if (TryAddDoorBetweenRoomsOnX(r1, r2))
                    {
                        internalDoorsAdded++;
                    }
                    else if (TryAddDoorBetweenRoomsOnZ(r1, r2))
                    {
                        internalDoorsAdded++;
                    }
                }
            }
        }
        
        Debug.Log($"Added {internalDoorsAdded} internal doors");
    }

    private bool TryAddDoorBetweenRoomsOnX(BuildingRoom r1, BuildingRoom r2)
    {
        // Check X-axis adjacency
        if (!AreRoomsAdjacentOnX(r1, r2))
            return false;
        
        // Calculate overlap in Z
        float overlapZ1 = Mathf.Max(r1.position.z, r2.position.z);
        float overlapZ2 = Mathf.Min(r1.position.z + r1.size.z, r2.position.z + r2.size.z);
        float overlapZLength = overlapZ2 - overlapZ1;
        
        // Door must fit within overlap (need doorWidth space)
        if (overlapZLength < doorWidth + 0.2f)
            return false;
        
        // Calculate door position - centered in the overlap but with safety margin from edges
        float safeMargin = 0.1f;
        float overlapZMin = overlapZ1 + safeMargin;
        float overlapZMax = overlapZ2 - safeMargin;
        float overlapZUsable = overlapZMax - overlapZMin;
        
        if (overlapZUsable < doorWidth)
            return false;
        
        // Center door in usable overlap
        float doorZ = overlapZMin + overlapZUsable / 2;
        float doorX = r1.position.x + r1.size.x;
        
        AddDoor(rooms.IndexOf(r1), rooms.IndexOf(r2), 
               new Vector3(doorX, r1.position.y + 1f, doorZ), false, true);
        
        return true;
    }

    private bool TryAddDoorBetweenRoomsOnZ(BuildingRoom r1, BuildingRoom r2)
    {
        // Check Z-axis adjacency
        if (!AreRoomsAdjacentOnZ(r1, r2))
            return false;
        
        // Calculate overlap in X
        float overlapX1 = Mathf.Max(r1.position.x, r2.position.x);
        float overlapX2 = Mathf.Min(r1.position.x + r1.size.x, r2.position.x + r2.size.x);
        float overlapXLength = overlapX2 - overlapX1;
        
        // Door must fit within overlap (need doorWidth space)
        if (overlapXLength < doorWidth + 0.2f)
            return false;
        
        // Calculate door position - centered in the overlap but with safety margin from edges
        float safeMargin = 0.1f;
        float overlapXMin = overlapX1 + safeMargin;
        float overlapXMax = overlapX2 - safeMargin;
        float overlapXUsable = overlapXMax - overlapXMin;
        
        if (overlapXUsable < doorWidth)
            return false;
        
        // Center door in usable overlap
        float doorX = overlapXMin + overlapXUsable / 2;
        float doorZ = r1.position.z + r1.size.z;
        
        AddDoor(rooms.IndexOf(r1), rooms.IndexOf(r2), 
               new Vector3(doorX, r1.position.y + 1f, doorZ), true, true);
        
        return true;
    }

    private bool AreRoomsAdjacentOnX(BuildingRoom r1, BuildingRoom r2)
    {
        bool r1_right_touches_r2_left = Mathf.Abs((r1.position.x + r1.size.x) - r2.position.x) < 0.5f;
        
        if (!r1_right_touches_r2_left)
            return false;
        
        return r1.position.z < r2.position.z + r2.size.z &&
               r1.position.z + r1.size.z > r2.position.z;
    }

    private bool AreRoomsAdjacentOnZ(BuildingRoom r1, BuildingRoom r2)
    {
        bool r1_back_touches_r2_front = Mathf.Abs((r1.position.z + r1.size.z) - r2.position.z) < 0.5f;
        
        if (!r1_back_touches_r2_front)
            return false;
        
        return r1.position.x < r2.position.x + r2.size.x &&
               r1.position.x + r1.size.x > r2.position.x;
    }

    private void ConnectAdjacentBuildings()
    {
        int buildingDoorsAdded = 0;
        
        for (int i = 0; i < buildings.Count; i++)
        {
            for (int j = i + 1; j < buildings.Count; j++)
            {
                BuildingSection b1 = buildings[i];
                BuildingSection b2 = buildings[j];
                
                // Check if buildings touch on X axis
                float b1_right = b1.position.x + b1.size.x;
                float b2_left = b2.position.x;
                float xGap = b2_left - b1_right;
                
                if (Mathf.Abs(xGap) < 0.1f)
                {
                    int doorCount = ConnectBuildingsOnXAxis(i, j);
                    buildingDoorsAdded += doorCount;
                }
                
                // Check if buildings touch on Z axis
                float b1_back = b1.position.z + b1.size.z;
                float b2_front = b2.position.z;
                float zGap = b2_front - b1_back;
                
                if (Mathf.Abs(zGap) < 0.1f)
                {
                    int doorCount = ConnectBuildingsOnZAxis(i, j);
                    buildingDoorsAdded += doorCount;
                }
            }
        }
        
        Debug.Log($"Added {buildingDoorsAdded} doors between adjacent (touching) buildings");
    }

    private int ConnectBuildingsOnXAxis(int buildingAIdx, int buildingBIdx)
    {
        BuildingSection b1 = buildings[buildingAIdx];
        BuildingSection b2 = buildings[buildingBIdx];
        
        int doorsCreated = 0;
        float sharedWallX = b1.position.x + b1.size.x;
        
        var edgeRooms1 = rooms.Where(r => r.buildingIndex == buildingAIdx && 
                                           r.floorIndex == 0 &&
                                           Mathf.Abs((r.position.x + r.size.x) - sharedWallX) < 0.1f).ToList();
        
        var edgeRooms2 = rooms.Where(r => r.buildingIndex == buildingBIdx && 
                                           r.floorIndex == 0 &&
                                           Mathf.Abs(r.position.x - b2.position.x) < 0.1f).ToList();
        
        if (edgeRooms1.Count > 0 && edgeRooms2.Count > 0)
        {
            var overlappingPairs = new List<(BuildingRoom, BuildingRoom)>();
            
            foreach (var r1 in edgeRooms1)
            {
                foreach (var r2 in edgeRooms2)
                {
                    // Check if rooms overlap in Z AND have enough space for door
                    if (r1.position.z < r2.position.z + r2.size.z &&
                        r1.position.z + r1.size.z > r2.position.z)
                    {
                        float overlapZ1 = Mathf.Max(r1.position.z, r2.position.z);
                        float overlapZ2 = Mathf.Min(r1.position.z + r1.size.z, r2.position.z + r2.size.z);
                        
                        // Only add if overlap is large enough for door
                        if (overlapZ2 - overlapZ1 >= doorWidth + 0.2f)
                        {
                            overlappingPairs.Add((r1, r2));
                        }
                    }
                }
            }
            
            if (overlappingPairs.Count > 0)
            {
                var (r1, r2) = overlappingPairs[random.Next(overlappingPairs.Count)];
                
                // Calculate safe position in overlap
                float overlapZ1 = Mathf.Max(r1.position.z, r2.position.z);
                float overlapZ2 = Mathf.Min(r1.position.z + r1.size.z, r2.position.z + r2.size.z);
                float safeMargin = 0.1f;
                float doorZ = overlapZ1 + safeMargin + (overlapZ2 - overlapZ1 - 2 * safeMargin) / 2;
                float doorX = sharedWallX;
                
                AddDoor(rooms.IndexOf(r1), rooms.IndexOf(r2), 
                       new Vector3(doorX, r1.position.y + 1f, doorZ), false, true);
                
                Debug.Log($"? Connected Building {buildingAIdx} to Building {buildingBIdx} on X axis (TOUCHING)");
                doorsCreated++;
            }
        }
        
        return doorsCreated;
    }

    private int ConnectBuildingsOnZAxis(int buildingAIdx, int buildingBIdx)
    {
        BuildingSection b1 = buildings[buildingAIdx];
        BuildingSection b2 = buildings[buildingBIdx];
        
        int doorsCreated = 0;
        float sharedWallZ = b1.position.z + b1.size.z;
        
        var edgeRooms1 = rooms.Where(r => r.buildingIndex == buildingAIdx && 
                                           r.floorIndex == 0 &&
                                           Mathf.Abs((r.position.z + r.size.z) - sharedWallZ) < 0.1f).ToList();
        
        var edgeRooms2 = rooms.Where(r => r.buildingIndex == buildingBIdx && 
                                           r.floorIndex == 0 &&
                                           Mathf.Abs(r.position.z - b2.position.z) < 0.1f).ToList();
        
        if (edgeRooms1.Count > 0 && edgeRooms2.Count > 0)
        {
            var overlappingPairs = new List<(BuildingRoom, BuildingRoom)>();
            
            foreach (var r1 in edgeRooms1)
            {
                foreach (var r2 in edgeRooms2)
                {
                    // Check if rooms overlap in X AND have enough space for door
                    if (r1.position.x < r2.position.x + r2.size.x &&
                        r1.position.x + r1.size.x > r2.position.x)
                    {
                        float overlapX1 = Mathf.Max(r1.position.x, r2.position.x);
                        float overlapX2 = Mathf.Min(r1.position.x + r1.size.x, r2.position.x + r2.size.x);
                        
                        // Only add if overlap is large enough for door
                        if (overlapX2 - overlapX1 >= doorWidth + 0.2f)
                        {
                            overlappingPairs.Add((r1, r2));
                        }
                    }
                }
            }
            
            if (overlappingPairs.Count > 0)
            {
                var (r1, r2) = overlappingPairs[random.Next(overlappingPairs.Count)];
                
                // Calculate safe position in overlap
                float overlapX1 = Mathf.Max(r1.position.x, r2.position.x);
                float overlapX2 = Mathf.Min(r1.position.x + r1.size.x, r2.position.x + r2.size.x);
                float safeMargin = 0.1f;
                float doorX = overlapX1 + safeMargin + (overlapX2 - overlapX1 - 2 * safeMargin) / 2;
                float doorZ = sharedWallZ;
                
                AddDoor(rooms.IndexOf(r1), rooms.IndexOf(r2), 
                       new Vector3(doorX, r1.position.y + 1f, doorZ), true, true);
                
                Debug.Log($"? Connected Building {buildingAIdx} to Building {buildingBIdx} on Z axis (TOUCHING)");
                doorsCreated++;
            }
        }
        
        return doorsCreated;
    }

    private void AddDoor(int roomA, int roomB, Vector3 position, bool dim, bool dir)
    {
        Vector3 doorSize = dim ? 
            new Vector3(doorWidth, doorHeight, wallThickness * 2) :
            new Vector3(wallThickness * 2, doorHeight, doorWidth);
        
        doors.Add(new BuildingDoor
        {
            position = position,
            size = doorSize,
            roomA = roomA,
            roomB = roomB,
            dim = dim,
            dir = dir
        });
    }

    private void GenerateStairs()
    {
        foreach (var building in buildings)
        {
            if (building.roomsInSection == 0) continue;
            
            var buildingRooms = rooms.Where(r => r.buildingIndex == building.buildingIndex).ToList();
            var floorIndices = buildingRooms.Select(r => r.floorIndex).Distinct().OrderBy(f => f).ToList();
            
            for (int i = 0; i < floorIndices.Count - 1; i++)
            {
                var roomsFloorA = buildingRooms.Where(r => r.floorIndex == floorIndices[i]).ToList();
                var stairRoom = FindBestStairRoom(roomsFloorA);
                
                if (stairRoom == null) continue;
                
                float padding = 0.2f;
                float stairX = stairRoom.position.x + padding;
                float stairZ = stairRoom.position.z + padding;
                
                if (stairX + stairDepth + padding > stairRoom.position.x + stairRoom.size.x)
                    stairX = stairRoom.position.x + stairRoom.size.x - stairDepth - padding;
                
                if (stairZ + stairWidth + padding > stairRoom.position.z + stairRoom.size.z)
                    stairZ = stairRoom.position.z + stairRoom.size.z - stairWidth - padding;
                
                bool dim = random.Next(0, 2) == 0;
                
                stairs.Add(new BuildingStairs
                {
                    position = new Vector3(stairX, stairRoom.position.y, stairZ),
                    size = dim ? new Vector3(stairDepth, floorHeight, stairWidth) : 
                               new Vector3(stairWidth, floorHeight, stairDepth),
                    floorA = floorIndices[i],
                    floorB = floorIndices[i + 1],
                    dim = dim
                });
            }
        }
    }

    private BuildingRoom FindBestStairRoom(List<BuildingRoom> roomList)
    {
        var hallways = roomList.Where(r => r.roomType == RoomType.Hallway).ToList();
        if (hallways.Count > 0) return hallways[random.Next(hallways.Count)];
        
        var large = roomList.Where(r => r.size.x > stairDepth + 1 && r.size.z > stairWidth + 1).ToList();
        if (large.Count > 0) return large[random.Next(large.Count)];
        
        return roomList.Count > 0 ? roomList[random.Next(roomList.Count)] : null;
    }

    private void InstantiateGeometry()
    {
        Transform parent = this.transform;
        
        var buildingParents = new Dictionary<int, Transform>();
        foreach (var building in buildings)
        {
            var obj = new GameObject($"Building_{building.buildingIndex} ({building.roomsInSection} rooms)");
            obj.transform.parent = parent;
            obj.transform.position = building.position;
            buildingParents[building.buildingIndex] = obj.transform;
        }
        
        foreach (var room in rooms)
        {
            var buildingParent = buildingParents[room.buildingIndex];
            
            var floorPos = room.position + new Vector3(room.size.x / 2, floorCeilThickness / 2, room.size.z / 2);
            var floor = Instantiate(floorPrefab, floorPos, Quaternion.identity, buildingParent);
            floor.name = $"Floor_{room.roomType}";
            floor.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);
            
            var ceilPos = new Vector3(room.position.x + room.size.x / 2,
                                     room.position.y + room.size.y - floorCeilThickness / 2,
                                     room.position.z + room.size.z / 2);
            var ceil = Instantiate(ceilingPrefab, ceilPos, Quaternion.identity, buildingParent);
            ceil.name = $"Ceiling_{room.roomType}";
            ceil.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);
        }
        
        foreach (var wall in walls)
        {
            var wallObj = Instantiate(wallPrefab, wall.position, Quaternion.identity, parent);
            wallObj.transform.localScale = wall.size;
        }
        
        foreach (var door in doors)
        {
            var doorObj = Instantiate(doorPrefab, door.position, Quaternion.identity, parent);
            doorObj.transform.localScale = door.size;
        }
        
        int stairCount = 0;
        foreach (var staircase in stairs)
        {
            GameObject stairObj = stairPrefab != null ? 
                Instantiate(stairPrefab, staircase.position, Quaternion.identity, parent) :
                CreateDefaultStairs(staircase, parent);
            
            stairObj.name = $"Stairs_{stairCount++}";
            stairObj.transform.localScale = staircase.size;
        }
    }

    private GameObject CreateDefaultStairs(BuildingStairs staircase, Transform parent)
    {
        var obj = new GameObject("Stairs");
        obj.transform.parent = parent;
        obj.transform.position = staircase.position;
        
        var meshFilter = obj.AddComponent<MeshFilter>();
        var meshRenderer = obj.AddComponent<MeshRenderer>();
        meshFilter.mesh = CreateStairMesh();
        meshRenderer.material = new Material(Shader.Find("Standard"));
        meshRenderer.material.color = Color.gray;
        obj.AddComponent<BoxCollider>();
        
        return obj;
    }

    private Mesh CreateStairMesh()
    {
        var mesh = new Mesh();
        mesh.vertices = new Vector3[8]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1)
        };
        mesh.triangles = new int[36]
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    private void ClearBuilding()
    {
        rooms.Clear();
        walls.Clear();
        doors.Clear();
        stairs.Clear();
        buildings.Clear();
    }

    [ContextMenu("Generate Building")]
    public void RegenerateBuilding()
    {
        GenerateBuilding();
    }
}