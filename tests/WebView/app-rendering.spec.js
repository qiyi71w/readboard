const { test, expect } = require("@playwright/test");
const http = require("http");
const fs = require("fs");
const path = require("path");

const webRoot = path.resolve(__dirname, "../../readboard/WebView");
let server;
let baseUrl;

function serveWebView(request, response) {
  const pathname = decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname);
  const filePath = path.resolve(webRoot, "." + pathname);
  if (!filePath.startsWith(webRoot + path.sep)) {
    response.writeHead(403);
    response.end();
    return;
  }

  fs.readFile(filePath, (error, content) => {
    if (error) {
      response.writeHead(error.code === "ENOENT" ? 404 : 500);
      response.end();
      return;
    }
    response.writeHead(200);
    response.end(content);
  });
}

test.beforeAll(async () => {
  server = http.createServer(serveWebView);
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  baseUrl = "http://127.0.0.1:" + server.address().port;
});

test.afterAll(async () => {
  await new Promise(resolve => server.close(resolve));
});

test("projects native thresholds over a focused draft without emitting updates", async ({ page }) => {
  const commands = [];
  page.on("console", message => {
    if (message.text().includes("ReadBoard preview command")) commands.push(message.text());
  });
  await page.goto(baseUrl + "/index.html");
  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.page = "settings";
    snapshot.settings = {
      ...snapshot.settings,
      thresholdsEditable: true,
      thresholdsDescription: "Shared by both Other modes. Changes take effect after saving settings.",
      thresholdsDefaultsDescription: "0 uses the default: black 33%, white 33%, black offset 96, white offset 96, gray offset 50.",
      thresholdsDraftWarning: null,
      blackPercent: "42", whitePercent: "0", blackOffset: "103", whiteOffset: "107", grayOffset: "61",
      syncInterval: "321", dirty: true, dirtyStatus: "Unsaved changes"
    };
    window.__customThresholdSnapshot = structuredClone(snapshot);
    window.readboardPreview.setState(snapshot);
  });
  const black = page.locator('[data-setting="blackPercent"]');
  const interval = page.locator('[data-setting="syncInterval"]');
  await expect(black).toBeEnabled();
  await expect(black).toHaveValue("42");
  await expect(page.locator("#thresholds-defaults")).toContainText("0 uses the default");
  await expect(interval).toBeEnabled();
  await black.focus();
  commands.length = 0;
  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.settings = {
      ...snapshot.settings,
      thresholdsEditable: false,
      thresholdsDescription: "Built-in platform values. Your Other-mode draft is retained and will also be saved.",
      blackPercent: "37", whitePercent: "30", blackOffset: "96", whiteOffset: "112", grayOffset: "50"
    };
    window.readboardPreview.setState(snapshot);
  });
  for (const [key, value] of Object.entries({ blackPercent: "37", whitePercent: "30", blackOffset: "96", whiteOffset: "112", grayOffset: "50" })) {
    const input = page.locator(`[data-setting="${key}"]`);
    await expect(input).toBeDisabled();
    await expect(input).toHaveValue(value);
  }
  await expect(interval).toBeEnabled();
  await expect(interval).toHaveValue("321");
  await expect(page.locator("#settings-dirty")).toHaveText("Unsaved changes");
  await expect(page.locator("#thresholds-description")).toContainText("draft is retained");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.settings.whiteOffset = "80";
    snapshot.settings.errors = { blackPercent: "Enter an integer" };
    snapshot.settings.thresholdsDraftWarning = "Switch to an Other mode to correct the draft, or Cancel to discard all unsaved changes.";
    window.readboardPreview.setState(snapshot);
  });
  await expect(page.locator('[data-setting="whiteOffset"]')).toHaveValue("80");
  await expect(page.locator("#thresholds-draft-warning")).toContainText("Switch to an Other mode");
  await expect(black).not.toHaveAttribute("aria-invalid");

  await page.evaluate(() => window.readboardPreview.setState(window.__customThresholdSnapshot));
  for (const [key, value] of Object.entries({ blackPercent: "42", whitePercent: "0", blackOffset: "103", whiteOffset: "107", grayOffset: "61" })) {
    const input = page.locator(`[data-setting="${key}"]`);
    await expect(input).toBeEnabled();
    await expect(input).toHaveValue(value);
  }
  await expect(interval).toBeEnabled();
  await expect(page.locator("#thresholds-draft-warning")).toHaveText("");
  await expect(page.locator("#settings-dirty")).toHaveText("Unsaved changes");
  expect(commands).toEqual([]);
});

test("renders dynamic snapshots, language-switched logs, and accessible controls", async ({ page }) => {
  await page.goto(baseUrl + "/index.html");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.page = "controlCenter";
    snapshot.language = "en";
    snapshot.text = {};
    snapshot.shell = {
      ...snapshot.shell,
      theme: "dark",
      connected: true,
      syncStatus: "Syncing",
      hostStatus: "Host ready",
      targetStatus: "Target valid",
      boardStatus: "Board recognized",
      placementStatus: "Placement resolved",
      targetWindowValid: true,
      boardRegionRecognized: true,
      placementRegionResolved: true,
      maximizeLabel: "Maximize"
    };
    snapshot.settings = {
      ...snapshot.settings,
      theme: "dark",
      diagnostics: true,
      errors: { syncInterval: "Enter an integer no less than 20" },
      dirtyStatus: "Unsaved changes"
    };
    snapshot.update = { open: false };
    snapshot.identity = { open: false };
    snapshot.dialog = {
      open: true,
      kind: "showInBoardHint",
      title: "Show on board",
      message: "Foreground mode is unavailable.",
      detail: "Enable two-way sync to restore placement.",
      confirmLabel: "Confirm",
      dontShowAgainLabel: "Do not show again"
    };
    snapshot.logs = [{ time: "12:34:56", level: "WARN", message: "Initial warning" }];
    window.readboardPreview.setState(snapshot);
  });

  await expect(page.locator("#sync-status")).toHaveText("Syncing");
  await expect(page.locator('[data-setting="diagnostics"]')).toBeChecked();
  await expect(page.locator('input[name="theme"][value="dark"]')).toBeChecked();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
  await expect(page.locator("#settings-dirty")).toHaveText("Unsaved changes");
  await expect(page.locator("#settings-error")).toHaveText("");
  const syncInterval = page.locator('[data-setting="syncInterval"]');
  await expect(syncInterval).toHaveAttribute("aria-invalid", "");
  await expect(syncInterval).toHaveAttribute("aria-describedby", /syncInterval-error/);
  await expect(page.locator('[data-setting="syncInterval"]').locator("xpath=../..").locator(".field-error")).toHaveText("Enter an integer no less than 20");
  await expect(page.locator("#log-list")).toContainText("Initial warning");
  await expect(page.locator('[data-command="window.maximize"]')).toHaveAttribute("aria-label", "Maximize");
  await expect(page.locator("#modal")).toHaveAttribute("role", "dialog");
  await expect(page.locator("#modal")).toHaveAttribute("aria-labelledby", "modal-title");
  await expect(page.locator("#modal-title")).toHaveText("Show on board");
  await expect(page.locator('#modal-actions button[data-command="dialog.confirm"]')).toHaveText("Confirm");

  const partialSnapshotIgnored = await page.evaluate(() => {
    window.readboardPreview.setState({ page: "settings", language: "jp" });
    return window.readboardPreview.getState();
  });
  expect(partialSnapshotIgnored.page).toBe("controlCenter");
  expect(partialSnapshotIgnored.language).toBe("en");
  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.language = "en";
    snapshot.update = {
      ...snapshot.update,
      open: true,
      status: "check-failed",
      dialogTitle: "Check for updates",
      closeLabel: "Close",
      title: "Update check failed",
      detail: "Network error"
    };
    snapshot.identity = { ...snapshot.identity, open: false };
    snapshot.dialog = { ...snapshot.dialog, open: false };
    window.readboardPreview.setState(snapshot);
  });

  await expect(page.locator("#modal-title")).toHaveText("Check for updates");
  await expect(page.locator("#modal-body")).toContainText("Update check failed");
  await expect(page.locator("#modal-body")).toContainText("Network error");
  await expect(page.locator('#modal-actions button[data-command="update.close"]')).toHaveText("Close");
  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.update = {
      ...snapshot.update,
      open: true,
      status: "processing",
      closeEnabled: false,
      installEnabled: false,
      openDownloadEnabled: false,
      dialogTitle: "Check for updates",
      title: "Installing update",
      detail: "Installing...",
      processingLabel: "Installing..."
    };
    window.readboardPreview.setState(snapshot);
  });
  await expect(page.locator('#modal-actions button[data-command="update.close"]')).toHaveCount(0);
  await page.keyboard.press("Escape");
  await expect(page.locator("#modal")).toBeVisible();
  await expect(page.locator("#modal-title")).toHaveText("Check for updates");
  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.update = { ...snapshot.update, closeEnabled: true };
    window.readboardPreview.setState(snapshot);
  });
  await expect(page.locator('#modal-actions button[data-command="update.close"]')).toHaveCount(1);


  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.update = { ...snapshot.update, open: false };
    snapshot.identity = {
      ...snapshot.identity,
      open: true,
      dialogTitle: "Select Fox identity",
      prompt: "Choose your player row.",
      detectedNicknamesLabel: "Detected rows",
      selectedLabel: "Selected:",
      cancelLabel: "Cancel",
      useOnceLabel: "Use once",
      saveAndUseLabel: "Save and use",
      savedLabel: "Saved",
      selectedId: "candidate-1",
      canUseOnce: true,
      canSaveAndUse: true,
      savedId: "candidate-1",
      hasSavedIdentity: true,
      candidates: [{ id: "candidate-1", label: "Player row 1", previewAlt: "Player row 1 screenshot", previewUrl: "data:image/png;base64,AA==" }]
    };
    snapshot.dialog = { ...snapshot.dialog, open: false };
    window.readboardPreview.setState(snapshot);
  });

  await expect(page.locator("#modal-title")).toHaveText("Select Fox identity");
  await expect(page.locator("#modal-body")).toContainText("Player row 1");
  await expect(page.locator("#modal-body")).not.toContainText("Selected:");
  await expect(page.locator(".candidate input")).toBeChecked();
  await expect(page.locator(".candidate img")).toHaveCount(0);
  await expect(page.locator(".candidate-list")).toHaveAttribute("role", "radiogroup");
  await expect(page.locator(".saved-pill")).toHaveText("Saved");
  await expect(page.locator('#modal-actions button[data-command="identity.clearSaved"]')).toHaveClass(/left/);
  await expect(page.locator('#modal-actions button[data-command="identity.saveAndUse"]')).toHaveText("Save and use");

  const projectedIdentityAvailability = await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.identity = {
      ...snapshot.identity,
      selectedId: "candidate-1",
      canUseOnce: false,
      canSaveAndUse: false
    };
    window.readboardPreview.setState(snapshot);
    return {
      useOnceDisabled: document.querySelector('[data-command="identity.useOnce"]').disabled,
      saveAndUseDisabled: document.querySelector('[data-command="identity.saveAndUse"]').disabled
    };
  });
  expect(projectedIdentityAvailability.useOnceDisabled).toBe(true);
  expect(projectedIdentityAvailability.saveAndUseDisabled).toBe(true);

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.language = "jp";
    snapshot.shell = { ...snapshot.shell, syncStatus: "同期中", maximizeLabel: "元に戻す", maximized: true };
    snapshot.update = { ...snapshot.update, open: false };
    snapshot.identity = { ...snapshot.identity, open: false };
    snapshot.dialog = { ...snapshot.dialog, open: false };
    snapshot.logs = [{ time: "12:34:56", level: "WARN", message: "現在の言語の警告" }];
    window.readboardPreview.setState(snapshot);
  });

  await expect(page.locator("#sync-status")).toHaveText("同期中");
  await expect(page.locator('[data-command="window.maximize"]')).toHaveAttribute("aria-label", "元に戻す");
  await expect(page.locator("#log-list")).toContainText("現在の言語の警告");
  await expect(page.locator("#log-list")).not.toContainText("Initial warning");
});

