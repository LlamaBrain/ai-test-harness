using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LlamaBrainLabs.Ath;

internal static class Program
{
    private static int _passed;
    private static async Task Main()
    {
        Check("generic host needs no gameplay model", () =>
        {
            using var host = new MinimalHost();
            Equal("minimal", host.HostName);
            Equal(true, host.TryGetState("inventory.slots", out var value));
            Equal("3", value);
            Equal("unsupported", host.Execute("player.kill", "{}", "op").Error);
            Equal(0, host.GetCapabilities().commands.Length);
        });
        Check("legacy named events also reach generic observers", () =>
        {
            var events = new AthHostEvents();
            var names = new List<string>();
            var deaths = 0;
            events.Observed += (name, payload) => names.Add(name + ":" + payload);
            events.PlayerDied += () => deaths++;
            events.RaisePlayerDied();
            events.Raise("quest.finished", "chapter-1");
            Equal(1, deaths);
            Equal("player_died:", names[0]);
            Equal("quest.finished:chapter-1", names[1]);
        });
        Check("sticky events survive reads and clear on reset", () =>
        {
            var store = new AthObservationStore();
            Equal(false, store.HasEvent("quest.finished"));
            store.Observe("quest.finished", "ok");
            Equal(true, store.HasEvent("quest.finished"));
            Equal(true, store.HasEvent("quest.finished"));
            store.ResetEvents();
            Equal(false, store.HasEvent("quest.finished"));
        });
        await CheckAsync("async failure is completed but unsuccessful", async () =>
        {
            var store = new AthObservationStore();
            await store.TrackAsync("load", "failed", Task.FromException(new Exception("broken")));
            Equal(false, store.FindAsync("failed")!.Value.Success);
            Equal("broken", store.FindAsync("failed")!.Value.Error);
        });
        await CheckAsync("lifecycle fences late completions", async () =>
        {
            var store = new AthObservationStore();
            var source = new TaskCompletionSource<bool>();
            var tracker = store.TrackAsync("old-world", "old", source.Task);
            store.BeginSession();
            source.SetResult(true);
            await tracker;
            Equal(true, store.FindAsync("old") == null);
        });
        await CheckAsync("reset events preserves completed async records", async () =>
        {
            var store = new AthObservationStore();
            await store.TrackAsync("load", "current", Task.CompletedTask);
            store.ResetEvents();
            Equal(true, store.FindAsync("current")!.Value.Success);
        });
        await CheckAsync("async ring is bounded and concurrent readers are safe", async () =>
        {
            var store = new AthObservationStore();
            var jobs = new List<Task>();
            for (var i = 0; i < 100; i++)
            {
                var id = i.ToString();
                jobs.Add(Task.Run(async () =>
                {
                    await store.TrackAsync("job", id, Task.CompletedTask);
                    store.FindAsync(id);
                    store.LastAsync();
                }));
            }
            await Task.WhenAll(jobs);
            Equal(16, store.AsyncCount);
            store.BeginSession();
            Equal(0, store.AsyncCount);
        });
        Check("invalid event names are rejected", () =>
        {
            Throws<ArgumentException>(() => new AthHostEvents().Raise(""));
            Throws<ArgumentException>(() => new AthHostEvents().Raise("line\nbreak"));
        });
        Check("registration owns replacement and isolates old events", () =>
        {
            var registration = new AthHostRegistration();
            var first = new MinimalHost();
            var second = new MinimalHost();
            registration.Register(first);
            first.Events.Raise("ready");
            Equal(true, registration.Observations.HasEvent("ready"));
            registration.Register(first);
            Equal(0, first.Disposed);
            registration.Register(second);
            Equal(1, first.Disposed);
            first.Events.Raise("stale");
            Equal(false, registration.Observations.HasEvent("stale"));
            Equal(false, registration.Observations.HasEvent("ready"));
            second.Events.Raise("current");
            Equal(true, registration.Observations.HasEvent("current"));
            registration.Unregister();
            registration.Unregister();
            Equal(1, second.Disposed);
            Equal(true, registration.Host == null);
        });
        Console.WriteLine($"PASS {_passed} engine-independent C# contract checks");
    }

    private static void Check(string name, Action action)
    {
        action();
        Console.WriteLine("PASS " + name);
        _passed++;
    }
    private static async Task CheckAsync(string name, Func<Task> action)
    {
        await action();
        Console.WriteLine("PASS " + name);
        _passed++;
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected {expected}; actual {actual}");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class MinimalHost : AthHostBase
    {
        public int Disposed { get; private set; }
        public override void Dispose() { Disposed++; }
        public override string HostName => "minimal";
        public override bool GameReady => true;
        public override bool TryGetState(string key, out string value)
        {
            value = key == "inventory.slots" ? "3" : "";
            return key == "inventory.slots";
        }
    }
}
