namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.Linq;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Rules;
    using DandaUtils.Editor.UnityInternalsGenerator.Selection;
    using NUnit.Framework;
    using UnityInternalsFixture;

    public sealed class ClassificationTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";

        private static readonly Type s_FixtureAssemblyType = typeof(FixtureMarker);

        private static GenerationContext NewContext(string host = c_Host, SelectionRules selection = null)
        {
            var input = new GenerationInput(new[] { s_FixtureAssemblyType.Assembly }, host, NamingStyle.Exact, selection, "test");
            return new GenerationContext(input);
        }

        private static Type Fixture(string name) => s_FixtureAssemblyType.Assembly.GetType("UnityInternalsFixture." + name, true);

        private static void AssertStatus(GenerationContext ctx, string typeName, WrapStatus expected)
        {
            Assert.AreEqual(expected, ctx.Classify(Fixture(typeName)).Status, typeName);
        }

        [Test]
        public void Types_Wrapped()
        {
            var ctx = NewContext();
            foreach (var name in new[] { "Color8", "Mask", "StaticApi", "StaticApi+Nested", "StaticApi+Signed", "Holder", "Collide" })
            {
                AssertStatus(ctx, name, WrapStatus.Wrapped);
            }
        }

        [Test]
        public void Types_Deferred()
        {
            AssertStatus(NewContext(), "Callback", WrapStatus.Deferred);
        }

        [Test]
        public void Types_StructInterfaceAndInstanceClass_Wrapped()
        {
            var ctx = NewContext();
            foreach (var name in new[] { "Point", "IThing", "Thing", "Branch", "Twig", "Leaf", "AbstractThing", "ReservedNames" })
            {
                AssertStatus(ctx, name, WrapStatus.Wrapped);
                Assert.IsTrue(ctx.IsInstanceWrapper(Fixture(name)), name);
            }

            Assert.IsFalse(ctx.IsInstanceWrapper(Fixture("StaticApi")), "static class");
            Assert.IsFalse(ctx.IsInstanceWrapper(Fixture("Mask")), "enum");
        }

        [Test]
        public void WrapperBase_StopsAtFirstPublicBase()
        {
            var ctx = NewContext();
            ctx.ClassifyAll();

            Assert.AreEqual(Fixture("Twig"), ctx.WrapperBase(Fixture("Leaf")));
            Assert.AreEqual(Fixture("Branch"), ctx.WrapperBase(Fixture("Twig")));
            Assert.IsNull(ctx.WrapperBase(Fixture("Branch")));
            Assert.AreEqual(Fixture("FixtureNode"), GenerationContext.PublicBase(Fixture("Branch")));
            Assert.IsNull(GenerationContext.PublicBase(Fixture("Holder")));
        }

        [Test]
        public void Types_RefStruct_Unsupported()
        {
            var refStruct = Fixture("FixtureRefStruct");
            Assert.IsTrue(GenerationContext.IsByRefLike(refStruct));

            var classification = NewContext().Classify(refStruct);
            Assert.AreEqual(WrapStatus.Unsupported, classification.Status);
            Assert.AreEqual("ref struct", classification.Reason);
        }

        [Test]
        public void Types_NestedTypeWithAReservedName_NameCollision()
        {
            var ctx = NewContext();
            var nested = ctx.Classify(Fixture("ReservedNested+Target"));

            Assert.AreEqual(WrapStatus.NameCollision, nested.Status);
            Assert.AreEqual("Name reserved by the wrapper 'Target'", nested.Reason);
            AssertStatus(ctx, "ReservedNested", WrapStatus.Wrapped);
        }

        [Test]
        public void Types_TargetName_ReservedOnlyWhereTheWrapperDeclaresIt()
        {
            var ctx = NewContext();
            ctx.ClassifyAll();

            Assert.IsTrue(ctx.DeclaresTarget(Fixture("ReservedNames")), "public base");
            Assert.IsTrue(ctx.DeclaresTarget(Fixture("Twig")), "inherits the root wrapper Target");
            Assert.IsFalse(ctx.DeclaresTarget(Fixture("Holder")), "base is object");
            Assert.IsFalse(ctx.DeclaresTarget(Fixture("Spot")), "struct wrapper");
            Assert.IsFalse(ctx.DeclaresTarget(Fixture("IThing")), "interface wrapper");

            AssertStatus(ctx, "Unreserved+Target", WrapStatus.Wrapped);
        }

        [Test]
        public void Types_GenericDefinition_Unsupported()
        {
            AssertStatus(NewContext(), "Generic`1", WrapStatus.Unsupported);
        }

        [Test]
        public void Types_HostNotGranted_NoBridgeAccess()
        {
            var ctx = NewContext("Other.Host");
            AssertStatus(ctx, "StaticApi", WrapStatus.NoBridgeAccess);
            AssertStatus(ctx, "Color8", WrapStatus.NoBridgeAccess);
            Assert.IsFalse(ctx.IsInternalCandidate(Fixture("StaticApi")));
        }

        [Test]
        public void Types_Unselected_StayWrapped()
        {
            var exceptMask = NewContext(selection: new SelectionRules(new[] { SelectionIds.Namespace("UnityInternalsFixture") }, new[] { MemberId.Type(Fixture("Mask")) }));
            AssertStatus(exceptMask, "Mask", WrapStatus.Wrapped);
            AssertStatus(exceptMask, "Color8", WrapStatus.Wrapped);

            var nothing = NewContext(selection: SelectionRules.Empty);
            AssertStatus(nothing, "Color8", WrapStatus.Wrapped);
        }

        [Test]
        public void IsInternalCandidate_Rules()
        {
            var ctx = NewContext();
            Assert.IsTrue(ctx.IsInternalCandidate(Fixture("StaticApi")));
            Assert.IsTrue(ctx.IsInternalCandidate(Fixture("StaticApi+Nested")));
            Assert.IsFalse(ctx.IsInternalCandidate(typeof(FixtureMarker)));
        }

        [Test]
        public void IsAccessible_Methods()
        {
            var ctx = NewContext();
            var api = Fixture("StaticApi");
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            Assert.IsTrue(ctx.IsAccessible(api.GetMethod("Add", flags)));
            Assert.IsFalse(ctx.IsAccessible(api.GetMethod("Hidden", flags)));
        }

        [Test]
        public void Report_OncePerType_AndSortedById()
        {
            var ctx = NewContext();
            var api = Fixture("StaticApi");
            ctx.Classify(api);
            ctx.Classify(api);
            ctx.IsWrappedType(api);
            ctx.Classify(Fixture("Point"));
            var forApi = ctx.Entries.Where(e => e.Id == MemberId.Type(api)).ToList();
            Assert.AreEqual(1, forApi.Count);
            Assert.AreEqual(WrapStatus.Wrapped, forApi[0].Status);
            var ids = ctx.Entries.Select(e => e.Id).ToList();
            CollectionAssert.AreEqual(ids.OrderBy(i => i, StringComparer.Ordinal).ToList(), ids);
        }

        [Test]
        public void ClassifyAll_ReturnsWrappedSortedByFullName()
        {
            var ctx = NewContext();
            var names = ctx.ClassifyAll().Select(t => t.FullName).ToList();
            CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
            CollectionAssert.Contains(names, Fixture("StaticApi").FullName);
            CollectionAssert.Contains(names, Fixture("Color8").FullName);
            CollectionAssert.DoesNotContain(names, Fixture("Callback").FullName);
            CollectionAssert.DoesNotContain(names, typeof(FixtureMarker).FullName);
        }

        [Test]
        public void Convert_Passthrough_Int()
        {
            var c = NewContext().Convert(typeof(int));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("int", c.WrappedTypeRef);
            Assert.AreEqual("x", c.ToWrapped("x"));
            Assert.AreEqual("x", c.ToUnity("x"));
        }

        [Test]
        public void Convert_Void_Passthrough()
        {
            Assert.AreEqual("void", NewContext().Convert(typeof(void)).WrappedTypeRef);
        }

        [Test]
        public void Convert_WrappedEnum_Cast()
        {
            var ctx = NewContext();
            var c = ctx.Convert(Fixture("Color8"));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Color8", c.WrappedTypeRef);
            Assert.AreEqual("(global::WrappedUnityInternalsFixture.Color8)e", c.ToWrapped("e"));
            Assert.AreEqual("(global::UnityInternalsFixture.Color8)e", c.ToUnity("e"));
        }

        [Test]
        public void Convert_NestedWrappedEnum_KeepsNesting()
        {
            var c = NewContext().Convert(Fixture("StaticApi+Nested"));
            Assert.AreEqual("global::WrappedUnityInternalsFixture.StaticApi.Nested", c.WrappedTypeRef);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.StaticApi.Signed", NewContext().Convert(Fixture("StaticApi+Signed")).WrappedTypeRef);
        }

        [Test]
        public void WrappedTypeRef_Context_MapsOnlyWrapped()
        {
            var ctx = NewContext();
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Mask", ctx.WrappedTypeRef(Fixture("Mask")));
            Assert.AreEqual("string", ctx.WrappedTypeRef(typeof(string)));
        }

        [Test]
        public void Convert_InstanceClass_WrapsAndUnwraps()
        {
            var c = NewContext().Convert(Fixture("Holder"));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Holder", c.WrappedTypeRef);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Holder.Wrap(x)", c.ToWrapped("x"));
            Assert.AreEqual("(x)?.m_Value", c.ToUnity("x"));
        }

        [Test]
        public void Convert_Struct_UnwrapsWithoutNullCheck()
        {
            var c = NewContext().Convert(Fixture("Point"));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Point.Wrap(x)", c.ToWrapped("x"));
            Assert.AreEqual("(x).m_Value", c.ToUnity("x"));
        }

        [Test]
        public void Convert_Interface_UsesWrapperClass()
        {
            var c = NewContext().Convert(Fixture("IThing"));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.IThing", c.WrappedTypeRef);
        }

        [Test]
        public void Convert_ByRef_Deferred()
        {
            var c = NewContext().Convert(typeof(int).MakeByRefType());
            Assert.AreEqual(WrapStatus.Deferred, c.Status);
            StringAssert.Contains("ref/out", c.Reason);
        }

        [Test]
        public void Convert_Pointer_And_TypedReference_Unsupported()
        {
            var ctx = NewContext();
            Assert.AreEqual(WrapStatus.Unsupported, ctx.Convert(typeof(int).MakePointerType()).Status);
            Assert.AreEqual(WrapStatus.Unsupported, ctx.Convert(typeof(TypedReference)).Status);
            Assert.AreEqual(WrapStatus.Unsupported, ctx.Convert(typeof(Span<int>)).Status);
        }

        [Test]
        public void Convert_WrappedEnum_WhenHostNotGranted_CarriesNoBridgeAccess()
        {
            var c = NewContext("Other.Host").Convert(Fixture("Color8"));
            Assert.AreEqual(WrapStatus.NoBridgeAccess, c.Status);
            Assert.IsNotEmpty(c.Reason);
        }

        [Test]
        public void Convert_NullableOfInternalEnum_Deferred()
        {
            var c = NewContext().Convert(typeof(Nullable<>).MakeGenericType(Fixture("Mask")));
            Assert.AreEqual(WrapStatus.Deferred, c.Status);
            StringAssert.Contains("Nullable", c.Reason);
        }

        [Test]
        public void Convert_ArrayOfInternalEnum_Deferred()
        {
            var c = NewContext().Convert(Fixture("Color8").MakeArrayType());
            Assert.AreEqual(WrapStatus.Deferred, c.Status);
            StringAssert.Contains("Array", c.Reason);
        }

        [Test]
        public void Convert_ListOfInternalEnum_Deferred()
        {
            var c = NewContext().Convert(typeof(System.Collections.Generic.List<>).MakeGenericType(Fixture("Color8")));
            Assert.AreEqual(WrapStatus.Deferred, c.Status);
            StringAssert.Contains("GenericCollection", c.Reason);
        }

        [Test]
        public void Convert_GenericParameter_Deferred()
        {
            var parameter = Fixture("StaticApi").GetMethod("Echo", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetParameters()[0].ParameterType;
            var c = NewContext().Convert(parameter);
            Assert.AreEqual(WrapStatus.Deferred, c.Status);
            StringAssert.Contains("GenericParameter", c.Reason);
        }

        [Test]
        public void Convert_InternalTypeOutsideSources_Unsupported()
        {
            var foreign = typeof(UnityEditor.Editor).Assembly.GetTypes().First(x => x.IsVisible == false && x.IsClass && x.IsGenericType == false && x.IsEnum == false);
            var c = NewContext().Convert(foreign);
            Assert.AreEqual(WrapStatus.Unsupported, c.Status);
            StringAssert.Contains("outside source", c.Reason);
        }

        [Test]
        public void Convert_UnselectedEnum_StillCast()
        {
            var ctx = NewContext(selection: new SelectionRules(Array.Empty<string>(), new[] { MemberId.Type(Fixture("Mask")) }));
            var c = ctx.Convert(Fixture("Mask"));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::WrappedUnityInternalsFixture.Mask", c.WrappedTypeRef);
        }

        [Test]
        public void Types_PrivateNested_NotAccessible()
        {
            var hidden = Fixture("StaticApi").GetNestedType("HiddenKind", System.Reflection.BindingFlags.NonPublic);
            Assert.AreEqual(WrapStatus.NotAccessible, NewContext().Classify(hidden).Status);
        }

        [Test]
        public void Types_InternalNestedInPublicType_Wrapped()
        {
            var inner = Fixture("PublicHost").GetNestedType("InnerMode", System.Reflection.BindingFlags.NonPublic);
            Assert.AreEqual(WrapStatus.Wrapped, NewContext().Classify(inner).Status);
        }

        [Test]
        public void Types_PublicWithInternalMembers_WrappedByTheStep4Rule()
        {
            var ctx = NewContext();
            foreach (var name in new[] { "PublicApi", "PublicHost", "PublicThing", "PublicSpot", "PublicOuter", "PublicDerived", "FixtureNode" })
            {
                AssertStatus(ctx, name, WrapStatus.Wrapped);
                Assert.AreEqual("PublicTypeInternalMembers", ctx.Classify(Fixture(name)).RuleName, name);
            }
        }

        [Test]
        public void Types_PublicWithoutInternals_NotClassifiable()
        {
            var ctx = NewContext();
            Assert.AreEqual(WrapStatus.Unsupported, ctx.Classify(typeof(FixtureMarker)).Status);
            Assert.IsFalse(ctx.IsWrappedType(typeof(FixtureMarker)));
            CollectionAssert.DoesNotContain(ctx.ClassifiedTypes.ToList(), typeof(FixtureMarker));
        }

        /// <summary>A public type keeps passing through signatures as itself, even once it has a wrapper of its own.</summary>
        [Test]
        public void Convert_PublicHost_StaysPassthrough()
        {
            var ctx = NewContext();
            var c = ctx.Convert(Fixture("PublicThing"));

            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::UnityInternalsFixture.PublicThing", c.WrappedTypeRef);
            Assert.AreEqual("x", c.ToWrapped("x"));
            Assert.IsFalse(ctx.NeedsWrapper(Fixture("PublicThing")));
            Assert.IsTrue(ctx.NeedsWrapper(Fixture("Point")));
        }

        [Test]
        public void Types_CompilerGenerated_Excluded()
        {
            var generated = Fixture("Lambdas").GetNestedTypes(System.Reflection.BindingFlags.NonPublic).First(x => x.Name.Contains("<"));
            Assert.AreEqual(WrapStatus.Excluded, NewContext().Classify(generated).Status);
        }

        [Test]
        public void Types_NestedInUnselectedType_StaysWrapped()
        {
            var ctx = NewContext(selection: new SelectionRules(new[] { SelectionIds.Namespace("UnityInternalsFixture") }, new[] { MemberId.Type(Fixture("StaticApi")) }));
            AssertStatus(ctx, "StaticApi", WrapStatus.Wrapped);
            AssertStatus(ctx, "StaticApi+Nested", WrapStatus.Wrapped);
        }

        [Test]
        public void Convert_PublicGeneric_Passthrough()
        {
            var c = NewContext().Convert(typeof(System.Collections.Generic.List<int>));
            Assert.AreEqual(WrapStatus.Wrapped, c.Status);
            Assert.AreEqual("global::System.Collections.Generic.List<int>", c.WrappedTypeRef);
        }

        [Test]
        public void Convert_NotWrapped_ToWrappedThrows()
        {
            var c = NewContext().Convert(Fixture("Callback"));
            Assert.Throws<InvalidOperationException>(() => c.ToWrapped("x"));
            Assert.Throws<InvalidOperationException>(() => c.ToUnity("x"));
        }
    }
}
