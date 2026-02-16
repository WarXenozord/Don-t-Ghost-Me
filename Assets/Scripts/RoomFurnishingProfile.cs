using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  PROP ENTRY  — one spawnable item inside a room profile
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class PropEntry
{
    [Tooltip("The prefab to spawn.")]
    public GameObject prefab;

    [Tooltip("Relative spawn weight among props of the same category. Higher = more likely.")]
    public float weight = 1f;

    [Tooltip("World-unit footprint on the XZ plane (width).")]
    public float footprintX = 1f;

    [Tooltip("World-unit footprint on the XZ plane (depth).")]
    public float footprintZ = 1f;

    [Tooltip("If true, this prop will try to place itself against a wall before falling back to center placement.")]
    public bool prefersWall = false;

    [Tooltip("Y offset from the floor (0 = sits on floor, use for floating or raised props).")]
    public float yOffset = 0f;
}

// ─────────────────────────────────────────────────────────────────────────────
//  ROOM FURNISHING PROFILE  — ScriptableObject, one per RoomType
//  Create via: right-click in Project → Create → Procedural Building → Room Furnishing Profile
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(
    fileName = "RoomFurnishingProfile",
    menuName  = "Procedural Building/Room Furnishing Profile")]
public class RoomFurnishingProfile : ScriptableObject
{
    [Header("Room Type")]
    public RoomType roomType;

    [Header("Floor Props")]
    [Tooltip("Props that sit on the floor. Large items (bed, couch) should have prefersWall = true.")]
    public PropEntry[] floorProps;

    [Tooltip("Minimum number of floor props to attempt to place (may be less if room is too small).")]
    public int minFloorProps = 1;

    [Tooltip("Maximum number of floor props to attempt to place.")]
    public int maxFloorProps = 4;

    [Header("Wall Props  (Paintings, Sconces…)")]
    [Tooltip("Props that hang on walls. Footprint is ignored; placement is along the wall surface.")]
    public PropEntry[] wallProps;

    [Tooltip("Maximum number of wall props per room.")]
    public int maxWallProps = 2;

    [Tooltip("Height above floor at which wall props are placed.")]
    public float wallPropHeight = 1.5f;

    [Tooltip("How far the wall prop sits in front of the wall surface.")]
    public float wallPropInset = 0.05f;

    [Header("Ceiling Props  (Lamps…)")]
    public PropEntry[] ceilingProps;

    [Tooltip("Room area (m²) below which only one ceiling lamp is placed.")]
    public float singleLampAreaThreshold = 16f;

    [Tooltip("Y offset downward from the ceiling (negative = hanging down).")]
    public float ceilingPropYOffset = -0.1f;
}