test("hides idle saved preference badge and aligns log level tags", async ({ page }) => {
  await page.clock.install();
  await page.goto(baseUrl + "/index.html");

  const badge = page.locator("#preferences-status");
  await expect(badge).toBeHidden();

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.controlCenter = {
      ...snapshot.controlCenter,
      preferencesSaved: true,
      preferencesStatus: "偏好已保存",
      persistenceError: null
    };
    snapshot.settings = {
      ...snapshot.settings,
      dirty: true,
      dirtyStatus: "有尚未保存的更改"
    };
    snapshot.logs = [
      { time: "12:34:56", level: "INFO", message: "info line" },
      { time: "12:34:57", level: "WARN", message: "warn line" },
      { time: "12:34:58", level: "SYNC", message: "sync line" }
    ];
    window.readboardPreview.setState(snapshot);
  });

  await expect(badge).toBeVisible();
  await expect(badge).toHaveClass(/not-saved/);
  await expect(badge).toHaveText("有尚未保存的更改");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.settings = {
      ...snapshot.settings,
      dirty: false,
      dirtyStatus: "当前没有未保存的更改"
    };
    window.readboardPreview.setState(snapshot);
  });

  await expect(badge).toBeVisible();
  await expect(badge).not.toHaveClass(/not-saved/);
  await expect(badge).toHaveText("偏好已保存");

  await page.clock.fastForward(2000);
  await expect(badge).toBeHidden();
  await expect(badge).toHaveText("");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.controlCenter = {
      ...snapshot.controlCenter,
      preferencesSaved: false,
      preferencesStatus: "当前选择已生效，但尚未保存",
      persistenceError: "disk full"
    };
    window.readboardPreview.setState(snapshot);
  });

  await expect(badge).toBeVisible();
  await expect(badge).toHaveClass(/not-saved/);
  await expect(badge).toHaveText("当前选择已生效，但尚未保存");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.controlCenter = {
      ...snapshot.controlCenter,
      preferencesSaved: true,
      preferencesStatus: "偏好已保存",
      persistenceError: null
    };
    window.readboardPreview.setState(snapshot);
  });

  await expect(badge).toBeVisible();
  await expect(badge).toHaveText("偏好已保存");
  await page.clock.fastForward(2000);
  await expect(badge).toBeHidden();


  const tagBoxes = await page.locator(".log-tag").evaluateAll(tags => tags.map(tag => {
    const box = tag.getBoundingClientRect();
    return { left: box.left, width: box.width, text: tag.textContent };
  }));
  expect(tagBoxes.map(tag => tag.text)).toEqual(["INFO", "WARN", "SYNC"]);
  expect(tagBoxes[0].width).toBeGreaterThan(40);
  expect(tagBoxes.every(tag => Math.abs(tag.left - tagBoxes[0].left) < 0.5)).toBe(true);
  expect(tagBoxes.every(tag => Math.abs(tag.width - tagBoxes[0].width) < 0.5)).toBe(true);
});

test("renders generic dialog confirm and cancel labels", async ({ page }) => {
  await page.goto(baseUrl + "/index.html");

  await page.evaluate(() => {
    const snapshot = window.readboardPreview.getState();
    snapshot.update = { open: false };
    snapshot.identity = { open: false };
    snapshot.dialog = {
      open: true,
      title: "无法同步",
      heading: "无法同步",
      message: "未选择棋盘,同步失败",
      confirmLabel: "确认",
      cancelLabel: "取消"
    };
    window.readboardPreview.setState(snapshot);
  });

  await expect(page.locator("#modal-title")).toHaveText("无法同步");
  await expect(page.locator('#modal-actions button[data-command="dialog.cancel"]')).toHaveText("取消");
  await expect(page.locator('#modal-actions button[data-command="dialog.confirm"]')).toHaveText("确认");
});

test("hosted shell waits for a complete backend snapshot", async ({ page }) => {
  await page.addInitScript(() => {
    window.__readboardPostedMessages = [];
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener(type, handler) {
            if (type === "message") window.__readboardWebViewMessageHandler = handler;
          },
          postMessage(message) {
            window.__readboardPostedMessages.push(message);
          }
        }
      }
    });
  });
  await page.goto(baseUrl + "/index.html");

  const initial = await page.evaluate(() => ({
    state: window.readboardPreview.getState(),
    awaitingState: document.body.classList.contains("awaiting-state"),
    shellVisibility: getComputedStyle(document.querySelector(".app-shell")).visibility,
    postedMessageCount: window.__readboardPostedMessages.length
  }));

  expect(initial.state).toBeNull();
  expect(initial.awaitingState).toBe(true);
  expect(initial.shellVisibility).toBe("hidden");
  expect(initial.postedMessageCount).toBe(0);
});

