using UnityEngine;

/// <summary>World placement and available functions are separate from inventory item types.</summary>
[DisallowMultipleComponent]
public sealed class WorldObject : MonoBehaviour
{
    static readonly System.Collections.Generic.HashSet<WorldObject> active = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => active.Clear();
    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);
    public string InteractionId => gameObject.scene.path + ":" + ObjectPath(transform);
    static string ObjectPath(Transform t) => (t.parent ? ObjectPath(t.parent) + "/" : "") + t.name + "[" + t.GetSiblingIndex() + "]";
    public bool InInteractionRange(Transform player) => player && isActiveAndEnabled && gameObject.scene == SceneLoadManager.CurrentMap &&
        Mathf.Abs(player.position.y-transform.position.y)<1.5f && Vector2.Distance(new Vector2(player.position.x,player.position.z),new Vector2(transform.position.x,transform.position.z))<=2f;
    public static WorldObject FindNearest(Transform player,FurnitureFunction function)
    {
        WorldObject best=null;float distance=float.MaxValue;
        foreach(var world in active)if(world && world.HasFunction(function)&&world.InInteractionRange(player))
        {float d=(world.transform.position-player.position).sqrMagnitude;if(d<distance){distance=d;best=world;}}
        return best;
    }
    public WorldObjectType objectType = WorldObjectType.StaticProp;
    public FurnitureFunction functions;
    [Tooltip("Only player-placeable furniture references an inventory definition.")]
    public ItemData furnitureItem;
    public bool HasFunction(FurnitureFunction function) => function != FurnitureFunction.None && (functions & function) == function;
    // Placement code must supply the verified current-residence permission, never just ownership of any building.
    public bool CanModifyInResidence(bool isPlayersCurrentResidence) =>
        objectType == WorldObjectType.PlaceableFurniture && isPlayersCurrentResidence;
}
