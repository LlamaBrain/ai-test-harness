#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
#nullable enable
// AthRemoteOptions — opt-in configuration for the exe remote console.
// Parse() is PURE (no Environment access) so it can be unit-tested outside
// Unity; FromEnvironment() is the thin runtime wrapper that feeds it the real
// command line + env and publishes the result as Current for command code.
//
// The PLAYER reads only launch args / env / defaults. Editor authoring
// defaults live in ProjectSettings/AthRemote.json and are resolved by the
// Node launcher, which passes them in as args/env — they never reach here.

using System;

namespace LlamaBrainLabs.Ath.RemoteConsole
{
    public sealed class AthRemoteOptions
    {
        public const int DefaultPort       = 8787;
        public const int DefaultDeadlineMs = 2000;

        public bool   Enabled;
        public int    Port               = DefaultPort;
        public string? MediaDir;            // null => server resolves persistentDataPath
        public int    ResponseDeadlineMs = DefaultDeadlineMs;

        /// <summary>The parsed options for this process (set by FromEnvironment).</summary>
        public static AthRemoteOptions? Current { get; private set; }

        /// <summary>Pure parse. <paramref name="env"/> returns null for unset keys.</summary>
        public static AthRemoteOptions Parse(string[]? args, Func<string, string?>? env)
        {
            args ??= Array.Empty<string>();
            env  ??= (_ => null);

            return new AthRemoteOptions
            {
                Enabled = HasFlag(args, "-ath-remote-console")
                          || IsTruthy(env("ATH_REMOTE_CONSOLE")),
                Port = FirstInt(ArgValue(args, "-ath-port"),  env("ATH_REMOTE_PORT"),       DefaultPort),
                MediaDir = FirstNonEmpty(ArgValue(args, "-ath-media-dir"), env("ATH_REMOTE_MEDIA_DIR")),
                ResponseDeadlineMs = FirstInt(ArgValue(args, "-ath-timeout-ms"), env("ATH_REMOTE_TIMEOUT_MS"), DefaultDeadlineMs),
            };
        }

        public static AthRemoteOptions FromEnvironment()
        {
            Current = Parse(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable);
            return Current;
        }

        // ---- pure helpers (tokenizes both `-flag value` and `-flag=value`) ----
        private static bool HasFlag(string[] args, string flag)
        {
            foreach (var a in args)
                if (a == flag || a.StartsWith(flag + "=", StringComparison.Ordinal)) return true;
            return false;
        }

        private static string? ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a == flag) return i + 1 < args.Length ? args[i + 1] : "";
                if (a.StartsWith(flag + "=", StringComparison.Ordinal)) return a.Substring(flag.Length + 1);
            }
            return null;
        }

        private static bool IsTruthy(string? v)
            => !string.IsNullOrEmpty(v) && (v == "1" || v!.Equals("true", StringComparison.OrdinalIgnoreCase));

        private static int FirstInt(string? a, string? b, int dflt)
        {
            if (int.TryParse(a, out var x)) return x;
            if (int.TryParse(b, out var y)) return y;
            return dflt;
        }

        private static string? FirstNonEmpty(params string?[] vals)
        {
            foreach (var v in vals) if (!string.IsNullOrEmpty(v)) return v;
            return null;
        }
    }
}
#endif
