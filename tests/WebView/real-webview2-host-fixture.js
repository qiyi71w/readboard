const fs = require("node:fs/promises");
const net = require("node:net");
const os = require("node:os");
const path = require("node:path");
const { spawn } = require("node:child_process");
const { chromium } = require("@playwright/test");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const PROJECT_FILE = path.join(REPO_ROOT, "readboard", "readboard.csproj");
const APP_EXECUTABLE = "readboard.exe";
const HOST_CONNECT_TIMEOUT_MS = 30_000;
const BROWSER_READY_TIMEOUT_MS = 45_000;
const PROCESS_EXIT_TIMEOUT_MS = 15_000;
const CDP_CLOSE_TIMEOUT_MS = 15_000;
const CDP_OPERATION_TIMEOUT_MS = 5_000;
const PROFILE_CLEANUP_TIMEOUT_MS = 15_000;
const COMMAND_TIMEOUT_MS = 240_000;
const SHUTDOWN_LINES = ["stopsync", "nobothSync", "endsync"];

function waitForPollingTurn(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function waitForCondition(description, predicate, timeoutMs = 30_000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;

  while (Date.now() < deadline) {
    try {
      if (await predicate()) {
        return;
      }
    } catch (error) {
      lastError = error;
      if (error && error.stopPolling) {
        throw error;
      }
    }

    await waitForPollingTurn(Math.min(50, Math.max(1, deadline - Date.now())));
  }

  const suffix = lastError ? ` Last error: ${lastError.message}` : "";
  throw new Error(`Timed out waiting for ${description}.${suffix}`);
}
function withTimeout(operation, timeoutMs, description) {
  let timer;
  const operationPromise = Promise.resolve().then(operation);
  const timeoutPromise = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(`Timed out waiting for ${description}.`)), timeoutMs);
  });
  return Promise.race([operationPromise, timeoutPromise]).finally(() => clearTimeout(timer));
}

function createStopPollingError(message) {
  const error = new Error(message);
  error.stopPolling = true;
  return error;
}

function terminateCommandTree(child, command) {
  if (process.platform !== "win32" || !child.pid || path.basename(command).toLowerCase() === "taskkill.exe") {
    child.kill();
    return Promise.resolve();
  }

  return new Promise(resolve => {
    let timer;
    let settled = false;
    const finish = () => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      resolve();
    };

    let killer;
    try {
      killer = spawn("taskkill.exe", ["/PID", String(child.pid), "/T", "/F"], {
        stdio: "ignore",
        windowsHide: true
      });
    } catch {
      child.kill();
      finish();
      return;
    }
    killer.once("error", () => {
      child.kill();
      finish();
    });
    killer.once("close", finish);
    timer = setTimeout(() => {
      try {
        killer.kill();
      } catch {
        // The bounded cleanup attempt is complete.
      }
      finish();
    }, PROCESS_EXIT_TIMEOUT_MS);
  });
}

function runCommand(command, args, options = {}, timeoutMs = COMMAND_TIMEOUT_MS) {
  return new Promise((resolve, reject) => {
    let child;
    try {
      child = spawn(command, args, {
        ...options,
        stdio: ["ignore", "pipe", "pipe"]
      });
    } catch (error) {
      reject(error);
      return;
    }

    let stdout = "";
    let stderr = "";
    let spawnError = null;
    let timer;
    let terminationTimer;
    let timedOut = false;
    let settled = false;
    const finish = result => {
      if (settled) {
        return;
      }
      settled = true;
      clearTimeout(timer);
      clearTimeout(terminationTimer);
      resolve({ command, args, stdout, stderr, timedOut, ...result });
    };

    child.stdout.on("data", chunk => {
      stdout += chunk.toString();
    });
    child.stderr.on("data", chunk => {
      stderr += chunk.toString();
    });
    child.once("error", error => {
      spawnError = error;
    });
    child.once("close", (code, signal) => {
      finish({
        error: timedOut ? `Command timed out after ${timeoutMs}ms.` : spawnError ? spawnError.message : null,
        code,
        signal
      });
    });
    timer = setTimeout(() => {
      timedOut = true;
      void terminateCommandTree(child, command).then(() => {
        if (settled) {
          return;
        }
        terminationTimer = setTimeout(() => {
          finish({ error: `Command did not exit after timeout ${timeoutMs}ms.`, code: null, signal: "SIGTERM" });
        }, PROCESS_EXIT_TIMEOUT_MS);
      });
    }, timeoutMs);
  });
}

