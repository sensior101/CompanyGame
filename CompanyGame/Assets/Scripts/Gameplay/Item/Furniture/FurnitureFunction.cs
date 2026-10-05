[System.Flags]
public enum FurnitureFunction
{
    None = 0,
    Seating = 1 << 0,
    Surface = 1 << 1,
    Storage = 1 << 2,
    Sleep = 1 << 3,
    Lighting = 1 << 4,
    Cooking = 1 << 5,
    WaterSource = 1 << 6,
    Display = 1 << 7,
    Music = 1 << 8,
    Decoration = 1 << 9
}
