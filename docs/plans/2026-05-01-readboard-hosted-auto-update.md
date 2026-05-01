# readboard Hosted Auto Update Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users start a readboard update from the readboard update dialog while LizzieYzy-Next performs the actual native `readboard/` replacement.

**Architecture:** Keep the process boundary strict: readboard discovers, downloads, preflights, and notifies; LizzieYzy-Next advertises support, confirms with the user, installs, rolls back, and restarts native readboard. Add explicit text protocol messages for capability, update-ready, and host result state, with old-host fallback to the existing release-page flow.

**Tech Stack:** C# WinForms/.NET 10 readboard, xUnit verification tests, Java 17 LizzieYzy-Next, Maven/JUnit 5, GitHub Releases API JSON, Windows filesystem move semantics.

---

## Source Specs And Constraints

- Spec: `docs/specs/2026-05-01-readboard-hosted-auto-update-design.md`
- Existing update behavior: `docs/specs/2026-04-24-update-download-link.md`
- Protocol boundary: `docs/specs/2026-04-23-protocol-keyword-constants.md`
- Host integration boundary: `docs/DEVELOPMENT.md`
- readboard repo: `D:\dev\weiqi\readboard`
- host repo: `D:\dev\weiqi\lizzieyzy-next`

Do not change readboard launch arguments. Do not let readboard replace files under its own app directory. Keep the old browser-download behavior whenever the host has not explicitly sent `readboardUpdateSupported`.

## File Structure

readboard files:

- Modify: `readboard/UpdateCheckResult.cs` - add optional release asset fields.
- Modify: `readboard/UpdateDialogModel.cs` - add hosted-install state and messages.
- Modify: `readboard/GitHubUpdateChecker.cs` - parse release assets and select the matching zip.
- Create: `readboard/HostedUpdatePackageVerifier.cs` - validate zip name, tag, required files, and unsafe entries.
- Create: `readboard/HostedUpdatePackageDownloader.cs` - download asset into `%LOCALAPPDATA%\LizzieYzyNext\readboard-updates\<tag>\`.
- Modify: `readboard/FormUpdate.cs` - choose browser vs hosted install behavior and show progress/results.
- Modify: `readboard/Form1.cs` - pass hosted update data into `FormUpdate`; hold host capability state and handle host result messages.
- Modify: `readboard/MainForm.Protocol.cs` - dispatch hosted update protocol callbacks onto the main form.
- Modify: `readboard/Core/Models/ProtocolMessage.cs` - add inbound hosted update message kinds.
- Modify: `readboard/Core/Protocol/ProtocolKeywords.cs` - add stable wire tokens.
- Modify: `readboard/Core/Protocol/LegacyProtocolAdapter.cs` - parse host capability/result messages and create update-ready message.
- Modify: `readboard/Core/Protocol/IProtocolCommandHost.cs` - expose hosted update command handlers to the coordinator.
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.cs` - route parsed hosted update messages through the existing dispatch boundary.
- Modify tests under `tests/Readboard.VerificationTests/Host` and `tests/Readboard.VerificationTests/Protocol`.
- Modify localization: `readboard/language_cn.txt`, `readboard/language_en.txt`, `readboard/language_jp.txt`, `readboard/language_kr.txt`, and default strings in `readboard/Program.cs`.

LizzieYzy-Next files:

- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoard.java` - parse `readboardUpdateReady`, send `readboardUpdateSupported`, and route update requests.
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardStream.java` only if a test seam is needed for emitted commands.
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\gui\LizzieFrame.java` - own confirmation, serialized install lifecycle, and native restart.
- Create: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardUpdateRequest.java` - parsed update request value object.
- Create: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardUpdateInstaller.java` - zip validation, staging, backup, replace, rollback.
- Create tests under `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis`.
- Modify resources: `D:\dev\weiqi\lizzieyzy-next\src\main\resources\l10n\DisplayStrings*.properties`.

## Task 1: readboard Protocol Contract And Host Capability State

**Files:**
- Modify: `readboard/Core/Models/ProtocolMessage.cs`
- Modify: `readboard/Core/Protocol/ProtocolKeywords.cs`
- Modify: `readboard/Core/Protocol/LegacyProtocolAdapter.cs`
- Modify: `readboard/Core/Protocol/IProtocolCommandHost.cs`
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.cs`
- Modify: `readboard/Form1.cs`
- Modify: `readboard/MainForm.Protocol.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/LegacyProtocolAdapterTests.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/LegacyOutboundProtocolContractTests.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/StartupAndShutdownRegressionTests.cs`

