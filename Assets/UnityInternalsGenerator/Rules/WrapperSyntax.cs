namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using System.Collections.Generic;

    /// <summary>Names and fragments the instance-wrapper shapes share. The reserved names are the ones a wrapper declares itself.</summary>
    public static class WrapperSyntax
    {
        /// <summary>The internal field every instance wrapper declares, holding the real Unity value.</summary>
        public const string ValueField = "m_Value";

        /// <summary>The internal factory every instance wrapper declares (internal because its parameter is an internal Unity type).</summary>
        public const string WrapMethod = "Wrap";

        /// <summary>The property a root class wrapper declares for its first public base.</summary>
        public const string TargetProperty = "Target";

        /// <summary>Namespace of the generated central <see cref="RegistryType"/>.</summary>
        public const string RegistryNamespace = "WrappedRuntime";

        /// <summary>Generated type holding the single public <c>Wrap(object)</c> entry point.</summary>
        public const string RegistryType = "WrapperRegistry";

        private static readonly string[] s_ReservedNames = { ValueField, WrapMethod, TargetProperty, "Equals", "GetHashCode" };

        /// <summary>Names a wrapped member may not take on an instance wrapper, because the wrapper declares them.</summary>
        public static IReadOnlyList<string> ReservedNames => s_ReservedNames;

        public static string RegistryRef => "global::" + RegistryNamespace + "." + RegistryType;

        public static string RegistryPath => RegistryNamespace + "/" + RegistryType + ".g.cs";

        /// <summary>Unwrapping suffix: value types are never null, reference types unwrap through <c>?.</c>.</summary>
        public static string UnwrapSuffix(bool isValueType) => isValueType ? ")." + ValueField : ")?." + ValueField;

        public static bool IsReserved(string name) => Array.IndexOf(s_ReservedNames, name) >= 0;
    }
}
