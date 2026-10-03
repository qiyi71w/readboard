const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { parseArgs } = require("node:util");

function discover(config, project) {
  const result = spawnSync(process.execPath, [
    require.resolve("@playwright/test/cli"), "test", "--config", config,
    "--project", project, "--list", "--pass-with-no-tests", "--reporter=json"
  ], { encoding: "utf8", maxBuffer: 16 * 1024 * 1024 });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`Discovery failed for ${project}:\n${result.stdout}\n${result.stderr}`);
  }
  const report = JSON.parse(result.stdout);
  if (report.errors.length) throw new Error(JSON.stringify(report.errors));
  const identities = new Set();
  function visit(suite, parents) {
    const titles = [...parents, suite.title];
    for (const spec of suite.specs || []) {
      // Include the file and describe hierarchy; never infer coverage from counts or line numbers.
      const identity = JSON.stringify([spec.file, ...titles.slice(1), spec.title]);
      if (identities.has(identity)) throw new Error(`Duplicate test identity: ${identity}`);
      identities.add(identity);
    }
    for (const child of suite.suites || []) visit(child, titles);
  }
  for (const suite of report.suites) visit(suite, []);
  return identities;
}

function checkCoverage(config) {
  const all = discover(config, "all");
  const core = discover(config, "core");
  const extended = discover(config, "extended");
  const errors = [];
  for (const [name, tests] of [["all", all], ["core", core], ["extended", extended]]) {
    if (!tests.size) errors.push(`Empty host group: ${name}`);
  }
  for (const identity of all) {
    if (!core.has(identity) && !extended.has(identity)) errors.push(`Ungrouped host test: ${identity}`);
    if (core.has(identity) && extended.has(identity)) errors.push(`Overlapping host groups: ${identity}`);
  }
  for (const identity of new Set([...core, ...extended])) {
    if (!all.has(identity)) errors.push(`Unexpected grouped test: ${identity}`);
  }
  if (errors.length) throw new Error(errors.join("\n"));
  console.log(`Host coverage complete: ${all.size} tests = core ${core.size} + extended ${extended.size}.`);
}

try {
  const { values } = parseArgs({ options: { config: { type: "string", default: path.resolve(__dirname, "../playwright.host.config.js") } } });
  checkCoverage(values.config);
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