test("keeps native type when the resident panel is compact", async ({ page }) => {
  await page.setViewportSize({ width: 700, height: 433 });
  await page.goto(baseUrl + "/index.html");

  const metrics = await page.evaluate(() => {
    const root = document.documentElement;
    const shell = document.querySelector(".app-shell");
    const label = document.querySelector(".nav-item span");
    const labelBox = label ? label.getBoundingClientRect() : null;
    const unit = document.querySelector(".engine-unit");
    const card = document.querySelector(".sync-card");
    const unitBox = unit.getBoundingClientRect();
    const cardBox = card.getBoundingClientRect();
    const inputLefts = [...document.querySelectorAll(".engine-options input")].map(input => input.getBoundingClientRect().left);
    const sidebar = document.querySelector(".sidebar").getBoundingClientRect();
    const sidebarButtons = [...document.querySelectorAll(".nav-item, .quick-actions button")].map(button => {
      const box = button.getBoundingClientRect();
      return box.top >= sidebar.top - 0.5 && box.bottom <= sidebar.bottom + 0.5;
    });
    return {
      scale: window.readboardPreview.getLayoutMetrics().scale,
      dense: root.classList.contains("dense"),
      transform: getComputedStyle(shell).transform,
      fontSize: parseFloat(getComputedStyle(document.body).fontSize),
      labelWidth: labelBox ? labelBox.width : 0,
      unitInsideCard: unitBox.right <= cardBox.right + 0.5,
      inputLefts,
      sidebarButtonCount: sidebarButtons.length,
      sidebarButtonsVisible: sidebarButtons.every(Boolean)
    };
  });

  expect(metrics.scale).toBe(1);
  expect(metrics.dense).toBe(true);
  expect(metrics.transform).toBe("none");
  expect(metrics.fontSize).toBeGreaterThanOrEqual(13);
  expect(metrics.labelWidth).toBeLessThan(2);
  expect(metrics.unitInsideCard).toBe(true);
  expect(metrics.inputLefts).toHaveLength(3);
  expect(Math.max(...metrics.inputLefts) - Math.min(...metrics.inputLefts)).toBeLessThan(1);
  expect(metrics.sidebarButtonCount).toBe(11);
  expect(metrics.sidebarButtonsVisible).toBe(true);
  const compactLayout = await page.evaluate(() => window.readboardPreview.getLayoutMetrics());
  expect(compactLayout.denseX).toBe(true);
  expect(compactLayout.denseY).toBe(true);
  expect(compactLayout.sidebarIcons).toBe(true);
  expect(compactLayout.sidebarWidth).toBe(48);


  await page.locator('[data-page="settings"]').first().click();
  const settingsLayout = await page.evaluate(() => {
    const bar = document.querySelector(".settings-actions");
    const status = document.getElementById("settings-dirty");
    status.textContent = "当前没有未保存的更改";
    const reset = document.querySelector(".settings-actions > button");
    const save = document.querySelector(".settings-actions .primary");
    const overlap = (a, b) => !(a.right <= b.left + 0.5 || b.right <= a.left + 0.5 || a.bottom <= b.top + 0.5 || b.bottom <= a.top + 0.5);
    const barBox = bar.getBoundingClientRect();
    const resetBox = reset.getBoundingClientRect();
    const saveBox = save.getBoundingClientRect();
    return {
      overlapResetStatus: overlap(resetBox, status.getBoundingClientRect()),
      overlapStatusSave: overlap(status.getBoundingClientRect(), saveBox),
      resetInsideBar: resetBox.bottom <= barBox.bottom + 0.5 && resetBox.top >= barBox.top - 0.5,
      saveInsideBar: saveBox.bottom <= barBox.bottom + 0.5
    };
  });
  expect(settingsLayout.overlapResetStatus).toBe(false);
  expect(settingsLayout.overlapStatusSave).toBe(false);
  expect(settingsLayout.resetInsideBar).toBe(true);
  expect(settingsLayout.saveInsideBar).toBe(true);
});

async function afterViewportResize(page) {
  await page.evaluate(() => new Promise(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  }));
}

test("follows window width for the sidebar and keeps label hysteresis", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 680 });
  await page.goto(baseUrl + "/index.html");

  const factory = await page.evaluate(() => {
    const label = document.querySelector(".nav-item span");
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0,
      titleHeight: document.querySelector(".window-controls").getBoundingClientRect().height
    };
  });
  expect(factory.dense).toBe(false);
  expect(factory.sidebarIcons).toBe(false);
  expect(factory.sidebarWidth).toBe(230);
  expect(factory.labelWidth).toBeGreaterThan(20);
  expect(factory.titleHeight).toBe(48);

  await page.setViewportSize({ width: 1050, height: 680 });
  await afterViewportResize(page);
  const shrinking = await page.evaluate(() => {
    const label = document.querySelector(".nav-item span");
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0,
      titleHeight: document.querySelector(".window-controls").getBoundingClientRect().height
    };
  });
  expect(shrinking.dense).toBe(false);
  expect(shrinking.sidebarIcons).toBe(false);
  expect(shrinking.sidebarWidth).toBe(180);
  expect(shrinking.labelWidth).toBeGreaterThan(20);
  expect(shrinking.titleHeight).toBe(48);

  await page.setViewportSize({ width: 1000, height: 680 });
  await afterViewportResize(page);
  const icons = await page.evaluate(() => {
    const label = document.querySelector(".nav-item span");
    const input = document.querySelector(".engine-options input");
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0,
      titleHeight: document.querySelector(".window-controls").getBoundingClientRect().height,
      inputWidth: input ? input.getBoundingClientRect().width : 0
    };
  });
  expect(icons.dense).toBe(false);
  expect(icons.denseX).toBe(false);
  expect(icons.sidebarIcons).toBe(true);
  expect(icons.sidebarWidth).toBe(130);
  expect(icons.labelWidth).toBeLessThan(2);
  expect(icons.titleHeight).toBe(48);
  expect(icons.inputWidth).toBeGreaterThan(100);

  await page.setViewportSize({ width: 1020, height: 680 });
  await afterViewportResize(page);
  const hysteresis = await page.evaluate(() => window.readboardPreview.getLayoutMetrics());
  expect(hysteresis.sidebarIcons).toBe(true);
  expect(hysteresis.sidebarWidth).toBe(150);

  await page.setViewportSize({ width: 1034, height: 680 });
  await afterViewportResize(page);
  const restored = await page.evaluate(() => {
    const label = document.querySelector(".nav-item span");
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0
    };
  });
  expect(restored.sidebarIcons).toBe(false);
  expect(restored.sidebarWidth).toBe(164);
  expect(restored.labelWidth).toBeGreaterThan(20);


  await page.setViewportSize({ width: 1100, height: 600 });
  await afterViewportResize(page);
  const midHeight = await page.evaluate(() => window.readboardPreview.getLayoutMetrics());
  expect(midHeight.denseX).toBe(false);
  expect(midHeight.titleHeight).toBe(45);
  expect(midHeight.sidebarWidth).toBe(230);

  await page.setViewportSize({ width: 1200, height: 500 });
  await afterViewportResize(page);
  const shortWide = await page.evaluate(() => {
    const label = document.querySelector(".nav-item span");
    const sidebar = document.querySelector(".sidebar").getBoundingClientRect();
    const buttonsVisible = [...document.querySelectorAll(".nav-item, .quick-actions button")].every(button => {
      const box = button.getBoundingClientRect();
      return box.top >= sidebar.top - 0.5 && box.bottom <= sidebar.bottom + 0.5;
    });
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0,
      titleHeight: document.querySelector(".window-controls").getBoundingClientRect().height,
      buttonsVisible
    };
  });
  expect(shortWide.dense).toBe(false);
  expect(shortWide.denseX).toBe(false);
  expect(shortWide.denseY).toBe(true);
  expect(shortWide.sidebarIcons).toBe(false);
  expect(shortWide.sidebarWidth).toBe(230);
  expect(shortWide.labelWidth).toBeGreaterThan(20);
  expect(shortWide.titleHeight).toBe(42);
  expect(shortWide.buttonsVisible).toBe(true);
});