async function publishRelease() {
  if (process.platform !== "win32") {
    throw new Error("The real WebView2 host test requires Windows and Evergreen WebView2.");
  }

  const publishedDirectory = process.env.READBOARD_PUBLISH_DIRECTORY;
  if (publishedDirectory) {
    const resolvedDirectory = path.resolve(publishedDirectory);
    await fs.access(path.join(resolvedDirectory, APP_EXECUTABLE));
    await fs.access(path.join(resolvedDirectory, "WebView", "index.html"));
    await fs.access(path.join(resolvedDirectory, "WebView2Loader.dll"));
    return resolvedDirectory;
  }

  const publishDirectory = await fs.mkdtemp(path.join(os.tmpdir(), "readboard-webview2-publish-"));
  const dotnet = process.env.DOTNET_EXE || "dotnet";
  const args = [
    "publish",
    PROJECT_FILE,
    "-c",
    "Release",
    "-r",
    "win-x64",
    "--self-contained",
    "true",
    "--output",
    publishDirectory
  ];

  try {
    const result = await runCommand(dotnet, args, { cwd: REPO_ROOT, windowsHide: true });
    if (result.timedOut || result.error || result.code !== 0) {
      throw new Error(
        `dotnet publish failed${result.timedOut ? " by timeout" : ` with exit code ${result.code}`}.\nerror: ${result.error || "none"}\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`
      );
    }

    await fs.access(path.join(publishDirectory, APP_EXECUTABLE));
    await fs.access(path.join(publishDirectory, "WebView", "index.html"));
    const webView2LoaderSource = path.join(
      publishDirectory,
      "runtimes",
      "win-x64",
      "native",
      "WebView2Loader.dll"
    );
    await fs.copyFile(webView2LoaderSource, path.join(publishDirectory, "WebView2Loader.dll"));
    return publishDirectory;
  } catch (error) {
    try {
      await removeDirectory(publishDirectory);
    } catch (cleanupError) {
      throw new AggregateError([error, cleanupError], "Release publish and temporary cleanup failed.");
    }
    throw error;
  }
}

async function removeDirectory(directory) {
  if (!directory) {
    return;
  }

  await waitForCondition(
    `temporary directory ${directory} to become removable`,
    async () => {
      try {
        await fs.rm(directory, { recursive: true, force: true });
        return true;
      } catch (error) {
        if (!["EBUSY", "ENOTEMPTY", "EPERM"].includes(error.code)) {
          throw createStopPollingError(`Failed to remove temporary directory ${directory}: ${error.message}`);
        }
        return false;
      }
    },
    PROFILE_CLEANUP_TIMEOUT_MS
  );
}
async function findNamedFile(directory, fileName) {
  let entries;
  try {
    entries = await fs.readdir(directory, { withFileTypes: true });
  } catch (error) {
    if (error.code === "ENOENT") {
      return null;
    }
    throw error;
  }

  for (const entry of entries) {
    const candidate = path.join(directory, entry.name);
    if (entry.isFile() && entry.name === fileName) {
      return candidate;
    }
    if (entry.isDirectory()) {
      const nested = await findNamedFile(candidate, fileName);
      if (nested) {
        return nested;
      }
    }
  }
  return null;
}


class FakeHost {
  constructor() {
    this.server = null;
    this.socket = null;
    this.connected = false;
    this.socketErrors = [];
    this.transcript = [];
    this.lineWaiters = [];
    this.connectedPromise = new Promise(resolve => {
      this.resolveConnected = resolve;
    });
  }

  async start() {
    this.server = net.createServer(socket => this.handleConnection(socket));
    await new Promise((resolve, reject) => {
      this.server.once("error", reject);
      this.server.listen(0, "127.0.0.1", resolve);
    });
    const address = this.server.address();
    this.port = typeof address === "object" && address ? address.port : 0;
    return this.port;
  }

  handleConnection(socket) {
    if (this.socket) {
      socket.destroy();
      return;
    }

    this.socket = socket;
    this.connected = true;
    this.resolveConnected();
    let buffer = "";
    socket.setEncoding("utf8");
    socket.on("data", chunk => {
      buffer += chunk;
      const lines = buffer.split(/\n/);
      buffer = lines.pop();
      for (const rawLine of lines) {
        this.recordLine("inbound", rawLine.endsWith("\r") ? rawLine.slice(0, -1) : rawLine);
      }
    });
    socket.on("close", () => {
      this.connected = false;
    });
    socket.on("error", error => {
      this.connected = false;
      this.socketErrors.push({ message: error.message, code: error.code });
      for (const waiter of this.lineWaiters.splice(0)) {
        waiter.reject(new Error(`Fake host socket failed: ${error.message}`));
      }
    });
  }

