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

test("real Release ReadBoard publishes its first authoritative WebView2 snapshot", async ({}, testInfo) => {
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


test("real Control Center exchanges version, platform, and resume analysis state with its host", async ({}, testInfo) => {
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
  });
});

test("real Control Center waits for host analysis observations after pause", async ({}, testInfo) => {
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
test("real Settings Cancel discards its draft and leaves persisted configuration unchanged", async ({}, testInfo) => {
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

test("real Settings Save persists its draft across a fresh WebView2 profile restart", async ({}, testInfo) => {
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

test("production shell close sends ordered shutdown and exits cleanly", async ({}, testInfo) => {
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
