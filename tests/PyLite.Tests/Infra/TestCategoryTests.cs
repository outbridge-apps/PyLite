using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Outbridge.PyLite.Tests.Infra
{
    // Every test class carries exactly one of the five categories TESTING.md describes (MachineSensitive comes
    // on top of one), so `--filter Category=...` slices the whole suite with nothing left over.
    [Trait("Category", "Regression")]
    public class TestCategoryTests
    {
        private static readonly string[] Primary = { "Compatibility", "Sandbox", "ResourceLimits", "Regression", "Fuzz" };

        [Fact]
        public void EveryTestClassHasOnePrimaryCategory()
        {
            var tested = typeof(TestCategoryTests).Assembly.GetTypes()
                .Where(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Any(m => m.IsDefined(typeof(FactAttribute), true)))
                .ToList();
            Assert.True(tested.Count > 200, "found only " + tested.Count + " test classes");
            var wrong = tested
                .Where(t => CategoriesOf(t).Count(c => Primary.Contains(c)) != 1
                    || CategoriesOf(t).Any(c => c != "MachineSensitive" && !Primary.Contains(c)))
                .Select(t => t.FullName)
                .ToList();
            Assert.True(wrong.Count == 0, "test classes without exactly one primary category: " + string.Join(", ", wrong));
        }

        // xUnit 2 keeps a trait's name and value only in the attribute's constructor arguments.
        private static string[] CategoriesOf(Type t)
        {
            return t.GetCustomAttributesData()
                .Where(a => a.AttributeType == typeof(TraitAttribute) && (string)a.ConstructorArguments[0].Value == "Category")
                .Select(a => (string)a.ConstructorArguments[1].Value)
                .ToArray();
        }
    }
}
