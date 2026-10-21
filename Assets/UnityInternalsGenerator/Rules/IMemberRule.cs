namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    /// <summary>Emits the forwarding wrapper of one member kind. Emit is only called for members <see cref="MemberPlanner"/> classified as Wrapped.</summary>
    public interface IMemberRule
    {
        string Name { get; }
        bool Match(MemberInfo m, GenerationContext ctx);
        void Emit(MemberInfo m, CodeWriter w, GenerationContext ctx);
    }
}