  recordLine(direction, line) {
    const entry = { direction, line, timestamp: new Date().toISOString() };
    this.transcript.push(entry);
    for (const waiter of this.lineWaiters.splice(0)) {
      if (waiter.line === line) {
        waiter.resolve();
      } else {
        this.lineWaiters.push(waiter);
      }
    }
  }

  async waitForConnection() {
    let timer;
    try {
      await Promise.race([
        this.connectedPromise,
        new Promise((_, reject) => {
          timer = setTimeout(
            () => reject(new Error("Timed out waiting for the ReadBoard TCP connection.")),
            HOST_CONNECT_TIMEOUT_MS
          );
        })
      ]);
    } finally {
      clearTimeout(timer);
    }
  }

  async waitForExactLine(line, timeoutMs = HOST_CONNECT_TIMEOUT_MS) {
    if (this.transcript.some(entry => entry.direction === "inbound" && entry.line === line)) {
      return;
    }

    await new Promise((resolve, reject) => {
      let timer;
      const waiter = {
        line,
        resolve: () => {
          clearTimeout(timer);
          resolve();
        },
        reject: error => {
          clearTimeout(timer);
          reject(error);
        }
      };
      this.lineWaiters.push(waiter);
      timer = setTimeout(() => {
        const index = this.lineWaiters.indexOf(waiter);
        if (index >= 0) {
          this.lineWaiters.splice(index, 1);
        }
        reject(new Error(`Timed out waiting for fake host line ${JSON.stringify(line)}.`));
      }, timeoutMs);
    });
  }

  async waitForOrderedLines(lines, startIndex, timeoutMs = HOST_CONNECT_TIMEOUT_MS) {
    let cursor = startIndex;
    let nextLine = 0;
    await waitForCondition(
      `fake host shutdown sequence ${JSON.stringify(lines)}`,
      () => {
        while (cursor < this.transcript.length) {
          const entry = this.transcript[cursor++];
          if (entry.direction !== "inbound" || !SHUTDOWN_LINES.includes(entry.line)) {
            continue;
          }
          if (entry.line !== lines[nextLine]) {
            throw createStopPollingError(
              `Fake host observed ${JSON.stringify(entry.line)} before ${JSON.stringify(lines[nextLine])}.`
            );
          }
          nextLine += 1;
          if (nextLine === lines.length) {
            return true;
          }
        }
        return false;
      },
      timeoutMs
    );
  }

  async sendLine(line) {
    await this.waitForConnection();
    this.recordLine("outbound", line);
    await withTimeout(
      () => new Promise((resolve, reject) => {
        this.socket.write(`${line}\r\n`, error => (error ? reject(error) : resolve()));
      }),
      HOST_CONNECT_TIMEOUT_MS,
      `fake host write ${JSON.stringify(line)}`
    );
  }

  async close() {
    for (const waiter of this.lineWaiters.splice(0)) {
      waiter.reject(new Error("Fake host closed while waiting for a line."));
    }
    if (this.socket) {
      this.socket.destroy();
      this.socket = null;
    }
    if (this.server) {
      await withTimeout(
        () => new Promise(resolve => this.server.close(() => resolve())),
        HOST_CONNECT_TIMEOUT_MS,
        "fake host close"
      );
      this.server = null;
    }
  }
}

class ReadBoardProcess {
  constructor(executable, args, options) {
    this.executable = executable;
    this.args = args;
    this.options = options;
    this.stdout = "";
    this.stderr = "";
    this.spawnError = null;
    this.exitResult = null;
    this.exitPromise = new Promise(resolve => {
      this.resolveExit = resolve;
    });
  }

  start() {
    this.child = spawn(this.executable, this.args, {
      ...this.options,
      stdio: ["ignore", "pipe", "pipe"]
    });
    this.pid = this.child.pid;
    this.child.stdout.on("data", chunk => {
      this.stdout += chunk.toString();
    });
    this.child.stderr.on("data", chunk => {
      this.stderr += chunk.toString();
    });
    this.child.once("error", error => {
      this.spawnError = error;
    });
    this.child.once("close", (code, signal) => {
      this.exitResult = {
        error: this.spawnError ? this.spawnError.message : null,
        code,
        signal
      };
      this.resolveExit(this.exitResult);
    });
    return this;
  }

  isRunning() {
    return Boolean(this.child && this.exitResult === null);
  }

