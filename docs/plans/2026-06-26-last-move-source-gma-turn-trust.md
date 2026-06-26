# Last Move Source Protocol Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ReadBoard-side `lastMoveSource` recognition and outbound protocol support so Lizzie can distinguish visual last-move markers from heuristic guesses.

**Architecture:** Keep board cells as `BlackLastMove` / `WhiteLastMove`, and carry marker provenance as separate `LastMoveSource` metadata on `BoardSnapshot` and outbound batches. Compute the Fox corner-flip signal in the existing per-cell recognition pass, then let a small inference resolver choose the first trusted/heuristic source in the existing priority order.

**Tech Stack:** C# / .NET `net10.0-windows`, xUnit, existing `LegacyBoardRecognitionService`, existing legacy ReadBoard wire protocol.

---

## Specs And Contracts

- Primary spec: `docs/specs/2026-06-26-last-move-source-gma-turn-trust-design.md`
- Lizzie companion spec: `/home/dev/.config/superpowers/worktrees/lizzieyzy-next/kata-genmove-analyze-sync/docs/specs/2026-06-26-readboard-last-move-source-gma-turn-trust-design.md`
- Existing contracts to preserve:
  - `docs/specs/2026-06-24-gma-engine-decision-autoplay-design.md`
  - `docs/specs/2026-05-20-fox-auto-play-color-detection-design.md`
  - `docs/specs/2026-04-23-protocol-keyword-constants.md`
  - `docs/specs/2026-05-04-sync-session-outbound-board-emitter-phase2.md`

## File Structure

- Create `readboard/Core/Models/LastMoveSource.cs`  
  Defines the internal enum: `None`, `RedBlueMarker`, `FoxCornerFlip`, `Deviation`, `StoneCount`.
- Create `readboard/Core/Recognition/LastMoveInference.cs`  
  Small immutable result carrying `BoardCoordinate Coordinate` and `LastMoveSource Source`; include `None`.
- Create `readboard/Core/Recognition/LastMoveInferenceResolver.cs`  
  Moves source-aware marker/deviation/stone-count inference out of `IBoardRecognitionService.cs` while preserving current behavior.
- Create `readboard/Core/Recognition/FoxCornerFlipSummary.cs`  
  Tracks best/second-best corner-flip candidates and enforces uniqueness/margin.
- Modify `readboard/Core/Recognition/BoardRecognitionResult.cs`  
  Add `LastMoveSource` to `LegacyBoardAnalysis`, `RegionMetrics`, and relevant diagnostics helpers.
- Modify `readboard/Core/Recognition/IBoardRecognitionService.cs`  
  Populate `LastMoveSource`, compute corner-flip metrics in `AnalyzeRegion`, and clone/cache source metadata.
- Modify `readboard/Core/Models/BoardSnapshot.cs`  
  Add `LastMoveSource LastMoveSource`.
- Modify `readboard/Core/Protocol/ProtocolKeywords.cs`  
  Add stable wire tokens.
- Modify `readboard/Core/Protocol/IReadBoardProtocolAdapter.cs` and `LegacyProtocolAdapter.cs`  
  Add `CreateLastMoveSourceMessage(LastMoveSource source)`.
- Modify `readboard/Core/Protocol/OutboundBoardSnapshotEmitter.cs`  
  Add source to `OutboundBoardSnapshotBatch` and emit it before board rows.
- Modify `readboard/Core/Protocol/SyncSessionCoordinator.cs`  
  Include source in outbound dedupe and batch creation.
- Modify `readboard/Core/Diagnostics/BoardDebugDiagnosticsWriter.cs`  
  Include `lastMoveSource` in recognition text/metadata if snapshot is present.
- Modify `docs/specs/2026-04-23-protocol-keyword-constants.md`  
  Record the new public wire token.
