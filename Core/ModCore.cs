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

        // ---- menu state ----
        private bool _menuOpen;
        private bool _cursorForced;
        private bool _prevCursorVisible;
        private CursorLockMode _prevCursorLock;
        private int _tab;
        private readonly string[][] _tabRows =
        {
            new[] { "Player", "Money", "Fishing", "Teleport", "Weapons" },
            new[] { "Casino", "World", "Items", "Unlocks" }
        };
        private Rect _windowRect = new Rect(60, 60, 440, 560);
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
                    bool any = ModState.EspFish || ModState.EspPlayers || ModState.EspItems;
                    ModState.EspFish = ModState.EspPlayers = ModState.EspItems = !any;
                    _settings.EspFish.Value = _settings.EspPlayers.Value = _settings.EspItems.Value = !any;
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
        // IMGUI mod menu
        // ==================================================================
        private void OnGUI()
        {
            try { EnsureUi(); } catch (Exception e) { Log.Warn("[VladMod] UI init failed: " + e.Message); return; }
            if (_ui == null) return;

            // ESP overlay draws regardless of whether the menu is open
            if (ModState.EspFish || ModState.EspPlayers || ModState.EspItems)
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
                GUILayout.MinWidth(440), GUILayout.MaxWidth(580));
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // ---- header ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("HOW TO FISH  MOD MENU  v2", _ui.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", _ui.CloseBtn, GUILayout.Width(24), GUILayout.Height(22)))
                _menuOpen = false;
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            // ---- tabs (two rows, segmented) ----
            int tab = 0;
            foreach (var row in _tabRows)
            {
                GUILayout.BeginHorizontal();
                foreach (string t in row)
                {
                    bool active = _tab == tab;
                    if (GUILayout.Button(t, active ? _ui.TabActive : _ui.TabInactive, GUILayout.Height(24)))
                        _tab = tab;
                    tab++;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6);

            try
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
            catch (Exception e) { Log.Warn("[VladMod] Tab draw failed: " + e.Message); }

            GUILayout.Space(8);
            DrawFooter();

            GUI.DragWindow(new Rect(0, 0, 10000, 30));
            GUILayout.EndVertical();
        }

        // ==================================================================
        // TABS
        // ==================================================================

        private void DrawPlayerTab()
        {
            Header("PLAYER");

            bool g = GUILayout.Toggle(ModState.GodMode, "  God mode  —  no damage / no drowning", _ui.Toggle);
            if (g != ModState.GodMode) SetGodMode(g);
            bool f = GUILayout.Toggle(ModState.InfiniteFullness, "  Infinite fullness  —  no hunger", _ui.Toggle);
            if (f != ModState.InfiniteFullness) { ModState.InfiniteFullness = f; _settings.InfFullness.Value = f; }

            GUILayout.Space(6);
            SubHeader("MOVEMENT");
            SliderCfg(_settings.Speed, ref ModState.SpeedMulti, 0.5f, 5f, "Speed", "x");
            SliderCfg(_settings.Jump, ref ModState.JumpMulti, 0.5f, 5f, "Jump", "x");

            GUILayout.Space(6);
            if (GUILayout.Button("Max out health / fullness", _ui.BtnAccent, GUILayout.Height(28)))
                MaxVitals();
            if (GUILayout.Button("Skip intro / tutorial  (host)", _ui.Btn, GUILayout.Height(28)))
                SkipIntroTutorial();

            Player p = Player.LocalPlayer;
            if (!p)
            {
                GUILayout.Space(6);
                GUILayout.Label("Not in a game yet", _ui.LabelDim);
                return;
            }

            GUILayout.Space(8);
            SubHeader("VITALS");
            Bar("Health", p.Vitals.Health, VitalsMax(_maxHealthField, 100), new Color(0.25f, 0.85f, 0.45f));
            Bar("Fullness", p.Vitals.Fullness, VitalsMax(_maxFullnessField, 100), new Color(0.95f, 0.7f, 0.3f));
        }

        private void DrawMoneyTab()
        {
            Header("MONEY");

            bool m = GUILayout.Toggle(ModState.InfiniteMoney, "  Infinite money  —  never drains, MP buys are free", _ui.Toggle);
            if (m != ModState.InfiniteMoney) SetInfMoney(m);
            bool s = GUILayout.Toggle(ModState.AutoSell, "  Auto-sell caught fish  (host)", _ui.Toggle);
            if (s != ModState.AutoSell) { ModState.AutoSell = s; _settings.AutoSell.Value = s; }

            GUILayout.Space(6);
            if (GUILayout.Button("Give max money  (+$99999)", _ui.Btn, GUILayout.Height(28)))
                GiveMaxMoney();
            if (GUILayout.Button("Give all baits  (free)", _ui.Btn, GUILayout.Height(28)))
                GiveAllBaits();

            Player p = Player.LocalPlayer;
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("CURRENT MONEY", _ui.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.Label(p ? "$" + MoneyManager.Money : "—", _ui.Value);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label(IsHost()
                ? "You are the HOST — money changes apply to everyone in the lobby."
                : "You are a CLIENT — free-buy RPCs work, but the shared money pool is controlled by the host.",
                _ui.LabelDim, GUILayout.Width(410));
        }

        private void DrawFishingTab()
        {
            Header("FISHING");

            bool c = GUILayout.Toggle(ModState.InstantCatch, "  Instant catch  —  fish bites immediately", _ui.Toggle);
            if (c != ModState.InstantCatch) SetInstantCatch(c);
            bool a = GUILayout.Toggle(ModState.AutoFish, "  Auto fish  —  instant bite + auto reel", _ui.Toggle);
            if (a != ModState.AutoFish) SetAutoFish(a);
            bool sh = GUILayout.Toggle(ModState.ForceShiny, "  Always rare / shiny fish", _ui.Toggle);
            if (sh != ModState.ForceShiny) { ModState.ForceShiny = sh; _settings.ForceShiny.Value = sh; }

            GUILayout.Space(6);
            SubHeader("FISH SIZE");
            SliderCfg(_settings.FishSize, ref ModState.FishSizeMulti, 0.5f, 10f, "Size", "x");

            GUILayout.Space(8);
            bool canDup = IsHost();
            bool dupWas = GUI.enabled;
            GUI.enabled = canDup;
            if (GUILayout.Button("Duplicate held item", _ui.Btn, GUILayout.Height(28)))
                DuplicateHeldItem();
            GUI.enabled = dupWas;

            GUILayout.Space(4);
            SubHeader("SPAWN CREATURE  (host)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                _spawnFishIndex = ClampIdx(_spawnFishIndex - 1, GameInfo.AllCreatureCount);
            GUILayout.Label(CreatureName(_spawnFishIndex), _ui.Value, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                _spawnFishIndex = ClampIdx(_spawnFishIndex + 1, GameInfo.AllCreatureCount);
            GUILayout.EndHorizontal();

            bool wasEnabled = GUI.enabled;
            GUI.enabled = IsHost();
            if (GUILayout.Button("Spawn creature in front of you", _ui.BtnAccent, GUILayout.Height(28)))
                SpawnCreature(_spawnFishIndex);
            GUI.enabled = wasEnabled;
        }

        private void DrawTeleportTab()
        {
            Header("TELEPORT");

            if (GUILayout.Button("Teleport to next island", _ui.BtnAccent, GUILayout.Height(30)))
                SafeCall(() => OnlineIslandManager.TpToNextIsland(false));
            if (GUILayout.Button("Teleport to previous island", _ui.Btn, GUILayout.Height(30)))
                SafeCall(() => OnlineIslandManager.TpToNextIsland(true));
            if (GUILayout.Button("Unlock ALL islands", _ui.Btn, GUILayout.Height(30)))
                SafeCall(() => OnlineIslandManager.Instance?.UnlockIsland(byte.MaxValue));

            GUILayout.Space(8);
            SubHeader("JUMP TO ISLAND");
            GUILayout.BeginHorizontal();
            for (byte i = 1; i <= 6; i++)
            {
                byte island = i; // copy: lambdas capture the for-loop variable by reference
                if (GUILayout.Button(island.ToString(), _ui.Btn, GUILayout.Height(28)))
                    SafeCall(() => OnlineIslandManager.TpToSpecificIsland(island));
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            var players = PlayerManager.Players;
            if (players.Count > 0)
            {
                _teleportIndex = Mathf.Clamp(_teleportIndex, 0, players.Count - 1);
                SubHeader("PLAYERS");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                    _teleportIndex = (_teleportIndex + players.Count - 1) % players.Count;
                Player t = players[_teleportIndex];
                string label = "Player " + (_teleportIndex + 1) + (t.Vitals != null ? "  (HP " + t.Vitals.Health + ")" : "");
                GUILayout.Label(label, _ui.Value, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(">", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                    _teleportIndex = (_teleportIndex + 1) % players.Count;
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Teleport to this player", _ui.Btn, GUILayout.Height(28)))
                    TeleportMeTo(t);
                if (GUILayout.Button("Pull this player to me", _ui.Btn, GUILayout.Height(28)))
                    PullPlayerToMe(t);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Damage 50", _ui.Btn, GUILayout.Height(26)))
                    DamagePlayer(t, 50);
                if (GUILayout.Button("One-shot", _ui.Btn, GUILayout.Height(26)))
                    DamagePlayer(t, 999999);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            if (GUILayout.Button("Revive all dead players", _ui.BtnAccent, GUILayout.Height(28)))
                ReviveAll();
        }

        private void DrawWeaponsTab()
        {
            Header("WEAPONS");

            bool a = GUILayout.Toggle(ModState.InfiniteAmmo, "  Infinite ammo  —  no reloads ever", _ui.Toggle);
            if (a != ModState.InfiniteAmmo) { ModState.InfiniteAmmo = a; _settings.InfAmmo.Value = a; }

            Player p = Player.LocalPlayer;
            GUILayout.Space(6);
            if (!p || p.Holding == null || p.Holding.HeldItem == null)
            {
                GUILayout.Label("Hold a weapon to see its stats and upgrades.", _ui.LabelDim);
                return;
            }

            Item held = p.Holding.HeldItem;
            if (held is Weapon w)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("AMMO", _ui.LabelDim);
                GUILayout.FlexibleSpace();
                GUILayout.Label(w.Ammo + " / " + (w.Attachments != null ? w.Attachments.AmmoPerMag : 0), _ui.Value);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                if (GUILayout.Button("Refill ammo now", _ui.Btn, GUILayout.Height(26)))
                {
                    try
                    {
                        var f = AccessTools.Field(typeof(Weapon), "<Ammo>k__BackingField");
                        f.SetValue(w, w.Attachments != null ? w.Attachments.AmmoPerMag : 1);
                    }
                    catch { }
                }
                if (GUILayout.Button("Free bullet damage upgrade", _ui.Btn, GUILayout.Height(26)))
                    SafeCall(() => Server.Instance.BuyBulletUpgrade(w));
                if (GUILayout.Button("Free attachment (all slots)", _ui.Btn, GUILayout.Height(26)))
                {
                    for (byte i = 0; i < 4; i++)
                    {
                        try { Server.Instance.BuyAttachment(w, i); } catch { break; }
                    }
                }
            }
            else if (held is Melee melee)
            {
                if (GUILayout.Button("Free sharpness upgrade", _ui.Btn, GUILayout.Height(26)))
                    SafeCall(() => Server.Instance.BuySharpnessUpgrade(melee));
            }
            else
            {
                GUILayout.Label("Held item is not a weapon.", _ui.LabelDim);
            }

            GUILayout.Space(8);
            GUILayout.Label("Upgrade RPCs are free because CanAfford is always true" +
                            " while Infinite Money is on (host-side).", _ui.LabelDim, GUILayout.Width(410));
        }

        private void DrawCasinoTab()
        {
            Header("CASINO  (host only)");

            bool r = GUILayout.Toggle(ModState.RigRoulette, "  Rig roulette  —  every spin pays out", _ui.Toggle);
            if (r != ModState.RigRoulette) { ModState.RigRoulette = r; _settings.RigRoulette.Value = r; }
            bool s = GUILayout.Toggle(ModState.RigSlots, "  Rig slot machine  —  always jackpot", _ui.Toggle);
            if (s != ModState.RigSlots) { ModState.RigSlots = s; _settings.RigSlots.Value = s; }

            GUILayout.Space(10);
            GUILayout.Label("How it works:", _ui.LabelDim);
            GUILayout.Label("• Roulette: the host picks the winning color in", _ui.LabelDim);
            GUILayout.Label("  ServerRouletteResult — we force it to your bet.", _ui.LabelDim);
            GUILayout.Label("• Slots: the game has a built-in cheat-skin hook", _ui.LabelDim);
            GUILayout.Label("  (SlotMachine.SetCheatSkin) — we feed it a", _ui.LabelDim);
            GUILayout.Label("  legendary item so the jackpot reel always hits.", _ui.LabelDim);
            GUILayout.Space(6);
            GUILayout.Label("Works only when you host the lobby.", _ui.LabelDim);
        }

        private void DrawWorldTab()
        {
            Header("WORLD");

            SubHeader("COMBAT");
            SliderCfg(_settings.Damage, ref ModState.DamageMulti, 0f, 10f, "Damage", "x");
            if (!Mathf.Approximately(ModState.DamageMulti, 1f))
            {
                try
                {
                    if (_dmgMultiField != null) _dmgMultiField.SetValue(null, ModState.DamageMulti);
                }
                catch { }
            }
            bool os = GUILayout.Toggle(ModState.OneShot, "  One-shot everything  (built-in)", _ui.Toggle);
            if (os != ModState.OneShot)
            {
                ModState.OneShot = os; _settings.OneShot.Value = os;
                try
                {
                    if (_oneShotField != null) _oneShotField.SetValue(null, os);
                }
                catch { }
            }

            GUILayout.Space(6);
            SubHeader("OCEAN");
            SliderCfg(_settings.Water, ref ModState.WaterOffset, -3f, 3f, "Water", "m");

            GUILayout.Space(6);
            SubHeader("ACTIONS  (host)");
            bool wasEnabled = GUI.enabled;
            GUI.enabled = IsHost();
            if (GUILayout.Button("Spawn explosion at my position", _ui.Btn, GUILayout.Height(28)))
                SpawnExplosion();
            if (GUILayout.Button("Spawn boss", _ui.Btn, GUILayout.Height(28)))
                SpawnBoss();
            if (GUILayout.Button("Kill all creatures", _ui.Btn, GUILayout.Height(28)))
                KillAllCreatures();
            GUI.enabled = wasEnabled;

            GUILayout.Space(8);
            bool sun = GUILayout.Toggle(ModState.Sunset, "  Force sunset", _ui.Toggle);
            if (sun != ModState.Sunset) SetSunset(sun);
            bool ch = GUILayout.Toggle(ModState.BuiltInCheats, "  Built-in dev cheats  (M/N money, O = island)", _ui.Toggle);
            if (ch != ModState.BuiltInCheats) SetCheats(ch);

            GUILayout.Space(8);
            SubHeader("ESP OVERLAY");
            bool ef = GUILayout.Toggle(ModState.EspFish, "  Fish / creatures", _ui.Toggle);
            if (ef != ModState.EspFish) { ModState.EspFish = ef; _settings.EspFish.Value = ef; }
            bool ep = GUILayout.Toggle(ModState.EspPlayers, "  Players", _ui.Toggle);
            if (ep != ModState.EspPlayers) { ModState.EspPlayers = ep; _settings.EspPlayers.Value = ep; }
            bool ei = GUILayout.Toggle(ModState.EspItems, "  Ground loot / weapons", _ui.Toggle);
            if (ei != ModState.EspItems) { ModState.EspItems = ei; _settings.EspItems.Value = ei; }

            GUILayout.Space(8);
            SubHeader("TICK SPEED  (experimental)");
            float oldTick = ModState.TickSpeed;
            SliderCfg(_settings.TickSpeed, ref ModState.TickSpeed, 0.5f, 3f, "Tick", "x");
            if (!Mathf.Approximately(oldTick, ModState.TickSpeed))
            {
                try
                {
                    if (oldTick > 0f) GameInfo.TickMulti = GameInfo.TickMulti / oldTick * ModState.TickSpeed;
                    else GameInfo.TickMulti *= ModState.TickSpeed;
                }
                catch { }
            }

            GUILayout.Space(8);
            SubHeader("ACHIEVEMENTS");
            if (GUILayout.Button("Unlock ALL Steam achievements", _ui.Btn, GUILayout.Height(28)))
                SafeCall(() => AchievementManager.ToggleAllAchievements(true));
        }

        private void DrawPresets()
        {
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
            GUILayout.Space(8);
            SubHeader("KEYBINDS  (click to rebind)");
            KeybindRow("God mode", _settings.KeyGod);
            KeybindRow("Infinite money", _settings.KeyMoney);
            KeybindRow("Instant catch", _settings.KeyCatch);
            KeybindRow("Auto fish", _settings.KeyFish);
            KeybindRow("ESP toggle", _settings.KeyEsp);
        }

        private void KeybindRow(string label, CfgEntry<KeyCode> entry)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.Label, GUILayout.Width(120));
            GUILayout.Label(entry.Value == KeyCode.None ? "(none)" : entry.Value.ToString(), _ui.Value, GUILayout.Width(90));
            if (GUILayout.Button(_rebindEntry == entry ? "Press a key..." : "Rebind", _ui.Btn, GUILayout.Width(80), GUILayout.Height(22)))
            {
                _rebindEntry = _rebindEntry == entry ? null : entry;
                _rebindLabel = label;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawUnlockTab()
        {
            Header("UNLOCKS");
            GUILayout.Label("One-time unlocks  (host recommended)", _ui.LabelDim);

            GUILayout.Space(6);
            if (GUILayout.Button("Unlock boat + boat radar", _ui.Btn, GUILayout.Height(28)))
            {
                SafeCall(() => { if (BoatManager.Boat) { BoatManager.Boat.UnlockBoat(); BoatManager.Boat.UnlockBoatRadar(); } });
            }
            if (GUILayout.Button("Unlock grill", _ui.Btn, GUILayout.Height(28)))
                SafeCall(() => NPCManager.UnlockGrill());
            if (GUILayout.Button("Unlock all inventory pockets", _ui.Btn, GUILayout.Height(28)))
                UnlockPockets();

            GUILayout.Space(10);
            SubHeader("PRESETS");
            DrawPresets();
            DrawKeybinds();
        }

        private void DrawItemsTab()
        {
            Header("ITEMS");

            int count = ModItems.Count;
            if (count <= 0)
            {
                GUILayout.Label("No items found.", _ui.LabelDim);
                return;
            }
            _itemIndex = Mathf.Clamp(_itemIndex, 0, count - 1);

            SubHeader("ITEM BROWSER");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                _itemIndex = (_itemIndex + count - 1) % count;
            GUILayout.Label(ModItems.Name(_itemIndex), _ui.Value, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", _ui.Btn, GUILayout.Width(34), GUILayout.Height(26)))
                _itemIndex = (_itemIndex + 1) % count;
            GUILayout.EndHorizontal();

            Item it = ModItems.Get(_itemIndex);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID", _ui.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.Label(it ? it.ID.ToString() : "—", _ui.Value);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("Give this item  (free, replaces held item)", _ui.BtnAccent, GUILayout.Height(28)))
                GiveItem(_itemIndex);

            GUILayout.Space(10);
            SubHeader("SKINS");
            if (GUILayout.Button("Unlock all outfits / skins", _ui.Btn, GUILayout.Height(28)))
                UnlockAllSkins();
        }

        // ==================================================================
        // UI helpers
        // ==================================================================

        private void Header(string text)
        {
            GUILayout.Space(2);
            GUILayout.Label(text, _ui.SectionHeader);
            GUILayout.Box(string.Empty, _ui.Sep, GUILayout.ExpandWidth(true), GUILayout.Height(2));
            GUILayout.Space(4);
        }

        private void SubHeader(string text)
        {
            GUILayout.Space(2);
            GUILayout.Label(text, _ui.LabelDim);
        }

        private void SliderCfg(CfgEntry<float> entry, ref float field, float min, float max, string label, string suffix)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.Label, GUILayout.Width(56));
            float v = GUILayout.HorizontalSlider(field, min, max);
            GUILayout.Label((Math.Round(v, 2)).ToString("0.00") + suffix, _ui.Value, GUILayout.Width(64));
            GUILayout.EndHorizontal();
            float rounded = (float)Math.Round(v, 2);
            if (!Mathf.Approximately(rounded, field))
            {
                field = rounded;
                if (entry != null) entry.Value = rounded;
            }
        }

        private void Bar(string label, int value, int max, Color fillColor)
        {
            const float barWidth = 250f;
            float percent = Mathf.Clamp01(max > 0 ? (float)value / max : 0f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _ui.Label, GUILayout.Width(72));
            GUILayout.Box(string.Empty, _ui.BarTrack, GUILayout.Width(barWidth), GUILayout.Height(12));
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
            GUILayout.BeginHorizontal();
            GUILayout.Label("Made by Vlad  ·  v2.1.0", _ui.Footer);
            GUILayout.FlexibleSpace();

            string role = IsHost() ? "HOST" : (IsClient() ? "CLIENT" : "IDLE");
            GUIStyle pill = IsHost() ? _ui.PillHost : (IsClient() ? _ui.PillClient : _ui.PillIdle);
            GUILayout.Label(role, pill, GUILayout.Width(58), GUILayout.Height(18));

            Player p = Player.LocalPlayer;
            if (p)
            {
                GUILayout.Space(6);
                GUILayout.Label("$" + MoneyManager.Money + "  ·  HP " + p.Vitals.Health, _ui.Footer);
            }
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
            if (ModState.EspPlayers)
            {
                foreach (Player p in PlayerManager.AlivePlayers)
                {
                    if (!p || p == local) continue;
                    DrawEspBox(cam, p.Transform, camPos, width, height, new Color(1f, 0.85f, 0.2f), 1.8f,
                        "P" + (PlayerManager.AlivePlayers.IndexOf(p) + 1));
                }
            }

            // ground loot
            if (ModState.EspItems)
            {
                foreach (var kv in ItemManager.Items)
                {
                    Item it = kv.Value;
                    if (!it || it == null) continue;
                    Color col = it is Weapon ? new Color(1f, 0.4f, 0.3f) : new Color(0.6f, 1f, 0.4f);
                    DrawEspBox(cam, it.transform, camPos, width, height, col, 0.7f, "");
                }
            }
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
            if (!p) return;
            SafeCall(() =>
            {
                for (byte i = 0; i < 6; i++)
                {
                    try { p.Inventory.UnlockExtraPocket(i); } catch { break; }
                }
            });
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
        // Cached UI theme — styles/textures are created ONCE and reused,
        // so the menu costs almost nothing while open.
        // ==================================================================
        private sealed class Ui
        {
            public GUIStyle Window, Title, CloseBtn;
            public GUIStyle TabActive, TabInactive;
            public GUIStyle Label, LabelDim, Value, SectionHeader, Footer;
            public GUIStyle Btn, BtnAccent, Toggle, Sep;
            public GUIStyle BarTrack, BarFill;
            public GUIStyle PillHost, PillClient, PillIdle, Hint;
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

            Color bg = new Color(0.070f, 0.086f, 0.105f, 0.97f);
            Color panel = new Color(0.105f, 0.125f, 0.15f, 1f);
            Color panelHover = new Color(0.16f, 0.19f, 0.23f, 1f);
            Color panelActive = new Color(0.21f, 0.25f, 0.30f, 1f);
            Color accent = new Color(0.09f, 0.66f, 0.62f, 1f);
            Color accentBright = new Color(0.13f, 0.82f, 0.76f, 1f);
            Color line = new Color(0.13f, 0.16f, 0.20f, 1f);
            Color text = new Color(0.90f, 0.94f, 0.97f, 1f);
            Color dim = new Color(0.55f, 0.62f, 0.68f, 1f);

            var skin = GUI.skin;
            var ui = new Ui();

            ui.Window = Clone(skin.window);
            ui.Window.normal.background = Solid(bg);
            ui.Window.border = new RectOffset(8, 8, 8, 8);
            ui.Window.padding = new RectOffset(10, 10, 8, 8);

            ui.Title = Clone(skin.label);
            ui.Title.fontStyle = FontStyle.Bold;
            ui.Title.fontSize = 15;
            ui.Title.normal.textColor = new Color(0.35f, 0.95f, 0.9f, 1f);

            ui.CloseBtn = Clone(skin.button);
            ui.CloseBtn.fontStyle = FontStyle.Bold;
            ui.CloseBtn.fontSize = 13;
            ui.CloseBtn.normal.background = Solid(panel);
            ui.CloseBtn.normal.textColor = dim;
            ui.CloseBtn.hover.background = Solid(panelHover);
            ui.CloseBtn.hover.textColor = text;
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
            ui.SectionHeader.fontSize = 12;
            ui.SectionHeader.normal.textColor = new Color(0.4f, 0.9f, 0.85f, 1f);

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
            ui.BtnAccent.normal.background = Solid(new Color(0.06f, 0.42f, 0.40f, 1f));
            ui.BtnAccent.normal.textColor = Color.white;
            ui.BtnAccent.hover.background = Solid(new Color(0.09f, 0.55f, 0.52f, 1f));
            ui.BtnAccent.hover.textColor = Color.white;
            ui.BtnAccent.active.background = Solid(accent);
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

            _ui = ui;
        }
    }

    /// <summary>All persisted config entries (auto-saved to vladmod.cfg on change).</summary>
    internal sealed class Settings
    {
        public readonly CfgEntry<bool> GodMode, InfFullness, InfMoney, InstantCatch, AutoFish, ForceShiny,
            Sunset, BuiltInCheats, InfAmmo, RigRoulette, RigSlots, AutoSell, OneShot, NoBaitLoss, NoCooldown,
            EspFish, EspPlayers, EspItems;
        public readonly CfgEntry<float> Speed, FishSize, Jump, Damage, Water, TickSpeed;
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

            Speed = Cfg.Bind("Sliders", "SpeedMulti", 1f, "Movement speed multiplier.");
            FishSize = Cfg.Bind("Sliders", "FishSizeMulti", 1f, "Fish size multiplier.");
            Jump = Cfg.Bind("Sliders", "JumpMulti", 1f, "Jump power multiplier.");
            Damage = Cfg.Bind("Sliders", "DamageMulti", 1f, "Global damage multiplier (0 = none, 10 = massive).");
            Water = Cfg.Bind("Sliders", "WaterOffset", 0f, "Ocean water level offset in meters.");
            TickSpeed = Cfg.Bind("Sliders", "TickSpeed", 1f, "Tick-based timer speed (experimental).");

            KeyGod = Cfg.Bind("Keybinds", "ToggleGodMode", KeyCode.F2, "Quick-toggle God mode.");
            KeyMoney = Cfg.Bind("Keybinds", "ToggleInfiniteMoney", KeyCode.F3, "Quick-toggle Infinite money.");
            KeyCatch = Cfg.Bind("Keybinds", "ToggleInstantCatch", KeyCode.F4, "Quick-toggle Instant catch.");
            KeyFish = Cfg.Bind("Keybinds", "ToggleAutoFish", KeyCode.F5, "Quick-toggle Auto fish.");
            KeyEsp = Cfg.Bind("Keybinds", "ToggleESP", KeyCode.F6, "Quick-toggle the ESP overlay.");
        }
    }
}