  async waitForExit(timeoutMs = PROCESS_EXIT_TIMEOUT_MS) {
    let timer;
    try {
      return await Promise.race([
        this.exitPromise,
        new Promise((_, reject) => {
          timer = setTimeout(
            () => reject(new Error(`Timed out waiting for ReadBoard process ${this.pid} to exit.`)),
            timeoutMs
          );
        })
      ]);
    } finally {
      clearTimeout(timer);
    }
  }
}

async function terminateProcessTree(readBoardProcess) {
  if (!readBoardProcess.isRunning()) {
    return;
  }

  if (process.platform === "win32") {
    await runCommand("taskkill.exe", ["/PID", String(readBoardProcess.pid), "/T", "/F"], {
      cwd: REPO_ROOT,
      windowsHide: true
    }, PROCESS_EXIT_TIMEOUT_MS);
  } else {
    try {
      process.kill(-readBoardProcess.pid, "SIGKILL");
    } catch (error) {
      if (error.code !== "ESRCH") {
        throw error;
      }
    }
  }

  await readBoardProcess.waitForExit(PROCESS_EXIT_TIMEOUT_MS);
}

class RealWebView2HostFixture {
  constructor(publishDirectory) {
    this.publishDirectory = publishDirectory;
    this.testDirectory = null;
    this.appDirectory = null;
    this.profileDirectory = null;
    this.browser = null;
    this.page = null;
    this.pageClosed = false;
    this.shutdownObserved = false;
    this.preTeardownEvidence = null;
    this.preTeardownRuntimeVersion = null;
    this.preTeardownConfiguration = null;
    this.postTeardownConfiguration = null;
    this.runtimeVersion = null;
    this.transcriptHistory = [];
    this.pageConsole = [];
    this.pageErrors = [];
    this.requestFailures = [];
    this.cleanupState = {
      escalated: false,
      processTreeTerminated: false,
      cdpTargetClosed: false,
      removedTestDirectory: false,
      errors: []
    };
  }


  async start(options = {}) {
    this.testDirectory = await fs.mkdtemp(path.join(os.tmpdir(), "readboard-webview2-host-"));
    this.appDirectory = path.join(this.testDirectory, "app");
    this.profileDirectory = path.join(this.testDirectory, "profile");
    await fs.mkdir(this.profileDirectory, { recursive: true });
    await fs.cp(this.publishDirectory, this.appDirectory, { recursive: true });
    if (options.seedSyncInterval !== undefined)
      await this.seedConfiguration(options.seedSyncInterval);
    await this.launchProcess();
    return this;
  }

  async seedConfiguration(syncInterval) {
    if (!Number.isInteger(syncInterval) || syncInterval < 20)
      throw new Error(`Invalid seeded sync interval: ${syncInterval}`);
    const machineName = process.env.ComputerName || process.env.COMPUTERNAME || os.hostname();
    const machineKey = machineName.replace(/_/g, "");
    await fs.writeFile(
      path.join(this.appDirectory, "config.readboard.json"),
      JSON.stringify({ ProtocolVersion: "220430", MachineKey: machineKey, SyncIntervalMs: syncInterval }, null, 2),
      "utf8"
    );
  }

  async launchProcess() {
    this.host = new FakeHost();
    this.hostPort = await this.host.start();
    this.cdpPort = null;
    this.cdpEndpoint = null;
    this.pageClosed = false;
    this.browser = null;
    this.page = null;
    this.process = new ReadBoardProcess(
      path.join(this.appDirectory, APP_EXECUTABLE),
      ["yzy", " ", " ", " ", "1", "en", String(this.hostPort)],
      {
        cwd: this.appDirectory,
        windowsHide: true,
        env: {
          ...process.env,
          WEBVIEW2_USER_DATA_FOLDER: this.profileDirectory,
          WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: "--remote-debugging-port=0"
        }
      }
    ).start();
    await this.host.waitForConnection();
    await this.attachToPage();
  }

  async restartWithFreshProfile() {
    if (!this.process || !this.process.isRunning())
      throw new Error("Cannot restart a ReadBoard process that is not running.");
    const startIndex = this.host.transcript.length;
    await this.host.sendLine("quit");
    await this.host.waitForOrderedLines(SHUTDOWN_LINES, startIndex);
    const exitResult = await this.process.waitForExit();
    if (exitResult.code !== 0)
      throw new Error(`ReadBoard exited with ${JSON.stringify(exitResult)} during restart.`);
    await this.waitForCdpTargetClosure();
    if (this.browser && this.browser.isConnected()) {
      await withTimeout(
        () => this.browser.close({ reason: "ReadBoard settings restart" }),
        CDP_OPERATION_TIMEOUT_MS,
        "the WebView2 CDP client close during restart"
      );
    }
    this.transcriptHistory.push(this.host.transcript);
    await this.host.close();
    this.profileDirectory = path.join(this.testDirectory, "profile-restart");
    await fs.mkdir(this.profileDirectory, { recursive: true });
    await this.launchProcess();
    return exitResult;
  }