- Tests:
  - `tests/Readboard.VerificationTests/Protocol/LegacyOutboundProtocolContractTests.cs`
  - `tests/Readboard.VerificationTests/Protocol/LegacyProtocolAdapterTests.cs`
  - `tests/Readboard.VerificationTests/Protocol/OutboundBoardSnapshotEmitterTests.cs`
  - `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorTests.cs`
  - Create `tests/Readboard.VerificationTests/Recognition/LastMoveInferenceResolverTests.cs`
  - Create `tests/Readboard.VerificationTests/Recognition/FoxCornerFlipSummaryTests.cs`
  - Create `tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/*.png`

## Task 1: Add Stable Protocol Tokens

**Files:**
- Create: `readboard/Core/Models/LastMoveSource.cs`
- Modify: `readboard/Core/Protocol/ProtocolKeywords.cs`
- Modify: `readboard/Core/Protocol/IReadBoardProtocolAdapter.cs`
- Modify: `readboard/Core/Protocol/LegacyProtocolAdapter.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/LegacyOutboundProtocolContractTests.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/LegacyProtocolAdapterTests.cs`

- [ ] **Step 1: Write failing protocol keyword tests**

Add assertions to `ProtocolKeywords_DefineStableLegacyWireTokens`:

```csharp
Assert.Equal("lastMoveSource ", ProtocolKeywords.LastMoveSourcePrefix);
Assert.Equal("none", ProtocolKeywords.LastMoveSourceNone);
Assert.Equal("redBlueMarker", ProtocolKeywords.LastMoveSourceRedBlueMarker);
Assert.Equal("foxCornerFlip", ProtocolKeywords.LastMoveSourceFoxCornerFlip);
Assert.Equal("deviation", ProtocolKeywords.LastMoveSourceDeviation);
Assert.Equal("stoneCount", ProtocolKeywords.LastMoveSourceStoneCount);
```

- [ ] **Step 2: Write failing adapter serialization tests**

Add to `LegacyProtocolAdapterTests.cs`:

```csharp
[Theory]
[InlineData(LastMoveSource.None, "lastMoveSource none")]
[InlineData(LastMoveSource.RedBlueMarker, "lastMoveSource redBlueMarker")]
[InlineData(LastMoveSource.FoxCornerFlip, "lastMoveSource foxCornerFlip")]
[InlineData(LastMoveSource.Deviation, "lastMoveSource deviation")]
[InlineData(LastMoveSource.StoneCount, "lastMoveSource stoneCount")]
public void CreateLastMoveSourceMessage_SerializesLegacyRawText(
    LastMoveSource source,
    string expected)
{
    LegacyProtocolAdapter adapter = new LegacyProtocolAdapter();

    string line = adapter.Serialize(adapter.CreateLastMoveSourceMessage(source));

    Assert.Equal(expected, line);
}
```

