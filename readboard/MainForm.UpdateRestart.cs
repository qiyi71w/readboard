using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace readboard
{
    public partial class MainForm
    {
        private bool updateReadySent;
        private bool updateUiReady;
        private bool updateProtocolReady;
        private bool updateResultPresented;
        private IndependentUpdatePresented pendingUpdatePresentation;

        private void InitializeUpdateRestart()
        {
            if (launchOptions.UpdateTransaction == null || sessionCoordinator is not SyncSessionCoordinator coordinator) return;
            coordinator.UpdateRestart.Connected += operation => InvokeUiHostAction(() => ReceiveUpdateConnected(operation));
        }

        private void NotifyUpdateUiReady()
        {
            if (updateReadySent || !updateUiReady || !updateProtocolReady || launchOptions.UpdateTransaction == null
                || sessionCoordinator is not SyncSessionCoordinator coordinator) return;
            var result = IndependentUpdateRestart.ReadLaunchResult(launchOptions.UpdateTransaction, Path.GetDirectoryName(Environment.ProcessPath));
            updateReadySent = true;
            coordinator.UpdateRestart.Ready(result.OperationId, "v" + AppReleaseVersion.GetCurrentVersion().TrimStart('v'));
        }

        private void ReceiveUpdateConnected(string operation)
        {
            if (!updateReadySent || updateResultPresented || pendingUpdatePresentation != null || isShuttingDown) return;
            string path = launchOptions.UpdateTransaction;
            try
            {
                var request = IndependentUpdateRestart.ReadRequest(path);
                if (operation != request.OperationId) return;
                var adoption = IndependentUpdateRestart.ReadAdoption(path, request);
                var connection = IndependentUpdateRestart.ReadConnection(path, request, adoption);
                var result = IndependentUpdateRestart.ReadLaunchResult(path, Path.GetDirectoryName(Environment.ProcessPath));
                using var current = Process.GetCurrentProcess();
                if (connection.State != "connected" || connection.ProcessId != current.Id
                    || connection.ProcessStartTimeUtc != current.StartTime.ToUniversalTime() || connection.ActualVersion != result.ActualVersion) return;
                pendingUpdatePresentation = new IndependentUpdatePresented
                {
                    OperationId = operation, RegistrationId = adoption.RegistrationId, ProcessId = current.Id,
                    ProcessStartTimeUtc = current.StartTime.ToUniversalTime(), ActualVersion = result.ActualVersion
                };
                webViewUpdateState = new ReadBoardUpdateUiState
                {
                    Open = true, Status = "notice", CloseEnabled = true, ResultOperationId = operation,
                    CurrentVersion = result.ActualVersion,
                    TitleMessage = result.State switch
                    {
                        "installed" => SemanticMessage.Create("IndependentUpdate_installed"),
                        "rolledBack" => SemanticMessage.Create("IndependentUpdate_rolledBack"),
                        "failedUnchanged" => SemanticMessage.Create("IndependentUpdate_failedUnchanged"),
                        _ => throw new InvalidDataException("Installation result is not launchable.")
                    },
                    DetailMessages = new[] { SemanticMessage.CreateWithDiagnostic("IndependentUpdate_connected", result.Diagnostic) }
                };
                PostWebViewState();
            }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }

        private bool AcknowledgeUpdatePresentation(JsonElement payload)
        {
            if (pendingUpdatePresentation == null || updateResultPresented || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("operationId", out var operation) || operation.ValueKind != JsonValueKind.String
                || operation.GetString() != pendingUpdatePresentation.OperationId) return false;
            try
            {
                IndependentUpdateFiles.Publish(Path.Combine(Path.GetDirectoryName(launchOptions.UpdateTransaction), "presented.json"), pendingUpdatePresentation);
                updateResultPresented = true;
                return true;
            }
            catch (IOException) { return false; }
            catch (InvalidDataException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
