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
  await expect(page.locator(".candidate input")).toBeChecked();
  await expect(page.locator(".candidate img")).toHaveAttribute("alt", "Player row 1 screenshot");
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


