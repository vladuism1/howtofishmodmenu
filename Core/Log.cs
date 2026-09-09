using System;
using System.IO;
using UnityEngine;

namespace HowToFishModMenu
{
    /// <summary>
    /// Tiny logger that writes to How to Fish_Data/VladModLog.txt so the mod can be
    /// verified when injected standalone (no BepInEx console to read).
    /// </summary>
    public static class Log
    {
        private static string _path;
        private static readonly object Sync = new object();

        public static void Init()
        {
            try
            {
                if (_path != null) return;
                string dir = Path.GetDirectoryName(Application.dataPath);
                if (string.IsNullOrEmpty(dir)) return;
                _path = Path.Combine(dir, "How to Fish_Data", "VladModLog.txt");
                File.WriteAllText(_path, "[VladMod] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " log started\n");
            }
            catch { }
        }

        public static void Info(string msg) => Write("Info", msg);
        public static void Warn(string msg) => Write("Warn", msg);
        public static void Error(string msg) => Write("Error", msg);

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Sync)
                {
                    if (_path == null) Init();
                    if (_path != null) File.AppendAllText(_path, "[" + level + "] " + msg + "\n");
                }
            }
            catch { }
            try { Debug.Log("[VladMod] " + msg); } catch { }
        }
    }
}