- [ ] **Step 1: Write failing protocol keyword tests**

Add assertions for:

```csharp
Assert.Equal("readboardUpdateSupported", ProtocolKeywords.ReadboardUpdateSupported);
Assert.Equal("readboardUpdateReady\t", ProtocolKeywords.ReadboardUpdateReadyPrefix);
Assert.Equal("readboardUpdateInstalling", ProtocolKeywords.ReadboardUpdateInstalling);
Assert.Equal("readboardUpdateCancelled", ProtocolKeywords.ReadboardUpdateCancelled);
Assert.Equal("readboardUpdateFailed\t", ProtocolKeywords.ReadboardUpdateFailedPrefix);
```

- [ ] **Step 2: Write failing adapter tests**

Cover:

```csharp
adapter.ParseInbound("readboardUpdateSupported").Kind == ProtocolMessageKind.ReadboardUpdateSupported
adapter.ParseInbound("readboardUpdateInstalling").Kind == ProtocolMessageKind.ReadboardUpdateInstalling
adapter.ParseInbound("readboardUpdateCancelled").Kind == ProtocolMessageKind.ReadboardUpdateCancelled
adapter.ParseInbound("readboardUpdateFailed\tbad zip").RawText == "readboardUpdateFailed\tbad zip"
adapter.CreateReadboardUpdateReadyMessage("v3.0.2", @"C:\x\readboard-github-release-v3.0.2.zip").RawText ==
    "readboardUpdateReady\tv3.0.2\tC:\\x\\readboard-github-release-v3.0.2.zip"
```

- [ ] **Step 3: Run tests to verify failure**

Run:

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~LegacyProtocolAdapterTests|FullyQualifiedName~LegacyOutboundProtocolContractTests"
```

Expected: fails because the new enum/message kinds and protocol methods do not exist.

- [ ] **Step 4: Implement minimal protocol model and adapter support**

Add new `ProtocolMessageKind` values if needed. Keep wire strings as `string` constants. Parsing rules:

- exact `readboardUpdateSupported`
- exact `readboardUpdateInstalling`
- exact `readboardUpdateCancelled`
- prefix `readboardUpdateFailed\t`, with the message remaining single-line text
- outbound `readboardUpdateReady\t<tag>\t<absoluteZipPath>`, rejecting empty tag/path and paths containing tab

- [ ] **Step 5: Route inbound update messages through the existing dispatch boundary**

Extend `IProtocolCommandHost` and `SyncSessionCoordinator.CreateDispatchCommand` so parsed messages flow through:

```text
transport -> LegacyProtocolAdapter.ParseInbound -> SyncSessionCoordinator -> IProtocolCommandHost -> MainForm.Protocol.cs -> Form1.cs state
```

Add coordinator tests that feed each inbound line and assert the matching host callback runs through `DispatchProtocolCommand`. Do not bypass the coordinator by handling raw lines in `Form1.cs`.

- [ ] **Step 6: Store host capability and host results on the main form**

Add a private `hostedUpdateSupported` boolean to `Form1.cs`. Set it only when the inbound capability message is received. Do not infer support from launch args, process name, TCP mode, or path.

- [ ] **Step 7: Run targeted tests**

Run:

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~Protocol|FullyQualifiedName~StartupAndShutdownRegressionTests"
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add readboard\Core\Models\ProtocolMessage.cs readboard\Core\Protocol readboard\Form1.cs readboard\MainForm.Protocol.cs tests\Readboard.VerificationTests\Protocol tests\Readboard.VerificationTests\Host
git commit -m "feat(update): add hosted update protocol state"
```

## Task 2: readboard Release Asset Parsing

**Files:**
- Modify: `readboard/UpdateCheckResult.cs`
- Modify: `readboard/GitHubUpdateChecker.cs`
- Test: `tests/Readboard.VerificationTests/Host/GitHubUpdateCheckerTests.cs`

- [ ] **Step 1: Write failing asset parsing tests**

Add JSON with `assets`:

```json
{
  "tag_name": "v3.0.2",
  "html_url": "https://github.com/qiyi71w/readboard/releases/tag/v3.0.2",
  "assets": [
    {
      "name": "readboard-github-release-v3.0.2.zip",
      "browser_download_url": "https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip",
      "size": 12345
    }
  ]
}
```

Assert:

