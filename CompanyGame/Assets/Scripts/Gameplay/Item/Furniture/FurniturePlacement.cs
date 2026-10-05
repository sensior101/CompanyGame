/// <summary>Authoring classification only. Placement validation is supplied by the residence system.</summary>
[System.Flags]
public enum FurniturePlacement
{
    None = 0,
    Floor = 1 << 0,
    Surface = 1 << 1,
    Wall = 1 << 2,
    Window = 1 << 3
}
