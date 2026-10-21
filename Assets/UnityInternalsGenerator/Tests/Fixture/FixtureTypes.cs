namespace UnityInternalsFixture
{
    public static class FixtureMarker { }
    internal enum Color8 : byte { Red = 1, Green = 2 }
    [System.Flags] internal enum Mask { None = 0, A = 1, B = 2 }
    internal static class StaticApi
    {
        internal const int Answer = 42;
        internal const string Greeting = "hi \"there\"";
        internal static int Counter;
        internal static readonly string Name = "fixture";
        internal static Color8 Current { get; set; } = Color8.Red;
        internal static int Add(int a, int b = 2) => a + b;
        internal static double Scale(double v, double factor = 1.5) => v * factor;
        internal static Color8 Next(Color8 c, Mask m = Mask.A | Mask.B) => c == Color8.Red ? Color8.Green : Color8.Red;
        internal static void Reset() => Counter = 0;
        internal static void WithRef(ref int x) { }        // Deferred (ref/out, step 3)
        internal static T Echo<T>(T x) => x;                // Deferred (generic method, step 3)
        internal static Holder MakeHolder() => new Holder(); // Deferred (wrapper conversion, step 2)
        [System.Obsolete("gone", true)] internal static void Dead() { } // Excluded ObsoleteError
        private static void Hidden() { }                    // NotAccessible
        internal enum Nested { X, Y }
        private enum HiddenKind { H }
        internal enum Signed { Neg = -1, Zero = 0 }
        internal static void WithSigned(Signed s = Signed.Neg) { }
        internal static void WithNullableMask(Mask? m = Mask.A) { }
        internal static void WithList(System.Collections.Generic.List<int> l) { }
        [System.Obsolete("old")] internal static void Old() { }   // Wrapped, obsolete attribute copied
        internal static void WithDate([System.Runtime.InteropServices.Optional, System.Runtime.CompilerServices.DateTimeConstant(0)] System.DateTime d) { } // Wrapped, default dropped
        internal static event System.Action Changed { add { } remove { } } // Deferred (event, step 3)
        internal static string Where([System.Runtime.CompilerServices.CallerMemberName] string caller = null) => caller;
        internal static int Overload(int x) => x;          // overloads are not collisions
        internal static int Overload(string s) => 0;
    }
    internal class Holder
    {
        internal static int StaticValue => 7;            // Wrapped
        internal int InstanceValue => 1;                 // Deferred (instance members, step 2)
        public static Holder operator +(Holder a, Holder b) => a; // Deferred (operator, step 3)
    }
    internal struct Point { }               // Deferred (step 2)
    internal interface IThing { }           // Deferred (step 2)
    internal delegate void Callback();      // Deferred (step 3)
    internal class Generic<T> { }           // Unsupported GenericType
    internal static class Collide { internal static int value; internal static int Value; } // PascalCase -> NameCollision
    internal static class CaseTwin { internal static int A; }   // file path collision with Casetwin (case-insensitive file systems)
    internal static class Casetwin { internal static int B; }
    internal static class UsesNested { internal static StaticApi.Nested Pick() => StaticApi.Nested.X; } // needs nested enum of an unselected class
    internal static class Clash { internal static int clash; } // PascalCase -> NameCollision with enclosing type name
}
namespace UnityInternalsFixture
{
    public static class PublicHost { internal enum InnerMode { A } }
    public static class PublicApi { internal static void Hidden() { } public static void Visible() { } } // public type with an internal member: Deferred, lists Hidden only
    internal static class Lambdas { internal static System.Func<int, int> Increment = x => x + 1; } // compiler-generated <>c
}