```csharp
Assert.Equal("readboard-github-release-v3.0.2.zip", result.AssetName);
Assert.Equal("https://github.com/qiyi71w/readboard/releases/download/v3.0.2/readboard-github-release-v3.0.2.zip", result.AssetDownloadUrl);
Assert.Equal(12345, result.AssetSize);
```

Also test mismatched asset name, non-HTTPS asset URL, missing assets, and asset filename `readboard-github-release-vv3.0.2.zip` not matching.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~GitHubUpdateCheckerTests"
```

Expected: fails because result asset fields are missing.

- [ ] **Step 3: Implement minimal asset model**

Add nullable properties:

```csharp
public string AssetName { get; internal set; }
public string AssetDownloadUrl { get; internal set; }
public long? AssetSize { get; internal set; }
```

Parse `assets` as `JsonElement` array. Select only exact `readboard-github-release-<tag_name>.zip` with absolute `https` `browser_download_url`. Leave asset fields null if no matching asset exists; keep `ReleaseUrl` populated.

- [ ] **Step 4: Run targeted tests**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~GitHubUpdateCheckerTests"
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add readboard\GitHubUpdateChecker.cs readboard\UpdateCheckResult.cs tests\Readboard.VerificationTests\Host\GitHubUpdateCheckerTests.cs
git commit -m "feat(update): parse GitHub release assets"
```

## Task 3: readboard Package Download And Zip Preflight

**Files:**
- Create: `readboard/HostedUpdatePackageDownloader.cs`
- Create: `readboard/HostedUpdatePackageVerifier.cs`
- Test: `tests/Readboard.VerificationTests/Host/HostedUpdatePackageDownloaderTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/HostedUpdatePackageVerifierTests.cs`

- [ ] **Step 1: Write failing verifier tests**

Create temporary zip files and assert:

- accepts a zip containing `readboard.exe`, `readboard.dll`, `readboard.runtimeconfig.json`, `readboard.deps.json`, `language_cn.txt`
- accepts required files under one top-level directory
- rejects entries with `..`
- rejects absolute path entries
- rejects `C:\evil.txt` and UNC-like `\\server\share\evil.txt`
- rejects missing `readboard.exe`
- rejects filename not equal to `readboard-github-release-v3.0.2.zip`

- [ ] **Step 2: Write failing downloader tests**

Avoid real network in tests. Inject a download delegate or `HttpMessageHandler` so the test can assert the destination path:

```text
%LOCALAPPDATA%\LizzieYzyNext\readboard-updates\v3.0.2\readboard-github-release-v3.0.2.zip
```

Use a temporary base directory override in tests rather than mutating the real user profile.

- [ ] **Step 3: Run tests to verify failure**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~HostedUpdatePackage"
```

Expected: fails because classes do not exist.

- [ ] **Step 4: Implement verifier**

Use `System.IO.Compression.ZipArchive`. Do not extract to app directory. Normalize every entry with `/` and `\` as separators. Reject:

- empty entry names
- rooted paths
- drive-letter paths
- UNC paths
- any `..` segment

Track required files by basename after optionally skipping one common top-level folder.

- [ ] **Step 5: Implement downloader**

Use `HttpClient` with the existing GitHub update checker style: timeout, no shell execution, and clear exception messages. Ensure the destination directory is created. Download to a temp file in the target directory first, then move to final zip path.

- [ ] **Step 6: Run targeted tests**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~HostedUpdatePackage"
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add readboard\HostedUpdatePackage*.cs tests\Readboard.VerificationTests\Host\HostedUpdatePackage*Tests.cs
git commit -m "feat(update): download and verify hosted update packages"
```

## Task 4: readboard Update Dialog Hosted Flow

**Files:**
- Modify: `readboard/UpdateDialogModel.cs`
- Modify: `readboard/FormUpdate.cs`
- Modify: `readboard/Form1.cs`
- Modify: `readboard/MainForm.Protocol.cs`
- Modify: `readboard/Program.cs`
- Modify: `readboard/language_cn.txt`
- Modify: `readboard/language_en.txt`
- Modify: `readboard/language_jp.txt`
- Modify: `readboard/language_kr.txt`
- Test: `tests/Readboard.VerificationTests/Host/UpdateDownloadLauncherTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/UpdateDialogFormatterTests.cs`
- Test: `tests/Readboard.VerificationTests/Host/StartupAndShutdownRegressionTests.cs`

- [ ] **Step 1: Write failing dialog behavior tests**

Add tests that lock:

