using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HowToFishModMenu
{
    // ==================================================================
    // MONEY
    // ==================================================================

    /// <summary>Money never decreases while InfiniteMoney is on (host / singleplayer).</summary>
    [HarmonyPatch(typeof(MoneyManager), nameof(MoneyManager.RemoveMoney))]
    internal static class Patch_RemoveMoney
    {
        private static bool Prefix() => !ModState.InfiniteMoney;
    }

    /// <summary>Always able to afford purchases (host-side validation).</summary>
    [HarmonyPatch(typeof(MoneyManager), nameof(MoneyManager.CanAfford))]
    internal static class Patch_CanAfford
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ModState.InfiniteMoney) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Show max money client-side (UI + client-side checks).</summary>
    [HarmonyPatch(typeof(MoneyManager), "get_Money")]
    internal static class Patch_MoneyGetter
    {
        private static bool Prefix(ref int __result)
        {
            if (!ModState.InfiniteMoney) return true;
            __result = 99999;
            return false;
        }
    }

    /// <summary>
    /// Suppress the client-side money sync callback while InfiniteMoney is on so the UI
    /// doesn't show misleading "lost money" popups (the getter already reports 99999).
    /// OnItemSold (fired here) has no subscribers in the game, so nothing breaks.
    /// </summary>
    [HarmonyPatch(typeof(MoneyManager), "OnChangeMoney")]
    internal static class Patch_OnChangeMoney
    {
        private static bool Prefix() => !ModState.InfiniteMoney;
    }

    // -------- Multiplayer free buys --------
    // FishNet host-authoritative money means a *joiner* cannot change the shared pool.
    // BUT the game's own ServerRPCs trust client-supplied "isFree" / "cost" parameters,
    // so a client can patch those params before they are sent to the host.

    /// <summary>Force isFree=true on shop item purchases.</summary>
    [HarmonyPatch(typeof(Server), nameof(Server.BuyItem))]
    internal static class Patch_BuyItem
    {
        private static void Prefix(ref bool isFree)
        {
            if (ModState.InfiniteMoney) isFree = true;
        }
    }

    /// <summary>Force cost=0 when buying bait.</summary>
    [HarmonyPatch(typeof(Server), nameof(Server.BuyBait))]
    internal static class Patch_BuyBait
    {
        private static void Prefix(ref int cost)
        {
            if (ModState.InfiniteMoney) cost = 0;
        }
    }

    /// <summary>Force cost=0 when buying a boat motor.</summary>
    [HarmonyPatch(typeof(Server), nameof(Server.BuyBoatMotor))]
    internal static class Patch_BuyBoatMotor
    {
        private static void Prefix(ref int cost)
        {
            if (ModState.InfiniteMoney) cost = 0;
        }
    }

    /// <summary>Force cost=0 when buying boat radar.</summary>
    [HarmonyPatch(typeof(Server), nameof(Server.BuyBoatRadar))]
    internal static class Patch_BuyBoatRadar
    {
        private static void Prefix(ref int cost)
        {
            if (ModState.InfiniteMoney) cost = 0;
        }
    }

    // ==================================================================
    // PLAYER
    // ==================================================================

    /// <summary>
    /// God mode: block ALL damage. TakeDamage is the funnel every damage type goes
    /// through (poison ticks, fire ticks, hunger damage and drowning damage all call it).
    /// </summary>
    [HarmonyPatch(typeof(PlayerVitals), nameof(PlayerVitals.TakeDamage))]
    internal static class Patch_TakeDamage
    {
        private static bool Prefix() => !ModState.GodMode;
    }

    /// <summary>Hunger ("fullness") never drains — the game's stamina equivalent.</summary>
    [HarmonyPatch(typeof(PlayerVitals), "LowerFullness")]
    internal static class Patch_LowerFullness
    {
        private static bool Prefix() => !ModState.InfiniteFullness;
    }

    /// <summary>Movement speed multiplier applied after the speed target is computed each tick.</summary>
    [HarmonyPatch(typeof(PlayerMovement), "UpdateMoveSpeed")]
    internal static class Patch_MoveSpeed
    {
        private static readonly FieldInfo _speedField = AccessTools.Field(typeof(PlayerMovement), "_curMoveSpeed");

        private static void Postfix(PlayerMovement __instance)
        {
            if (Mathf.Approximately(ModState.SpeedMulti, 1f) || __instance == null || _speedField == null) return;
            try
            {
                _speedField.SetValue(__instance, (float)_speedField.GetValue(__instance) * ModState.SpeedMulti);
            }
            catch { }
        }
    }

    // ==================================================================
    // FISHING
    // ==================================================================

    /// <summary>
    /// Instant catch: the catch timer is randomized between BaitInfo.CatchTimeMinMax;
    /// returning 0 means TimeUnderWater >= 0 on the very next host tick, so the fish
    /// bites immediately after the bait lands in water.
    /// </summary>
    [HarmonyPatch(typeof(Bait), "get_RandomizedCatchTime")]
    internal static class Patch_RandomizedCatchTime
    {
        private static bool Prefix(ref float __result)
        {
            if (!ModState.InstantCatch && !ModState.AutoFish) return true;
            __result = 0f;
            return false;
        }
    }

    /// <summary>
    /// Fish size multiplier: every Creature (fish, shark, crab...) gets scaled on spawn.
    /// This runs after Item.Awake resets the scale, so it is the last word.
    /// </summary>
    [HarmonyPatch(typeof(Creature), "Awake_UserLogic_Creature_Assembly-CSharp.dll")]
    internal static class Patch_FishScale
    {
        private static void Postfix(Creature __instance)
        {
            if (Mathf.Approximately(ModState.FishSizeMulti, 1f) || __instance == null) return;
            try
            {
                Vector3 s = __instance.transform.localScale;
                __instance.transform.localScale = new Vector3(
                    s.x * ModState.FishSizeMulti,
                    s.y * ModState.FishSizeMulti,
                    s.z * ModState.FishSizeMulti);
            }
            catch { }
        }
    }

    /// <summary>
    /// Always rare/shiny: the game rolls a 1% "drip" (shiny skin) chance per hooked
    /// creature in CreatureManager.HookItem. Raising the field to 100 makes every catch shiny.
    /// </summary>
    [HarmonyPatch(typeof(CreatureManager), nameof(CreatureManager.Awake))]
    internal static class Patch_CreatureManagerAwake
    {
        private static void Postfix(CreatureManager __instance)
        {
            if (__instance == null) return;
            try
            {
                var f = AccessTools.Field(typeof(CreatureManager), "_shinyCreatureChance");
                f.SetValue(__instance, ModState.ForceShiny ? 100 : 1);
            }
            catch { }
        }
    }

    // ==================================================================
    // WEAPONS
    // ==================================================================

    /// <summary>
    /// Infinite ammo: Weapon.Shoot decrements Ammo at the end; refill it right after
    /// every shot so the weapon never runs dry (no forced reload, no ammo pickup needed).
    /// </summary>
    [HarmonyPatch(typeof(Weapon), "Shoot")]
    internal static class Patch_InfiniteAmmo
    {
        private static readonly FieldInfo _ammoField = AccessTools.Field(typeof(Weapon), "<Ammo>k__BackingField");

        private static void Postfix(Weapon __instance)
        {
            if (!ModState.InfiniteAmmo || __instance == null) return;
            try
            {
                int max = __instance.Attachments != null ? __instance.Attachments.AmmoPerMag : 1;
                int cur = (int)_ammoField.GetValue(__instance);
                _ammoField.SetValue(__instance, Mathf.Max(cur, max));
            }
            catch { }
        }
    }

    /// <summary>Jump power multiplier applied when a player spawns.</summary>
    [HarmonyPatch(typeof(PlayerMovement), "Awake_UserLogic_PlayerMovement_Assembly-CSharp.dll")]
    internal static class Patch_JumpPower
    {
        private static float _baseJump = -1f;

        /// <summary>Applies the current JumpMulti to a movement component, capturing the
        /// game's base value exactly once so re-applies never compound.</summary>
        internal static void ApplyJump(PlayerMovement m)
        {
            if (m == null || Mathf.Approximately(ModState.JumpMulti, 1f)) return;
            try
            {
                var f = AccessTools.Field(typeof(PlayerMovement), "_jumpForce");
                if (_baseJump < 0f) _baseJump = (float)f.GetValue(m);
                f.SetValue(m, _baseJump * ModState.JumpMulti);
            }
            catch { }
        }

        private static void Postfix(PlayerMovement __instance)
        {
            ApplyJump(__instance);
        }
    }

    // ==================================================================
    // CASINO
    // ==================================================================

    /// <summary>
    /// Rig the roulette: the host decides the winning color in ServerRouletteResult;
    /// force it to match the color the player bet on so every spin pays out.
    /// </summary>
    [HarmonyPatch(typeof(CasinoManager), nameof(CasinoManager.ServerRouletteResult))]
    internal static class Patch_RigRoulette
    {
        private static void Prefix(ref BetColor winColor)
        {
            if (!ModState.RigRoulette) return;
            try
            {
                if (CasinoManager.Instance == null) return;
                var f = AccessTools.Field(typeof(CasinoManager), "_curBetColor");
                winColor = (BetColor)f.GetValue(CasinoManager.Instance);
            }
            catch { }
        }
    }

    /// <summary>
    /// Rig the slot machine: the game has a built-in "cheat skin" slot that forces the
    /// jackpot slot to roll a chosen item. We set it to a legendary item + skin, and the
    /// original RollRandom code then forces the winning reel to it.
    /// </summary>
    [HarmonyPatch(typeof(SlotMachineManager), nameof(SlotMachineManager.RollRandom))]
    internal static class Patch_RigSlots
    {
        private static void Prefix(Player roller)
        {
            if (!ModState.RigSlots || roller == null) return;
            try
            {
                var legendary = ModItems.FindLegendarySkinItem();
                if (legendary == null) return;
                byte skin = legendary.SkinPreset.GetRandomSkinIndex(Rarity.Legendary);
                if (skin == byte.MaxValue) return;
                SlotMachineManager.SetCheatSkin(legendary, skin);
            }
            catch { }
        }
    }

    // ==================================================================
    // FISHING / WEAPONS / WORLD (v2.1)
    // ==================================================================

    /// <summary>No bait loss: bait survives every catch.</summary>
    [HarmonyPatch(typeof(BaitInfo), "get_LostOnBaitChance")]
    internal static class Patch_NoBaitLoss
    {
        private static bool Prefix(ref float __result)
        {
            if (!ModState.NoBaitLoss) return true;
            __result = 0f;
            return false;
        }
    }

    /// <summary>No weapon fire cooldown.</summary>
    [HarmonyPatch(typeof(Weapon), "HasCooldown")]
    internal static class Patch_NoCooldown
    {
        private static bool Prefix(ref bool __result)
        {
            if (!ModState.NoCooldown) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Tick speed: GameInfo.Awake recomputes TickMulti once per scene from the server
    /// tick rate; scale it right after so tick-based timers run faster/slower.
    /// </summary>
    [HarmonyPatch(typeof(GameInfo), "Awake")]
    internal static class Patch_TickSpeed
    {
        private static void Postfix()
        {
            if (Mathf.Approximately(ModState.TickSpeed, 1f)) return;
            try { GameInfo.TickMulti *= ModState.TickSpeed; } catch { }
        }
    }
}