namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    /// <summary>Emits the wrapper for a type classified as Wrapped. Implemented in Task 5.</summary>
    public interface ITypeGenerator
    {
        void Emit(Type t, CodeWriter w, GenerationContext ctx);
    }

    public interface ITypeRule
    {
        string Name { get; }
        bool Match(Type t, GenerationContext ctx);
        ITypeGenerator Generator { get; }
    }
}