  getWireTranscript() {
    return [...this.transcriptHistory.flat(), ...(this.host ? this.host.transcript : [])];
  }

  async attachToPage() {
    await waitForCondition(
      "the WebView2 DevToolsActivePort file",
      async () => {
        if (!this.process.isRunning()) {
          throw createStopPollingError(`ReadBoard exited before CDP became available: ${JSON.stringify(this.process.exitResult)}`);
        }
        const activePortFile = await findNamedFile(this.profileDirectory, "DevToolsActivePort");
        if (!activePortFile) {
          return false;
        }
        const [portText] = (await fs.readFile(activePortFile, "utf8")).split(/\r?\n/);
        const port = Number(portText);
        if (!Number.isInteger(port) || port < 1 || port > 65_535) {
          return false;
        }
        this.cdpPort = port;
        this.cdpEndpoint = `http://127.0.0.1:${port}`;
        return true;
      },
      BROWSER_READY_TIMEOUT_MS
    );

    await waitForCondition(
      "the WebView2 CDP endpoint",
      async () => {
        if (!this.process.isRunning()) {
          throw createStopPollingError(`ReadBoard exited before CDP connected: ${JSON.stringify(this.process.exitResult)}`);
        }
        if (!this.browser) {
          try {
            this.browser = await chromium.connectOverCDP(this.cdpEndpoint);
          } catch {
            return false;
          }
        }
        return true;
      },
      BROWSER_READY_TIMEOUT_MS
    );

    await waitForCondition(
      "the production ReadBoard WebView2 page",
      async () => {
        if (!this.process.isRunning()) {
          throw createStopPollingError(`ReadBoard exited before its page became ready: ${JSON.stringify(this.process.exitResult)}`);
        }
        for (const context of this.browser.contexts()) {
          const page = context.pages().find(candidate =>
            candidate.url().startsWith("https://app.readboard/index.html")
          );
          if (page) {
            this.page = page;
            this.observePage(page);
            return true;
          }
        }
        return false;
      },
      BROWSER_READY_TIMEOUT_MS
    );
    this.runtimeVersion = await this.readRuntimeVersion();
  }

  observePage(page) {
    page.on("close", () => {
      this.pageClosed = true;
    });
    page.on("console", message => {
      this.pageConsole.push({
        type: message.type(),
        text: message.text(),
        location: message.location()
      });
    });
    page.on("pageerror", error => {
      this.pageErrors.push({ name: error.name, message: error.message, stack: error.stack });
    });
    page.on("requestfailed", request => {
      this.requestFailures.push({
        url: request.url(),
        method: request.method(),
        failure: request.failure()
      });
    });
  }
  async capturePreTeardownEvidence() {
    this.preTeardownRuntimeVersion = this.runtimeVersion || await this.readRuntimeVersion();
    this.preTeardownConfiguration = await this.readConfigurationFiles();
    if (!this.page) {
      return;
    }

    const snapshot = { errors: [] };
    try {
      snapshot.screenshot = await this.page.screenshot({ fullPage: true });
    } catch (error) {
      snapshot.errors.push({ file: "failure.png", error: error.message });
    }
    try {
      snapshot.semanticDom = await this.page.locator("body").ariaSnapshot();
    } catch (error) {
      snapshot.errors.push({ file: "semantic-dom.txt", error: error.message });
      try {
        snapshot.html = await this.page.content();
      } catch (contentError) {
        snapshot.errors.push({ file: "page.html", error: contentError.message });
      }
    }
    this.preTeardownEvidence = snapshot;
  }

