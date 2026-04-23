# .NET 10 Upgrade Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate readboard from .NET Framework 4.8 to .NET 10, replacing all incompatible dependencies, removing dead code, simplifying the test architecture, and enabling WinForms dark mode.

**Architecture:** The main WinForms app (readboard.csproj) converts from legacy MSBuild + packages.config to SDK-style + PackageReference targeting `net10.0-windows`. All .NET FW-only dependencies (System.Web.Extensions, OpenCvSharp3, MouseKeyboardActivityMonitor, Interop.lw) are replaced or removed in the same phase since none compile on .NET 10. Test/benchmark projects switch from source-linking to ProjectReference once the framework gap is eliminated. UI modernization follows as a final phase.

**Tech Stack:** .NET 10 SDK, WinForms (.NET 10), OpenCvSharp4.Windows, System.Text.Json, xUnit 2.9.2, GitHub Actions

**Spec:** `docs/superpowers/specs/2026-04-22-dotnet10-upgrade-design.md`

---

## Phase 1: Framework Upgrade + Dependency Updates + Dead Code Cleanup

All changes in Phase 1 must land together — switching to `net10.0-windows` breaks every legacy dependency simultaneously.

### Task 1: Remove lw.dll dead code from production sources

This must happen first because `Interop.lw` COM interop cannot load on .NET 10 and `using LwInterop = Interop.lw;` will not compile.

