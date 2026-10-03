const { test, expect } = require("@playwright/test");
const {
  publishRelease,
  removeDirectory,
  withRealWebView2Host
} = require("./real-webview2-host-fixture");

function readPersistedSyncInterval(configuration) {
  return JSON.parse(configuration["config.readboard.json"].replace(/^\uFEFF/, "")).SyncIntervalMs;
}

let publishDirectory;

test.describe.configure({ mode: "serial" });
test.setTimeout(300_000);

test.beforeAll(async () => {
  publishDirectory = await publishRelease();
}, 300_000);

test.afterAll(async () => {
  if (!process.env.READBOARD_PUBLISH_DIRECTORY) {
    await removeDirectory(publishDirectory);
  }
});

test("real Release ReadBoard publishes its first authoritative WebView2 snapshot", { tag: "@host-core" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");

    expect(readBoard.host.connected).toBe(true);
    expect(readBoard.process.pid).toBeGreaterThan(0);
    await expect.poll(() => readBoard.page.url()).toBe("https://app.readboard/index.html");
    await expect.poll(
      () => readBoard.page.evaluate(() => Boolean(window.chrome?.webview)))
      .toBe(true);
    await expect(readBoard.page.locator("body")).not.toHaveClass(/awaiting-state/);
    await expect(readBoard.page.locator(".app-shell")).toBeVisible();
    await expect(readBoard.page.locator("#sync-status")).toHaveText("Ready");
    await expect(readBoard.page.locator("#host-state")).toHaveText("Host communication active");
  });
});


test("real Control Center exchanges version and platform state with its host", { tag: "@host-core" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");

    await readBoard.host.sendLine("version");
    await expect.poll(() => readBoard.host.transcript
      .filter(entry => entry.direction === "inbound")
      .map(entry => entry.line)
      .find(line => /^version: \d+$/.test(line)) || null).toMatch(/^version: \d+$/);
    await expect(readBoard.page.locator("#log-list")).toContainText("Host communication active");
    await expect(readBoard.page.locator("#host-state")).toHaveText("Host communication active");
    await expect(readBoard.page.locator("#log-list")).toContainText("Host mode started; ReadBoard is ready");

    await readBoard.page.locator('input[name="platform"][value="yike"]').check();
    await expect(readBoard.page.locator("#context-platform")).toHaveText("Yike");
    await expect.poll(async () => {
      const configuration = await readBoard.readConfigurationFiles();
      return JSON.parse(configuration["config.readboard.json"].replace(/^\uFEFF/, "")).SyncMode;
    }).toBe(6);
  });
});

