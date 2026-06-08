// Exposes the base editor assembly's internals to the optional Recorder
// assembly (LlamaBrainLabs.Ath.Editor.Recorder), which is compiled only when
// com.unity.recorder is installed (the ATH_RECORDER define). This lets the
// Recorder tool reuse AthMediaUtil / AthTraceEmitter without widening those
// editor internals into public package API.

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LlamaBrainLabs.Ath.Editor.Recorder")]
[assembly: InternalsVisibleTo("LlamaBrainLabs.Ath.Editor.Tests")]
