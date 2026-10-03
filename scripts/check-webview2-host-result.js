const { CHANGES_RESULT, RUN_HEAVY } = process.env;

if (CHANGES_RESULT !== "success") {
  console.error(`Change detection did not succeed: ${CHANGES_RESULT}`);
  process.exitCode = 1;
} else if (RUN_HEAVY === "false") {
  console.log("Only documentation, templates, or update-channel metadata changed; host E2E was intentionally skipped.");
} else if (RUN_HEAVY !== "true") {
  console.error(`Invalid run_heavy output: ${JSON.stringify(RUN_HEAVY)}`);
  process.exitCode = 1;
} else {
  for (const name of ["COVERAGE_RESULT", "BUILD_RESULT", "CORE_RESULT", "EXTENDED_RESULT"]) {
    if (process.env[name] !== "success") {
      console.error(`${name} did not succeed: ${process.env[name]}`);
      process.exitCode = 1;
    }
  }
  if (!process.exitCode) console.log("All WebView2 host groups and coverage checks succeeded.");
}
