using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace HowToFishModMenu
{
    /// <summary>
    /// Memory-only dependency resolution. Harmony + MonoMod + Cecil ship as embedded
    /// resources inside VladMod.Core.dll, so injection writes nothing to the game folder
    /// and vanishes completely on restart. Must run before any Harmony type is touched
    /// (first line of ModCore.Entry).
    /// </summary>
    internal static class Deps
    {
        private static readonly Dictionary<string, Assembly> Cache =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private static bool _installed;

        public static void Init()
        {
            if (_installed) return;
            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string simple = args.Name.Split(',')[0].Trim();
                lock (Cache)
                {
                    if (Cache.TryGetValue(simple, out Assembly hit)) return hit;
                    Assembly self = typeof(Deps).Assembly;
                    foreach (string res in self.GetManifestResourceNames())
                    {
                        if (!res.StartsWith("Deps.", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!res.EndsWith(simple + ".dll", StringComparison.OrdinalIgnoreCase)) continue;
                        using (Stream s = self.GetManifestResourceStream(res))
                        {
                            if (s == null) continue;
                            byte[] buf = new byte[s.Length];
                            int read = 0;
                            while (read < buf.Length)
                            {
                                int n = s.Read(buf, read, buf.Length - read);
                                if (n <= 0) break;
                                read += n;
                            }
                            Assembly loaded = Assembly.Load(buf);
                            Cache[simple] = loaded;
                            return loaded;
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
