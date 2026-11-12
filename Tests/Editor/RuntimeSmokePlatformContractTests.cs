using System;
using System.Linq;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    /// <summary>
    /// Initial red contract tests for the host-neutral RuntimeSmoke package surface.
    /// Reflection keeps this fixture compilable before the planned API is added.
    /// </summary>
    public sealed class RuntimeSmokePlatformContractTests
    {
        private static Type RuntimeSmokeType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LlamaBrainLabs.Ath.RuntimeSmokes." + name, false))
                .FirstOrDefault(type => type != null);
        }

        [Test]
        public void RuntimeSmokeCompiler_ExistsInHostNeutralRuntimeAssembly()
        {
            var compiler = RuntimeSmokeType("RuntimeSmokeDocumentCompiler");

            Assert.That(compiler, Is.Not.Null,
                "RuntimeSmoke needs a data-driven compiler in the reusable runtime assembly.");
            Assert.That(compiler.Assembly.GetName().Name, Is.EqualTo("LlamaBrainLabs.Ath.Runtime"),
                "Document expansion and validation must not depend on Editor, MCP, or host assemblies.");
        }

        [Test]
        public void RuntimeSmokeContract_ExposesTypedTargetModeAndAssertionEvaluator()
        {
            Assert.That(RuntimeSmokeType("RuntimeSmokeTarget"), Is.Not.Null,
                "RuntimeSmoke targets must be an explicit capability policy.");
            Assert.That(RuntimeSmokeType("RuntimeSmokeMode"), Is.Not.Null,
                "FSD, AI-observed, and HITL execution modes must be explicit.");
            Assert.That(RuntimeSmokeType("RuntimeSmokeAssertionEvaluator"), Is.Not.Null,
                "Structured typed assertions must provide the RuntimeSmoke pass authority.");
        }

        [Test]
        public void RuntimeSmokeContract_ProvidesVersionedSchemasAndSuiteValidation()
        {
            Assert.That(RuntimeSmokeType("RuntimeSmokeAssertionSchema"), Is.Not.Null,
                "Assertion evidence requires a versioned schema.");
            Assert.That(RuntimeSmokeType("RuntimeSmokeSuiteValidator"), Is.Not.Null,
                "Build suites need validation that rejects non-FSD workflows.");
        }
    }
}
