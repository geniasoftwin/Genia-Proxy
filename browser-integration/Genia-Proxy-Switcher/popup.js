// Genia Proxy Switcher Direct 5.6.0 Stable popup

"use strict";

const DEFAULT_STATE = Object.freeze({
  enabled: false,
  status: "off",
  port: 2080,
  profileName: "GeniaProxy / NekoBox / Hysteria / VLESS",
  strictLocal: true,
  trustedExitIps: [],
  expectedExitIp: null,
  webRtcShieldEnabled: true,
  webRtcCompatibility: false,
  hardKillSwitch: true,
  killSwitchEngaged: false,
  killSwitchReason: null,
  shieldState: "off",
  webRtcPolicy: null,
  webRtcLevelOfControl: null,
  predictionLevelOfControl: null,
  consecutiveFailures: 0,
  lastCheckAt: null,
  lastError: null,
  exitIp: null,
  latencyMs: null,
  protectionsApplied: false,
  lastCheckSource: null,
  traceLocation: null,
  traceColo: null,
  proxyControl: null,
  proxyConfigVerified: false,
  webRtcProtected: false,
  predictionDisabled: false,
  lastProxyError: null,
  lastProxyErrorAt: null,
  lastProxyFatal: false,
  lastGuardAt: null,
  lastGuardEvent: null,
  lastSelfTestAt: null,
  lastSelfTestStatus: null,
  lastSelfTestSummary: null,
  lastSelfTestIceCandidates: 0,
  lastSelfTestRawIpExposed: false,
  lastSelfTestMdnsCandidates: 0,
  lastSelfTestLocalRawCandidates: 0,
  lastSelfTestPublicRawCandidates: 0,
  lastSelfTestSafeSpecialCandidates: 0,
  lastSelfTestHostUdpCandidates: 0,
  lastSelfTestHostTcpCandidates: 0,
  lastSelfTestUnknownCandidates: 0,
  eventLog: [],
  auditHistory: [],
  lastAuditAt: null,
  lastAuditScore: null,
  lastAuditStatus: null,
  lastAuditSummary: null,
  routeStatsSummary: {},
  trustedExitTest: { active: false, startedAt: null, completedAt: null, seenIps: [] }
});

const elements = {
  appIcon: document.getElementById("appIcon"),
  statusCard: document.getElementById("statusCard"),
  statusTitle: document.getElementById("statusTitle"),
  statusDetail: document.getElementById("statusDetail"),
  routeInfo: document.getElementById("routeInfo"),
  lastCheck: document.getElementById("lastCheck"),
  shieldCard: document.getElementById("shieldCard"),
  shieldBadge: document.getElementById("shieldBadge"),
  shieldDetail: document.getElementById("shieldDetail"),
  selfTestButton: document.getElementById("selfTestButton"),
  selfTestResult: document.getElementById("selfTestResult"),
  profileSelect: document.getElementById("profileSelect"),
  customRow: document.getElementById("customRow"),
  customPort: document.getElementById("customPort"),
  applyPortButton: document.getElementById("applyPortButton"),
  expectedIp: document.getElementById("expectedIp"),
  strictLocal: document.getElementById("strictLocal"),
  webRtcShield: document.getElementById("webRtcShield"),
  webRtcCompatibility: document.getElementById("webRtcCompatibility"),
  hardKillSwitch: document.getElementById("hardKillSwitch"),
  applySettingsButton: document.getElementById("applySettingsButton"),
  toggleButton: document.getElementById("toggleButton"),
  checkButton: document.getElementById("checkButton"),
  errorBox: document.getElementById("errorBox"),
  copyDiagnosticsButton: document.getElementById("copyDiagnosticsButton"),
  clearLogButton: document.getElementById("clearLogButton"),
  dailyAuditButton: document.getElementById("dailyAuditButton"),
  trustedExitTestButton: document.getElementById("trustedExitTestButton"),
  auditScore: document.getElementById("auditScore"),
  trustedExitTestStatus: document.getElementById("trustedExitTestStatus"),
  managerCard: document.getElementById("managerCard"),
  managerBadge: document.getElementById("managerBadge"),
  managerStatusText: document.getElementById("managerStatusText"),
  managerVersionEngine: document.getElementById("managerVersionEngine"),
  managerProfile: document.getElementById("managerProfile"),
  managerLocalProxy: document.getElementById("managerLocalProxy"),
  managerEndpoint: document.getElementById("managerEndpoint"),
  managerMode: document.getElementById("managerMode"),
  managerVerifiedExit: document.getElementById("managerVerifiedExit"),
  managerObservedExit: document.getElementById("managerObservedExit"),
  managerMonitorStatus: document.getElementById("managerMonitorStatus"),
  managerCoherence: document.getElementById("managerCoherence"),
  managerConnectButton: document.getElementById("managerConnectButton"),
  managerRefreshButton: document.getElementById("managerRefreshButton"),
  managerNote: document.getElementById("managerNote"),
  routeManager: document.getElementById("routeManager"),
  routeSocks: document.getElementById("routeSocks"),
  routeChrome: document.getElementById("routeChrome"),
  routeExit: document.getElementById("routeExit"),
  routeStats: document.getElementById("routeStats"),
  auditHistoryText: document.getElementById("auditHistoryText"),
  eventLogList: document.getElementById("eventLogList"),
  diagEndpoint: document.getElementById("diagEndpoint"),
  diagProxyControl: document.getElementById("diagProxyControl"),
  diagProxyConfig: document.getElementById("diagProxyConfig"),
  diagWebRtc: document.getElementById("diagWebRtc"),
  diagWebRtcControl: document.getElementById("diagWebRtcControl"),
  diagPrediction: document.getElementById("diagPrediction"),
  diagLiveGuard: document.getElementById("diagLiveGuard"),
  diagKillSwitch: document.getElementById("diagKillSwitch"),
  diagStrictLocal: document.getElementById("diagStrictLocal"),
  diagExitIp: document.getElementById("diagExitIp"),
  diagExpectedIp: document.getElementById("diagExpectedIp"),
  diagSource: document.getElementById("diagSource"),
  diagLatency: document.getElementById("diagLatency"),
  diagCloudflare: document.getElementById("diagCloudflare"),
  diagFailures: document.getElementById("diagFailures"),
  diagSelfTest: document.getElementById("diagSelfTest"),
  diagBrowserTz: document.getElementById("diagBrowserTz"),
  diagTimezoneMatch: document.getElementById("diagTimezoneMatch"),
  diagProxyError: document.getElementById("diagProxyError"),
  diagAudit: document.getElementById("diagAudit"),
  diagCadence: document.getElementById("diagCadence"),
  diagManager: document.getElementById("diagManager"),
  diagManagerRoute: document.getElementById("diagManagerRoute")
};

let currentState = { ...DEFAULT_STATE };
let busy = false;
let trustedTestCooldownTimer = null;

const SELF_TEST_CACHE_MS = 60 * 1000;
const TRUSTED_TEST_RESTART_COOLDOWN_MS = 15 * 1000;

