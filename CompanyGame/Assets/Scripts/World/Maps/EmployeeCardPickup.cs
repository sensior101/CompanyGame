using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompanyGame.World.Maps
{
    /// <summary>A reception-issued card that can be collected into the existing inventory with F.</summary>
    [DisallowMultipleComponent]
    public sealed class EmployeeCardPickup : MonoBehaviour
    {
        public ItemData card;
        public float radius = 2f;
        public bool IsAvailable => !taken && card && isActiveAndEnabled;
        public event Action AvailabilityChanged;
        static readonly HashSet<EmployeeCardPickup> active = new HashSet<EmployeeCardPickup>();
        bool taken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterLoaded()
        {
            foreach (var pickup in FindObjectsByType<EmployeeCardPickup>(FindObjectsSortMode.None))
                if (pickup.isActiveAndEnabled) active.Add(pickup);
        }

        void OnEnable() { active.Add(this); }

        void OnDisable()
        {
            active.Remove(this);
            AvailabilityChanged?.Invoke();
        }

        public bool IsInRange(Transform player) => IsAvailable && player &&
            gameObject.scene == SceneLoadManager.CurrentMap &&
            (player.position - transform.position).sqrMagnitude <= radius * radius;

        public static EmployeeCardPickup FindNearest(Transform player)
        {
            EmployeeCardPickup nearest = null;
            float best = float.PositiveInfinity;
            foreach (var pickup in active)
            {
                if (!pickup || !pickup.IsInRange(player)) continue;
                float distance = (pickup.transform.position - player.position).sqrMagnitude;
                if (distance >= best) continue;
                nearest = pickup;
                best = distance;
            }
            return nearest;
        }

        void Update()
        {
            if (!IsAvailable || !GameInput.PickupPressed || SceneLoadManager.IsLoading ||
                InputFocus.GameplayBlocked() || InputFocus.InventoryOpen() || InputFocus.ChatOpen() ||
                UIEventSystem.IsEditingText() || (PhoneManager.Instance && PhoneManager.Instance.IsPhoneOpen)) return;
            var player = SceneLoadManager.Traveller;
            if (!player || FindNearest(player.transform) != this) return;
            TryCollect(InventoryManager.Instance ? InventoryManager.Instance.State : null);
        }

        /// <summary>The card into <paramref name="inventory"/> (my own) and the pickup gone. False when it could not be taken.</summary>
        public bool TryCollect(InventoryState inventory)
        {
            if (!IsAvailable || inventory == null) return false;
            // On a sync-layer server the card is the server's to give (p.grant, 12.10.5): one while the bag has none.
            bool server = InventoryLedgerRules.OnServer;
            bool held = server && InventoryLedgerRules.Holds(inventory, card.itemId);
            if (!held && !inventory.TryAdd(card, 1, out _)) return false;
            if (server && !held) InventoryLedgerRules.SendGrant(inventory, card);
            taken = true;
            gameObject.SetActive(false);
            return !held;
        }

    }
}