- [ ] **Step 3: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~ProtocolKeywords_DefineStableLegacyWireTokens|FullyQualifiedName~CreateLastMoveSourceMessage_SerializesLegacyRawText"
```

Expected: compile/test failure because `LastMoveSource` and protocol constants/method do not exist.

- [ ] **Step 4: Implement minimal protocol model**

Add `LastMoveSource.cs`:

```csharp
namespace readboard
{
    internal enum LastMoveSource
    {
        None = 0,
        RedBlueMarker = 1,
        FoxCornerFlip = 2,
        Deviation = 3,
        StoneCount = 4
    }
}
```

Add constants to `ProtocolKeywords.cs`, add `CreateLastMoveSourceMessage(...)` to the interface and adapter, mapping unknown/default enum values to `none`.

- [ ] **Step 5: Run protocol tests and verify they pass**

Run the command from Step 3.

- [ ] **Step 6: Commit**

```bash
git add readboard/Core/Models/LastMoveSource.cs readboard/Core/Protocol/ProtocolKeywords.cs readboard/Core/Protocol/IReadBoardProtocolAdapter.cs readboard/Core/Protocol/LegacyProtocolAdapter.cs tests/Readboard.VerificationTests/Protocol/LegacyOutboundProtocolContractTests.cs tests/Readboard.VerificationTests/Protocol/LegacyProtocolAdapterTests.cs
git commit -m "feat(readboard): add last move source protocol token"
```

## Task 2: Carry `LastMoveSource` Through Snapshots And Outbound Frames

**Files:**
- Modify: `readboard/Core/Models/BoardSnapshot.cs`
- Modify: `readboard/Core/Protocol/OutboundBoardSnapshotEmitter.cs`
- Modify: `readboard/Core/Protocol/SyncSessionCoordinator.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/OutboundBoardSnapshotEmitterTests.cs`
- Test: `tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorTests.cs`

- [ ] **Step 1: Write failing emitter ordering test**

Update `Emit_SendsWindowContextForceRebuildFoxMoveBoardLinesAndEndInOrder` so the batch includes `LastMoveSource.FoxCornerFlip` and expected output includes:

```text
foxMoveNumber 57
lastMoveSource foxCornerFlip
re=000
```

Also update `Emit_SkipsOptionalSegmentsWhenBatchDoesNotNeedThem` to expect `lastMoveSource none` before `re=000`.

- [ ] **Step 2: Write failing coordinator dedupe test**

Add:

```csharp
[Fact]
public void SendBoardSnapshot_ResendsFullFrameWhenLastMoveSourceChangesWithoutPayloadChange()
{
    FakeTransport transport = new FakeTransport();
    SyncSessionCoordinator coordinator = new SyncSessionCoordinator(transport, new LegacyProtocolAdapter());
    BoardSnapshot first = CreateSnapshot("payload-1", 57);
    first.LastMoveSource = LastMoveSource.StoneCount;
    BoardSnapshot second = CreateSnapshot("payload-1", 57);
    second.LastMoveSource = LastMoveSource.FoxCornerFlip;

    coordinator.SendBoardSnapshot(first);
    coordinator.SendBoardSnapshot(second);

    Assert.Equal(
        new[]
        {
            "syncPlatform generic", "foxMoveNumber 57", "lastMoveSource stoneCount", "re=000", "re=111", "end",
            "syncPlatform generic", "foxMoveNumber 57", "lastMoveSource foxCornerFlip", "re=000", "re=111", "end"
        },
        transport.SentLines);
}
```

- [ ] **Step 3: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~OutboundBoardSnapshotEmitterTests|FullyQualifiedName~SendBoardSnapshot_ResendsFullFrameWhenLastMoveSourceChangesWithoutPayloadChange"
```

Expected: compile/test failure because batches and snapshots do not carry source.

- [ ] **Step 4: Implement snapshot and outbound plumbing**

Add `LastMoveSource LastMoveSource { get; set; }` to `BoardSnapshot`, defaulting to `None`. Add `LastMoveSource` to `OutboundBoardSnapshotBatch`. Emit `protocolAdapter.CreateLastMoveSourceMessage(batch.LastMoveSource)` after optional `foxMoveNumber` and before board rows.

In `SyncSessionCoordinator`, add a `lastSentBoardLastMoveSource` field and include it in the dedupe comparison/reset alongside `lastSentBoardFoxMoveNumber`.

- [ ] **Step 5: Run tests and verify they pass**

Run the command from Step 3.

- [ ] **Step 6: Commit**

```bash
git add readboard/Core/Models/BoardSnapshot.cs readboard/Core/Protocol/OutboundBoardSnapshotEmitter.cs readboard/Core/Protocol/SyncSessionCoordinator.cs tests/Readboard.VerificationTests/Protocol/OutboundBoardSnapshotEmitterTests.cs tests/Readboard.VerificationTests/Protocol/SyncSessionCoordinatorTests.cs
git commit -m "feat(readboard): emit last move source with board snapshots"
```

## Task 3: Make Existing Last-Move Inference Source-Aware