function sendMessage(action, payload = {}) {
  return new Promise((resolve, reject) => {
    chrome.runtime.sendMessage({ action, ...payload }, response => {
      const runtimeError = chrome.runtime.lastError;
      if (runtimeError) {
        reject(new Error(runtimeError.message));
        return;
      }
      if (!response?.ok) {
        const error = new Error(response?.error || "Фоновый процесс не подтвердил команду.");
        error.state = response?.state || null;
        reject(error);
        return;
      }
      resolve(response.state);
    });
  });
}

function normalizePort(value) {
  const port = Number(value);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new RangeError("Порт должен быть целым числом от 1 до 65535.");
  }
  return port;
}

function showError(message) {
  elements.errorBox.textContent = message;
  elements.errorBox.hidden = false;
}

function clearError() {
  elements.errorBox.textContent = "";
  elements.errorBox.hidden = true;
}

function syncCompatibilityControl() {
  elements.webRtcCompatibility.disabled = busy || !elements.webRtcShield.checked;
}

function trustedTestRestartCoolingDown(state = currentState) {
  const completedAt = Number(state?.trustedExitTest?.completedAt) || 0;
  return completedAt > 0 && Date.now() - completedAt < TRUSTED_TEST_RESTART_COOLDOWN_MS;
}

function hasFreshPassedSelfTest(state = currentState) {
  const at = Number(state?.lastSelfTestAt) || 0;
  return state?.lastSelfTestStatus === "passed" && at > 0 && Date.now() - at < SELF_TEST_CACHE_MS;
}

function canCheckNow(state = currentState) {
  return Boolean(state?.enabled || (state?.managerBridgeConnected && state?.managerMode === "tun"));
}

function setBusy(value) {
  busy = value;
  const tunMonitor = currentState?.managerBridgeConnected && currentState?.managerMode === "tun";
  elements.toggleButton.disabled = value || tunMonitor;
  elements.checkButton.disabled = value || !canCheckNow(currentState);
  elements.selfTestButton.disabled = value;
  elements.profileSelect.disabled = value;
  elements.customPort.disabled = value;
  elements.applyPortButton.disabled = value;
  elements.expectedIp.disabled = value;
  elements.strictLocal.disabled = value;
  elements.webRtcShield.disabled = value;
  elements.hardKillSwitch.disabled = value;
  elements.applySettingsButton.disabled = value;
  elements.copyDiagnosticsButton.disabled = value;
  elements.clearLogButton.disabled = value;
  const auditAvailable = Boolean(currentState.enabled || (currentState.managerBridgeConnected && currentState.managerMode === "tun"));
  elements.dailyAuditButton.disabled = value || !auditAvailable;
  elements.trustedExitTestButton.disabled = value || !currentState.enabled || trustedTestRestartCoolingDown(currentState);
  syncCompatibilityControl();
}

function getSelectedPortAndName() {
  const option = elements.profileSelect.selectedOptions[0];
  if (elements.profileSelect.value === "custom") {
    return { port: normalizePort(elements.customPort.value), profileName: "Свой порт" };
  }
  return {
    port: normalizePort(elements.profileSelect.value),
    profileName: option?.dataset.name || option?.textContent.trim() || "SOCKS5"
  };
}

function getSelectedProfile() {
  return {
    ...getSelectedPortAndName(),
    strictLocal: elements.strictLocal.checked,
    trustedExitIps: elements.expectedIp.value.trim(),
    webRtcShieldEnabled: elements.webRtcShield.checked,
    webRtcCompatibility: elements.webRtcShield.checked && elements.webRtcCompatibility.checked,
    hardKillSwitch: elements.hardKillSwitch.checked
  };
}

function syncProfileControls(state) {
  const port = normalizePort(state.port || DEFAULT_STATE.port);
  const knownOption = Array.from(elements.profileSelect.options)
    .find(option => option.value === String(port));

  if (knownOption) {
    elements.profileSelect.value = knownOption.value;
    elements.customRow.hidden = true;
  }
  else {
    elements.profileSelect.value = "custom";
    elements.customPort.value = String(port);
    elements.customRow.hidden = false;
  }

  elements.expectedIp.value = Array.isArray(state.trustedExitIps) ? state.trustedExitIps.join(", ") : "";
  elements.strictLocal.checked = state.strictLocal !== false;
  elements.webRtcShield.checked = state.webRtcShieldEnabled !== false;
  elements.webRtcCompatibility.checked = Boolean(state.webRtcCompatibility);
  elements.hardKillSwitch.checked = state.hardKillSwitch !== false;
  syncCompatibilityControl();
}

function formatTime(timestamp) {
  if (!timestamp) return null;
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) return null;
  return new Intl.DateTimeFormat("ru-RU", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(date);
}

function formatLastCheck(timestamp) {
  const time = formatTime(timestamp);
  return time ? `Последняя проверка: ${time}` : "Проверок ещё не было";
}

