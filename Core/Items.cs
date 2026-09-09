using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToFishModMenu
{
    /// <summary>
    /// Cached lookups for the item browser, explosion spawns, boss spawns and slot rigging.
    /// Lists are rebuilt only when the underlying game data changes.
    /// </summary>
    public static class ModItems
    {
        private static readonly List<Item> AllItems = new List<Item>();
        private static int _devCount = -1;
        private static Item _explosive;
        private static Creature _boss;

        public static int Count { get { Refresh(); return AllItems.Count; } }

        public static Item Get(int index)
        {
            Refresh();
            return index >= 0 && index < AllItems.Count ? AllItems[index] : null;
        }

        public static string Name(int index)
        {
            Item it = Get(index);
            if (!it) return "(none)";
            try { return it.GetName(); } catch { return it.name; }
        }

        private static void Refresh()
        {
            int n = -1;
            try
            {
                Item[] arr = GameInfo.ItemWithSkinsforCommands;
                n = arr != null ? arr.Length : -1;
            }
            catch { }

            if (n == _devCount && AllItems.Count > 0) return;
            _devCount = n;
            AllItems.Clear();
            _explosive = null;
            _boss = null;

            if (n > 0)
            {
                try
                {
                    foreach (Item it in GameInfo.ItemWithSkinsforCommands)
                        if (it && !AllItems.Contains(it)) AllItems.Add(it);
                }
                catch { }
            }

            if (AllItems.Count == 0)
            {
                // Fallback: scan item ids directly
                for (byte i = 0; i < 255; i++)
                {
                    try
                    {
                        Item it = GameInfo.IDToItem(i);
                        if (it && !AllItems.Contains(it)) AllItems.Add(it);
                    }
                    catch { }
                }
            }
        }

        /// <summary>Any item that has a Legendary skin, for slot-machine rigging.</summary>
        public static Item FindLegendarySkinItem()
        {
            Refresh();
            foreach (Item it in AllItems)
            {
                if (!it || it.SkinPreset == null) continue;
                try
                {
                    foreach (ItemSkin skin in it.SkinPreset.Skins)
                        if (skin.Rarity == Rarity.Legendary) return it;
                }
                catch { }
            }
            return null;
        }

        /// <summary>An Explosive (dynamite) item prefab, cached.</summary>
        public static Item ExplosivePrefab
        {
            get
            {
                Refresh();
                if (_explosive != null) return _explosive;
                foreach (Item it in AllItems)
                {
                    if (it && it.GetComponent<Explosive>() != null)
                    {
                        _explosive = it;
                        break;
                    }
                }
                return _explosive;
            }
        }

        /// <summary>A boss creature prefab, cached.</summary>
        public static Creature BossCreature
        {
            get
            {
                if (_boss != null) return _boss;
                int n = 0;
                try { n = GameInfo.AllCreatureCount; } catch { }
                for (int i = 0; i < n; i++)
                {
                    try
                    {
                        Creature c = GameInfo.GetCreature(i);
                        if (c && c.BossType != BossType.None)
                        {
                            _boss = c;
                            break;
                        }
                    }
                    catch { }
                }
                return _boss;
            }
        }
    }
}