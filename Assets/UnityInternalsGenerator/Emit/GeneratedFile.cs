namespace DandaUtils.Editor.UnityInternalsGenerator.Emit
{
    using System;
    using System.Collections.Generic;

    /// <summary>Immutable generated source file. Line numbers are 1-based over <see cref="Content"/> lines.</summary>
    public sealed class GeneratedFile
    {
        private readonly string[] m_LineOwners;

        public GeneratedFile(string relativePath, string content, IReadOnlyList<string> lineOwners)
        {
            if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (lineOwners == null) throw new ArgumentNullException(nameof(lineOwners));

            RelativePath = relativePath;
            Content = content;
            m_LineOwners = new string[lineOwners.Count];
            for (var i = 0; i < m_LineOwners.Length; i++) m_LineOwners[i] = lineOwners[i];
        }

        public string RelativePath { get; }

        public string Content { get; }

        /// <summary>Innermost member id covering the line, or null when the line lies outside any member scope.</summary>
        public string MemberAt(int line1Based)
        {
            if (line1Based < 1 || line1Based > m_LineOwners.Length) return null;
            return m_LineOwners[line1Based - 1];
        }
    }
}
