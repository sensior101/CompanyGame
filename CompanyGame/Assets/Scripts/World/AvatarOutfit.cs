using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dresses an avatar visual (MaleVisual / FemaleVisual and everything made from them) in a <see cref="CharacterOutfit"/>.
/// Each garment is a SkinnedMeshRenderer on the avatar's own bones, so it bends with every pose, walk, sit or punch.
/// The body mesh is swapped for the outfit's masked body, which leaves out the skin under the clothes.
/// Runs before other components look at the avatar's renderers (first-person arms, inventory preview).
/// </summary>
[DefaultExecutionOrder(-600)]
[DisallowMultipleComponent]
public sealed class AvatarOutfit : MonoBehaviour
{
    public CharacterOutfit outfit;
    [Tooltip("Use the colours below instead of the outfit's own materials.")]
    public bool tint;
    public Color top = Color.white, bottom = Color.white;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    readonly List<SkinnedMeshRenderer> garments = new List<SkinnedMeshRenderer>();
    readonly List<int> slots = new List<int>();
    bool built;

    public IReadOnlyList<SkinnedMeshRenderer> Garments => garments;

    void Awake() => Build();

    /// <summary>Colours for the top and the bottom (citizens, staff uniforms). Applies now if already dressed.</summary>
    public void SetColors(Color topColor, Color bottomColor)
    {
        tint = true; top = topColor; bottom = bottomColor;
        ApplyColors();
    }

    /// <summary>Puts the clothes on once. Safe to call early (an editor test, a preview copy).</summary>
    public void Build()
    {
        if (built || !outfit) return;
        var body = FindBody();
        if (!body) return;
        built = true;
        var bones = new Dictionary<string, Transform>();
        foreach (var bone in body.bones) if (bone) bones[bone.name] = bone;
        if (body.rootBone)
            foreach (var t in body.rootBone.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;

        if (outfit.maskedBody && TryMap(outfit.maskedBodyBones, bones, out var bodyBones))
        {
            int slotsWanted = outfit.maskedBody.subMeshCount;
            var materials = body.sharedMaterials;
            if (materials.Length > slotsWanted) System.Array.Resize(ref materials, slotsWanted);
            body.sharedMesh = outfit.maskedBody;
            body.bones = bodyBones;
            body.sharedMaterials = materials;
        }
        foreach (var piece in outfit.pieces)
        {
            if (piece == null || !piece.mesh || !TryMap(piece.bones, bones, out var pieceBones)) continue;
            var go = new GameObject(piece.name);
            go.layer = body.gameObject.layer;
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = piece.mesh;
            skin.bones = pieceBones;
            skin.rootBone = body.rootBone;
            skin.localBounds = body.localBounds;
            skin.updateWhenOffscreen = body.updateWhenOffscreen;
            skin.quality = body.quality;
            skin.shadowCastingMode = body.shadowCastingMode;
            skin.receiveShadows = body.receiveShadows;
            skin.sharedMaterial = piece.material;
            skin.enabled = body.enabled;
            garments.Add(skin); slots.Add(piece.slot);
        }
        ApplyColors();
    }

    SkinnedMeshRenderer FindBody()
    {
        foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.sharedMesh || garments.Contains(skin)) continue;
            bool isPiece = false;
            foreach (var piece in outfit.pieces) if (piece != null && piece.mesh == skin.sharedMesh) isPiece = true;
            if (!isPiece) return skin;
        }
        return null;
    }

    static bool TryMap(string[] names, Dictionary<string, Transform> bones, out Transform[] mapped)
    {
        mapped = new Transform[names.Length];
        for (int i = 0; i < names.Length; i++)
            if (!bones.TryGetValue(names[i], out mapped[i])) { Debug.LogWarning("[AvatarOutfit] bone missing: " + names[i]); return false; }
        return true;
    }

    void ApplyColors()
    {
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < garments.Count; i++)
        {
            if (!garments[i]) continue;
            garments[i].GetPropertyBlock(block);
            if (tint) block.SetColor(BaseColor, slots[i] == 0 ? top : bottom);
            else block.Clear();
            garments[i].SetPropertyBlock(block);
        }
    }
}
