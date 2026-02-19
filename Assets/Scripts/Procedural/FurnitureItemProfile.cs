using UnityEngine;

/// <summary>
/// Defines what small items can spawn on furniture (tables, shelves, etc.)
/// Create via: Right-click ? Create ? Procedural Building ? Furniture Item Profile
/// 
/// Examples:
///   - Kitchen tables: plates, cups, utensils
///   - Bedroom nightstands: lamps, books, alarm clocks
///   - Living room tables: magazines, remote controls, decorations
///   - Bathroom counters: toiletries, towels
/// </summary>
[CreateAssetMenu(fileName = "FurnitureItemProfile", 
                 menuName = "Procedural Building/Furniture Item Profile")]
public class FurnitureItemProfile : ScriptableObject
{
    [Header("Item Spawning")]
    [Tooltip("Items that can spawn on this type of furniture.")]
    public PropEntry[] items;

    [Tooltip("Minimum items to spawn on each furniture piece.")]
    public int minItems = 0;

    [Tooltip("Maximum items to spawn on each furniture piece.")]
    public int maxItems = 3;

    [Header("Spawn Behavior")]
    [Tooltip("Percentage chance (0-1) that each available spawn point gets an item. 1.0 = always fill, 0.5 = 50% chance per point.")]
    [Range(0f, 1f)]
    public float fillProbability = 0.7f;

    [Tooltip("If true, tries to fill spawn points evenly. If false, picks random points.")]
    public bool distributeEvenly = true;
}