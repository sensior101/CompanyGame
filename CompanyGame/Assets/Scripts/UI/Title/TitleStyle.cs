using TMPro;
using UnityEngine;

/// <summary>
/// Font and rounded sprite shared by the title screen and the in-game pause menu.
/// Lives in Resources/Title/TitleStyle.asset (made by the title scene builder) so both can load it without a scene reference.
/// </summary>
public sealed class TitleStyle : ScriptableObject
{
    public const string ResourcePath = "Title/TitleStyle";

    public TMP_FontAsset font;
    public Sprite rounded;

    static TitleStyle cached;

    public static TitleStyle Load() => cached ? cached : cached = Resources.Load<TitleStyle>(ResourcePath);
}
