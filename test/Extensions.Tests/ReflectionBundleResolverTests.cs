// MIT License.

using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WebForms.Extensions.Tests
{
    [TestClass]
    public class ReflectionBundleResolverTests
    {
        [TestMethod]
        public void ReflectionBundleResolverUsesLoadedBundleResolverType()
        {
            var extensionsAssembly = typeof(System.Web.UI.ScriptManager).Assembly;
            var resolverType = extensionsAssembly.GetType("WebForms.Extensions.ReflectionBundleResolver", throwOnError: true);
            Assert.IsNotNull(resolverType);

            var createLoggerMethod = typeof(ReflectionBundleResolverTests)
                .GetMethod(nameof(CreateNullLogger), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(resolverType);
            var logger = createLoggerMethod
                .Invoke(null, null);
            Assert.IsNotNull(logger);

            var resolver = Activator.CreateInstance(resolverType, [logger]);
            Assert.IsNotNull(resolver);

            var isBundleVirtualPath = (bool)resolverType
                .GetMethod("IsBundleVirtualPath", BindingFlags.Instance | BindingFlags.Public)!
                .Invoke(resolver, ["/scripts/asp-form.js"])!;
            var bundleUrl = (string)resolverType
                .GetMethod("GetBundleUrl", BindingFlags.Instance | BindingFlags.Public)!
                .Invoke(resolver, ["/scripts/asp-form.js"])!;
            var bundleContents = ((IEnumerable<string>)resolverType
                .GetMethod("GetBundleContents", BindingFlags.Instance | BindingFlags.Public)!
                .Invoke(resolver, ["/scripts/asp-form.js"])!).ToArray();

            Assert.IsTrue(isBundleVirtualPath);
            Assert.AreEqual("/scripts/asp-form.js?v=test", bundleUrl);
            CollectionAssert.AreEqual(new[] { "Focus.js", "WebForms.js" }, bundleContents);
        }

        private static object CreateNullLogger<T>()
            => NullLogger<T>.Instance;
    }
}

namespace System.Web.Optimization
{
    public static class BundleResolver
    {
        public static TestBundleResolver Current { get; } = new();

        public sealed class TestBundleResolver
        {
            public bool IsBundleVirtualPath(string virtualPath)
                => string.Equals(virtualPath, "/scripts/asp-form.js", StringComparison.Ordinal);

            public IEnumerable<string> GetBundleContents(string virtualPath)
                => ["Focus.js", "WebForms.js"];

            public string GetBundleUrl(string virtualPath)
                => virtualPath + "?v=test";
        }
    }
}
