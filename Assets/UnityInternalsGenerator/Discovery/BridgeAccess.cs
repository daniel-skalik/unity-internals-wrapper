namespace DandaUtils.Editor.UnityInternalsGenerator.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text.RegularExpressions;

    public static class BridgeAccess
    {
        private static readonly Regex s_BridgeName = new Regex(@"^Unity\.InternalAPI(Editor|Engine)Bridge(Dev)?\.\d{3}$", RegexOptions.CultureInvariant);

        public static IReadOnlyCollection<string> GetGrantedFriends(Assembly a)
        {
            var result = new List<string>();
            foreach (var attribute in a.GetCustomAttributes<InternalsVisibleToAttribute>())
            {
                var name = attribute.AssemblyName;
                var comma = name.IndexOf(',');
                if (comma >= 0) name = name.Substring(0, comma);
                name = name.Trim();
                if (name.Length > 0) result.Add(name);
            }
            return result;
        }

        public static bool Grants(Assembly a, string hostName) => GetGrantedFriends(a).Contains(hostName);

        public static string ChooseHostName(IReadOnlyList<IReadOnlyCollection<string>> grantsPerSource, ISet<string> usedNames, string persisted, out string warning)
        {
            warning = null;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var source in grantsPerSource)
            {
                foreach (var name in source.Distinct(StringComparer.Ordinal))
                {
                    if (s_BridgeName.IsMatch(name) == false || usedNames.Contains(name)) continue;
                    counts.TryGetValue(name, out var count);
                    counts[name] = count + 1;
                }
            }

            if (counts.Count == 0)
            {
                if (string.IsNullOrEmpty(persisted) == false) warning = $"Persisted host name '{persisted}' is no longer usable and no replacement is available.";
                return null;
            }

            var best = counts
                .OrderByDescending(p => p.Value)
                .ThenBy(p => p.Key.Contains("BridgeDev.", StringComparison.Ordinal))
                .ThenBy(p => p.Key, StringComparer.Ordinal)
                .First();

            if (string.IsNullOrEmpty(persisted)) return best.Key;
            if (counts.TryGetValue(persisted, out var persistedCount) && persistedCount == best.Value) return persisted;

            var chosen = best.Key;
            warning = $"Persisted host name '{persisted}' is no longer usable (already used or not granted by all sources); using '{chosen}'.";
            return chosen;
        }
    }
}
