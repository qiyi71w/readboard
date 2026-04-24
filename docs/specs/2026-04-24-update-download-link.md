# Update Download Link Handling

## Scope

The update dialog download button opens the latest GitHub release page returned by the update checker.

## Contract

- `GitHubUpdateChecker` uses GitHub release `html_url` as `UpdateCheckResult.ReleaseUrl`.
- `FormUpdate` accepts only absolute `http` and `https` download URLs before attempting to open them.
- `FormUpdate` opens accepted URLs through the Windows shell with `ProcessStartInfo.UseShellExecute = true`, so the user's default browser handles the link.
- Any `Process` object returned by `Process.Start` is disposed immediately after the shell launch request is made.
- Shell launch failures are shown to the user with the localized generic download-open failure message and recorded with `Trace.TraceError` for diagnostics.

## Out of Scope

- In-app download, installer execution, and auto-update are not implemented.
- Non-HTTP schemes are intentionally rejected.