test("keeps labeled sidebar actions visible at short factory width", async ({ page }) => {
  const measure = () => page.evaluate(() => {
    const sidebar = document.querySelector(".sidebar").getBoundingClientRect();
    const label = document.querySelector(".nav-item span");
    const buttons = [...document.querySelectorAll(".nav-item, .quick-actions button")].map(button => {
      const box = button.getBoundingClientRect();
      return box.top >= sidebar.top - 0.5 && box.bottom <= sidebar.bottom + 0.5;
    });
    return {
      ...window.readboardPreview.getLayoutMetrics(),
      labelWidth: label ? label.getBoundingClientRect().width : 0,
      sidebarButtonCount: buttons.length,
      sidebarButtonsVisible: buttons.every(Boolean)
    };
  });

  await page.setViewportSize({ width: 1100, height: 433 });
  await page.goto(baseUrl + "/index.html");
  const minLabeled = await measure();
  expect(minLabeled.sidebarIcons).toBe(false);
  expect(minLabeled.sidebarWidth).toBe(230);
  expect(minLabeled.labelWidth).toBeGreaterThan(20);
  expect(minLabeled.sidebarButtonCount).toBe(11);
  expect(minLabeled.sidebarButtonsVisible).toBe(true);

  await page.setViewportSize({ width: 1100, height: 500 });
  await afterViewportResize(page);
  const shortLabeled = await measure();
  expect(shortLabeled.sidebarIcons).toBe(false);
  expect(shortLabeled.labelWidth).toBeGreaterThan(20);
  expect(shortLabeled.sidebarButtonCount).toBe(11);
  expect(shortLabeled.sidebarButtonsVisible).toBe(true);
});

test("clears color radio selection on empty snapshot without lingering checked state or outbound commands", async ({ page }) => {
  const commands = [];
  page.on("console", message => {
    if (message.text().includes("ReadBoard preview command")) commands.push(message.text());
  });
  await page.goto(baseUrl + "/index.html");
  const setColor = async (color, extras = {}) => {
    await page.evaluate(({ color, extras }) => {
      const snapshot = window.readboardPreview.getState();
      Object.assign(snapshot.controlCenter, {
        color, autoPlay: true, colorEnabled: true, autoColorEnabled: true, ...extras
      });
      window.readboardPreview.setState(snapshot);
    }, { color, extras });
  };
  const checked = page.locator('input[name="color"]:checked');
  for (const color of ["black", "white", "auto", "black", "white"]) {
    await setColor(color);
    await expect(checked).toHaveValue(color);
    await setColor("");
    await expect(checked).toHaveCount(0);
  }
  await setColor("auto", {
    platform: "fox", playColorKnown: false, autoPlayColorStatus: "Auto identifying"
  });
  await expect(checked).toHaveValue("auto");
  await expect(page.locator("#auto-play-color-status")).toHaveAttribute("data-known", "false");
  await setColor("", { colorEnabled: false, autoColorEnabled: false });
  await expect(checked).toHaveCount(0);
  for (const color of ["black", "white", "auto"])
    await expect(page.locator(`input[name="color"][value="${color}"]`)).toBeDisabled();
  expect(commands).toEqual([]);

  await setColor("");
  await page.locator('label:has(input[name="color"][value="black"])').click();
  await expect(checked).toHaveValue("black");
  await expect.poll(() => commands.length).toBe(1);
  await setColor("");
  await expect(checked).toHaveCount(0);
  expect(commands).toHaveLength(1);
});

for (const theme of ["light", "dark"]) {
  for (const miniMode of [false, true]) {
    test(`shows partial and complete color disablement in ${theme} ${miniMode ? "mini" : "full"} mode`, async ({ page }) => {
      await page.setViewportSize(miniMode ? { width: 680, height: 218 } : { width: 1100, height: 680 });
      await openHostedShell(page);
      const snapshot = hostedSnapshot({ theme, miniMode });
      snapshot.controlCenter = miniControlCenter({
        platform: "otherBackground", color: "", autoColorEnabled: false, identityEnabled: false,
        boardSelectionInsideEnabled: false, boardSelectionRectangleEnabled: true, boardSelectionLine1Enabled: true
      });
      await publish(page, snapshot);
      const surface = page.locator(miniMode ? "#mini-body" : '.page[data-page-panel="controlCenter"]');
      const colorName = miniMode ? "mini-color" : "color";
      const black = surface.locator(`input[name="${colorName}"][value="black"]`);
      const white = surface.locator(`input[name="${colorName}"][value="white"]`);
      const auto = surface.locator(`input[name="${colorName}"][value="auto"]`);
      const blackLabel = surface.locator(`label:has(input[name="${colorName}"][value="black"])`);
      const whiteLabel = surface.locator(`label:has(input[name="${colorName}"][value="white"])`);
      const autoLabel = surface.locator(`label:has(input[name="${colorName}"][value="auto"])`);
      const group = blackLabel.locator("..");
      const identity = surface.locator('[data-command="identity.open"]');
      const disabledColor = await identity.evaluate(element => getComputedStyle(element).color);
      const disabledBackground = await identity.evaluate(element => getComputedStyle(element).backgroundColor);
      const disabledBorder = await identity.evaluate(element => getComputedStyle(element).borderTopColor);

      await expect(black).toBeEnabled();
      await expect(white).toBeEnabled();
      await expect(auto).toBeDisabled();
      await expect(identity).toBeDisabled();
      await expect(blackLabel).not.toHaveCSS("color", disabledColor);
      await expect(whiteLabel).not.toHaveCSS("color", disabledColor);
      await expect(autoLabel).toHaveCSS("color", disabledColor);
      await expect(autoLabel).toHaveCSS("background-color", disabledBackground);
      await expect(group).not.toHaveCSS("background-color", disabledBackground);
      if (!miniMode) {
        await expect(surface.locator(".color-row > b")).not.toHaveCSS("color", disabledColor);
        const inside = surface.locator('[data-board-mode="inside"]');
        await expect(inside).toBeDisabled();
        await expect(inside).toHaveCSS("color", disabledColor);
        await expect(inside).toHaveCSS("border-top-color", disabledBorder);
      }
      await blackLabel.click();
      await expect(black).toBeChecked();
      await whiteLabel.click();
      await expect(white).toBeChecked();
      // Deliver the pointer click without waiting for this intentionally disabled control to enable.
      await autoLabel.click({ force: true });
      await expect(white).toBeChecked();

      Object.assign(snapshot.controlCenter, { autoPlay: false, color: "white", colorEnabled: false });
      await publish(page, snapshot);
      await expect(black).toBeDisabled();
      await expect(white).toBeDisabled();
      await expect(blackLabel).toHaveCSS("color", disabledColor);
      await expect(whiteLabel).toHaveCSS("color", disabledColor);
      await expect(group).toHaveCSS("background-color", disabledBackground);
      await expect(whiteLabel).not.toHaveCSS("background-color", disabledBackground);
      if (!miniMode) await expect(surface.locator(".color-row > b")).toHaveCSS("color", disabledColor);
      await blackLabel.click({ force: true });
      await expect(white).toBeChecked();

      Object.assign(snapshot.controlCenter, {
        platform: "fox", autoPlay: true, color: "", colorEnabled: true, autoColorEnabled: true,
        identityEnabled: true, boardSelectionInsideEnabled: true,
        boardSelectionRectangleEnabled: false, boardSelectionLine1Enabled: false
      });
      await publish(page, snapshot);
      await expect(auto).toBeEnabled();
      await expect(identity).toBeEnabled();
      await expect(autoLabel).not.toHaveCSS("color", disabledColor);
      await expect(group).not.toHaveCSS("background-color", disabledBackground);
      if (!miniMode) {
        const inside = surface.locator('[data-board-mode="inside"]');
        await expect(inside).toBeEnabled();
        await expect(inside).not.toHaveCSS("color", disabledColor);
      }
    });
  }
}

