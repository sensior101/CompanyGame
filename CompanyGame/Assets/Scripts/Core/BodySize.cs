/// <summary>
/// One body for the player and the citizens (same avatar model): the player's CharacterController, the citizen
/// capsule/NavMeshAgent and the citizen NavMesh bake all read these, so both walk exactly the same places.
/// StepHeight .35: curbs and stair steps pass, tables, benches and sofas (.4 m and up) do not.
/// </summary>
public static class BodySize
{
    public const float Radius = .3f, Height = 1.8f, StepHeight = .35f;
}
