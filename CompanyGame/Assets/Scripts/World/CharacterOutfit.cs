using System;
using UnityEngine;

/// <summary>
/// A set of skinned clothes for one avatar body (Meshy articulated male or female). Every mesh is already in the body
/// mesh's space with the body's bind poses, so it only needs the avatar's own bones by name (<see cref="AvatarOutfit"/>).
/// Built by CharacterOutfitBuilder from ArtSource/Daldongne/Characters/Outfits_20261111.
/// </summary>
[CreateAssetMenu(menuName = "CompanyGame/Character Outfit")]
public sealed class CharacterOutfit : ScriptableObject
{
    [Serializable]
    public sealed class Piece
    {
        public string name;
        public Mesh mesh;
        public Material material;
        public string[] bones;
        [Tooltip("Tint slot: 0 = top, 1 = bottom.")]
        public int slot;
    }

    public Piece[] pieces = Array.Empty<Piece>();
    [Tooltip("The body without the skin the clothes cover, so it never shows through them. Empty keeps the full body.")]
    public Mesh maskedBody;
    public string[] maskedBodyBones = Array.Empty<string>();
}