**Files:**
- Create: `readboard/Core/Recognition/LastMoveInference.cs`
- Create: `readboard/Core/Recognition/LastMoveInferenceResolver.cs`
- Modify: `readboard/Core/Recognition/BoardRecognitionResult.cs`
- Modify: `readboard/Core/Recognition/IBoardRecognitionService.cs`
- Test: `tests/Readboard.VerificationTests/Recognition/LastMoveInferenceResolverTests.cs`

- [ ] **Step 1: Write failing resolver tests**

Create tests for the existing priority order without corner-flip:

```csharp
[Fact]
public void Apply_PromotesRedOnlyMarkerAsRedBlueMarker()
{
    BoardCellState[] state = { BoardCellState.Black, BoardCellState.White };
    StoneSummary black = new StoneSummary(BoardCellState.Black, BoardCellState.BlackLastMove);
    StoneSummary white = new StoneSummary(BoardCellState.White, BoardCellState.WhiteLastMove);
    black.Observe(80, 0, 0);
    white.Observe(80, 1, 0);
    MarkerSummary marker = new MarkerSummary();
    marker.Observe(redPercent: 5, bluePercent: 0, threshold: 1, x: 0, y: 0);

    LastMoveInference result = LastMoveInferenceResolver.Apply(state, 2, black, white, marker, FoxCornerFlipSummary.Empty);

    Assert.Equal(LastMoveSource.RedBlueMarker, result.Source);
    Assert.Equal(new BoardCoordinate(0, 0), result.Coordinate);
    Assert.Equal(BoardCellState.BlackLastMove, state[0]);
}
```

Add similar tests for:

- no marker but deviation chooses black or white -> `Deviation`
- count imbalance chooses black or white -> `StoneCount`
- no candidates -> `None`

- [ ] **Step 2: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~LastMoveInferenceResolverTests"
```

Expected: compile failure because resolver/result classes do not exist.

- [ ] **Step 3: Implement resolver by moving existing private logic**

Move `ApplyLastMoveInference`, `TryApplyMarkerLastMove`, `TryApplyDeviationLastMove`, `TryApplyStoneCountLastMove`, `CalculateDeviation`, and `PromoteLastMove` into `LastMoveInferenceResolver`. Keep behavior identical except each successful branch returns a source.

Update `LegacyBoardAnalysis` and `BoardSnapshot` build path to assign `LastMoveSource`.

- [ ] **Step 4: Run resolver tests and a narrow recognition smoke test**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~LastMoveInferenceResolverTests|FullyQualifiedName~FixtureReplayRecognitionTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add readboard/Core/Recognition/LastMoveInference.cs readboard/Core/Recognition/LastMoveInferenceResolver.cs readboard/Core/Recognition/BoardRecognitionResult.cs readboard/Core/Recognition/IBoardRecognitionService.cs tests/Readboard.VerificationTests/Recognition/LastMoveInferenceResolverTests.cs
git commit -m "feat(readboard): track source for last move inference"
```

## Task 4: Detect Fox Corner-Flip Visual Markers

**Files:**
- Create: `readboard/Core/Recognition/FoxCornerFlipSummary.cs`
- Modify: `readboard/Core/Recognition/BoardRecognitionResult.cs`
- Modify: `readboard/Core/Recognition/IBoardRecognitionService.cs`
- Create: `tests/Readboard.VerificationTests/Recognition/FoxCornerFlipSummaryTests.cs`
- Create: `tests/Readboard.VerificationTests/Recognition/FoxCornerFlipRecognitionTests.cs`
- Create: `tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/fox-corner-flip-black-last-move.png`
- Create: `tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/fox-corner-flip-white-last-move.png`
- Create: `tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/fox-numbered-blue-marker-white-last-move.png`
- Create: `tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/fox-numbered-red-marker-black-last-move.png`

- [ ] **Step 1: Copy the four tiny fixture images**

Copy from:

```text
docs/specs/assets/2026-06-26-last-move-source/
```

to:

```text
tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource/
```

