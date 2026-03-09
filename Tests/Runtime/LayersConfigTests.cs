using NUnit.Framework;
using System;
using Layers.Unity;

namespace Layers.Unity.Tests
{
    [TestFixture]
    public class LayersConfigTests
    {
        // ── Default Values ──────────────────────────────────────────────

        [Test]
        public void DefaultEnvironment_IsProduction()
        {
            var config = new LayersConfig();
            Assert.AreEqual(LayersEnvironment.Production, config.Environment);
        }

        [Test]
        public void DefaultFlushIntervalMs_Is30000()
        {
            var config = new LayersConfig();
            Assert.AreEqual(30000, config.FlushIntervalMs);
        }

        [Test]
        public void DefaultFlushThreshold_Is20()
        {
            var config = new LayersConfig();
            Assert.AreEqual(20, config.FlushThreshold);
        }

        [Test]
        public void DefaultMaxQueueSize_Is10000()
        {
            var config = new LayersConfig();
            Assert.AreEqual(10000, config.MaxQueueSize);
        }

        [Test]
        public void DefaultMaxBatchSize_Is20()
        {
            var config = new LayersConfig();
            Assert.AreEqual(20, config.MaxBatchSize);
        }

        [Test]
        public void DefaultAutoTrackAppOpen_IsTrue()
        {
            var config = new LayersConfig();
            Assert.IsTrue(config.AutoTrackAppOpen);
        }

        [Test]
        public void DefaultAutoTrackDeepLinks_IsTrue()
        {
            var config = new LayersConfig();
            Assert.IsTrue(config.AutoTrackDeepLinks);
        }

        [Test]
        public void DefaultEnableDebug_IsFalse()
        {
            var config = new LayersConfig();
            Assert.IsFalse(config.EnableDebug);
        }

        [Test]
        public void DefaultBaseUrl_IsNull()
        {
            var config = new LayersConfig();
            Assert.IsNull(config.BaseUrl);
        }

        [Test]
        public void DefaultAppId_IsNull()
        {
            var config = new LayersConfig();
            Assert.IsNull(config.AppId);
        }

        // ── Custom Values ───────────────────────────────────────────────

        [Test]
        public void SetAppId_IsRetained()
        {
            var config = new LayersConfig { AppId = "my-app-id" };
            Assert.AreEqual("my-app-id", config.AppId);
        }

        [Test]
        public void SetEnvironment_Development()
        {
            var config = new LayersConfig
            {
                Environment = LayersEnvironment.Development
            };
            Assert.AreEqual(LayersEnvironment.Development, config.Environment);
        }

        [Test]
        public void SetEnvironment_Staging()
        {
            var config = new LayersConfig
            {
                Environment = LayersEnvironment.Staging
            };
            Assert.AreEqual(LayersEnvironment.Staging, config.Environment);
        }

        [Test]
        public void SetBaseUrl_IsRetained()
        {
            var config = new LayersConfig { BaseUrl = "http://localhost:3333" };
            Assert.AreEqual("http://localhost:3333", config.BaseUrl);
        }

        [Test]
        public void SetCustomFlushInterval_IsRetained()
        {
            var config = new LayersConfig { FlushIntervalMs = 5000 };
            Assert.AreEqual(5000, config.FlushIntervalMs);
        }

        [Test]
        public void SetCustomMaxQueueSize_IsRetained()
        {
            var config = new LayersConfig { MaxQueueSize = 500 };
            Assert.AreEqual(500, config.MaxQueueSize);
        }

        // ── Validation (in Layers.Initialize) ───────────────────────────

        [Test]
        public void Initialize_NullConfig_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => Layers.Initialize(null));
        }

        [Test]
        public void Initialize_NullAppId_ThrowsArgumentException()
        {
            var config = new LayersConfig { AppId = null };
            Assert.Throws<ArgumentException>(() => Layers.Initialize(config));
        }

        [Test]
        public void Initialize_EmptyAppId_ThrowsArgumentException()
        {
            var config = new LayersConfig { AppId = "" };
            Assert.Throws<ArgumentException>(() => Layers.Initialize(config));
        }

        // ── Environment Enum ────────────────────────────────────────────

        [Test]
        public void EnvironmentEnum_HasThreeValues()
        {
            var values = Enum.GetValues(typeof(LayersEnvironment));
            Assert.AreEqual(3, values.Length);
        }

        [Test]
        public void EnvironmentEnum_ContainsDevelopment()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(LayersEnvironment), "Development"));
        }

        [Test]
        public void EnvironmentEnum_ContainsStaging()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(LayersEnvironment), "Staging"));
        }

        [Test]
        public void EnvironmentEnum_ContainsProduction()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(LayersEnvironment), "Production"));
        }
    }
}
