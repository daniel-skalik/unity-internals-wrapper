namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using DandaUtils.Editor.UnityInternalsGenerator.Model;
    using DandaUtils.Editor.UnityInternalsGenerator.Naming;
    using DandaUtils.Editor.UnityInternalsGenerator.Pipeline;
    using NUnit.Framework;
    using UnityInternalsFixture;

    /// <summary>
    /// Compiles the fixture wrappers with the Roslyn bundled in the editor, loads the assembly and exercises the runtime behaviour of
    /// step 2. The fixture grants InternalsVisibleTo to the host name used here, so the generated code reaches the internals.
    /// </summary>
    public sealed class WrapperRuntimeTests
    {
        private const string c_Host = "Wrapped.Fixture.Host";
        private const BindingFlags c_Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private const BindingFlags c_Ctor = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static Assembly s_Wrappers;
        private static Assembly s_Fixture;

        [OneTimeSetUp]
        public void CompileAndLoadWrappers()
        {
            s_Fixture = typeof(FixtureMarker).Assembly;
            var input = new GenerationInput(new[] { s_Fixture }, c_Host, NamingStyle.Exact, null, "test");
            var files = WrapperGenerator.Generate(input).Files;
            var dir = Path.Combine(Path.GetTempPath(), "UnityInternalsGeneratorRuntime", Guid.NewGuid().ToString("N")).Replace('\\', '/');

            var diagnostics = new RoslynProcessCompiler(dir).Compile(files, c_Host, CompilerReferences.Collect(input.Sources));

            Assert.AreEqual(0, diagnostics.Count(d => d.IsError), string.Join("\n", diagnostics.Where(d => d.IsError).Select(d => d.File + "(" + d.Line + "): " + d.Code + ": " + d.Message)));
            s_Wrappers = Assembly.LoadFrom(dir + "/" + c_Host + ".dll");
        }

        private static Type Wrapper(string name) => s_Wrappers.GetType("WrappedUnityInternalsFixture." + name, true);

        private static Type Fixture(string name) => s_Fixture.GetType("UnityInternalsFixture." + name, true);

        private static object Unity(string name, params object[] args) => Activator.CreateInstance(Fixture(name), c_Ctor, null, args, null);

        /// <summary>Calls the wrapper's own internal Wrap factory (declared-only: a derived wrapper also inherits the base one).</summary>
        private static object Wrap(string name, object value) => Wrapper(name).GetMethod("Wrap", c_Declared).Invoke(null, new[] { value });

        private static MethodInfo Method(string wrapper, string name) => Wrapper(wrapper).GetMethod(name, c_Declared);

        private static PropertyInfo Property(string wrapper, string name) => Wrapper(wrapper).GetProperty(name, c_Declared);

        /// <summary>The wrapper's indexer with exactly these index parameter types (a wrapper can declare several).</summary>
        private static PropertyInfo Indexer(string wrapper, params Type[] parameterTypes) =>
            Wrapper(wrapper).GetProperties(c_Declared).Single(p => p.GetIndexParameters().Select(i => i.ParameterType).SequenceEqual(parameterTypes));

        [Test]
        public void ClassWrapper_WrapNull_IsNull()
        {
            Assert.IsNull(Wrap("Branch", null));
            Assert.IsNull(Wrap("IThing", null));
        }

        [Test]
        public void ClassWrapper_SameUnderlyingInstance_EqualityAndHashCodeAgree()
        {
            var branch = Unity("Branch");
            var a = Wrap("Branch", branch);
            var b = Wrap("Branch", branch);
            var other = Wrap("Branch", Unity("Branch"));

            Assert.AreNotSame(a, b);
            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(other));
            Assert.IsTrue((bool)Method("Branch", "op_Equality").Invoke(null, new[] { a, b }));
            Assert.IsFalse((bool)Method("Branch", "op_Inequality").Invoke(null, new[] { a, b }));
            Assert.IsFalse((bool)Method("Branch", "op_Equality").Invoke(null, new[] { a, other }));
        }

        [Test]
        public void ClassWrapper_NullComparison_ReadsLikeTheOriginalReference()
        {
            var equality = Method("Branch", "op_Equality");
            Assert.IsTrue((bool)equality.Invoke(null, new object[] { null, null }));
            Assert.IsFalse((bool)equality.Invoke(null, new[] { Wrap("Branch", Unity("Branch")), null }));
            Assert.IsFalse((bool)equality.Invoke(null, new[] { null, Wrap("Branch", Unity("Branch")) }));
        }

        [Test]
        public void Registry_IsTheSingleEntryPoint_AndReturnsTheMostDerivedWrapper()
        {
            var wrap = s_Wrappers.GetType("WrappedRuntime.WrapperRegistry", true).GetMethod("Wrap", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(wrap);

            Assert.IsNull(wrap.Invoke(null, new object[] { null }));
            Assert.AreEqual(Wrapper("Branch"), wrap.Invoke(null, new[] { Unity("Branch") }).GetType());
            Assert.AreEqual(Wrapper("Twig"), wrap.Invoke(null, new[] { Unity("Twig", 1) }).GetType());
            Assert.AreEqual(Wrapper("Leaf"), wrap.Invoke(null, new[] { Unity("Leaf") }).GetType());

            // Wrapping through the base wrapper's factory also yields the most-derived wrapper, so 'as'/'is' behave.
            Assert.AreEqual(Wrapper("Leaf"), Wrap("Branch", Unity("Leaf")).GetType());
            Assert.IsTrue(Wrapper("Branch").IsInstanceOfType(Wrap("Branch", Unity("Leaf"))));
        }

        [Test]
        public void Hierarchy_MirrorsTheInternalChain_UpToTheFirstPublicBase()
        {
            Assert.AreEqual(Wrapper("Twig"), Wrapper("Leaf").BaseType);
            Assert.AreEqual(Wrapper("Branch"), Wrapper("Twig").BaseType);
            Assert.AreEqual(typeof(object), Wrapper("Branch").BaseType);

            Assert.AreEqual(Fixture("Branch"), Wrapper("Branch").GetField("m_Value", c_Declared).FieldType);
            Assert.AreEqual(Fixture("Twig"), Wrapper("Twig").GetField("m_Value", c_Declared).FieldType);
            Assert.IsTrue(Wrapper("Branch").GetField("m_Value", c_Declared).IsAssembly);
        }

        [Test]
        public void Hierarchy_TargetAndImplicitConversion_ReachThePublicBase()
        {
            var branch = Unity("Branch");
            var wrapper = Wrap("Branch", branch);
            var target = Property("Branch", "Target");

            Assert.AreEqual(Fixture("FixtureNode"), target.PropertyType);
            Assert.AreSame(branch, target.GetValue(wrapper));

            var conversion = Wrapper("Branch").GetMethods(c_Declared).Single(m => m.Name == "op_Implicit");
            Assert.AreEqual(Fixture("FixtureNode"), conversion.ReturnType);
            Assert.AreSame(branch, conversion.Invoke(null, new[] { wrapper }));

            // A wrapper whose first public base is object gets neither.
            Assert.IsNull(Property("Holder", "Target"));
            Assert.IsEmpty(Wrapper("Holder").GetMethods(c_Declared).Where(m => m.Name == "op_Implicit").ToList());
        }

        [Test]
        public void StructWrapper_HasValueCopySemantics_AndNeedsNoBoxing()
        {
            var point = Wrapper("Point");
            var x = Property("Point", "X");
            Assert.IsTrue(point.IsValueType);

            // An array element assignment copies the value, so mutating the copy must not reach the original.
            var original = Wrap("Point", Unity("Point", 4));
            var array = Array.CreateInstance(point, 1);
            array.SetValue(original, 0);
            var copy = array.GetValue(0);
            x.SetValue(copy, 9);

            Assert.AreEqual(9, x.GetValue(copy));
            Assert.AreEqual(4, x.GetValue(original));
            Assert.AreEqual(4, x.GetValue(array.GetValue(0)));
            Assert.AreEqual(8, Method("Point", "Shift").Invoke(original, new object[] { 4 }));

            // Passing the wrapper needs no boxing: the generated signature is the struct itself.
            var shift = Method("StaticApi", "Shift");
            Assert.AreEqual(point, shift.GetParameters()[0].ParameterType);
            Assert.AreEqual(point, shift.ReturnType);
            Assert.AreEqual(6, x.GetValue(shift.Invoke(null, new[] { original, (object)2 })));
        }

        [Test]
        public void InterfaceWrapper_ForwardsToTheInterfaceValue()
        {
            var thing = Method("StaticApi", "MakeThing").Invoke(null, null);

            Assert.AreEqual(Wrapper("IThing"), thing.GetType());
            Assert.AreEqual(5, Property("IThing", "Value").GetValue(thing));
            Assert.AreEqual("thing", Method("IThing", "Describe").Invoke(thing, null));
        }

        [Test]
        public void Constructor_Indexer_AndInstanceMembers()
        {
            var wrapper = Activator.CreateInstance(Wrapper("Branch"), new object[] { 3 });

            Assert.AreEqual(3, Property("Branch", "Size").GetValue(wrapper));
            Assert.AreEqual(6, Method("Branch", "Twice").Invoke(wrapper, null));
            Assert.AreEqual(5, Indexer("Branch", typeof(int)).GetValue(wrapper, new object[] { 2 }));
            Assert.AreEqual("branch", Method("Branch", "Describe").Invoke(wrapper, null));

            Property("Branch", "Label").SetValue(wrapper, "leafy");
            Assert.AreEqual("leafy", Property("Branch", "Label").GetValue(wrapper));

            Property("Branch", "Size").SetValue(wrapper, 10);
            Assert.AreEqual(20, Method("Branch", "Twice").Invoke(wrapper, null));
        }

        [Test]
        public void ConstructorAndIndexer_TakingAWrappedType_CrossTheBoundary()
        {
            var point = Wrap("Point", Unity("Point", 9));
            var branch = Activator.CreateInstance(Wrapper("Branch"), new[] { point });

            Assert.AreEqual(9, Property("Point", "X").GetValue(Property("Branch", "Corner").GetValue(branch)));
            Assert.AreEqual(9, Indexer("Grid", Wrapper("Point")).GetValue(Activator.CreateInstance(Wrapper("Grid")), new[] { point }));
        }

        [Test]
        public void DerivedWrapper_HidesTheBaseMember_AndKeepsInheritedOnes()
        {
            var wrapper = Activator.CreateInstance(Wrapper("Twig"), new object[] { 2 });

            Assert.AreEqual("twig", Method("Twig", "Describe").Invoke(wrapper, null));
            Assert.AreEqual("twig", Method("Branch", "Describe").Invoke(wrapper, null)); // same virtual call underneath
            Assert.AreEqual(4, Method("Branch", "Twice").Invoke(wrapper, null));
        }

        [Test]
        public void WrappedReturnType_WrapsTheMostDerivedWrapper()
        {
            var deepest = Method("StaticApi", "Deepest");

            Assert.AreEqual(Wrapper("Branch"), deepest.ReturnType);
            Assert.AreEqual(Wrapper("Leaf"), deepest.Invoke(null, null).GetType());
        }

        [Test]
        public void StructTypedMember_CrossesTheBoundary()
        {
            var wrapper = Activator.CreateInstance(Wrapper("Branch"), new object[] { 1 });
            var corner = Property("Branch", "Corner");
            var x = Property("Point", "X");

            corner.SetValue(wrapper, Wrap("Point", Unity("Point", 7)));

            Assert.AreEqual(7, x.GetValue(corner.GetValue(wrapper)));
        }
    }
}
