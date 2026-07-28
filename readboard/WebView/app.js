(() => {
  "use strict";

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const webview = window.chrome?.webview;
  const preview = !webview;
  const systemThemeQuery = window.matchMedia("(prefers-color-scheme: dark)");
  const baseViewport = Object.freeze({ width: 1100, height: 680 });
  const minimumViewport = Object.freeze({ width: 960, height: 600 });
  const minimumScale = minimumViewport.width / baseViewport.width;
  const initialState = {
    page: "controlCenter",
    language: "cn",
    text: {},
    shell: {
      version: "v3.1.0",
      theme: "system",
      connected: false,
      syncStatus: "",
      lastSync: "--:--:--",
      stoneCount: 0,
      duration: "--",
      targetWindowValid: null,
      boardRegionRecognized: false,
      placementRegionResolved: false,
      maximized: false
    },
    controlCenter: {
      platform: "fox",
      room: "--",
      moves: "--",
      nextTurn: "--",
      titleBound: false,
      boardSize: "19",
      boardWidth: 19,
      boardHeight: 19,
      twoWaySync: true,
      autoPlay: false,
      color: "auto",
      placement: "direct",
      aiTime: 2,
      playouts: "",
      firstPolicy: "",
      firstPolicyEnabled: false,
      showOnBoard: false,
      quickSyncActive: false,
      continuousSyncActive: false,
      quickSyncEnabled: true,
      continuousSyncEnabled: true,
      syncInterval: 200,
      analysisRunning: false,
      analysisStateAvailable: false,
      analysisToggleEnabled: true,
      configurationEnabled: true,
      twoWaySyncEnabled: true,
      autoPlayToggleEnabled: true,
      autoPlayControlsEnabled: false,
      customBoardDimensionsEnabled: false,
      identityEnabled: true,
      showOnBoardEnabled: true
    },
    settings: {
      autoMinimize: true,
      backgroundAnalysis: true,
      magnifier: false,
      enhancedCapture: false,
      placementValidation: true,
      syncInterval: 200,
      grayOffset: 0,
      blackOffset: 0,
      blackPercent: 85,
      whiteOffset: 0,
      whitePercent: 85,
      theme: "system",
      diagnostics: false,
      dirty: false,
      errors: {}
    },
    logs: [],
    update: null,
    identity: null,
    dialog: null
  };

  let state = structuredClone(initialState);
  if (preview) state.logs = [{ time: "--:--:--", level: "INFO", message: t("WebView_previewWaiting", "本地预览模式，等待宿主状态") }];
  let activeModal = null;
  let modalOpener = null;
  let resizeFrame = 0;
  let themePreference = "system";
  let localizedText = null;
  let localizedLanguage = "";

  function updateViewportLayout() {
    resizeFrame = 0;
    const viewportWidth = Math.max(1, window.innerWidth);
    const viewportHeight = Math.max(1, window.innerHeight);
    const emergency = viewportWidth < minimumViewport.width || viewportHeight < minimumViewport.height;
    const limitingScale = Math.min(
      viewportWidth / baseViewport.width,
      viewportHeight / baseViewport.height);
    const scale = emergency
      ? minimumScale
      : Math.min(1, Math.max(minimumScale, limitingScale));
    const layoutWidth = Math.max(baseViewport.width, viewportWidth / scale);
    const layoutHeight = Math.max(baseViewport.height, viewportHeight / scale);
    const root = document.documentElement;
    root.style.setProperty("--ui-scale", String(scale));
    root.style.setProperty("--layout-width", `${layoutWidth}px`);
    root.style.setProperty("--layout-height", `${layoutHeight}px`);
    root.classList.toggle("emergency", emergency);
    root.dataset.uiScale = scale.toFixed(6);
  }

  function scheduleViewportLayout() {
    if (resizeFrame) return;
    resizeFrame = window.requestAnimationFrame(updateViewportLayout);
  }

  function merge(base, patch) {
    if (!patch || typeof patch !== "object" || Array.isArray(patch)) return patch;
    const result = { ...(base && typeof base === "object" ? base : {}) };
    for (const [key, value] of Object.entries(patch)) {
      result[key] = value && typeof value === "object" && !Array.isArray(value)
        ? merge(result[key], value)
        : value;
    }
    return result;
  }

  function normalizeTheme(value) {
    return value === "dark" || value === "light" ? value : "system";
  }

  function applyTheme(value) {
    themePreference = normalizeTheme(value);
    const resolvedTheme = themePreference === "system"
      ? (systemThemeQuery.matches ? "dark" : "light")
      : themePreference;
    document.documentElement.dataset.theme = resolvedTheme;
    document.documentElement.style.colorScheme = resolvedTheme;
  }

  systemThemeQuery.addEventListener("change", () => {
    if (themePreference === "system") applyTheme(themePreference);
  });

  function send(type, payload = {}) {
    const message = { type, payload };
    if (webview) webview.postMessage(message);
    else console.info("ReadBoard preview command", message);
  }

  function text(id, value) {
    const element = document.getElementById(id);
    if (element) element.textContent = value ?? "";
  }

  function t(key, previewValue = key) {
    const value = state.text?.[key];
    return typeof value === "string" && value ? value : previewValue;
  }

  function htmlLanguage(value) {
    return ({ cn: "zh-CN", en: "en", jp: "ja", kr: "ko" })[value] || "und";
  }

  function localizeStaticPage() {
    if (!preview && !Object.keys(state.text || {}).length) return;
    if (localizedText === state.text && localizedLanguage === state.language) return;
    document.documentElement.lang = htmlLanguage(state.language);
    $$('[data-i18n]').forEach(element => {
      element.textContent = t(element.dataset.i18n, element.textContent);
    });
    $$('[data-i18n-aria-label]').forEach(element => {
      element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel, element.getAttribute("aria-label")));
    });
    document.title = `ReadBoard / ${t("MainForm_title", "棋盘同步工具")}`;
    localizedText = state.text;
    localizedLanguage = state.language;
  }

  function dynamicText(id, value) {
    const element = document.getElementById(id);
    if (!element) return;
    const content = value ?? "";
    element.textContent = content;
    element.title = String(content);
  }

  function setChecked(selector, value) {
    const element = $(selector);
    if (element) element.checked = Boolean(value);
  }

  function setValue(selector, value) {
    const element = $(selector);
    if (element && document.activeElement !== element) element.value = value ?? "";
  }

  function setDisabled(selector, value) {
    $$(selector).forEach(element => { element.disabled = Boolean(value); });
  }

  function showPage(page, notify = false) {
    const target = ["controlCenter", "settings", "rules", "about"].includes(page) ? page : "controlCenter";
    $$("[data-page-panel]").forEach(element => element.classList.toggle("active", element.dataset.pagePanel === target));
    $$(".nav-item").forEach(element => {
      const active = element.dataset.page === target;
      element.classList.toggle("active", active);
      if (active) element.setAttribute("aria-current", "page");
      else element.removeAttribute("aria-current");
    });
    state.page = target;
    if (notify) send("navigate", { page: target });
  }

  function renderShell() {
    const shell = state.shell || {};
    const version = shell.version || "v3.1.0";
    ["title-version", "version", "about-version", "project-version"].forEach(id => text(id, version));
    const syncStatus = shell.syncStatus === "同步中" ? t("WebView_syncing", "同步中")
      : shell.syncStatus === "就绪" ? t("WebView_ready", "就绪")
        : shell.syncStatus === "宿主模式已启动" ? t("WebView_hostModeStarted", "宿主模式已启动") : shell.syncStatus;
    text("sync-status", syncStatus || (shell.connected ? t("WebView_ready", "就绪") : t("WebView_hostModeStarted", "宿主模式已启动")));
    text("last-sync", shell.lastSync || "--:--:--");
    text("stone-count", shell.stoneCount ?? 0);
    text("duration", shell.duration || "--");
    text("host-state", shell.connected ? t("WebView_hostConnected", "宿主通信正常") : t("WebView_hostModeStarted", "宿主模式已启动"));
    const maximizeButton = $('[data-command="window.maximize"]');
    if (maximizeButton) {
      maximizeButton.setAttribute("aria-label", shell.maximized ? t("WebView_restore", "还原") : t("WebView_maximize", "最大化"));
      const icon = $(".icon", maximizeButton);
      if (icon) icon.innerHTML = shell.maximized ? "&#xE923;" : "&#xE922;";
    }
    $("#sync-status")?.classList.toggle("good", Boolean(shell.connected));
    $("#sync-dot").className = `dot${shell.connected ? " good" : ""}`;
    $("#host-dot").className = `dot${shell.connected ? " good" : ""}`;
    setStatus("target", shell.targetWindowValid === true
      ? t("WebView_targetValid", "目标窗口有效")
      : shell.targetWindowValid === false ? t("WebView_targetInvalid", "目标窗口已失效，请重新选择") : t("WebView_waitTarget", "等待选择目标窗口"), shell.targetWindowValid);
    setStatus("board", shell.boardRegionRecognized ? t("WebView_boardRecognized", "棋盘区域已识别") : t("WebView_waitBoardRecognition", "等待首次棋盘识别"), shell.boardRegionRecognized);
    setStatus("placement", shell.placementRegionResolved ? t("WebView_placementResolved", "落子区域已解析") : t("WebView_placementUnavailable", "落子区域暂不可用"), shell.placementRegionResolved);
  }

  function setStatus(prefix, label, value) {
    text(`${prefix}-status`, label);
    const dot = document.getElementById(`${prefix}-dot`);
    if (dot) dot.className = `dot${value === true ? " good" : value === false && prefix === "target" ? " bad" : ""}`;
  }

  function renderControlCenter() {
    const control = state.controlCenter || {};
    text("context-platform", control.platformLabel || platformLabel(control.platform));
    dynamicText("context-room", control.room || "--");
    text("context-moves", control.moves ?? "--");
    text("context-turn", control.nextTurn === "黑" ? t("WebView_black", "黑") : control.nextTurn === "白" ? t("WebView_white", "白") : control.nextTurn || "--");
    text("context-binding", control.titleBound ? t("WebView_bound", "已绑定") : t("WebView_notBound", "未绑定"));
    const bindingDot = $("#binding-dot");
    if (bindingDot) bindingDot.className = `dot${control.titleBound ? " good" : ""}`;
    setChecked(`input[name="platform"][value="${cssValue(control.platform || "fox")}"]`, true);
    setChecked(`input[name="boardSize"][value="${cssValue(String(control.boardSize || "19"))}"]`, true);
    setValue("#board-width", control.boardWidth ?? 19);
    setValue("#board-height", control.boardHeight ?? 19);
    setChecked("#two-way", control.twoWaySync);
    setChecked("#auto-play", control.autoPlay);
    setChecked(`input[name="color"][value="${cssValue(control.color || "auto")}"]`, true);
    setChecked(`input[name="placement"][value="${cssValue(control.placement || "direct")}"]`, true);
    setValue("#ai-time", control.aiTime ?? 2);
    setValue("#playouts", control.playouts ?? "");
    setValue("#first-policy", control.firstPolicy ?? "");
    setChecked("#show-on-board", control.showOnBoard);
    setDisabled('input[name="platform"], input[name="boardSize"]', !control.configurationEnabled);
    setDisabled("#board-width, #board-height", !control.customBoardDimensionsEnabled);
    setDisabled("#two-way", !control.twoWaySyncEnabled);
    setDisabled("#auto-play", !control.autoPlayToggleEnabled);
    setDisabled('input[name="color"], input[name="placement"], #ai-time, #playouts', !control.autoPlayControlsEnabled);
    setDisabled("#first-policy", !control.firstPolicyEnabled);
    setDisabled('[data-command="identity.open"]', !control.identityEnabled);
    setDisabled("#show-on-board", !control.showOnBoardEnabled);
    text("quick-label", control.quickSyncActive ? t("WebView_stopQuickSync", "停止快速同步") : t("WebView_quickSync", "快速同步"));
    text("continuous-label", `${control.continuousSyncActive ? t("WebView_stopContinuousSync", "停止持续同步") : t("WebView_continuousSync", "持续同步")} (${control.syncInterval ?? 200}ms)`);
    text("analysis-label", control.analysisRunning ? t("WebView_pauseAnalysis", "暂停分析") : t("WebView_resumeAnalysis", "继续分析"));
    setDisabled('[data-command="sync.quick"]', !control.quickSyncEnabled);
    setDisabled('[data-command="sync.continuous"]', !control.continuousSyncEnabled);
    setDisabled('[data-command="sync.toggleAnalysis"]', !control.analysisToggleEnabled || (!control.analysisRunning && !control.analysisStateAvailable));
    const quick = $("[data-command='sync.quick']");
    if (quick) {
      quick.classList.toggle("running", Boolean(control.quickSyncActive));
      quick.setAttribute("aria-pressed", String(Boolean(control.quickSyncActive)));
    }
    const continuous = $("[data-command='sync.continuous']");
    if (continuous) {
      continuous.classList.toggle("running", Boolean(control.continuousSyncActive));
      continuous.setAttribute("aria-pressed", String(Boolean(control.continuousSyncActive)));
    }
    $("[data-command='sync.toggleAnalysis']")?.setAttribute("aria-pressed", String(Boolean(control.analysisRunning)));
  }

  function platformLabel(value) {
    return ({ fox: t("MainForm_rdoFox", "野狐"), foxBackground: t("MainForm_rdoFoxBack", "野狐(后台落子)"), yike: t("MainForm_rdoYike", "弈客"), yicheng: t("MainForm_rdoTygem", "弈城"), sina: t("MainForm_rdoSina", "新浪"), otherBackground: t("MainForm_rdoBack", "其他(后台)"), otherForeground: t("MainForm_rdoFore", "其他(前台)") })[value] || t("WebView_notSelected", "未选择");
  }

  function cssValue(value) {
    return String(value).replace(/["\\]/g, "\\$&");
  }

  function renderSettings() {
    const settings = state.settings || {};
    $$('[data-setting]').forEach(input => {
      const value = settings[input.dataset.setting];
      if (input.type === "checkbox") input.checked = Boolean(value);
      else if (document.activeElement !== input) input.value = value ?? "";
      const error = input.closest("label")?.querySelector(".field-error");
      if (error) {
        const message = settings.errors?.[input.dataset.setting] || "";
        error.textContent = localizedSettingsError(message);
        if (!error.id) error.id = `${input.dataset.setting}-error`;
        input.toggleAttribute("aria-invalid", Boolean(message));
        if (message) input.setAttribute("aria-describedby", error.id);
        else input.removeAttribute("aria-describedby");
      }
    });
    setChecked(`input[name="theme"][value="${cssValue(settings.theme || "system")}"]`, true);
    text("settings-dirty", settings.dirty ? t("WebView_unsavedChanges", "有尚未保存的更改") : t("WebView_noUnsavedChanges", "当前没有未保存的更改"));
  }

  function localizedSettingsError(value) {
    if (value === "请输入整数") return t("SettingsForm_mustBeInteger", "请输入整数");
    const minimum = /^\u8bf7\u8f93\u5165\u4e0d\u5c0f\u4e8e (\d+) \u7684\u6574\u6570$/.exec(value);
    if (minimum) return t("WebView_integerAtLeast", "请输入不小于 {0} 的整数").replace("{0}", minimum[1]);
    const range = /^\u8bf7\u8f93\u5165 (\d+)–(\d+) \u4e4b\u95f4\u7684\u6574\u6570$/.exec(value);
    if (range) return t("WebView_integerRange", "请输入 {0}–{1} 之间的整数").replace("{0}", range[1]).replace("{1}", range[2]);
    return value || "";
  }

  function renderLogs() {
    const list = $("#log-list");
    if (!list) return;
    list.replaceChildren(...(state.logs || []).slice(-100).map(log => {
      const row = document.createElement("div");
      const level = ["INFO", "SYNC", "WARN"].includes(log.level) ? log.level : "INFO";
      row.className = "log-row";
      const time = document.createElement("span");
      time.textContent = log.time || "--:--:--";
      const tag = document.createElement("span");
      tag.className = `log-tag ${level.toLowerCase()}`;
      tag.textContent = level;
      const message = document.createElement("span");
      message.textContent = localizedLogMessage(log.message);
      row.append(time, tag, message);
      return row;
    }));
    list.scrollTop = list.scrollHeight;
  }

  function localizedLogMessage(value) {
    return ({
      "宿主通信正常": t("WebView_hostConnected", "宿主通信正常"),
      "宿主模式已启动，ReadBoard 就绪": t("WebView_hostReadyLog", "宿主模式已启动，ReadBoard 就绪"),
      "开始持续同步": t("WebView_continuousSyncStarted", "开始持续同步"),
      "持续同步已停止": t("WebView_continuousSyncStopped", "持续同步已停止"),
      "开始快速同步": t("WebView_quickSyncStarted", "开始快速同步"),
      "快速同步已停止": t("WebView_quickSyncStopped", "快速同步已停止"),
      "已识别并发送棋盘状态": t("WebView_boardSent", "已识别并发送棋盘状态")
    })[value] || value || "";
  }

  function renderModal() {
    if (state.update?.open) return renderUpdate(state.update);
    if (state.identity?.open) return renderIdentity(state.identity);
    if (state.dialog?.open) return renderDialog(state.dialog);
    closeModal();
  }

  function openModal(kind, title, body, actions, size) {
    const layer = $("#modal-layer");
    const wasHidden = layer.hidden;
    if (wasHidden) modalOpener = document.activeElement;
    activeModal = kind;
    text("modal-title", title);
    $("#modal-body").innerHTML = body;
    $("#modal-actions").innerHTML = actions;
    $("#modal").className = `modal ${kind}`;
    $("#modal").style.width = size || "min(820px, calc(100vw - 48px))";
    $(".app-shell").inert = true;
    layer.hidden = false;
    if (wasHidden || !$("#modal").contains(document.activeElement)) {
      $("#modal [data-command], #modal button")?.focus();
    }
  }

  function closeModal() {
    activeModal = null;
    $("#modal-layer").hidden = true;
    $(".app-shell").inert = false;
    modalOpener?.focus();
    modalOpener = null;
  }

  function renderUpdate(update) {
    const mode = update.status || "checking";
    let body;
    let actions = button("update.close", t("Update_close", "关闭"));
    if (mode === "checking") {
      body = message("&#xE895;", t("WebView_updateChecking", "正在检查可用更新"), t("WebView_updateConnecting", "正在连接 GitHub Release，请稍候。"), '<div class="progress indeterminate"><i></i></div>');
    } else if (mode === "latest") {
      body = message("&#xE73E;", update.title || t("WebView_updateLatest", "当前已是最新版本"), escapeHtml(update.detail || `ReadBoard ${update.currentVersion || state.shell.version || ""}`), `<p>${escapeHtml(update.completedAt || update.message || t("WebView_updateJustChecked", "刚刚完成检查"))}</p>`);
      actions = button("update.close", t("WebView_done", "完成"), "primary");
    } else if (mode === "available" || mode === "manual") {
      body = `<div class="update-details"><span>${escapeHtml(t("Update_currentVersion", "当前版本"))}</span><b>${escapeHtml(update.currentVersion || "--")}</b><span>${escapeHtml(t("Update_latestVersion", "最新版本"))}</span><b>${escapeHtml(update.latestVersion || "--")}</b><span>${escapeHtml(t("Update_releaseDate", "发布日期"))}</span><b>${escapeHtml(update.releaseDate || "--")}</b>${mode === "manual" ? `<div class="update-warning"><b>${escapeHtml(t("WebView_hostedInstallUnsupported", "当前宿主不支持托管安装"))}</b><p>${escapeHtml(update.detail || t("WebView_manualDownload", "可打开 Release 页面手动下载更新。"))}</p></div>` : ""}<span>${escapeHtml(t("Update_releaseNotes", "更新说明"))}</span><div class="release-notes">${escapeHtml(update.releaseNotes || t("Update_releaseNotesUnavailable", "暂无更新说明"))}</div></div>`;
      actions += button(mode === "manual" ? "update.openDownload" : "update.install", mode === "manual" ? t("Update_download", "去下载") : t("Update_downloadAndInstall", "下载并安装"), "primary");
    } else if (mode === "notice") {
      body = message("&#xE946;", update.title || t("WebView_updateChannelNotice", "更新通道提示"), escapeHtml(update.detail || t("WebView_noUpdateAvailable", "当前没有可安装的更新。")));
      actions = button("update.close", t("WebView_done", "完成"), "primary");
    } else if (mode === "check-failed") {
      body = message("&#xE783;", update.title || t("Update_checkFailed", "检查更新失败"), escapeHtml(update.detail || t("WebView_tryAgainLater", "请稍后重试。")));
      actions = button("update.close", t("Update_close", "关闭"), "primary");
    } else if (mode === "processing") {
      body = `<h3>${escapeHtml(update.title || t("WebView_preparingUpdate", "正在准备更新包"))}</h3><p>${escapeHtml(update.detail || t("WebView_pleaseWait", "请稍候…"))}</p>${progress(update.progress)}<div class="steps">${steps(update.steps)}</div>`;
      actions = `<button type="button" disabled>${escapeHtml(t("WebView_processing", "处理中…"))}</button>`;
    } else {
      body = `<div class="dialog-copy"><h3>${escapeHtml(update.title || t("WebView_installIncomplete", "安装未完成"))}</h3><p>${escapeHtml(update.message || t("WebView_updateIncomplete", "更新未完成，已切换为手动下载。"))}</p><div class="update-warning"><b>${escapeHtml(update.errorTitle || t("WebView_operationFailed", "操作失败"))}</b><p>${escapeHtml(update.error || t("WebView_retryOrDownload", "可稍后重试或手动下载。"))}</p></div></div>`;
      actions += button("update.openDownload", t("Update_download", "去下载"), "primary");
    }
    openModal("update", t("MainForm_btnCheckUpdate", "检查更新"), body, actions, "min(660px, calc(100vw - 48px))");
  }

  function renderIdentity(identity) {
    const candidates = Array.isArray(identity.candidates) ? identity.candidates : [];
    const selected = identity.selectedId;
    const body = candidates.length
      ? `<p class="identity-intro">${escapeHtml(t("FoxAutoPlayIdentityDialog_lblPrompt", "请选择你在野狐当前房间里的玩家行。"))}</p><b>${escapeHtml(t("FoxAutoPlayIdentityDialog_lblDetectedNicknames", "可选玩家行"))}</b><div class="candidate-list">${candidates.map(candidate => candidateHtml(candidate, selected, identity.savedId)).join("")}</div>${selected ? `<p class="identity-intro">${escapeHtml(t("WebView_selectedIdentity", "已选择"))} ${escapeHtml(candidates.find(item => item.id === selected)?.label || "")}</p>` : ""}`
      : `<p class="identity-intro">${escapeHtml(t("FoxAutoPlayIdentityDialog_lblPrompt", "请选择你在野狐当前房间里的玩家行。"))}</p><b>${escapeHtml(t("FoxAutoPlayIdentityDialog_lblDetectedNicknames", "可选玩家行"))}</b><div class="empty-state"><div><i class="icon">&#xE738;</i><h3>${escapeHtml(t("FoxAutoPlayIdentityDialog_noDetectedNicknames", "暂未识别到可选玩家行"))}</h3><p>${escapeHtml(t("WebView_identityWindowHint", "请确认野狐棋局窗口可见，然后重新打开身份选择。"))}</p></div></div>`;
    const actions = identity.hasSavedIdentity ? button("identity.clearSaved", t("FoxAutoPlayIdentityDialog_btnClearSavedIdentity", "清除保存"), "danger-outline left") : "";
    openModal("identity", t("WebView_selectIdentity", "选择野狐身份"), body, actions + button("identity.close", t("FoxAutoPlayIdentityDialog_btnCancel", "取消")) + button("identity.useOnce", t("FoxAutoPlayIdentityDialog_btnUseOnce", "本次使用"), "", !selected) + button("identity.saveAndUse", t("FoxAutoPlayIdentityDialog_btnSaveAndUse", "保存并使用"), "primary", !selected), "min(780px, calc(100vw - 48px))");
  }

  function candidateHtml(candidate, selectedId, savedId) {
    const selected = candidate.id === selectedId;
    const previewUrl = safeImageUrl(candidate.previewUrl);
    return `<label class="candidate${selected ? " selected" : ""}"><input type="radio" name="candidate" value="${escapeAttr(candidate.id)}"${selected ? " checked" : ""}><b>${escapeHtml(candidate.label || t("WebView_unnamedCandidate", "未命名候选"))}</b>${candidate.id === savedId ? `<span class="saved-pill">${escapeHtml(t("WebView_saved", "已保存"))}</span>` : ""}${previewUrl ? `<img src="${escapeAttr(previewUrl)}" alt="${escapeAttr((candidate.label || t("WebView_candidateRow", "候选玩家行")) + t("WebView_screenshot", "截图"))}">` : ""}</label>`;
  }

  function safeImageUrl(value) {
    if (typeof value !== "string") return "";
    return /^(data:image\/(?:png|jpeg|webp);base64,|https:\/\/app\.readboard\/)/i.test(value) ? value : "";
  }

  function renderDialog(dialog) {
    const content = {
      resetDefaults: [t("SettingsForm_btnReset", "恢复默认设置"), t("WebView_resetDefaultsDescription", "将当前设置草稿恢复为默认值。此操作不会立即写入配置，仍需点击保存设置。"), t("WebView_resetDefaults", "恢复默认")],
      diagnostics: [t("WebView_enableDiagnostics", "开启调试诊断"), t("WebView_diagnosticsDescription", "调试诊断可能产生较大的文件。确认后仅修改当前设置草稿，保存设置后生效。"), t("WebView_continueEnable", "继续开启")],
      showInBoardHint: [t("MainForm_chkShowInBoard", "原棋盘上显示选点"), t("WebView_showInBoardHintForeground", "[前台]方式同步时不支持此功能。选点显示在原棋盘上后，原棋盘将无法落子。"), t("TipsForm_btnConfirm", "确定")]
    }[dialog.kind] || [dialog.title || t("TipsForm_title", "提示"), dialog.message || "", t("TipsForm_btnConfirm", "确定")];
    let actions = button("dialog.cancel", dialog.cancelLabel || t("SettingsForm_btnCancel", "取消"));
    if (dialog.kind === "showInBoardHint") {
      actions = button("dialog.dontShowAgain", t("TipsForm_btnNotAskAgain", "不再提示")) + button("dialog.confirm", dialog.confirmLabel || content[2], "primary");
      openModal("dialog show-in-board-hint", t("TipsForm_title", "提示"), `<div class="dialog-copy"><p>${escapeHtml(dialog.message || content[1])}</p><p>${escapeHtml(t("WebView_showInBoardHintRestore", "可通过勾选“双向同步”选项恢复落子功能。"))}</p></div>`, actions, "min(520px, calc(100vw - 48px))");
      return;
    }
    actions += button("dialog.confirm", dialog.confirmLabel || content[2], "primary");
    openModal("dialog", content[0], `<div class="dialog-copy"><h3>${escapeHtml(dialog.heading || content[0])}</h3><p>${escapeHtml(dialog.message || content[1])}</p></div>`, actions, "min(580px, calc(100vw - 48px))");
  }

  function message(icon, title, detail, extra = "") {
    return `<div class="modal-message"><div><i class="icon">${icon}</i><h3>${escapeHtml(title)}</h3><p>${escapeHtml(detail)}</p>${extra}</div></div>`;
  }

  function steps(items) {
    if (!Array.isArray(items)) return "";
    return items.map(item => `<span class="${item.status === "done" ? "done" : item.status === "active" ? "active" : ""}"><i class="icon">${item.status === "done" ? "&#xE73E;" : item.status === "active" ? "&#xE895;" : "&#xE73C;"}</i>${escapeHtml(item.label || "")}</span>`).join("");
  }

  function button(command, label, className = "", disabled = false) {
    return `<button type="button" data-command="${escapeAttr(command)}" class="${escapeAttr(className)}"${disabled ? " disabled" : ""}>${escapeHtml(label)}</button>`;
  }

  function progress(value) {
    if (value === null || value === undefined || value === "") {
      return '<div class="progress indeterminate"><i></i></div>';
    }
    const number = Number(value);
    if (!Number.isFinite(number)) return '<div class="progress indeterminate"><i></i></div>';
    const percent = Math.min(100, Math.max(0, number));
    return `<div class="progress"><i style="width:${percent}%"></i></div>`;
  }

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character]);
  }

  function escapeAttr(value) { return escapeHtml(value); }

  function render() {
    localizeStaticPage();
    applyTheme(state.shell.theme || "system");
    showPage(state.page);
    renderShell();
    renderControlCenter();
    renderSettings();
    renderLogs();
    renderModal();
  }

  function commandPayload(element) {
    if (!element.dataset.payload) return {};
    try { return JSON.parse(element.dataset.payload); }
    catch { return {}; }
  }

  function modalCloseCommand() {
    return activeModal === "update" ? "update.close" : activeModal === "identity" ? "identity.close" : "dialog.cancel";
  }

  document.addEventListener("click", event => {
    const pageButton = event.target.closest("[data-page]");
    if (pageButton) {
      showPage(pageButton.dataset.page, true);
      return;
    }
    const buttonElement = event.target.closest("[data-command]");
    if (!buttonElement || buttonElement.disabled) return;
    let type = buttonElement.dataset.command;
    if (type === "modal.close") type = modalCloseCommand();
    let payload = commandPayload(buttonElement);
    if (["identity.useOnce", "identity.saveAndUse"].includes(type) && state.identity?.selectedId) {
      payload = { candidateId: state.identity.selectedId };
    }
    send(type, payload);
  });

  document.addEventListener("change", event => {
    const input = event.target;
    if (!(input instanceof HTMLInputElement)) return;
    if (input.dataset.setting) {
      send("settings.update", {
        key: input.dataset.setting,
        value: input.type === "checkbox" ? input.checked : input.value
      });
      return;
    }
    if (input.name === "theme") {
      send("settings.update", { key: "theme", value: input.value });
      return;
    }
    if (input.name === "candidate") {
      send("identity.select", { candidateId: input.value });
      return;
    }
    const key = input.id || input.name;
    if (key) send("control.update", { key, value: input.type === "checkbox" ? input.checked : input.value });
  });

  document.addEventListener("keydown", event => {
    if (!activeModal) return;
    if (event.key === "Escape") {
      send(modalCloseCommand());
      return;
    }
    if (event.key !== "Tab") return;
    const focusable = $$("button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex='-1'])", $("#modal"));
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  });

  function acceptMessage(message) {
    if (!message || message.type !== "state" || !message.payload || typeof message.payload !== "object") return;
    const currentText = state.text;
    state = merge(initialState, message.payload);
    if (!message.payload.text) state.text = currentText;
    render();
  }

  webview?.addEventListener("message", event => acceptMessage(event.data));
  window.addEventListener("resize", scheduleViewportLayout, { passive: true });
  window.readboardPreview = Object.freeze({
    setState(payload) { if (preview) acceptMessage({ type: "state", payload }); },
    getState() { return structuredClone(state); },
    getLayoutMetrics() {
      const shell = $(".app-shell");
      return {
        scale: Number(document.documentElement.dataset.uiScale || "1"),
        emergency: document.documentElement.classList.contains("emergency"),
        viewport: { width: window.innerWidth, height: window.innerHeight },
        layout: shell ? { width: shell.offsetWidth, height: shell.offsetHeight } : null
      };
    }
  });

  updateViewportLayout();
  render(true);
  if (webview) send("navigate", { page: state.page });
})();
