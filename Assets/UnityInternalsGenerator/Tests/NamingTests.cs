namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class NamingTests
    {
        private static MethodInfo FixtureMethod(string name)
        {
            var type = typeof(FixtureMarker).Assembly.GetType("UnityInternalsFixture.StaticApi");
            return type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static string Format(string method, int paramIndex, string typeRef, string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var p = FixtureMethod(method).GetParameters()[paramIndex];
                Assert.IsTrue(DefaultValueFormatter.TryFormat(p, typeRef, out var literal));
                return literal;
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void NamespaceMapper_Maps()
        {
            Assert.AreEqual("WrappedUnityEditor", NamespaceMapper.Map("UnityEditor"));
            Assert.AreEqual("WrappedUnityEditor.UIElements", NamespaceMapper.Map("UnityEditor.UIElements"));
            Assert.AreEqual("WrappedGlobal", NamespaceMapper.Map(null));
            Assert.AreEqual("WrappedGlobal", NamespaceMapper.Map(""));
            Assert.AreEqual("WrappedUnity.Foo", NamespaceMapper.Map("Unity.Foo"));
        }

        [Test]
        public void NamingStrategies_Work()
        {
            Assert.AreEqual("SelectedSizeIndex", new PascalCaseNaming().MemberName("selectedSizeIndex"));
            Assert.AreEqual("selectedSizeIndex", new ExactNaming().MemberName("selectedSizeIndex"));
            Assert.IsInstanceOf<PascalCaseNaming>(NamingStrategies.For(NamingStyle.PascalCase));
            Assert.IsInstanceOf<ExactNaming>(NamingStrategies.For(NamingStyle.Exact));
        }

        [Test]
        public void TypeRef_Works()
        {
            Assert.AreEqual("int", CSharpNames.TypeRef(typeof(int)));
            Assert.AreEqual("void", CSharpNames.TypeRef(typeof(void)));
            Assert.AreEqual("string", CSharpNames.TypeRef(typeof(string)));
            Assert.AreEqual("global::System.Collections.Generic.List<string>", CSharpNames.TypeRef(typeof(List<string>)));
            Assert.AreEqual("int[,]", CSharpNames.TypeRef(typeof(int[,])));
            Assert.AreEqual("int[]", CSharpNames.TypeRef(typeof(int[])));
            Assert.AreEqual("global::System.Environment.SpecialFolder", CSharpNames.TypeRef(typeof(Environment.SpecialFolder)));
            Assert.AreEqual("global::System.Collections.Generic.Dictionary<int, string>", CSharpNames.TypeRef(typeof(Dictionary<int, string>)));
        }

        [Test]
        public void TypeRef_UsesOverrideMap()
        {
            string Map(Type t) => t == typeof(string) ? "Wrapped.Str" : null;
            Assert.AreEqual("global::System.Collections.Generic.List<Wrapped.Str>", CSharpNames.TypeRef(typeof(List<string>), Map));
            Assert.AreEqual("Wrapped.Str[]", CSharpNames.TypeRef(typeof(string[]), Map));
        }

        [Test]
        public void Identifier_EscapesKeywords()
        {
            Assert.AreEqual("@object", CSharpNames.Identifier("object"));
            Assert.AreEqual("value", CSharpNames.Identifier("value"));
        }

        [Test]
        public void TypeDeclName_StripsArity()
        {
            Assert.AreEqual("List", CSharpNames.TypeDeclName(typeof(List<int>)));
            Assert.AreEqual("String", CSharpNames.TypeDeclName(typeof(string)));
        }

        [Test]
        public void DefaultValue_Int_SkSk() => Assert.AreEqual("2", Format("Add", 1, "int", "sk-SK"));

        [Test]
        public void DefaultValue_Double_SkSk() => Assert.AreEqual("1.5d", Format("Scale", 1, "double", "sk-SK"));

        [Test]
        public void DefaultValue_Enum() => Assert.AreEqual("(X)3", Format("Next", 1, "X", "sk-SK"));

        [Test]
        public void DefaultValue_NegativeEnum_IsParenthesized() => Assert.AreEqual("(X)(-1)", Format("WithSigned", 0, "X", "sk-SK"));

        [Test]
        public void DefaultValue_NullableEnum_IsCast() => Assert.AreEqual("(X)1", Format("WithNullableMask", 0, "X", "sk-SK"));

        [Test]
        public void DefaultValue_NullableEnum_UsesPassedTypeRef() => Assert.AreEqual("(X?)1", Format("WithNullableMask", 0, "X?", "sk-SK"));

        [Test]
        public void DefaultValue_EscapesLineSeparators()
        {
            var m = typeof(NamingTests).GetMethod(nameof(SeparatorHost), BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsTrue(DefaultValueFormatter.TryFormat(m.GetParameters()[0], "string", out var s));
            Assert.AreEqual("\"a\\u2028b\\u2029\"", s);
            Assert.IsTrue(DefaultValueFormatter.TryFormat(m.GetParameters()[1], "char", out var c));
            Assert.AreEqual("'\\u2028'", c);
        }

        private static void SeparatorHost(string s = "a\u2028b\u2029", char c = '\u2028') { }

        [Test]
        public void DefaultValue_None_ReturnsFalse()
        {
            var p = FixtureMethod("Add").GetParameters()[0];
            Assert.IsFalse(DefaultValueFormatter.TryFormat(p, "int", out _));
        }
    }
}