test("real Control Center waits for authoritative analysis observations", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    const analysis = readBoard.page.locator('[data-command="sync.toggleAnalysis"]');
    const analysisLabel = readBoard.page.locator("#analysis-label");

    await readBoard.host.sendLine("analysisState paused");
    await expect(analysisLabel).toHaveText("Resume Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "false");
    await expect(analysis).toBeEnabled();

    await analysis.click();
    await readBoard.host.waitForExactLine("resumeponder");
    await expect(analysisLabel).toHaveText("Resume Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "false");

    await readBoard.host.sendLine("analysisState running");
    await expect(analysisLabel).toHaveText("Pause Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "true");

    await analysis.click();
    await readBoard.host.waitForExactLine("noponder");
    await expect(analysisLabel).toHaveText("Pause Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "true");

    await readBoard.host.sendLine("analysisState paused");
    await expect(analysisLabel).toHaveText("Resume Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "false");
  });
});

test("real Control Center waits for host analysis observations after pause", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    const analysis = readBoard.page.locator('[data-command="sync.toggleAnalysis"]');
    const analysisLabel = readBoard.page.locator("#analysis-label");

    await readBoard.host.sendLine("analysisState running");
    await expect(analysisLabel).toHaveText("Pause Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "true");
    await expect(analysis).toBeEnabled();

    await analysis.click();
    await readBoard.host.waitForExactLine("noponder");
    await expect(analysisLabel).toHaveText("Pause Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "true");

    await readBoard.host.sendLine("analysisState paused");
    await expect(analysisLabel).toHaveText("Resume Analysis");
    await expect(analysis).toHaveAttribute("aria-pressed", "false");
  });
});

test("real Control Center manual autoplay color requires explicit selection per enablement cycle", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    const state = () => readBoard.page.evaluate(() => window.readboardPreview.getState().controlCenter);
    const expectColor = async (color) => {
      await expect.poll(async () => {
        const control = await state();
        return { color: control.color, known: control.playColorKnown };
      }).toEqual({ color, known: color !== "" });
      const checked = readBoard.page.locator('input[name="color"]:checked');
      if (color) await expect(checked).toHaveValue(color);
      else await expect(checked).toHaveCount(0);
    };
    const setToggle = async (id, key, value) => {
      if ((await state())[key] !== value)
        await readBoard.page.locator(`label:has(#${id})`).click();
      await expect.poll(async () => (await state())[key]).toBe(value);
    };
    const chooseColor = async (color) => {
      await readBoard.page.locator(`label:has(input[name="color"][value="${color}"])`).click();
      await expectColor(color);
    };
    const cancelAutomaticSelection = async () => {
      await readBoard.page.locator('label:has(input[name="color"][value="auto"])').click();
      await expect(readBoard.page.locator('[data-command="identity.close"]')).toBeVisible();
      await readBoard.page.locator('[data-command="identity.close"]').click();
      await expect(readBoard.page.locator("#modal-layer")).toBeHidden();
    };

    await readBoard.host.waitForExactLine("ready");
    await readBoard.page.waitForFunction(() => window.readboardPreview?.getState()?.controlCenter);
    await setToggle("two-way", "twoWaySync", true);
    await setToggle("auto-play", "autoPlay", true);
    await expectColor("");
    await cancelAutomaticSelection();
    await expectColor("");
    await chooseColor("black");
    await chooseColor("white");
    await cancelAutomaticSelection();
    await expectColor("white");
    await setToggle("auto-play", "autoPlay", false);
    await setToggle("auto-play", "autoPlay", true);
    await expectColor("");
    await chooseColor("white");
    await setToggle("two-way", "twoWaySync", false);
    await expect.poll(async () => (await state()).autoPlay).toBe(false);
    await setToggle("two-way", "twoWaySync", true);
    await setToggle("auto-play", "autoPlay", true);
    await expectColor("");
    await chooseColor("black");

    await readBoard.restartWithFreshProfile();
    await readBoard.host.waitForExactLine("ready");
    await readBoard.page.waitForFunction(() => window.readboardPreview?.getState()?.controlCenter);
    await expect.poll(async () => (await state()).autoPlay).toBe(false);
    await setToggle("two-way", "twoWaySync", true);
    await setToggle("auto-play", "autoPlay", true);
    await expectColor("");
    await readBoard.page.screenshot({ path: testInfo.outputPath("manual-color-unselected.png") });
  });
});