Do not copy the full screenshots.

- [ ] **Step 2: Write failing corner summary and fixture-recognition tests**

Create tests around `FoxCornerFlipSummary`:

```csharp
[Fact]
public void Observe_AcceptsUniqueBlackCornerFlip()
{
    FoxCornerFlipSummary summary = new FoxCornerFlipSummary();

    summary.Observe(BoardCellState.Black, blackOppositePercent: 18, whiteOppositePercent: 0, x: 3, y: 4);
    summary.Observe(BoardCellState.Black, blackOppositePercent: 3, whiteOppositePercent: 0, x: 5, y: 6);

    Assert.True(summary.TryGetUniqueCandidate(out BoardCoordinate candidate));
    Assert.Equal(new BoardCoordinate(3, 4), candidate);
}
```

Add tests for:

- white corner flip
- two close candidates -> no unique candidate
- low score -> no candidate

Create `FoxCornerFlipRecognitionTests` that loads the four fixture PNGs copied in Step 1 and routes them through the production recognition path, not only through synthetic summary scores. Build a 1x1 `BoardRecognitionRequest` per fixture:

```csharp
using (Bitmap bitmap = new Bitmap(FixturePath("fox-corner-flip-black-last-move.png")))
{
    BoardRecognitionResult result = new LegacyBoardRecognitionService().Recognize(
        new BoardRecognitionRequest
        {
            Frame = new BoardFrame
            {
                SyncMode = SyncMode.Background,
                BoardSize = new BoardDimensions(1, 1),
                Image = bitmap,
                Viewport = new BoardViewport
                {
                    SourceBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    ScreenBounds = new PixelRect(0, 0, bitmap.Width, bitmap.Height),
                    CellWidth = bitmap.Width,
                    CellHeight = bitmap.Height
                }
            },
            InferLastMove = true
        });

    Assert.True(result.Success, result.FailureReason);
    Assert.Equal(BoardCellState.BlackLastMove, result.Snapshot.BoardState[0]);
    Assert.Equal(LastMoveSource.FoxCornerFlip, result.Snapshot.LastMoveSource);
}
```

Add fixture-recognition cases for:

- black corner-flip crop -> `BlackLastMove` and `FoxCornerFlip`
- white corner-flip crop -> `WhiteLastMove` and `FoxCornerFlip`
- numbered red marker crop -> `BlackLastMove` and `RedBlueMarker`
- numbered blue marker crop -> `WhiteLastMove` and `RedBlueMarker`

The numbered fixture assertions lock the priority rule that red/blue marker wins even if corner-like opposite-color pixels are present.

- [ ] **Step 3: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxCornerFlipSummaryTests|FullyQualifiedName~FoxCornerFlipRecognitionTests"
```

Expected: compile failure because the summary, fixture test target, and snapshot source do not exist.

- [ ] **Step 4: Implement corner metric collection**

Extend `RegionMetrics` with enough normalized metrics to score right-lower opposite-color wedges. During `AnalyzeRegion`, count only pixels in the stone's lower-right inner triangle, for example normalized positions where `x > regionWidth / 2`, `y > regionHeight / 2`, and `x + y` is in the lower-right sector. Count:

- light pixels in a black stone candidate as black-opposite score
- dark pixels in a white stone candidate as white-opposite score

Feed those scores to `FoxCornerFlipSummary.Observe(...)` from `AnalyzeRow` only when `inferLastMove` and the cell is occupied. Do not satisfy the fixture-recognition tests with a test-only shortcut; the fixture tests must pass through `Recognize(...)`, `AnalyzeBoard(...)`, `AnalyzeRow(...)`, and `AnalyzeRegion(...)`.

- [ ] **Step 5: Wire corner source into inference priority**

Pass `FoxCornerFlipSummary` into `LastMoveInferenceResolver.Apply(...)` and check it after red/blue marker, before deviation.

- [ ] **Step 6: Run corner and resolver tests**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~FoxCornerFlipSummaryTests|FullyQualifiedName~FoxCornerFlipRecognitionTests|FullyQualifiedName~LastMoveInferenceResolverTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add readboard/Core/Recognition/FoxCornerFlipSummary.cs readboard/Core/Recognition/BoardRecognitionResult.cs readboard/Core/Recognition/IBoardRecognitionService.cs readboard/Core/Recognition/LastMoveInferenceResolver.cs tests/Readboard.VerificationTests/Recognition/FoxCornerFlipSummaryTests.cs tests/Readboard.VerificationTests/Recognition/FoxCornerFlipRecognitionTests.cs tests/Readboard.VerificationTests/Recognition/Fixtures/LastMoveSource
git commit -m "feat(readboard): detect fox corner flip last move markers"
```

