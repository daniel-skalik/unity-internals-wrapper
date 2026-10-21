namespace DandaUtils.Editor.UnityInternalsGenerator.Emit
{
    using System;
    using System.Collections.Generic;

    /// <summary>Line-based C# writer. Lines written inside <see cref="Member"/> scopes are attributed to the innermost id.</summary>
    public sealed class CodeWriter
    {
        private const string IndentUnit = "    ";

        private readonly List<string> m_Lines = new List<string>();
        private readonly List<string> m_LineOwners = new List<string>();
        private readonly List<string> m_MemberScopes = new List<string>();
        private string m_Indent = string.Empty;

        public void Line(string text = "")
        {
            var value = text ?? string.Empty;
            foreach (var part in value.Split('\n'))
            {
                m_Lines.Add(part.Length == 0 ? string.Empty : m_Indent + part);
                m_LineOwners.Add(CurrentOwner());
            }
        }

        /// <summary>Writes header and "{", indents the body by 4 spaces, and writes "}" on dispose.</summary>
        public IDisposable Block(string header)
        {
            Line(header);
            Line("{");
            m_Indent += IndentUnit;
            return new Scope(() =>
            {
                m_Indent = m_Indent.Substring(IndentUnit.Length);
                Line("}");
            });
        }

        /// <summary>Attributes lines written inside the scope to <paramref name="memberId"/>. Nestable; innermost wins.</summary>
        public IDisposable Member(string memberId)
        {
            if (memberId == null) throw new ArgumentNullException(nameof(memberId));

            m_MemberScopes.Add(memberId);
            return new Scope(() => m_MemberScopes.RemoveAt(m_MemberScopes.Count - 1));
        }

        /// <summary>Trailing empty lines are dropped so Content ends with exactly one "\n".</summary>
        public GeneratedFile ToFile(string relativePath)
        {
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));

            var count = m_Lines.Count;
            while (count > 0 && m_Lines[count - 1].Length == 0) count--;
            if (count == 0) return new GeneratedFile(relativePath, string.Empty, new string[0]);

            var content = string.Join("\n", m_Lines.GetRange(0, count)) + "\n";
            return new GeneratedFile(relativePath, content, m_LineOwners.GetRange(0, count));
        }

        private string CurrentOwner() => m_MemberScopes.Count == 0 ? null : m_MemberScopes[m_MemberScopes.Count - 1];

        private sealed class Scope : IDisposable
        {
            private readonly Action m_OnDispose;
            private bool m_Disposed;

            public Scope(Action onDispose)
            {
                m_OnDispose = onDispose;
            }

            public void Dispose()
            {
                if (m_Disposed) return;

                m_Disposed = true;
                m_OnDispose();
            }
        }
    }
}
