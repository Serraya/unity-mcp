using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Constants;
using UnityEditor;

namespace MCPForUnityTests.Editor.Helpers
{
    public class AssetPathUtilityOfflineTests
    {
        private bool _originalForceRefresh;

        [SetUp]
        public void SetUp()
        {
            _originalForceRefresh = EditorPrefs.GetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, _originalForceRefresh);
        }

        [Test]
        public void ShouldUseUvxOffline_WhenForceRefreshEnabled_ReturnsFalse()
        {
            EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, true);
            Assert.IsFalse(AssetPathUtility.ShouldUseUvxOffline());
        }

        [Test]
        public void ShouldUseUvxOffline_DoesNotThrow()
        {
            EditorPrefs.SetBool(EditorPrefKeys.DevModeForceServerRefresh, false);
            Assert.DoesNotThrow(() => AssetPathUtility.ShouldUseUvxOffline());
        }

        [Test]
        public void GetServerSourceFromPackageDependency_GitPackage_ReturnsPinnedServerSource()
        {
            const string dependency = "https://github.com/Serraya/unity-mcp.git?path=/MCPForUnity#0d42699a72c5f53cc89d8ed3eb7788b95ee3fdeb";

            string result = AssetPathUtility.GetServerSourceFromPackageDependency(dependency);

            Assert.AreEqual(
                "git+https://github.com/Serraya/unity-mcp.git@0d42699a72c5f53cc89d8ed3eb7788b95ee3fdeb#subdirectory=Server",
                result);
        }

        [Test]
        public void GetServerSourceFromPackageDependency_EncodedPackagePath_UsesResolvedRevision()
        {
            const string dependency = "https://github.com/CoplayDev/unity-mcp.git?path=%2FMCPForUnity#beta";
            const string resolvedRevision = "1234567890abcdef1234567890abcdef12345678";

            string result = AssetPathUtility.GetServerSourceFromPackageDependency(dependency, resolvedRevision);

            Assert.AreEqual(
                "git+https://github.com/CoplayDev/unity-mcp.git@1234567890abcdef1234567890abcdef12345678#subdirectory=Server",
                result);
        }

        [Test]
        public void GetServerSourceFromPackageDependency_WithoutResolvedRevision_UsesManifestRevision()
        {
            const string dependency = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#beta";

            string result = AssetPathUtility.GetServerSourceFromPackageDependency(dependency);

            Assert.AreEqual(
                "git+https://github.com/CoplayDev/unity-mcp.git@beta#subdirectory=Server",
                result);
        }

        [TestCase("https://github.com/CoplayDev/unity-mcp.git#beta")]
        [TestCase("https://github.com/CoplayDev/unity-mcp.git?path=/OtherPackage#beta")]
        [TestCase("com.coplaydev.unity-mcp")]
        public void GetServerSourceFromPackageDependency_NonMatchingDependency_ReturnsNull(string dependency)
        {
            Assert.IsNull(AssetPathUtility.GetServerSourceFromPackageDependency(dependency));
        }
    }
}