test("theme radio focus scrolls settings without moving the titlebar", async ({ page }) => {
  await page.goto(baseUrl + "/index.html");
  await page.locator('.nav-item[data-page="settings"]').click();
  const panel = page.locator('.page[data-page-panel="settings"]');
  const titlebar = page.locator(".titlebar");
  const assertChromeVisible = async () => {
    await expect.poll(() => titlebar.evaluate(element => element.getBoundingClientRect().top)).toBe(0);
    expect(await page.evaluate(() => window.scrollY)).toBe(0);
    await expect(titlebar.locator('[data-command="window.close"]')).toBeInViewport();
  };
  for (const viewport of [{ width: 1100, height: 760 }, { width: 1100, height: 680 }, { width: 700, height: 433 }, { width: 1100, height: 760 }]) {
    await page.setViewportSize(viewport);
    await afterViewportResize(page);
    await panel.evaluate(element => { element.scrollTop = element.scrollHeight; });
    for (const theme of ["dark", "light", "system"]) {
      await page.locator(`.appearance-theme label:has(input[value="${theme}"])`).click();
      await expect(page.locator(`input[name="theme"][value="${theme}"]`)).toBeChecked();
      await assertChromeVisible();
    }
    await page.locator('input[name="theme"]:checked').press("ArrowRight");
    await expect(page.locator('input[name="theme"][value="dark"]')).toBeChecked();
    await assertChromeVisible();
    const save = page.locator('[data-command="settings.save"]');
    await save.scrollIntoViewIfNeeded();
    await expect(save).toBeInViewport();
    await assertChromeVisible();
  }
});

function hostedSnapshot(shell) {
  return {
    page: "controlCenter",
    language: "cn",
    text: {},
    shell: {
      version: "v3.1.5",
      theme: "light",
      connected: true,
      syncStatus: "就绪",
      targetStatus: "目标窗口有效",
      boardStatus: "等待首次棋盘识别",
      placementStatus: "落子区域暂不可用",
      lastSync: "12:34:56",
      stoneCount: 42,
      targetWindowValid: true,
      boardRegionRecognized: false,
      placementRegionResolved: false,
      maximized: false,
      maximizeLabel: "最大化",
      miniMode: false,
      ...shell
    },
    controlCenter: {},
    settings: {},
    update: null,
    identity: null,
    dialog: null,
    logs: []
  };
}

async function openHostedShell(page) {
  await page.addInitScript(() => {
    window.__readboardPostedMessages = [];
    Object.defineProperty(window, "chrome", {
      configurable: true,
      value: {
        webview: {
          addEventListener(type, handler) {
            if (type === "message") window.__readboardWebViewMessageHandler = handler;
          },
          postMessage(message) {
            window.__readboardPostedMessages.push(message);
          }
        }
      }
    });
  });
  await page.goto(baseUrl + "/index.html");
}

async function publish(page, snapshot) {
  await page.evaluate(payload => window.__readboardWebViewMessageHandler({ data: { type: "state", payload } }), snapshot);
}

test("renders only the mini shell from shell.miniMode without the emergency layout", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);

  await publish(page, hostedSnapshot({ miniMode: true }));

  await expect(page.locator("#mini-shell")).toBeVisible();
  await expect(page.locator(".app-shell")).toBeHidden();
  await expect(page.locator("#mini-sync-status")).toHaveText("就绪");
  await expect(page.locator("#mini-stone-count")).toHaveText("42");
  await expect(page.locator("#mini-last-sync")).toHaveText("12:34:56");
  await expect(page.locator("#mini-target-light")).toHaveAttribute("title", "目标窗口有效");
  await expect(page.locator("#mini-target-dot")).toHaveClass(/good/);
  await expect(page.locator("#mini-board-light")).toHaveAttribute("title", "等待首次棋盘识别");
  const layout = await page.evaluate(() => ({
    metrics: window.readboardPreview.getLayoutMetrics(),
    scrollWidth: document.documentElement.scrollWidth,
    scrollHeight: document.documentElement.scrollHeight,
    titlebarHeight: document.querySelector(".mini-titlebar").getBoundingClientRect().height
  }));
  expect(layout.metrics.emergency).toBe(false);
  expect(layout.scrollWidth).toBeLessThanOrEqual(680);
  expect(layout.scrollHeight).toBeLessThanOrEqual(218);
  expect(layout.titlebarHeight).toBe(32);

  await page.setViewportSize({ width: 1100, height: 680 });
  await publish(page, hostedSnapshot({ miniMode: false }));

  await expect(page.locator(".app-shell")).toBeVisible();
  await expect(page.locator("#mini-shell")).toBeHidden();
  const topActions = page.locator(".top-actions button");
  await expect(topActions.first()).toHaveAttribute("data-command", "window.enterMiniMode");
  await expect(topActions.first().locator("span")).toHaveText("迷你模式");
  await expect(page.locator('.top-actions [data-page="settings"]')).toHaveCount(0);
  await expect(page.locator('.sidebar .nav-item[data-page="settings"]')).toHaveCount(1);
});

test("mini title bar and full-mode entry send window mode commands with exact payloads", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  await publish(page, hostedSnapshot({ miniMode: true }));

  await page.locator(".mini-full-mode").click();
  await page.locator('.mini-icon-button[data-payload*="settings"]').click();
  await page.locator('.mini-icon-button[data-payload*="rules"]').click();
  await page.locator('#mini-shell [data-command="window.minimize"]').click();

  await page.setViewportSize({ width: 1100, height: 680 });
  await publish(page, hostedSnapshot({ miniMode: false }));
  await page.locator('.top-actions [data-command="window.enterMiniMode"]').click();

  const posted = await page.evaluate(() => window.__readboardPostedMessages);
  expect(posted).toEqual([
    { type: "window.exitMiniMode", payload: { page: "controlCenter" } },
    { type: "window.exitMiniMode", payload: { page: "settings" } },
    { type: "window.exitMiniMode", payload: { page: "rules" } },
    { type: "window.minimize", payload: {} },
    { type: "window.enterMiniMode", payload: {} }
  ]);
});

function languageText(language) {
  const file = path.resolve(__dirname, `../../readboard/language_${language}.txt`);
  const text = {};
  for (const line of fs.readFileSync(file, "utf8").replace(/^\uFEFF/, "").split(/\r?\n/)) {
    const separator = line.indexOf("=");
    if (separator > 0) text[line.slice(0, separator)] = line.slice(separator + 1);
  }
  return text;
}

function miniControlCenter(overrides = {}) {
  return {
    platform: "fox",
    boardSize: "19",
    boardWidth: 19,
    boardHeight: 19,
    twoWaySync: true,
    autoPlay: true,
    color: "auto",
    placement: "direct",
    aiTime: 2,
    playouts: "",
    firstPolicy: "",
    showOnBoard: false,
    configurationEnabled: true,
    customBoardSizeEnabled: false,
    customBoardDimensionsEnabled: false,
    twoWaySyncEnabled: true,
    autoPlayToggleEnabled: true,
    colorEnabled: true,
    autoColorEnabled: true,
    placementEnabled: true,
    aiTimeEnabled: true,
    playoutsEnabled: true,
    firstPolicyEnabled: true,
    identityEnabled: true,
    showOnBoardEnabled: true,
    boardSelectionInsideEnabled: true,
    boardSelectionRectangleEnabled: false,
    boardSelectionLine1Enabled: false,
    quickSyncEnabled: true,
    continuousSyncEnabled: true,
    oneTimeSyncEnabled: true,
    analysisToggleEnabled: true,
    swapOrderEnabled: true,
    forceRebuildEnabled: true,
    clearBoardEnabled: true,
    quickSyncActive: false,
    continuousSyncActive: false,
    analysisRunning: false,
    quickSyncLabel: "快速同步",
    continuousSyncLabel: "持续同步 (200ms)",
    analysisLabel: "继续分析",
    preferencesSaved: true,
    preferencesStatus: "偏好已保存",
    persistenceError: null,
    ...overrides
  };
}

function miniSnapshot(controlOverrides = {}, extra = {}) {
  return { ...hostedSnapshot({ miniMode: true }), controlCenter: miniControlCenter(controlOverrides), ...extra };
}

