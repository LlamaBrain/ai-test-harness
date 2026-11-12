using LlamaBrainLabs.Ath;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System;
using System.Text;
using System.IO;
using LlamaBrainLabs.Ath.Commands;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    public class AthHostCompatibilityTests
    {
        [TearDown] public void TearDown() => AthServices.Unregister();

        [TestCase("{broken}")]
        [TestCase("{} {}")]
        [TestCase("[]")]
        public void MalformedArgumentsNeverReachRegisteredHost(string json)
        {
            var host = new NeutralHost();
            AthServices.RegisterHost(host);
            AthGenericCommands.Execute("custom.echo", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), "bad-json");
            Assert.That(host.Executions, Is.Zero);
        }

        [TestCase("{}")]
        [TestCase("{\"text\":\"hello\",\"count\":42}")]
        [TestCase("{\"text\":\"caf\u00e9 \\\"quoted\\\"\"}")]
        public void ValidArgumentsPreserveJsonAndCorrelation(string json)
        {
            var host = new NeutralHost();
            AthServices.RegisterHost(host);
            AthGenericCommands.Execute("custom.echo", Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), "valid-json");
            Assert.That(host.Executions, Is.EqualTo(1));
            Assert.That(host.LastArguments, Is.EqualTo(json));
            Assert.That(host.LastCorrelationId, Is.EqualTo("valid-json"));
        }

        [Test]
        public void CapabilityDiscoveryDoesNotModifyHostOwnedMetadata()
        {
            var host = new NeutralHost();
            AthServices.RegisterHost(host);
            var metadata = AthGenericCommands.GetCapabilities();
            Assert.That(metadata.engine, Is.EqualTo("unity"));
            Assert.That(host.GetCapabilities().engine, Is.EqualTo("host"));
            CollectionAssert.AreEqual(new[] { "custom.echo" }, host.GetCapabilities().commands);
        }

        [UnityTest]
        public IEnumerator ReplacementClearsReadinessAndRejectedDuplicatePreservesObservations()
        {
            yield return new EnterPlayMode();
            var adapter = new LegacyAdapter();
            AthServices.Register(adapter);
            adapter.Events.RaiseGameReady();
            var host = new NeutralHost { Ready = false };
            AthServices.RegisterHost(host);
            Assert.That(Read("game_ready"), Is.EqualTo("false"));
            host.Events.Raise("keep.event");
            var duplicate = new GameObject("Duplicate ATH bridge");
            duplicate.AddComponent<AthBridge>();
            yield return null;
            Assert.That(Read("event:keep.event"), Is.EqualTo("true"));
            UnityEngine.Object.Destroy(AthBridge.Instance.gameObject);
            yield return null;
            Assert.That(host.Disposed, Is.EqualTo(1), "Destroying the bridge releases the ATH-owned host");
            host.Events.Raise("late.event");
            Assert.That(AthServices.Observations.HasEvent("late.event"), Is.False);
            // ExitPlayMode reloads the test coroutine's managed objects. A temporary
            // marker observes the actual live host's disposal across that boundary.
            var marker = Path.Combine(Path.GetTempPath(), "ath-dispose-" + Guid.NewGuid().ToString("N"));
            new GameObject("ATH session teardown test").AddComponent<AthBridge>();
            AthServices.RegisterHost(new NeutralHost { DisposeMarkerPath = marker });
            yield return new ExitPlayMode();
            try { Assert.That(File.Exists(marker), Is.True, "Ending PlayMode disposes the live owned host"); }
            finally { if (File.Exists(marker)) File.Delete(marker); }
        }

        [Test]
        public void LegacyRegistrationRetainsGameplayAndCustomState()
        {
            var adapter = new LegacyAdapter();
            AthServices.Register(adapter);
            Assert.That(AthServices.Adapter, Is.SameAs(adapter));
            Assert.That(Read("player_alive"), Is.EqualTo("true"));
            Assert.That(Read("player_position"), Is.EqualTo("1.0000,2.0000,3.0000"));
            Assert.That(Read("custom.counter"), Is.EqualTo("7"));
            adapter.Events.RaisePlayerDied();
            Assert.That(Read("event:player_died"), Is.EqualTo("true"));
            AthServices.Observations.ResetEvents();
            Assert.That(Read("event:player_died"), Is.EqualTo("false"));
            AthServices.Register(adapter);
            Assert.That(adapter.Disposed, Is.Zero);
        }

        [Test]
        public void NeutralHostReplacesLegacyWithoutGameplayRequirements()
        {
            var adapter = new LegacyAdapter();
            var host = new NeutralHost();
            AthServices.Register(adapter);
            AthServices.RegisterHost(host);
            Assert.That(adapter.Disposed, Is.EqualTo(1));
            Assert.That(AthServices.Adapter, Is.Null);
            Assert.That(Read("host_name"), Is.EqualTo("neutral"));
            Assert.That(Read("custom.counter"), Is.EqualTo("42"));
            adapter.Events.Raise("old.event");
            Assert.That(Read("event:old.event"), Is.EqualTo("false"));
            host.Events.Raise("new.event");
            Assert.That(Read("event:new.event"), Is.EqualTo("true"));
            AthServices.Unregister();
            AthServices.Unregister();
            Assert.That(host.Disposed, Is.EqualTo(1));
        }

        private static string Read(string key)
        {
            AthStateDispatcher.Resolve(key, out var status, out var value, out _, out _, out _);
            Assert.That(status, Is.EqualTo("ok"));
            return value;
        }

        private sealed class NeutralHost : AthHostBase
        {
            public int Disposed;
            public int Executions;
            public string LastArguments;
            public string LastCorrelationId;
            public string DisposeMarkerPath;
            public bool Ready = true;
            private readonly AthHostCapabilities _capabilities = new AthHostCapabilities
            { engine = "host", commands = new[] { "custom.echo" } };
            public override AthHostCapabilities GetCapabilities() => _capabilities;
            public override AthCommandResult Execute(string command, string argumentsJson, string correlationId)
            { Executions++; LastArguments = argumentsJson; LastCorrelationId = correlationId; return AthCommandResult.Ok(); }
            public override string HostName => "neutral";
            public override bool GameReady => Ready;
            public override bool TryGetState(string key, out string value)
            { value = "42"; return key == "custom.counter"; }
            public override void Dispose()
            {
                Disposed++;
                if (!string.IsNullOrEmpty(DisposeMarkerPath)) File.WriteAllText(DisposeMarkerPath, "disposed");
            }
        }

        private sealed class LegacyAdapter : IAthHostAdapter
        {
            public int Disposed;
            public string HostName => "legacy";
            public string ActiveSceneName => "legacy-scene";
            public bool InGameplayScene => true;
            public bool GameReady => true;
            public bool IsValid => true;
            public bool PlayerAlive => true;
            public bool PlayerHoldingItem => false;
            public Vector3 PlayerPosition => new Vector3(1, 2, 3);
            public Vector3 PlayerVelocity => Vector3.zero;
            public int SpawnAttempts => 0;
            public bool IsPaused => false;
            public bool GhostActive => false;
            public bool GhostItemActive => false;
            public int LastRunRecordingFrameCount => 0;
            public int LastItemRecordingFrameCount => 0;
            public AthHostEvents Events { get; } = new AthHostEvents();
            public bool TryGetCustomState(string key, out string value)
            { value = "7"; return key == "custom.counter"; }
            public void RequestRestart() { }
            public void RequestPause(bool paused) { }
            public void RequestPlayerKill() { }
            public void RequestPlayerRespawn() { }
            public void RequestPlayerTeleport(float x, float y) { }
            public void RequestPlayerSetVelocity(float x, float y) { }
            public void RequestItemConsume() { }
            public void RequestItemTeleport(float x, float y) { }
            public void Dispose() { Disposed++; }
        }
    }
}
