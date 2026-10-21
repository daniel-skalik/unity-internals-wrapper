namespace DandaUtils.Editor.UnityInternalsGenerator.Model
{
    public enum WrapStatus
    {
        Wrapped,
        Deferred,
        Unsupported,
        Excluded,
        NotAccessible,
        NoBridgeAccess,
        NameCollision,
        CompileFailed,

        /// <summary>A selection rule id that does not exist in the current Unity (report-only).</summary>
        SelectedMissing,
    }
}