function formatUptime(seconds) {
  const total = Math.max(0, Math.floor(Number(seconds) || 0));
  if (!total) return null;
  const days = Math.floor(total / 86400);
  const hours = Math.floor((total % 86400) / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  if (days) return `${days}д ${hours}ч`;
  if (hours) return `${hours}ч ${minutes}м`;
  return `${minutes}м`;
}

function getStatusPresentation(state) {
  const endpoint = `127.0.0.1:${state.port}`;
  if (state.managerTransition === "to_tun") {
    return { title: "Switching to TUN… · WEB BLOCKED", detail: "Browser SOCKS освобождён; ждём свежий TUN exit probe перед T✓." };
  }
  if (state.managerTransition === "to_local") {
    return { title: "Switching to Local…", detail: `Восстанавливаем SOCKS5 ${endpoint} и повторно подтверждаем маршрут.` };
  }
  switch (state.status) {
    case "checking":
      return { title: "Проверяем Privacy Shield", detail: `SOCKS5 ${endpoint} применён. Проверяем маршрут, WebRTC и защиту…` };
    case "verified":
      return {
        title: "VERIFIED · маршрут подтверждён",
        detail: Array.isArray(state.trustedExitIps) && state.trustedExitIps.length
          ? `Chrome идёт через ${endpoint}; выходной IP входит в Trusted Exit.`
          : `Chrome идёт через ${endpoint}; Cloudflare подтвердил внешний IP.`
      };
    case "degraded":
      return { title: state.killSwitchEngaged ? "DEGRADED · WEB BLOCKED" : "DEGRADED · IP не подтверждён", detail: state.lastError || "Маршрут отвечает, но основной IP-check недоступен." };
    case "wrong_exit":
      return { title: "ВНИМАНИЕ · недоверенный выходной IP", detail: state.lastError || "Наблюдаемый IP отсутствует в Trusted Exit IPs." };
    case "unreachable":
      return { title: state.killSwitchEngaged ? "Прокси недоступен · WEB BLOCKED" : "Прокси недоступен", detail: state.lastError || `SOCKS5 ${endpoint} или VPS-маршрут не отвечает.` };
    case "blocked":
      return { title: state.killSwitchEngaged ? "Privacy Shield · WEB BLOCKED" : "Privacy Shield перехвачен", detail: state.lastError || "Настройками управляет политика Chrome или другое расширение." };
    case "error":
      return { title: state.killSwitchEngaged ? "Ошибка · WEB BLOCKED" : "Ошибка", detail: state.lastError || "Не удалось подтвердить защищённые настройки." };
    default:
      if (state.managerBridgeConnected && state.managerMode === "tun") {
        const monitor = state.managerMonitorStatus || "idle";
        const observed = state.managerMonitorObservedExitIp ? ` · IP ${state.managerMonitorObservedExitIp}` : "";
        return {
          title: monitor === "mismatch" ? "TUN Monitor · MISMATCH" : monitor === "verified" ? "TUN Monitor · VERIFIED" : "TUN Monitor · Manager connected",
          detail: `Браузерный SOCKS выключен. Chrome использует системный TUN GeniaProxy${observed}.`
        };
      }
      return { title: "Прокси выключен", detail: "Chrome использует системные настройки подключения." };
  }
}

function setDiagnostic(element, text, className = "") {
  element.textContent = text;
  element.className = `diag-value${className ? ` ${className}` : ""}`;
}

function renderShield(state) {
  const privacyActive = Boolean(state.enabled || (state.managerBridgeConnected && state.managerMode === "tun"));
  let shield = state.shieldState || "off";
  if (!privacyActive || !state.webRtcShieldEnabled) shield = "off";
  if (privacyActive && state.webRtcShieldEnabled && (!state.webRtcProtected || state.shieldState === "compromised")) shield = "compromised";

  elements.shieldCard.dataset.shield = shield;
  const badge = {
    protected: "PROTECTED",
    compatibility: "COMPATIBILITY",
    compromised: "COMPROMISED",
    off: "OFF"
  }[shield] || "OFF";
  elements.shieldBadge.textContent = badge;

  if (shield === "protected") {
    elements.shieldDetail.textContent = `Maximum protection · ${state.webRtcPolicy || "disable_non_proxied_udp"} · Live Guard активен.`;
  }
  else if (shield === "compatibility") {
    elements.shieldDetail.textContent = `Compatibility Mode · ${state.webRtcPolicy || "default_public_interface_only"}. Используйте только при проблемах с WebRTC.`;
  }
  else if (shield === "compromised") {
    elements.shieldDetail.textContent = state.killSwitchEngaged
      ? "Настройка WebRTC/Proxy не подтверждена. Hard Kill Switch блокирует браузерный web-трафик до восстановления."
      : "Настройка WebRTC/Proxy не подтверждена. Live Guard пытается восстановить защиту.";
  }
  else {
    elements.shieldDetail.textContent = state.webRtcShieldEnabled
      ? "Включается автоматически вместе с защищённым маршрутом."
      : "WebRTC Shield отключён вручную.";
  }

  if (state.lastSelfTestAt) {
    const mark = state.lastSelfTestStatus === "passed" ? "✓" : state.lastSelfTestStatus === "failed" ? "✕" : "!";
    const label = state.lastSelfTestStatus === "passed" ? "PROTECTED" : state.lastSelfTestStatus === "failed" ? "FAILED" : "CHECK";
    elements.selfTestResult.textContent = `${mark} ${label} · ${formatTime(state.lastSelfTestAt) || ""}`;
  }
  else {
    elements.selfTestResult.textContent = "Проверка ещё не запускалась";
  }
}

const COLO_TIMEZONES = Object.freeze({
  AMS: "Europe/Amsterdam",
  BRU: "Europe/Brussels",
  CDG: "Europe/Paris",
  FRA: "Europe/Berlin",
  BER: "Europe/Berlin",
  DUS: "Europe/Berlin",
  HAM: "Europe/Berlin",
  MUC: "Europe/Berlin",
  LHR: "Europe/London",
  DUB: "Europe/Dublin",
  MAD: "Europe/Madrid",
  BCN: "Europe/Madrid",
  LIS: "Europe/Lisbon",
  MXP: "Europe/Rome",
  FCO: "Europe/Rome",
  VIE: "Europe/Vienna",
  ZRH: "Europe/Zurich",
  PRG: "Europe/Prague",
  WAW: "Europe/Warsaw",
  CPH: "Europe/Copenhagen",
  OSL: "Europe/Oslo",
  ARN: "Europe/Stockholm",
  HEL: "Europe/Helsinki",
  ATH: "Europe/Athens",
  OTP: "Europe/Bucharest",
  SOF: "Europe/Sofia",
  IST: "Europe/Istanbul"
});

function getBrowserTimeZone() {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "неизвестно";
  }
  catch (_error) {
    return "неизвестно";
  }
}

function getTimeZoneOffsetMinutes(timeZone, at = new Date()) {
  try {
    const part = new Intl.DateTimeFormat("en-US", {
      timeZone,
      timeZoneName: "longOffset",
      hour: "2-digit"
    }).formatToParts(at).find(item => item.type === "timeZoneName")?.value || "";
    if (part === "GMT" || part === "UTC") return 0;
    const match = /GMT([+-])(\d{2}):(\d{2})/.exec(part);
    if (!match) return null;
    const minutes = Number(match[2]) * 60 + Number(match[3]);
    return match[1] === "-" ? -minutes : minutes;
  }
  catch (_error) {
    return null;
  }
}

function formatUtcOffset(minutes) {
  if (!Number.isFinite(minutes)) return "?";
  const sign = minutes >= 0 ? "+" : "-";
  const absolute = Math.abs(minutes);
  const hours = String(Math.floor(absolute / 60)).padStart(2, "0");
  const mins = String(absolute % 60).padStart(2, "0");
  return `UTC${sign}${hours}:${mins}`;
}

function getTimezoneFingerprint(state) {
  const browserTz = getBrowserTimeZone();
  const browserOffset = -new Date().getTimezoneOffset();
  const expectedTz = COLO_TIMEZONES[String(state.traceColo || "").toUpperCase()] || null;
  if (!expectedTz) {
    return {
      browserTz,
      browserOffset,
      expectedTz: null,
      expectedOffset: null,
      status: "unknown",
      text: "не оценивается для этого colo"
    };
  }
  const expectedOffset = getTimeZoneOffsetMinutes(expectedTz);
  if (!Number.isFinite(expectedOffset)) {
    return { browserTz, browserOffset, expectedTz, expectedOffset: null, status: "unknown", text: "не удалось вычислить" };
  }
  const match = browserOffset === expectedOffset;
  return {
    browserTz,
    browserOffset,
    expectedTz,
    expectedOffset,
    status: match ? "match" : "mismatch",
    text: match
      ? `MATCH · ${formatUtcOffset(browserOffset)}`
      : `MISMATCH · browser ${formatUtcOffset(browserOffset)} / ${state.traceColo} ${formatUtcOffset(expectedOffset)}`
  };
}