test("mini panel values and availability follow snapshot enablement", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  const mini = selector => page.locator(`#mini-body ${selector}`);
  const color = value => mini(`input[name="mini-color"][value="${value}"]`);
  const placement = value => mini(`input[name="mini-placement"][value="${value}"]`);

  await publish(page, miniSnapshot({
    platform: "yike", boardSize: "13", twoWaySync: false, autoPlay: false, color: "black", placement: "engine",
    aiTime: 3, playouts: "800", showOnBoard: true,
    autoPlayToggleEnabled: false, colorEnabled: false, autoColorEnabled: false, placementEnabled: false,
    aiTimeEnabled: false, playoutsEnabled: false, firstPolicyEnabled: false, identityEnabled: false, showOnBoardEnabled: false
  }));
  await expect(mini("#mini-platform")).toHaveValue("yike");
  await expect(mini("#mini-platform")).toBeEnabled();
  await expect(mini("#mini-board-size")).toHaveValue("13");
  await expect(mini('#mini-board-size option[value="custom"]')).toBeDisabled();
  await expect(mini("#mini-board-width")).toBeDisabled();
  await expect(mini("#mini-board-width")).toHaveValue("19");
  await expect(mini("#mini-two-way")).not.toBeChecked();
  await expect(mini("#mini-auto-play")).toBeDisabled();
  await expect(color("black")).toBeChecked();
  for (const value of ["black", "white", "auto"]) await expect(color(value)).toBeDisabled();
  await expect(placement("engine")).toBeChecked();
  for (const value of ["direct", "engine"]) await expect(placement(value)).toBeDisabled();
  await expect(mini("#mini-ai-time")).toHaveValue("3");
  for (const id of ["ai-time", "playouts", "first-policy"]) await expect(mini(`#mini-${id}`)).toBeDisabled();
  await expect(mini("#mini-playouts")).toHaveValue("800");
  await expect(mini('[data-command="identity.open"]')).toBeDisabled();
  await expect(mini("#mini-show-on-board")).toBeChecked();
  await expect(mini("#mini-show-on-board")).toBeDisabled();

  await publish(page, miniSnapshot({
    platform: "yike", placement: "engine", color: "white",
    autoColorEnabled: false, firstPolicyEnabled: false, identityEnabled: false
  }));
  await expect(mini("#mini-auto-play")).toBeEnabled();
  await expect(mini("#mini-auto-play")).toBeChecked();
  await expect(color("black")).toBeEnabled();
  await expect(color("white")).toBeChecked();
  await expect(color("auto")).toBeDisabled();
  await expect(mini('[data-command="identity.open"]')).toBeDisabled();
  for (const value of ["direct", "engine"]) await expect(placement(value)).toBeEnabled();
  await expect(mini("#mini-ai-time")).toBeEnabled();
  await expect(mini("#mini-playouts")).toBeEnabled();
  await expect(mini("#mini-first-policy")).toBeDisabled();

  await publish(page, miniSnapshot({ configurationEnabled: false }));
  await expect(color("auto")).toBeEnabled();
  await expect(color("auto")).toBeChecked();
  await expect(mini('[data-command="identity.open"]')).toBeEnabled();
  await expect(mini("#mini-first-policy")).toBeEnabled();
  await expect(mini("#mini-platform")).toBeDisabled();
  await expect(mini("#mini-board-size")).toBeDisabled();

  const inside = mini('[data-board-mode="inside"]');
  const manual = [mini('[data-board-mode="rectangle"]'), mini('[data-board-mode="line1"]')];
  await expect(inside).toBeVisible();
  await expect(inside).toBeEnabled();
  for (const button of manual) await expect(button).toBeHidden();

  const manualPlatform = {
    platform: "otherForeground", boardSize: "custom", boardWidth: 21, boardHeight: 17,
    customBoardSizeEnabled: true, customBoardDimensionsEnabled: true, identityEnabled: false, autoColorEnabled: false,
    boardSelectionInsideEnabled: false, boardSelectionRectangleEnabled: true, boardSelectionLine1Enabled: true
  };
  await publish(page, miniSnapshot(manualPlatform));
  await expect(mini("#mini-board-size")).toHaveValue("custom");
  await expect(mini('#mini-board-size option[value="custom"]')).toBeEnabled();
  await expect(mini("#mini-board-width")).toBeEnabled();
  await expect(mini("#mini-board-width")).toHaveValue("21");
  await expect(mini("#mini-board-height")).toHaveValue("17");
  await expect(inside).toBeHidden();
  for (const button of manual) {
    await expect(button).toBeVisible();
    await expect(button).toBeEnabled();
  }

  await publish(page, miniSnapshot({
    ...manualPlatform, continuousSyncActive: true,
    boardSelectionRectangleEnabled: false, boardSelectionLine1Enabled: false
  }));
  await expect(inside).toBeHidden();
  for (const button of manual) {
    await expect(button).toBeVisible();
    await expect(button).toBeDisabled();
  }

  await publish(page, miniSnapshot({ platform: "sina" }));
  await expect(inside).toBeVisible();
  for (const button of manual) await expect(button).toBeHidden();

  const actions = ["sync.quick", "sync.continuous", "sync.once", "sync.toggleAnalysis", "sync.swapOrder", "sync.rebuild", "sync.clearBoard"];
  for (const command of actions) await expect(mini(`[data-command="${command}"]`)).toBeEnabled();
  await publish(page, miniSnapshot({
    quickSyncEnabled: false, continuousSyncEnabled: false, oneTimeSyncEnabled: false, analysisToggleEnabled: false,
    swapOrderEnabled: false, forceRebuildEnabled: false, clearBoardEnabled: false
  }));
  for (const command of actions) await expect(mini(`[data-command="${command}"]`)).toBeDisabled();
});

test("mini controls post the same commands and payloads as full mode", async ({ page }) => {
  await openHostedShell(page);
  const control = { autoPlay: false, customBoardSizeEnabled: true, customBoardDimensionsEnabled: true };
  const manualSelection = { boardSelectionInsideEnabled: false, boardSelectionRectangleEnabled: true, boardSelectionLine1Enabled: true };
  const run = async (miniMode, steps, overrides = control) => {
    await page.setViewportSize(miniMode ? { width: 680, height: 218 } : { width: 1100, height: 680 });
    await publish(page, { ...miniSnapshot(overrides), shell: hostedSnapshot({ miniMode }).shell });
    await page.evaluate(() => { window.__readboardPostedMessages.length = 0; });
    await steps(selector => page.locator(`${miniMode ? "#mini-shell" : ".app-shell"} ${selector}`));
    return page.evaluate(() => structuredClone(window.__readboardPostedMessages));
  };
  const fill = (locator, value) => locator.fill(value);
  const buttons = ['[data-board-mode="inside"]', '[data-command="identity.open"]', '[data-command="sync.quick"]',
    '[data-command="sync.continuous"]', '[data-command="sync.once"]', '[data-command="sync.toggleAnalysis"]',
    '[data-command="sync.swapOrder"]', '[data-command="sync.rebuild"]', '[data-command="sync.clearBoard"]'];

  const full = await run(false, async $ => {
    await $('input[name="platform"][value="yike"]').check();
    await $('input[name="boardSize"][value="custom"]').check();
    await fill($("#board-width"), "13");
    await fill($("#board-height"), "11");
    await $("#two-way").uncheck();
    await $("#auto-play").check();
    await $('label:has(input[name="color"][value="white"])').click();
    await $('label:has(input[name="placement"][value="engine"])').click();
    await fill($("#ai-time"), "5");
    await fill($("#playouts"), "100");
    await fill($("#first-policy"), "50");
    await $("#show-on-board").check();
    for (const button of buttons) await $(button).click();
  });
  const mini = await run(true, async $ => {
    await $("#mini-platform").selectOption("yike");
    await $("#mini-board-size").selectOption("custom");
    await fill($("#mini-board-width"), "13");
    await fill($("#mini-board-height"), "11");
    await $("#mini-two-way").uncheck();
    await $("#mini-auto-play").check();
    await $('label:has(input[name="mini-color"][value="white"])').click();
    await $('label:has(input[name="mini-placement"][value="engine"])').click();
    await fill($("#mini-ai-time"), "5");
    await fill($("#mini-playouts"), "100");
    await fill($("#mini-first-policy"), "50");
    await $("#mini-show-on-board").check();
    for (const button of buttons) await $(button).click();
  });

  expect(full).toHaveLength(21);
  expect(full[0]).toEqual({ type: "control.update", payload: { key: "platform", value: "yike" } });
  expect(full[20]).toEqual({ type: "sync.clearBoard", payload: {} });
  expect(mini).toEqual(full);

  const clickManualSelection = async $ => {
    await $('[data-board-mode="rectangle"]').click();
    await $('[data-board-mode="line1"]').click();
  };
  const fullManual = await run(false, clickManualSelection, manualSelection);
  const miniManual = await run(true, clickManualSelection, manualSelection);
  expect(fullManual).toEqual([
    { type: "board.select", payload: { mode: "rectangle" } },
    { type: "board.select", payload: { mode: "line1" } }
  ]);
  expect(miniManual).toEqual(fullManual);
});

