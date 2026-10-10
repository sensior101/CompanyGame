using UnityEngine;
using UnityEngine.Serialization;

// Keep serialized values stable. Accessory is retained only for old item assets.
public enum ItemCategory { General = 0, Top = 1, Bottom = 2, Socks = 3, Shoes = 4, Accessory = 5, Pet = 6, Bag = 7, Currency = 8, Weapon = 9,
    Food = 10, Material = 11, Furniture = 12, Vehicle = 13, Fish = 14, Document = 15, Book = 16 }
public enum EquipmentSlot { Top = 0, Bottom = 1, Socks = 2, Shoes = 3, Accessory = 4, Pet = 5 }
public enum WeaponKind { Melee = 0, Firearm = 1 }

/// <summary>Shared item definition. Inventories store these assets, never scene objects.</summary>
[CreateAssetMenu(fileName = "Item", menuName = "Company Game/Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public string displayName;
    public Sprite icon;
    [Min(0)] public long price;
    public ItemType itemType = ItemType.Misc;
    [SerializeField, HideInInspector, FormerlySerializedAs("category")] ItemCategory legacyCategory;
    [SerializeField, HideInInspector] int classificationVersion;
    [Header("Consumable classification (expiry system pending)")]
    public ConsumableType consumableType;
    [Tooltip("Configured shelf life. Zero means not configured; expiry tracking is a future system.")]
    [Min(0f)] public float shelfLifeHours;
    [Header("Clothing / equipment")]
    public EquipmentSlot clothingSlot;
    public bool isBag;
    [Header("Furniture classification")]
    public FurnitureFunction furnitureFunctions;
    public StorageType storageType;
    public FurniturePlacement furniturePlacement = FurniturePlacement.Floor;
    public GameObject furniturePrefab;
    public FurnitureTheme furnitureTheme;
    [Tooltip("Book subtype for the Books storage filter; independent of furniture functions.")]
    public bool isBook;
    [Header("Tool classification")]
    public bool isWeapon;
    [Min(1)] public int maxStack = 1;
    [SerializeField] private long currencyValue;

    [Header("Food effects (per item)")]
    [Min(0f)] public float healthRestore;
    [Min(0f)] public float staminaRestore;
    [Min(0f)] public float stressRelief;
    public bool IsConsumable => itemType == ItemType.Consumable &&
        (healthRestore > 0f || staminaRestore > 0f || stressRelief > 0f);

    [Header("Held appearance")]
    public GameObject heldPrefab;
    public Vector3 heldLocalPosition = Vector3.zero;
    public Vector3 heldLocalEulerAngles = Vector3.zero;
    public Vector3 heldLocalScale = Vector3.one;

    [Header("Portable vehicle")]
    public GameObject vehiclePrefab;
    [Min(1f)] public float vehicleSpeed = 8f;
    public bool IsVehicle => category == ItemCategory.Vehicle;

    [Header("Weapon")]
    public WeaponKind weaponKind;
    [Min(0.1f)] public float weaponDamage = 25f;
    [Min(0.1f)] public float weaponRange = 50f;
    [Min(0.1f)] public float attacksPerSecond = 3f;

    [Header("Property deed")]
    public string propertyId;
    public string propertyName;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public bool IsBook => category == ItemCategory.Book;
    public int StackLimit => IsBook ? 99 : IsStackable ? Mathf.Max(1, maxStack) : 1;
    bool IsStackable => IsCurrency || category == ItemCategory.General || category == ItemCategory.Food ||
        category == ItemCategory.Material || category == ItemCategory.Fish;
    public bool IsCurrency => category == ItemCategory.Currency && currencyValue >= 0;
    public bool IsDeed => category == ItemCategory.Document && !string.IsNullOrEmpty(propertyId);
    public bool IsWeapon => category == ItemCategory.Weapon;
    public long CurrencyValue => IsCurrency ? currencyValue : 0;

    internal void ConfigureCurrency(long value)
    {
        if (value < 0) throw new System.ArgumentOutOfRangeException(nameof(value));
        category = ItemCategory.Currency;
        currencyValue = value;
        maxStack = 99;
        itemId = "cash:" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        name = itemId;
        displayName = value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "원";
    }

    public bool Fits(EquipmentSlot slot)
    {
        return slot != EquipmentSlot.Accessory &&
            ((itemType == ItemType.Clothing && slot == clothingSlot) || (itemType == ItemType.Pet && slot == EquipmentSlot.Pet));
    }

    void OnEnable()
    {
        if (classificationVersion == 0) category = legacyCategory;
    }

    /// <summary>Compatibility for older builders. New authoring uses itemType and subtype fields.</summary>
    public ItemCategory category
    {
        get
        {
            switch (itemType)
            {
                case ItemType.Consumable: return ItemCategory.Food;
                case ItemType.Currency: return ItemCategory.Currency;
                case ItemType.Material: return ItemCategory.Material;
                case ItemType.Clothing: return (ItemCategory)((int)clothingSlot + 1);
                case ItemType.Furniture: return ItemCategory.Furniture;
                case ItemType.Document: return ItemCategory.Document;
                case ItemType.Vehicle: return ItemCategory.Vehicle;
                case ItemType.Pet: return ItemCategory.Pet;
                case ItemType.Tool: return isWeapon ? ItemCategory.Weapon : ItemCategory.General;
                default: return isBag ? ItemCategory.Bag : ItemCategory.General;
            }
        }
        set
        {
            legacyCategory = value;
            isBag = value == ItemCategory.Bag;
            isWeapon = value == ItemCategory.Weapon;
            switch (value)
            {
                case ItemCategory.Top: case ItemCategory.Bottom: case ItemCategory.Socks: case ItemCategory.Shoes: case ItemCategory.Accessory:
                    itemType = ItemType.Clothing; clothingSlot = (EquipmentSlot)((int)value - 1); break;
                case ItemCategory.Food: case ItemCategory.Fish: itemType = ItemType.Consumable; break;
                case ItemCategory.Currency: itemType = ItemType.Currency; break;
                case ItemCategory.Material: itemType = ItemType.Material; break;
                case ItemCategory.Furniture: itemType = ItemType.Furniture; break;
                case ItemCategory.Document: itemType = ItemType.Document; break;
                case ItemCategory.Vehicle: itemType = ItemType.Vehicle; break;
                case ItemCategory.Pet: itemType = ItemType.Pet; break;
                case ItemCategory.Weapon: itemType = ItemType.Tool; break;
                default: itemType = healthRestore > 0 || staminaRestore > 0 || stressRelief > 0 ? ItemType.Consumable : ItemType.Misc; break;
            }
            classificationVersion = 1;
        }
    }
}
