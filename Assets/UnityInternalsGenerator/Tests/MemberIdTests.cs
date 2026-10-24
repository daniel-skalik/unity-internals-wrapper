namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class MemberIdTests
    {
        private static System.Type StaticApi => typeof(FixtureMarker).Assembly.GetType("UnityInternalsFixture.StaticApi");

        private const string Asm = "DandaUtils.UnityInternalsGenerator.Tests.Fixture";

        [Test]
        public void Method_Id()
        {
            var add = StaticApi.GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Method|Add(System.Int32,System.Int32)", MemberId.Member(add));
        }

        [Test]
        public void Param_ConstructedGeneric()
        {
            var m = StaticApi.GetMethod("WithList", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Method|WithList(System.Collections.Generic.List`1[System.Int32])", MemberId.Member(m));
        }

        [Test]
        public void Param_ByRef()
        {
            var m = StaticApi.GetMethod("WithRef", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Method|WithRef(System.Int32&)", MemberId.Member(m));
        }

        [Test]
        public void GenericMethod_ArityAndPositionalParam()
        {
            var m = StaticApi.GetMethod("Echo", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Method|Echo``1(!!0)", MemberId.Member(m));
        }

        [Test]
        public void Kinds()
        {
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Const|Answer", MemberId.Member(StaticApi.GetField("Answer", f)));
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Field|Counter", MemberId.Member(StaticApi.GetField("Counter", f)));
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Property|Current", MemberId.Member(StaticApi.GetProperty("Current", f)));
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi|Method|Reset()", MemberId.Member(StaticApi.GetMethod("Reset", f)));
        }

        [Test]
        public void Indexers_Ids_CarryTheirIndexParameters()
        {
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var assembly = typeof(FixtureMarker).Assembly;
            var indexers = assembly.GetType("UnityInternalsFixture.Grid", true).GetProperties(f);
            var point = assembly.GetType("UnityInternalsFixture.Point", true);

            Assert.AreEqual(Asm + "|UnityInternalsFixture.Grid|Property|Item(System.Int32)", MemberId.Member(indexers.Single(p => p.GetIndexParameters()[0].ParameterType == typeof(int))));
            Assert.AreEqual(Asm + "|UnityInternalsFixture.Grid|Property|Item(UnityInternalsFixture.Point)", MemberId.Member(indexers.Single(p => p.GetIndexParameters()[0].ParameterType == point)));
        }

        [Test]
        public void Type_Id_Nested()
        {
            var nested = StaticApi.GetNestedType("Nested", BindingFlags.NonPublic);
            var id = MemberId.Type(nested);
            StringAssert.Contains("StaticApi+Nested", id);
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi+Nested|Type|Nested", id);
        }

        [Test]
        public void DeclaringTypeId_FromMemberIds()
        {
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Static;
            Assert.AreEqual(MemberId.Type(StaticApi), MemberId.DeclaringTypeId(MemberId.Member(StaticApi.GetMethod("Reset", f))));
            Assert.AreEqual(Asm + "|UnityInternalsFixture.StaticApi+Nested|Type|Nested", MemberId.DeclaringTypeId(Asm + "|UnityInternalsFixture.StaticApi+Nested|Field|X"));
            Assert.AreEqual("A|Top|Type|Top", MemberId.DeclaringTypeId("A|Top|Method|M(System.Int32)"));
            Assert.IsNull(MemberId.DeclaringTypeId(MemberId.Type(StaticApi)));
            Assert.IsNull(MemberId.DeclaringTypeId("ns:UnityEditor"));
            Assert.IsNull(MemberId.DeclaringTypeId(null));
        }
    }
}
