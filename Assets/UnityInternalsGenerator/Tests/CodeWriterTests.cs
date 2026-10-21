namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using DandaUtils.Editor.UnityInternalsGenerator.Emit;
    using NUnit.Framework;

    public sealed class CodeWriterTests
    {
        [Test]
        public void Block_WritesHeaderBracesAndFourSpaceIndentation()
        {
            var w = new CodeWriter();
            using (w.Block("namespace N"))
            {
                using (w.Block("class C"))
                {
                    w.Line("int X;");
                }
            }

            Assert.AreEqual("namespace N\n{\n    class C\n    {\n        int X;\n    }\n}\n", w.ToFile("x.cs").Content);
        }

        [Test]
        public void Line_WithoutArgument_WritesEmptyLineWithoutIndentation()
        {
            var w = new CodeWriter();
            using (w.Block("class C"))
            {
                w.Line("int X;");
                w.Line();
                w.Line("int Y;");
            }

            Assert.AreEqual("class C\n{\n    int X;\n\n    int Y;\n}\n", w.ToFile("x.cs").Content);
        }

        [Test]
        public void Line_WithEmbeddedNewline_SplitsIntoIndentedLines()
        {
            var w = new CodeWriter();
            using (w.Block("class C"))
            {
                w.Line("a\nb");
            }

            Assert.AreEqual("class C\n{\n    a\n    b\n}\n", w.ToFile("x.cs").Content);
        }

        [Test]
        public void MemberAt_ReturnsInnermostScopeId_OuterForBetween_NullOutside()
        {
            var w = new CodeWriter();
            w.Line("// header");
            using (w.Member("T"))
            {
                w.Line("line1");
                using (w.Member("T.M"))
                {
                    w.Line("line2");
                }

                w.Line("line3");
            }

            w.Line("tail");
            var f = w.ToFile("a/b.g.cs");

            Assert.IsNull(f.MemberAt(0));
            Assert.IsNull(f.MemberAt(1));
            Assert.AreEqual("T", f.MemberAt(2));
            Assert.AreEqual("T.M", f.MemberAt(3));
            Assert.AreEqual("T", f.MemberAt(4));
            Assert.IsNull(f.MemberAt(5));
            Assert.IsNull(f.MemberAt(6));
        }

        [Test]
        public void MemberAt_BlockHeaderAndBracesInsideMember_AttributedToMember()
        {
            var w = new CodeWriter();
            w.Line("// outside");
            using (w.Member("T"))
            {
                using (w.Block("class C"))
                {
                    w.Line("int X;");
                }
            }

            var f = w.ToFile("a.g.cs");

            Assert.IsNull(f.MemberAt(1));
            Assert.AreEqual("T", f.MemberAt(2));
            Assert.AreEqual("T", f.MemberAt(3));
            Assert.AreEqual("T", f.MemberAt(4));
            Assert.AreEqual("T", f.MemberAt(5));
        }

        [Test]
        public void ToFile_SetsRelativePath_EndsWithSingleNewline_DropsTrailingBlankLines()
        {
            var w = new CodeWriter();
            w.Line("a");
            w.Line();
            w.Line();
            var f = w.ToFile("Dir/Out.g.cs");

            Assert.AreEqual("Dir/Out.g.cs", f.RelativePath);
            Assert.AreEqual("a\n", f.Content);
            Assert.IsNull(f.MemberAt(2));
        }

        [Test]
        public void ToFile_EmptyWriter_HasEmptyContentAndNoLines()
        {
            var f = new CodeWriter().ToFile("e.cs");

            Assert.AreEqual(string.Empty, f.Content);
            Assert.IsNull(f.MemberAt(1));
        }
    }
}
