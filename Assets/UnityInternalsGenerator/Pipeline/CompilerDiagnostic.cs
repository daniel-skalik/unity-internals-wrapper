namespace DandaUtils.Editor.UnityInternalsGenerator.Pipeline
{
    /// <summary>One compiler message. <see cref="File"/> is relative to the compile root with '/' separators, or null when the message has no source location.</summary>
    public sealed class CompilerDiagnostic
    {
        public string File { get; }
        public int Line { get; }
        public string Code { get; }
        public string Message { get; }
        public bool IsError { get; }

        public CompilerDiagnostic(string file, int line, string code, string message, bool isError)
        {
            File = file;
            Line = line;
            Code = code;
            Message = message;
            IsError = isError;
        }

        public override string ToString() => (File == null ? "" : File + "(" + Line + "): ") + (IsError ? "error " : "warning ") + Code + ": " + Message;
    }
}