function renderManager(state) {
  const status = state.managerBridgeStatus || "off";
  const connected = state.managerBridgeConnected && status === "connected";
  const labels = {
    off: "OFF",
    api_unavailable: "API UNAVAILABLE",
    connecting: "CONNECTING",
    connected: "CONNECTED",
    unavailable: "APP OFFLINE",
    disconnected: "MANAGER OFFLINE",
    stale: "STALE / RECONNECT",
    error: "ERROR"
  };
  elements.managerBadge.textContent = labels[status] || status.toUpperCase();
  elements.managerBadge.className = `manager-badge ${connected ? "connected" : ["unavailable", "disconnected", "stale", "error", "api_unavailable"].includes(status) ? "warn" : "off"}`;
  const uptimeText = formatUptime(state.managerUptimeSeconds);
  elements.managerStatusText.textContent = connected
    ? `CONNECTED${uptimeText ? ` · uptime ${uptimeText}` : ""}${state.managerLastSeenAt ? ` · ${formatTime(state.managerLastSeenAt)}` : ""}`
    : state.managerLastError || (status === "api_unavailable" ? "Fetch API недоступен" : status === "unavailable" ? "GeniaProxy Direct Bridge недоступен" : status === "disconnected" ? "Direct Bridge доступен; Manager не активен" : status === "stale" ? "Heartbeat просрочен; выполняется переподключение" : "Bridge не подключён");
  elements.managerVersionEngine.textContent = [state.managerVersion ? `Manager v${state.managerVersion}` : null, state.managerEngine, state.managerBridgeHostVersion ? `Direct ${state.managerBridgeHostVersion}` : null].filter(Boolean).join(" · ") || "—";
  elements.managerProfile.textContent = state.managerProfile || "—";
  elements.managerMode.textContent = state.managerMode ? state.managerMode.toUpperCase() : "—";
  elements.managerLocalProxy.textContent = state.managerLocalProxy || (state.managerMode === "tun" ? "system TUN" : "—");
  elements.managerEndpoint.textContent = state.managerEndpoint || "—";
  const verifiedAt = state.managerVerifiedExitAt ? ` · ${formatTime(state.managerVerifiedExitAt)}` : "";
  elements.managerVerifiedExit.textContent = state.managerVerifiedExitIp ? `${state.managerVerifiedExitIp}${verifiedAt}` : "—";
  elements.managerObservedExit.textContent = state.managerMode === "tun" ? (state.managerMonitorObservedExitIp || "—") : (state.exitIp || "—");
  elements.managerMonitorStatus.textContent = state.managerMode === "tun" && !state.enabled ? String(state.managerMonitorStatus || "idle").toUpperCase() : "—";
  const coherence = state.managerCoherence || "unknown";
  elements.managerCoherence.textContent = coherence === "match" ? "MATCH ✅" : coherence === "mismatch" ? "MISMATCH ⚠" : "не проверено";
  elements.managerCoherence.className = `manager-value ${coherence === "match" ? "diag-good" : coherence === "mismatch" ? "diag-warn" : ""}`;
  elements.managerConnectButton.textContent = state.managerBridgeEnabled ? "Отключить Direct Bridge" : "Подключить Direct Bridge";
  elements.managerRefreshButton.disabled = busy || !state.managerBridgeEnabled;
  elements.managerConnectButton.disabled = busy;
  elements.routeManager.textContent = connected ? [state.managerEngine || "READY", state.managerMode ? state.managerMode.toUpperCase() : null].filter(Boolean).join(" / ") : "—";
  elements.routeSocks.textContent = state.managerLocalProxy ? state.managerLocalProxy.replace(/^socks5:\/\//, "") : `127.0.0.1:${state.port}`;
  elements.routeChrome.textContent = state.status === "verified" ? "VERIFIED" : String(state.status || "OFF").toUpperCase();
  elements.routeExit.textContent = state.enabled ? (state.exitIp || "—") : state.managerMode === "tun" ? (state.managerMonitorObservedExitIp || "—") : "—";
  elements.managerNote.textContent = connected
    ? state.managerMode === "tun" && !state.enabled
      ? "TUN Monitor read-only: браузерный SOCKS выключен; расширение наблюдает системный TUN через Cloudflare и не управляет VPN."
      : "GeniaProxy.exe сообщает состояние напрямую через loopback; Cloudflare Trusted Exit остаётся независимым источником фактической проверки браузерного выхода."
    : `Direct Bridge: http://127.0.0.1:47831/v1/status. NativeHost не используется. Профиль Chrome: ${chrome.runtime.id}`;
}

function renderDiagnostics(state) {
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  const tunVerified = tunMonitor && state.managerMonitorStatus === "verified";
  const observedIp = tunMonitor ? state.managerMonitorObservedExitIp : state.exitIp;
  const observedSource = tunMonitor ? state.managerMonitorSource : state.lastCheckSource;
  const observedLatency = tunMonitor ? state.managerMonitorLatencyMs : state.latencyMs;
  const observedLocation = tunMonitor ? state.managerMonitorTraceLocation : state.traceLocation;
  const observedColo = tunMonitor ? state.managerMonitorTraceColo : state.traceColo;

  setDiagnostic(elements.diagEndpoint, tunMonitor ? "system TUN" : `127.0.0.1:${state.port}`);
  setDiagnostic(elements.diagProxyControl, tunMonitor ? "system TUN · browser proxy OFF" : (state.proxyControl || (state.enabled ? "не проверено" : "—")), tunMonitor ? (tunVerified ? "diag-good" : "diag-warn") : state.proxyControl === "controlled_by_this_extension" ? "diag-good" : state.enabled ? "diag-warn" : "");
  setDiagnostic(elements.diagProxyConfig, tunMonitor ? "browser proxy OFF" : state.proxyConfigVerified ? "подтверждена" : state.enabled ? "не подтверждена" : "—", tunMonitor ? "diag-good" : state.proxyConfigVerified ? "diag-good" : state.enabled ? "diag-bad" : "");

  const webRtcText = !state.webRtcShieldEnabled
    ? "OFF"
    : state.webRtcPolicy || (state.webRtcProtected ? "подтверждена" : state.enabled ? "не подтверждена" : "—");
  setDiagnostic(elements.diagWebRtc, webRtcText, !state.webRtcShieldEnabled ? "diag-warn" : state.webRtcProtected ? (state.webRtcCompatibility ? "diag-warn" : "diag-good") : state.enabled ? "diag-bad" : "");
  setDiagnostic(elements.diagWebRtcControl, state.webRtcLevelOfControl || "—", state.webRtcLevelOfControl === "controlled_by_this_extension" ? "diag-good" : state.enabled && state.webRtcShieldEnabled ? "diag-warn" : "");
  setDiagnostic(elements.diagPrediction, state.predictionDisabled ? "выключено" : state.enabled ? "не подтверждено" : "—", state.predictionDisabled ? "diag-good" : state.enabled ? "diag-warn" : "");

  const guardTime = formatTime(state.lastGuardAt);
  setDiagnostic(elements.diagLiveGuard, state.enabled ? (guardTime ? `${guardTime} · ${state.lastGuardEvent || "активен"}` : "активен") : "—", state.enabled && state.shieldState !== "compromised" ? "diag-good" : state.enabled ? "diag-bad" : "");
  setDiagnostic(elements.diagKillSwitch, state.hardKillSwitch ? (state.killSwitchEngaged ? "ENGAGED" : "armed") : "OFF", state.killSwitchEngaged ? "diag-bad" : state.hardKillSwitch ? "diag-good" : "diag-warn");
  setDiagnostic(elements.diagStrictLocal, state.strictLocal ? "STRICT" : "COMPATIBILITY", state.strictLocal ? "diag-good" : "diag-warn");
  setDiagnostic(elements.diagExitIp, observedIp || "—", (tunVerified || state.status === "verified") ? "diag-good" : observedIp ? "diag-warn" : "");
  const trustedExitIps = Array.isArray(state.trustedExitIps) ? state.trustedExitIps : [];
  const trustedIndex = observedIp ? trustedExitIps.indexOf(observedIp) : -1;
  const trustedMatch = trustedIndex >= 0;
  const trustedText = trustedExitIps.length
    ? trustedMatch
      ? `MATCH ${trustedIndex + 1}/${trustedExitIps.length} · ${observedIp}`
      : observedIp
        ? `UNTRUSTED · ${observedIp}`
        : `${trustedExitIps.length} trusted`
    : "не задан";
  setDiagnostic(elements.diagExpectedIp, trustedText, trustedExitIps.length ? (trustedMatch || !observedIp ? "diag-good" : "diag-bad") : "diag-warn");
  setDiagnostic(elements.diagSource, observedSource || "—");
  setDiagnostic(elements.diagLatency, Number.isFinite(observedLatency) ? `${Math.round(observedLatency)} ms` : "—");
  setDiagnostic(elements.diagCloudflare, [observedLocation, observedColo].filter(Boolean).join(" / ") || "—");
  setDiagnostic(elements.diagFailures, String(state.consecutiveFailures || 0), state.consecutiveFailures ? "diag-warn" : "diag-good");

  const selfTestLabel = state.lastSelfTestStatus === "passed" ? "PROTECTED" : state.lastSelfTestStatus === "failed" ? "FAILED" : state.lastSelfTestStatus ? "CHECK" : "—";
  const selfTest = state.lastSelfTestAt
    ? `${selfTestLabel} · ext ICE ${state.lastSelfTestIceCandidates || 0} · local ${state.lastSelfTestLocalRawCandidates || 0} · public ${state.lastSelfTestPublicRawCandidates || 0} · UDP ${state.lastSelfTestHostUdpCandidates || 0}`
    : "—";
  setDiagnostic(elements.diagSelfTest, selfTest, state.lastSelfTestStatus === "passed" ? "diag-good" : state.lastSelfTestStatus === "failed" ? "diag-bad" : state.lastSelfTestStatus ? "diag-warn" : "");

  const timezone = getTimezoneFingerprint(state);
  setDiagnostic(elements.diagBrowserTz, `${timezone.browserTz} · ${formatUtcOffset(timezone.browserOffset)}`);
  setDiagnostic(elements.diagTimezoneMatch, timezone.text, timezone.status === "match" ? "diag-good" : timezone.status === "mismatch" ? "diag-warn" : "");

  const proxyErrorTime = formatTime(state.lastProxyErrorAt);
  const proxyError = state.lastProxyError
    ? `${state.lastProxyFatal ? "FATAL · " : ""}${state.lastProxyError}${proxyErrorTime ? ` · ${proxyErrorTime}` : ""}`
    : "—";
  setDiagnostic(elements.diagProxyError, proxyError, state.lastProxyError ? (state.lastProxyFatal ? "diag-bad" : "diag-warn") : "");
  setDiagnostic(elements.diagAudit, state.lastAuditScore !== null && state.lastAuditScore !== undefined ? `${state.lastAuditScore}/10 · ${(state.lastAuditStatus || "-").toUpperCase()}` : "не запускался", state.lastAuditStatus === "pass" ? "diag-good" : state.lastAuditStatus === "fail" ? "diag-bad" : state.lastAuditStatus ? "diag-warn" : "");
  setDiagnostic(elements.diagCadence, tunMonitor ? "TUN monitor · 30 sec" : state.status === "verified" ? "adaptive · 3 min" : state.enabled ? "recovery · 30 sec" : "off", tunVerified || state.status === "verified" ? "diag-good" : state.enabled || tunMonitor ? "diag-warn" : "");
  const managerText = state.managerBridgeConnected ? `CONNECTED${state.managerVersion ? ` · v${state.managerVersion}` : ""}${state.managerEngine ? ` · ${state.managerEngine}` : ""}` : (state.managerBridgeStatus || "OFF").toUpperCase();
  setDiagnostic(elements.diagManager, managerText, state.managerBridgeConnected ? "diag-good" : state.managerBridgeEnabled ? "diag-warn" : "");
  const managerRoute = [state.managerProfile, state.managerExpectedExitIp ? `expected ${state.managerExpectedExitIp}` : null, state.managerCoherence && state.managerCoherence !== "unknown" ? state.managerCoherence.toUpperCase() : null].filter(Boolean).join(" · ") || "—";
  setDiagnostic(elements.diagManagerRoute, managerRoute, state.managerCoherence === "match" ? "diag-good" : state.managerCoherence === "mismatch" ? "diag-warn" : "");
}

function renderAudit(state) {
  const score = state.lastAuditScore;
  elements.auditScore.textContent = score === null || score === undefined ? "не запускался" : `${score}/10 ${(state.lastAuditStatus || "").toUpperCase()}`;
  elements.auditScore.className = `audit-score${state.lastAuditStatus ? ` ${state.lastAuditStatus}` : ""}`;
  const test = state.trustedExitTest || {};
  const trusted = Array.isArray(state.trustedExitIps) ? state.trustedExitIps : [];
  const seen = Array.isArray(test.seenIps) ? test.seenIps : [];
  if (test.active) {
    elements.trustedExitTestStatus.textContent = `Trusted Exit Test: ${seen.length}/${trusted.length} подтверждено. Переключите NekoBox на другой VPS.`;
    elements.trustedExitTestButton.textContent = "Отменить тест";
  }
  else if (test.completedAt && trusted.length && trusted.every(ip => seen.includes(ip))) {
    elements.trustedExitTestStatus.textContent = `Trusted Exit Test: COMPLETED ${trusted.length}/${trusted.length} PASSED.`;
    const coolingDown = trustedTestRestartCoolingDown(state);
    elements.trustedExitTestButton.textContent = coolingDown ? "2/2 PASSED" : "Начать заново";
    elements.trustedExitTestButton.disabled = busy || !state.enabled || coolingDown;

    if (trustedTestCooldownTimer) clearTimeout(trustedTestCooldownTimer);
    if (coolingDown) {
      const delay = Math.max(50, TRUSTED_TEST_RESTART_COOLDOWN_MS - (Date.now() - Number(test.completedAt)));
      trustedTestCooldownTimer = setTimeout(() => {
        trustedTestCooldownTimer = null;
        if (!busy) renderAudit(currentState);
      }, delay);
    }
  }
  else {
    elements.trustedExitTestStatus.textContent = "Trusted Exit Test не запущен.";
    elements.trustedExitTestButton.textContent = "Тест Trusted Exits";
  }

  const summary = state.routeStatsSummary || {};
  const lines = [];
  for (const ip of trusted) {
    const meta = "";
    const d7 = summary.days7?.[ip];
    const d30 = summary.days30?.[ip];
    if (!d7 && !d30) continue;
    lines.push(`${meta ? `${meta} · ` : ""}${ip}`);
    if (d7) lines.push(`7d: ${d7.availabilityPct ?? "-"}% · median ${d7.medianLatencyMs ?? "-"} ms · P95 ${d7.p95LatencyMs ?? "-"} ms`);
    if (d30) lines.push(`30d: ${d30.availabilityPct ?? "-"}% · median ${d30.medianLatencyMs ?? "-"} ms · P95 ${d30.p95LatencyMs ?? "-"} ms`);
  }
  elements.routeStats.textContent = lines.length ? lines.join("\n") : "Статистика появится после нескольких health-check.";
  const audits = Array.isArray(state.auditHistory) ? state.auditHistory.slice(-7).reverse() : [];
  elements.auditHistoryText.textContent = audits.length
    ? `Audit history: ${audits.map(item => `${item.day} ${item.score}/10 ${String(item.status || "").toUpperCase()}`).join(" · ")}`
    : "История Daily Audit пока пуста.";
}

function renderEventLog(state) {
  elements.eventLogList.replaceChildren();
  const events = Array.isArray(state.eventLog) ? [...state.eventLog].slice(-30).reverse() : [];
  if (events.length === 0) {
    const empty = document.createElement("div");
    empty.className = "log-empty";
    empty.textContent = "Событий пока нет.";
    elements.eventLogList.appendChild(empty);
    return;
  }

  for (const event of events) {
    const row = document.createElement("div");
    row.className = "log-item";
    const time = document.createElement("span");
    time.className = "log-time";
    time.textContent = formatTime(event.at) || "--:--:--";
    const text = document.createElement("span");
    text.textContent = event.message || "Событие";
    row.append(time, text);
    elements.eventLogList.appendChild(row);
  }
}

function render(state, options = {}) {
  const { syncControls = false } = options;
  currentState = { ...DEFAULT_STATE, ...state };
  if (syncControls) syncProfileControls(currentState);

  const presentation = getStatusPresentation(currentState);
  const tunMonitorActive = currentState.managerBridgeConnected && currentState.managerMode === "tun" && !currentState.enabled;
  const effectiveStatus = tunMonitorActive
    ? (currentState.managerMonitorStatus === "verified" ? "verified" : currentState.managerMonitorStatus === "checking" ? "checking" : currentState.managerMonitorStatus === "mismatch" ? "wrong_exit" : currentState.managerMonitorStatus === "degraded" ? "degraded" : currentState.managerMonitorStatus === "unreachable" ? "unreachable" : "off")
    : currentState.status;
  elements.statusCard.dataset.status = effectiveStatus;
  elements.statusTitle.textContent = presentation.title;
  elements.statusDetail.textContent = presentation.detail;

  const routeParts = [];
  if (currentState.exitIp) routeParts.push(`IP ${currentState.exitIp}`);
  if (Number.isFinite(currentState.latencyMs)) routeParts.push(`${Math.round(currentState.latencyMs)} ms`);
  if (currentState.traceLocation) routeParts.push(currentState.traceLocation);
  if (currentState.traceColo) routeParts.push(currentState.traceColo);
  if (currentState.killSwitchEngaged) routeParts.push("WEB BLOCKED");
  const tunMonitor = currentState.managerBridgeConnected && currentState.managerMode === "tun";
  if (tunMonitor && currentState.managerMonitorObservedExitIp) routeParts.push(`TUN IP ${currentState.managerMonitorObservedExitIp}`);
  elements.routeInfo.textContent = routeParts.join(" · ") || (tunMonitor ? "System TUN + Privacy Monitor active" : currentState.enabled && currentState.protectionsApplied ? "SOCKS5 + Privacy Shield active" : "");

  elements.lastCheck.textContent = tunMonitor ? formatLastCheck(currentState.managerMonitorLastCheckAt) : formatLastCheck(currentState.lastCheckAt);
  const tunHealthy = tunMonitor && currentState.managerMonitorStatus === "verified" && currentState.shieldState !== "compromised";
  elements.appIcon.src = (currentState.status === "verified" && currentState.shieldState !== "compromised") || tunHealthy ? "on.png" : "off.png";
  elements.toggleButton.textContent = currentState.managerTransition === "to_tun" ? "Switching to TUN…" : currentState.managerTransition === "to_local" ? "Switching to Local…" : tunMonitor ? "TUN Monitor · SOCKS5 не нужен" : currentState.enabled ? "Выключить прокси" : "Включить прокси";
  elements.toggleButton.classList.toggle("is-enabled", currentState.enabled || tunMonitor);
  elements.toggleButton.setAttribute("aria-pressed", String(currentState.enabled));
  elements.checkButton.disabled = busy || !canCheckNow(currentState);

  renderShield(currentState);
  renderManager(currentState);
  renderDiagnostics(currentState);
  renderAudit(currentState);
  renderEventLog(currentState);
  setBusy(busy);
}

async function runAction(action, payload = {}, options = {}) {
  const { syncControls = false } = options;
  clearError();
  setBusy(true);
  try {
    const state = await sendMessage(action, payload);
    render(state, { syncControls });
    return state;
  }
  catch (error) {
    if (error?.state) render(error.state, { syncControls });
    showError(error instanceof Error ? error.message : String(error));
    throw error;
  }
  finally {
    setBusy(false);
  }
}

async function applyProfile() {
  const profile = getSelectedProfile();
  await runAction("setProfile", { profile }, { syncControls: true });
}

function buildDiagnosticsText(state) {
  const eventLines = (Array.isArray(state.eventLog) ? state.eventLog.slice(-15) : [])
    .map(event => `${new Date(event.at).toISOString()} [${event.type}] ${event.message}`);
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  const tunVerified = tunMonitor && state.managerMonitorStatus === "verified";
  const observedIp = tunMonitor ? state.managerMonitorObservedExitIp : state.exitIp;
  const observedSource = tunMonitor ? state.managerMonitorSource : state.lastCheckSource;
  const observedLatency = tunMonitor ? state.managerMonitorLatencyMs : state.latencyMs;
  const observedLocation = tunMonitor ? state.managerMonitorTraceLocation : state.traceLocation;
  const observedColo = tunMonitor ? state.managerMonitorTraceColo : state.traceColo;
  const displayStatus = tunMonitor ? `tun_${state.managerMonitorStatus || "idle"}` : state.status;
  const displayProxyControl = tunMonitor ? "system_tun / browser_proxy_off" : (state.proxyControl || "-");
  return [
    "Genia Proxy Switcher Direct 5.6.0 Stable",
    `Extension ID: ${chrome.runtime.id}`,
    `Status: ${displayStatus}`,
    `Enabled: ${state.enabled}`,
    `SOCKS5: 127.0.0.1:${state.port}`,
    `Profile: ${state.profileName}`,
    `Strict local: ${state.strictLocal}`,
    `Proxy control: ${displayProxyControl}`,
    `Proxy config verified: ${tunMonitor ? "browser proxy OFF (TUN)" : state.proxyConfigVerified}`,
    `WebRTC Shield enabled: ${state.webRtcShieldEnabled}`,
    `WebRTC compatibility: ${state.webRtcCompatibility}`,
    `WebRTC policy: ${state.webRtcPolicy || "-"}`,
    `WebRTC control: ${state.webRtcLevelOfControl || "-"}`,
    `Prediction disabled: ${state.predictionDisabled}`,
    `Hard Kill Switch armed: ${state.hardKillSwitch}`,
    `Hard Kill Switch engaged: ${state.killSwitchEngaged}`,
    `Kill reason: ${state.killSwitchReason || "-"}`,
    `Observed exit IP: ${observedIp || "-"}`,
    `Trusted exit IPs: ${(Array.isArray(state.trustedExitIps) && state.trustedExitIps.length) ? state.trustedExitIps.join(", ") : "-"}`,
    `Trusted exit match: ${(() => { const ips = Array.isArray(state.trustedExitIps) ? state.trustedExitIps : []; const i = observedIp ? ips.indexOf(observedIp) : -1; return i >= 0 ? `MATCH ${i + 1}/${ips.length}` : observedIp ? "UNTRUSTED" : "-"; })()}`,
    `Source: ${observedSource || "-"}`,
    `Latency: ${Number.isFinite(observedLatency) ? `${Math.round(observedLatency)} ms` : "-"}`,
    `Cloudflare loc/colo: ${[observedLocation, observedColo].filter(Boolean).join("/") || "-"}`,
    `Browser timezone: ${getBrowserTimeZone()} / ${formatUtcOffset(-new Date().getTimezoneOffset())}`,
    `Timezone fingerprint: ${getTimezoneFingerprint(state).text}${getTimezoneFingerprint(state).expectedTz ? ` / expected ${getTimezoneFingerprint(state).expectedTz}` : ""}`,
    `Failures: ${state.consecutiveFailures || 0}`,
    `Last check: ${state.lastCheckAt ? new Date(state.lastCheckAt).toISOString() : "-"}`,
    `Last guard: ${state.lastGuardAt ? new Date(state.lastGuardAt).toISOString() : "-"}`,
    `Last guard event: ${state.lastGuardEvent || "-"}`,
    `WebRTC Shield Check: ${state.lastSelfTestStatus || "-"} / extension ICE ${state.lastSelfTestIceCandidates || 0}`,
    `Extension-origin ICE (informational): mDNS ${state.lastSelfTestMdnsCandidates || 0} / raw-local ${state.lastSelfTestLocalRawCandidates || 0} / raw-public ${state.lastSelfTestPublicRawCandidates || 0} / safe ${state.lastSelfTestSafeSpecialCandidates || 0} / host-UDP ${state.lastSelfTestHostUdpCandidates || 0} / host-TCP ${state.lastSelfTestHostTcpCandidates || 0} / unknown ${state.lastSelfTestUnknownCandidates || 0}`,
    `Shield Check summary: ${state.lastSelfTestSummary || "-"}`,
    `Daily Audit: ${state.lastAuditScore ?? "-"}/10 / ${state.lastAuditStatus || "-"} / ${state.lastAuditSummary || "-"}`,
    `Trusted Exit Test: ${state.trustedExitTest?.active ? "ACTIVE" : state.trustedExitTest?.completedAt ? "COMPLETED" : "-"} / seen ${(state.trustedExitTest?.seenIps || []).join(", ") || "-"}`,
    `Health cadence: ${state.status === "verified" ? "adaptive 3 min" : state.enabled ? "recovery 30 sec" : state.managerBridgeConnected && state.managerMode === "tun" ? "TUN monitor 30 sec" : "off"}`,
    `Direct Bridge: ${state.managerBridgeStatus || "off"} / connected ${Boolean(state.managerBridgeConnected)}`,
    `Manager version/engine: ${state.managerVersion || "-"} / ${state.managerEngine || "-"}`,
    `Direct bridge transport: ${state.managerBridgeHostVersion || "-"} / ${state.managerBridgeGoVersion || "-"}`,
    `Manager profile: ${state.managerProfile || "-"}`,
    `Manager local proxy: ${state.managerLocalProxy || "-"}`,
    `Manager mode: ${state.managerMode || "-"}`,
    `Manager transition: ${state.managerTransition || "-"}`,
    `Manager endpoint: ${state.managerEndpoint || "-"}`,
    `Manager verified exit: ${state.managerVerifiedExitIp || state.managerExpectedExitIp || "-"}`,
    `Manager verified at: ${state.managerVerifiedExitAt ? new Date(state.managerVerifiedExitAt).toISOString() : "-"}`,
    `Manager TUN monitor: ${state.managerMonitorStatus || "off"} / observed ${state.managerMonitorObservedExitIp || "-"} / ${Number.isFinite(state.managerMonitorLatencyMs) ? `${Math.round(state.managerMonitorLatencyMs)} ms` : "-"}`,
    `Manager uptime: ${state.managerUptimeSeconds ? `${Math.floor(state.managerUptimeSeconds)} s` : "-"}`,
    `Manager route coherence: ${state.managerCoherence || "unknown"}`,
    `Direct bridge last seen: ${state.managerLastSeenAt ? new Date(state.managerLastSeenAt).toISOString() : "-"}`,
    `Direct bridge last error: ${state.managerLastError || "-"}`,
    `Last error: ${state.lastError || "-"}`,
    `Last proxy error: ${state.lastProxyError || "-"}`,
    `Proxy fatal: ${state.lastProxyFatal}`,
    "",
    "Security Event Log:",
    ...(eventLines.length ? eventLines : ["-"])
  ].join("\n");
}

async function copyText(text) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }
  const textarea = document.createElement("textarea");
  textarea.value = text;
  textarea.setAttribute("readonly", "");
  textarea.style.position = "fixed";
  textarea.style.opacity = "0";
  document.body.appendChild(textarea);
  textarea.select();
  const copied = document.execCommand("copy");
  textarea.remove();
  if (!copied) throw new Error("Chrome не разрешил копирование в буфер обмена.");
}

