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
    window.readboardPreview.setState({
      page: "controlCenter",
      language: "en",
      text: {},
      shell: {
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
      },
      settings: {
        errors: { syncInterval: "Enter an integer no less than 20" },
        dirtyStatus: "Unsaved changes"
      },
      update: { open: false },
      identity: { open: false },
      dialog: { open: true, kind: "showInBoardHint", title: "Show on board", message: "Foreground mode is unavailable.", detail: "Enable two-way sync to restore placement.", confirmLabel: "Confirm", dontShowAgainLabel: "Do not show again" },
      logs: [{ time: "12:34:56", level: "WARN", message: "Initial warning" }]
    });
  });

  await expect(page.locator("#sync-status")).toHaveText("Syncing");
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

  await page.evaluate(() => {
    window.readboardPreview.setState({
      language: "en",
      update: {
        open: true,
        status: "check-failed",
        dialogTitle: "Check for updates",
        closeLabel: "Close",
        title: "Update check failed",
        detail: "Network error"
      },
      identity: { open: false },
      dialog: { open: false }
    });
  });

  await expect(page.locator("#modal-title")).toHaveText("Check for updates");
  await expect(page.locator("#modal-body")).toContainText("Update check failed");
  await expect(page.locator("#modal-body")).toContainText("Network error");
  await expect(page.locator('#modal-actions button[data-command="update.close"]')).toHaveText("Close");

  await page.evaluate(() => {
    window.readboardPreview.setState({
      update: { open: false },
      identity: {
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
        savedId: "candidate-1",
        hasSavedIdentity: true,
        candidates: [{ id: "candidate-1", label: "Player row 1", previewAlt: "Player row 1 screenshot", previewUrl: "data:image/png;base64,AA==" }]
      }
    });
  });

  await expect(page.locator("#modal-title")).toHaveText("Select Fox identity");
  await expect(page.locator("#modal-body")).toContainText("Player row 1");
  await expect(page.locator(".candidate input")).toBeChecked();
  await expect(page.locator(".candidate img")).toHaveAttribute("alt", "Player row 1 screenshot");
  await expect(page.locator('#modal-actions button[data-command="identity.saveAndUse"]')).toHaveText("Save and use");

  await page.evaluate(() => {
    window.readboardPreview.setState({
      language: "jp",
      shell: { syncStatus: "同期中", maximizeLabel: "元に戻す", maximized: true },
      update: { open: false },
      identity: { open: false },
      dialog: { open: false },
      logs: [{ time: "12:34:56", level: "WARN", message: "現在の言語の警告" }]
    });
  });

  await expect(page.locator("#sync-status")).toHaveText("同期中");
  await expect(page.locator('[data-command="window.maximize"]')).toHaveAttribute("aria-label", "元に戻す");
  await expect(page.locator("#log-list")).toContainText("現在の言語の警告");
  await expect(page.locator("#log-list")).not.toContainText("Initial warning");
});
