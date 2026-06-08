// EditMode unit tests for AthTraceEmitter.EnsureGitignore — idempotent-append
// behavior over a throwaway temp project root. Pure System.IO; no Editor.
// Constraint-model asserts compile under both Unity NUnit 3.5 and NUnit 4.x.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LlamaBrainLabs.Ath.Editor.McpSkills;
using NUnit.Framework;

namespace LlamaBrainLabs.Ath.Editor.Tests
{
    [TestFixture]
    public class AthTraceGitignoreTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ath-gi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* best-effort cleanup */ }
        }

        string GitignorePath =>
            Path.Combine(_root, AthTraceEmitter.StateDirName, ".gitignore");

        [Test]
        public void EnsureGitignore_FreshDir_WritesBothLines()
        {
            AthTraceEmitter.EnsureGitignore(_root);

            var set = ToLineSet(File.ReadAllText(GitignorePath));
            Assert.That(set, Does.Contain("trace/"));
            Assert.That(set, Does.Contain("side-store/"));
        }

        [Test]
        public void EnsureGitignore_PreexistingMissingTrace_NoTrailingNewline_Appends()
        {
            var stateDir = Path.Combine(_root, AthTraceEmitter.StateDirName);
            Directory.CreateDirectory(stateDir);
            // custom content + side-store/ but NO trace/ and NO trailing newline.
            File.WriteAllText(GitignorePath, "# custom\nside-store/", new UTF8Encoding(false));

            AthTraceEmitter.EnsureGitignore(_root);

            var text = File.ReadAllText(GitignorePath);
            Assert.That(text, Does.Contain("# custom"));                   // preserved verbatim
            Assert.That(text, Does.Not.Contain("side-store/trace/"),       // clean line boundary
                "missing trailing newline must be added before appending");
            var set = ToLineSet(text);
            Assert.That(set, Does.Contain("trace/"));
            Assert.That(set, Does.Contain("side-store/"));
        }

        [Test]
        public void EnsureGitignore_CommentedTraceDoesNotCount_AppendsRealLine()
        {
            var stateDir = Path.Combine(_root, AthTraceEmitter.StateDirName);
            Directory.CreateDirectory(stateDir);
            File.WriteAllText(GitignorePath, "# trace/\n", new UTF8Encoding(false));

            AthTraceEmitter.EnsureGitignore(_root);

            var set = ToLineSet(File.ReadAllText(GitignorePath));
            Assert.That(set, Does.Contain("trace/"),
                "exact-line match: a comment must not satisfy the requirement");
        }

        [Test]
        public void EnsureGitignore_Idempotent_NoDuplicateOnSecondCall()
        {
            AthTraceEmitter.EnsureGitignore(_root);
            var first = File.ReadAllText(GitignorePath);
            AthTraceEmitter.EnsureGitignore(_root);
            var second = File.ReadAllText(GitignorePath);
            Assert.That(second, Is.EqualTo(first));
        }

        static HashSet<string> ToLineSet(string text)
        {
            var set = new HashSet<string>();
            foreach (var raw in text.Split('\n'))
                set.Add(raw.TrimEnd('\r').Trim());
            return set;
        }
    }
}