function parseIpv4(value) {
  const parts = String(value || "").split(".");
  if (parts.length !== 4 || !parts.every(part => /^\d{1,3}$/.test(part))) return null;
  const nums = parts.map(Number);
  return nums.every(n => n >= 0 && n <= 255) ? nums : null;
}

function classifyIceAddress(value) {
  let address = String(value || "").trim().toLowerCase();
  if (!address) return "unknown";
  if (address.endsWith(".local")) return "mdns";
  if (address.startsWith("[") && address.endsWith("]")) address = address.slice(1, -1);
  address = address.split("%")[0];

  const v4 = parseIpv4(address);
  if (v4) {
    const [a, b, c] = v4;
    if (a === 0 && b === 0 && c === 0 && v4[3] === 0) return "safe";
    if (a === 127) return "safe";
    if (a === 10 || (a === 172 && b >= 16 && b <= 31) || (a === 192 && b === 168)) return "local";
    if (a === 169 && b === 254) return "local";
    if (a === 100 && b >= 64 && b <= 127) return "local";
    if (a >= 224 || (a === 198 && (b === 18 || b === 19)) || (a === 192 && b === 0 && c === 2) || (a === 198 && b === 51 && c === 100) || (a === 203 && b === 0 && c === 113)) return "safe";
    return "public";
  }

  if (address.includes(":")) {
    if (address === "::" || address === "::1") return "safe";
    const mapped = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/.exec(address);
    if (mapped) return classifyIceAddress(mapped[1]);
    const firstHex = parseInt(address.split(":")[0] || "0", 16);
    if (Number.isFinite(firstHex)) {
      if ((firstHex & 0xffc0) === 0xfe80) return "local"; // fe80::/10 link-local
      if ((firstHex & 0xfe00) === 0xfc00) return "local"; // fc00::/7 ULA
      if ((firstHex & 0xff00) === 0xff00) return "safe";  // multicast
    }
    if (address.startsWith("2001:db8:")) return "safe";
    return "public";
  }

  return "unknown";
}

