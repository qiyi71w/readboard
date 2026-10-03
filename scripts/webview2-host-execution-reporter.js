class HostExecutionReporter {
  constructor() {
    this.incomplete = [];
  }

  onTestEnd(test, result) {
    if (result.status !== "passed" || test.expectedStatus !== "passed") {
      this.incomplete.push(`${test.titlePath().join(" > ")}: ${result.status} (expected ${test.expectedStatus})`);
    }
  }

  async onEnd(result) {
    if (this.incomplete.length) {
      console.error(`Host tests must actually pass without skips:\n${this.incomplete.join("\n")}`);
      return { status: "failed" };
    }
    return { status: result.status };
  }
}

module.exports = HostExecutionReporter;
