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
