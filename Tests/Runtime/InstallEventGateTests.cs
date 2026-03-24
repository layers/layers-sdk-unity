using NUnit.Framework;
using System;
using Layers.Unity.Internal;
using UnityEngine;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class InstallEventGateTests
    {
        [SetUp]
        public void SetUp()
        {
            // Clear the install gate keys so each test starts clean
            PlayerPrefs.DeleteKey("layers_first_launch_tracked");
            PlayerPrefs.DeleteKey("layers_install_id");
            PlayerPrefs.Save();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up
            PlayerPrefs.DeleteKey("layers_first_launch_tracked");
            PlayerPrefs.DeleteKey("layers_install_id");
            PlayerPrefs.Save();
        }

        // ── 24-hour Window Constant ───────────────────────────────────────

        [Test]
        public void InstallEventMaxDiffMs_Is24Hours()
        {
            long expected = 24L * 60 * 60 * 1000; // 86400000
            Assert.AreEqual(expected, InstallEventGate.InstallEventMaxDiffMs);
        }

        // ── ShouldTreatAsNewInstall Logic ─────────────────────────────────

        [Test]
        public void ShouldTreatAsNewInstall_NotFirstLaunch_ReturnsFalse()
        {
            // If the flag says this is NOT the first launch, always return false
            bool result = InstallEventGate.ShouldTreatAsNewInstall(false, false);
            Assert.IsFalse(result);
        }

        [Test]
        public void ShouldTreatAsNewInstall_FirstLaunch_WithPriorState_ReturnsTrue()
        {
            // If SDK had prior state (install_id existed before init), trust the flag
            bool result = InstallEventGate.ShouldTreatAsNewInstall(true, true);
            Assert.IsTrue(result, "Should trust first-launch flag when prior SDK state exists");
        }

        [Test]
        public void ShouldTreatAsNewInstall_FirstLaunch_NoPriorState_InEditor_ReturnsTrue()
        {
            // In the editor, GetFirstInstallTimeMs returns 0 (unknown),
            // so the method should default to trusting the flag
            bool result = InstallEventGate.ShouldTreatAsNewInstall(true, false);

            // In editor, install time is unknown (0), so it returns true as safe default
            Assert.IsTrue(result,
                "In editor, unknown install time should default to trusting the flag");
        }

        // ── DetermineIsFirstLaunch Idempotency ───────────────────────────

        [Test]
        public void DetermineIsFirstLaunch_FirstCall_ReturnsTrue()
        {
            // First call with no prior state and no flag set
            // In editor, install time is unknown, so it trusts the flag
            bool result = InstallEventGate.DetermineIsFirstLaunch();
            Assert.IsTrue(result,
                "First call to DetermineIsFirstLaunch should return true");
        }

        [Test]
        public void DetermineIsFirstLaunch_SecondCall_ReturnsFalse()
        {
            // First call sets the flag
            InstallEventGate.DetermineIsFirstLaunch();

            // Second call should see the flag and return false
            bool result = InstallEventGate.DetermineIsFirstLaunch();
            Assert.IsFalse(result,
                "Second call to DetermineIsFirstLaunch should return false");
        }

        [Test]
        public void DetermineIsFirstLaunch_SetsPlayerPrefsFlag()
        {
            InstallEventGate.DetermineIsFirstLaunch();

            int flagValue = PlayerPrefs.GetInt("layers_first_launch_tracked", 0);
            Assert.AreEqual(1, flagValue,
                "DetermineIsFirstLaunch should set the PlayerPrefs flag to 1");
        }

        [Test]
        public void DetermineIsFirstLaunch_WithFlagAlreadySet_ReturnsFalse()
        {
            // Pre-set the flag as if a previous launch already occurred
            PlayerPrefs.SetInt("layers_first_launch_tracked", 1);
            PlayerPrefs.Save();

            bool result = InstallEventGate.DetermineIsFirstLaunch();
            Assert.IsFalse(result,
                "DetermineIsFirstLaunch should return false when flag is already set");
        }
    }
}
