using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace readboard
{
    internal sealed partial class ControlCenterRuntime
    {
        private DateTime lastDiagnosticPlayersUtc = DateTime.MinValue;
        private int diagnosticWindowRevision;

        private static LoggingRuntime AutoPlayDiagnosticLog()
        {
            LoggingRuntime logging = Program.CurrentContext == null ? null : Program.CurrentContext.Logging;
            return logging != null && logging.Observe().Diagnostics == LoggingToggle.On ? logging : null;
        }

        private void RecordAutoPlayDiagnostic(string eventId, FoxWindowContext context,
            Dictionary<string, LoggingField> fields)
        {
            LoggingRuntime logging = AutoPlayDiagnosticLog();
            if (logging == null)
                return;
            context = context ?? FoxWindowContext.Unknown();
            fields["windowRevision"] = LoggingField.Safe(diagnosticWindowRevision);
            fields["hwnd"] = LoggingField.Safe(observedWindowHandle.ToInt64().ToString("X"));
            fields["kind"] = LoggingField.Safe(context.Kind.ToString());
            fields["roomState"] = LoggingField.Safe(context.LiveRoomState.ToString());
            fields["room"] = LoggingField.Tagged(context.RoomToken, LoggingPrivacy.UserText);
            fields["platform"] = LoggingField.Safe(preferences.Platform.ToString());
            fields["autoPlayEnabled"] = LoggingField.Safe(sessionState.AutoPlayEnabled);
            fields["selectedMode"] = LoggingField.Safe(sessionState.SelectedAutoPlayColorMode.ToString());
            fields["keepSync"] = LoggingField.Safe(coordinator.KeepSync);
            fields["twoWaySync"] = LoggingField.Safe(preferences.TwoWaySync);
            logging.Write(new LoggingRecord
            {
                Level = LogLevel.Information,
                Stream = LoggingStreams.App,
                EventId = eventId,
                Module = "FoxAutoPlay",
                DiagnosticOnly = true,
                Fields = fields
            });
        }

        private void RecordWindowDiagnostic(ControlCenterWindowFacts facts, FoxWindowContext previous)
        {
            diagnosticWindowRevision++;
            lastDiagnosticPlayersUtc = DateTime.MinValue;
            if (AutoPlayDiagnosticLog() == null)
                return;
            FoxWindowContext next = facts.Context ?? FoxWindowContext.Unknown();
            previous = previous ?? FoxWindowContext.Unknown();
            RecordAutoPlayDiagnostic("fox.autoplay.window", next, new Dictionary<string, LoggingField>
            {
                ["previousHwnd"] = LoggingField.Safe(observedWindowHandle.ToInt64().ToString("X")),
                ["nextHwnd"] = LoggingField.Safe(facts.Handle.ToInt64().ToString("X")),
                ["bindingInvalidated"] = LoggingField.Safe(facts.BindingInvalidated),
                ["previousKind"] = LoggingField.Safe(previous.Kind.ToString()),
                ["previousState"] = LoggingField.Safe(previous.LiveRoomState.ToString()),
                ["roomChanged"] = LoggingField.Safe(!string.Equals(previous.RoomToken, next.RoomToken, StringComparison.Ordinal))
            });
        }

        private void RecordPlayersDiagnostic(FoxWindowContext context, DateTime now,
            string identity, bool sampled, bool force, FoxMatchBarReading reading)
        {
            if (AutoPlayDiagnosticLog() == null)
                return;
            if (!sampled && (now - lastDiagnosticPlayersUtc).TotalMilliseconds < FoxMatchBarLiveRecognition.RetryIntervalMs)
                return;
            lastDiagnosticPlayersUtc = now;
            // Shadow reads are evidence only: never feed them back into liveRecognition or authorization.
            reading = reading ?? environment.ReadPlayers(observedWindowHandle, context) ?? FoxMatchBarReading.Empty;
            AutoPlayColorResolution observed = FoxMatchBarSeatResolver.Resolve(identity, reading.Players);
            RecordAutoPlayDiagnostic("fox.autoplay.players", context, new Dictionary<string, LoggingField>
            {
                ["sampled"] = LoggingField.Safe(sampled),
                ["forced"] = LoggingField.Safe(force),
                ["cacheKnown"] = LoggingField.Safe(liveRecognition.CurrentResolution.IsKnown),
                ["cachedColor"] = LoggingField.Safe(liveRecognition.CurrentResolution.PlayColor),
                ["cachedStatus"] = LoggingField.Safe(liveRecognition.CurrentResolution.Status.ToString()),
                ["observedColor"] = LoggingField.Safe(observed.PlayColor),
                ["observedStatus"] = LoggingField.Safe(observed.Status.ToString()),
                ["playerCount"] = LoggingField.Safe(reading.Players.Count),
                ["identityConfigured"] = LoggingField.Safe(!string.IsNullOrWhiteSpace(identity)),
                ["reader"] = LoggingField.Safe(reading.Diagnostic)
            });
        }

        private void RecordAuthorizationDiagnostic(ControlCenterRuntimeSnapshot snapshot)
        {
            if (AutoPlayDiagnosticLog() == null)
                return;
            RecordAutoPlayDiagnostic("fox.autoplay.request", sessionState.FoxWindowContext,
                new Dictionary<string, LoggingField>
                {
                    ["canSend"] = LoggingField.Safe(snapshot.CanSendAutoPlayCommand(coordinator.KeepSync)),
                    ["color"] = LoggingField.Safe(snapshot.PlayColor),
                    ["status"] = LoggingField.Safe(snapshot.AutoPlayColorResolution == null
                        ? "none" : snapshot.AutoPlayColorResolution.Status.ToString())
                });
        }
    }
}
