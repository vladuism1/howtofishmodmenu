using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FishNet.Managing;

namespace HowToFishModMenu
{
    /// <summary>Central on/off state for every mod feature.</summary>
    public static class ModState
    {
        public static bool InfiniteMoney;      // money never decreases + free buys (host) / free-buy RPC (joiner)
        public static bool GodMode;            // immune to all damage (drowning, poison, fire, hunger)
        public static bool InfiniteFullness;   // hunger/stamina never drains
        public static bool InstantCatch;       // fish bites the instant the bait hits water
        public static bool AutoFish;           // auto-reel hooked fish + instant catch
        public static bool ForceShiny;         // every caught fish gets the rare "drip" skin
        public static bool Sunset;             // force sunset atmosphere
        public static bool BuiltInCheats;      // enable the game's own dev cheats (M/N money, O island)
        public static bool InfiniteAmmo;       // weapons never run out of ammo
        public static bool RigRoulette;        // casino roulette always pays out (host)
        public static bool RigSlots;           // slot machine always rolls the jackpot (host)
        public static bool AutoSell;           // automatically sell caught fish (host)
        public static bool OneShot;            // ServerSettings.OneShotEnabled
        public static bool NoBaitLoss;         // bait survives every catch
        public static bool NoCooldown;         // weapons fire with no cooldown
        public static bool EspFish;            // ESP overlay: fish / creatures
        public static bool EspPlayers;         // ESP overlay: players
        public static bool EspItems;           // ESP overlay: ground loot
        public static bool EspIslands;         // ESP overlay: island positions (from IslandManager)
        public static bool AimbotPlayers;      // auto-hit nearest player via Server.HitPlayer RPC
        public static bool AimbotFish;         // auto-hit nearest fish/creature via Server.HitCreature RPC
        public static bool AimbotBosses;       // auto-hit nearest boss (BossType != None)
        public static bool AimSoftSnap;        // client-side camera snap toward aimbot target (visual only)
        public static float AimRange = 60f;    // aimbot max distance in meters
        public static float AimFov = 30f;      // aimbot max angle from crosshair in degrees
        public static float SpeedMulti = 1f;   // movement speed multiplier
        public static float FishSizeMulti = 1f;// caught/spawned creature size multiplier
        public static float JumpMulti = 1f;    // jump power multiplier
        public static float DamageMulti = 1f;  // global damage multiplier (0 = no damage)
        public static float WaterOffset = 0f;  // ocean water level offset
        public static float TickSpeed = 1f;    // tick-based timer speed (experimental)
    }

    /// <summary>Standalone entry point used by the injector.</summary>
    public static class ModCore
    {
        public static void Entry()
        {
            Deps.Init(); // first: resolve Harmony/MonoMod/Cecil from embedded resources (memory-only)
            Log.Init();
            Log.Info("Entry called — creating mod menu");
            var go = new GameObject("VladModMenu");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MenuBehaviour>();
        }

        public static void Exit()
        {
#pragma warning disable CS0618 // FindObjectOfType is obsolete in Unity 6 but present in every version
            var mb = UnityEngine.Object.FindObjectOfType<MenuBehaviour>();
#pragma warning restore CS0618
            if (mb != null) UnityEngine.Object.Destroy(mb.gameObject);
        }
    }

    public class MenuBehaviour : MonoBehaviour
    {
        internal static MenuBehaviour Instance;

        private static readonly Harmony Harmony = new Harmony("vlad.HowToFishModMenu");
        private static bool _patched;

        // ---- config ----
        private static Settings _settings;

        // ---- menu state (PLITCH-style) ----
        private bool _menuOpen;
        private bool _cursorForced;
        private bool _prevCursorVisible;
        private CursorLockMode _prevCursorLock;
        private int _tab;
        // PLITCH sidebar: plain ASCII names (Unity's default font has no
        // unicode symbols — they render as boxes, see bug report screenshot)
        private readonly string[] _plitchTabs =
        {
            "Player", "Money", "Fishing", "Teleport", "Weapons",
            "Casino", "World", "Items", "Unlocks"
        };
        private readonly string[] _plitchTabSubs =
        {
            "Health / Movement", "Money / Baits", "Catch / Spawn", "Islands / Players",
            "Ammo / Upgrades", "Host only", "Ocean / ESP", "Browser / Skins", "Boats / Presets"
        };
        private string _search = string.Empty;
        private Vector2 _contentScroll;
        private Rect _windowRect = new Rect(60, 60, 860, 560);
        private const int WindowId = 0x564D4144; // "VMAD" — unique so game windows can't collide
        private int _spawnFishIndex;
        private int _itemIndex;
        private int _teleportIndex;
        private float _autoFishTimer;
        private float _upkeepTimer;
        private float _autoSellTimer;

        // ---- reflection targets ----
        private static FieldInfo _isReelingIn;
        private static FieldInfo _isReelingOut;
        private static FieldInfo _shinyChance;
        private static FieldInfo _maxHealthField;
        private static FieldInfo _maxFullnessField;
        private static FieldInfo _dmgMultiField;
        private static FieldInfo _oneShotField;
        private static FieldInfo _waterMgrInstanceField;
        private static FieldInfo _mainWaterField;
        private static float _waterBaseY;
        private static bool _waterBaseCaptured;

        // ---- cached UI ----
        private static Ui _ui;
        private static GUIContent _hintContent;

        // ---- in-game keybind rebinding -------
        private CfgEntry<KeyCode> _rebindEntry;
        private string _rebindLabel;

        // ---- ESP caches ----
        private static FieldInfo _aliveCreaturesField;
        private static Texture2D _espOutline;
        private static GUIStyle _espBoxStyle;
        private static GUIStyle _espLabelStyle;
        private const float EspMaxDistance = 120f;

        public void Awake()
        {
            Instance = this;
            Log.Info("[VladMod] Loading How To Fish Mod Menu v2.1.0");

            // ---- config ----
            try
            {
                Cfg.Init();
                _settings = new Settings();
                Cfg.Reload();
                ModState.GodMode = _settings.GodMode.Value;
                ModState.InfiniteFullness = _settings.InfFullness.Value;
                ModState.InfiniteMoney = _settings.InfMoney.Value;
                ModState.InstantCatch = _settings.InstantCatch.Value;
                ModState.AutoFish = _settings.AutoFish.Value;
                ModState.ForceShiny = _settings.ForceShiny.Value;
                ModState.Sunset = _settings.Sunset.Value;
                ModState.BuiltInCheats = _settings.BuiltInCheats.Value;
                ModState.InfiniteAmmo = _settings.InfAmmo.Value;
                ModState.RigRoulette = _settings.RigRoulette.Value;
                ModState.RigSlots = _settings.RigSlots.Value;
                ModState.AutoSell = _settings.AutoSell.Value;
                ModState.OneShot = _settings.OneShot.Value;
                ModState.NoBaitLoss = _settings.NoBaitLoss.Value;
                ModState.NoCooldown = _settings.NoCooldown.Value;
                ModState.EspFish = _settings.EspFish.Value;
                ModState.EspPlayers = _settings.EspPlayers.Value;
                ModState.EspItems = _settings.EspItems.Value;
                ModState.EspIslands = _settings.EspIslands.Value;
                ModState.AimbotPlayers = _settings.AimPlayers.Value;
                ModState.AimbotFish = _settings.AimFish.Value;
                ModState.AimbotBosses = _settings.AimBosses.Value;
                ModState.AimSoftSnap = _settings.AimSnap.Value;
                ModState.AimRange = _settings.AimRange.Value;
                ModState.AimFov = _settings.AimFov.Value;
                ModState.SpeedMulti = _settings.Speed.Value;
                ModState.FishSizeMulti = _settings.FishSize.Value;
                ModState.JumpMulti = _settings.Jump.Value;
                ModState.DamageMulti = _settings.Damage.Value;
                ModState.WaterOffset = _settings.Water.Value;
                ModState.TickSpeed = _settings.TickSpeed.Value;
            }
            catch (Exception e)
            {
                Log.Error("[VladMod] Config load failed: " + e.Message);
            }

            // ---- patches ----
            try
            {
                _isReelingIn = AccessTools.Field(typeof(FishingRod), "_isReelingIn");
                _isReelingOut = AccessTools.Field(typeof(FishingRod), "_isReelingOut");
                _shinyChance = AccessTools.Field(typeof(CreatureManager), "_shinyCreatureChance");
                _aliveCreaturesField = AccessTools.Field(typeof(CreatureManager), "_aliveCreatures");
                _maxHealthField = AccessTools.Field(typeof(PlayerVitals), "_maxHealth");
                _maxFullnessField = AccessTools.Field(typeof(PlayerVitals), "_maxFullness");
                _dmgMultiField = AccessTools.Field(typeof(ServerSettings), "<DamageMultiplier>k__BackingField");
                _oneShotField = AccessTools.Field(typeof(ServerSettings), "<OneShotEnabled>k__BackingField");
                _waterMgrInstanceField = AccessTools.Field(typeof(WaterManager), "_instance");
                _mainWaterField = AccessTools.Field(typeof(WaterManager), "_mainWater");

                if (!_patched)
                {
                    Harmony.PatchAll(typeof(MenuBehaviour).Assembly);
                    _patched = true;
                    Log.Info("[VladMod] Harmony patches applied");
                }
            }
            catch (Exception e)
            {
                Log.Error("[VladMod] Failed to apply patches: " + e);
            }
        }

        public void OnDestroy()
        {
            try { Cfg.Save(); } catch { }
            try { Harmony.UnpatchSelf(); } catch { }
        }

        private void Update()
        {
            // Toggle the menu with F1 or Insert
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.Insert))
                _menuOpen = !_menuOpen;

            // While open the menu needs the mouse: free the cursor, restore it on close.
            if (_menuOpen && !_cursorForced)
            {
                _prevCursorVisible = Cursor.visible;
                _prevCursorLock = Cursor.lockState;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                _cursorForced = true;
            }
            else if (!_menuOpen && _cursorForced)
            {
                Cursor.visible = _prevCursorVisible;
                Cursor.lockState = _prevCursorLock;
                _cursorForced = false;
            }

            // Quick keybinds (suspended while rebinding a key)
            if (_settings != null && _rebindEntry == null)
            {
                if (Input.GetKeyDown(_settings.KeyGod.Value)) SetGodMode(!ModState.GodMode);
                if (Input.GetKeyDown(_settings.KeyMoney.Value)) SetInfMoney(!ModState.InfiniteMoney);
                if (Input.GetKeyDown(_settings.KeyCatch.Value)) SetInstantCatch(!ModState.InstantCatch);
                if (Input.GetKeyDown(_settings.KeyFish.Value)) SetAutoFish(!ModState.AutoFish);
                if (Input.GetKeyDown(_settings.KeyEsp.Value))
                {
                    bool any = ModState.EspFish || ModState.EspPlayers || ModState.EspItems || ModState.EspIslands;
                    ModState.EspFish = ModState.EspPlayers = ModState.EspItems = ModState.EspIslands = !any;
                    _settings.EspFish.Value = _settings.EspPlayers.Value = _settings.EspItems.Value = _settings.EspIslands.Value = !any;
                }
            }

