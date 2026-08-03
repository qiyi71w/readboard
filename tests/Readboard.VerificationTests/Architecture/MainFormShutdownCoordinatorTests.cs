using System;
using System.Collections.Generic;
using readboard;
using Xunit;

namespace Readboard.VerificationTests.Architecture
{
    public sealed class MainFormShutdownCoordinatorTests
    {
        [Fact]
        public void Execute_StopsOwnersInOrderAndContinuesAfterPersistenceFailure()
        {
            var actions = new RecordingShutdownActions("persist");
            var errors = new List<Exception>();
            var coordinator = new MainFormShutdownCoordinator(actions);

            coordinator.Execute(true, errors.Add);

            Assert.Equal(
                new[]
                {
                    "queue",
                    "pending",
                    "title",
                    "persist",
                    "hooks",
                    "protocol",
                    "bitmap",
                    "coordinator",
                    "webViewUpdate",
                    "close"
                },
                actions.Events);
            Assert.Single(errors);
            Assert.Equal("persist failed", errors[0].Message);
        }

        [Fact]
        public void Execute_RecordsEachFailureAndStillRequestsClose()
        {
            var actions = new RecordingShutdownActions(
                "queue",
                "pending",
                "title",
                "persist");
            var errors = new List<Exception>();
            var coordinator = new MainFormShutdownCoordinator(actions);

            coordinator.Execute(true, errors.Add);

            Assert.Equal(
                new[]
                {
                    "queue",
                    "pending",
                    "title",
                    "persist",
                    "hooks",
                    "protocol",
                    "bitmap",
                    "coordinator",
                    "webViewUpdate",
                    "close"
                },
                actions.Events);
            Assert.Equal(
                new[] { "queue failed", "pending failed", "title failed", "persist failed" },
                errors.ConvertAll(error => error.Message));
        }

        [Fact]
        public void Execute_WithoutPersistenceSkipsOnlyPersistence()
        {
            var actions = new RecordingShutdownActions();
            var errors = new List<Exception>();
            var coordinator = new MainFormShutdownCoordinator(actions);

            coordinator.Execute(false, errors.Add);

            Assert.Equal(
                new[]
                {
                    "queue",
                    "pending",
                    "title",
                    "hooks",
                    "protocol",
                    "bitmap",
                    "coordinator",
                    "webViewUpdate",
                    "close"
                },
                actions.Events);
            Assert.Empty(errors);
        }

        private sealed class RecordingShutdownActions : IMainFormShutdownActions
        {
            private readonly HashSet<string> failures;

            public RecordingShutdownActions(params string[] failures)
            {
                this.failures = new HashSet<string>(failures);
            }

            public List<string> Events { get; } = new List<string>();

            public void StopPlaceRequestQueue() { Record("queue"); }
            public void ClearPendingProtocolCommands() { Record("pending"); }
            public void ResetTitle() { Record("title"); }
            public void PersistConfiguration() { Record("persist"); }
            public void DisposeInputHooks() { Record("hooks"); }
            public void SendShutdownProtocol() { Record("protocol"); }
            public void DisposeBitmap() { Record("bitmap"); }
            public void StopCoordinator() { Record("coordinator"); }
            public void DisposeWebViewUpdateBridge() { Record("webViewUpdate"); }
            public void RequestClose() { Record("close"); }

            private void Record(string step)
            {
                Events.Add(step);
                if (failures.Contains(step))
                    throw new InvalidOperationException(step + " failed");
            }
        }
    }
}