- without `HostedInstallAvailable`, the download button still calls shell launcher
- with `HostedInstallAvailable`, the button uses hosted install handler and does not call `OpenDownloadUri`
- `readboardUpdateInstalling` updates the currently open update dialog into waiting/installing state
- cancel result restores enabled button text
- failed result shows sanitized one-line message
- timeout path restores manual download fallback
- closing the update dialog clears the active hosted update session so late host messages do not touch a disposed form

Keep tests focused on testable helpers if direct WinForms event tests become brittle.

- [ ] **Step 2: Run tests to verify failure**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~Update"
```

Expected: fails because hosted update UI model does not exist.

- [ ] **Step 3: Implement model and UI state**

Extend `UpdateDialogModel` with fields such as:

```csharp
public bool HostedInstallAvailable { get; set; }
public string HostedAssetUrl { get; set; }
public string HostedAssetName { get; set; }
public string HostedReleaseTag { get; set; }
public Func<UpdateDialogModel, Task<HostedUpdatePrepareResult>> PrepareHostedUpdateAsync { get; set; }
public Action<string, string> NotifyHostedUpdateReady { get; set; }
```

Keep the implementation smaller if a dedicated service object is cleaner. Do not add a settings checkbox.

- [ ] **Step 4: Wire Form1 to FormUpdate and keep an active update session**

When building the update dialog model in `Form1.cs`, set `HostedInstallAvailable` only when:

- latest release has a matching asset
- `hostedUpdateSupported == true`
- current transport is pipe mode, not TCP

After download/preflight, send `readboardUpdateReady\t<tag>\t<path>` through the existing protocol send path.

Hold a single active hosted update session/controller on `MainForm` while `FormUpdate` is open. The session should expose small methods such as `MarkInstalling`, `MarkCancelled`, `MarkFailed`, and `MarkTimedOut`; `MainForm.Protocol.cs` host-result callbacks call those methods on the UI thread. Clear the active session when the dialog closes, when timeout/manual fallback is reached, or after a terminal result.

- [ ] **Step 5: Handle host result messages**

On inbound:

- `readboardUpdateInstalling`: show waiting/installing state
- `readboardUpdateCancelled`: restore UI and manual fallback
- `readboardUpdateFailed\t<message>`: show sanitized error and manual fallback

If no update dialog is active, ignore these messages after logging/diagnostics rather than opening a new dialog.

- [ ] **Step 6: Update localization**

Add localized keys for:

- `Update_downloadAndInstall`
- `Update_downloading`
- `Update_waitingForHostInstall`
- `Update_hostCancelled`
- `Update_hostFailed`
- `Update_manualDownloadFallback`

For JP/KR, a direct English fallback is acceptable for the first pass if that matches existing project practice; do not leave missing keys.

- [ ] **Step 7: Run targeted tests**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore --filter "FullyQualifiedName~Update|FullyQualifiedName~StartupAndShutdownRegressionTests"
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add readboard\FormUpdate* readboard\Form1.cs readboard\MainForm.Protocol.cs readboard\UpdateDialogModel.cs readboard\Program.cs readboard\language_*.txt tests\Readboard.VerificationTests\Host
git commit -m "feat(update): add hosted update dialog flow"
```

## Task 5: LizzieYzy-Next Protocol Parsing And Capability Handshake

**Files:**
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoard.java`
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardStream.java` if needed to observe sent commands in tests.
- Create: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardUpdateRequest.java`
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis\ReadBoardUpdateProtocolTest.java`
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis\ReadBoardStreamTest.java` or a new native startup protocol test.
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis\ReadBoardLaunchPathTest.java` only for directory/path parsing, not handshake behavior.

- [ ] **Step 1: Write failing protocol parser tests**

Test accepted:

```text
readboardUpdateReady\tv3.0.2\tC:\Users\me\AppData\Local\LizzieYzyNext\readboard-updates\v3.0.2\readboard-github-release-v3.0.2.zip
```

Test rejected:

- spaces instead of tabs
- missing version
- missing path
- extra field
- path containing tab
- version not matching `vX.Y.Z`

- [ ] **Step 2: Write failing capability tests**

Add a seam to verify native pipe readboard sends `readboardUpdateSupported` deterministically after receiving child `ready`, in the same loading path that currently calls `checkVersion()`, before or next to the existing `version` command. Java readboard and unsupported modes must not send it.

Keep capability assertions in `ReadBoardUpdateProtocolTest` plus `ReadBoardStreamTest` or a new native startup protocol test. Leave `ReadBoardLaunchPathTest` focused on directory and process-builder path parsing.

