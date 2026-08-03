(() => {
  "use strict";

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const webview = window.chrome?.webview;
  const preview = !webview;
  if (!preview) document.body.classList.add("awaiting-state");
  const systemThemeQuery = window.matchMedia("(prefers-color-scheme: dark)");
  const baseViewport = Object.freeze({ width: 1100, height: 680 });
  const minimumViewport = Object.freeze({ width: 960, height: 600 });
  const minimumScale = minimumViewport.width / baseViewport.width;
  const previewState = {
    page: "controlCenter",
    language: "cn",
    text: {},
    shell: {
      version: "v3.1.0",
      theme: "system",
      connected: false,
      syncStatus: "",
      hostStatus: "",
      targetStatus: "",
      boardStatus: "",
      placementStatus: "",
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
      colorEnabled: false,
      autoColorEnabled: false,
      placementEnabled: false,
      aiTimeEnabled: false,
      playoutsEnabled: false,
      autoPlayColorStatus: "",
      platformLabel: "",
      bindingStatus: "",
      playColorKnown: false,
      showOnBoard: false,
      quickSyncActive: false,
      continuousSyncActive: false,
      quickSyncEnabled: true,
      continuousSyncEnabled: true,
      oneTimeSyncEnabled: true,
      syncInterval: 200,
      analysisRunning: false,
      analysisStateAvailable: false,
      analysisToggleEnabled: false,
      swapOrderEnabled: true,
      forceRebuildEnabled: true,
      quickSyncLabel: "",
      continuousSyncLabel: "",
      clearBoardEnabled: true,
      boardSelectionInsideEnabled: true,
      boardSelectionRectangleEnabled: false,
      boardSelectionLine1Enabled: false,
      configurationEnabled: true,
      twoWaySyncEnabled: true,
      analysisLabel: "",
      autoPlayToggleEnabled: true,
      autoPlayControlsEnabled: false,
      customBoardSizeEnabled: false,
      customBoardDimensionsEnabled: false,
      preferencesSaved: true,
      preferencesStatus: "",
      persistenceError: null,
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
      language: "host",
      diagnostics: false,
      dirty: false,
      dirtyStatus: "",
      errors: {},
      saveError: null
    },
    logs: [],
    update: null,
    identity: null,
    dialog: null
  };

  let state = preview ? structuredClone(previewState) : null;
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


  function normalizeTheme(value) {
    return value === "dark" || value === "light" ? value : "system";
  }

  function applyTheme(value) {
    const nextPreference = normalizeTheme(value);
    const resolvedTheme = nextPreference === "system"
      ? (systemThemeQuery.matches ? "dark" : "light")
      : nextPreference;
    if (themePreference === nextPreference
      && document.documentElement.dataset.theme === resolvedTheme
      && document.documentElement.style.colorScheme === resolvedTheme) return;
    themePreference = nextPreference;
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
    const value = state?.text?.[key];
    return typeof value === "string" && value ? value : previewValue;
  }

  function htmlLanguage(value) {
    return ({ cn: "zh-CN", en: "en", jp: "ja", kr: "ko" })[value] || "und";
  }

  function localizeStaticPage() {
    if (!preview && !Object.keys(state?.text || {}).length) return;
    if (localizedText === state?.text && localizedLanguage === state?.language) return;
    document.documentElement.lang = htmlLanguage(state?.language);
    $$('[data-i18n]').forEach(element => {
      element.textContent = t(element.dataset.i18n, element.textContent);
    });
    $$('[data-i18n-aria-label]').forEach(element => {
      element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel, element.getAttribute("aria-label")));
    });
    document.title = `ReadBoard / ${t("MainForm_title", "棋盘同步工具")}`;
    localizedText = state?.text;
    localizedLanguage = state?.language;
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

  function normalizePage(page) {
    return ["controlCenter", "settings", "rules", "about"].includes(page)
      ? page
      : "controlCenter";
  }

  function showPage(page) {
    const target = normalizePage(page);
    $$('[data-page-panel]').forEach(element => element.classList.toggle('active', element.dataset.pagePanel === target));
    $$('.nav-item').forEach(element => {
      const active = element.dataset.page === target;
      element.classList.toggle('active', active);
      if (active) element.setAttribute('aria-current', 'page');
      else element.removeAttribute('aria-current');
    });
  }

  function requestPage(page) {
    const target = normalizePage(page);
    if (preview) {
      state.page = target;
      render();
      return;
    }
    send("navigate", { page: target });
  }


  function renderShell() {
    const shell = state.shell || {};
    const version = shell.version || "v3.1.0";
    ["title-version", "version", "about-version", "project-version"].forEach(id => text(id, version));
    text("sync-status", shell.syncStatus || "");
    text("last-sync", shell.lastSync || "--:--:--");
    text("stone-count", shell.stoneCount ?? 0);
    text("duration", shell.duration || "--");
    text("host-state", shell.hostStatus || "");
    const maximizeButton = $('[data-command="window.maximize"]');
    if (maximizeButton) {
      maximizeButton.setAttribute("aria-label", shell.maximizeLabel || "");
      const icon = $(".icon", maximizeButton);
      if (icon) icon.innerHTML = shell.maximized ? "&#xE923;" : "&#xE922;";
    }
    $("#sync-status")?.classList.toggle("good", Boolean(shell.connected));
    $("#sync-dot").className = `dot${shell.connected ? " good" : ""}`;
    $("#host-dot").className = `dot${shell.connected ? " good" : ""}`;
    setStatus("target", shell.targetStatus || "", shell.targetWindowValid);
    setStatus("board", shell.boardStatus || "", shell.boardRegionRecognized);
    setStatus("placement", shell.placementStatus || "", shell.placementRegionResolved);
  }

  function setStatus(prefix, label, value) {
    text(`${prefix}-status`, label);
    const dot = document.getElementById(`${prefix}-dot`);
    if (dot) dot.className = `dot${value === true ? " good" : value === false && prefix === "target" ? " bad" : ""}`;
  }

  function renderControlCenter() {
    const control = state.controlCenter || {};
    text("context-platform", control.platformLabel || "");
    dynamicText("context-room", control.room || "--");
    text("context-moves", control.moves ?? "--");
    text("context-turn", control.nextTurn || "--");
    text("context-binding", control.bindingStatus || "");
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
    const preferencesStatus = $("#preferences-status");
    if (preferencesStatus) {
      const saved = control.preferencesSaved !== false;
      preferencesStatus.className = `preference-status${saved ? "" : " not-saved"}`;
      preferencesStatus.textContent = control.preferencesStatus || "";
      preferencesStatus.title = saved ? "" : (control.persistenceError || preferencesStatus.textContent);
    }
    setDisabled('input[name="platform"], input[name="boardSize"]', !control.configurationEnabled);
    setDisabled('input[name="boardSize"][value="custom"]', !control.customBoardSizeEnabled);
    setDisabled("#board-width, #board-height", !control.customBoardDimensionsEnabled);
    setDisabled("#two-way", !control.twoWaySyncEnabled);
    setDisabled("#auto-play", !control.autoPlayToggleEnabled);
    setDisabled('input[name="color"][value="black"], input[name="color"][value="white"]', !control.colorEnabled);
    setDisabled('input[name="color"][value="auto"]', !control.autoColorEnabled);
    setDisabled('input[name="placement"]', !control.placementEnabled);
    setDisabled('#ai-time', !control.aiTimeEnabled);
    setDisabled('#playouts', !control.playoutsEnabled);
    setDisabled("#first-policy", !control.firstPolicyEnabled);
    setDisabled('[data-command="identity.open"]', !control.identityEnabled);
    setDisabled("#show-on-board", !control.showOnBoardEnabled);
    text("quick-label", control.quickSyncLabel || "");
    text("continuous-label", control.continuousSyncLabel || "");
    text("analysis-label", control.analysisLabel || "");
    setDisabled('[data-command="sync.quick"]', !control.quickSyncEnabled);
    setDisabled('[data-command="sync.continuous"]', !control.continuousSyncEnabled);
    setDisabled('[data-command="sync.once"]', !control.oneTimeSyncEnabled);
    setDisabled('[data-command="sync.toggleAnalysis"]', !control.analysisToggleEnabled);
    setDisabled('[data-command="sync.swapOrder"]', !control.swapOrderEnabled);
    setDisabled('[data-command="sync.rebuild"]', !control.forceRebuildEnabled);
    setDisabled('[data-command="sync.clearBoard"]', !control.clearBoardEnabled);
    setDisabled('[data-command="board.select"][data-board-mode="inside"]', !control.boardSelectionInsideEnabled);
    setDisabled('[data-command="board.select"][data-board-mode="rectangle"]', !control.boardSelectionRectangleEnabled);
    setDisabled('[data-command="board.select"][data-board-mode="line1"]', !control.boardSelectionLine1Enabled);
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
        error.textContent = message;
        if (!error.id) error.id = `${input.dataset.setting}-error`;
        input.toggleAttribute("aria-invalid", Boolean(message));
        if (message) input.setAttribute("aria-describedby", error.id);
        else input.removeAttribute("aria-describedby");
      }
    });
    setChecked(`input[name="theme"][value="${cssValue(settings.theme || "system")}"]`, true);
    text("settings-dirty", settings.dirtyStatus || "");
    text("settings-error", settings.saveError || "");
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
      message.textContent = log && log.message ? log.message : "";
      row.append(time, tag, message);
      return row;
    }));
    list.scrollTop = list.scrollHeight;
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
    let actions = update.closeEnabled === false ? "" : button("update.close", update.closeLabel || "");
    if (mode === "checking") {
      body = message("&#xE895;", update.title || "", update.detail || "", '<div class="progress indeterminate"><i></i></div>');
    } else if (mode === "latest") {
      body = message("&#xE73E;", update.title || "", update.detail || "", `<p>${escapeHtml(update.message || "")}</p>`);
      actions = update.closeEnabled === false ? "" : button("update.close", update.doneLabel || "", "primary");
    } else if (mode === "available" || mode === "manual") {
      body = `<div class="update-details"><span>${escapeHtml(update.currentVersionLabel || "")}</span><b>${escapeHtml(update.currentVersion || "--")}</b><span>${escapeHtml(update.latestVersionLabel || "")}</span><b>${escapeHtml(update.latestVersion || "--")}</b><span>${escapeHtml(update.releaseDateLabel || "")}</span><b>${escapeHtml(update.releaseDate || "--")}</b>${mode === "manual" ? `<div class="update-warning"><b>${escapeHtml(update.title || "")}</b><p>${escapeHtml(update.message || update.detail || "")}</p></div>` : ""}<span>${escapeHtml(update.releaseNotesLabel || "")}</span><div class="release-notes">${escapeHtml(update.releaseNotes || "")}</div></div>`;
    } else if (mode === "notice") {
      body = message("&#xE946;", update.title || "", update.detail || "");
      actions = update.closeEnabled === false ? "" : button("update.close", update.doneLabel || "", "primary");
    } else if (mode === "check-failed") {
      body = message("&#xE783;", update.title || "", update.detail || "");
    } else if (mode === "processing") {
      body = `<h3>${escapeHtml(update.title || "")}</h3><p>${escapeHtml(update.detail || "")}</p>${progress(update.progress)}<div class="steps">${steps(update.steps)}</div>`;
      actions += `<button type="button" disabled>${escapeHtml(update.processingLabel || "")}</button>`;
    } else {
      body = `<div class="dialog-copy"><h3>${escapeHtml(update.title || "")}</h3><p>${escapeHtml(update.message || "")}</p><div class="update-warning"><b>${escapeHtml(update.errorTitle || "")}</b><p>${escapeHtml(update.error || "")}</p></div></div>`;
    }
    if (update.installEnabled) actions += button("update.install", update.downloadAndInstallLabel || "", "primary");
    if (update.openDownloadEnabled) actions += button("update.openDownload", update.downloadLabel || "", "primary");
    openModal("update", update.dialogTitle || "", body, actions, "min(660px, calc(100vw - 48px))");
  }

  function renderIdentity(identity) {
    const candidates = Array.isArray(identity.candidates) ? identity.candidates : [];
    const selected = identity.selectedId;
    const body = candidates.length
      ? `<p class="identity-intro">${escapeHtml(identity.prompt || "")}</p><b>${escapeHtml(identity.detectedNicknamesLabel || "")}</b><div class="candidate-list">${candidates.map(candidate => candidateHtml(candidate, selected, identity.savedId, identity.savedLabel)).join("")}</div>${selected ? `<p class="identity-intro">${escapeHtml(identity.selectedLabel || "")} ${escapeHtml(candidates.find(item => item.id === selected)?.label || "")}</p>` : ""}`
      : `<p class="identity-intro">${escapeHtml(identity.prompt || "")}</p><b>${escapeHtml(identity.detectedNicknamesLabel || "")}</b><div class="empty-state"><div><i class="icon">&#xE738;</i><h3>${escapeHtml(identity.emptyTitle || "")}</h3><p>${escapeHtml(identity.windowHint || "")}</p></div></div>`;
    const actions = identity.hasSavedIdentity ? button("identity.clearSaved", identity.clearSavedLabel || "", "danger-outline left") : "";
    openModal("identity", identity.dialogTitle || "", body, actions + button("identity.close", identity.cancelLabel || "") + button("identity.useOnce", identity.useOnceLabel || "", "", !identity.canUseOnce) + button("identity.saveAndUse", identity.saveAndUseLabel || "", "primary", !identity.canSaveAndUse), "min(780px, calc(100vw - 48px))");
  }

  function candidateHtml(candidate, selectedId, savedId, savedLabel) {
    const selected = candidate.id === selectedId;
    const previewUrl = safeImageUrl(candidate.previewUrl);
    return `<label class="candidate${selected ? " selected" : ""}"><input type="radio" name="candidate" value="${escapeAttr(candidate.id)}"${selected ? " checked" : ""}><b>${escapeHtml(candidate.label || "")}</b>${candidate.id === savedId ? `<span class="saved-pill">${escapeHtml(savedLabel || "")}</span>` : ""}${previewUrl ? `<img src="${escapeAttr(previewUrl)}" alt="${escapeAttr(candidate.previewAlt || "")}">` : ""}</label>`;
  }

  function safeImageUrl(value) {
    if (typeof value !== "string") return "";
    return /^(data:image\/(?:png|jpeg|webp);base64,|https:\/\/app\.readboard\/)/i.test(value) ? value : "";
  }

  function renderDialog(dialog) {
    const title = dialog.title || "";
    const message = dialog.message || "";
    const detail = dialog.detail || "";
    const confirmLabel = dialog.confirmLabel || "";
    const cancelLabel = dialog.cancelLabel || "";
    const dontShowAgainLabel = dialog.dontShowAgainLabel || "";
    const detailHtml = detail ? `<p>${escapeHtml(detail)}</p>` : "";
    let actions = button("dialog.cancel", cancelLabel);
    if (dialog.kind === "showInBoardHint") {
      actions = button("dialog.dontShowAgain", dontShowAgainLabel) + button("dialog.confirm", confirmLabel, "primary");
      openModal("dialog show-in-board-hint", title, `<div class="dialog-copy"><p>${escapeHtml(message)}</p>${detailHtml}</div>`, actions, "min(520px, calc(100vw - 48px))");
      return;
    }
    actions += button("dialog.confirm", confirmLabel, "primary");
    openModal("dialog", title, `<div class="dialog-copy"><h3>${escapeHtml(dialog.heading || title)}</h3><p>${escapeHtml(message)}</p>${detailHtml}</div>`, actions, "min(580px, calc(100vw - 48px))");
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
    if (!state) return;
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
      requestPage(pageButton.dataset.page);
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
    if (!(input instanceof HTMLInputElement) && !(input instanceof HTMLSelectElement)) return;
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
      if (activeModal === "update" && state.update?.closeEnabled === false) {
        event.preventDefault();
        return;
      }
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
    if (!message || message.type !== "state" || !message.payload
      || typeof message.payload !== "object") return;
    const snapshot = message.payload;
    if (Array.isArray(snapshot)
      || !["page", "language", "text", "shell", "controlCenter", "settings", "update", "identity", "dialog", "logs"]
        .every(key => Object.prototype.hasOwnProperty.call(snapshot, key))) return;
    state = structuredClone(snapshot);
    document.body.classList.remove("awaiting-state");
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

  if (preview) render();
})();
