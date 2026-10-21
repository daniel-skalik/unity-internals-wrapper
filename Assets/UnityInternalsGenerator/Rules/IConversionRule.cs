namespace DandaUtils.Editor.UnityInternalsGenerator.Rules
{
    using System;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;

    public interface IConversionRule
    {
        string Name { get; }
        bool Match(Type t, GenerationContext ctx);
        Conversion Create(Type t, GenerationContext ctx);
    }
}
