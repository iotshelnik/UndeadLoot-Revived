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
// This works because PlayerMoveController.HandleInteraction still routes a look-at ray
// on a DEAD entity to the activation-command check (it does not early-out on IsDead),
// and the base Entity class already has the full self-loot machinery (bag, lootList,
// OnLockedLocal -> XUiC_BagStorageWindowGroup.Open) used by loot bags / vehicles.

namespace UndeadLoot
{
    public class UndeadLootMod : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            Debug.Log("[UndeadLoot] Initializing (v3.0 lootable dead bodies)...");
            new Harmony("com.7modstodead.undeadloot").PatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("[UndeadLoot] Patches applied.");
        }
    }

    internal static class Corpse
    {
        // Is this entity a (humanoid) zombie we should make lootable?
        // EntityZombie covers every vanilla humanoid zombie AND modded zombies that use the
        // standard EntityZombie class. Zombie ANIMALS (dog/bear/vulture/boar) are EntityAnimal,
        // not EntityZombie, so they are intentionally excluded - they already give their parts
        // via the normal harvest/destroy mechanic, like any animal corpse.
        public static bool IsZombie(Entity e)
        {
            return e is EntityZombie;
        }

        // Choose a loot container by matching keywords in the entity class name, so ALL
        // current/future/modded humanoid zombie types are covered automatically:
        //   - archetype by keyword (nurse, cop/mutated, soldier/demolition, business,
        //     hazmat, wight), otherwise the generic default;
        //   - tier by keyword (feral / radiated|charged|infernal), otherwise base.
        // Anything unmatched (e.g. a zombie from another mod) falls back to "groupZombies".
        public static string PickLootList(Entity e)
        {
            string n = ClassName(e);

            int t = Any(n, "infernal", "charged", "radiated") ? 2 : (n.Contains("feral") ? 1 : 0);

            if (n.Contains("nurse"))                     return Tier("groupZombieNurse", "groupZombieNurseFeral", "groupZombieNurseRadiated", t);
            if (n.Contains("cop") || n.Contains("mutated")) return Tier("groupZombiesCops", "groupZombiesCopsFeral", "groupZombieFatCopRadiated", t);
            if (n.Contains("soldier") || n.Contains("demolition")) return Tier("groupZombieSoldier", "groupZombieSoldierFeral", "groupZombieSoldierRadiated", t);
            if (n.Contains("business"))                  return Tier("groupZombieBusinessMan", "groupZombieBusinessManFeral", "groupZombieBusinessManRadiated", t);
            if (n.Contains("hazmat"))                    return "groupZombieHazmat";
            if (n.Contains("wight"))                     return "groupZombieWight";

            return "groupZombies"; // generic default (all other vanilla + modded zombies)
        }

        // A body we made lootable: a dead zombie that has been given a loot bag.
        public static bool IsLootableBody(Entity e)
        {
            return IsZombie(e) && e.IsDead() && e.bag != null && !string.IsNullOrEmpty(e.lootList);
        }

        private static string Tier(string b, string f, string r, int t) { return t == 2 ? r : (t == 1 ? f : b); }

        private static string ClassName(Entity e)
        {
            string n = (e != null && e.EntityClass != null) ? e.EntityClass.entityClassName : null;
            return n == null ? "" : n.ToLowerInvariant();
        }

        private static bool Any(string s, params string[] subs)
        {
            for (int i = 0; i < subs.Length; i++) if (s.Contains(subs[i])) return true;
            return false;
        }
    }

    // On death: fill the zombie's own bag from its loot list and expose a "search" command.
    [HarmonyPatch(typeof(EntityAlive), "OnEntityDeath")]
    public static class Patch_OnEntityDeath
    {
        public static void Postfix(EntityAlive __instance) { CorpseSetup.OnDeath(__instance); }
    }

    internal static class CorpseSetup
    {
        public static void OnDeath(EntityAlive __instance)
        {
            try
            {
                if (__instance == null || __instance.isEntityRemote) return; // server / host only
                if (!Corpse.IsZombie(__instance)) return;                     // zombies only (incl. modded)
                if (__instance.bag != null) return;                           // already set up

                World world = __instance.world;
                if (world == null) return;

                GameRandom rand = __instance.rand;
                if (rand == null) return;

                string lootName = Corpse.PickLootList(__instance);
                LootContainer lc = LootContainer.GetLootContainer(lootName, false);
                if (lc == null) { Debug.LogWarning("[UndeadLoot] loot container missing: " + lootName); return; }

                EntityPlayer player = world.GetPrimaryPlayer();
                int lootStage = player != null ? player.GetHighestPartyLootStage(0f, 0f) : 1;

                List<ItemStack> items = lc.Spawn(rand, 100, lootStage, 0f, player,
                    FastTags<TagGroup.Global>.none, true, false, true);

                int slots = Mathf.Max(lc.size.x * lc.size.y, 8);
                if (items != null && items.Count > slots) slots = items.Count;

                ItemStack[] arr = ItemStack.CreateArray(slots);
                if (items != null)
                    for (int i = 0; i < items.Count && i < slots; i++)
                        arr[i] = items[i];

                Bag bag = new Bag(slots);
                bag.SetSlots(arr);

                __instance.bag = bag;
                __instance.lootList = lootName;
                __instance.customCmds = new[] { new EntityActivationCommand("search", "search", null, null) };
                __instance.activationCommands = null; // force the command list to rebuild with "search"
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] death setup failed: " + e); }
        }
    }

    // Answer the "search" activation on a lootable body by opening its loot window (bound to
    // the entity itself via the standard lock flow - same path loot bags & vehicles use).
    [HarmonyPatch(typeof(Entity), "OnEntityActivated")]
    public static class Patch_OnEntityActivated
    {
        // Resolved by reflection to select the (ILockTarget, ILockContext, ushort) overload;
        // referencing it directly makes the C# compiler try to load a ReadOnlySpan<> overload
        // whose element type it can't resolve against the game's mono assemblies.
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
                var ctx = new Entity.EntityLockContext(_command.commandId, __instance.bag);
                LockRequest.Invoke(LockManager.Instance, new object[] { __instance, ctx, (ushort)0 });
            }
            catch (Exception e) { Debug.LogWarning("[UndeadLoot] activation failed: " + e); }
        }
    }

    // Prompt shown when aiming at a lootable body - matches the vanilla loot-container
    // prompt: shows the Activate key binding and the untouched / touched / empty state.
    [HarmonyPatch(typeof(Entity), "GetActivationText")]
    public static class Patch_GetActivationText
    {
        public static bool Prefix(Entity __instance, ref string __result)
        {
            if (!Corpse.IsLootableBody(__instance)) return true; // run original for everything else

            Bag bag = __instance.bag;
            string name = __instance.LocalizedEntityName;

            // Build the Activate key hint (empty state shows no key - nothing to do).
            string key = string.Empty;
            if (!bag.IsEmpty())
            {
                EntityPlayerLocal player = (GameManager.Instance != null && GameManager.Instance.World != null)
                    ? GameManager.Instance.World.GetPrimaryPlayer() : null;
                if (player != null)
                {
                    PlayerActionsLocal input = player.playerInput;
                    key = XUiUtils.GetBindingXuiMarkupString(input.Activate, (XUiUtils.EmptyBindingStyle)0, (XUiUtils.DisplayStyle)0, null) +
                          XUiUtils.GetBindingXuiMarkupString(input.PermanentActions.Activate, (XUiUtils.EmptyBindingStyle)0, (XUiUtils.DisplayStyle)0, null);
                }
            }

            // Use the game's global loot strings so the corpse prompt matches every other
            // container. Config/Localization.csv recolors those strings for the whole game
            // (Okabe-Ito colorblind-safe palette): green = fresh, orange = opened, gray = empty.
            // Untouched until first opened; OnLockedLocal flips bag.Touched on open.
            string fmtKey = bag.IsEmpty() ? "lootTooltipEmpty" : (bag.Touched ? "lootTooltipTouched" : "lootTooltipNew");
            __result = string.Format(Localization.Get(fmtKey, false), key, name);
            return false;
        }
    }

    // EntityAlive refuses to be locked while dead (and the base also requires
    // spawnByAllowShare). Allow the lock for our lootable corpses so the loot window opens.
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
}
