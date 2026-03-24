using NUnit.Framework;
using Layers.Unity;
using Layers.Unity.Internal;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class DebugOverlayTests
    {
        [SetUp]
        public void SetUp()
        {
            LayersTestMode.Enable();
            Layers.Initialize(new LayersConfig { AppId = "test-debug" });
        }

        [TearDown]
        public void TearDown()
        {
            Layers.HideDebugOverlay();
            Layers.Shutdown();
            LayersTestMode.Disable();
        }

        // ── Show / Hide State ─────────────────────────────────────────────

        [Test]
        public void IsDebugOverlayVisible_BeforeShow_IsFalse()
        {
            Assert.IsFalse(Layers.IsDebugOverlayVisible);
        }

        [Test]
        public void ShowDebugOverlay_SetsVisibleTrue()
        {
            Layers.ShowDebugOverlay();

            Assert.IsTrue(Layers.IsDebugOverlayVisible);
        }

        [Test]
        public void HideDebugOverlay_SetsVisibleFalse()
        {
            Layers.ShowDebugOverlay();
            Layers.HideDebugOverlay();

            Assert.IsFalse(Layers.IsDebugOverlayVisible);
        }

        [Test]
        public void ShowDebugOverlay_CalledTwice_NoError()
        {
            // Should not throw or create duplicate overlays
            Layers.ShowDebugOverlay();
            Layers.ShowDebugOverlay();

            Assert.IsTrue(Layers.IsDebugOverlayVisible);
        }

        [Test]
        public void HideDebugOverlay_CalledWithoutShow_NoError()
        {
            // Should not throw
            Layers.HideDebugOverlay();

            Assert.IsFalse(Layers.IsDebugOverlayVisible);
        }

        [Test]
        public void HideDebugOverlay_CalledTwice_NoError()
        {
            Layers.ShowDebugOverlay();
            Layers.HideDebugOverlay();
            Layers.HideDebugOverlay();

            Assert.IsFalse(Layers.IsDebugOverlayVisible);
        }
    }
}
