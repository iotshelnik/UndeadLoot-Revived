using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// UndeadLoot v3.0 - makes the ACTUAL defeated zombie body lootable.
//
// No block is placed, no chest or bag entity is spawned, and the ragdoll is NOT
// replaced. Instead the dead zombie entity itself is given a loot-filled Bag and a
// "search" activation command, so the game's own interaction -> lock -> loot-window
// flow opens the loot on the real corpse when you press/hold E on it.
//
// v1.3.0 adds instanced multiplayer loot: each player who opens a zombie corpse gets
// their own personal loot roll from the same body. The bag contents are swapped in and
// out per player at open/close time — no extra entity, block, or bag is ever spawned.
//
// If InstancedLoot by Kobonator is also installed, our instancing patches are skipped
// and that mod handles per-player distribution instead. The two mods complement each
// other: UndeadLoot provides the lootable corpse, InstancedLoot provides instancing.

namespace UndeadLoot
{
    public class UndeadLootMod : IModApi
    {
        // True when InstancedLoot by Kobonator is loaded alongside us.
        internal static bool InstancedLootPresent;

        public void InitMod(Mod _modInstance)
        {
            Debug.Log("[UndeadLoot] Initializing (v1.3.0 lootable dead bodies + instanced MP loot)...");

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "InstancedLoot") continue;
                InstancedLootPresent = true;
                Debug.Log("[UndeadLoot] InstancedLoot detected — per-player loot deferred to that mod.");
                break;
            }

            new Harmony("com.7modstodead.undeadloot").PatchAll(Assembly.GetExecutingAssembly());

            // Clear per-session instanced-loot data when a new world starts.
            ModEvents.GameStartDone.RegisterHandler(InstancedCorpseLoot.ClearAll);