test("real Control Center runtime authorization publishes one final snapshot per identity and color operation", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    await expect(readBoard.page.locator("#host-state")).toHaveText("Host communication active");
    await expect(readBoard.page.locator("#log-list")).toContainText("Host mode started; ReadBoard is ready");
    await readBoard.page.waitForFunction(() => window.readboardPreview?.getState()?.controlCenter);

    // Observe the actual native bridge; never replace the renderer's state or inject players.
    await readBoard.page.evaluate(() => {
      window.receivedAuthorizationStates = [];
      window.chrome.webview.addEventListener("message", event => {
        if (event.data?.type === "state")
          window.receivedAuthorizationStates.push(structuredClone(event.data));
      });
    });
    const receivedStates = () => readBoard.page.evaluate(() => window.receivedAuthorizationStates);
    const renderedState = () => readBoard.page.evaluate(() => window.readboardPreview.getState());
    const operations = [];
    const operate = async (name, action, expected) => {
      const operation = {
        name,
        stateStart: (await receivedStates()).length,
        wireStart: readBoard.host.transcript.length
      };
      operations.push(operation);
      await action();
      await expect.poll(async () => (await receivedStates()).length).toBeGreaterThan(operation.stateStart);
      // Keep listening after the first delivery so a duplicate final or intermediate
      // publication cannot pass just because the expected state arrived first.
      await readBoard.page.evaluate(() => new Promise(resolve => setTimeout(resolve, 250)));
      const messages = (await receivedStates()).slice(operation.stateStart);
      operation.stateEnd = operation.stateStart + messages.length;
      operation.wireEnd = readBoard.host.transcript.length;
      expect(messages, `${name}: exactly one complete final native snapshot`).toHaveLength(1);
      const snapshot = messages[0].payload;
      for (const key of ["page", "language", "text", "shell", "controlCenter", "settings", "update", "identity", "dialog", "logs"])
        expect(snapshot, `${name}: complete snapshot`).toHaveProperty(key);
      expect(snapshot).toMatchObject(expected);
      expect(await renderedState()).toEqual(snapshot);
      return snapshot;
    };
    const identityButton = command => readBoard.page.locator(`[data-command="identity.${command}"]`);
    const expectNoPlayers = async () => {
      await expect(readBoard.page.locator('input[name="candidate"]')).toHaveCount(0);
      await expect(identityButton("useOnce")).toBeDisabled();
      await expect(identityButton("saveAndUse")).toBeDisabled();
    };
    const emptySelection = { candidates: [], selectedId: null, canUseOnce: false, canSaveAndUse: false };

    try {
      // A saved preference is real config input, not evidence of any Fox player.
      await operate("open saved identity", () => identityButton("open").click(), {
        identity: { ...emptySelection, open: true, hasSavedIdentity: true }
      });
      await expectNoPlayers();
      await readBoard.page.screenshot({ path: testInfo.outputPath("authorization-saved-identity.png") });
      await operate("clear saved identity", () => identityButton("clearSaved").click(), {
        identity: { ...emptySelection, open: true, hasSavedIdentity: false }
      });
      await expect(identityButton("clearSaved")).toHaveCount(0);
      const configuration = await readBoard.readConfigurationFiles();
      expect(JSON.parse(configuration["config.readboard.json"].replace(/^\uFEFF/, "")).FoxAutoPlayNickname).toBe("");
      await operate("close cleared identity", () => identityButton("close").click(), {
        identity: { open: false, hasSavedIdentity: false }
      });

      if ((await renderedState()).controlCenter.twoWaySync) {
        await operate("disable two-way sync", () => readBoard.page.locator('label:has(#two-way)').click(), {
          controlCenter: { twoWaySync: false, autoPlay: false }
        });
      }
      await operate("enable two-way sync", () => readBoard.page.locator('label:has(#two-way)').click(), {
        controlCenter: { twoWaySync: true, autoPlay: false }
      });
      await operate("enable autoplay", () => readBoard.page.locator('label:has(#auto-play)').click(), {
        controlCenter: { autoPlay: true, color: "", playColorKnown: false }
      });
      await expect(readBoard.page.locator('input[name="color"]:checked')).toHaveCount(0);
      for (const color of ["black", "white"]) {
        await operate(`select manual ${color}`, () => readBoard.page.locator(`label:has(input[name="color"][value="${color}"])`).click(), {
          controlCenter: { autoPlay: true, color, playColorKnown: true }
        });
        await expect(readBoard.page.locator('input[name="color"]:checked')).toHaveValue(color);
      }
      const retainedManualColor = { autoPlay: true, color: "white", playColorKnown: true };
      await operate("first automatic selection without identity", () => readBoard.page.locator('label:has(input[name="color"][value="auto"])').click(), {
        controlCenter: retainedManualColor,
        identity: { ...emptySelection, open: true, hasSavedIdentity: false }
      });
      await expectNoPlayers();
      await expect(readBoard.page.locator('input[name="color"]:checked')).toHaveValue("white");
      await readBoard.page.screenshot({ path: testInfo.outputPath("authorization-empty-automatic-selection.png") });
      await operate("cancel first automatic selection", () => identityButton("close").click(), {
        controlCenter: retainedManualColor,
        identity: { open: false, hasSavedIdentity: false }
      });
      await expect(readBoard.page.locator("#modal-layer")).toBeHidden();
      await operate("reopen identity without players", () => identityButton("open").click(), {
        controlCenter: retainedManualColor,
        identity: { ...emptySelection, open: true, hasSavedIdentity: false }
      });
      await expectNoPlayers();
      await operate("cancel reopened identity", () => identityButton("close").click(), {
        controlCenter: retainedManualColor,
        identity: { open: false, hasSavedIdentity: false }
      });
      await expect(readBoard.page.locator("#modal-layer")).toBeHidden();
      await expect(readBoard.page.locator('input[name="color"]:checked')).toHaveValue("white");
      expect(readBoard.host.transcript.filter(entry => entry.direction === "inbound" && entry.line.startsWith("play>"))).toEqual([]);
      expect(readBoard.pageErrors).toEqual([]);
      expect(readBoard.process.isRunning()).toBe(true);
      await readBoard.page.screenshot({ path: testInfo.outputPath("authorization-final-manual-color.png") });
    } finally {
      await testInfo.attach("authorization-state-publications.json", {
        body: Buffer.from(JSON.stringify({
          operations,
          received: await receivedStates(),
          finalRenderedState: await renderedState(),
          wireTranscript: readBoard.getWireTranscript(),
          notCovered: ["Identity selection and confirmation require actual detected Fox players; no candidates were injected.", "No real Fox placement or live recognition is exercised by FakeHost."]
        }, null, 2)),
        contentType: "application/json"
      });
    }
  }, { seedFoxIdentity: "ReadBoard native bridge saved identity" });
});

