#if UNITY_EDITOR || DEVELOPMENT_BUILD || SMOKE_HARNESS
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace LlamaBrainLabs.Ath.RuntimeSmokes
{
    /// <summary>Claims and executes the one FSD workflow requested by the outer AITH process runner.</summary>
    internal sealed class RuntimeSmokePlayerBootstrap : MonoBehaviour
    {
        private const float RegistryReadyDeadlineSeconds = 30f;
        private RuntimeSmokePlayerLaunchOptions _options;
        private float _startedAt;
        private Task<RuntimeSmokeRegistrySnapshot> _completion;
        private bool _started;

        // Claim or reject the external launch before the first scene's Awake/OnEnable gameplay code runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            RuntimeSmokePlayerLaunchOptions options;
            try { options = RuntimeSmokePlayerLaunchOptions.FromEnvironment(); }
            catch (Exception exception)
            {
                Debug.LogError("[RuntimeSmoke] launch arguments are invalid: " + exception.Message);
                Application.Quit(2);
                return;
            }
            if (options == null) return;

            RuntimeSmokePlayerProcessIdentity identity;
            try
            {
                identity = RuntimeSmokePlayerProcessIdentity.CaptureCurrent();
            }
            catch (Exception exception)
            {
                Debug.LogError("[RuntimeSmoke] process identity capture failed: " + exception);
                Application.Quit(3);
                return;
            }

            try
            {
                if (!RuntimeSmokeDurableLaunchClaim.TryClaim(options, identity, DateTime.UtcNow, out var armed, out var failure))
                {
                    Debug.LogError("[RuntimeSmoke] launch claim failed: " + failure);
                    Application.Quit(3);
                    return;
                }
                options.SetFixtureIdentity(armed.scenario, armed.seed);
            }
            catch (Exception exception)
            {
                Debug.LogError("[RuntimeSmoke] durable launch claim threw: " + exception);
                Application.Quit(3);
                return;
            }

            var host = new GameObject("[RuntimeSmokePlayerBootstrap]") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(host);
            var bootstrap = host.AddComponent<RuntimeSmokePlayerBootstrap>();
            bootstrap._options = options;
            bootstrap._startedAt = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (!_started)
            {
                var registry = RuntimeSmokeRegistryService.Current;
                if (registry == null)
                {
                    if (Time.realtimeSinceStartup - _startedAt >= RegistryReadyDeadlineSeconds)
                        Fail("host RuntimeSmoke registry did not register before startup deadline.", 4);
                    return;
                }

#if SMOKE_HARNESS
                var target = RuntimeSmokeTarget.SmokeHarness;
#else
                var target = RuntimeSmokeTarget.Development;
#endif
                var started = registry.Start(_options.WorkflowId, _options.CorrelationId, target, RuntimeSmokeMode.Fsd,
                    _options.Scenario, _options.Seed);
                if (!started.Accepted)
                {
                    Fail("workflow could not start: " + started.Status + " " + started.Diagnostic, 5);
                    return;
                }
                _started = true;
                _completion = registry.WaitForCompletionAsync(_options.CorrelationId);
                return;
            }

            if (_completion == null || !_completion.IsCompleted) return;
            try
            {
                var result = _completion.GetAwaiter().GetResult();
                if (result != null && result.State == RuntimeSmokeRegistryRunState.Passed)
                {
                    Debug.Log("[RuntimeSmoke] terminal=passed correlation=" + _options.CorrelationId);
                    Application.Quit(0);
                }
                else Fail("workflow finished without PASS: " + (result == null ? "missing result" : result.State.ToString()), 1);
            }
            catch (Exception exception) { Fail("workflow completion failed: " + exception.Message, 1); }
        }

        private void Fail(string message, int exitCode)
        {
            Debug.LogError("[RuntimeSmoke] " + message);
            Application.Quit(exitCode);
        }
    }
}
#endif