test("mini action bar shows interval-free running labels from the snapshot text", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  const text = languageText("cn");
  const continuous = page.locator('#mini-body [data-command="sync.continuous"]');

  await publish(page, miniSnapshot({
    quickSyncActive: true, continuousSyncActive: true, analysisRunning: true,
    quickSyncLabel: "停止快速同步", continuousSyncLabel: "停止持续同步 (200ms)", analysisLabel: "暂停分析"
  }, { text }));
  await expect(page.locator("#mini-quick-label")).toHaveText(text.WebView_stopQuickSync);
  await expect(page.locator("#mini-continuous-label")).toHaveText(text.WebView_stopContinuousSync);
  await expect(page.locator("#mini-analysis-label")).toHaveText(text.WebView_pauseAnalysis);
  await expect(continuous).toHaveAttribute("title", "停止持续同步 (200ms)");
  await expect(continuous).toHaveAttribute("aria-pressed", "true");
  await expect(continuous.locator(".mini-live")).toBeVisible();

  await publish(page, miniSnapshot({}, { text }));
  await expect(page.locator("#mini-quick-label")).toHaveText(text.WebView_quickSync);
  await expect(page.locator("#mini-continuous-label")).toHaveText(text.WebView_continuousSync);
  await expect(page.locator("#mini-analysis-label")).toHaveText(text.WebView_resumeAnalysis);
  await expect(continuous).toHaveAttribute("title", "持续同步 (200ms)");
  await expect(continuous.locator(".mini-live")).toBeHidden();
});

test("mini title bar shows the unsaved preference state and a short saved flash", async ({ page }) => {
  await page.clock.install();
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  const text = languageText("cn");
  const chip = page.locator("#mini-preferences-status");

  await publish(page, miniSnapshot({}, { text }));
  await expect(chip).toBeHidden();

  await publish(page, miniSnapshot({
    preferencesSaved: false, preferencesStatus: text.WebView_preferencesNotSaved, persistenceError: "disk full"
  }, { text }));
  await expect(chip).toBeVisible();
  await expect(chip.locator(".icon")).toBeVisible();
  await expect(page.locator("#mini-preferences-text")).toHaveText(text.MiniMode_notSaved);
  await expect(chip).toHaveAttribute("title", "disk full");

  await publish(page, miniSnapshot({}, { text }));
  await expect(chip).toBeVisible();
  await expect(chip.locator(".icon")).toBeHidden();
  await expect(page.locator("#mini-preferences-text")).toHaveText(text.WebView_saved);
  await page.clock.fastForward(2000);
  await expect(chip).toBeHidden();
});

for (const language of ["cn", "en", "jp", "kr"]) {
  test(`mini panel labels fit without truncation in ${language}`, async ({ page }) => {
    await page.setViewportSize({ width: 680, height: 218 });
    await openHostedShell(page);
    const text = languageText(language);
    const widest = {
      platform: "otherForeground", boardSize: "custom", autoPlay: true,
      customBoardSizeEnabled: true, customBoardDimensionsEnabled: true, identityEnabled: false, autoColorEnabled: false,
      boardSelectionInsideEnabled: false, boardSelectionRectangleEnabled: true, boardSelectionLine1Enabled: true,
      quickSyncActive: true, continuousSyncActive: true
    };
    const states = [
      { ...widest, analysisRunning: true },
      { ...widest, analysisRunning: false },
      { boardSelectionInsideEnabled: true }
    ];
    for (const control of states) {
      await publish(page, miniSnapshot(control, { language, text }));
      await page.evaluate(() => document.fonts.ready);
      const report = await page.evaluate(() => {
        const body = document.querySelector("#mini-body");
        const visible = element => element.getClientRects().length > 0;
        const canvas = document.createElement("canvas").getContext("2d");
        const measure = (element, value) => {
          const style = getComputedStyle(element);
          canvas.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
          return canvas.measureText(value).width;
        };
        const clipped = [...body.querySelectorAll("[data-i18n]:not(option), .mini-label, #mini-quick-label, #mini-continuous-label, #mini-analysis-label")]
          .filter(element => visible(element) && element.scrollWidth > element.clientWidth)
          .map(element => ({ text: element.textContent, shown: element.clientWidth, needed: element.scrollWidth }));
        const selects = [...body.querySelectorAll("select")].flatMap(select => {
          const style = getComputedStyle(select);
          const room = select.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) - 20;
          return [...select.options]
            .map(option => ({ text: option.textContent, room, needed: measure(select, option.textContent) }))
            .filter(option => option.needed > option.room);
        });
        const inputs = [...body.querySelectorAll('input[type="number"]')].filter(visible).map(input => {
          const style = getComputedStyle(input);
          return {
            id: input.id,
            room: input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight),
            needed: measure(input, input.value || input.placeholder),
            appearance: style.appearance
          };
        });
        return {
          clipped,
          selects,
          inputs,
          bodyOverflow: body.scrollHeight > body.clientHeight || body.scrollWidth > body.clientWidth,
          documentOverflow: document.documentElement.scrollWidth > 680 || document.documentElement.scrollHeight > 218
        };
      });
      expect(report.clipped, JSON.stringify(control)).toEqual([]);
      expect(report.selects, JSON.stringify(control)).toEqual([]);
      expect(report.inputs.filter(input => input.needed > input.room)).toEqual([]);
      expect(report.inputs.every(input => input.appearance === "textfield")).toBe(true);
      expect(report.bodyOverflow).toBe(false);
      expect(report.documentOverflow).toBe(false);
    }
  });
}

function miniIdentity(text, count) {
  return {
    open: true,
    dialogTitle: text.WebView_selectIdentity,
    prompt: text.FoxAutoPlayIdentityDialog_lblPrompt,
    detectedNicknamesLabel: text.FoxAutoPlayIdentityDialog_lblDetectedNicknames,
    clearSavedLabel: text.FoxAutoPlayIdentityDialog_btnClearSavedIdentity,
    cancelLabel: text.FoxAutoPlayIdentityDialog_btnCancel,
    useOnceLabel: text.FoxAutoPlayIdentityDialog_btnUseOnce,
    saveAndUseLabel: text.FoxAutoPlayIdentityDialog_btnSaveAndUse,
    savedLabel: text.WebView_saved,
    selectedId: "c1",
    savedId: "c1",
    hasSavedIdentity: true,
    canUseOnce: true,
    canSaveAndUse: true,
    candidates: Array.from({ length: count }, (_, index) => ({ id: `c${index + 1}`, label: `野狐棋手Player${index + 1}` }))
  };
}

function miniShowInBoardHint(text) {
  return {
    open: true,
    kind: "showInBoardHint",
    title: text.TipsForm_title,
    message: text.WebView_showInBoardHintForeground,
    detail: text.WebView_showInBoardHintRestore,
    confirmLabel: text.TipsForm_btnConfirm,
    dontShowAgainLabel: text.TipsForm_btnNotAskAgain
  };
}

function miniMessageDialog(text, message = text.recgnizeFaild) {
  return {
    open: true,
    kind: "message",
    title: text.WebView_recognitionFailedTitle,
    message,
    confirmLabel: text.SettingsForm_btnConfirm,
    cancelLabel: text.SettingsForm_btnCancel
  };
}

async function miniModalLayout(page) {
  return page.evaluate(() => {
    const box = element => {
      const rect = element.getBoundingClientRect();
      return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
    };
    const hitAtCenter = element => {
      const rect = element.getBoundingClientRect();
      return document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
    };
    const onTop = element => element.contains(hitAtCenter(element));
    const modal = document.querySelector("#modal");
    const body = document.querySelector("#modal-body");
    const header = modal.querySelector(":scope > header");
    const actions = document.querySelector("#modal-actions");
    const titlebar = document.querySelector(".mini-titlebar");
    const layer = document.querySelector("#modal-layer");
    const candidates = [...document.querySelectorAll("#modal .candidate")];
    return {
      modal: box(modal),
      titlebar: box(titlebar),
      header: box(header),
      actions: box(actions),
      body: box(body),
      // The inert title bar is not hit-testable, so "visible" means the dialog layer does not cover it.
      titlebarUncovered: [...titlebar.querySelectorAll(".mini-status, .mini-window-controls button")].every(element => !layer.contains(hitAtCenter(element))),
      headerOnTop: onTop(header.querySelector("h2")),
      actionsOnTop: [...actions.querySelectorAll("button")].every(onTop),
      bodyScrolls: body.scrollHeight > body.clientHeight,
      modalScrolls: modal.scrollHeight > modal.clientHeight,
      candidateColumns: new Set(candidates.map(candidate => Math.round(candidate.getBoundingClientRect().left))).size,
      documentOverflow: document.documentElement.scrollWidth > 680 || document.documentElement.scrollHeight > 218
    };
  });
}

