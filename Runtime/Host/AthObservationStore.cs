#if UNITY_EDITOR || DEVELOPMENT_BUILD || ATH_REMOTE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LlamaBrainLabs.Ath
{
    /// <summary>Bounded thread-safe observations; generations fence old host/world completions.</summary>
    public sealed class AthObservationStore
    {
        private const int AsyncCapacity = 16;
        private const int EventCapacity = 256;
        private readonly object _gate = new object();
        private readonly HashSet<string> _events = new HashSet<string>(StringComparer.Ordinal);
        private readonly LinkedList<AthAsyncOpRecord> _async = new LinkedList<AthAsyncOpRecord>();
        private long _generation;
        public int AsyncCount { get { lock (_gate) return _async.Count; } }
        public void Observe(string name, string payload)
        {
            lock (_gate)
            {
                if (_events.Count < EventCapacity || _events.Contains(name)) _events.Add(name);
            }
        }
        public bool HasEvent(string name) { lock (_gate) return _events.Contains(name); }
        public void ResetEvents() { lock (_gate) _events.Clear(); }
        public void BeginSession()
        {
            lock (_gate) { _generation++; _events.Clear(); _async.Clear(); }
        }
        public AthAsyncOpRecord? FindAsync(string id)
        {
            lock (_gate)
            {
                for (var node = _async.Last; node != null; node = node.Previous)
                    if (node.Value.CorrelationId == id) return node.Value;
                return null;
            }
        }
        public AthAsyncOpRecord? LastAsync()
        {
            lock (_gate) return _async.Last == null ? (AthAsyncOpRecord?)null : _async.Last.Value;
        }
        public async Task TrackAsync(string name, string id, Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            long generation;
            lock (_gate) generation = _generation;
            bool success = true;
            string error = "";
            try { await task.ConfigureAwait(false); }
            catch (Exception ex) { success = false; error = ex.Message; }
            lock (_gate)
            {
                if (generation != _generation) return;
                _async.AddLast(new AthAsyncOpRecord(name, id, success, error));
                while (_async.Count > AsyncCapacity) _async.RemoveFirst();
            }
        }
    }
}
#endif
