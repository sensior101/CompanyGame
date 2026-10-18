using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps NPC cars on the map's lanes. Every PC spawns and drives its own cars (also on a server, until stage 5).
/// </summary>
public sealed class TrafficSpawner : MonoBehaviour
{
    public const int MaxCars = 40;
    const float MinSpacing = 8f;
    const float MinPlayerDistance = 12f;
    const float EndMargin = 8f;              // do not drop a car right at a stop line

    [Range(0, MaxCars)] public int count = 8;
    public TrafficLane[] lanes = Array.Empty<TrafficLane>();

    readonly Dictionary<int, TrafficCar> cars = new Dictionary<int, TrafficCar>();
    readonly List<TrafficLane> spawnLanes = new List<TrafficLane>();
    List<ItemData> models;
    int nextKey;
    float nextAttempt;

    public IEnumerable<TrafficCar> Cars => cars.Values;

    void Awake()
    {
        if (lanes == null || lanes.Length == 0) lanes = GetComponentsInChildren<TrafficLane>();
        foreach (TrafficLane lane in lanes)
            if (lane && !lane.connector && lane.Count > 1 && lane.Length > EndMargin * 2f) spawnLanes.Add(lane);
        models = LoadModels();
    }

    static List<ItemData> LoadModels()
    {
        var list = new List<ItemData>();
        foreach (ItemData item in Resources.LoadAll<ItemData>("Inventory/Vehicles/Cars"))
            if (item && item.vehiclePrefab) list.Add(item);
        return list;
    }

    void Update()
    {
        Reap();
        if (Time.time < nextAttempt || cars.Count >= Mathf.Min(count, MaxCars)) return;
        nextAttempt = Time.time + (cars.Count == 0 ? 0f : .5f);
        TrySpawn();
    }

    void Reap()
    {
        List<int> done = null;
        foreach (KeyValuePair<int, TrafficCar> pair in cars)
            if (!pair.Value || pair.Value.Finished) (done ??= new List<int>()).Add(pair.Key);
        if (done == null) return;
        foreach (int key in done)
        {
            if (cars[key]) Destroy(cars[key].gameObject);
            cars.Remove(key);
        }
    }

    bool TrySpawn()
    {
        if (models.Count == 0 || spawnLanes.Count == 0) return false;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            TrafficLane lane = PickLane();
            float s = UnityEngine.Random.Range(EndMargin, lane.Length - EndMargin);
            if (!IsClear(lane.Sample(s, out _, out _))) continue;
            Create(models[UnityEngine.Random.Range(0, models.Count)], lane, s, nextKey++);
            return true;
        }
        return false;
    }

    // Longer lanes get proportionally more cars.
    TrafficLane PickLane()
    {
        float total = 0f;
        foreach (TrafficLane lane in spawnLanes) total += lane.Length;
        float roll = UnityEngine.Random.value * total;
        foreach (TrafficLane lane in spawnLanes)
        {
            roll -= lane.Length;
            if (roll <= 0f) return lane;
        }
        return spawnLanes[spawnLanes.Count - 1];
    }

    bool IsClear(Vector3 p)
    {
        foreach (TrafficCar car in cars.Values)
            if (car && (car.Position - p).sqrMagnitude < MinSpacing * MinSpacing) return false;
        Behaviour player = SceneLoadManager.Traveller;
        return !player || (player.transform.position - p).sqrMagnitude > MinPlayerDistance * MinPlayerDistance;
    }

    TrafficCar Create(ItemData item, TrafficLane lane, float s, int key)
    {
        GameObject go = Instantiate(item.vehiclePrefab, Vector3.zero, Quaternion.identity, transform);
        go.name = "NpcCar_" + key;
        TrafficCar car = go.AddComponent<TrafficCar>();
        car.grade = item.grade;
        car.wheelRadius = item.wheelRadius;
        car.itemId = item.itemId;
        car.key = key;
        car.Init(lane, s);
        cars[key] = car;
        return car;
    }
}

/// <summary>Wires gameplay-level code that cannot see World (SpeedCamera) to the player.</summary>
static class TrafficBridge
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Hook()
    {
        SpeedCamera.PlayerProvider = () => SceneLoadManager.Traveller ? SceneLoadManager.Traveller.transform : null;
    }
}
