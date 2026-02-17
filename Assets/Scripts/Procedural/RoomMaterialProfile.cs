using UnityEngine;

/// <summary>
/// Defines the floor, ceiling and wall materials for one RoomType.
/// Create via: right-click in Project → Create → Procedural Building → Room Material Profile
/// 
/// One asset per RoomType — assign all of them to PropSpawner.materialProfiles[].
/// Unassigned slots (null material) leave the existing material untouched.
/// </summary>
[CreateAssetMenu(
    fileName = "RoomMaterialProfile",
    menuName  = "Procedural Building/Room Material Profile")]
public class RoomMaterialProfile : ScriptableObject
{
    public RoomType roomType;

    [Tooltip("Applied to the floor quad of every room of this type. Null = keep default.")]
    public Material floorMaterial;

    [Tooltip("Applied to the ceiling quad of every room of this type. Null = keep default.")]
    public Material ceilingMaterial;

    [Tooltip("Applied to wall segments that border a room of this type. " +
             "For shared walls between two different types the higher-priority type wins " +
             "(priority = order in PropSpawner.materialProfiles array, first = highest).")]
    public Material wallMaterial;
}