  async captureFailureEvidence(testInfo, failure) {
    const evidenceDirectory = testInfo.outputPath("real-webview2-host-evidence");
    const evidenceErrors = [];
    await fs.mkdir(evidenceDirectory, { recursive: true });

    const writeText = async (name, content) => {
      try {
        await fs.writeFile(path.join(evidenceDirectory, name), content, "utf8");
      } catch (error) {
        evidenceErrors.push({ file: name, error: error.message });
      }
    };
    const writeBinary = async (name, content) => {
      try {
        await fs.writeFile(path.join(evidenceDirectory, name), content);
      } catch (error) {
        evidenceErrors.push({ file: name, error: error.message });
      }
    };
    const writeJson = async (name, value) =>
      writeText(name, `${JSON.stringify(value, null, 2)}\n`);

    await writeText("failure.txt", failure && failure.stack ? failure.stack : String(failure));
    await writeJson("wire-transcript.json", this.getWireTranscript());
    await writeJson("socket-errors.json", this.host ? this.host.socketErrors : []);
    await writeText(
      "process-output.txt",
      [
        `executable: ${this.process ? this.process.executable : ""}`,
        `args: ${this.process ? JSON.stringify(this.process.args) : ""}`,
        "stdout:",
        this.process ? this.process.stdout : "",
        "stderr:",
        this.process ? this.process.stderr : ""
      ].join("\n")
    );
    await writeJson("page-console.json", this.pageConsole);
    await writeJson("page-errors.json", this.pageErrors);
    await writeJson("request-failures.json", this.requestFailures);
    const runtimeVersion = this.preTeardownRuntimeVersion || await this.readRuntimeVersion();
    const configurationFiles = this.preTeardownConfiguration || await this.readConfigurationFiles();
    this.preTeardownRuntimeVersion = runtimeVersion;
    this.preTeardownConfiguration = configurationFiles;
    await writeJson("exit-result.json", this.process ? this.process.exitResult : null);
    await writeJson("cleanup-state.json", this.cleanupState);
    await writeJson("runtime-version.json", runtimeVersion);
    await writeJson("configuration-files.json", configurationFiles);

    const snapshot = this.preTeardownEvidence;
    if (snapshot) {
      if (snapshot.screenshot) {
        await writeBinary("failure.png", snapshot.screenshot);
      }
      if (snapshot.semanticDom) {
        await writeText("semantic-dom.txt", snapshot.semanticDom);
      } else if (snapshot.html) {
        await writeText("page.html", snapshot.html);
      }
      evidenceErrors.push(...snapshot.errors);
    } else if (this.page) {
      try {
        await writeBinary("failure.png", await this.page.screenshot({ fullPage: true }));
      } catch (error) {
        evidenceErrors.push({ file: "failure.png", error: error.message });
      }
      try {
        await writeText("semantic-dom.txt", await this.page.locator("body").ariaSnapshot());
      } catch (error) {
        evidenceErrors.push({ file: "semantic-dom.txt", error: error.message });
        try {
          await writeText("page.html", await this.page.content());
        } catch (contentError) {
          evidenceErrors.push({ file: "page.html", error: contentError.message });
        }
      }
    } else {
      await writeText("semantic-dom.txt", "No production page was attached.\n");
    }

    await writeJson("evidence-errors.json", evidenceErrors);
    return evidenceDirectory;
  }

  async captureFinalEvidence(testInfo) {
    const evidenceDirectory = testInfo.outputPath("real-webview2-host-evidence");
    const evidenceErrors = [];
    await fs.mkdir(evidenceDirectory, { recursive: true });

    const writeText = async (name, content) => {
      try {
        await fs.writeFile(path.join(evidenceDirectory, name), content, "utf8");
      } catch (error) {
        evidenceErrors.push({ file: name, error: error.message });
      }
    };
    const writeJson = async (name, value) =>
      writeText(name, `${JSON.stringify(value, null, 2)}\n`);

    await writeJson("post-wire-transcript.json", this.getWireTranscript());
    await writeJson("post-socket-errors.json", this.host ? this.host.socketErrors : []);
    await writeText("post-process-output.txt", [
      `executable: ${this.process ? this.process.executable : ""}`,
      `args: ${this.process ? JSON.stringify(this.process.args) : ""}`,
      "stdout:",
      this.process ? this.process.stdout : "",
      "stderr:",
      this.process ? this.process.stderr : ""
    ].join("\n"));
    await writeJson("post-process-output.json", {
      executable: this.process ? this.process.executable : null,
      args: this.process ? this.process.args : [],
      stdout: this.process ? this.process.stdout : "",
      stderr: this.process ? this.process.stderr : ""
    });
    await writeJson("post-exit-result.json", this.process ? this.process.exitResult : null);
    await writeJson("post-cleanup-state.json", this.cleanupState);
    await writeJson("post-runtime-version.json", await this.readRuntimeVersion());
    await writeJson("post-configuration-files.json", this.postTeardownConfiguration || this.preTeardownConfiguration || {});
    if (evidenceErrors.length > 0) {
      await writeJson("finalization-errors.json", evidenceErrors);
    }
  }

