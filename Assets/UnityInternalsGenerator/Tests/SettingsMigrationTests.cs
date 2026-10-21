namespace DandaUtils.Editor.UnityInternalsGenerator.Tests
{
    using System.Collections.Generic;
    using DandaUtils.Editor.UnityInternalsGenerator.Settings;
    using NUnit.Framework;

    public sealed class SettingsMigrationTests
    {
        [Test]
        public void MigrateNamespaces_MovesNamespacesIntoSortedExceptions()
        {
            var namespaces = new List<string> { "UnityEditor.Foo" };
            var ids = new List<string> { "b", "a" };

            var changed = SettingsMigration.MigrateNamespaces(namespaces, ids);

            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(new[] { "a", "b", "ns:UnityEditor.Foo" }, ids);
            Assert.IsEmpty(namespaces);
        }

        [Test]
        public void MigrateNamespaces_SecondCall_ReturnsFalse()
        {
            var namespaces = new List<string> { "UnityEditor.Foo" };
            var ids = new List<string> { "b", "a" };
            SettingsMigration.MigrateNamespaces(namespaces, ids);

            Assert.IsFalse(SettingsMigration.MigrateNamespaces(namespaces, ids));
            CollectionAssert.AreEqual(new[] { "a", "b", "ns:UnityEditor.Foo" }, ids);
        }

        [Test]
        public void MigrateNamespaces_UnsortedDuplicateIds_AreNormalized()
        {
            var namespaces = new List<string>();
            var ids = new List<string> { "b", "a", "b" };

            Assert.IsTrue(SettingsMigration.MigrateNamespaces(namespaces, ids));
            CollectionAssert.AreEqual(new[] { "a", "b" }, ids);
        }

        [Test]
        public void MigrateNamespaces_AlreadyNormalized_ReturnsFalse()
        {
            var ids = new List<string> { "a", "b" };

            Assert.IsFalse(SettingsMigration.MigrateNamespaces(new List<string>(), ids));
        }

        [Test]
        public void Normalize_DropsEmptyDuplicatesAndSortsOrdinal()
        {
            var result = SettingsMigration.Normalize(new[] { "b", "", "a", "b", null, "B" });

            CollectionAssert.AreEqual(new[] { "B", "a", "b" }, result);
        }
    }
}