async function runLocalIceSelfTest() {
  const empty = {
    iceCandidates: 0,
    mdnsCandidates: 0,
    localRawCandidates: 0,
    publicRawCandidates: 0,
    safeSpecialCandidates: 0,
    hostUdpCandidates: 0,
    hostTcpCandidates: 0,
    unknownCandidates: 0
  };
  if (typeof RTCPeerConnection !== "function") return empty;

  const pc = new RTCPeerConnection({ iceServers: [] });
  const seen = new Set();
  const result = { ...empty };

  try {
    pc.createDataChannel("genia-privacy-self-test");
    const gathering = new Promise(resolve => {
      const timer = setTimeout(resolve, 2500);
      pc.addEventListener("icecandidate", event => {
        if (!event.candidate) {
          clearTimeout(timer);
          resolve();
          return;
        }

        const text = event.candidate.candidate || "";
        if (seen.has(text)) return;
        seen.add(text);

        const parts = text.split(/\s+/);
        const address = event.candidate.address || parts[4] || "";
        const type = event.candidate.type || (text.match(/\styp\s+(\w+)/)?.[1] || "");
        const protocol = String(event.candidate.protocol || parts[2] || "").toLowerCase();
        const classification = classifyIceAddress(address);

        if (classification === "mdns") result.mdnsCandidates += 1;
        else if (classification === "local") result.localRawCandidates += 1;
        else if (classification === "public") result.publicRawCandidates += 1;
        else if (classification === "safe") result.safeSpecialCandidates += 1;
        else result.unknownCandidates += 1;

        if (type === "host" && protocol === "udp") result.hostUdpCandidates += 1;
        if (type === "host" && protocol === "tcp") result.hostTcpCandidates += 1;
      });
    });

    const offer = await pc.createOffer();
    await pc.setLocalDescription(offer);
    await gathering;
    result.iceCandidates = seen.size;
    return result;
  }
  finally {
    pc.close();
  }
}

