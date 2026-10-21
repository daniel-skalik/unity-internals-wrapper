namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    public sealed class TypeClassification
    {
        public WrapStatus Status { get; }
        public string RuleName { get; }
        public string Reason { get; }
        public ITypeGenerator Generator { get; }

        public TypeClassification(WrapStatus status, string ruleName, string reason, ITypeGenerator generator = null)
        {
            Status = status;
            RuleName = ruleName;
            Reason = reason;
            Generator = generator;
        }
    }
}
