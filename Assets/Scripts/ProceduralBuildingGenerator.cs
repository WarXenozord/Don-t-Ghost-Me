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
    
    public override string ToString()
    {
        return $"Room({roomType}) at {position}, size {size}";
    }
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
    [Header("Building Parameters")]
    [SerializeField] private Vector3 buildingSize = new Vector3(30, 12, 30);
    [SerializeField] private int numFloors = 2;
    [SerializeField] private float floorHeight = 3.5f;
    [SerializeField] private int targetNumRooms = 20;
    
    [Header("Building Sections")]
    [SerializeField] private float buildingSectionSpacing = 2f;
    [SerializeField] private Vector3 minBuildingSize = new Vector3(15, 12, 15); // Minimum size for a building
    [SerializeField] private Vector3 maxBuildingSize = new Vector3(40, 12, 40); // Maximum size for a building
    [SerializeField] private int targetRoomsPerBuilding = 5; // Try to create this many rooms per building
    
    [Header("Room Generation")]
    [SerializeField] private float minRoomSize = 3f;
    [SerializeField] private float maxRoomSize = 10f;
    [SerializeField] private float minHallwayWidth = 1.2f;
    [SerializeField] private float doorWidth = 1f;
    [SerializeField] private float doorHeight = 2.2f;
    [SerializeField] private float wallThickness = 0.2f;
    [SerializeField] private float floorCeilThickness = 0.15f;
    
    [Header("Stairs")]
    [SerializeField] private float stairWidth = 1.2f;
    [SerializeField] private float stairDepth = 2f;
    [SerializeField] public GameObject stairPrefab;
    [SerializeField] private int maxIterations = 5000;
    
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
    private int randomSeed = 42;
    private int currentBuildingIndex = 0;

    void Start()
    {
        random = new System.Random(randomSeed);
        GenerateBuilding();
    }

    public void GenerateBuilding()
    {
        ClearBuilding();
        Debug.Log("Starting building generation...");
        
        int calculatedFloors = Mathf.Max(1, Mathf.FloorToInt(buildingSize.y / floorHeight));
        numFloors = Mathf.Min(numFloors, calculatedFloors);
        Debug.Log($"Generating {numFloors} floors with target of {targetNumRooms} rooms");
        
        currentBuildingIndex = 0;
        Vector3 currentOffset = Vector3.zero;
        
        // Keep generating building sections until we reach target room count
        while (rooms.Count < targetNumRooms)
        {
            Debug.Log($"\n=== Building Section {currentBuildingIndex} ===");
            
            // Generate a random size for this building
            Vector3 sectionSize = new Vector3(
                Random.Range(minBuildingSize.x, maxBuildingSize.x),
                buildingSize.y,
                Random.Range(minBuildingSize.z, maxBuildingSize.z)
            );
            
            GenerateBuildingSection(currentBuildingIndex, currentOffset, sectionSize);
            
            // Offset for next building (arrange in a staggered pattern)
            if (currentBuildingIndex % 2 == 0)
                currentOffset.x += sectionSize.x + buildingSectionSpacing;
            else
            {
                currentOffset.x -= (sectionSize.x + buildingSectionSpacing) / 2;
                currentOffset.z += sectionSize.z + buildingSectionSpacing;
            }
            
            currentBuildingIndex++;
        }
        
        Debug.Log($"\n? Generated {rooms.Count} rooms across {currentBuildingIndex} building sections");
        CreateWallsAndDoors();
        ConnectAdjacentBuildings(); // NEW: Connect buildings with doors
        
        if (numFloors > 1)
        {
            Debug.Log("Attempting to generate stairs...");
            GenerateStairs();
        }
        
        Debug.Log($"Generated {walls.Count} walls, {doors.Count} doors, and {stairs.Count} stairs");
        InstantiateGeometry();
        Debug.Log("Building generation complete!");
    }

    private void GenerateBuildingSection(int buildingIndex, Vector3 offset, Vector3 sectionSize)
    {
        // Register this building section
        var section = new BuildingSection
        {
            position = offset,
            size = sectionSize,
            buildingIndex = buildingIndex,
            roomsInSection = 0
        };
        buildings.Add(section);
        
        Debug.Log($"Building section {buildingIndex} at position {offset} with size {sectionSize}");
        
        int roomsBeforeSection = rooms.Count;
        int roomsToCreate = targetRoomsPerBuilding;
        
        // Generate floors for this building section
        for (int floorIdx = 0; floorIdx < numFloors; floorIdx++)
        {
            float floorY = floorIdx * floorHeight;
            GenerateFloor(floorY, floorIdx, buildingIndex, offset, sectionSize, roomsToCreate);
        }
        
        int roomsInSection = rooms.Count - roomsBeforeSection;
        section.roomsInSection = roomsInSection;
        Debug.Log($"Building section {buildingIndex}: {roomsInSection} rooms created");
    }

    private void GenerateFloor(float floorY, int floorIndex, int buildingIndex, Vector3 offset, Vector3 sectionSize, int targetRoomsThisSection)
    {
        var toSplit = new Queue<(Vector3 pos, Vector3 size)>();
        
        Vector3 floorOrigin = offset + new Vector3(0, floorY, 0);
        Vector3 floorSize = new Vector3(sectionSize.x, floorHeight, sectionSize.z);
        
        toSplit.Enqueue((floorOrigin, floorSize));
        
        int iterations = 0;
        int roomsThisFloor = 0;
        int startingRoomCount = rooms.Count;
        
        while (toSplit.Count > 0 && iterations < maxIterations && (rooms.Count - startingRoomCount) < targetRoomsThisSection)
        {
            iterations++;
            var (position, size) = toSplit.Dequeue();
            
            if (size.x <= 0 || size.z <= 0 || size.y <= 0)
            {
                continue;
            }
            
            // BASE CASE: Room is too small to split further
            if (size.x < (minRoomSize * 2 + wallThickness) || 
                size.z < (minRoomSize * 2 + wallThickness))
            {
                CreateRoom(position, size, floorIndex, buildingIndex);
                roomsThisFloor++;
                continue;
            }
            
            // BASE CASE: Stop splitting if we've reached target for this building section
            if ((rooms.Count - startingRoomCount) >= targetRoomsThisSection)
            {
                CreateRoom(position, size, floorIndex, buildingIndex);
                roomsThisFloor++;
                continue;
            }
            
            // RECURSIVE CASE: Try to split the room
            bool splitSuccess = false;
            int splitDim = random.Next(0, 2);
            
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (splitDim == 0)
                {
                    if (size.x >= minRoomSize * 2 + wallThickness)
                    {
                        float splitX = GetRandomSplitPosition(size.x, minRoomSize, maxRoomSize);
                        if (splitX > position.x && splitX < position.x + size.x)
                        {
                            SplitRoomOnX(position, size, splitX, toSplit);
                            splitSuccess = true;
                            break;
                        }
                    }
                }
                else
                {
                    if (size.z >= minRoomSize * 2 + wallThickness)
                    {
                        float splitZ = GetRandomSplitPosition(size.z, minRoomSize, maxRoomSize);
                        if (splitZ > position.z && splitZ < position.z + size.z)
                        {
                            SplitRoomOnZ(position, size, splitZ, toSplit);
                            splitSuccess = true;
                            break;
                        }
                    }
                }
                
                splitDim = 1 - splitDim;
            }
            
            if (!splitSuccess)
            {
                CreateRoom(position, size, floorIndex, buildingIndex);
                roomsThisFloor++;
            }
        }
        
        if (roomsThisFloor > 0)
        {
            Debug.Log($"Floor {floorIndex} (Building {buildingIndex}): {roomsThisFloor} rooms created");
        }
    }

    private float GetRandomSplitPosition(float dimensionLength, float minSize, float maxSize)
    {
        float minSplit = minSize + wallThickness;
        float maxSplit = dimensionLength - minSize - wallThickness;
        
        minSplit = Mathf.Max(minSplit, minSize);
        maxSplit = Mathf.Min(maxSplit, dimensionLength - minSize);
        
        if (maxSplit <= minSplit)
            return -1f;
        
        return (float)(minSplit + (maxSplit - minSplit) * random.NextDouble());
    }

    private void SplitRoomOnX(Vector3 position, Vector3 size, float splitX, Queue<(Vector3, Vector3)> toSplit)
    {
        Vector3 size1 = size;
        Vector3 size2 = size;
        
        float leftSize = splitX - position.x;
        float rightSize = position.x + size.x - splitX;
        
        if (leftSize <= 0 || rightSize <= 0)
            return;
        
        size1.x = leftSize;
        size2.x = rightSize;
        
        Vector3 pos2 = position;
        pos2.x = splitX;
        
        toSplit.Enqueue((position, size1));
        toSplit.Enqueue((pos2, size2));
    }

    private void SplitRoomOnZ(Vector3 position, Vector3 size, float splitZ, Queue<(Vector3, Vector3)> toSplit)
    {
        Vector3 size1 = size;
        Vector3 size2 = size;
        
        float frontSize = splitZ - position.z;
        float backSize = position.z + size.z - splitZ;
        
        if (frontSize <= 0 || backSize <= 0)
            return;
        
        size1.z = frontSize;
        size2.z = backSize;
        
        Vector3 pos2 = position;
        pos2.z = splitZ;
        
        toSplit.Enqueue((position, size1));
        toSplit.Enqueue((pos2, size2));
    }

    private void CreateRoom(Vector3 position, Vector3 size, int floorIndex, int buildingIndex)
    {
        if (size.x <= 0.1f || size.z <= 0.1f || size.y <= 0.1f)
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
        
        if (minDim < minHallwayWidth && ratio > 2.5f)
            return RoomType.Hallway;
        
        if (minDim < 2f && maxDim < 3f)
            return RoomType.Bathroom;
        
        if (minDim > 2f && maxDim < 6f && ratio < 2f)
            return RoomType.Kitchen;
        
        if (minDim > 6f && maxDim > 8f)
            return RoomType.LivingRoom;
        
        if (minDim > 3f && maxDim < 7f)
            return RoomType.Bedroom;
        
        return RoomType.General;
    }

    private void CreateWallsAndDoors()
    {
        foreach (var room in rooms)
        {
            CreateWallsAroundRoom(room);
        }
        
        // Only connect rooms within the same building section
        ConnectAdjacentRooms();
    }

    private void CreateWallsAroundRoom(BuildingRoom room)
    {
        float x1 = room.position.x;
        float x2 = room.position.x + room.size.x;
        float z1 = room.position.z;
        float z2 = room.position.z + room.size.z;
        float y1 = room.position.y;
        float height = room.size.y;
        
        CreateWall(
            new Vector3(x1, y1 + height / 2, (z1 + z2) / 2),
            new Vector3(wallThickness, height, room.size.z),
            0
        );
        
        CreateWall(
            new Vector3(x2, y1 + height / 2, (z1 + z2) / 2),
            new Vector3(wallThickness, height, room.size.z),
            0
        );
        
        CreateWall(
            new Vector3((x1 + x2) / 2, y1 + height / 2, z1),
            new Vector3(room.size.x, height, wallThickness),
            1
        );
        
        CreateWall(
            new Vector3((x1 + x2) / 2, y1 + height / 2, z2),
            new Vector3(room.size.x, height, wallThickness),
            1
        );
    }

    private void CreateWall(Vector3 position, Vector3 size, int dimension)
    {
        var wall = new BuildingWall
        {
            position = position,
            size = size,
            dimension = dimension
        };
        walls.Add(wall);
    }

    private void ConnectAdjacentRooms()
    {
        // Group rooms by floor AND building section
        var roomsByFloorAndBuilding = rooms.GroupBy(r => (r.floorIndex, r.buildingIndex))
                                           .ToDictionary(g => g.Key, g => g.ToList());
        
        foreach (var floorRooms in roomsByFloorAndBuilding.Values)
        {
            for (int i = 0; i < floorRooms.Count; i++)
            {
                for (int j = i + 1; j < floorRooms.Count; j++)
                {
                    BuildingRoom r1 = floorRooms[i];
                    BuildingRoom r2 = floorRooms[j];
                    
                    if (Mathf.Abs((r1.position.x + r1.size.x) - r2.position.x) < 0.5f &&
                        r1.position.z < r2.position.z + r2.size.z &&
                        r1.position.z + r1.size.z > r2.position.z)
                    {
                        float doorZ = Mathf.Max(r1.position.z, r2.position.z) + 
                                     Mathf.Min(r1.size.z, r2.size.z) / 2;
                        float doorX = r1.position.x + r1.size.x;
                        AddDoor(i, j, new Vector3(doorX, r1.position.y + 1f, doorZ), 
                               dim: false, dir: true);
                    }
                    
                    if (Mathf.Abs((r1.position.z + r1.size.z) - r2.position.z) < 0.5f &&
                        r1.position.x < r2.position.x + r2.size.x &&
                        r1.position.x + r1.size.x > r2.position.x)
                    {
                        float doorX = Mathf.Max(r1.position.x, r2.position.x) + 
                                     Mathf.Min(r1.size.x, r2.size.x) / 2;
                        float doorZ = r1.position.z + r1.size.z;
                        AddDoor(i, j, new Vector3(doorX, r1.position.y + 1f, doorZ), 
                               dim: true, dir: true);
                    }
                }
            }
        }
    }

    private void ConnectAdjacentBuildings()
    {
        // Find buildings that are close to each other and add doors between them
        for (int i = 0; i < buildings.Count; i++)
        {
            for (int j = i + 1; j < buildings.Count; j++)
            {
                BuildingSection b1 = buildings[i];
                BuildingSection b2 = buildings[j];
                
                // Check if buildings are adjacent on X axis
                float b1_x_max = b1.position.x + b1.size.x;
                float b2_x_min = b2.position.x;
                float x_gap = Mathf.Abs(b2_x_min - b1_x_max);
                
                // Check if buildings are adjacent on Z axis
                float b1_z_max = b1.position.z + b1.size.z;
                float b2_z_min = b2.position.z;
                float z_gap = Mathf.Abs(b2_z_min - b1_z_max);
                
                // If buildings are close enough (within spacing distance), connect them
                if (x_gap < buildingSectionSpacing * 2)
                {
                    // Adjacent on X axis - find edge rooms and add doors
                    var b1_rooms = rooms.Where(r => r.buildingIndex == b1.buildingIndex && 
                                                     Mathf.Abs((r.position.x + r.size.x) - b1_x_max) < 1f).ToList();
                    var b2_rooms = rooms.Where(r => r.buildingIndex == b2.buildingIndex && 
                                                     Mathf.Abs(r.position.x - b2_x_min) < 1f).ToList();
                    
                    if (b1_rooms.Count > 0 && b2_rooms.Count > 0)
                    {
                        var room1 = b1_rooms[random.Next(b1_rooms.Count)];
                        var room2 = b2_rooms[random.Next(b2_rooms.Count)];
                        
                        float doorZ = (room1.position.z + room1.size.z / 2 + room2.position.z + room2.size.z / 2) / 2;
                        float doorX = (b1_x_max + b2_x_min) / 2;
                        
                        AddDoor(rooms.IndexOf(room1), rooms.IndexOf(room2), 
                               new Vector3(doorX, room1.position.y + 1f, doorZ), dim: false, dir: true);
                        
                        Debug.Log($"Connected Building {i} to Building {j} on X axis");
                    }
                }
                
                if (z_gap < buildingSectionSpacing * 2)
                {
                    // Adjacent on Z axis - find edge rooms and add doors
                    var b1_rooms = rooms.Where(r => r.buildingIndex == b1.buildingIndex && 
                                                     Mathf.Abs((r.position.z + r.size.z) - b1_z_max) < 1f).ToList();
                    var b2_rooms = rooms.Where(r => r.buildingIndex == b2.buildingIndex && 
                                                     Mathf.Abs(r.position.z - b2_z_min) < 1f).ToList();
                    
                    if (b1_rooms.Count > 0 && b2_rooms.Count > 0)
                    {
                        var room1 = b1_rooms[random.Next(b1_rooms.Count)];
                        var room2 = b2_rooms[random.Next(b2_rooms.Count)];
                        
                        float doorX = (room1.position.x + room1.size.x / 2 + room2.position.x + room2.size.x / 2) / 2;
                        float doorZ = (b1_z_max + b2_z_min) / 2;
                        
                        AddDoor(rooms.IndexOf(room1), rooms.IndexOf(room2), 
                               new Vector3(doorX, room1.position.y + 1f, doorZ), dim: true, dir: true);
                        
                        Debug.Log($"Connected Building {i} to Building {j} on Z axis");
                    }
                }
            }
        }
    }

    private void AddDoor(int roomA, int roomB, Vector3 position, bool dim, bool dir)
    {
        Vector3 doorSize;
        if (dim == false)
        {
            doorSize = new Vector3(wallThickness * 2, doorHeight, doorWidth);
        }
        else
        {
            doorSize = new Vector3(doorWidth, doorHeight, wallThickness * 2);
        }
        
        var door = new BuildingDoor
        {
            position = position,
            size = doorSize,
            roomA = roomA,
            roomB = roomB,
            dim = dim,
            dir = dir
        };
        doors.Add(door);
    }

    private void GenerateStairs()
    {
        // Generate stairs for each building section separately
        foreach (var building in buildings)
        {
            GenerateStairsForBuilding(building);
        }
    }

    private void GenerateStairsForBuilding(BuildingSection building)
    {
        var buildingRooms = rooms.Where(r => r.buildingIndex == building.buildingIndex).ToList();
        var floorIndices = buildingRooms.Select(r => r.floorIndex).Distinct().OrderBy(f => f).ToList();
        
        Debug.Log($"Building {building.buildingIndex}: floors {string.Join(", ", floorIndices)}");
        
        for (int i = 0; i < floorIndices.Count - 1; i++)
        {
            int floorA = floorIndices[i];
            int floorB = floorIndices[i + 1];
            
            var roomsFloorA = buildingRooms.Where(r => r.floorIndex == floorA).ToList();
            var roomsFloorB = buildingRooms.Where(r => r.floorIndex == floorB).ToList();
            
            if (roomsFloorA.Count == 0)
                continue;
            
            BuildingRoom stairRoom = FindBestStairRoom(roomsFloorA);
            if (stairRoom == null)
                continue;
            
            float padding = 0.2f;
            float stairX = stairRoom.position.x + padding;
            float stairZ = stairRoom.position.z + padding;
            float stairY = stairRoom.position.y;
            
            if (stairX + stairDepth + padding > stairRoom.position.x + stairRoom.size.x)
                stairX = stairRoom.position.x + stairRoom.size.x - stairDepth - padding;
            
            if (stairZ + stairWidth + padding > stairRoom.position.z + stairRoom.size.z)
                stairZ = stairRoom.position.z + stairRoom.size.z - stairWidth - padding;
            
            bool dim = random.Next(0, 2) == 0;
            
            var staircase = new BuildingStairs
            {
                position = new Vector3(stairX, stairY, stairZ),
                size = dim ? new Vector3(stairDepth, floorHeight, stairWidth) : 
                           new Vector3(stairWidth, floorHeight, stairDepth),
                floorA = floorA,
                floorB = floorB,
                dim = dim
            };
            
            stairs.Add(staircase);
            Debug.Log($"Building {building.buildingIndex}: Created stairs from floor {floorA} to {floorB}");
        }
    }

    private BuildingRoom FindBestStairRoom(List<BuildingRoom> roomList)
    {
        var hallways = roomList.Where(r => r.roomType == RoomType.Hallway).ToList();
        if (hallways.Count > 0)
            return hallways[random.Next(hallways.Count)];
        
        var largeRooms = roomList.Where(r => r.size.x > stairDepth + 1 && r.size.z > stairWidth + 1).ToList();
        if (largeRooms.Count > 0)
            return largeRooms[random.Next(largeRooms.Count)];
        
        var fitRooms = roomList.Where(r => 
            (r.size.x > stairDepth && r.size.z > stairWidth) ||
            (r.size.x > stairWidth && r.size.z > stairDepth)).ToList();
        
        if (fitRooms.Count > 0)
            return fitRooms[random.Next(fitRooms.Count)];
        
        return null;
    }

    private void InstantiateGeometry()
    {
        Transform parent = this.transform;
        
        Debug.Log($"Instantiating {rooms.Count} rooms across {buildings.Count} building sections...");
        
        // Create a parent for each building section
        Dictionary<int, Transform> buildingParents = new Dictionary<int, Transform>();
        foreach (var building in buildings)
        {
            var buildingObj = new GameObject($"Building_{building.buildingIndex} ({building.roomsInSection} rooms)");
            buildingObj.transform.parent = parent;
            buildingObj.transform.position = building.position;
            buildingParents[building.buildingIndex] = buildingObj.transform;
        }
        
        foreach (var room in rooms)
        {
            Transform buildingParent = buildingParents[room.buildingIndex];
            
            Vector3 floorPos = room.position + new Vector3(room.size.x / 2, floorCeilThickness / 2, room.size.z / 2);
            GameObject floor = Instantiate(floorPrefab, floorPos, Quaternion.identity, buildingParent);
            floor.name = $"Floor_{room.roomType}_{room.floorIndex}";
            floor.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);
            
            Vector3 ceilPos = new Vector3(
                room.position.x + room.size.x / 2,
                room.position.y + room.size.y - floorCeilThickness / 2,
                room.position.z + room.size.z / 2
            );
            GameObject ceil = Instantiate(ceilingPrefab, ceilPos, Quaternion.identity, buildingParent);
            ceil.name = $"Ceiling_{room.roomType}_{room.floorIndex}";
            ceil.transform.localScale = new Vector3(room.size.x, floorCeilThickness, room.size.z);
        }
        
        Debug.Log($"Instantiating {walls.Count} walls...");
        
        int wallCount = 0;
        foreach (var wall in walls)
        {
            GameObject wallObj = Instantiate(wallPrefab, wall.position, Quaternion.identity, parent);
            wallObj.name = $"Wall_{wallCount++}";
            wallObj.transform.localScale = wall.size;
        }
        
        Debug.Log($"Instantiating {doors.Count} doors...");
        
        int doorCount = 0;
        foreach (var door in doors)
        {
            GameObject doorObj = Instantiate(doorPrefab, door.position, Quaternion.identity, parent);
            doorObj.name = $"Door_{doorCount++}";
            doorObj.transform.localScale = door.size;
        }
        
        Debug.Log($"Instantiating {stairs.Count} staircases...");
        
        int stairCount = 0;
        foreach (var staircase in stairs)
        {
            GameObject stairObj;
            if (stairPrefab != null)
            {
                stairObj = Instantiate(stairPrefab, staircase.position, Quaternion.identity, parent);
            }
            else
            {
                stairObj = new GameObject($"Stairs_{stairCount}");
                stairObj.transform.parent = parent;
                stairObj.transform.position = staircase.position;
                
                var meshFilter = stairObj.AddComponent<MeshFilter>();
                var meshRenderer = stairObj.AddComponent<MeshRenderer>();
                meshFilter.mesh = CreateStairMesh();
                meshRenderer.material = new Material(Shader.Find("Standard"));
                meshRenderer.material.color = Color.gray;
                
                stairObj.AddComponent<BoxCollider>();
            }
            
            stairObj.name = $"Stairs_{stairCount++}";
            stairObj.transform.localScale = staircase.size;
        }
    }

    private Mesh CreateStairMesh()
    {
        Mesh mesh = new Mesh();
        
        Vector3[] vertices = new Vector3[8]
        {
            new Vector3(0, 0, 0),
            new Vector3(1, 0, 0),
            new Vector3(1, 1, 0),
            new Vector3(0, 1, 0),
            new Vector3(0, 0, 1),
            new Vector3(1, 0, 1),
            new Vector3(1, 1, 1),
            new Vector3(0, 1, 1)
        };
        
        int[] triangles = new int[36]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3,
            1, 2, 6, 1, 6, 5
        };
        
        mesh.vertices = vertices;
        mesh.triangles = triangles;
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
        
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }

    [ContextMenu("Generate Building")]
    public void RegenerateBuilding()
    {
        randomSeed = Random.Range(0, 100000);
        GenerateBuilding();
    }
}