- [ ] **Step 3: Run Maven tests to verify failure**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateProtocolTest,ReadBoardStreamTest test
```

Expected: fails because protocol parser and capability are missing.

- [ ] **Step 4: Implement request value object**

`ReadBoardUpdateRequest` should contain:

```java
String versionTag();
File zipPath();
```

Parsing must trim only line endings before splitting on `\t`. Do not call broad `trim()` before parsing because it can hide malformed leading/trailing spaces.

- [ ] **Step 5: Route parsed request**

In `ReadBoard.parseLine`, parse `readboardUpdateReady`. If valid, call into a small callback or `Lizzie.frame` method that will own confirmation/install. Invalid lines should be ignored or logged without throwing.

- [ ] **Step 6: Send host capability**

When native pipe readboard emits `ready` and command sending is available, send:

```text
readboardUpdateSupported
```

Do not send it for Java readboard or unsupported modes. Keep the send point deterministic in the same startup loading path that calls `checkVersion()`, before or next to the existing `version` command.

- [ ] **Step 7: Run targeted tests**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateProtocolTest,ReadBoardStreamTest,ReadBoardLaunchPathTest test
```

Expected: pass.

- [ ] **Step 8: Commit in LizzieYzy-Next**

```powershell
git -C D:\dev\weiqi\lizzieyzy-next add src\main\java\featurecat\lizzie\analysis src\test\java\featurecat\lizzie\analysis
git -C D:\dev\weiqi\lizzieyzy-next commit -m "feat(readboard): add hosted update protocol"
```

## Task 6: LizzieYzy-Next Installer Service

**Files:**
- Create: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\analysis\ReadBoardUpdateInstaller.java`
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis\ReadBoardUpdateInstallerTest.java`

- [ ] **Step 1: Write failing installer tests**

Use JUnit temp directories. Cover:

- valid zip installs into sibling `readboard/`
- valid zip may have required files at the zip root
- valid zip may have required files under one common top-level directory
- old `readboard/` moves to `readboard.backup-<timestamp>`
- staging is removed after success
- missing any required release file is rejected: `readboard.exe`, `readboard.dll`, `readboard.runtimeconfig.json`, `readboard.deps.json`, `language_cn.txt`
- filename/version mismatch is rejected
- `../evil.txt`, absolute paths, drive-letter paths, and UNC paths are rejected
- canonical path escaping staging is rejected
- failure after old directory move restores backup to `readboard/`

The host installer must revalidate the complete release file set and must not trust the preflight performed by readboard.

- [ ] **Step 2: Run tests to verify failure**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateInstallerTest test
```

Expected: fails because installer class does not exist.

- [ ] **Step 3: Implement installer**

Implement a focused service with injectable clock or timestamp provider:

```java
public final class ReadBoardUpdateInstaller {
  public InstallResult install(ReadBoardUpdateRequest request, File currentReadBoardDir) throws IOException;
}
```

Rules:

- target parent is `currentReadBoardDir.getCanonicalFile().getParentFile()`
- staging name: `readboard.staging-<yyyyMMdd-HHmmss>`
- backup name: `readboard.backup-<yyyyMMdd-HHmmss>`
- validate every zip entry before writing any file
- validate the complete required release file set before replacing anything: `readboard.exe`, `readboard.dll`, `readboard.runtimeconfig.json`, `readboard.deps.json`, `language_cn.txt`
- accept either a flat release zip or a zip with exactly one common top-level release directory
- resolve every output path with canonical checks under staging
- move `readboard` to backup, then staging to `readboard`
- on failure after backup move, move backup back to `readboard`

- [ ] **Step 4: Run installer tests**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateInstallerTest test
```

Expected: pass.

- [ ] **Step 5: Commit in LizzieYzy-Next**

```powershell
git -C D:\dev\weiqi\lizzieyzy-next add src\main\java\featurecat\lizzie\analysis\ReadBoardUpdateInstaller.java src\test\java\featurecat\lizzie\analysis\ReadBoardUpdateInstallerTest.java
git -C D:\dev\weiqi\lizzieyzy-next commit -m "feat(readboard): install hosted update packages"
```

## Task 7: LizzieYzy-Next Lifecycle And UI Integration

