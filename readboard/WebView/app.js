(() => {
  "use strict";

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const webview = window.chrome?.webview;
  const preview = !webview;
  const baseViewport = Object.freeze({ width: 1100, height: 680 });
  const minimumViewport = Object.freeze({ width: 960, height: 600 });
  const minimumScale = minimumViewport.width / baseViewport.width;
  const initialState = {
    page: "controlCenter",
    shell: {
      version: "v3.0.8",
      connected: false,
      syncStatus: "等待宿主连接",
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
      showOnBoard: false,
      continuousSync: false,
      syncInterval: 200,
      analysisRunning: false,
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
      disableShowShortcut: false,
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
    logs: preview ? [{ time: "--:--:--", level: "INFO", message: "本地预览模式，等待宿主状态" }] : [],
    update: null,
    identity: null,
    dialog: null
  };

  let state = structuredClone(initialState);
  let activeModal = null;
  let modalOpener = null;
  let resizeFrame = 0;

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

  function send(type, payload = {}) {
    const message = { type, payload };
    if (webview) webview.postMessage(message);
    else console.info("ReadBoard preview command", message);
  }

  function text(id, value) {
    const element = document.getElementById(id);
    if (element) element.textContent = value ?? "";
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
    const version = shell.version || "v3.0.8";
    ["title-version", "version", "about-version", "project-version"].forEach(id => text(id, version));
    text("sync-status", shell.syncStatus || (shell.connected ? "已连接" : "等待宿主连接"));
    text("last-sync", shell.lastSync || "--:--:--");
    text("stone-count", shell.stoneCount ?? 0);
    text("duration", shell.duration || "--");
    text("host-state", shell.connected ? "宿主已连接" : "等待宿主");
    const maximizeButton = $('[data-command="window.maximize"]');
    if (maximizeButton) {
      maximizeButton.setAttribute("aria-label", shell.maximized ? "还原" : "最大化");
      const icon = $(".icon", maximizeButton);
      if (icon) icon.innerHTML = shell.maximized ? "&#xE923;" : "&#xE922;";
    }
    $("#sync-status")?.classList.toggle("good", Boolean(shell.connected));
    $("#sync-dot").className = `dot${shell.connected ? " good" : ""}`;
    $("#host-dot").className = `dot${shell.connected ? " good" : ""}`;
    setStatus("target", shell.targetWindowValid === true
      ? "目标窗口有效"
      : shell.targetWindowValid === false ? "目标窗口已失效，请重新选择" : "等待选择目标窗口", shell.targetWindowValid);
    setStatus("board", shell.boardRegionRecognized ? "棋盘区域已识别" : "等待首次棋盘识别", shell.boardRegionRecognized);
    setStatus("placement", shell.placementRegionResolved ? "落子区域已解析" : "落子区域暂不可用", shell.placementRegionResolved);
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
    text("context-turn", control.nextTurn || "--");
    text("context-binding", control.titleBound ? "已绑定" : "未绑定");
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
    setDisabled('input[name="color"], input[name="placement"], #ai-time, #playouts, #first-policy', !control.autoPlayControlsEnabled);
    setDisabled('[data-command="identity.open"]', !control.identityEnabled);
    setDisabled("#show-on-board", !control.showOnBoardEnabled);
    text("continuous-label", `${control.continuousSync ? "停止持续同步" : "持续同步"} (${control.syncInterval ?? 200}ms)`);
    text("analysis-label", control.analysisRunning ? "停止分析" : "分析/停止");
    const continuous = $("[data-command='sync.continuous']");
    if (continuous) {
      continuous.classList.toggle("running", Boolean(control.continuousSync));
      continuous.setAttribute("aria-pressed", String(Boolean(control.continuousSync)));
    }
    $("[data-command='sync.toggleAnalysis']")?.setAttribute("aria-pressed", String(Boolean(control.analysisRunning)));
  }

  function platformLabel(value) {
    return ({ fox: "野狐", foxBackground: "野狐(后台落子)", yike: "弈客", yicheng: "弈城", sina: "新浪", otherBackground: "其他(后台)", otherForeground: "其他(前台)" })[value] || "未选择";
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
    text("settings-dirty", settings.dirty ? "有尚未保存的更改" : "当前没有未保存的更改");
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
      message.textContent = log.message || "";
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
    let actions = button("update.close", "关闭");
    if (mode === "checking") {
      body = message("&#xE895;", "正在检查可用更新", "正在连接 GitHub Release，请稍候。", '<div class="progress indeterminate"><i></i></div>');
    } else if (mode === "latest") {
      body = message("&#xE73E;", "当前已是最新版本", `ReadBoard ${escapeHtml(update.currentVersion || state.shell.version || "")}`, `<p>${escapeHtml(update.completedAt || update.message || "刚刚完成检查")}</p>`);
      actions = button("update.close", "完成", "primary");
    } else if (mode === "available" || mode === "manual") {
      body = `<div class="update-details"><span>当前版本</span><b>${escapeHtml(update.currentVersion || "--")}</b><span>最新版本</span><b>${escapeHtml(update.latestVersion || "--")}</b><span>发布日期</span><b>${escapeHtml(update.releaseDate || "--")}</b>${mode === "manual" ? `<div class="update-warning"><b>当前宿主不支持托管安装</b><p>可打开 Release 页面手动下载更新。</p></div>` : `<span>更新说明</span><div class="release-notes">${escapeHtml(update.releaseNotes || "暂无更新说明")}</div>`}</div>`;
      actions += button(mode === "manual" ? "update.openDownload" : "update.install", mode === "manual" ? "去下载" : "下载并安装", "primary");
    } else if (mode === "processing") {
      body = `<h3>${escapeHtml(update.title || "正在准备更新包")}</h3><p>${escapeHtml(update.detail || "请稍候…")}</p>${progress(update.progress)}<div class="steps">${steps(update.steps)}</div>`;
      actions = '<button type="button" disabled>处理中…</button>';
    } else {
      body = `<div class="dialog-copy"><h3>${escapeHtml(update.title || "安装未完成")}</h3><p>${escapeHtml(update.message || "更新未完成，已切换为手动下载。")}</p><div class="update-warning"><b>${escapeHtml(update.errorTitle || "操作失败")}</b><p>${escapeHtml(update.error || "可稍后重试或手动下载。")}</p></div></div>`;
      actions += button("update.openDownload", "去下载", "primary");
    }
    openModal("update", "检查更新", body, actions, "min(660px, calc(100vw - 48px))");
  }

  function renderIdentity(identity) {
    const candidates = Array.isArray(identity.candidates) ? identity.candidates : [];
    const selected = identity.selectedId;
    const body = candidates.length
      ? `<p class="identity-intro">请选择你在野狐当前房间里的玩家行。</p><b>可选玩家行</b><div class="candidate-list">${candidates.map(candidate => candidateHtml(candidate, selected, identity.savedId)).join("")}</div>${selected ? `<p class="identity-intro">已选择“${escapeHtml(candidates.find(item => item.id === selected)?.label || "") }”</p>` : ""}`
      : `<p class="identity-intro">请选择你在野狐当前房间里的玩家行。</p><b>可选玩家行</b><div class="empty-state"><div><i class="icon">&#xE738;</i><h3>暂未识别到可选玩家行</h3><p>请确认野狐棋局窗口可见，然后重新打开身份选择。</p></div></div>`;
    const actions = identity.hasSavedIdentity ? button("identity.clearSaved", "清除保存", "danger-outline left") : "";
    openModal("identity", "选择野狐身份", body, actions + button("identity.close", "取消") + button("identity.useOnce", "本次使用", "", !selected) + button("identity.saveAndUse", "保存并使用", "primary", !selected), "min(780px, calc(100vw - 48px))");
  }

  function candidateHtml(candidate, selectedId, savedId) {
    const selected = candidate.id === selectedId;
    const previewUrl = safeImageUrl(candidate.previewUrl);
    return `<label class="candidate${selected ? " selected" : ""}"><input type="radio" name="candidate" value="${escapeAttr(candidate.id)}"${selected ? " checked" : ""}><b>${escapeHtml(candidate.label || "未命名候选")}</b>${candidate.id === savedId ? '<span class="saved-pill">已保存</span>' : ""}${previewUrl ? `<img src="${escapeAttr(previewUrl)}" alt="${escapeAttr(candidate.label || "候选玩家行")}截图">` : ""}</label>`;
  }

  function safeImageUrl(value) {
    if (typeof value !== "string") return "";
    return /^(data:image\/(?:png|jpeg|webp);base64,|https:\/\/app\.readboard\/)/i.test(value) ? value : "";
  }

  function renderDialog(dialog) {
    const content = {
      resetDefaults: ["恢复默认设置", "将当前设置草稿恢复为默认值。此操作不会立即写入配置，仍需点击保存设置。", "恢复默认"],
      diagnostics: ["开启调试诊断", "调试诊断可能产生较大的文件。确认后仅修改当前设置草稿，保存设置后生效。", "继续开启"],
      themeRestart: ["需要重新启动", "颜色模式已保存。重新启动 ReadBoard 后可完整应用新的界面外观。", "知道了"],
      showInBoardHint: ["原棋盘上显示选点", "此模式依赖前台同步状态；关闭后可恢复双向同步。", "确定"]
    }[dialog.kind] || [dialog.title || "提示", dialog.message || "", "确定"];
    let actions = button("dialog.cancel", dialog.cancelLabel || "取消");
    if (dialog.kind === "themeRestart") actions = "";
    if (dialog.kind === "showInBoardHint") actions += button("dialog.dontShowAgain", "不再提示");
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
    state = merge(initialState, message.payload);
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