  async readRuntimeVersion() {
    const result = {
      node: process.version,
      platform: process.platform,
      arch: process.arch,
      cdpEndpoint: this.cdpEndpoint || null
    };
    if (this.cdpEndpoint) {
      const controller = new AbortController();
      try {
        const response = await withTimeout(
          () => fetch(`${this.cdpEndpoint}/json/version`, { signal: controller.signal }),
          CDP_OPERATION_TIMEOUT_MS,
          "the browser Runtime version"
        );
        if (!response.ok) {
          throw new Error(`CDP version request failed with HTTP ${response.status}.`);
        }
        const version = await withTimeout(
          () => response.json(),
          CDP_OPERATION_TIMEOUT_MS,
          "the browser Runtime version body"
        );
        result.browser = version.Browser || null;
        result.protocolVersion = version["Protocol-Version"] || null;
      } catch (error) {
        result.error = error.message;
      } finally {
        controller.abort();
      }
    }
    return result;
  }

  async readConfigurationFiles() {
    if (!this.appDirectory) {
      return {};
    }

    const files = [
      "config.readboard.json",
      "config_readboard.txt",
      "config_readboard_others.txt"
    ];
    const result = {};
    for (const file of files) {
      try {
        result[file] = await fs.readFile(path.join(this.appDirectory, file), "utf8");
      } catch (error) {
        if (error.code !== "ENOENT") {
          result[file] = { error: error.message };
        }
      }
    }
    return result;
  }
  async readConfigurationTransactionDirectories() {
    if (!this.appDirectory)
      return [];
    const entries = await fs.readdir(this.appDirectory, { withFileTypes: true });
    return entries
      .filter(entry => entry.isDirectory() && entry.name.startsWith(".readboard-config-transaction-"))
      .map(entry => entry.name)
      .sort();
  }

  async waitForCdpTargetClosure() {
    await waitForCondition(
      "the production WebView2 CDP page target to close",

      async () => {
        if (this.pageClosed || (this.page && this.page.isClosed())) {
          return true;
        }
        const controller = new AbortController();
        try {
          const response = await withTimeout(
            () => fetch(`${this.cdpEndpoint}/json/list`, { signal: controller.signal }),
            CDP_OPERATION_TIMEOUT_MS,
            "the CDP target list"
          );
          if (!response.ok) {
            return false;
          }
          const targets = await withTimeout(
            () => response.json(),
            CDP_OPERATION_TIMEOUT_MS,
            "the CDP target list body"
          );
          return !targets.some(target =>
            target.type === "page" && target.url && target.url.startsWith("https://app.readboard/index.html")
          );
        } catch (error) {
          if (error.message.startsWith("Timed out waiting")) {
            throw error;
          }
          throw createStopPollingError(`CDP target probe failed: ${error.message}`);
        } finally {
          controller.abort();
        }
      },
      CDP_CLOSE_TIMEOUT_MS
    );
  }

  async closeFromShell() {
    const startIndex = this.host.transcript.length;
    await this.page.locator('[data-command="window.close"]').click();
    await this.host.waitForOrderedLines(SHUTDOWN_LINES, startIndex);
    const exitResult = await this.process.waitForExit();
    if (exitResult.code !== 0) {
      throw new Error(`ReadBoard exited with ${JSON.stringify(exitResult)} after shell close.`);
    }
    await this.waitForCdpTargetClosure();
    if (this.browser.isConnected()) {
      await withTimeout(() => this.browser.close({ reason: "ReadBoard host fixture cleanup" }), CDP_OPERATION_TIMEOUT_MS, "the WebView2 CDP client close");
      await waitForCondition("the Playwright CDP connection to close", () => !this.browser.isConnected(), 2_000);
    }
    this.shutdownObserved = true;
    this.cleanupState.cdpTargetClosed = true;
    return { exitResult, startIndex };
  }
  async quitFromHost() {
    await this.capturePreTeardownEvidence();
    const startIndex = this.host.transcript.length;
    await this.host.sendLine("quit");
    await this.host.waitForOrderedLines(SHUTDOWN_LINES, startIndex);
    const exitResult = await this.process.waitForExit();
    if (exitResult.code !== 0)
      throw new Error(`ReadBoard exited with ${JSON.stringify(exitResult)} after host quit.`);
    await this.waitForCdpTargetClosure();
    if (this.browser && this.browser.isConnected()) {
      await withTimeout(
        () => this.browser.close({ reason: "ReadBoard host quit" }),
        CDP_OPERATION_TIMEOUT_MS,
        "the WebView2 CDP client close after host quit"
      );
      await waitForCondition("the Playwright CDP connection to close", () => !this.browser.isConnected(), 2_000);
    }
    this.shutdownObserved = true;
    this.cleanupState.cdpTargetClosed = true;
    this.postTeardownConfiguration = await this.readConfigurationFiles();
    return { exitResult, startIndex };
  }


