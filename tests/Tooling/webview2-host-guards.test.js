const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { test } = require("node:test");

const root = path.resolve(__dirname, "../..");
const playwright = require.resolve("@playwright/test/cli");
const testImport = `const { test } = require(${JSON.stringify(require.resolve("@playwright/test"))});\n`;
const passing = [
  'test("core renamed without selection keywords", { tag: "@host-core" }, async () => {});',
  'test("extended renamed without selection keywords", { tag: "@host-extended" }, async () => {});'
].join("\n");

function workspace(t, source = passing, configOverride = "") {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "readboard-host-guards-"));
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const config = path.join(directory, "playwright.config.js");
  fs.writeFileSync(config, `const base = require(${JSON.stringify(path.join(root, "playwright.host.config.js"))});
module.exports = { ...base, testDir: __dirname, outputDir: ${JSON.stringify(path.join(directory, "results"))},
reporter: [["line"], [${JSON.stringify(path.join(root, "scripts/webview2-host-execution-reporter.js"))}]],
${configOverride}
};\n`);
  fs.writeFileSync(path.join(directory, "real-webview2-existing.spec.js"), testImport + source);
  return { directory, config };
}

function run(args, env = {}) {
  const result = spawnSync(process.execPath, args, {
    cwd: root, env: { ...process.env, ...env }, encoding: "utf8", timeout: 60_000
  });
  assert.ifError(result.error);
  return { status: result.status, output: result.stdout + result.stderr };
}

function coverage(fixture) {
  return run(["scripts/check-webview2-host-coverage.js", "--config", fixture.config]);
}

test("discovered host groups remain complete after titles change and every selected test executes", t => {
  const fixture = workspace(t);
  const checked = coverage(fixture);
  assert.equal(checked.status, 0, checked.output);
  assert.match(checked.output, /2 tests = core 1 \+ extended 1/);
  for (const group of ["core", "extended"]) {
    const executed = run([playwright, "test", "--config", fixture.config, "--project", group]);
    assert.equal(executed.status, 0, executed.output);
    assert.match(executed.output, /1 passed/);
  }
});

for (const newFile of [false, true]) {
  test(`ungrouped test in ${newFile ? "new" : "existing"} host file fails with its identity`, t => {
    const fixture = workspace(t);
    fs.appendFileSync(path.join(fixture.directory, newFile ? "real-webview2-added.spec.js" : "real-webview2-existing.spec.js"),
      (newFile ? testImport : "\n") + 'test("unassigned case", async () => {});');
    const result = coverage(fixture);
    assert.equal(result.status, 1, result.output);
    assert.match(result.output, /Ungrouped host test: .*unassigned case/);
    if (newFile) assert.match(result.output, /real-webview2-added\.spec\.js/);
  });
}

test("a test in both groups and an empty group fail explicitly", t => {
  const overlap = workspace(t, passing + '\ntest("ambiguous", { tag: ["@host-core", "@host-extended"] }, async () => {});');
  const duplicate = coverage(overlap);
  assert.equal(duplicate.status, 1, duplicate.output);
  assert.match(duplicate.output, /Overlapping host groups: .*ambiguous/);
  const empty = coverage(workspace(t, passing.replace('@host-extended', '@unassigned')));
  assert.equal(empty.status, 1, empty.output);
  assert.match(empty.output, /Empty host group: extended/);
});

test("equal counts cannot hide a different discovered test identity", t => {
  const fixture = workspace(t, passing, `projects: base.projects.map(project => project.name === "extended" ? { ...project, testMatch: "**/unrelated.spec.js" } : project),`);
  fs.writeFileSync(path.join(fixture.directory, "unrelated.spec.js"), testImport + 'test("wrong case", { tag: "@host-extended" }, async () => {});');
  const result = coverage(fixture);
  assert.equal(result.status, 1, result.output);
  assert.match(result.output, /Ungrouped host test: .*extended renamed/);
  assert.match(result.output, /Unexpected grouped test: .*wrong case/);
});

test("describe hierarchy and file keep identical leaf titles distinct", t => {
  const fixture = workspace(t, 'test.describe("first", () => { test("same", { tag: "@host-core" }, async () => {}); });\ntest.describe("second", () => { test("same", { tag: "@host-extended" }, async () => {}); });');
  fs.writeFileSync(path.join(fixture.directory, "real-webview2-another.spec.js"), testImport + 'test.describe("first", () => { test("same", { tag: "@host-extended" }, async () => {}); });');
  const result = coverage(fixture);
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /3 tests = core 1 \+ extended 2/);
});

for (const [name, source, diagnostic] of [
  ["declared skip", 'test.skip("not executed", { tag: "@host-extended" }, async () => {});', /skipped/],
  ["runtime skip", 'test("not executed", { tag: "@host-extended" }, async () => { test.skip(); });', /skipped/],
  ["expected failure", 'test("not executed", { tag: "@host-extended" }, async () => { test.fail(); throw new Error("expected failure"); });', /failed \(expected failed\)/]
]) {
  test(`${name} cannot produce a successful host execution`, t => {
    const fixture = workspace(t, source);
    const result = run([playwright, "test", "--config", fixture.config, "--project", "extended"]);
    assert.equal(result.status, 1, result.output);
    assert.match(result.output, /Host tests must actually pass without skips/);
    assert.match(result.output, diagnostic);
  });
}

test("required gate accepts only all-success or an explicit documentation skip", () => {
  const success = { CHANGES_RESULT: "success", RUN_HEAVY: "true", COVERAGE_RESULT: "success", BUILD_RESULT: "success", CORE_RESULT: "success", EXTENDED_RESULT: "success" };
  const gate = env => run(["scripts/check-webview2-host-result.js"], env);
  assert.equal(gate(success).status, 0);
  for (const field of ["CHANGES_RESULT", "COVERAGE_RESULT", "BUILD_RESULT", "CORE_RESULT", "EXTENDED_RESULT"]) {
    for (const status of ["failure", "cancelled", "skipped", ""]) {
      const result = gate({ ...success, [field]: status });
      assert.equal(result.status, 1, `${field}=${status}\n${result.output}`);
    }
  }
  for (const runHeavy of ["", "False", "unexpected"]) {
    const result = gate({ ...success, RUN_HEAVY: runHeavy });
    assert.equal(result.status, 1, result.output);
  }
  const docs = { ...success, RUN_HEAVY: "false", COVERAGE_RESULT: "skipped", BUILD_RESULT: "skipped", CORE_RESULT: "skipped", EXTENDED_RESULT: "skipped" };
  assert.equal(gate(docs).status, 0);
  assert.equal(gate({ ...docs, CHANGES_RESULT: "failure" }).status, 1);
});
