using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A switchable lamp on a furniture item or a fixed lighting fixture.</summary>
[DisallowMultipleComponent]
public sealed class FurnitureLight : MonoBehaviour
{
    [Serializable]
    public struct EmissionTarget
    {
        public Renderer renderer;
        [Min(0)] public int materialIndex;
    }

    public WorldObject owner;
    public Transform interactionPoint;
    [Min(.5f)] public float interactionRadius = 1.8f;
    public Light[] lights = Array.Empty<Light>();
    [Tooltip("Only the bulb's material slots; the shade and frame must not be included.")]
    public EmissionTarget[] emissionTargets = Array.Empty<EmissionTarget>();
    [ColorUsage(false, true)] public Color bulbEmission = new Color(2f, 2f, 2f);
    [SerializeField] bool isOn = true;
    public bool IsOn => isOn;

    static readonly HashSet<FurnitureLight> active = new HashSet<FurnitureLight>();
    static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock properties;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => active.Clear();

    void Awake() { if (!owner) owner = GetComponentInParent<WorldObject>(); }
    void OnEnable() { active.Add(this); Apply(); }
    void OnDisable() { active.Remove(this); Apply(false); }

    public void SetOn(bool value) { isOn = value; Apply(); }
    public void Toggle() => SetOn(!isOn);
    void Apply() => Apply(isOn && isActiveAndEnabled);
    void Apply(bool illuminated)
    {
        foreach (var lamp in lights)
            if (lamp) lamp.enabled = illuminated;
        if (properties == null) properties = new MaterialPropertyBlock();
        foreach (var target in emissionTargets)
        {
            if (!target.renderer || target.materialIndex < 0 || target.materialIndex >= target.renderer.sharedMaterials.Length) continue;
            // Per-instance properties keep identical lamps independently switchable.
            properties.Clear();
            target.renderer.GetPropertyBlock(properties, target.materialIndex);
            properties.SetColor(EmissionColor, illuminated ? bulbEmission : Color.black);
            target.renderer.SetPropertyBlock(properties, target.materialIndex);
        }
    }

    public static FurnitureLight FindNearest(Transform player)
    {
        if (!player) return null;
        FurnitureLight nearest = null;
        float best = float.PositiveInfinity;
        foreach (var lamp in active)
        {
            if (!lamp || !lamp.isActiveAndEnabled || !lamp.owner ||
                !lamp.owner.HasFunction(FurnitureFunction.Lighting) || lamp.gameObject.scene != SceneLoadManager.CurrentMap) continue;
            Vector3 point = lamp.interactionPoint ? lamp.interactionPoint.position : lamp.transform.position + Vector3.up * .6f;
            Vector3 delta = player.position - point;
            float distance = new Vector2(delta.x, delta.z).sqrMagnitude;
            if (Mathf.Abs(delta.y) > 2.5f || distance > lamp.interactionRadius * lamp.interactionRadius || distance >= best) continue;
            Vector3 start = player.position + Vector3.up * 1.2f;
            Vector3 ray = point - start;
            bool blocked = false;
            foreach (var hit in Physics.RaycastAll(start, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(player) && !hit.transform.IsChildOf(lamp.owner.transform)) { blocked = true; break; }
            if (blocked) continue;
            nearest = lamp; best = distance;
        }
        return nearest;
    }
}
