const { defineConfig } = require("@playwright/test");

module.exports = defineConfig({
  testDir: "./tests/WebView",
  testMatch: "**/real-webview2-*.spec.js",
  forbidOnly: true,
  workers: 1,
  retries: 0,
  reporter: [
    ["line"],
    ["./scripts/webview2-host-execution-reporter.js"],
    ["json", { outputFile: "test-results/host-results.json" }]
  ],
  projects: [
    { name: "all" },
    { name: "core", grep: /(?:^|\s)@host-core(?=\s|$)/ },
    { name: "extended", grep: /(?:^|\s)@host-extended(?=\s|$)/ }
  ]
});