elements.toggleButton.addEventListener("click", () => {
  let profile;
  try { profile = getSelectedProfile(); }
  catch (error) { showError(error instanceof Error ? error.message : String(error)); return; }
  runAction("toggle", { profile }, { syncControls: true }).catch(() => {});
});

elements.checkButton.addEventListener("click", () => runAction("checkNow").catch(() => {}));

elements.profileSelect.addEventListener("change", () => {
  const isCustom = elements.profileSelect.value === "custom";
  elements.customRow.hidden = !isCustom;
  clearError();
  if (isCustom) { elements.customPort.focus(); elements.customPort.select(); }
});

elements.webRtcShield.addEventListener("change", syncCompatibilityControl);
elements.applyPortButton.addEventListener("click", () => applyProfile().catch(() => {}));
elements.applySettingsButton.addEventListener("click", () => applyProfile().catch(() => {}));

elements.customPort.addEventListener("keydown", event => {
  if (event.key === "Enter") { event.preventDefault(); applyProfile().catch(() => {}); }
});

elements.expectedIp.addEventListener("keydown", event => {
  if (event.key === "Enter") { event.preventDefault(); applyProfile().catch(() => {}); }
});

elements.selfTestButton.addEventListener("click", async () => {
  clearError();
  setBusy(true);
  const original = elements.selfTestButton.textContent;
  elements.selfTestButton.textContent = "Тестируем…";
  try {
    const result = await runLocalIceSelfTest();
    const state = await sendMessage("recordSelfTest", { result });
    render(state);
  }
  catch (error) {
    showError(error instanceof Error ? error.message : String(error));
  }
  finally {
    elements.selfTestButton.textContent = original;
    setBusy(false);
  }
});

