namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    /// <summary>How a Unity-side type crosses the wrapper boundary. Status other than Wrapped means the member cannot be wrapped.</summary>
    public sealed class Conversion
    {
        private readonly string m_ToWrappedPrefix;
        private readonly string m_ToUnityPrefix;

        public WrapStatus Status { get; }
        public string Reason { get; }
        public string WrappedTypeRef { get; }

        private Conversion(WrapStatus status, string reason, string wrappedTypeRef, string toWrappedPrefix, string toUnityPrefix)
        {
            Status = status;
            Reason = reason;
            WrappedTypeRef = wrappedTypeRef;
            m_ToWrappedPrefix = toWrappedPrefix;
            m_ToUnityPrefix = toUnityPrefix;
        }

        public static Conversion Passthrough(string typeRef) => new Conversion(WrapStatus.Wrapped, null, typeRef, string.Empty, string.Empty);

        public static Conversion Cast(string wrappedTypeRef, string unityTypeRef) => new Conversion(WrapStatus.Wrapped, null, wrappedTypeRef, "(" + wrappedTypeRef + ")", "(" + unityTypeRef + ")");

        public static Conversion NotWrapped(WrapStatus status, string reason) => new Conversion(status, reason, null, null, null);

        public static Conversion Deferred(string reason) => NotWrapped(WrapStatus.Deferred, reason);

        public static Conversion Unsupported(string reason) => NotWrapped(WrapStatus.Unsupported, reason);

        public string ToWrapped(string expr) => Prefix(m_ToWrappedPrefix) + expr;

        public string ToUnity(string expr) => Prefix(m_ToUnityPrefix) + expr;

        private string Prefix(string prefix) => Status == WrapStatus.Wrapped ? prefix : throw new InvalidOperationException("Conversion is not usable: " + Status + " (" + Reason + ")");
    }
}
