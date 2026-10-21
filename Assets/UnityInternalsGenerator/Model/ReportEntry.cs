namespace DandaUtils.Editor.UnityInternalsGenerator.Model
{
    public sealed class ReportEntry
    {
        public string Id { get; }
        public string Rule { get; }
        public WrapStatus Status { get; }
        public string Reason { get; }

        /// <summary>True when the selection picks this id, or it is emitted because something selected needs it. Independent of <see cref="Status"/>.</summary>
        public bool Selected { get; }

        public ReportEntry(string id, string rule, WrapStatus status, string reason, bool selected = true)
        {
            Id = id;
            Rule = rule;
            Status = status;
            Reason = reason;
            Selected = selected;
        }
    }
}
