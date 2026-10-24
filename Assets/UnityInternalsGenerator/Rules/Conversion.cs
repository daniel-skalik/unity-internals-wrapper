namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    /// <summary>How a Unity-side type crosses the wrapper boundary. Status other than Wrapped means the member cannot be wrapped.</summary>
    public sealed class Conversion
    {
        private readonly string m_ToWrappedPrefix;
        private readonly string m_ToWrappedSuffix;
        private readonly string m_ToUnityPrefix;
        private readonly string m_ToUnitySuffix;

        public WrapStatus Status { get; }
        public string Reason { get; }
        public string WrappedTypeRef { get; }

        private Conversion(WrapStatus status, string reason, string wrappedTypeRef, string toWrappedPrefix, string toWrappedSuffix, string toUnityPrefix, string toUnitySuffix)
        {
            Status = status;
            Reason = reason;
            WrappedTypeRef = wrappedTypeRef;
            m_ToWrappedPrefix = toWrappedPrefix;
            m_ToWrappedSuffix = toWrappedSuffix;
            m_ToUnityPrefix = toUnityPrefix;
            m_ToUnitySuffix = toUnitySuffix;
        }

        public static Conversion Passthrough(string typeRef) => new Conversion(WrapStatus.Wrapped, null, typeRef, string.Empty, string.Empty, string.Empty, string.Empty);

        public static Conversion Cast(string wrappedTypeRef, string unityTypeRef) => new Conversion(WrapStatus.Wrapped, null, wrappedTypeRef, "(" + wrappedTypeRef + ")", string.Empty, "(" + unityTypeRef + ")", string.Empty);

        /// <summary>Instance wrapper: the wrapper's own factory one way, its value field the other.</summary>
        public static Conversion Wrapper(string wrappedTypeRef, bool isValueType)
        {
            return new Conversion(WrapStatus.Wrapped, null, wrappedTypeRef, wrappedTypeRef + "." + WrapperSyntax.WrapMethod + "(", ")", "(", WrapperSyntax.UnwrapSuffix(isValueType));
        }

        public static Conversion NotWrapped(WrapStatus status, string reason) => new Conversion(status, reason, null, null, null, null, null);

        public static Conversion Deferred(string reason) => NotWrapped(WrapStatus.Deferred, reason);

        public static Conversion Unsupported(string reason) => NotWrapped(WrapStatus.Unsupported, reason);

        public string ToWrapped(string expr) => Usable(m_ToWrappedPrefix) + expr + m_ToWrappedSuffix;

        public string ToUnity(string expr) => Usable(m_ToUnityPrefix) + expr + m_ToUnitySuffix;

        private string Usable(string prefix) => Status == WrapStatus.Wrapped ? prefix : throw new InvalidOperationException("Conversion is not usable: " + Status + " (" + Reason + ")");
    }
}