## Task 5: Update Diagnostics And Protocol Docs

**Files:**
- Modify: `readboard/Core/Diagnostics/BoardDebugDiagnosticsWriter.cs`
- Modify: `docs/specs/2026-04-23-protocol-keyword-constants.md`
- Test: `tests/Readboard.VerificationTests/Diagnostics/BoardDebugDiagnosticsWriterTests.cs`

- [ ] **Step 1: Write failing diagnostics/docs tests**

Update diagnostics expectations so recognition success includes:

```text
lastMoveSource=foxCornerFlip
```

Add an assertion in an existing source-doc test if present, or add a small test that reads `docs/specs/2026-04-23-protocol-keyword-constants.md` and asserts it contains ``lastMoveSource ``.

- [ ] **Step 2: Run tests and verify they fail**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~BoardDebugDiagnosticsWriterTests|FullyQualifiedName~ProtocolKeywords"
```

Expected: FAIL until diagnostics/docs are updated.

- [ ] **Step 3: Implement docs/diagnostics update**

Include `LastMoveSource` in `BoardDebugDiagnosticsWriter.FormatRecognition(...)` and JSON-like metadata where the snapshot is serialized. Update `docs/specs/2026-04-23-protocol-keyword-constants.md` with the new outbound prefix and token list.

- [ ] **Step 4: Run tests and verify they pass**

Run the command from Step 2.

- [ ] **Step 5: Commit**

```bash
git add readboard/Core/Diagnostics/BoardDebugDiagnosticsWriter.cs docs/specs/2026-04-23-protocol-keyword-constants.md tests/Readboard.VerificationTests/Diagnostics/BoardDebugDiagnosticsWriterTests.cs
git commit -m "docs(readboard): document last move source protocol"
```

## Task 6: Final ReadBoard Verification

**Files:**
- No new files unless failures expose missing coverage.

- [ ] **Step 1: Run focused test suite**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj --filter "FullyQualifiedName~LastMoveSource|FullyQualifiedName~FoxCornerFlip|FullyQualifiedName~OutboundBoardSnapshotEmitterTests|FullyQualifiedName~SyncSessionCoordinatorTests|FullyQualifiedName~LegacyOutboundProtocolContractTests|FullyQualifiedName~LegacyProtocolAdapterTests|FullyQualifiedName~FixtureReplayRecognitionTests|FullyQualifiedName~BoardDebugDiagnosticsWriterTests"
```

Expected: PASS.

- [ ] **Step 2: Run broader verification if time allows**

Run:

```bash
dotnet test tests/Readboard.VerificationTests/Readboard.VerificationTests.csproj
```

Expected: PASS. If Windows-only SDK tooling is required, run the same command from Windows PowerShell against the WSL worktree path.

- [ ] **Step 3: Review scope**

Run:

```bash
git status --short
git diff --stat HEAD
```

Expected: only files listed in this plan changed.

- [ ] **Step 4: Commit final fixes if any**

If Step 1 or Step 2 required a small follow-up fix:

```bash
git add readboard/Core tests/Readboard.VerificationTests docs/specs docs/plans
git commit -m "fix(readboard): harden last move source propagation"
```
