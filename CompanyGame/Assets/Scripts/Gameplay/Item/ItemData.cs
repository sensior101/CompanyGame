using UnityEngine;

// Keep serialized values stable. Accessory is retained only for old item assets.
public enum ItemCategory { General = 0, Top = 1, Bottom = 2, Socks = 3, Shoes = 4, Accessory = 5, Pet = 6, Bag = 7, Currency = 8, Weapon = 9 }
public enum EquipmentSlot { Top = 0, Bottom = 1, Socks = 2, Shoes = 3, Accessory = 4, Pet = 5 }
public enum WeaponKind { Melee = 0, Firearm = 1 }

/// <summary>Shared item definition. Inventories store these assets, never scene objects.</summary>
[CreateAssetMenu(fileName = "Item", menuName = "Company Game/Inventory/Item")]
public class ItemData : ScriptableObject
{
    public string itemId;
    public string displayName;
    public Sprite icon;
    public ItemCategory category;
    [Min(1)] public int maxStack = 1;
    [SerializeField] private long currencyValue;

    [Header("Food effects (per item)")]
    [Min(0f)] public float healthRestore;
    [Min(0f)] public float staminaRestore;
    [Min(0f)] public float stressRelief;
    public bool IsConsumable => category == ItemCategory.General &&
        (healthRestore > 0f || staminaRestore > 0f || stressRelief > 0f);

    [Header("Held appearance")]
    public GameObject heldPrefab;
    public Vector3 heldLocalPosition = Vector3.zero;
    public Vector3 heldLocalEulerAngles = Vector3.zero;
    public Vector3 heldLocalScale = Vector3.one;

    [Header("Weapon")]
    public WeaponKind weaponKind;
    [Min(0.1f)] public float weaponDamage = 25f;
    [Min(0.1f)] public float weaponRange = 50f;
    [Min(0.1f)] public float attacksPerSecond = 3f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public int StackLimit => category == ItemCategory.General || IsCurrency ? Mathf.Max(1, maxStack) : 1;
    public bool IsCurrency => category == ItemCategory.Currency && currencyValue >= 0;
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
        return slot != EquipmentSlot.Accessory && (int)slot >= 0 && (int)slot < 6 && (int)category == (int)slot + 1;
    }
}
