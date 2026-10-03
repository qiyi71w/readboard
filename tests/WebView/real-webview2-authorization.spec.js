const { test, expect } = require("@playwright/test");
const { publishRelease, removeDirectory, withRealWebView2Host, withNativeFoxWindow } = require("./real-webview2-host-fixture");

let publishDirectory;
test.setTimeout(300_000);
test.beforeAll(async () => { publishDirectory = await publishRelease(); }, 300_000);
test.afterAll(async () => {
  if (!process.env.READBOARD_PUBLISH_DIRECTORY) await removeDirectory(publishDirectory);
});

async function observeOperations(readBoard, testInfo, body) {
  await readBoard.host.waitForExactLine("ready");
  await expect(readBoard.page.locator("#host-state")).toHaveText("Host communication active");
  await expect(readBoard.page.locator("#log-list")).toContainText("Host mode started; ReadBoard is ready");
  await readBoard.page.evaluate(() => {
    window.nativeAuthorizationStates = [];
    window.chrome.webview.addEventListener("message", event => {
      if (event.data?.type === "state") window.nativeAuthorizationStates.push(structuredClone(event.data.payload));
    });
  });
  const state = () => readBoard.page.evaluate(() => window.readboardPreview.getState());
  const received = () => readBoard.page.evaluate(() => window.nativeAuthorizationStates);
  const operations = [];
  const operate = async (name, action, expected) => {
    const start = (await received()).length;
    await action();
    await expect.poll(async () => (await received()).length).toBeGreaterThan(start);
    await readBoard.page.evaluate(() => new Promise(resolve => setTimeout(resolve, 250)));
    const snapshots = (await received()).slice(start);
    operations.push({ name, snapshots });
    expect(snapshots, name).toHaveLength(1);
    for (const key of ["page", "language", "text", "shell", "controlCenter", "settings", "update", "identity", "dialog", "logs"])
      expect(snapshots[0]).toHaveProperty(key);
    expect(snapshots[0]).toMatchObject(expected);
    expect(await state()).toEqual(snapshots[0]);
    return snapshots[0];
  };
  try {
    await body({ state, operate });
    expect(readBoard.pageErrors).toEqual([]);
    expect(readBoard.process.isRunning()).toBe(true);
  } finally {
    await testInfo.attach("native-input-authorization.json", {
      body: Buffer.from(JSON.stringify({ operations, received: await received(), finalState: await state(), wire: readBoard.getWireTranscript(),
        scope: "Real WebView2, native HWND and UI Automation input fixture; not real Fox gameplay or placement." }, null, 2)),
      contentType: "application/json"
    });
  }
}

test("native player input confirms identity through the real WebView2 bridge", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withNativeFoxWindow(async nativeWindow => {
    await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
      await observeOperations(readBoard, testInfo, async ({ state, operate }) => {
        const button = command => readBoard.page.locator(`[data-command="identity.${command}"]`);
        await operate("enable two-way", () => readBoard.page.locator("label:has(#two-way)").click(), { controlCenter: { twoWaySync: true } });
        await operate("enable autoplay", () => readBoard.page.locator("label:has(#auto-play)").click(), { controlCenter: { autoPlay: true } });
        await operate("first automatic identity selection", () => readBoard.page.locator('label:has(input[name="color"][value="auto"])').click(), {
          controlCenter: { color: "", playColorKnown: false }, identity: { open: true }
        });
        await expect(readBoard.page.locator('input[name="candidate"]'), nativeWindow.diagnostics()).toHaveCount(2);
        const candidates = (await state()).identity.candidates;
        const black = candidates.find(candidate => candidate.label === "NativeBlack");
        expect(black).toBeDefined();
        await operate("select black identity", () => readBoard.page.locator("label.candidate").filter({ hasText: "NativeBlack" }).click(), {
          identity: { open: true, selectedId: black.id, canUseOnce: true }
        });
        await readBoard.page.screenshot({ path: testInfo.outputPath("native-identity-selected.png"), timeout: 15_000 });
        await operate("confirm use once", () => button("useOnce").click(), {
          controlCenter: { color: "auto", playColorKnown: false }, identity: { open: false, hasSavedIdentity: false }
        });
        await expect(readBoard.page.locator("#modal-layer")).toBeHidden();
        await operate("reopen process identity", () => button("open").click(), { identity: { open: true, selectedId: black.id } });
        await operate("confirm save and use", () => button("saveAndUse").click(), {
          controlCenter: { color: "auto", playColorKnown: false }, identity: { open: false, hasSavedIdentity: true }
        });
        const config = JSON.parse((await readBoard.readConfigurationFiles())["config.readboard.json"].replace(/^\uFEFF/, ""));
        expect(config.FoxAutoPlayNickname).toBe("NativeBlack");
        expect(readBoard.host.transcript.filter(entry => entry.direction === "inbound" && entry.line.startsWith("play>"))).toEqual([]);
        await readBoard.page.screenshot({ path: testInfo.outputPath("native-identity-confirmed.png"), timeout: 15_000 });
      });
    }, { seedFoxIdentity: "" });
  });
});

test("platform change refreshes a destroyed native target in one final snapshot", { tag: "@host-extended" }, async ({}, testInfo) => {
  await withNativeFoxWindow(async nativeWindow => {
    await withRealWebView2Host(publishDirectory, testInfo, async readBoard => {
      await readBoard.host.waitForExactLine("ready");
      await readBoard.page.locator('[data-command="sync.quick"]').click();
      await expect.poll(() => readBoard.page.evaluate(() => window.readboardPreview.getState().shell.targetWindowValid)).toBe(true);
      await readBoard.page.locator('[data-command="sync.quick"]').click();
      await expect.poll(() => readBoard.page.evaluate(() => window.readboardPreview.getState().controlCenter.quickSyncActive)).toBe(false);
      // Quick Sync honors the production auto-minimize preference; restore the real window before visual evidence.
      await readBoard.page.locator('[data-command="window.maximize"]').click();
      await expect.poll(() => readBoard.page.evaluate(() => window.readboardPreview.getState().shell.maximized)).toBe(true);
      await readBoard.page.locator('[data-command="window.maximize"]').click();
      await expect.poll(() => readBoard.page.evaluate(() => window.readboardPreview.getState().shell.maximized)).toBe(false);
      await nativeWindow.stop();
      await observeOperations(readBoard, testInfo, async ({ state, operate }) => {
        expect((await state()).shell.targetWindowValid).toBe(true);
        await operate("platform change after target destruction", () => readBoard.page.locator('label:has(input[name="platform"][value="otherForeground"])').click(), {
          shell: { targetWindowValid: false }, controlCenter: { platform: "otherForeground", titleBound: false }
        });
        await readBoard.page.screenshot({ path: testInfo.outputPath("destroyed-target-unbound.png"), timeout: 15_000 });
      });
    });
  });
});