**Files:**
- Modify: `readboard/Core/Placement/IMovePlacementService.cs`
- Modify: `readboard/Core/Placement/MovePlacementRequest.cs`
- Modify: `readboard/Core/Placement/MovePlacementResult.cs`
- Modify: `readboard/Core/Protocol/SyncCoordinatorHostSnapshot.cs`
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.Orchestration.cs`
- Modify: `readboard/Form1.cs`
- Delete: `readboard/Interop.lw.dll`
- Delete: `readboard/lw.dll`
- Delete: `readboard/generate_lw_interop.ps1`

- [ ] **Step 1: Remove lw imports and fields from Form1.cs**

Remove line 15 (`using LwInterop = Interop.lw;`), line 47 (`Boolean canUseLW = false;`), line 74 (`LwInterop.lwsoft lw;`).

In `ReleasePlacementBinding` (lines 1335-1341), replace body with empty — just the `if (handle == IntPtr.Zero) return;` guard stays, remove the lwsoft instantiation and ForceUnBindWindow call:
```csharp
private void ReleasePlacementBinding(IntPtr handle)
{
    if (handle == IntPtr.Zero)
        return;
}
```

In `CanUseForegroundFoxInBoardProtocol` (line 1255-1258), simplify:
```csharp
private bool CanUseForegroundFoxInBoardProtocol()
{
    return CurrentSyncType == TYPE_FOX;
}
```

In the snapshot builder (line 1538), remove the line entirely:
```csharp
// Remove: CanUseLightweightInterop = CurrentSyncType == TYPE_FOX && canUseLW,
```

Remove the lw initialization block (lines 1874-1893 — the try/catch that creates `new LwInterop.lwsoft()`).

- [ ] **Step 2: Remove LightweightInterop from IMovePlacementService.cs**

Remove `using LwInterop = Interop.lw;` (line 3).

Remove `PlaceLightweight` method (lines 305-322).

Remove `IPlacementLightweightInteropFactory` interface (lines 397-400).

Remove `IPlacementLightweightInteropClient` interface (lines 402-407).

Remove `PlacementLightweightInteropFactory` class (lines 409-415).

Remove `PlacementLightweightInteropClient` class (lines 417-463).

Remove the `lightweightInteropFactory` field from `LegacyMovePlacementService` (line 15) and the constructor parameter + null check (lines 23-24, 27-28).

Update `CreateDefault()` (lines 31-36):
```csharp
internal static LegacyMovePlacementService CreateDefault()
{
    return new LegacyMovePlacementService(
        new User32PlacementNativeMethods());
}
```

In `ResolvePath` (line 83-84), remove the LightweightInterop branch:
```csharp
// Remove these two lines:
// if (syncMode == SyncMode.Fox && request.UseLightweightInterop)
//     return PlacementPathKind.LightweightInterop;
```

In the `Place` method dispatch (around line 50-60), remove the `case PlacementPathKind.LightweightInterop:` branch.

- [ ] **Step 3: Remove UseLightweightInterop from MovePlacementRequest.cs**

Remove line 9: `public bool UseLightweightInterop { get; set; }`

- [ ] **Step 4: Remove LightweightInterop from PlacementPathKind enum in MovePlacementResult.cs**

Remove line 18: `LightweightInterop = 4`

- [ ] **Step 5: Remove CanUseLightweightInterop from SyncCoordinatorHostSnapshot.cs**

Remove line 72: `public bool CanUseLightweightInterop { get; set; }`

- [ ] **Step 6: Clean up SyncSessionCoordinator.Orchestration.cs**

In `PlacePendingMove` (around line 713), remove the `UseLightweightInterop` assignment from the request construction:
```csharp
// Remove this line:
// UseLightweightInterop = snapshot.CanUseLightweightInterop,
```

In `TrackPlacementBindingHandle` (lines 913-930), this entire method only does work when `PlacementPathKind.LightweightInterop` — simplify to no-op or remove entirely:
```csharp
private void TrackPlacementBindingHandle(
    SyncCoordinatorHostSnapshot snapshot,
    MovePlacementResult result)
{
}
```

Also remove the `pendingPlacementBindingHandles` field (line 13: `private readonly HashSet<IntPtr> pendingPlacementBindingHandles = new HashSet<IntPtr>();`) since it's now dead. Clean up `TakePendingPlacementBindingHandlesLocked` and simplify `ReleasePlacementBindings` to a no-op (keep the interface method signature but empty the body). Search for all references to `pendingPlacementBindingHandles` in this file and remove them.

- [ ] **Step 7: Delete binary files**

Delete `readboard/Interop.lw.dll`, `readboard/lw.dll`, `readboard/generate_lw_interop.ps1`.

- [ ] **Step 8: Verify compile locally (expect failures from other deps — that's OK)**

Compilation will still fail due to remaining .NET FW dependencies. This is expected — we're cleaning lw first to reduce the blast radius.

---

### Task 2: Replace JavaScriptSerializer with System.Text.Json

**Files:**
- Modify: `readboard/Core/Configuration/DualFormatAppConfigStore.cs`
- Modify: `readboard/GitHubUpdateChecker.cs`

- [ ] **Step 1: Migrate DualFormatAppConfigStore.cs**

Replace `using System.Web.Script.Serialization;` (line 5) with `using System.Text.Json;`.

Remove the field (line 41):
```csharp
// Remove: private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
```

In `DeserializePartialJsonConfig` (around line 90), replace:
```csharp
// Old:
// IDictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(content);
// New:
IDictionary<string, object> values = JsonSerializer.Deserialize<Dictionary<string, object>>(content,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

In `WriteJsonConfig` (around line 223), replace:
```csharp
// Old:
// string content = serializer.Serialize(config);
// New:
string content = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
```

**Important:** `JavaScriptSerializer.Deserialize<Dictionary<string, object>>` produces `Dictionary<string, object>` where nested objects are also `Dictionary<string, object>` and numbers are `int`/`long`. `System.Text.Json` produces `JsonElement` values instead. The `ApplyJsonOverrides` method (line 124+) accesses values with casts like `(int)values["key"]` — these must be updated to handle `JsonElement`:

```csharp
// Helper for reading values from deserialized JSON:
private static T GetJsonValue<T>(IDictionary<string, object> values, string key)
{
    if (!values.TryGetValue(key, out object raw))
        return default;
    if (raw is JsonElement element)
        return element.Deserialize<T>();
    if (raw is T typed)
        return typed;
    return default;
}
```

Review `ApplyJsonOverrides` and update every direct cast to use `JsonElement`-aware access.

- [ ] **Step 2: Migrate GitHubUpdateChecker.cs**

Replace `using System.Web.Script.Serialization;` (line 9) with `using System.Text.Json;`.

In `ParseLatestRelease` (line 194-195), replace:
```csharp
// Old:
// var serializer = new JavaScriptSerializer();
// var payload = serializer.DeserializeObject(json) as Dictionary<string, object>;
// New:
var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(json,
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
```

Update subsequent property accesses to handle `JsonElement` values (e.g., `payload["tag_name"]` returns `JsonElement`, needs `.GetString()` or `.ToString()`).

- [ ] **Step 3: Run existing config tests to verify compatibility**

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~Configuration"
```

Expected: Tests may still fail due to framework mismatch at this stage.

---

### Task 3: Replace MouseKeyboardActivityMonitor with P/Invoke keyboard hook

**Files:**
- Create: `readboard/Core/Input/GlobalKeyboardHook.cs`
- Modify: `readboard/Form1.cs`
- Delete: `readboard/bin/Debug/MouseKeyboardActivityMonitor.dll` (if committed)

- [ ] **Step 1: Create GlobalKeyboardHook.cs**

```csharp
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace readboard
{
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private readonly LowLevelKeyboardProc callback;
        private IntPtr hookId = IntPtr.Zero;

        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;

        public GlobalKeyboardHook()
        {
            callback = HookCallback;
        }

        public void Start()
        {
            if (hookId != IntPtr.Zero)
                return;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                hookId = SetWindowsHookEx(WH_KEYBOARD_LL, callback,
                    GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Stop()
        {
            if (hookId == IntPtr.Zero)
                return;
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int message = (int)wParam;
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    KeyDown?.Invoke(this, new KeyEventArgs((Keys)vkCode));
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    KeyUp?.Invoke(this, new KeyEventArgs((Keys)vkCode));
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
```

- [ ] **Step 2: Create GlobalMouseHook.cs**

Same pattern as keyboard hook but for `WH_MOUSE_LL` (14). The mouse hook is used for selection mode (`mh` in Form1.cs). Needs `MouseMove` and `MouseClick` events.

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace readboard
{
    internal sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;

        private readonly LowLevelMouseProc callback;
        private IntPtr hookId = IntPtr.Zero;
        private bool enabled;

        public event MouseEventHandler MouseMove;
        public event MouseEventHandler MouseClick;

        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (value) Start();
                else Stop();
            }
        }

        public GlobalMouseHook()
        {
            callback = HookCallback;
        }

        public void Start()
        {
            if (hookId != IntPtr.Zero)
                return;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                hookId = SetWindowsHookEx(WH_MOUSE_LL, callback,
                    GetModuleHandle(module.ModuleName), 0);
            }
        }

        public void Stop()
        {
            if (hookId == IntPtr.Zero)
                return;
            UnhookWindowsHookEx(hookId);
            hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && enabled)
            {
                MSLLHOOKSTRUCT hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int message = (int)wParam;
                MouseEventArgs args = new MouseEventArgs(
                    message == WM_LBUTTONDOWN ? MouseButtons.Left :
                    message == WM_RBUTTONDOWN ? MouseButtons.Right : MouseButtons.None,
                    1, hookData.pt.X, hookData.pt.Y, 0);

                if (message == WM_MOUSEMOVE)
                    MouseMove?.Invoke(this, args);
                else if (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN)
                    MouseClick?.Invoke(this, args);
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
```

- [ ] **Step 3: Update Form1.cs to use new hooks**

Replace imports (lines 8-9):
```csharp
// Remove:
// using MouseKeyboardActivityMonitor;
// using MouseKeyboardActivityMonitor.WinApi;
```

Replace field (line 51):
```csharp
// Old: private KeyboardHookListener hookListener;
// New:
private GlobalKeyboardHook keyboardHook;
```

Replace mouse hook field (find `MouseHookListener mh` declaration):
```csharp
// Old: MouseHookListener mh;
// New:
private GlobalMouseHook mouseHook;
```

Update constructor (lines 1844-1849):
```csharp
// Old:
// GlobalHooker hooker = new GlobalHooker();
// hookListener = new KeyboardHookListener(hooker);
// hookListener.KeyDown += HookListener_KeyDown;
// hookListener.KeyUp += HookListener_KeyUp;
// hookListener.Start();
// New:
keyboardHook = new GlobalKeyboardHook();
keyboardHook.KeyDown += HookListener_KeyDown;
keyboardHook.KeyUp += HookListener_KeyUp;
keyboardHook.Start();
```

Update `Form1_Load` (lines 2119-2123):
```csharp
// Old:
// mh = new MouseHookListener(new GlobalHooker());
// mh.MouseMove += mh_MouseMoveEvent;
// mh.MouseClick += mh_MouseMoveEvent2;
// mh.Enabled = false;
// New:
mouseHook = new GlobalMouseHook();
mouseHook.MouseMove += mh_MouseMoveEvent;
mouseHook.MouseClick += mh_MouseMoveEvent2;
mouseHook.Enabled = false;
```

Update `DisposeInputHooks` (lines 2417-2434):
```csharp
private void DisposeInputHooks()
{
    if (keyboardHook != null)
    {
        keyboardHook.KeyDown -= HookListener_KeyDown;
        keyboardHook.KeyUp -= HookListener_KeyUp;
        keyboardHook.Stop();
        keyboardHook.Dispose();
        keyboardHook = null;
    }
    if (mouseHook == null)
        return;
    mouseHook.MouseMove -= mh_MouseMoveEvent;
    mouseHook.MouseClick -= mh_MouseMoveEvent2;
    mouseHook.Enabled = false;
    mouseHook.Stop();
    mouseHook.Dispose();
    mouseHook = null;
}
```

Also update all other references to `mh` (mouse hook variable) throughout Form1.cs — search for `mh.Enabled`, `mh.Start()`, `mh.Stop()` and replace with `mouseHook.Enabled`, etc.

---

### Task 4: Convert readboard.csproj to SDK-style targeting net10.0-windows

**Files:**
- Rewrite: `readboard/readboard.csproj`
- Delete: `readboard/packages.config`
- Modify: `readboard/Program.cs`
- Keep: `readboard/Properties/AssemblyInfo.cs` (retain for CI version validation)
- Keep: `readboard/Properties/app.manifest`

- [ ] **Step 1: Write the new SDK-style csproj**

Replace the entire `readboard.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>WinExe</OutputType>
    <UseWindowsForms>true</UseWindowsForms>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ApplicationManifest>Properties\app.manifest</ApplicationManifest>
    <ApplicationIcon>lizziey.ico</ApplicationIcon>
    <RootNamespace>readboard</RootNamespace>
    <AssemblyName>readboard</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="OpenCvSharp4.Windows" Version="4.10.0.20241108" />
    <PackageReference Include="System.Drawing.Common" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="language_cn.txt" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="language_en.txt" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="language_jp.txt" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="language_kr.txt" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="readme.rtf" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="readme_en.rtf" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="readme_jp.rtf" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

**Key decisions:**
- No `PlatformTarget` — default AnyCPU (OpenCvSharp4 supports both x86/x64)
- `GenerateAssemblyInfo=false` — keeps `Properties/AssemblyInfo.cs` for CI version tag validation
- `AllowUnsafeBlocks=true` — Release config had this
- `ApplicationManifest` — explicit (SDK-style doesn't auto-discover)
- `System.Drawing.Common` NuGet — required for .NET 10 (System.Drawing is not included)
- Language/readme files as `Content` — ensures they copy to output

**Note on OpenCvSharp4 version:** Check nuget.org for the latest `OpenCvSharp4.Windows` version. The version above (`4.10.0.20241108`) may need updating. Run:
```bash
dotnet package search OpenCvSharp4.Windows --take 1
```

- [ ] **Step 2: Delete packages.config**

```bash
rm readboard/packages.config
```

- [ ] **Step 3: Update Program.cs for .NET 10 DPI configuration**

Add DPI mode setup before `Application.EnableVisualStyles()` in the `Main` method (around line 230):
```csharp
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
```

If `Application.SetHighDpiMode` is already called (check first), just verify the parameter is `PerMonitorV2`.

- [ ] **Step 4: Delete App.config**

On .NET 10, DPI configuration moves to code (Step 3). The `<startup>` element is ignored (no runtime version selection needed). The `AppContextSwitchOverrides` for DPI is handled by `SetHighDpiMode`. The `App.config` can be deleted — .NET 10 SDK generates `readboard.runtimeconfig.json` automatically.

```bash
rm readboard/App.config
```

- [ ] **Step 5: Delete the packages/ directory**

```bash
rm -rf packages/
```

The OpenCvSharp3 NuGet cache under `packages/OpenCvSharp3-AnyCPU.4.0.0.20181129/` is no longer needed — NuGet restores to the global cache in SDK-style projects.

---

### Task 5: Fix P/Invoke pointer safety for AnyCPU

**Files:**
- Modify: `readboard/Core/Placement/IMovePlacementService.cs`
- Modify: `readboard/Core/Protocol/LegacySyncWindowLocator.cs`
- Modify: `readboard/Form1.cs`

- [ ] **Step 1: Fix PostMessage and SendMessage signatures in IMovePlacementService.cs**

In `IMovePlacementService.cs` (around lines 534-537):
```csharp
// Old:
// private static extern bool PostMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
// private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, int wParam, int lParam);
// New:
[DllImport("user32.dll", SetLastError = true)]
private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll", SetLastError = true)]
private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
```

Update all call sites: `TryPostMouseMessage` and `SendMouseMessage` methods pass `int` values — wrap them with `(IntPtr)`:
```csharp
public bool TryPostMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
{
    return PostMessage(handle, message, (IntPtr)wParam, (IntPtr)lParam);
}

public void SendMouseMessage(IntPtr handle, uint message, int wParam, int lParam)
{
    SendMessage(handle, message, (IntPtr)wParam, (IntPtr)lParam);
}
```

- [ ] **Step 2: Fix PostMessage and SendMessage signatures in Form1.cs**

Form1.cs also has its own PostMessage/SendMessage declarations (around lines 2556-2560):
```csharp
// Old:
// static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
// static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
// New:
[DllImport("user32.dll", SetLastError = true)]
static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll", SetLastError = true)]
static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
```

Update all call sites in Form1.cs that pass `int` to these methods — wrap with `(IntPtr)` casts. Search for `PostMessage(` and `SendMessage(` calls within Form1.cs.

- [ ] **Step 3: Fix EnumWindows in LegacySyncWindowLocator.cs**

In `LegacySyncWindowLocator.cs` (around line 207):
```csharp
// Old:
// private static extern bool EnumWindows(EnumWindowsProc callback, int parameter);
// private delegate bool EnumWindowsProc(IntPtr handle, int parameter);
// New:
[DllImport("user32.dll")]
private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
```

Update the call site (around line 158) and callback implementation to use `IntPtr.Zero` instead of `0`, and `IntPtr parameter` in the callback signature.

---

### Task 6: Update test and benchmark projects

**Files:**
- Modify: `tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj`
- Modify: `benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj`
- Delete: `tests/Shared/LightweightInteropShim.cs`
- Delete: `tests/Shared/JavaScriptSerializerShim.cs`
- Modify: `tests/Readboard.VerificationTests/Placement/LegacyMovePlacementAcceptanceMatrixTests.cs`
- Modify: `tests/Readboard.VerificationTests/Placement/LegacyMovePlacementServiceTests.cs`
- Modify: `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorOrchestrationTests.cs`
- Modify: `tests/Readboard.VerificationTests/Protocol/SustainedSyncAcceptanceHarness.cs`

- [ ] **Step 1: Update VerificationTests.csproj**

Change `TargetFramework` from `net8.0` to `net10.0-windows`:
```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

- [ ] **Step 2: Update Benchmarks.csproj**

Change `TargetFramework` from `net8.0` to `net10.0-windows`:
```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

- [ ] **Step 3: Delete LightweightInteropShim.cs**

```bash
rm tests/Shared/LightweightInteropShim.cs
```

Remove the corresponding `<Compile Include>` entries from both test and benchmark csproj files.

- [ ] **Step 4: Delete JavaScriptSerializerShim.cs**

```bash
rm tests/Shared/JavaScriptSerializerShim.cs
```

Remove the corresponding `<Compile Include>` entries from both test and benchmark csproj files.

- [ ] **Step 5: Update LegacyMovePlacementAcceptanceMatrixTests.cs**

Remove `Place_FoxLightweightInteropUsesLwWhenDllIsAvailable` test (lines 25-44).

Remove `Place_FoxLightweightInteropFailsWhenDllIsUnavailable` test (lines 46-59).

Update `CancellationCases` — remove the `(SyncMode.Fox, true)` case that uses `useLightweightInterop: true` (line 22).

Remove `RecordingLightweightFactory` and `RecordingLightweightClient` inner classes (lines 189-246) and `ThrowingLightweightFactory` (lines 204-217).

Update all `LegacyMovePlacementService` constructor calls — remove the `lightweightInteropFactory` parameter:
```csharp
// Old: new LegacyMovePlacementService(nativeMethods, new RecordingLightweightFactory(client))
// New: new LegacyMovePlacementService(nativeMethods)
```

- [ ] **Step 6: Update LegacyMovePlacementServiceTests.cs**

Remove `RecordingLightweightFactory` and `RecordingLightweightClient` inner classes (lines 223-249).

Update all `LegacyMovePlacementService` constructor calls — remove second parameter:
```csharp
// Old: new LegacyMovePlacementService(nativeMethods, new RecordingLightweightFactory())
// New: new LegacyMovePlacementService(nativeMethods)
```

- [ ] **Step 7: Update SyncSessionCoordinatorOrchestrationTests.cs**

Search for `CanUseLightweightInterop` and remove/update any test code that sets this property. Search for `SetProperty(snapshot, "CanUseLightweightInterop", ...)` and remove those lines.

- [ ] **Step 8: Update SustainedSyncAcceptanceHarness.cs**

Remove line: `CanUseLightweightInterop = false,` from the snapshot construction.

- [ ] **Step 9: Build and run tests**

```bash
dotnet build readboard.sln
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

Expected: All compile and pass. Fix any remaining issues.

- [ ] **Step 10: Commit Phase 1**

Stage all changed/deleted/new files explicitly. Key files to stage:
- Modified: `readboard/readboard.csproj`, `readboard/Form1.cs`, `readboard/Program.cs`, all `Core/` modifications, test and benchmark csproj files, test placement files
- New: `readboard/Core/Input/GlobalKeyboardHook.cs`, `readboard/Core/Input/GlobalMouseHook.cs`
- Deleted: `readboard/Interop.lw.dll`, `readboard/lw.dll`, `readboard/generate_lw_interop.ps1`, `readboard/packages.config`, `readboard/App.config`, `tests/Shared/LightweightInteropShim.cs`, `tests/Shared/JavaScriptSerializerShim.cs`, `packages/` directory

Run `git status` first to review, then stage specific files. Avoid `git add -A` to prevent staging unintended files.

```bash
git add -A
git commit -m "feat: migrate to .NET 10 with dependency updates and dead code cleanup

- Convert readboard.csproj from legacy MSBuild to SDK-style targeting net10.0-windows
- Replace OpenCvSharp3 (2018, unmaintained) with OpenCvSharp4.Windows
- Replace JavaScriptSerializer with System.Text.Json (DualFormatAppConfigStore, GitHubUpdateChecker)
- Replace MouseKeyboardActivityMonitor with inline P/Invoke GlobalKeyboardHook/GlobalMouseHook
- Remove lw.dll COM interop dead code (canUseLW was hardcoded to false)
- Fix PostMessage/SendMessage P/Invoke signatures for AnyCPU pointer safety
- Update test/benchmark projects from net8.0 to net10.0-windows
- Remove test shims no longer needed (JavaScriptSerializerShim, LightweightInteropShim)

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Phase 2: Test Project Simplification + CI/Packaging Updates

### Task 7: Convert test project from source-linking to ProjectReference

**Files:**
- Modify: `tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj`
- Modify: `readboard/readboard.csproj` (add InternalsVisibleTo)
- Delete: `tests/Shared/WindowsFormsScreenShim.cs`

- [ ] **Step 1: Add InternalsVisibleTo to main project**

Add to `readboard/Properties/AssemblyInfo.cs`:
```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Readboard.VerificationTests")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Readboard.ProtocolConfigBenchmarks")]
```

- [ ] **Step 2: Replace source-linked Compile items with ProjectReference**

In `Readboard.VerificationTests.csproj`, remove ALL `<Compile Include="..\..\readboard\..." Link="Production\..." />` entries (approximately 65 lines), and replace with:
```xml
<ProjectReference Include="..\..\readboard\readboard.csproj" />
```

Keep the test-specific `<Compile Include>` items (test harnesses, fixtures, shared helpers that are test infrastructure).

- [ ] **Step 3: Delete ProgramShim.cs**

`ProgramShim.cs` declares `internal static class Program` in namespace `readboard` — this will conflict with the real `Program` class when using ProjectReference. Delete it:
```bash
rm tests/Shared/ProgramShim.cs
```

Remove the corresponding `<Compile Include>` entry from the test csproj. If any test depends on `Program.UiThemeOptimized`, reference it from the real `Program` class (the field already exists there).

- [ ] **Step 4: Delete WindowsFormsScreenShim.cs**

```bash
rm tests/Shared/WindowsFormsScreenShim.cs
```

On .NET 10 with `UseWindowsForms`, `System.Windows.Forms.Screen` is available directly — the shim is no longer needed.

Remove the corresponding `<Compile Include>` entries from BOTH the test AND benchmark csproj files (both reference this shim).

Verify no test code references the shim's custom `Screen` class instead of the real one. If tests use a custom namespace for `Screen`, update them.

- [ ] **Step 5: Build and run tests**

```bash
dotnet build readboard.sln
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj
```

Fix any visibility issues (classes/methods that were accessible via source-linking but are now `private`/`internal`).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(tests): replace source-linking with ProjectReference

Unified on .NET 10 eliminates the framework gap that required source-level
linking. Tests now reference the main assembly directly via ProjectReference,
with InternalsVisibleTo for internal type access.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Convert benchmark project from source-linking to ProjectReference

**Files:**
- Modify: `benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj`

- [ ] **Step 1: Replace source-linked Compile items with ProjectReference**

Same approach as Task 7 — remove all `<Compile Include="..\..\readboard\..." />` and `<Compile Include="..\..\tests\Shared\..." />` entries (including `ProgramShim.cs`, `WindowsFormsScreenShim.cs`, `JavaScriptSerializerShim.cs`, `LightweightInteropShim.cs` references), replace with:
```xml
<ProjectReference Include="..\..\readboard\readboard.csproj" />
```

Keep benchmark-specific harness files.

- [ ] **Step 2: Build and run benchmark**

```bash
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(benchmarks): replace source-linking with ProjectReference

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Update CI workflow and packaging script

**Files:**
- Modify: `.github/workflows/package-release.yml`
- Modify: `scripts/package-readboard-release.local.ps1`

- [ ] **Step 1: Update package-release.yml**

Key changes:
- Change `dotnet-version` from `8.0.x` to `10.0.x`
- Remove `microsoft/setup-msbuild@v2` step (no longer needed)
- Replace MSBuild restore: `msbuild.exe ... /t:Restore /p:RestorePackagesConfig=true` → `dotnet restore readboard.sln`
- Replace MSBuild build (in packaging script invocation): ensure `dotnet build` or `dotnet publish` is used
- Keep test and benchmark steps (already use `dotnet test` / `dotnet run`)

- [ ] **Step 2: Update package-readboard-release.local.ps1**

Key changes:
- Replace MSBuild invocation with `dotnet publish`:
  ```powershell
  dotnet publish readboard/readboard.csproj -c Release -o $BuildOutputDir
  ```
- Update `$requiredBuildFiles` — remove:
  - `readboard.exe.config` (not produced by .NET 10)
  - `MouseKeyboardActivityMonitor.dll` (removed)
  - `OpenCvSharp.Blob.dll`, `OpenCvSharp.UserInterface.dll` (not in OpenCvSharp4)
  - `dll\x86\OpenCvSharpExtern.dll`, `dll\x86\opencv_ffmpeg400.dll` (old native layout)
- Add .NET 10 output files:
  - `readboard.dll` (.NET 10 produces host exe + managed dll)
  - `readboard.runtimeconfig.json`
  - OpenCvSharp4 native dependencies (layout differs from v3)
- Update `$BuildOutputDir` — .NET 10 output path is typically `readboard/bin/Release/net10.0-windows/`
- Remove `$Platform = 'x86'` validation or update to accept AnyCPU

- [ ] **Step 3: Test packaging locally**

```pwsh
pwsh ./scripts/package-readboard-release.local.ps1 -SkipZip
```

Verify the release folder contains all required files.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "ci: update workflow and packaging for .NET 10

- GitHub Actions: .NET 10 SDK, remove MSBuild setup, use dotnet build/publish
- Packaging script: update required files for .NET 10 output structure,
  remove legacy dependencies (lw.dll, MouseKeyboardActivityMonitor, OpenCvSharp3)

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Update CLAUDE.md and project documentation

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update build commands**

Replace the Build & Test Commands section:
```bash
# Restore NuGet packages
dotnet restore readboard.sln

# Build
dotnet build readboard.sln -c Release

# Run all tests
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj

# Run a single test class
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FixtureReplayTests"

# Run benchmark acceptance harness
dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj

# Package release
pwsh ./scripts/package-readboard-release.local.ps1
```

- [ ] **Step 2: Update Constraints section**

- Change ".NET Framework 4.8" to ".NET 10 LTS"
- Remove "x86 target" constraint (now AnyCPU)
- Update test project description (ProjectReference, not source-linking)
- Remove mentions of `packages.config`, `msbuild` for main build

- [ ] **Step 3: Update Architecture section**

Remove mentions of test shims (`JavaScriptSerializerShim`, `LightweightInteropShim`, `WindowsFormsScreenShim`). Update the test project description.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for .NET 10 migration

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Phase 3: UI Modernization

### Task 11: Enable WinForms dark mode

**Files:**
- Modify: `readboard/Program.cs`
- Modify: `readboard/UiTheme.cs`

- [ ] **Step 1: Add dark mode to Program.cs**

In `Main`, before `Application.EnableVisualStyles()`:
```csharp
Application.SetColorMode(SystemColorMode.System);
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
```

`SystemColorMode.System` follows the Windows system dark/light setting.

- [ ] **Step 2: Verify dark mode renders correctly**

Start the application with Windows in dark mode. Check each form:
- Form1 (main window)
- Form4 (settings)
- Form5 (platform selection)
- Form7 (tips)
- FormUpdate (update dialog)

- [ ] **Step 3: Adjust UiTheme.cs for dark mode compatibility**

The current `UiTheme.cs` hardcodes light colors (e.g., `WindowBackground = Color.FromArgb(242, 245, 249)`). These will conflict with system dark mode.

Option A: Remove hardcoded colors and let system dark mode handle everything.
Option B: Make `UiTheme` responsive to `Application.SystemColorMode`.

Start with Option A — remove all `UiTheme.Apply*` calls from Form constructors and let the system handle it. If specific controls don't render well in dark mode, add targeted fixes.

- [ ] **Step 4: Test both light and dark modes**

Manually switch Windows between light and dark mode. Verify:
- All text is readable
- Custom-painted areas are visible
- Board capture/recognition overlay displays correctly
- Status indicators maintain meaning

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ui): enable system dark mode support via .NET 10 WinForms

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

## Verification Checklist

After all tasks are complete:

- [ ] `dotnet build readboard.sln -c Release` compiles without warnings
- [ ] `dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj` — all tests pass
- [ ] `dotnet run --project benchmarks/Readboard.ProtocolConfigBenchmarks/Readboard.ProtocolConfigBenchmarks.csproj` — benchmarks run without regression
- [ ] `pwsh ./scripts/package-readboard-release.local.ps1 -SkipZip` — produces correct release structure
- [ ] Application starts and syncs a board successfully (manual test)
- [ ] Dark mode displays correctly on all forms
- [ ] Light mode displays correctly on all forms
- [ ] `readboard/Interop.lw.dll`, `readboard/lw.dll` are gone from repo
- [ ] `readboard/packages.config` is gone
- [ ] `packages/` directory is gone
- [ ] No `System.Web.Script.Serialization` references remain
- [ ] No `MouseKeyboardActivityMonitor` references remain
