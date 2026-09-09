using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace HowToFishModMenu
{
    /// <summary>A single typed config entry. Setting Value marks the file dirty;
    /// it is flushed to disk at most once every few seconds (see Cfg.Tick).</summary>
    public class CfgEntry<T> : Cfg.CfgEntryBase
    {
        private T _value;

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                Cfg.SaveDeferred();
            }
        }

        public CfgEntry(string section, string key, T def)
        {
            Section = section;
            Key = key;
            _value = def;
        }

        public override void Load(string raw)
        {
            try
            {
                if (typeof(T) == typeof(bool)) { _value = (T)(object)(raw == "true"); return; }
                if (typeof(T) == typeof(float)) { _value = (T)(object)float.Parse(raw, CultureInfo.InvariantCulture); return; }
                if (typeof(T) == typeof(int)) { _value = (T)(object)int.Parse(raw); return; }
                if (typeof(T) == typeof(KeyCode)) { _value = (T)(object)Enum.Parse(typeof(KeyCode), raw); return; }
                _value = (T)Convert.ChangeType(raw, typeof(T));
            }
            catch { }
        }

        public override string Serialize()
        {
            if (typeof(T) == typeof(float)) return ((float)(object)_value).ToString("0.##", CultureInfo.InvariantCulture);
            return _value != null ? _value.ToString() : string.Empty;
        }
    }

    /// <summary>Mini ini config: [Section] / key = value. Saved next to the game exe.</summary>
    public static class Cfg
    {
        public abstract class CfgEntryBase
        {
            public string Section;
            public string Key;
            public abstract void Load(string raw);
            public abstract string Serialize();
        }

        private static readonly List<CfgEntryBase> All = new List<CfgEntryBase>();
        public static string FilePath { get; private set; }

        private static bool _dirty;
        private static float _lastSaveTime;
        private const float SaveInterval = 2f;

        public static void Init()
        {
            try
            {
                if (FilePath != null) return;
                string dir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(dir)) return;
                FilePath = Path.Combine(dir, "How to Fish_Data", "vladmod.cfg");
            }
            catch { }
        }

        public static CfgEntry<T> Bind<T>(string section, string key, T def, string description = null)
        {
            var entry = new CfgEntry<T>(section, key, def);
            All.Add(entry);
            return entry;
        }

        /// <summary>Load values from disk into the bound entries (call after binding).</summary>
        public static void Reload()
        {
            Init();
            try
            {
                if (FilePath == null || !File.Exists(FilePath)) { Save(); return; }
                string section = "";
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string l = line.Trim();
                    if (l.StartsWith("[") && l.EndsWith("]")) { section = l.Substring(1, l.Length - 2); continue; }
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    int eq = l.IndexOf('=');
                    if (eq < 0) continue;
                    string key = l.Substring(0, eq).Trim();
                    string val = l.Substring(eq + 1).Trim();
                    foreach (CfgEntryBase e in All)
                    {
                        if (e.Section == section && e.Key == key) { e.Load(val); break; }
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            Init();
            try
            {
                if (FilePath == null) return;
                var sb = new StringBuilder();
                string cur = null;
                foreach (CfgEntryBase e in All)
                {
                    if (e.Section != cur)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[" + e.Section + "]");
                        cur = e.Section;
                    }
                    sb.AppendLine(e.Key + " = " + e.Serialize());
                }
                File.WriteAllText(FilePath, sb.ToString());
                _dirty = false;
                _lastSaveTime = Time.realtimeSinceStartup;
            }
            catch { }
        }

        /// <summary>Marks the config dirty; the next Tick flushes it (at most once per SaveInterval).</summary>
        public static void SaveDeferred()
        {
            _dirty = true;
        }

        /// <summary>Call every frame; flushes a dirty config at most once per SaveInterval.</summary>
        public static void Tick()
        {
            if (!_dirty) return;
            try
            {
                if (Time.realtimeSinceStartup - _lastSaveTime < SaveInterval) return;
                Save();
            }
            catch { }
        }
    }
}