test("real Settings Cancel discards its draft and leaves persisted configuration unchanged", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    const initialConfiguration = await readBoard.readConfigurationFiles();
    expect(readPersistedSyncInterval(initialConfiguration)).toBe(200);

    await readBoard.page.locator('.nav-item[data-page="settings"]').click();
    const syncInterval = readBoard.page.locator('[data-setting="syncInterval"]');
    await expect(syncInterval).toHaveValue("200");
    await syncInterval.fill("350");
    await syncInterval.press("Tab");
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("You have unsaved changes");

    await readBoard.page.locator('[data-command="settings.cancel"]').click();
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("No unsaved changes");
    await expect(syncInterval).toHaveValue("200");
    expect(readPersistedSyncInterval(await readBoard.readConfigurationFiles())).toBe(200);
    expect(await readBoard.readConfigurationTransactionDirectories()).toEqual([]);

    await readBoard.page.locator('.nav-item[data-page="controlCenter"]').click();
    await readBoard.page.locator('.nav-item[data-page="settings"]').click();
    await expect(syncInterval).toHaveValue("200");
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("No unsaved changes");
    await readBoard.quitFromHost();
    expect(readPersistedSyncInterval(readBoard.postTeardownConfiguration)).toBe(200);
    expect(await readBoard.readConfigurationTransactionDirectories()).toEqual([]);
  }, { seedSyncInterval: 200 });
});

test("real Settings Save persists its draft across a fresh WebView2 profile restart", { tag: "@host-core" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    const firstProfile = readBoard.profileDirectory;

    await readBoard.page.locator('.nav-item[data-page="settings"]').click();
    const syncInterval = readBoard.page.locator('[data-setting="syncInterval"]');
    await expect(syncInterval).toHaveValue("200");
    await syncInterval.fill("350");
    await syncInterval.press("Tab");
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("You have unsaved changes");

    await readBoard.page.locator('[data-command="settings.save"]').click();
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("No unsaved changes");
    expect(readPersistedSyncInterval(await readBoard.readConfigurationFiles())).toBe(350);
    expect(await readBoard.readConfigurationTransactionDirectories()).toEqual([]);

    await readBoard.restartWithFreshProfile();
    expect(readBoard.profileDirectory).not.toBe(firstProfile);
    await readBoard.host.waitForExactLine("ready");
    await readBoard.page.locator('.nav-item[data-page="settings"]').click();
    await expect(readBoard.page.locator('[data-setting="syncInterval"]')).toHaveValue("350");
    await expect(readBoard.page.locator("#settings-dirty")).toHaveText("No unsaved changes");
    expect(readPersistedSyncInterval(await readBoard.readConfigurationFiles())).toBe(350);
    expect(await readBoard.readConfigurationTransactionDirectories()).toEqual([]);
  }, { seedSyncInterval: 200 });
});

test("production shell close sends ordered shutdown and exits cleanly", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
    await readBoard.host.waitForExactLine("ready");
    await expect(readBoard.page.locator(".app-shell")).toBeVisible();
    await expect(readBoard.page.locator('[data-command="window.close"]')).toBeVisible();

    const shutdown = await readBoard.closeFromShell();

    expect(shutdown.exitResult.code).toBe(0);
    expect(readBoard.cleanupState.cdpTargetClosed).toBe(true);
    expect(readBoard.host.transcript
      .slice(shutdown.startIndex)
      .filter(entry => entry.direction === "inbound" && ["stopsync", "nobothSync", "endsync"].includes(entry.line))
      .map(entry => entry.line)).toEqual(["stopsync", "nobothSync", "endsync"]);

    const configuration = await readBoard.readConfigurationFiles();
    expect(configuration["config.readboard.json"]).toEqual(expect.any(String));
    const jsonConfiguration = JSON.parse(configuration["config.readboard.json"].replace(/^\uFEFF/, ""));
    expect(jsonConfiguration).toEqual(expect.objectContaining({
      SyncMode: expect.any(Number),
      BoardWidth: expect.any(Number),
      BoardHeight: expect.any(Number),
      LanguagePreference: expect.stringMatching(/\S/)
    }));
    expect(configuration["config_readboard.txt"].split("_")).toHaveLength(12);
    expect(configuration["config_readboard_others.txt"].split("_")).toHaveLength(23);
  });
});
