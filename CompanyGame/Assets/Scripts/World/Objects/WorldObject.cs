using UnityEngine;

/// <summary>World placement and available functions are separate from inventory item types.</summary>
[DisallowMultipleComponent]
public sealed class WorldObject : MonoBehaviour
{
    public WorldObjectType objectType = WorldObjectType.StaticProp;
    public FurnitureFunction functions;
    [Tooltip("Only player-placeable furniture references an inventory definition.")]
    public ItemData furnitureItem;
    public bool HasFunction(FurnitureFunction function) => function != FurnitureFunction.None && (functions & function) == function;
    // Placement code must supply the verified current-residence permission, never just ownership of any building.
    public bool CanModifyInResidence(bool isPlayersCurrentResidence) =>
        objectType == WorldObjectType.PlaceableFurniture && isPlayersCurrentResidence;
}