            Debug.Log("[UndeadLoot] Patches applied.");
        }
    }

    // -----------------------------------------------------------------------
    // Per-player loot storage for instanced corpse looting
    // -----------------------------------------------------------------------

    internal static class InstancedCorpseLoot
    {
        // entityId -> (playerId -> remaining items for that player)
        private static readonly Dictionary<int, Dictionary<int, ItemStack[]>> _loot =
            new Dictionary<int, Dictionary<int, ItemStack[]>>();

        // entityId -> playerId who currently has this entity's bag open
        private static readonly Dictionary<int, int> _currentLocker =
            new Dictionary<int, int>();

        // entityId -> playerId who just triggered OnEntityActivated (client-side capture,
        // consumed in OnLockedServer; works for SP and the host player in listen-server MP)
        private static readonly Dictionary<int, int> _pendingLock =
            new Dictionary<int, int>();

        // Reflection cache: Bag internal slots field
        private static FieldInfo _bagSlotsField;
        private static bool _bagSlotsSearched;

        // Reflection cache: LockManager locking-player field (dedicated-server support)
        private static FieldInfo _lockingEntityIdField;
        private static bool _lockingFieldSearched;

        // ---- pending lock (client-side capture) ----

        public static void SetPendingLock(int entityId, int playerId)
            => _pendingLock[entityId] = playerId;

        public static int ConsumePendingLock(int entityId)
        {
            if (!_pendingLock.TryGetValue(entityId, out int pid)) return -1;
            _pendingLock.Remove(entityId);
            return pid;
        }

        // ---- current locker (server-side, lives between OnLockedServer / OnUnlockedServer) ----

        public static void SetCurrentLocker(int entityId, int playerId)
            => _currentLocker[entityId] = playerId;

        public static int ConsumeCurrentLocker(int entityId)
        {
            if (!_currentLocker.TryGetValue(entityId, out int pid)) return -1;
            _currentLocker.Remove(entityId);
            return pid;
        }

        // ---- LockManager reflection (dedicated-server fallback) ----

        public static int TryGetLockingPlayerFromManager()
        {
            if (!_lockingFieldSearched)
            {
                _lockingFieldSearched = true;
                _lockingEntityIdField =
                    AccessTools.Field(typeof(LockManager), "_lockingEntityId") ??
                    AccessTools.Field(typeof(LockManager), "lockingEntityId") ??
                    AccessTools.Field(typeof(LockManager), "_lockingPlayerId") ??
                    AccessTools.Field(typeof(LockManager), "lockingPlayerId");
                if (_lockingEntityIdField != null)
                    Debug.Log("[UndeadLoot] LockManager locking-player field: " + _lockingEntityIdField.Name);
                else
                    Debug.Log("[UndeadLoot] LockManager locking-player field not found — dedicated-server instancing uses primary-player fallback.");
            }
            if (_lockingEntityIdField == null) return -1;
            try { return Convert.ToInt32(_lockingEntityIdField.GetValue(LockManager.Instance)); }
            catch { return -1; }
        }

        // ---- loot state ----

        public static void Register(int entityId)
        {
            if (!_loot.ContainsKey(entityId))
                _loot[entityId] = new Dictionary<int, ItemStack[]>();
        }

        public static bool IsRegistered(int entityId) => _loot.ContainsKey(entityId);

        public static bool HasOpened(int entityId, int playerId)
            => _loot.TryGetValue(entityId, out var pd) && pd.ContainsKey(playerId);

        public static bool IsEmptyForPlayer(int entityId, int playerId)
        {
            if (!_loot.TryGetValue(entityId, out var pd)) return false;
            if (!pd.TryGetValue(playerId, out var items)) return false;
            foreach (var s in items) if (s != null && !s.IsEmpty()) return false;
            return true;
        }

        public static ItemStack[] GetOrGenerate(int entityId, int playerId, EntityAlive entity)
        {
            if (!_loot.TryGetValue(entityId, out var pd))
            {
                pd = new Dictionary<int, ItemStack[]>();
                _loot[entityId] = pd;
            }
            if (!pd.TryGetValue(playerId, out var items))
            {
                items = GenerateLoot(entity, playerId);
                pd[playerId] = items;
            }
            return items;
        }

        public static void SaveItems(int entityId, int playerId, ItemStack[] items)
        {
            if (!_loot.TryGetValue(entityId, out var pd)) return;
            var copy = new ItemStack[items.Length];
            Array.Copy(items, copy, items.Length);
            pd[playerId] = copy;
        }

        public static void Cleanup(int entityId)
        {
            _loot.Remove(entityId);
            _currentLocker.Remove(entityId);
            _pendingLock.Remove(entityId);
        }

        public static void ClearAll()
        {
            _loot.Clear();
            _currentLocker.Clear();
            _pendingLock.Clear();
        }

        // ---- bag slot access via reflection ----

        public static ItemStack[] ReadBagSlots(Bag bag)
        {
            if (bag == null) return null;
            if (!_bagSlotsSearched)
            {
                _bagSlotsSearched = true;
                _bagSlotsField =
                    AccessTools.Field(typeof(Bag), "slots") ??
                    AccessTools.Field(typeof(Bag), "_slots") ??
                    AccessTools.Field(typeof(Bag), "m_slots");
            }
            return _bagSlotsField?.GetValue(bag) as ItemStack[];
        }

        // ---- loot generation ----

        private static ItemStack[] GenerateLoot(EntityAlive entity, int playerId)
        {
            string lootName = Corpse.PickLootList(entity);
            LootContainer lc = LootContainer.GetLootContainer(lootName, false);
            if (lc == null) return ItemStack.CreateArray(8);

            World world = entity.world;
            // Use the specific player for loot-stage calculation; fall back to primary.
            EntityPlayer player = (world?.GetEntity(playerId) as EntityPlayer)
                               ?? world?.GetPrimaryPlayer();
            int lootStage = player != null ? player.GetHighestPartyLootStage(0f, 0f) : 1;

            GameRandom rand = entity.rand;
            if (rand == null) return ItemStack.CreateArray(8);

            List<ItemStack> spawned = lc.Spawn(rand, 100, lootStage, 0f, player,
                FastTags<TagGroup.Global>.none, true, false, true);

            int slots = Mathf.Max(lc.size.x * lc.size.y, 8);
            if (spawned != null && spawned.Count > slots) slots = spawned.Count;

            ItemStack[] arr = ItemStack.CreateArray(slots);
            if (spawned != null)
                for (int i = 0; i < spawned.Count && i < arr.Length; i++)
                    arr[i] = spawned[i];
            return arr;
        }
    }

    // -----------------------------------------------------------------------
    // Corpse helpers (unchanged)
    // -----------------------------------------------------------------------

    internal static class Corpse
    {
        public static bool IsZombie(Entity e) => e is EntityZombie;

        public static string PickLootList(Entity e)
        {
            string n = ClassName(e);
            int t = Any(n, "infernal", "charged", "radiated") ? 2 : (n.Contains("feral") ? 1 : 0);

            if (n.Contains("nurse"))                               return Tier("groupZombieNurse", "groupZombieNurseFeral", "groupZombieNurseRadiated", t);
            if (n.Contains("cop") || n.Contains("mutated"))       return Tier("groupZombiesCops", "groupZombiesCopsFeral", "groupZombieFatCopRadiated", t);
            if (n.Contains("soldier") || n.Contains("demolition")) return Tier("groupZombieSoldier", "groupZombieSoldierFeral", "groupZombieSoldierRadiated", t);
            if (n.Contains("business"))                            return Tier("groupZombieBusinessMan", "groupZombieBusinessManFeral", "groupZombieBusinessManRadiated", t);
            if (n.Contains("hazmat"))                              return "groupZombieHazmat";
            if (n.Contains("wight"))                               return "groupZombieWight";
            return "groupZombies";
        }

        public static bool IsLootableBody(Entity e)
            => e is EntityAlive ea && ea.IsDead() && ea.bag != null && !string.IsNullOrEmpty(ea.lootList);

        private static string Tier(string b, string f, string r, int t) => t == 2 ? r : (t == 1 ? f : b);

        private static string ClassName(Entity e)
        {
            string n = e?.EntityClass?.entityClassName;
            return n == null ? "" : n.ToLowerInvariant();
        }

        private static bool Any(string s, params string[] subs)
        {
            for (int i = 0; i < subs.Length; i++) if (s.Contains(subs[i])) return true;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // On death: set up the lootable body
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(EntityAlive), "OnEntityDeath")]
    public static class Patch_OnEntityDeath
    {
        public static void Postfix(EntityAlive __instance) => CorpseSetup.OnDeath(__instance);
    }

    internal static class CorpseSetup
    {
        public static void OnDeath(EntityAlive entity)
        {
            try
            {
                if (entity == null || !Corpse.IsZombie(entity) || entity.bag != null) return;

                World world = entity.world;
                if (world == null) return;

                string lootName = Corpse.PickLootList(entity);
                LootContainer lc = LootContainer.GetLootContainer(lootName, false);
                if (lc == null) { Debug.LogWarning("[UndeadLoot] loot container missing: " + lootName); return; }

                int slots = Mathf.Max(lc.size.x * lc.size.y, 8);
                Bag bag;

                if (UndeadLootMod.InstancedLootPresent)
                {
                    // InstancedLoot is installed — fill the bag normally so IL can instance it.
                    GameRandom rand = entity.rand;
                    if (rand == null) return;
                    EntityPlayer player = world.GetPrimaryPlayer();
                    int lootStage = player != null ? player.GetHighestPartyLootStage(0f, 0f) : 1;
                    List<ItemStack> items = lc.Spawn(rand, 100, lootStage, 0f, player,
                        FastTags<TagGroup.Global>.none, true, false, true);
                    if (items != null && items.Count > slots) slots = items.Count;
                    ItemStack[] arr = ItemStack.CreateArray(slots);
                    if (items != null)
                        for (int i = 0; i < items.Count && i < slots; i++) arr[i] = items[i];
                    bag = new Bag(slots);
                    bag.SetSlots(arr);
                }
                else
                {
                    // Instanced mode: empty bag — loot is generated per player on their first open.
                    bag = new Bag(slots);
                    bag.SetSlots(ItemStack.CreateArray(slots));
                    InstancedCorpseLoot.Register(entity.entityId);
                }

                entity.bag = bag;
                entity.lootList = lootName;
                entity.customCmds = new[] { new EntityActivationCommand("search", "search", null, null) };
                entity.activationCommands = null;
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] death setup failed: " + e); }
        }
    }

    // -----------------------------------------------------------------------
    // Activation: track the requesting player, then open the bag
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(Entity), "OnEntityActivated")]
    public static class Patch_OnEntityActivated
    {
        private static readonly MethodInfo LockRequest = AccessTools.Method(
            typeof(LockManager), "LockRequestLocal",
            new[] { typeof(ILockTarget), typeof(ILockContext), typeof(ushort) });

        public static void Postfix(Entity __instance, EntityActivationCommand _command, EntityPlayerLocal _playerFocusing)
        {
            try
            {
                if (!Corpse.IsLootableBody(__instance)) return;
                if (_command.commandId != "search") return;
                if (LockRequest == null) { Debug.LogWarning("[UndeadLoot] LockRequestLocal not found"); return; }

                // Store the local player ID so OnLockedServer can retrieve it (works for SP
                // and the host in a listen-server game; dedicated-server remote clients use
                // the LockManager reflection fallback in Patch_OnLockedServer).
                if (!UndeadLootMod.InstancedLootPresent && _playerFocusing != null)
                    InstancedCorpseLoot.SetPendingLock(__instance.entityId, _playerFocusing.entityId);

                var ctx = new Entity.EntityLockContext(_command.commandId, __instance.bag);
                LockRequest.Invoke(LockManager.Instance, new object[] { __instance, ctx, (ushort)0 });
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] activation failed: " + e); }
        }
    }

    // -----------------------------------------------------------------------
    // Server lock: swap in this player's personal loot before the bag syncs
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(Entity), "OnLockedServer")]
    public static class Patch_OnLockedServer
    {
        public static void Prefix(Entity __instance)
        {
            try
            {
                if (UndeadLootMod.InstancedLootPresent) return;
                if (!(__instance is EntityAlive ea) || !Corpse.IsLootableBody(ea)) return;

                int entityId = __instance.entityId;
                if (!InstancedCorpseLoot.IsRegistered(entityId)) return;

                // Player resolution priority:
                // 1. Pending dict — set in OnEntityActivated (SP + listen-server host).
                // 2. LockManager reflection — covers dedicated-server remote clients.
                // 3. Primary player — guaranteed in SP, correct for host in listen-server.
                int playerId = InstancedCorpseLoot.ConsumePendingLock(entityId);
                if (playerId < 0) playerId = InstancedCorpseLoot.TryGetLockingPlayerFromManager();
                if (playerId < 0) playerId = ea.world?.GetPrimaryPlayer()?.entityId ?? -1;
                if (playerId < 0) return;

                InstancedCorpseLoot.SetCurrentLocker(entityId, playerId);

                ItemStack[] items = InstancedCorpseLoot.GetOrGenerate(entityId, playerId, ea);
                int slots = Mathf.Max(items.Length, 8);
                Bag bag = new Bag(slots);
                bag.SetSlots((ItemStack[])items.Clone());
                ea.bag = bag;
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] OnLockedServer failed: " + e); }
        }
    }

    // -----------------------------------------------------------------------
    // Server unlock: save remaining items, reset bag to empty for next player
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(Entity), "OnUnlockedServer")]
    public static class Patch_OnUnlockedServer
    {
        public static void Prefix(Entity __instance)
        {
            try
            {
                if (UndeadLootMod.InstancedLootPresent) return;
                if (!(__instance is EntityAlive ea) || !Corpse.IsLootableBody(ea)) return;

                int entityId = __instance.entityId;
                if (!InstancedCorpseLoot.IsRegistered(entityId)) return;

                int playerId = InstancedCorpseLoot.ConsumeCurrentLocker(entityId);
                if (playerId < 0) return;

                ItemStack[] remaining = InstancedCorpseLoot.ReadBagSlots(ea.bag);
                if (remaining != null)
                    InstancedCorpseLoot.SaveItems(entityId, playerId, remaining);

                // Empty the bag so the next player opens a clean slate for their own loot.
                int slots = remaining?.Length ?? 8;
                ea.bag.SetSlots(ItemStack.CreateArray(slots));
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] OnUnlockedServer failed: " + e); }
        }
    }

    // -----------------------------------------------------------------------
    // Activation text: per-player state (untouched / opened / empty)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(Entity), "GetActivationText")]
    public static class Patch_GetActivationText
    {
        public static bool Prefix(Entity __instance, ref string __result)
        {
            if (!Corpse.IsLootableBody(__instance)) return true;

            EntityAlive ea = (EntityAlive)__instance;
            string name = __instance.LocalizedEntityName;

            bool isEmpty, untouched;

            if (!UndeadLootMod.InstancedLootPresent && InstancedCorpseLoot.IsRegistered(__instance.entityId))
            {
                // Instanced mode: show state relative to the local player only.
                EntityPlayerLocal localPlayer = GameManager.Instance?.World?.GetPrimaryPlayer();
                int pid = localPlayer?.entityId ?? -1;
                bool hasOpened = pid >= 0 && InstancedCorpseLoot.HasOpened(__instance.entityId, pid);
                untouched = !hasOpened;
                isEmpty   = hasOpened && InstancedCorpseLoot.IsEmptyForPlayer(__instance.entityId, pid);
            }
            else
            {
                // InstancedLoot present (or fallback): use the shared bag state as before.
                Bag bag = ea.bag;
                isEmpty  = bag.IsEmpty();
                untouched = !isEmpty && !bag.Touched;
            }

            string keyHint = string.Empty;
            if (!isEmpty)
            {
                EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
                if (player != null)
                {
                    PlayerActionsLocal input = player.playerInput;
                    keyHint =
                        XUiUtils.GetBindingXuiMarkupString(input.Activate, (XUiUtils.EmptyBindingStyle)0, (XUiUtils.DisplayStyle)0, null) +
                        XUiUtils.GetBindingXuiMarkupString(input.PermanentActions.Activate, (XUiUtils.EmptyBindingStyle)0, (XUiUtils.DisplayStyle)0, null);
                }
            }

            string fmtKey = isEmpty ? "lootTooltipEmpty" : (untouched ? "lootTooltipNew" : "lootTooltipTouched");
            __result = string.Format(Localization.Get(fmtKey, false), keyHint, name);
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Lock guards: allow locking our lootable corpses (unchanged)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(EntityAlive), "CanLockOnServer")]
    public static class Patch_CanLockOnServer
    {
        public static bool Prefix(EntityAlive __instance, ref bool __result)
        {
            if (!Corpse.IsLootableBody(__instance)) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(EntityAlive), "CanLockLocally")]
    public static class Patch_CanLockLocally
    {
        public static bool Prefix(EntityAlive __instance, ref bool __result)
        {
            if (!Corpse.IsLootableBody(__instance)) return true;
            __result = true;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Game-wide Activate-key color (unchanged)
    // -----------------------------------------------------------------------

    [HarmonyPatch(typeof(XUiUtils), "GetBindingXuiMarkupString", new Type[] {
        typeof(InControl.PlayerAction), typeof(XUiUtils.EmptyBindingStyle),
        typeof(XUiUtils.DisplayStyle), typeof(string) })]
    public static class Patch_ActivateKeyColor
    {
        const string DefaultKeyColor = "56B4E9";

        static string GetKeyColor()
        {
            string color = Localization.Get("activateKeyColor", false);
            if (!string.IsNullOrEmpty(color)) color = color.Trim().TrimStart('#');
            if (color == null || color.Length != 6) return DefaultKeyColor;
            for (int i = 0; i < color.Length; i++)
            {
                char c = color[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return DefaultKeyColor;
            }
            return color;
        }

        public static void Postfix(InControl.PlayerAction _action, ref string __result)
        {
            if (_action == null || string.IsNullOrEmpty(__result)) return;
            if (_action.Name != "Activate") return;
            __result = "[" + GetKeyColor() + "]" + __result + "[-]";
        }
    }
}
