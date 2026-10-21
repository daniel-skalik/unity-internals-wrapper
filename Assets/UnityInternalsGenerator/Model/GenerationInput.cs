namespace DandaUtils.Editor.UnityInternalsGenerator.Model
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;

    public sealed class GenerationInput
    {
        public IReadOnlyList<Assembly> Sources { get; }
        public string HostAssemblyName { get; }
        public NamingStyle Naming { get; }

        /// <summary>What gets emitted. Never changes classification; null in the constructor means <see cref="SelectionRules.All"/>.</summary>
        public SelectionRules Selection { get; }

        public string UnityVersion { get; }

        /// <summary>Ids the compile loop excluded after a failed pre-compile; classified Excluded (AutoExclusion), which dependency need never overrides.</summary>
        public IReadOnlyCollection<string> AutoExcludedIds { get; }

        public GenerationInput(
            IReadOnlyList<Assembly> sources,
            string hostAssemblyName,
            NamingStyle naming,
            SelectionRules selection,
            string unityVersion,
            IReadOnlyCollection<string> autoExcludedIds = null)
        {
            Sources = sources ?? Array.Empty<Assembly>();
            HostAssemblyName = hostAssemblyName;
            Naming = naming;
            Selection = selection ?? SelectionRules.All;
            UnityVersion = unityVersion;
            AutoExcludedIds = autoExcludedIds ?? Array.Empty<string>();
        }
    }
}