  async dispose() {
    const cleanupErrors = [];
    try {
      if (this.process && this.process.isRunning()) {
        try {
          const startIndex = this.host.transcript.length;
          await this.host.sendLine("quit");
          await this.host.waitForOrderedLines(SHUTDOWN_LINES, startIndex);
          const exitResult = await this.process.waitForExit();
          if (exitResult.code !== 0) {
            cleanupErrors.push(new Error(`ReadBoard exited with ${JSON.stringify(exitResult)} after quit.`));
          } else {
            this.shutdownObserved = true;
          }
        } catch (error) {
          cleanupErrors.push(error);
          if (this.process.isRunning()) {
            try {
              await this.process.waitForExit(PROCESS_EXIT_TIMEOUT_MS);
            } catch (timeoutError) {
              this.cleanupState.escalated = true;
              this.cleanupState.errors.push(timeoutError.message);
              cleanupErrors.push(timeoutError);
              try {
                await terminateProcessTree(this.process);
                this.cleanupState.processTreeTerminated = true;
              } catch (terminationError) {
                cleanupErrors.push(terminationError);
              }
            }
          }
        }
      } else if (this.process && !this.shutdownObserved && !this.cleanupState.escalated) {
        cleanupErrors.push(
          new Error(`ReadBoard exited before the fixture observed normal shutdown: ${JSON.stringify(this.process.exitResult)}.`)
        );
      }

      if (this.browser && !this.cleanupState.cdpTargetClosed) {
        try {
          await this.waitForCdpTargetClosure();
          this.cleanupState.cdpTargetClosed = true;
        } catch (error) {
          this.cleanupState.errors.push(error.message);
          cleanupErrors.push(error);
        } finally {
          if (this.browser.isConnected()) {
            try {
              await withTimeout(() => this.browser.close({ reason: "ReadBoard host fixture cleanup" }), CDP_OPERATION_TIMEOUT_MS, "the WebView2 CDP client close");
              await waitForCondition("the Playwright CDP connection to close", () => !this.browser.isConnected(), 2_000);
            } catch (closeError) {
              cleanupErrors.push(closeError);
            }
          }
        }
      }
      try {
        this.postTeardownConfiguration = await this.readConfigurationFiles();
      } catch (error) {
        cleanupErrors.push(error);
      }

    } finally {
      if (this.host) {
        try {
          await this.host.close();
        } catch (error) {
          cleanupErrors.push(error);
        }
      }
      if (this.testDirectory) {
        try {
          await removeDirectory(this.testDirectory);
          this.cleanupState.removedTestDirectory = true;
        } catch (error) {
          cleanupErrors.push(error);
        }
      }
    }

    if (this.cleanupState.escalated) {
      cleanupErrors.unshift(new Error("ReadBoard teardown required process-tree termination."));
    }
    if (cleanupErrors.length > 0) {
      throw new AggregateError(cleanupErrors, "Real WebView2 host cleanup failed.");
    }
  }
}

async function withRealWebView2Host(publishDirectory, testInfo, body, startOptions = {}) {
  const fixture = new RealWebView2HostFixture(publishDirectory);
  let failure;

  try {
    await fixture.start(startOptions);
    await body(fixture);
  } catch (error) {
    failure = error;
  }

  if (failure) {
    try {
      await fixture.captureFailureEvidence(testInfo, failure);
    } catch (evidenceError) {
      fixture.cleanupState.errors.push(`Failure evidence capture failed: ${evidenceError.message}`);
    }
  } else {
    try {
      await fixture.capturePreTeardownEvidence();
    } catch (evidenceError) {
      fixture.cleanupState.errors.push(`Pre-teardown evidence capture failed: ${evidenceError.message}`);
    }
  }

  try {
    await fixture.dispose();
  } catch (cleanupError) {
    if (!failure) {
      failure = cleanupError;
      try {
        await fixture.captureFailureEvidence(testInfo, cleanupError);
      } catch (evidenceError) {
        fixture.cleanupState.errors.push(`Cleanup failure evidence capture failed: ${evidenceError.message}`);
      }
    } else {
      failure = new AggregateError([failure, cleanupError], "Real WebView2 host test and cleanup failed.");
    }
  }

  if (failure) {
    try {
      await fixture.captureFinalEvidence(testInfo);
    } catch (evidenceError) {
      fixture.cleanupState.errors.push(`Final evidence capture failed: ${evidenceError.message}`);
    }
    throw failure;
  }
}

module.exports = {
  publishRelease,
  removeDirectory,
  withRealWebView2Host
};