**Files:**
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\java\featurecat\lizzie\gui\LizzieFrame.java`
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\resources\l10n\DisplayStrings.properties`
- Modify: `D:\dev\weiqi\lizzieyzy-next\src\main\resources\l10n\DisplayStrings_zh_CN.properties`
- Modify: other `DisplayStrings_*.properties`
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\analysis\ReadBoardShutdownTest.java`
- Test: `D:\dev\weiqi\lizzieyzy-next\src\test\java\featurecat\lizzie\gui\LizzieFrameRegressionTest.java`

- [ ] **Step 1: Write failing lifecycle tests**

Cover:

- user cancel sends `readboardUpdateCancelled`, does not stop current readboard, and does not alter directories
- accepted install sends `readboardUpdateInstalling`, stops readboard, installs, then starts native readboard
- install path uses existing restart serialization and does not block Swing EDT
- concurrent `reopenReadBoard` and update install cannot both replace/start readboard at once

- [ ] **Step 2: Run tests to verify failure**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardShutdownTest,LizzieFrameRegressionTest test
```

Expected: fails because lifecycle hook is missing.

- [ ] **Step 3: Implement frame entry point**

Add a method such as:

```java
public void handleReadBoardUpdateReady(ReadBoardUpdateRequest request)
```

It should:

- show a localized confirmation dialog on the Swing thread
- send `readboardUpdateCancelled` on cancel
- validate obvious pre-stop failures and send `readboardUpdateFailed\t<message>`
- run install on a background thread
- use the same restart lock/queue boundary as `reopenReadBoard`
- after success, call deterministic native `startReadBoard(this::createNativeReadBoard)`

- [ ] **Step 4: Add localized messages**

Add keys:

- `ReadBoard.updateConfirmTitle`
- `ReadBoard.updateConfirmMessage`
- `ReadBoard.updateFailed`
- `ReadBoard.updateComplete`
- `ReadBoard.updateCancelled`

- [ ] **Step 5: Run targeted tests**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateProtocolTest,ReadBoardStreamTest,ReadBoardUpdateInstallerTest,ReadBoardShutdownTest,LizzieFrameRegressionTest test
```

Expected: pass.

- [ ] **Step 6: Commit in LizzieYzy-Next**

```powershell
git -C D:\dev\weiqi\lizzieyzy-next add src\main\java src\main\resources\l10n src\test\java
git -C D:\dev\weiqi\lizzieyzy-next commit -m "feat(readboard): install updates through host lifecycle"
```

## Task 8: Cross-Repo Verification And Packaging Smoke Test

**Files:**
- Modify docs only if implementation discovers a real divergence from the spec.
- No new production files expected.

- [ ] **Step 1: Run readboard full verification**

```powershell
dotnet test tests\Readboard.VerificationTests\Readboard.VerificationTests.csproj --no-restore
```

Expected: all tests pass.

- [ ] **Step 2: Run LizzieYzy-Next targeted verification**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml -Dtest=ReadBoardUpdateProtocolTest,ReadBoardStreamTest,ReadBoardUpdateInstallerTest,ReadBoardLaunchPathTest,ReadBoardShutdownTest,LizzieFrameRegressionTest test
```

Expected: all tests pass.

- [ ] **Step 3: Run broader host tests if time permits**

```powershell
mvn -f D:\dev\weiqi\lizzieyzy-next\pom.xml test
```

Expected: pass. If it is too slow or environment-bound, record the reason and the targeted results.

- [ ] **Step 4: Build readboard release folder**

Use the repository script, not ad hoc commands:

```powershell
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File scripts\package-readboard-release.local.ps1 -SkipZip
```

Expected:

- `readboard.exe` exists in the generated release folder
- no new zip is generated because `-SkipZip` was used
- `PackageVersion` matches `readboard/Properties/AssemblyInfo.cs`
- release folder timestamp is from this run

- [ ] **Step 5: Optional local integration smoke**

Default to a temp constructed `readboard-github-release-vX.Y.Z.zip` containing the required release file set. Point the test harness at a temp `readboard/` directory and verify:

- readboard can prepare `readboardUpdateReady\t<tag>\t<zip>`
- LizzieYzy-Next installer replaces temp `readboard/`
- native readboard starts and responds to `version`

Only use a real package zip from `scripts\package-readboard-release.local.ps1` without `-SkipZip` after explicit human approval to create a zip artifact.

- [ ] **Step 6: Final status and commits**

Ensure both repos are clean except intentional untracked local artifacts:

```powershell
git status --short
git -C D:\dev\weiqi\lizzieyzy-next status --short
```

If any implementation changed the plan/spec, commit docs separately. Otherwise leave the plan as implementation guidance.
