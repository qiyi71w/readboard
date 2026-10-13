using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace readboard
{
    internal sealed class IndependentUpdateRegistration
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RegistrationId { get; set; }
        public int HostProcessId { get; set; }
        public DateTime HostProcessStartTimeUtc { get; set; }
        public int SourceProcessId { get; set; }
        public DateTime SourceProcessStartTimeUtc { get; set; }
        public string InstallDirectory { get; set; }
        public string RequestSha256 { get; set; }
    }

    internal sealed class IndependentUpdateAdoption
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RegistrationId { get; set; }
        public int? HostProcessId { get; set; }
        public DateTime? HostProcessStartTimeUtc { get; set; }
    }

    internal sealed class IndependentUpdateConnection
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RegistrationId { get; set; }
        public string State { get; set; }
        public int? ProcessId { get; set; }
        public DateTime? ProcessStartTimeUtc { get; set; }
        public string ActualVersion { get; set; }
    }

    internal sealed class IndependentUpdatePresented
    {
        public int SchemaVersion { get; set; } = 1;
        public string OperationId { get; set; }
        public string RegistrationId { get; set; }
        public int ProcessId { get; set; }
        public DateTime ProcessStartTimeUtc { get; set; }
        public string ActualVersion { get; set; }
    }

    internal static class IndependentUpdateRestart
    {
        internal static IndependentUpdateRequest ReadRequest(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new InvalidDataException("Invalid transaction path.");
            var request = IndependentUpdateFiles.Read<IndependentUpdateRequest>(path);
            IndependentUpdateFiles.ValidateRequest(request);
            if (!string.Equals(Path.GetFullPath(path), Path.Combine(IndependentUpdateFiles.OperationDirectory(
                request.InstallDirectory, request.OperationId), "request.json"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Transaction path does not match installation.");
            return request;
        }

        internal static IndependentUpdateRegistration ReadRegistration(string path, IndependentUpdateRequest request)
        {
            var registration = IndependentUpdateFiles.Read<IndependentUpdateRegistration>(Path.Combine(Path.GetDirectoryName(path), "registration.json"));
            IndependentUpdateFiles.ValidateOperationId(registration.RegistrationId);
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (registration.SchemaVersion != 1 || registration.OperationId != request.OperationId
                || registration.SourceProcessId != request.SourceProcessId
                || registration.SourceProcessStartTimeUtc != request.SourceProcessStartTimeUtc
                || !string.Equals(registration.InstallDirectory, request.InstallDirectory, StringComparison.OrdinalIgnoreCase)
                || registration.RequestSha256 != hash || registration.HostProcessId <= 0
                || registration.HostProcessStartTimeUtc.Kind != DateTimeKind.Utc)
                throw new InvalidDataException("Restart registration identity mismatch.");
            return registration;
        }

        internal static IndependentUpdateAdoption ReadAdoption(string path, IndependentUpdateRequest request)
        {
            var adoption = IndependentUpdateFiles.Read<IndependentUpdateAdoption>(Path.Combine(Path.GetDirectoryName(path), "adoption.json"));
            if (adoption.SchemaVersion != 1 || adoption.OperationId != request.OperationId)
                throw new InvalidDataException("Invalid restart adoption.");
            if (adoption.RegistrationId == null)
            {
                if (adoption.HostProcessId != null || adoption.HostProcessStartTimeUtc != null)
                    throw new InvalidDataException("Unadopted host identity.");
            }
            else
            {
                var registration = ReadRegistration(path, request);
                if (adoption.RegistrationId != registration.RegistrationId || adoption.HostProcessId != registration.HostProcessId
                    || adoption.HostProcessStartTimeUtc != registration.HostProcessStartTimeUtc)
                    throw new InvalidDataException("Adoption does not match host registration.");
            }
            return adoption;
        }

        internal static bool IsAlive(int pid, DateTime start)
        {
            try { using var process = Process.GetProcessById(pid); return !process.HasExited && process.StartTime.ToUniversalTime() == start; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        internal static IndependentUpdateResult ReadLaunchResult(string path, string install)
        {
            var request = ReadRequest(path);
            var result = IndependentUpdateFiles.Read<IndependentUpdateResult>(Path.Combine(Path.GetDirectoryName(path), "result.json"));
            if (result.SchemaVersion != 1 || result.OperationId != request.OperationId
                || !string.Equals(result.InstallDirectory, request.InstallDirectory, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(IndependentUpdateFiles.CanonicalDirectory(install), request.InstallDirectory, StringComparison.OrdinalIgnoreCase)
                || (result.State != "installed" && result.State != "rolledBack" && result.State != "failedUnchanged")
                || result.ActualVersion != (result.State == "installed" ? request.TargetTag : "v" + request.SourceVersion.TrimStart('v'))
                || result.ActualVersion != IndependentUpdateFiles.ReadVersion(install))
                throw new InvalidDataException("Installation is not a matching launchable result.");
            return result;
        }

        internal static IndependentUpdateConnection ReadConnection(string path, IndependentUpdateRequest request, IndependentUpdateAdoption adoption)
        {
            var connection = IndependentUpdateFiles.Read<IndependentUpdateConnection>(Path.Combine(Path.GetDirectoryName(path), "connection.json"));
            if (connection.SchemaVersion != 1 || connection.OperationId != request.OperationId
                || adoption.RegistrationId == null || connection.RegistrationId != adoption.RegistrationId
                || !new[] { "connected", "unavailable", "hostExited", "startFailed", "handshakeFailed" }.AsSpan().Contains(connection.State))
                throw new InvalidDataException("Invalid restart result.");
            if (connection.State == "connected" && (!connection.ProcessId.HasValue || connection.ProcessId <= 0 || connection.ProcessStartTimeUtc?.Kind != DateTimeKind.Utc
                || !Regex.IsMatch(connection.ActualVersion ?? "", @"^v[0-9]+\.[0-9]+\.[0-9]+$")))
                throw new InvalidDataException("Missing connected process identity.");
            return connection;
        }
    }
}