            // In-game rebinding: catch the next key pressed.
            // Mouse buttons are ignored (clicking would instantly rebind to Mouse0),
            // Escape cancels, Backspace unbinds.
            if (_rebindEntry != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    _rebindEntry = null;
                }
                else
                {
                    foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                    {
                        if (k == KeyCode.None || k == KeyCode.Escape) continue;
                        if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;
                        if (Input.GetKeyDown(k))
                        {
                            _rebindEntry.Value = (k == KeyCode.Backspace) ? KeyCode.None : k;
                            _rebindEntry = null;
                            break;
                        }
                    }
                }
            }

            try { AutoFishLoop(); } catch (Exception e) { Log.Warn("[VladMod] AutoFish error: " + e.Message); }
            try { AutoSellLoop(); } catch (Exception e) { Log.Warn("[VladMod] AutoSell error: " + e.Message); }
            try { AimbotTick(); } catch (Exception e) { Log.Warn("[VladMod] Aimbot error: " + e.Message); }

            // Slow upkeep: re-apply persistent world changes and shiny chance.
            _upkeepTimer -= Time.deltaTime;
            if (_upkeepTimer <= 0f)
            {
                _upkeepTimer = 1f;
                try { Upkeep(); } catch (Exception e) { Log.Warn("[VladMod] Upkeep error: " + e.Message); }
            }

            // Flush dirty config to disk (debounced so slider drags don't spam writes).
            try { Cfg.Tick(); } catch { }
        }

        // ------------------------------------------------------------------
        // Auto fish: the bait bites instantly (RandomizedCatchTime = 0 patch),
        // then we hold the reel button down until the fish is reeled in.
        // ------------------------------------------------------------------
        private void AutoFishLoop()
        {
            if (!ModState.AutoFish) return;
            _autoFishTimer -= Time.deltaTime;
            if (_autoFishTimer > 0f) return;
            _autoFishTimer = 0.2f;

            Player player = Player.LocalPlayer;
            if (!player || player.BlockInputs) return;

            FishingRod rod = player.Holding != null ? player.Holding.HeldItem as FishingRod : null;
            if (!rod) return;

            Bait bait = rod.Bait;
            if (!bait) return;

            if (bait.ItemOnBait)
            {
                try
                {
                    _isReelingIn.SetValue(rod, true);
                    _isReelingOut.SetValue(rod, false);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------
        // Auto sell: host sells every fish in the local player's inventory.
        // ------------------------------------------------------------------
        private void AutoSellLoop()
        {
            if (!ModState.AutoSell || !IsHost()) return;
            _autoSellTimer -= Time.deltaTime;
            if (_autoSellTimer > 0f) return;
            _autoSellTimer = 1.5f;

            Player player = Player.LocalPlayer;
            if (!player || player.Inventory == null) return;

            var toSell = new List<Item>();
            foreach (KeyValuePair<byte, Item> kv in player.Inventory._items)
            {
                Item it = kv.Value;
                if (it && it is Fish) toSell.Add(it);
            }
            foreach (Item it in toSell)
            {
                try
                {
                    MoneyManager.SellItem(it);
                    player.Inventory.RemoveItem(it);
                    it.DestroyItem(0, byte.MaxValue);
                }
                catch { }
            }
        }

        private float _aimbotTimer;

        // ------------------------------------------------------------------
        // Aimbot: uses verified Server RPCs (Server.HitPlayer / HitCreature),
        // whose RpcLogic has no ownership check, so they work as host AND
        // client. Camera snap is client-side visual only.
        // ------------------------------------------------------------------
        private void AimbotTick()
        {
            if (!ModState.AimbotPlayers && !ModState.AimbotFish && !ModState.AimbotBosses) return;
            Player local = Player.LocalPlayer;
            if (!local || local.BlockInputs) return;
            if (Server.Instance == null) return;
            Camera cam = GameInfo.CurCamera != null ? GameInfo.CurCamera : Camera.main;
            if (!cam) return;

            _aimbotTimer -= Time.deltaTime;
            bool doSnap = ModState.AimSoftSnap;

            // Soft camera snap every frame (visual, client-side, MP-safe).
            if (doSnap)
            {
                try
                {
                    Transform t = FindAimTransform(cam);
                    if (t != null)
                    {
                        Vector3 aimPos = t.position + Vector3.up * 1.2f;
                        Vector3 dir = aimPos - cam.transform.position;
                        if (dir.sqrMagnitude > 0.01f && dir.sqrMagnitude < ModState.AimRange * ModState.AimRange)
                        {
                            Quaternion want = Quaternion.LookRotation(dir.normalized);
                            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, want, 0.25f);
                        }
                    }
                }
                catch { }
            }

            // Auto-hit trigger every 0.25s. Requires holding a weapon/melee? No —
            // the RPCs work bare-handed too, but gate on not-blocked inputs only.
            if (_aimbotTimer > 0f) return;
            _aimbotTimer = 0.25f;

            try
            {
                int dmg = AimbotDamage();
                Vector3 from = cam.transform.position;
                Vector3 fwd = cam.transform.forward;

                if (ModState.AimbotPlayers)
                {
                    Player best = FindAimPlayer(cam, from, fwd);
                    if (best != null)
                    {
                        Vector3 hp = best.Transform.position + Vector3.up * 1.2f;
                        Vector3 dir = (hp - from).normalized;
                        SafeCall(() => Server.Instance.HitPlayer(best, dmg, dir * 10f, hp, (byte)DamageType.Generic, local));
                    }
                }
                if (ModState.AimbotFish || ModState.AimbotBosses)
                {
                    Creature best = FindAimCreature(cam, from, fwd, ModState.AimbotBosses, ModState.AimbotFish);
                    if (best != null)
                    {
                        Vector3 hp = best.transform.position + Vector3.up * 0.8f;
                        Vector3 dir = (hp - from).normalized;
                        SafeCall(() => Server.Instance.HitCreature(best, local, dmg, hp, dir));
                    }
                }
            }
            catch { }
        }

        private int AimbotDamage()
        {
            try
            {
                Player p = Player.LocalPlayer;
                Item held = p != null && p.Holding != null ? p.Holding.HeldItem : null;
                Weapon w = held as Weapon;
                if (w != null && w.Attachments != null && w.Attachments.Damage > 0)
                    return Mathf.Clamp(w.Attachments.Damage * Mathf.Max(1, Mathf.RoundToInt(ModState.DamageMulti)), 1, 999999);
            }
            catch { }
            if (ModState.OneShot) return 999999;
            return Mathf.Clamp(Mathf.RoundToInt(50f * ModState.DamageMulti), 1, 999999);
        }

        private Player FindAimPlayer(Camera cam, Vector3 from, Vector3 fwd)
        {
            Player local = Player.LocalPlayer;
            Player best = null;
            float bestScore = float.MaxValue;
            var alive = PlayerManager.AlivePlayers;
            if (alive == null) return null;
            foreach (Player p in alive)
            {
                if (!p || p == local || p.Transform == null || p.Vitals == null) continue;
                try
                {
                    if (p.Vitals.Health <= 0) continue;
                    Vector3 tp = p.Transform.position + Vector3.up * 1.2f;
                    Vector3 to = tp - from;
                    float dist = to.magnitude;
                    if (dist > ModState.AimRange || dist < 0.5f) continue;
                    float ang = Vector3.Angle(fwd, to.normalized);
                    if (ang > ModState.AimFov) continue;
                    float score = ang * 2f + dist * 0.1f;
                    if (score < bestScore) { bestScore = score; best = p; }
                }
                catch { }
            }
            return best;
        }

        private Creature FindAimCreature(Camera cam, Vector3 from, Vector3 fwd, bool bosses, bool fishes)
        {
            if (CreatureManager.Instance == null || _aliveCreaturesField == null) return null;
            Creature best = null;
            float bestScore = float.MaxValue;
            var list = _aliveCreaturesField.GetValue(CreatureManager.Instance) as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object o in list)
            {
                var c = o as Creature;
                if (!c || c == null || c.transform == null) continue;
                try
                {
                    if (c.IsDead) continue;
                    bool isBoss = c.BossType != BossType.None;
                    if (isBoss && !bosses) continue;
                    if (!isBoss && !fishes) continue;
                    Vector3 tp = c.transform.position + Vector3.up * 0.8f;
                    Vector3 to = tp - from;
                    float dist = to.magnitude;
                    if (dist > ModState.AimRange || dist < 0.5f) continue;
                    float ang = Vector3.Angle(fwd, to.normalized);
                    if (ang > ModState.AimFov) continue;
                    float score = ang * 2f + dist * 0.1f;
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                catch { }
            }
            return best;
        }

        private Transform FindAimTransform(Camera cam)
        {
            Vector3 from = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            Transform bestT = null;
            if (ModState.AimbotPlayers)
            {
                Player bp = FindAimPlayer(cam, from, fwd);
                if (bp != null && bp.Transform != null) { bestT = bp.Transform; }
            }
            if (ModState.AimbotFish || ModState.AimbotBosses)
            {
                Creature bc = FindAimCreature(cam, from, fwd, ModState.AimbotBosses, ModState.AimbotFish);
                if (bc != null && bc.transform != null && bestT == null) bestT = bc.transform;
            }
            return bestT;
        }

        // ------------------------------------------------------------------
        // One-second upkeep: keeps world-wide values applied.
        // ------------------------------------------------------------------
        private void Upkeep()
        {
            if (ModState.ForceShiny && CreatureManager.Instance != null)
            {
                try
                {
                    int cur = (int)_shinyChance.GetValue(CreatureManager.Instance);
                    if (cur < 100) _shinyChance.SetValue(CreatureManager.Instance, 100);
                }
                catch { }
            }

            if (ModState.JumpMulti != 1f && Player.LocalPlayer != null && Player.LocalPlayer.Movement != null)
            {
                // Single shared base value lives in Patch_JumpPower — never compounds.
                try { Patch_JumpPower.ApplyJump(Player.LocalPlayer.Movement); } catch { }
            }

            if (ModState.DamageMulti != 1f || ModState.OneShot)
            {
                try
                {
                    if (_dmgMultiField != null) _dmgMultiField.SetValue(null, ModState.DamageMulti);
                    if (_oneShotField != null) _oneShotField.SetValue(null, ModState.OneShot);
                }
                catch { }
            }

            if (ModState.WaterOffset != 0f)
            {
                try
                {
                    object wm = _waterMgrInstanceField != null ? _waterMgrInstanceField.GetValue(null) : null;
                    if (wm != null && _mainWaterField != null)
                    {
                        var water = (Transform)_mainWaterField.GetValue(wm);
                        if (water != null)
                        {
                            if (!_waterBaseCaptured) { _waterBaseY = water.position.y; _waterBaseCaptured = true; }
                            water.position = new Vector3(water.position.x, _waterBaseY + ModState.WaterOffset, water.position.z);
                        }
                    }
                }
                catch { }
            }
        }

        // ==================================================================
        // Setters (persist to config, run side effects)
        // ==================================================================
        private void SetGodMode(bool v) { ModState.GodMode = v; _settings.GodMode.Value = v; }
        private void SetInfMoney(bool v) { ModState.InfiniteMoney = v; _settings.InfMoney.Value = v; }
        private void SetInstantCatch(bool v) { ModState.InstantCatch = v; _settings.InstantCatch.Value = v; }
        private void SetAutoFish(bool v) { ModState.AutoFish = v; _settings.AutoFish.Value = v; }

        private void SetSunset(bool v)
        {
            ModState.Sunset = v; _settings.Sunset.Value = v;
            try { ShaderManager.ToggleSunset(v); } catch { }
        }

        private void SetCheats(bool v)
        {
            ModState.BuiltInCheats = v; _settings.BuiltInCheats.Value = v;
            try { ClientSettings.ToggleCheats(v); } catch { }
        }

        // ==================================================================
        // PLITCH-style menu shell (still IMGUI, new look)
        // ==================================================================
        private void OnGUI()
        {
            try { EnsureUi(); } catch (Exception e) { Log.Warn("[VladMod] UI init failed: " + e.Message); return; }
            if (_ui == null) return;

            // ESP overlay draws regardless of whether the menu is open
            if (ModState.EspFish || ModState.EspPlayers || ModState.EspItems || ModState.EspIslands)
            {
                try { DrawEsp(); } catch { }
            }

            if (!_menuOpen)
            {
                if (_hintContent == null)
                    _hintContent = new GUIContent("How To Fish Mod Menu — press F1 or Insert");
                GUI.Label(new Rect(10, Screen.height - 26, 340, 22), _hintContent, _ui.Hint);
                return;
            }

            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, string.Empty, _ui.Window,
                GUILayout.MinWidth(760), GUILayout.MaxWidth(1100));
        }

        private void DrawWindow(int id)
        {
            // If a previous frame left GUI disabled via an early exit, reset it.
            GUI.enabled = true;
            GUILayout.BeginVertical();

            // ---- PLITCH top banner (ASCII only — default font lacks symbols) ----
            GUILayout.BeginHorizontal(_ui.Banner, GUILayout.Height(54));
            GUILayout.Space(10);
            GUILayout.BeginVertical(GUILayout.Width(280));
            GUILayout.Label("HOW TO FISH", _ui.BannerTitle);
            GUILayout.Label("30 CODES  |  v2.2.0 by Vlad  |  @vladuism", _ui.BannerSub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(200));
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", _ui.SearchIcon, GUILayout.Width(52));
            string newSearch = GUILayout.TextField(_search ?? string.Empty, _ui.Search, GUILayout.Height(26));
            if (newSearch != _search) { _search = newSearch; _contentScroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
            if (GUILayout.Button("X", _ui.CloseBtn, GUILayout.Width(30), GUILayout.Height(30)))
                _menuOpen = false;
            GUILayout.Space(6);
            GUILayout.EndHorizontal();

            // ---- body: sidebar + content ----
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // sidebar
            GUILayout.BeginVertical(_ui.Sidebar, GUILayout.Width(180), GUILayout.ExpandHeight(true));
            GUILayout.Space(8);
            for (int i = 0; i < _plitchTabs.Length; i++)
            {
                bool active = _tab == i;
                int count = ActiveCountForTab(i);
                int total = TotalCountForTab(i);
                string label = (active ? "> " : "  ") + _plitchTabs[i];
                if (GUILayout.Button(label, active ? _ui.SideActive : _ui.SideInactive, GUILayout.Height(32)))
                    { _tab = i; _contentScroll = Vector2.zero; }
                // sub-label + counter under active tab
                if (active)
                    GUILayout.Label(_plitchTabSubs[i] + "  |  " + count + "/" + total + " ON", _ui.SideCounter);
                else
                    GUILayout.Label(_plitchTabSubs[i], _ui.SideSub);
            }
            GUILayout.FlexibleSpace();
            DrawSidebarStatus();
            GUILayout.Space(8);
            GUILayout.EndVertical();

            // content — ONE scroll view, closed in finally so an exception in
            // any row can never corrupt the IMGUI layout stack (that was the
            // "Mismatched LayoutGroup" bug: per-tab Begin/EndScrollView pairs
            // skipped their End when a row threw).
            GUILayout.BeginVertical(_ui.Content, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Space(6);
            _contentScroll = GUILayout.BeginScrollView(_contentScroll, GUILayout.ExpandHeight(true));
            try
            {
                if (!string.IsNullOrEmpty(_search))
                    DrawSearchResults();
                else
                {
                    switch (_tab)
                    {
                        case 0: DrawPlayerTab(); break;
                        case 1: DrawMoneyTab(); break;
                        case 2: DrawFishingTab(); break;
                        case 3: DrawTeleportTab(); break;
                        case 4: DrawWeaponsTab(); break;
                        case 5: DrawCasinoTab(); break;
                        case 6: DrawWorldTab(); break;
                        case 7: DrawItemsTab(); break;
                        default: DrawUnlockTab(); break;
                    }
                }
            }
            catch (Exception e) { Log.Warn("[VladMod] Tab draw failed: " + e.Message); }
            finally { GUILayout.EndScrollView(); }
            DrawFooter();
            GUILayout.Space(4);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0, 0, 10000, 54));
            GUILayout.EndVertical();
        }

        private int ActiveCountForTab(int tab)
        {
            switch (tab)
            {
                case 0: return (ModState.GodMode ? 1 : 0) + (ModState.InfiniteFullness ? 1 : 0);
                case 1: return (ModState.InfiniteMoney ? 1 : 0) + (ModState.AutoSell ? 1 : 0);
                case 2: return (ModState.InstantCatch ? 1 : 0) + (ModState.AutoFish ? 1 : 0) + (ModState.ForceShiny ? 1 : 0);
                case 3: return 0;
                case 4: return (ModState.InfiniteAmmo ? 1 : 0) + (ModState.NoCooldown ? 1 : 0)
                    + (ModState.AimbotPlayers ? 1 : 0) + (ModState.AimbotFish ? 1 : 0) + (ModState.AimbotBosses ? 1 : 0);
                case 5: return (ModState.RigRoulette ? 1 : 0) + (ModState.RigSlots ? 1 : 0);
                case 6: return (ModState.OneShot ? 1 : 0) + (ModState.Sunset ? 1 : 0) + (ModState.BuiltInCheats ? 1 : 0)
                    + (ModState.EspFish ? 1 : 0) + (ModState.EspPlayers ? 1 : 0) + (ModState.EspItems ? 1 : 0) + (ModState.EspIslands ? 1 : 0);
                case 7: return 0;
                default: return 0;
            }
        }

        private int TotalCountForTab(int tab)
        {
            switch (tab)
            {
                case 0: return 2; case 1: return 2; case 2: return 3; case 3: return 0;
                case 4: return 5; case 5: return 2; case 6: return 7; default: return 0;
            }
        }

        private void DrawSidebarStatus()
        {
            GUILayout.BeginHorizontal(_ui.StatusBox, GUILayout.Height(40));
            GUILayout.Space(8);
            GUILayout.BeginVertical();
            GUILayout.Label(IsHost() ? "HOST" : (IsClient() ? "CLIENT" : "IDLE"), IsHost() ? _ui.StatusHost : _ui.StatusClient);
            Player p = Player.LocalPlayer;
            GUILayout.Label(p ? ("$" + MoneyManager.Money + "  •  HP " + (p.Vitals != null ? p.Vitals.Health.ToString() : "?")) : "Not in game", _ui.StatusSub);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // ==================================================================
        // TABS
        // ==================================================================

        private void DrawPlayerTab()
        {
            PlitchSection("Player", "Health / Movement");
            // content scroll is opened/closed centrally in DrawWindow


            bool g = CheatRow("God Mode", "No damage, no drowning, no hunger drain", ModState.GodMode, _settings.KeyGod.Value.ToString());
            if (g != ModState.GodMode) SetGodMode(g);
            bool f = CheatRow("Infinite Fullness", "Hunger and stamina never drain", ModState.InfiniteFullness, "—");
            if (f != ModState.InfiniteFullness) { ModState.InfiniteFullness = f; _settings.InfFullness.Value = f; }

            PlitchSlider(_settings.Speed, ref ModState.SpeedMulti, 0.5f, 5f, "Player Speed", "x");
            PlitchSlider(_settings.Jump, ref ModState.JumpMulti, 0.5f, 5f, "Jump Power", "x");

            if (PlitchButton("+  Max out health / fullness", true))
                MaxVitals();
            GUILayout.Space(6);
            if (PlitchButton("Skip intro / tutorial  (host)", false))
                SkipIntroTutorial();

            Player p = Player.LocalPlayer;
            if (!p)
            {
                GUILayout.Space(6);
                GUILayout.Label("Not in a game yet — join or host a lobby.", _ui.LabelDim);
            }
            else
            {
                GUILayout.Space(8);
                SubHeader("Vitals");
                int hp = 0, hpMax = 100, ful = 0, fulMax = 100;
                try
                {
                    if (p.Vitals != null)
                    {
                        hp = p.Vitals.Health; ful = p.Vitals.Fullness;
                    }
                    hpMax = VitalsMax(_maxHealthField, 100);
                    fulMax = VitalsMax(_maxFullnessField, 100);
                }
                catch { }
                Bar("Health", hp, hpMax, new Color(0.25f, 0.85f, 0.45f));
                Bar("Fullness", ful, fulMax, new Color(0.95f, 0.7f, 0.3f));
            }

            // (scroll closed centrally)

        }

        private void DrawMoneyTab()
        {
            PlitchSection("Money", "Infinite / Auto-sell");
            // content scroll is opened/closed centrally in DrawWindow


            bool m = CheatRow("Infinite Money", "KEPT IN MP — free purchases via RPC (host: also locks pool)", ModState.InfiniteMoney, _settings.KeyMoney.Value.ToString());
            if (m != ModState.InfiniteMoney) SetInfMoney(m);
            bool s = CheatRow("Auto-Sell Fish", "Automatically sell caught fish (host)", ModState.AutoSell, "—");
            if (s != ModState.AutoSell) { ModState.AutoSell = s; _settings.AutoSell.Value = s; }

            bool hostMoney = IsHost();
            GUI.enabled = hostMoney;
            if (PlitchButton(hostMoney ? "+  Give max money  (+$99999)" : "+  Give max money (HOST ONLY)", false))
                GiveMaxMoney();
            GUI.enabled = true;
            if (!hostMoney && IsClient())
                GUILayout.Label("As client, use Infinite Money + buy — purchases go through free.", _ui.CardDesc);
            GUILayout.Space(6);
            if (PlitchButton("Give all baits  (free via RPC — works in MP)", false))
                GiveAllBaits();

            Player p = Player.LocalPlayer;
            GUILayout.Space(8);
            GUILayout.BeginVertical(_ui.Card);
            GUILayout.BeginHorizontal();
            GUILayout.Label("CURRENT MONEY", _ui.CardDesc);
            GUILayout.FlexibleSpace();
            GUILayout.Label(p ? "$" + MoneyManager.Money : "—", _ui.CardTitle);
            GUILayout.EndHorizontal();
            GUILayout.Label(IsHost()
                ? "HOST — money changes apply to everyone."
                : "CLIENT — free-buy RPCs work, shared pool belongs to host.",
                _ui.CardDesc);
            GUILayout.EndVertical();

            // (scroll closed centrally)

        }

        private void DrawFishingTab()
        {
            PlitchSection("Fishing", "Catch / Spawn");
            // content scroll is opened/closed centrally in DrawWindow


            bool c = CheatRow("Instant Catch", "Fish bites the instant bait hits water", ModState.InstantCatch, _settings.KeyCatch.Value.ToString());
            if (c != ModState.InstantCatch) SetInstantCatch(c);
            bool a = CheatRow("Auto Fish", "Instant bite + auto reel", ModState.AutoFish, _settings.KeyFish.Value.ToString());
            if (a != ModState.AutoFish) SetAutoFish(a);
            bool sh = CheatRow("Always Shiny", "Every caught fish gets the rare drip skin", ModState.ForceShiny, "—");
            if (sh != ModState.ForceShiny) { ModState.ForceShiny = sh; _settings.ForceShiny.Value = sh; }

            PlitchSlider(_settings.FishSize, ref ModState.FishSizeMulti, 0.5f, 10f, "Fish Size", "x");

            bool canDup = IsHost();
            bool dupWas = GUI.enabled;
            GUI.enabled = canDup;
            if (PlitchButton(canDup ? "Duplicate held item" : "Duplicate held item  (host only)", false))
                DuplicateHeldItem();
            GUI.enabled = dupWas;
            GUILayout.Space(6);

            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("SPAWN CREATURE  (HOST)", _ui.CardDesc);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                _spawnFishIndex = ClampIdx(_spawnFishIndex - 1, GameInfo.AllCreatureCount);
            GUILayout.Label(CreatureName(_spawnFishIndex), _ui.CardTitle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                _spawnFishIndex = ClampIdx(_spawnFishIndex + 1, GameInfo.AllCreatureCount);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = IsHost();
            if (PlitchButton("Spawn creature in front of you", true))
                SpawnCreature(_spawnFishIndex);
            GUI.enabled = wasEnabled;
            GUILayout.EndVertical();

            // (scroll closed centrally)

        }

        private void DrawTeleportTab()
        {
            PlitchSection("Teleport", "Islands / Players");
            // content scroll is opened/closed centrally in DrawWindow


            // BUG FIX (verified via IL): OnlineIslandManager.SpawnIsland returns
            // early unless IsServerInitialized, so island SWAPS are host-only.
            // Self-teleport via Server.TeleportPlayer is a ServerRPC with no
            // ownership check, so it works as client too.
            bool host = IsHost();
            if (!host && IsClient())
                GUILayout.Label("CLIENT MODE — island swaps are host-only. Use MP-safe self-teleports below.", _ui.CardTitle);

            GUI.enabled = host;
            if (PlitchButton(host ? ">  Teleport to next island (host)" : ">  Next island (HOST ONLY)", true))
                SafeCall(() => OnlineIslandManager.TpToNextIsland(false));
            GUILayout.Space(6);
            if (PlitchButton(host ? "Teleport to previous island (host)" : "Previous island (HOST ONLY)", false))
                SafeCall(() => OnlineIslandManager.TpToNextIsland(true));
            GUILayout.Space(6);
            if (PlitchButton(host ? "Unlock ALL islands (host)" : "Unlock ALL islands (HOST ONLY)", false))
                SafeCall(() => OnlineIslandManager.Instance?.UnlockIsland(byte.MaxValue));
            GUI.enabled = true;
            GUILayout.Space(6);

            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label(host ? "JUMP TO ISLAND  (HOST — swaps for everyone)" : "JUMP TO ISLAND  (HOST ONLY — swaps for everyone)", _ui.CardDesc);
            GUILayout.BeginHorizontal();
            GUI.enabled = host;
            for (byte i = 1; i <= 6; i++)
            {
                byte island = i; // copy: lambdas capture the for-loop variable by reference
                if (GUILayout.Button(island.ToString(), _ui.StepBtn, GUILayout.Height(26)))
                    SafeCall(() => OnlineIslandManager.TpToSpecificIsland(island));
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.Label("MP-SAFE: teleport YOURSELF to an island position (works as client).", _ui.CardDesc);
            GUILayout.BeginHorizontal();
            for (byte i = 1; i <= 6; i++)
            {
                byte island = i;
                if (GUILayout.Button("Me>" + island, _ui.Btn, GUILayout.Height(24)))
                    TeleportMeToIsland(island);
            }
            GUILayout.EndHorizontal();
            if (PlitchButton("Teleport ME to current island center (MP-safe)", false))
                TeleportMeToCurrentIsland();
            GUILayout.EndVertical();
            GUILayout.Space(6);

            var players = PlayerManager.Players;
            if (players != null && players.Count > 0)
            {
                _teleportIndex = Mathf.Clamp(_teleportIndex, 0, players.Count - 1);
                GUILayout.BeginVertical(_ui.Card);
                GUILayout.Label("PLAYERS  (MP-SAFE via Server RPC)", _ui.CardDesc);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                    _teleportIndex = (_teleportIndex + players.Count - 1) % players.Count;
                Player t = players[_teleportIndex];
                string label = "Player " + (_teleportIndex + 1) + (t.Vitals != null ? "  (HP " + t.Vitals.Health + ")" : "");
                GUILayout.Label(label, _ui.CardTitle, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(">", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                    _teleportIndex = (_teleportIndex + 1) % players.Count;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                if (PlitchButton("Teleport to this player", false))
                    TeleportMeTo(t);
                GUILayout.Space(6);
                if (PlitchButton("Pull this player to me", false))
                    PullPlayerToMe(t);
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Damage 50", _ui.Btn, GUILayout.Height(26)))
                    DamagePlayer(t, 50);
                if (GUILayout.Button("One-shot", _ui.Btn, GUILayout.Height(26)))
                    DamagePlayer(t, 999999);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            if (PlitchButton("+  Revive all dead players", true))
                ReviveAll();

            // (scroll closed centrally)

        }

        private void DrawWeaponsTab()
        {
            PlitchSection("Weapons", "Ammo / Aimbot / Upgrades");
            // content scroll is opened/closed centrally in DrawWindow


            bool a = CheatRow("Infinite Ammo", "Weapons never run out, no reloads (works in MP)", ModState.InfiniteAmmo, "—");
            if (a != ModState.InfiniteAmmo) { ModState.InfiniteAmmo = a; _settings.InfAmmo.Value = a; }
            bool cd = CheatRow("No Cooldown", "Weapons fire with no cooldown (works in MP)", ModState.NoCooldown, "—");
            if (cd != ModState.NoCooldown) { ModState.NoCooldown = cd; _settings.NoCooldown.Value = cd; }

            SubHeader("Aimbot  (MP-safe Server RPCs)");
            bool ap = CheatRow("Aimbot Players", "Auto-hit nearest player in crosshair", ModState.AimbotPlayers, "—");
            if (ap != ModState.AimbotPlayers) { ModState.AimbotPlayers = ap; _settings.AimPlayers.Value = ap; }
            bool af = CheatRow("Aimbot Fish", "Auto-hit nearest fish / creature", ModState.AimbotFish, "—");
            if (af != ModState.AimbotFish) { ModState.AimbotFish = af; _settings.AimFish.Value = af; }
            bool ab = CheatRow("Aimbot Bosses", "Auto-hit nearest boss (BossType != None)", ModState.AimbotBosses, "—");
            if (ab != ModState.AimbotBosses) { ModState.AimbotBosses = ab; _settings.AimBosses.Value = ab; }
            bool sn = CheatRow("Camera Snap", "Client-side camera eases toward target (visual)", ModState.AimSoftSnap, "—");
            if (sn != ModState.AimSoftSnap) { ModState.AimSoftSnap = sn; _settings.AimSnap.Value = sn; }
            PlitchSlider(_settings.AimRange, ref ModState.AimRange, 10f, 150f, "Aim Range", "m");
            PlitchSlider(_settings.AimFov, ref ModState.AimFov, 5f, 90f, "Aim FOV", "°");

            Player p = Player.LocalPlayer;
            if (!p || p.Holding == null || p.Holding.HeldItem == null)
            {
                GUILayout.Label("Hold a weapon to see its stats and upgrades.", _ui.LabelDim);
                // (scroll closed centrally)

                return;
            }

            Item held = p.Holding.HeldItem;
            GUILayout.BeginVertical(_ui.Card);
            if (held is Weapon w)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("AMMO", _ui.CardDesc);
                GUILayout.FlexibleSpace();
                GUILayout.Label(w.Ammo + " / " + (w.Attachments != null ? w.Attachments.AmmoPerMag : 0), _ui.CardTitle);
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
                if (PlitchButton("Refill ammo now", false))
                {
                    try
                    {
                        var f = AccessTools.Field(typeof(Weapon), "<Ammo>k__BackingField");
                        f.SetValue(w, w.Attachments != null ? w.Attachments.AmmoPerMag : 1);
                    }
                    catch { }
                }
                GUILayout.Space(6);
                if (PlitchButton("Free bullet damage upgrade", false))
                    SafeCall(() => Server.Instance.BuyBulletUpgrade(w));
                GUILayout.Space(6);
                if (PlitchButton("Free attachment (all slots)", false))
                {
                    for (byte i = 0; i < 4; i++)
                    {
                        try { Server.Instance.BuyAttachment(w, i); } catch { break; }
                    }
                }
            }
            else if (held is Melee melee)
            {
                if (PlitchButton("Free sharpness upgrade", false))
                    SafeCall(() => Server.Instance.BuySharpnessUpgrade(melee));
            }
            else
            {
                GUILayout.Label("Held item is not a weapon.", _ui.LabelDim);
            }
            GUILayout.EndVertical();

            // (scroll closed centrally)

        }

        private void DrawCasinoTab()
        {
            PlitchSection("Casino", "Host only");
            // content scroll is opened/closed centrally in DrawWindow


            bool r = CheatRow("Rig Roulette", "Every spin pays out (host forces result)", ModState.RigRoulette, "—");
            if (r != ModState.RigRoulette) { ModState.RigRoulette = r; _settings.RigRoulette.Value = r; }
            bool s = CheatRow("Rig Slots", "Slot machine always rolls the jackpot", ModState.RigSlots, "—");
            if (s != ModState.RigSlots) { ModState.RigSlots = s; _settings.RigSlots.Value = s; }

            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("HOW IT WORKS", _ui.CardDesc);
            GUILayout.Label("Roulette: host picks winning color — we force it to your bet. Slots: cheat-skin hook feeds a legendary item so jackpot always hits.", _ui.CardDesc);
            GUILayout.Label("Works only when you host the lobby.", _ui.CardTitle);
            GUILayout.EndVertical();

            // (scroll closed centrally)

        }

        private void DrawWorldTab()
        {
            PlitchSection("World", "Combat / Ocean / ESP");
            // content scroll is opened/closed centrally in DrawWindow


            PlitchSlider(_settings.Damage, ref ModState.DamageMulti, 0f, 10f, "Damage Multiplier", "x");
            if (!Mathf.Approximately(ModState.DamageMulti, 1f))
            {
                try
                {
                    if (_dmgMultiField != null) _dmgMultiField.SetValue(null, ModState.DamageMulti);
                }
                catch { }
            }
            bool os = CheatRow("One-Shot", "Built-in one-shot everything", ModState.OneShot, "—");
            if (os != ModState.OneShot)
            {
                ModState.OneShot = os; _settings.OneShot.Value = os;
                try
                {
                    if (_oneShotField != null) _oneShotField.SetValue(null, os);
                }
                catch { }
            }

            PlitchSlider(_settings.Water, ref ModState.WaterOffset, -3f, 3f, "Ocean Level", "m");

            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("ACTIONS  (HOST)", _ui.CardDesc);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = IsHost();
            if (PlitchButton("Spawn explosion at me", false))
                SpawnExplosion();
            GUILayout.Space(6);
            if (PlitchButton("Spawn boss", false))
                SpawnBoss();
            GUILayout.Space(6);
            if (PlitchButton("Kill all creatures", false))
                KillAllCreatures();
            GUI.enabled = wasEnabled;
            GUILayout.EndVertical();
            GUILayout.Space(6);

            bool sun = CheatRow("Force Sunset", "Sunset atmosphere override", ModState.Sunset, "—");
            if (sun != ModState.Sunset) SetSunset(sun);
            bool ch = CheatRow("Dev Cheats", "Built-in cheats: M/N money, O island", ModState.BuiltInCheats, "—");
            if (ch != ModState.BuiltInCheats) SetCheats(ch);

            SubHeader("ESP Overlay");
            bool ef = CheatRow("ESP Fish", "Overlay boxes on fish / creatures", ModState.EspFish, _settings.KeyEsp.Value.ToString());
            if (ef != ModState.EspFish) { ModState.EspFish = ef; _settings.EspFish.Value = ef; }
            bool ep = CheatRow("ESP Players", "Overlay boxes on players", ModState.EspPlayers, _settings.KeyEsp.Value.ToString());
            if (ep != ModState.EspPlayers) { ModState.EspPlayers = ep; _settings.EspPlayers.Value = ep; }
            bool ei = CheatRow("ESP Loot", "Overlay boxes on ground loot / weapons", ModState.EspItems, _settings.KeyEsp.Value.ToString());
            if (ei != ModState.EspItems) { ModState.EspItems = ei; _settings.EspItems.Value = ei; }
            bool esl = CheatRow("ESP Islands", "Markers to every island position (IslandManager)", ModState.EspIslands, _settings.KeyEsp.Value.ToString());
            if (esl != ModState.EspIslands) { ModState.EspIslands = esl; _settings.EspIslands.Value = esl; }

            SubHeader("Tick Speed  (experimental)");
            float oldTick = ModState.TickSpeed;
            PlitchSlider(_settings.TickSpeed, ref ModState.TickSpeed, 0.5f, 3f, "Tick Speed", "x");
            if (!Mathf.Approximately(oldTick, ModState.TickSpeed))
            {
                try
                {
                    if (oldTick > 0f) GameInfo.TickMulti = GameInfo.TickMulti / oldTick * ModState.TickSpeed;
                    else GameInfo.TickMulti *= ModState.TickSpeed;
                }
                catch { }
            }

            SubHeader("Achievements");
            if (PlitchButton("Unlock ALL Steam achievements", false))
                SafeCall(() => AchievementManager.ToggleAllAchievements(true));
            // (scroll closed centrally)

        }

        private void DrawPresets()
        {
            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("PRESETS  —  one click, multiple cheats", _ui.CardDesc);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fishing god", _ui.Btn, GUILayout.Height(26)))
            {
                ApplyPreset(new[]
                {
                    (_settings.InstantCatch, true), (_settings.AutoFish, true), (_settings.ForceShiny, true), (_settings.NoBaitLoss, true)
                }, (_settings.FishSize, 2.5f));
            }
            if (GUILayout.Button("War god", _ui.Btn, GUILayout.Height(26)))
            {
                ApplyPreset(new[]
                {
                    (_settings.GodMode, true), (_settings.InfAmmo, true), (_settings.NoCooldown, true), (_settings.OneShot, true)
                }, (_settings.Damage, 10f));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Money farm", _ui.Btn, GUILayout.Height(26)))
            {
                ApplyPreset(new[]
                {
                    (_settings.InfMoney, true), (_settings.AutoSell, true), (_settings.InstantCatch, true), (_settings.AutoFish, true)
                }, null);
            }
            if (GUILayout.Button("Everything", _ui.BtnAccent, GUILayout.Height(26)))
            {
                ApplyPreset(new[]
                {
                    (_settings.InfMoney, true), (_settings.GodMode, true), (_settings.InfFullness, true), (_settings.InstantCatch, true),
                    (_settings.AutoFish, true), (_settings.ForceShiny, true), (_settings.Sunset, false), (_settings.InfAmmo, true),
                    (_settings.NoCooldown, true), (_settings.NoBaitLoss, true), (_settings.AutoSell, true), (_settings.OneShot, false)
                }, (_settings.Speed, 1.5f));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void ApplyPreset((CfgEntry<bool>, bool)[] toggles, (CfgEntry<float>, float)? slider)
        {
            try
            {
                foreach (var (entry, val) in toggles)
                {
                    entry.Value = val;
                    if (entry == _settings.InstantCatch) ModState.InstantCatch = val;
                    else if (entry == _settings.AutoFish) ModState.AutoFish = val;
                    else if (entry == _settings.ForceShiny) ModState.ForceShiny = val;
                    else if (entry == _settings.NoBaitLoss) ModState.NoBaitLoss = val;
                    else if (entry == _settings.GodMode) ModState.GodMode = val;
                    else if (entry == _settings.InfAmmo) ModState.InfiniteAmmo = val;
                    else if (entry == _settings.NoCooldown) ModState.NoCooldown = val;
                    else if (entry == _settings.OneShot) ModState.OneShot = val;
                    else if (entry == _settings.InfMoney) ModState.InfiniteMoney = val;
                    else if (entry == _settings.AutoSell) ModState.AutoSell = val;
                    else if (entry == _settings.InfFullness) ModState.InfiniteFullness = val;
                    else if (entry == _settings.Sunset) { ModState.Sunset = val; try { ShaderManager.ToggleSunset(val); } catch { } }
                }
                if (slider.HasValue)
                {
                    slider.Value.Item1.Value = slider.Value.Item2;
                    if (slider.Value.Item1 == _settings.FishSize) ModState.FishSizeMulti = slider.Value.Item2;
                    else if (slider.Value.Item1 == _settings.Speed) ModState.SpeedMulti = slider.Value.Item2;
                    else if (slider.Value.Item1 == _settings.Damage) ModState.DamageMulti = slider.Value.Item2;
                }
            }
            catch (Exception e) { Log.Warn("[VladMod] Preset failed: " + e.Message); }
        }

        private void DrawKeybinds()
        {
            GUILayout.Space(6);
            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("KEYBINDS  —  click Rebind, press a key (Esc cancels)", _ui.CardDesc);
            KeybindRow("God mode", _settings.KeyGod);
            KeybindRow("Infinite money", _settings.KeyMoney);
            KeybindRow("Instant catch", _settings.KeyCatch);
            KeybindRow("Auto fish", _settings.KeyFish);
            KeybindRow("ESP toggle", _settings.KeyEsp);
            GUILayout.EndVertical();
        }

        private void KeybindRow(string label, CfgEntry<KeyCode> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.CardTitle, GUILayout.Width(130));
            GUILayout.Label(entry.Value == KeyCode.None ? "(none)" : entry.Value.ToString(), _ui.Hotkey, GUILayout.Width(90), GUILayout.Height(18));
            if (GUILayout.Button(_rebindEntry == entry ? "Press a key..." : "Rebind", _ui.StepBtn, GUILayout.Width(90), GUILayout.Height(22)))
            {
                _rebindEntry = _rebindEntry == entry ? null : entry;
                _rebindLabel = label;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // PLITCH-style global search: filters every toggle cheat.
        private void DrawSearchResults()
        {
            string q = (_search ?? string.Empty).Trim().ToLower();
            PlitchSection("Search", "\"" + _search + "\"");
            // content scroll is opened/closed centrally in DrawWindow

            int hits = 0;
            hits += SearchCheat(q, "God Mode", "Player — no damage / drowning", ModState.GodMode, v => SetGodMode(v), _settings.KeyGod.Value.ToString());
            hits += SearchCheat(q, "Infinite Fullness", "Player — no hunger", ModState.InfiniteFullness, v => { ModState.InfiniteFullness = v; _settings.InfFullness.Value = v; }, null);
            hits += SearchCheat(q, "Infinite Money", "Money — never drains", ModState.InfiniteMoney, v => SetInfMoney(v), _settings.KeyMoney.Value.ToString());
            hits += SearchCheat(q, "Auto-Sell Fish", "Money — auto sell (host)", ModState.AutoSell, v => { ModState.AutoSell = v; _settings.AutoSell.Value = v; }, null);
            hits += SearchCheat(q, "Instant Catch", "Fishing — instant bite", ModState.InstantCatch, v => SetInstantCatch(v), _settings.KeyCatch.Value.ToString());
            hits += SearchCheat(q, "Auto Fish", "Fishing — auto reel", ModState.AutoFish, v => SetAutoFish(v), _settings.KeyFish.Value.ToString());
            hits += SearchCheat(q, "Always Shiny", "Fishing — rare skin", ModState.ForceShiny, v => { ModState.ForceShiny = v; _settings.ForceShiny.Value = v; }, null);
            hits += SearchCheat(q, "Infinite Ammo", "Weapons — no reloads", ModState.InfiniteAmmo, v => { ModState.InfiniteAmmo = v; _settings.InfAmmo.Value = v; }, null);
            hits += SearchCheat(q, "No Cooldown", "Weapons — no cooldown", ModState.NoCooldown, v => { ModState.NoCooldown = v; _settings.NoCooldown.Value = v; }, null);
            hits += SearchCheat(q, "Rig Roulette", "Casino — always pays (host)", ModState.RigRoulette, v => { ModState.RigRoulette = v; _settings.RigRoulette.Value = v; }, null);
            hits += SearchCheat(q, "Rig Slots", "Casino — jackpot (host)", ModState.RigSlots, v => { ModState.RigSlots = v; _settings.RigSlots.Value = v; }, null);
            hits += SearchCheat(q, "One-Shot", "World — one-shot", ModState.OneShot, v => { ModState.OneShot = v; _settings.OneShot.Value = v; }, null);
            hits += SearchCheat(q, "Force Sunset", "World — sunset", ModState.Sunset, v => SetSunset(v), null);
            hits += SearchCheat(q, "Dev Cheats", "World — M/N money, O island", ModState.BuiltInCheats, v => SetCheats(v), null);
            hits += SearchCheat(q, "ESP Fish", "World — fish overlay", ModState.EspFish, v => { ModState.EspFish = v; _settings.EspFish.Value = v; }, _settings.KeyEsp.Value.ToString());
            hits += SearchCheat(q, "ESP Players", "World — player overlay", ModState.EspPlayers, v => { ModState.EspPlayers = v; _settings.EspPlayers.Value = v; }, _settings.KeyEsp.Value.ToString());
            hits += SearchCheat(q, "ESP Loot", "World — loot overlay", ModState.EspItems, v => { ModState.EspItems = v; _settings.EspItems.Value = v; }, _settings.KeyEsp.Value.ToString());
            hits += SearchCheat(q, "ESP Islands", "World — island positions", ModState.EspIslands, v => { ModState.EspIslands = v; _settings.EspIslands.Value = v; }, _settings.KeyEsp.Value.ToString());
            hits += SearchCheat(q, "Aimbot Players", "Weapons — auto-hit players (MP-safe RPC)", ModState.AimbotPlayers, v => { ModState.AimbotPlayers = v; _settings.AimPlayers.Value = v; }, null);
            hits += SearchCheat(q, "Aimbot Fish", "Weapons — auto-hit fish (MP-safe RPC)", ModState.AimbotFish, v => { ModState.AimbotFish = v; _settings.AimFish.Value = v; }, null);
            hits += SearchCheat(q, "Aimbot Bosses", "Weapons — auto-hit bosses (MP-safe RPC)", ModState.AimbotBosses, v => { ModState.AimbotBosses = v; _settings.AimBosses.Value = v; }, null);
            if (hits == 0)
                GUILayout.Label("No cheats match \"" + _search + "\".", _ui.LabelDim);
            // (scroll closed centrally)

        }

        private int SearchCheat(string q, string title, string desc, bool value, Action<bool> set, string hotkey)
        {
            if (!title.ToLower().Contains(q) && !desc.ToLower().Contains(q)) return 0;
            bool nv = CheatRow(title, desc, value, hotkey ?? "—");
            if (nv != value) set(nv);
            return 1;
        }

        private void DrawUnlockTab()
        {
            PlitchSection("Unlocks", "One-time / Presets");
            // content scroll is opened/closed centrally in DrawWindow


            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("ONE-TIME UNLOCKS", _ui.CardDesc);
            bool uhost = IsHost();
            if (!uhost && IsClient())
                GUILayout.Label("CLIENT MODE — boat/grill unlocks are host-only (server-side, no RPC). Pockets work in MP.", _ui.CardTitle);
            GUI.enabled = uhost;
            if (PlitchButton(uhost ? "Unlock boat + boat radar (host)" : "Unlock boat + radar (HOST ONLY)", false))
            {
                SafeCall(() => { if (BoatManager.Boat) { BoatManager.Boat.UnlockBoat(); BoatManager.Boat.UnlockBoatRadar(); } });
            }
            GUILayout.Space(6);
            if (PlitchButton(uhost ? "Unlock grill (host)" : "Unlock grill (HOST ONLY)", false))
                SafeCall(() => NPCManager.UnlockGrill());
            GUI.enabled = true;
            GUILayout.Space(6);
            if (PlitchButton("Unlock all inventory pockets (MP-safe RPC)", false))
                UnlockPockets();
            GUILayout.EndVertical();
            GUILayout.Space(6);
            DrawPresets();
            DrawKeybinds();
            // (scroll closed centrally)

        }

        private void DrawItemsTab()
        {
            PlitchSection("Items", "Browser / Skins");
            // content scroll is opened/closed centrally in DrawWindow


            int count = ModItems.Count;
            if (count <= 0)
            {
                GUILayout.Label("No items found.", _ui.LabelDim);
                // (scroll closed centrally)

                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, count - 1);

            GUILayout.BeginVertical(_ui.Card);
            GUILayout.Label("ITEM BROWSER", _ui.CardDesc);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                _itemIndex = (_itemIndex + count - 1) % count;
            GUILayout.Label(ModItems.Name(_itemIndex), _ui.CardTitle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", _ui.StepBtn, GUILayout.Width(30), GUILayout.Height(24)))
                _itemIndex = (_itemIndex + 1) % count;
            GUILayout.EndHorizontal();

            Item it = ModItems.Get(_itemIndex);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID", _ui.CardDesc);
            GUILayout.FlexibleSpace();
            GUILayout.Label(it ? it.ID.ToString() : "—", _ui.CardTitle);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (PlitchButton("+  Give this item  (free)", true))
                GiveItem(_itemIndex);
            GUILayout.EndVertical();
            GUILayout.Space(6);

            if (PlitchButton("Unlock all outfits / skins", false))
                UnlockAllSkins();
            // (scroll closed centrally)

        }

        // ==================================================================
        // PLITCH UI helpers — cheat cards, sections, sliders, buttons
        // ==================================================================

        private void Header(string text)
        {
            GUILayout.Label(text, _ui.SectionHeader);
            GUILayout.Box(string.Empty, _ui.Sep, GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(4);
        }

        private void SubHeader(string text)
        {
            GUILayout.Space(4);
            GUILayout.Label(text.ToUpper(), _ui.LabelDim);
        }

        private void PlitchSection(string title, string sub)
        {
            GUILayout.Space(4);
            GUILayout.Label(title.ToUpper(), _ui.SectionHeader);
            GUILayout.Label(sub, _ui.CardDesc);
            GUILayout.Box(string.Empty, _ui.Sep, GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(4);
        }

        // PLITCH cheat row: card with name + desc left, hotkey pill + switch right.
        // Returns the new toggle value.
        private bool CheatRow(string title, string desc, bool value, string hotkey)
        {
            GUILayout.BeginVertical(_ui.Card, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label(title, _ui.CardTitle);
            GUILayout.Label(desc, _ui.CardDesc);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(110));
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(hotkey))
                GUILayout.Label(hotkey, _ui.Hotkey, GUILayout.Width(110), GUILayout.Height(18));
            bool nv = PlitchSwitch(value);
            GUILayout.EndVertical();
            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
            return nv;
        }

        private bool PlitchSwitch(bool value)
        {
            string label = value ? "ON" : "OFF";
            GUIStyle st = value ? _ui.SwitchOn : _ui.SwitchOff;
            if (GUILayout.Button(label, st, GUILayout.Width(110), GUILayout.Height(24)))
                return !value;
            return value;
        }

        private bool PlitchButton(string text, bool accent)
        {
            return GUILayout.Button(text, accent ? _ui.BtnAccent : _ui.Btn, GUILayout.ExpandWidth(true), GUILayout.Height(30));
        }

        private void PlitchSlider(CfgEntry<float> entry, ref float field, float min, float max, string label, string suffix)
        {
            GUILayout.BeginVertical(_ui.Card, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.CardTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("-", _ui.StepBtn, GUILayout.Width(26), GUILayout.Height(22)))
                field = Mathf.Clamp((float)Math.Round(field - 0.1f, 2), min, max);
            GUILayout.Label(field.ToString("0.00") + suffix, _ui.Hotkey, GUILayout.Width(70), GUILayout.Height(18));
            if (GUILayout.Button("+", _ui.StepBtn, GUILayout.Width(26), GUILayout.Height(22)))
                field = Mathf.Clamp((float)Math.Round(field + 0.1f, 2), min, max);
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            // Plain slider style: custom ScrollView-cloned styles risk null
            // textures on some Unity versions and render as black boxes.
            float v = GUILayout.HorizontalSlider(field, min, max);
            float rounded = (float)Math.Round(v, 2);
            if (!Mathf.Approximately(rounded, field))
            {
                field = rounded;
                if (entry != null) entry.Value = rounded;
            }
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void BeginContentScroll()
        {
            _contentScroll = GUILayout.BeginScrollView(_contentScroll, GUILayout.ExpandHeight(true));
        }

        private void EndContentScroll()
        {
            GUILayout.EndScrollView();
        }

        private void SliderCfg(CfgEntry<float> entry, ref float field, float min, float max, string label, string suffix)
        {
            PlitchSlider(entry, ref field, min, max, label, suffix);
        }

        private void Bar(string label, int value, int max, Color fillColor)
        {
            float percent = Mathf.Clamp01(max > 0 ? (float)value / max : 0f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.Label, GUILayout.Width(72));
            GUILayout.Box(string.Empty, _ui.BarTrack, GUILayout.ExpandWidth(true), GUILayout.Height(12));
            Rect track = GUILayoutUtility.GetLastRect();
            if (track.width > 1f)
            {
                float fillW = Mathf.Max(2f, (track.width - 2f) * percent);
                var fillRect = new Rect(track.x + 1f, track.y + 1f, fillW, track.height - 2f);
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = fillColor;
                GUI.Box(fillRect, string.Empty, _ui.BarFill);
                GUI.backgroundColor = prev;
            }
            GUILayout.Label(value + "/" + max, _ui.Value, GUILayout.Width(52));
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawFooter()
        {
            GUILayout.Box(string.Empty, _ui.Sep, GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Made by Vlad  •  @vladuism  •  F1 = menu", _ui.Footer);
            GUILayout.FlexibleSpace();
            string role = IsHost() ? "HOST" : (IsClient() ? "CLIENT" : "IDLE");
            GUIStyle pill = IsHost() ? _ui.PillHost : (IsClient() ? _ui.PillClient : _ui.PillIdle);
            GUILayout.Label(role, pill, GUILayout.Width(58), GUILayout.Height(18));
            GUILayout.EndHorizontal();
        }

        // ==================================================================
        // ESP overlay
        // ==================================================================
        private void DrawEsp()
        {
            if (_espOutline == null)
            {
                _espOutline = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                for (int y = 0; y < 4; y++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        bool border = x == 0 || x == 3 || y == 0 || y == 3;
                        _espOutline.SetPixel(x, y, border ? Color.white : Color.clear);
                    }
                }
                _espOutline.Apply();
                _espBoxStyle = new GUIStyle { border = new RectOffset(1, 1, 1, 1) };
                _espBoxStyle.normal.background = _espOutline;
                _espLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
                _espLabelStyle.normal.textColor = Color.white;
            }

            Player local = Player.LocalPlayer;
            Camera cam = GameInfo.CurCamera != null ? GameInfo.CurCamera : Camera.main;
            if (!local || !cam) return;

            Vector3 camPos = cam.transform.position;
            int width = Screen.width;
            int height = Screen.height;

            // fish / creatures
            if (ModState.EspFish && CreatureManager.Instance != null && _aliveCreaturesField != null)
            {
                var list = _aliveCreaturesField.GetValue(CreatureManager.Instance) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (object o in list)
                    {
                        var c = o as Creature;
                        if (!c || c == null) continue;
                        DrawEspBox(cam, c.transform, camPos, width, height, new Color(0.3f, 0.8f, 1f), 0.9f, "");
                    }
                }
            }

            // players (skip self)
            if (ModState.EspPlayers && PlayerManager.AlivePlayers != null)
            {
                foreach (Player p in PlayerManager.AlivePlayers)
                {
                    if (!p || p == local) continue;
                    DrawEspBox(cam, p.Transform, camPos, width, height, new Color(1f, 0.85f, 0.2f), 1.8f,
                        "P" + (PlayerManager.AlivePlayers.IndexOf(p) + 1));
                }
            }

            // ground loot
            if (ModState.EspItems && ItemManager.Items != null)
            {
                foreach (var kv in ItemManager.Items)
                {
                    Item it = kv.Value;
                    if (!it || it == null) continue;
                    Color col = it is Weapon ? new Color(1f, 0.4f, 0.3f) : new Color(0.6f, 1f, 0.4f);
                    DrawEspBox(cam, it.transform, camPos, width, height, col, 0.7f, "");
                }
            }

            // islands (verified: IslandManager.GetIslandInfo(i).IslandPosition,
            // IslandManager.TotalIslands, Island.IslandPos for current).
            if (ModState.EspIslands)
            {
                try
                {
                    int total = IslandManager.TotalIslands;
                    byte cur = 0;
                    try { cur = OnlineIslandManager.CurIsland; } catch { }
                    for (int i = 0; i < total; i++)
                    {
                        Vector3 ipos;
                        try { ipos = IslandManager.GetIslandInfo(i).IslandPosition; }
                        catch { continue; }
                        if (ipos == Vector3.zero) continue;
                        float dist = Vector3.Distance(camPos, ipos);
                        string label = "Island " + (i + 1) + " " + Mathf.RoundToInt(dist) + "m";
                        if (i + 1 == cur) label = "(*) " + label;
                        DrawEspMarker(cam, ipos, camPos, width, height, new Color(0.65f, 0.45f, 1f), label);
                    }
                }
                catch { }
            }

            // aimbot crosshair marker
            if (ModState.AimbotPlayers || ModState.AimbotFish || ModState.AimbotBosses)
            {
                try
                {
                    Transform at = FindAimTransform(cam);
                    if (at != null)
                        DrawEspBox(cam, at, camPos, width, height, Color.red, 1.8f, "LOCK");
                }
                catch { }
            }
        }

        private void DrawEspMarker(Camera cam, Vector3 pos, Vector3 camPos, int width, int height, Color color, string label)
        {
            Vector3 s = cam.WorldToScreenPoint(pos + Vector3.up * 3f);
            if (s.z <= 0f) return;
            float x = s.x;
            float y = height - s.y;
            if (x < 0f || x > width || y < 0f || y > height) return;
            Color prev = GUI.color;
            GUI.color = color;
            GUI.Box(new Rect(x - 3f, y - 3f, 6f, 6f), string.Empty, _espBoxStyle);
            GUI.color = prev;
            GUI.Label(new Rect(x + 8f, y - 8f, 200f, 16f), label, _espLabelStyle);
        }

        private void DrawEspBox(Camera cam, Transform t, Vector3 camPos, int width, int height, Color color, float boxHeight, string label)
        {
            if (!t) return;
            Vector3 pos = t.position;
            if ((pos - camPos).sqrMagnitude > EspMaxDistance * EspMaxDistance) return;

            Vector3 head = pos + Vector3.up * boxHeight;
            Vector3 feet = pos;
            Vector3 h = cam.WorldToScreenPoint(head);
            Vector3 f = cam.WorldToScreenPoint(feet);
            if (h.z <= 0f || f.z <= 0f) return;

            float top = height - h.y;
            float bottom = height - f.y;
            float boxH = Mathf.Max(8f, bottom - top);
            float boxW = Mathf.Max(6f, boxH * 0.55f);
            float cx = (h.x + f.x) * 0.5f;
            var rect = new Rect(cx - boxW * 0.5f, top, boxW, boxH);

            Color prev = GUI.color;
            GUI.color = color;
            GUI.Box(rect, string.Empty, _espBoxStyle);
            GUI.color = prev;

            if (label.Length > 0)
                GUI.Label(new Rect(rect.x, rect.y - 14, 80, 14), label, _espLabelStyle);
        }

        // ==================================================================
        // Feature actions
        // ==================================================================
        private static void GiveMaxMoney()
        {
            SafeCall(() =>
            {
                if (MoneyManager.Instance && MoneyManager.Instance.IsServerInitialized)
                {
                    int cur = MoneyManager.Instance._money.Value;
                    MoneyManager.Instance._money.Value = Math.Min(cur + 99999, int.MaxValue - 1);
                }
            });
        }

        private static void GiveAllBaits()
        {
            Player p = Player.LocalPlayer;
            if (!p || !Server.Instance) return;
            SafeCall(() =>
            {
                for (int i = 0; i < GameInfo.AllBaits.Count; i++)
                    Server.Instance.BuyBait(p, (byte)i, 0);
            });
        }

        private static void MaxVitals()
        {
            Player p = Player.LocalPlayer;
            if (!p || !p.Vitals) return;
            SafeCall(() =>
            {
                p.Vitals.ServerResetVitals();
                p.Vitals.Heal(100);
                p.Vitals.RestoreFullness(100);
            });
        }

        private static void DuplicateHeldItem()
        {
            Player p = Player.LocalPlayer;
            if (!p || !IsHost() || !ItemManager.Instance) return;
            SafeCall(() =>
            {
                Item held = p.Holding != null ? p.Holding.HeldItem : null;
                if (!held) return;
                Vector3 pos = p.Transform.position + p.Transform.forward * 1.5f + Vector3.up * 1f;
                ItemManager.Instance.SpawnNewItem(held, pos, Quaternion.identity);
            });
        }

        private static void SpawnCreature(int index)
        {
            Player p = Player.LocalPlayer;
            if (!p || !IsHost() || !ItemManager.Instance) return;
            SafeCall(() =>
            {
                Creature c = GameInfo.GetCreature(index);
                if (!c) return;
                Vector3 pos = p.Transform.position + p.Transform.forward * 2.5f + Vector3.up * 1f;
                ItemManager.Instance.SpawnNewItem(c, pos, Quaternion.identity);
            });
        }

        private static void GiveItem(int index)
        {
            Player p = Player.LocalPlayer;
            if (!p || !Server.Instance) return;
            Item it = ModItems.Get(index);
            if (!it) return;
            SafeCall(() =>
            {
                Item held = p.Holding != null ? p.Holding.HeldItem : null;
                Vector3 pos = p.Transform.position + p.Transform.forward * 1.5f + Vector3.up * 1f;
                Server.Instance.BuyItem(it.ID, p, held, pos, Quaternion.identity, true);
            });
        }

        private static void SpawnExplosion()
        {
            Player p = Player.LocalPlayer;
            if (!p || !IsHost() || !ItemManager.Instance) return;
            SafeCall(() =>
            {
                Item prefab = ModItems.ExplosivePrefab;
                if (!prefab) { Log.Warn("[VladMod] No explosive item found"); return; }
                Vector3 pos = p.Transform.position + p.Transform.forward * 3f + Vector3.up * 0.5f;
                Item spawned = ItemManager.Instance.SpawnNewItem(prefab, pos, Quaternion.identity);
                Explosive ex = spawned != null ? spawned.GetComponent<Explosive>() : null;
                if (ex) ex.ForceExplode(p, true);
            });
        }

        private static void SpawnBoss()
        {
            Player p = Player.LocalPlayer;
            if (!p || !IsHost() || !ItemManager.Instance) return;
            SafeCall(() =>
            {
                Creature boss = ModItems.BossCreature;
                if (!boss) { Log.Warn("[VladMod] No boss creature found"); return; }
                Vector3 pos = p.Transform.position + p.Transform.forward * 5f + Vector3.up * 1f;
                Item spawned = ItemManager.Instance.SpawnNewItem(boss, pos, Quaternion.identity);
                if (spawned is Creature c) BossManager.InitializeBossFight(c);
            });
        }

        private static void KillAllCreatures()
        {
            if (!IsHost() || CreatureManager.Instance == null) return;
            SafeCall(() =>
            {
                var list = (System.Collections.IEnumerable)AccessTools
                    .Field(typeof(CreatureManager), "_aliveCreatures")
                    .GetValue(CreatureManager.Instance);
                if (list != null)
                {
                    foreach (Creature c in list)
                        if (c) { try { c.DestroyItem(0, byte.MaxValue); } catch { } }
                }
                try { GameInfo.ToggleAllCreaturesKilled(true, false); } catch { }
            });
        }

        private static void TeleportMeTo(Player target)
        {
            if (Player.LocalPlayer == null || target == null || Server.Instance == null) return;
            SafeCall(() => Server.Instance.TeleportPlayer(Player.LocalPlayer, target.Transform.position, 0f));
        }

        private static void PullPlayerToMe(Player target)
        {
            if (Player.LocalPlayer == null || target == null || Server.Instance == null) return;
            SafeCall(() => Server.Instance.TeleportPlayer(target, Player.LocalPlayer.Transform.position, 0f));
        }

        private static void ReviveAll()
        {
            if (Server.Instance == null) return;
            SafeCall(() =>
            {
                foreach (Player pl in PlayerManager.Players)
                {
                    if (!pl || pl.Vitals == null || pl.Vitals.Health > 0) continue;
                    if (pl.Dying != null && pl.Dying.DeadPlayer)
                        Server.Instance.ResurrectPlayer(pl, pl.Dying.DeadPlayer);
                }
            });
        }

        private static void DamagePlayer(Player target, int damage)
        {
            if (target == null || Server.Instance == null) return;
            // Attacker = null bypasses the friendly-fire check; works from host or joiner.
            SafeCall(() => Server.Instance.HitPlayer(target, damage, Vector3.zero, Vector3.zero, (byte)DamageType.Generic, null));
        }

        private static void SkipIntroTutorial()
        {
            Player p = Player.LocalPlayer;
            if (!p || !IsHost()) return;
            SafeCall(() =>
            {
                var skipTutorial = AccessTools.Method(typeof(Player), "SkipTutorial");
                var skipIntro = AccessTools.Method(typeof(Player), "SkipIntro");
                skipTutorial?.Invoke(p, new object[] { p.Owner });
                skipIntro?.Invoke(p, new object[] { p.Owner });
                var setFinished = AccessTools.Method(typeof(Player), "SetFinishedTutorial");
                setFinished?.Invoke(p, null);
            });
        }

        private static void UnlockPockets()
        {
            Player p = Player.LocalPlayer;
            if (!p || Server.Instance == null) return;
            SafeCall(() =>
            {
                // BUG FIX: PlayerInventory.UnlockExtraPocket has no RPC attribute
                // (local-only), while Server.UnlockPocket IS a ServerRPC, so it
                // works as host AND client. Use the server RPC.
                for (byte i = 0; i < 6; i++)
                {
                    try { Server.Instance.UnlockPocket(p, i); } catch { break; }
                }
            });
        }

        private static Vector3 IslandPosition(int islandIndex)
        {
            try
            {
                IslandInfo info = IslandManager.GetIslandInfo(islandIndex);
                if (info != null) return info.IslandPosition;
            }
            catch { }
            try { return Island.IslandPos; } catch { }
            return Vector3.zero;
        }

        // MP-safe: self-teleport via Server.TeleportPlayer (verified ServerRPC,
        // RpcLogic has no ownership check). Island SWAP stays host-only.
        private static void TeleportMeToIsland(byte island)
        {
            Player p = Player.LocalPlayer;
            if (!p || Server.Instance == null) return;
            Vector3 pos = IslandPosition(island);
            if (pos == Vector3.zero) { Log.Warn("[VladMod] Unknown island position"); return; }
            pos += Vector3.up * 2f;
            SafeCall(() => Server.Instance.TeleportPlayer(p, pos, 0f));
        }

        private static void TeleportMeToCurrentIsland()
        {
            Player p = Player.LocalPlayer;
            if (!p || Server.Instance == null) return;
            Vector3 pos = Vector3.zero;
            try { pos = Island.IslandPos; } catch { }
            if (pos == Vector3.zero) { Log.Warn("[VladMod] Current island position unknown"); return; }
            pos += Vector3.up * 2f;
            SafeCall(() => Server.Instance.TeleportPlayer(p, pos, 0f));
        }

        private static void UnlockAllSkins()
        {
            SafeCall(() =>
            {
                foreach (string name in new[]
                {
                    "UnlockLighthouseKeeper", "UnlockSwampMan", "UnlockSwampLady", "UnlockKioskLady",
                    "UnlockTourist", "UnlockGrillmaster", "UnlockAndrei", "UnlockJacob",
                    "UnlockGunStoreClerc", "UnlockScaredGuyInShorts", "UnlockStoreGradma",
                    "UnlockMilitary", "UnlockScientist", "UnlockBean"
                })
                {
                    try
                    {
                        var m = AccessTools.Method(typeof(SkinManager), name);
                        if (m != null) m.Invoke(null, null);
                    }
                    catch { }
                }
            });
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        internal static bool IsHost()
        {
            var instances = NetworkManager.Instances;
            if (instances == null || instances.Count == 0) return false;
            return instances[0].IsServerStarted;
        }

        internal static bool IsClient()
        {
            var instances = NetworkManager.Instances;
            if (instances == null || instances.Count == 0) return false;
            return instances[0].IsClientStarted;
        }

        private static int ClampIdx(int i, int max)
        {
            if (max <= 0) return 0;
            return ((i % max) + max) % max;
        }

        private static string CreatureName(int index)
        {
            try
            {
                Creature c = GameInfo.GetCreature(index);
                return c ? c.GetName() : "(none)";
            }
            catch { return "(none)"; }
        }

        private static int VitalsMax(FieldInfo field, int fallback)
        {
            try
            {
                if (field != null)
                {
                    object v = field.GetValue(null);
                    if (v is int i && i > 0) return i;
                }
            }
            catch { }
            return fallback;
        }

        private static void SafeCall(Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Warn("[VladMod] " + e.Message); }
        }

        // ==================================================================
        // Cached PLITCH theme — dark + purple, created ONCE and reused.
        // ==================================================================
        private sealed class Ui
        {
            public GUIStyle Window, Title, CloseBtn;
            public GUIStyle TabActive, TabInactive;
            public GUIStyle Label, LabelDim, Value, SectionHeader, Footer;
            public GUIStyle Btn, BtnAccent, Toggle, Sep;
            public GUIStyle BarTrack, BarFill;
            public GUIStyle PillHost, PillClient, PillIdle, Hint;
            // PLITCH additions
            public GUIStyle Banner, BannerTitle, BannerSub;
            public GUIStyle Sidebar, SideActive, SideInactive, SideSub, SideCounter;
            public GUIStyle Content, Card, CardTitle, CardDesc;
            public GUIStyle Hotkey, SwitchOn, SwitchOff, StepBtn;
            public GUIStyle Search, SearchIcon, StatusBox, StatusHost, StatusClient, StatusSub;
            public GUIStyle Slider, Thumb, ScrollView;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static GUIStyle Clone(GUIStyle src)
        {
            return new GUIStyle(src);
        }

        private static void EnsureUi()
        {
            if (_ui != null) return;

            // PLITCH palette: near-black bg, dark sidebar, purple accent
            Color bg = new Color(0.059f, 0.063f, 0.078f, 0.98f);
            Color sidebar = new Color(0.078f, 0.082f, 0.106f, 1f);
            Color card = new Color(0.106f, 0.114f, 0.149f, 1f);
            Color cardHover = new Color(0.137f, 0.149f, 0.192f, 1f);
            Color panel = new Color(0.106f, 0.114f, 0.149f, 1f);
            Color panelHover = new Color(0.16f, 0.19f, 0.23f, 1f);
            Color panelActive = new Color(0.21f, 0.25f, 0.30f, 1f);
            Color accent = new Color(0.482f, 0.180f, 0.933f, 1f);       // PLITCH purple #7B2EFF
            Color accentBright = new Color(0.580f, 0.278f, 1f, 1f);
            Color accentDim = new Color(0.28f, 0.14f, 0.55f, 1f);
            Color cyan = new Color(0.0f, 0.898f, 0.80f, 1f);
            Color line = new Color(0.16f, 0.17f, 0.22f, 1f);
            Color text = new Color(0.93f, 0.94f, 0.97f, 1f);
            Color dim = new Color(0.58f, 0.62f, 0.70f, 1f);

            var skin = GUI.skin;
            var ui = new Ui();

            ui.Window = Clone(skin.window);
            ui.Window.normal.background = Solid(bg);
            ui.Window.border = new RectOffset(10, 10, 10, 10);
            ui.Window.padding = new RectOffset(0, 0, 0, 0);

            ui.Title = Clone(skin.label);
            ui.Title.fontStyle = FontStyle.Bold;
            ui.Title.fontSize = 15;
            ui.Title.normal.textColor = Color.white;

            ui.CloseBtn = Clone(skin.button);
            ui.CloseBtn.fontStyle = FontStyle.Bold;
            ui.CloseBtn.fontSize = 14;
            ui.CloseBtn.normal.background = Solid(sidebar);
            ui.CloseBtn.normal.textColor = dim;
            ui.CloseBtn.hover.background = Solid(new Color(0.7f, 0.2f, 0.25f, 1f));
            ui.CloseBtn.hover.textColor = Color.white;
            ui.CloseBtn.active.background = Solid(new Color(0.6f, 0.2f, 0.2f, 1f));
            ui.CloseBtn.active.textColor = Color.white;

            ui.TabActive = Clone(skin.button);
            ui.TabActive.fontStyle = FontStyle.Bold;
            ui.TabActive.fontSize = 12;
            ui.TabActive.normal.background = Solid(accent);
            ui.TabActive.normal.textColor = Color.white;
            ui.TabActive.hover.background = Solid(accentBright);
            ui.TabActive.hover.textColor = Color.white;
            ui.TabActive.active.background = Solid(accentBright);
            ui.TabActive.active.textColor = Color.white;

            ui.TabInactive = Clone(skin.button);
            ui.TabInactive.fontSize = 12;
            ui.TabInactive.normal.background = Solid(panel);
            ui.TabInactive.normal.textColor = dim;
            ui.TabInactive.hover.background = Solid(panelHover);
            ui.TabInactive.hover.textColor = text;
            ui.TabInactive.active.background = Solid(panelActive);
            ui.TabInactive.active.textColor = text;

            ui.Label = Clone(skin.label);
            ui.Label.fontSize = 13;
            ui.Label.normal.textColor = text;

            ui.LabelDim = Clone(skin.label);
            ui.LabelDim.fontSize = 12;
            ui.LabelDim.normal.textColor = dim;

            ui.Value = Clone(skin.label);
            ui.Value.fontStyle = FontStyle.Bold;
            ui.Value.fontSize = 13;
            ui.Value.normal.textColor = Color.white;

            ui.SectionHeader = Clone(skin.label);
            ui.SectionHeader.fontStyle = FontStyle.Bold;
            ui.SectionHeader.fontSize = 13;
            ui.SectionHeader.normal.textColor = Color.white;

            ui.Footer = Clone(skin.label);
            ui.Footer.fontSize = 11;
            ui.Footer.normal.textColor = dim;

            ui.Sep = Clone(skin.box);
            ui.Sep.normal.background = Solid(line);
            ui.Sep.border = new RectOffset(0, 0, 0, 0);
            ui.Sep.margin = new RectOffset(0, 0, 0, 0);
            ui.Sep.padding = new RectOffset(0, 0, 0, 0);

            ui.Btn = Clone(skin.button);
            ui.Btn.fontSize = 13;
            ui.Btn.normal.background = Solid(panel);
            ui.Btn.normal.textColor = text;
            ui.Btn.hover.background = Solid(panelHover);
            ui.Btn.hover.textColor = Color.white;
            ui.Btn.active.background = Solid(panelActive);
            ui.Btn.active.textColor = Color.white;
            ui.Btn.border = new RectOffset(4, 4, 4, 4);

            ui.BtnAccent = Clone(ui.Btn);
            ui.BtnAccent.fontStyle = FontStyle.Bold;
            ui.BtnAccent.normal.background = Solid(accent);
            ui.BtnAccent.normal.textColor = Color.white;
            ui.BtnAccent.hover.background = Solid(accentBright);
            ui.BtnAccent.hover.textColor = Color.white;
            ui.BtnAccent.active.background = Solid(accentDim);
            ui.BtnAccent.active.textColor = Color.white;

            ui.Toggle = Clone(skin.toggle);
            ui.Toggle.fontSize = 13;
            ui.Toggle.normal.textColor = text;
            ui.Toggle.hover.textColor = text;

            ui.BarTrack = Clone(skin.box);
            ui.BarTrack.normal.background = Solid(new Color(0.05f, 0.06f, 0.08f, 1f));
            ui.BarTrack.border = new RectOffset(2, 2, 2, 2);
            ui.BarTrack.margin = new RectOffset(0, 0, 0, 0);
            ui.BarTrack.padding = new RectOffset(0, 0, 0, 0);

            ui.BarFill = Clone(skin.box);
            ui.BarFill.normal.background = Solid(Color.white);
            ui.BarFill.border = new RectOffset(2, 2, 2, 2);
            ui.BarFill.margin = new RectOffset(0, 0, 0, 0);
            ui.BarFill.padding = new RectOffset(0, 0, 0, 0);

            ui.PillHost = Clone(skin.box);
            ui.PillHost.fontStyle = FontStyle.Bold;
            ui.PillHost.fontSize = 10;
            ui.PillHost.alignment = TextAnchor.MiddleCenter;
            ui.PillHost.normal.textColor = Color.white;
            ui.PillHost.normal.background = Solid(new Color(0.12f, 0.55f, 0.30f, 1f));

            ui.PillClient = Clone(ui.PillHost);
            ui.PillClient.normal.background = Solid(new Color(0.75f, 0.45f, 0.10f, 1f));

            ui.PillIdle = Clone(ui.PillHost);
            ui.PillIdle.normal.background = Solid(new Color(0.30f, 0.34f, 0.38f, 1f));

            ui.Hint = Clone(skin.label);
            ui.Hint.fontSize = 12;
            ui.Hint.normal.textColor = new Color(1f, 1f, 1f, 0.45f);

            // ---- PLITCH chrome ----
            ui.Banner = Clone(skin.box);
            ui.Banner.normal.background = Solid(new Color(0.10f, 0.07f, 0.20f, 1f));
            ui.Banner.border = new RectOffset(0, 0, 0, 0);
            ui.Banner.margin = new RectOffset(0, 0, 0, 0);
            ui.Banner.padding = new RectOffset(4, 4, 4, 4);

            ui.BannerTitle = Clone(skin.label);
            ui.BannerTitle.fontStyle = FontStyle.Bold;
            ui.BannerTitle.fontSize = 16;
            ui.BannerTitle.normal.textColor = Color.white;

            ui.BannerSub = Clone(skin.label);
            ui.BannerSub.fontSize = 11;
            ui.BannerSub.normal.textColor = new Color(0.75f, 0.68f, 1f, 1f);

            ui.Sidebar = Clone(skin.box);
            ui.Sidebar.normal.background = Solid(sidebar);
            ui.Sidebar.border = new RectOffset(0, 0, 0, 0);
            ui.Sidebar.margin = new RectOffset(0, 0, 0, 0);
            ui.Sidebar.padding = new RectOffset(6, 6, 4, 4);

            ui.SideActive = Clone(skin.button);
            ui.SideActive.fontStyle = FontStyle.Bold;
            ui.SideActive.fontSize = 13;
            ui.SideActive.alignment = TextAnchor.MiddleLeft;
            ui.SideActive.normal.background = Solid(accent);
            ui.SideActive.normal.textColor = Color.white;
            ui.SideActive.hover.background = Solid(accentBright);
            ui.SideActive.hover.textColor = Color.white;
            ui.SideActive.active.background = Solid(accentBright);
            ui.SideActive.active.textColor = Color.white;

            ui.SideInactive = Clone(skin.button);
            ui.SideInactive.fontSize = 13;
            ui.SideInactive.alignment = TextAnchor.MiddleLeft;
            ui.SideInactive.normal.background = Solid(new Color(0f, 0f, 0f, 0f));
            ui.SideInactive.normal.textColor = dim;
            ui.SideInactive.hover.background = Solid(cardHover);
            ui.SideInactive.hover.textColor = text;
            ui.SideInactive.active.background = Solid(card);
            ui.SideInactive.active.textColor = text;

            ui.SideSub = Clone(skin.label);
            ui.SideSub.fontSize = 10;
            ui.SideSub.normal.textColor = new Color(0.45f, 0.48f, 0.56f, 1f);

            ui.SideCounter = Clone(skin.label);
            ui.SideCounter.fontSize = 10;
            ui.SideCounter.fontStyle = FontStyle.Bold;
            ui.SideCounter.normal.textColor = cyan;

            ui.Content = Clone(skin.box);
            ui.Content.normal.background = Solid(bg);
            ui.Content.border = new RectOffset(0, 0, 0, 0);
            ui.Content.margin = new RectOffset(0, 0, 0, 0);
            ui.Content.padding = new RectOffset(10, 10, 6, 6);

            ui.Card = Clone(skin.box);
            ui.Card.normal.background = Solid(card);
            ui.Card.border = new RectOffset(6, 6, 6, 6);
            ui.Card.margin = new RectOffset(0, 0, 2, 2);
            ui.Card.padding = new RectOffset(10, 10, 8, 8);

            ui.CardTitle = Clone(skin.label);
            ui.CardTitle.fontStyle = FontStyle.Bold;
            ui.CardTitle.fontSize = 13;
            ui.CardTitle.normal.textColor = text;

            ui.CardDesc = Clone(skin.label);
            ui.CardDesc.fontSize = 11;
            ui.CardDesc.normal.textColor = dim;

            ui.Hotkey = Clone(skin.box);
            ui.Hotkey.fontSize = 10;
            ui.Hotkey.fontStyle = FontStyle.Bold;
            ui.Hotkey.alignment = TextAnchor.MiddleCenter;
            ui.Hotkey.normal.textColor = new Color(0.85f, 0.78f, 1f, 1f);
            ui.Hotkey.normal.background = Solid(new Color(0.20f, 0.16f, 0.34f, 1f));
            ui.Hotkey.border = new RectOffset(4, 4, 4, 4);
            ui.Hotkey.margin = new RectOffset(0, 0, 2, 2);
            ui.Hotkey.padding = new RectOffset(2, 2, 2, 2);

            ui.SwitchOn = Clone(skin.button);
            ui.SwitchOn.fontStyle = FontStyle.Bold;
            ui.SwitchOn.fontSize = 11;
            ui.SwitchOn.normal.background = Solid(accent);
            ui.SwitchOn.normal.textColor = Color.white;
            ui.SwitchOn.hover.background = Solid(accentBright);
            ui.SwitchOn.hover.textColor = Color.white;
            ui.SwitchOn.active.background = Solid(accentBright);
            ui.SwitchOn.active.textColor = Color.white;

            ui.SwitchOff = Clone(skin.button);
            ui.SwitchOff.fontSize = 11;
            ui.SwitchOff.normal.background = Solid(new Color(0.20f, 0.22f, 0.28f, 1f));
            ui.SwitchOff.normal.textColor = dim;
            ui.SwitchOff.hover.background = Solid(cardHover);
            ui.SwitchOff.hover.textColor = text;
            ui.SwitchOff.active.background = Solid(cardHover);
            ui.SwitchOff.active.textColor = text;

            ui.StepBtn = Clone(skin.button);
            ui.StepBtn.fontStyle = FontStyle.Bold;
            ui.StepBtn.fontSize = 13;
            ui.StepBtn.normal.background = Solid(new Color(0.16f, 0.17f, 0.24f, 1f));
            ui.StepBtn.normal.textColor = text;
            ui.StepBtn.hover.background = Solid(accentDim);
            ui.StepBtn.hover.textColor = Color.white;
            ui.StepBtn.active.background = Solid(accent);
            ui.StepBtn.active.textColor = Color.white;

            ui.Search = Clone(skin.textField);
            ui.Search.fontSize = 12;
            ui.Search.normal.background = Solid(new Color(0.13f, 0.14f, 0.20f, 1f));
            ui.Search.normal.textColor = text;
            ui.Search.padding = new RectOffset(8, 8, 4, 4);

            ui.SearchIcon = Clone(skin.label);
            ui.SearchIcon.fontSize = 14;
            ui.SearchIcon.normal.textColor = dim;

            ui.StatusBox = Clone(skin.box);
            ui.StatusBox.normal.background = Solid(card);
            ui.StatusBox.border = new RectOffset(4, 4, 4, 4);
            ui.StatusBox.padding = new RectOffset(4, 4, 6, 6);

            ui.StatusHost = Clone(skin.label);
            ui.StatusHost.fontStyle = FontStyle.Bold;
            ui.StatusHost.fontSize = 12;
            ui.StatusHost.normal.textColor = new Color(0.3f, 0.95f, 0.55f, 1f);

            ui.StatusClient = Clone(skin.label);
            ui.StatusClient.fontStyle = FontStyle.Bold;
            ui.StatusClient.fontSize = 12;
            ui.StatusClient.normal.textColor = new Color(1f, 0.75f, 0.3f, 1f);

            ui.StatusSub = Clone(skin.label);
            ui.StatusSub.fontSize = 10;
            ui.StatusSub.normal.textColor = dim;

            ui.Slider = Clone(skin.horizontalSlider);
            ui.Thumb = Clone(skin.horizontalSliderThumb);

            ui.ScrollView = Clone(skin.box);
            ui.ScrollView.normal.background = Solid(new Color(0f, 0f, 0f, 0f));
            ui.ScrollView.border = new RectOffset(0, 0, 0, 0);
            ui.ScrollView.margin = new RectOffset(0, 0, 0, 0);
            ui.ScrollView.padding = new RectOffset(0, 0, 0, 0);

            _ui = ui;
        }
    }

    /// <summary>All persisted config entries (auto-saved to vladmod.cfg on change).</summary>
    internal sealed class Settings
    {
        public readonly CfgEntry<bool> GodMode, InfFullness, InfMoney, InstantCatch, AutoFish, ForceShiny,
            Sunset, BuiltInCheats, InfAmmo, RigRoulette, RigSlots, AutoSell, OneShot, NoBaitLoss, NoCooldown,
            EspFish, EspPlayers, EspItems, EspIslands, AimPlayers, AimFish, AimBosses, AimSnap;
        public readonly CfgEntry<float> Speed, FishSize, Jump, Damage, Water, TickSpeed, AimRange, AimFov;
        public readonly CfgEntry<KeyCode> KeyGod, KeyMoney, KeyCatch, KeyFish, KeyEsp;

        public Settings()
        {
            GodMode = Cfg.Bind("Toggles", "GodMode", false, "Immune to all damage (drowning, poison, fire, hunger).");
            InfFullness = Cfg.Bind("Toggles", "InfiniteFullness", false, "Hunger never drains.");
            InfMoney = Cfg.Bind("Toggles", "InfiniteMoney", false, "Money never drains + free buys (MP: free-buy RPCs).");
            InstantCatch = Cfg.Bind("Toggles", "InstantCatch", false, "Fish bites instantly.");
            AutoFish = Cfg.Bind("Toggles", "AutoFish", false, "Instant bite + auto reel.");
            ForceShiny = Cfg.Bind("Toggles", "ForceShiny", false, "Every catch is rare/shiny.");
            Sunset = Cfg.Bind("Toggles", "Sunset", false, "Force sunset atmosphere.");
            BuiltInCheats = Cfg.Bind("Toggles", "BuiltInCheats", false, "Game's own dev cheats (M/N money, O island).");
            InfAmmo = Cfg.Bind("Toggles", "InfiniteAmmo", false, "Weapons never run out of ammo.");
            RigRoulette = Cfg.Bind("Toggles", "RigRoulette", false, "Casino roulette always pays out (host).");
            RigSlots = Cfg.Bind("Toggles", "RigSlots", false, "Slot machine always hits the jackpot (host).");
            AutoSell = Cfg.Bind("Toggles", "AutoSell", false, "Auto-sell caught fish (host).");
            OneShot = Cfg.Bind("Toggles", "OneShot", false, "ServerSettings.OneShotEnabled (built-in).");
            NoBaitLoss = Cfg.Bind("Toggles", "NoBaitLoss", false, "Bait survives every catch.");
            NoCooldown = Cfg.Bind("Toggles", "NoCooldown", false, "Weapons fire with no cooldown.");
            EspFish = Cfg.Bind("Toggles", "EspFish", false, "ESP overlay: fish/creatures.");
            EspPlayers = Cfg.Bind("Toggles", "EspPlayers", false, "ESP overlay: players.");
            EspItems = Cfg.Bind("Toggles", "EspItems", false, "ESP overlay: ground loot.");
            EspIslands = Cfg.Bind("Toggles", "EspIslands", false, "ESP overlay: island positions.");
            AimPlayers = Cfg.Bind("Toggles", "AimbotPlayers", false, "Auto-hit nearest player (Server RPC).");
            AimFish = Cfg.Bind("Toggles", "AimbotFish", false, "Auto-hit nearest fish (Server RPC).");
            AimBosses = Cfg.Bind("Toggles", "AimbotBosses", false, "Auto-hit nearest boss (Server RPC).");
            AimSnap = Cfg.Bind("Toggles", "AimSoftSnap", false, "Camera eases toward aimbot target.");

            Speed = Cfg.Bind("Sliders", "SpeedMulti", 1f, "Movement speed multiplier.");
            FishSize = Cfg.Bind("Sliders", "FishSizeMulti", 1f, "Fish size multiplier.");
            Jump = Cfg.Bind("Sliders", "JumpMulti", 1f, "Jump power multiplier.");
            Damage = Cfg.Bind("Sliders", "DamageMulti", 1f, "Global damage multiplier (0 = none, 10 = massive).");
            Water = Cfg.Bind("Sliders", "WaterOffset", 0f, "Ocean water level offset in meters.");
            TickSpeed = Cfg.Bind("Sliders", "TickSpeed", 1f, "Tick-based timer speed (experimental).");
            AimRange = Cfg.Bind("Sliders", "AimRange", 60f, "Aimbot max distance in meters.");
            AimFov = Cfg.Bind("Sliders", "AimFov", 30f, "Aimbot max angle from crosshair.");

            KeyGod = Cfg.Bind("Keybinds", "ToggleGodMode", KeyCode.F2, "Quick-toggle God mode.");
            KeyMoney = Cfg.Bind("Keybinds", "ToggleInfiniteMoney", KeyCode.F3, "Quick-toggle Infinite money.");
            KeyCatch = Cfg.Bind("Keybinds", "ToggleInstantCatch", KeyCode.F4, "Quick-toggle Instant catch.");
            KeyFish = Cfg.Bind("Keybinds", "ToggleAutoFish", KeyCode.F5, "Quick-toggle Auto fish.");
            KeyEsp = Cfg.Bind("Keybinds", "ToggleESP", KeyCode.F6, "Quick-toggle the ESP overlay.");
        }
    }
}