function expectInMiniPanel(layout) {
  expect(layout.titlebarUncovered).toBe(true);
  expect(layout.modal.top).toBe(layout.titlebar.bottom);
  expect(layout.modal).toMatchObject({ left: 0, right: 680, bottom: 218 });
  expect(layout.header.height).toBe(30);
  expect(layout.actions.height).toBe(42);
  expect(layout.headerOnTop).toBe(true);
  expect(layout.actionsOnTop).toBe(true);
  expect(layout.modalScrolls).toBe(false);
  expect(layout.documentOverflow).toBe(false);
}

for (const language of ["cn", "en", "jp", "kr"]) {
  test(`mini dialogs replace the panel below the title bar in ${language}`, async ({ page }) => {
    await page.setViewportSize({ width: 680, height: 218 });
    await openHostedShell(page);
    const text = languageText(language);
    const show = async extra => {
      await publish(page, miniSnapshot({}, { language, text, ...extra }));
      await page.evaluate(() => document.fonts.ready);
      return miniModalLayout(page);
    };

    const identity = await show({ identity: miniIdentity(text, 6) });
    expectInMiniPanel(identity);
    expect(identity.candidateColumns).toBe(3);
    expect(identity.bodyScrolls).toBe(false);

    for (const dialog of [miniShowInBoardHint(text), miniMessageDialog(text)]) {
      const layout = await show({ dialog });
      expectInMiniPanel(layout);
      expect(layout.bodyScrolls).toBe(false);
    }
  });
}

test("mini dialog overflow scrolls only the content area", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  const text = languageText("cn");
  const longMessage = Array.from({ length: 12 }, () => text.recgnizeFaild).join(" ");

  for (const extra of [{ identity: miniIdentity(text, 15) }, { dialog: miniMessageDialog(text, longMessage) }]) {
    await publish(page, miniSnapshot({}, { text, ...extra }));
    const before = await miniModalLayout(page);
    expectInMiniPanel(before);
    expect(before.bodyScrolls).toBe(true);

    await page.locator("#modal-body").evaluate(body => { body.scrollTop = body.scrollHeight; });
    const after = await miniModalLayout(page);
    expectInMiniPanel(after);
    expect(after.header).toEqual(before.header);
    expect(after.actions).toEqual(before.actions);
    const lastVisible = await page.locator("#modal-body").evaluate(body => {
      const last = body.lastElementChild.lastElementChild || body.lastElementChild;
      return last.getBoundingClientRect().bottom <= body.getBoundingClientRect().bottom + 0.5;
    });
    expect(lastVisible).toBe(true);
  }
});

test("mini panel stays non-interactive while a dialog is open", async ({ page }) => {
  await page.setViewportSize({ width: 680, height: 218 });
  await openHostedShell(page);
  const text = languageText("cn");
  const posted = () => page.evaluate(() => window.__readboardPostedMessages.splice(0));
  const centerOf = selector => page.locator(selector).evaluate(element => {
    const rect = element.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
  });
  const autoPlayPoint = await (async () => {
    await publish(page, miniSnapshot());
    return centerOf("#mini-auto-play");
  })();

  await publish(page, miniSnapshot({}, { text, dialog: miniShowInBoardHint(text) }));
  await posted();
  const fullModePoint = await centerOf(".mini-full-mode");
  await page.mouse.click(fullModePoint.x, fullModePoint.y);
  await page.mouse.click(autoPlayPoint.x, autoPlayPoint.y);
  await page.keyboard.press("Tab");
  await page.keyboard.press("Tab");
  await page.keyboard.press("Tab");
  const focusInsideModal = await page.evaluate(() => document.querySelector("#modal").contains(document.activeElement));
  const panelFocusable = await page.evaluate(() => {
    document.querySelector("#mini-platform").focus();
    return document.activeElement === document.querySelector("#mini-platform");
  });
  expect(await posted()).toEqual([]);
  expect(focusInsideModal).toBe(true);
  expect(panelFocusable).toBe(false);

  await publish(page, miniSnapshot({}, { text }));
  await page.mouse.click(autoPlayPoint.x, autoPlayPoint.y);
  expect((await posted()).map(message => message.type)).toEqual(["control.update"]);
});

test("mini dialogs post the same close commands as full mode", async ({ page }) => {
  await openHostedShell(page);
  const text = languageText("cn");
  const closeSteps = [
    [{ identity: miniIdentity(text, 2) }, '#modal > header [data-command="modal.close"]'],
    [{ identity: miniIdentity(text, 2) }, '#modal-actions [data-command="identity.close"]'],
    [{ dialog: miniShowInBoardHint(text) }, '#modal > header [data-command="modal.close"]'],
    [{ dialog: miniShowInBoardHint(text) }, '#modal-actions [data-command="dialog.confirm"]'],
    [{ dialog: miniShowInBoardHint(text) }, '#modal-actions [data-command="dialog.dontShowAgain"]'],
    [{ dialog: miniMessageDialog(text) }, '#modal-actions [data-command="dialog.cancel"]'],
    [{ dialog: miniMessageDialog(text) }, '#modal-actions [data-command="dialog.confirm"]']
  ];
  const run = async miniMode => {
    await page.setViewportSize(miniMode ? { width: 680, height: 218 } : { width: 1100, height: 680 });
    const sent = [];
    for (const [extra, selector] of closeSteps) {
      await publish(page, { ...miniSnapshot({}, { text, ...extra }), shell: hostedSnapshot({ miniMode }).shell });
      await page.evaluate(() => { window.__readboardPostedMessages.length = 0; });
      await page.locator(selector).click();
      sent.push(...await page.evaluate(() => structuredClone(window.__readboardPostedMessages)));
    }
    return sent;
  };

  const full = await run(false);
  expect(full.map(message => message.type)).toEqual([
    "identity.close", "identity.close", "dialog.cancel", "dialog.confirm", "dialog.dontShowAgain", "dialog.cancel", "dialog.confirm"
  ]);
  expect(await run(true)).toEqual(full);
});

test("full-mode dialog presentation is unchanged and survives a mini round trip", async ({ page }) => {
  await page.setViewportSize({ width: 1100, height: 680 });
  await openHostedShell(page);
  const text = languageText("cn");
  const fullLayout = () => page.evaluate(() => {
    const modal = document.querySelector("#modal").getBoundingClientRect();
    const layer = document.querySelector("#modal-layer").getBoundingClientRect();
    return {
      modal: { width: modal.width, centerX: modal.left + modal.width / 2, centerY: modal.top + modal.height / 2 },
      layer: { top: layer.top, height: layer.height },
      appShellInert: document.querySelector(".app-shell").inert
    };
  });
  const cases = [
    [{ identity: miniIdentity(text, 6) }, 480],
    [{ dialog: miniShowInBoardHint(text) }, 520],
    [{ dialog: miniMessageDialog(text) }, 580]
  ];

  for (const [extra, pixels] of cases) {
    const full = { ...miniSnapshot({}, { text, ...extra }), shell: hostedSnapshot({ miniMode: false }).shell };
    await page.setViewportSize({ width: 1100, height: 680 });
    await publish(page, full);
    const expected = { modal: { width: pixels, centerX: 550, centerY: 340 }, layer: { top: 0, height: 680 }, appShellInert: true };
    expect(await fullLayout()).toEqual(expected);

    await page.setViewportSize({ width: 680, height: 218 });
    await publish(page, { ...full, shell: hostedSnapshot({ miniMode: true }).shell });
    expectInMiniPanel(await miniModalLayout(page));

    await page.setViewportSize({ width: 1100, height: 680 });
    await publish(page, full);
    expect(await fullLayout()).toEqual(expected);
  }

  await publish(page, { ...hostedSnapshot({ miniMode: false }), text });
  await expect(page.locator("#modal-layer")).toBeHidden();
  expect(await page.evaluate(() => [document.querySelector(".app-shell").inert, document.querySelector("#mini-shell").inert])).toEqual([false, false]);
});
