using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared, instance-only tint and smoke for held and dropped spoiled food.</summary>
[DisallowMultipleComponent]
public sealed class FoodSpoilageVisual : MonoBehaviour
{
    struct Surface
    {
        public Renderer renderer;
        public int index;
        public MaterialPropertyBlock original, tinted;
    }
    readonly List<Surface> surfaces = new List<Surface>();
    ParticleSystem smoke;
    Material smokeMaterial;
    bool spoiled;
    public bool IsSpoiled => spoiled;

    public static void Set(Transform root, bool value)
    {
        var effect = root.GetComponent<FoodSpoilageVisual>();
        if (!effect && value) effect = root.gameObject.AddComponent<FoodSpoilageVisual>();
        if (effect) effect.Show(value);
    }

    void Show(bool value)
    {
        if (spoiled == value) return;
        if (surfaces.Count == 0) CacheSurfaces();
        spoiled = value;
        foreach (var surface in surfaces)
            if (surface.renderer) surface.renderer.SetPropertyBlock(value ? surface.tinted : surface.original, surface.index);
        if (value)
        {
            if (!smoke) BuildSmoke();
            if (smoke) smoke.Play();
        }
        else if (smoke) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void CacheSurfaces()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer) continue;
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (!material) continue;
                var original = new MaterialPropertyBlock();
                var tinted = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, i);
                renderer.GetPropertyBlock(tinted, i);
                foreach (string property in new[] { "_BaseColor", "_Color" })
                {
                    if (!material.HasProperty(property)) continue;
                    Color color = material.GetColor(property);
                    var green = new Color(color.r * .63f, color.g * .94f, color.b * .49f, color.a);
                    tinted.SetColor(property, Color.Lerp(color, green, .65f));
                }
                surfaces.Add(new Surface { renderer = renderer, index = i, original = original, tinted = tinted });
            }
        }
    }

    void BuildSmoke()
    {
        var source = Resources.Load<Material>("Effects/DroppedItemGlow");
        if (!source) return;
        smokeMaterial = new Material(source) { name = "SpoiledFoodSmoke", hideFlags = HideFlags.DontSave };
        smokeMaterial.SetColor("_Tint", new Color(.36f, .72f, .12f, .65f));
        smokeMaterial.SetFloat("_Intensity", .65f);
        smokeMaterial.SetFloat("_Beam", 0f);
        var host = new GameObject("SpoiledFoodSmoke");
        host.transform.SetParent(transform, false);
        host.layer = surfaces.Count > 0 ? surfaces[0].renderer.gameObject.layer : gameObject.layer;
        var bounds = new Bounds(transform.position, Vector3.zero);
        bool found = false;
        foreach (var surface in surfaces)
            if (surface.renderer) { if (!found) { bounds = surface.renderer.bounds; found = true; } else bounds.Encapsulate(surface.renderer.bounds); }
        host.transform.localPosition = transform.InverseTransformPoint(bounds.center);
        smoke = host.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main;
        main.loop = true; main.playOnAwake = false; main.duration = 2f; main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(.06f, .11f);
        main.startSpeed = 0f; main.startColor = Color.white;
        var emission = smoke.emission; emission.rateOverTime = 8f;
        var shape = smoke.shape; shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Clamp(bounds.extents.magnitude * .3f, .025f, .08f);
        var velocity = smoke.velocityOverLifetime; velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-.025f, .025f);
        velocity.y = new ParticleSystem.MinMaxCurve(.09f, .16f);
        velocity.z = new ParticleSystem.MinMaxCurve(-.025f, .025f);
        var size = smoke.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .5f, 1f, 2.4f));
        var color = smoke.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .2f), new GradientAlphaKey(0, 1) });
        color.color = gradient;
        var renderer = smoke.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = smokeMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
    }

    void OnDestroy()
    {
        foreach (var surface in surfaces)
            if (surface.renderer) surface.renderer.SetPropertyBlock(surface.original, surface.index);
        if (smokeMaterial) Destroy(smokeMaterial);
        if (smoke) Destroy(smoke.gameObject);
    }
}