elements.copyDiagnosticsButton.addEventListener("click", async () => {
  clearError();
  try {
    await copyText(buildDiagnosticsText(currentState));
    const original = elements.copyDiagnosticsButton.textContent;
    elements.copyDiagnosticsButton.textContent = "Скопировано";
    setTimeout(() => { elements.copyDiagnosticsButton.textContent = original; }, 1200);
  }
  catch (error) { showError(error instanceof Error ? error.message : String(error)); }
});

elements.dailyAuditButton.addEventListener("click", async () => {
  clearError();
  setBusy(true);
  const original = elements.dailyAuditButton.textContent;
  elements.dailyAuditButton.textContent = "Проверяем…";
  try {
    // A recent successful Shield Check is still valid for Daily Audit.
    // Avoid re-running extension-origin ICE just because the audit button was pressed again.
    if (!hasFreshPassedSelfTest(currentState)) {
      const ice = await runLocalIceSelfTest();
      currentState = await sendMessage("recordSelfTest", { result: ice });
      render(currentState);
    }
    const state = await sendMessage("runDailyAudit");
    render(state);
  }
  catch (error) { showError(error instanceof Error ? error.message : String(error)); }
  finally { elements.dailyAuditButton.textContent = original; setBusy(false); }
});

elements.trustedExitTestButton.addEventListener("click", async () => {
  clearError();
  const action = currentState.trustedExitTest?.active ? "cancelTrustedExitTest" : "startTrustedExitTest";
  runAction(action).catch(() => {});
});


elements.managerConnectButton.addEventListener("click", async () => {
  clearError();
  if (currentState.managerBridgeEnabled) {
    runAction("managerDisconnect").catch(() => {});
    return;
  }
  try {
    setBusy(true);
    const state = await sendMessage("managerConnect");
    render(state);
  }
  catch (error) { showError(error instanceof Error ? error.message : String(error)); }
  finally { setBusy(false); }
});

elements.managerRefreshButton.addEventListener("click", () => runAction("managerRefresh").catch(() => {}));

elements.clearLogButton.addEventListener("click", () => runAction("clearEventLog").catch(() => {}));

chrome.storage.onChanged.addListener((_changes, areaName) => {
  if (areaName !== "local" || busy) return;
  sendMessage("getState")
    .then(state => render(state))
    .catch(error => showError(error instanceof Error ? error.message : String(error)));
});

async function initializePopup() {
  clearError();
  const state = await sendMessage("getState");
  render(state, { syncControls: true });
  setBusy(false);
  // FIX2: opening the popup is read-only for route status.
  // Fresh route probes are driven by the background worker/alarms or the explicit Check button.
  // This prevents a harmless popup click from flashing the action badge orange.
}

initializePopup().catch(error => {
  showError(error instanceof Error ? error.message : String(error));
  setBusy(false);
});
