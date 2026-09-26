// Genia Proxy Switcher Direct 5.6.0 Stable — validated Direct Bridge/TUN fail-closed build
// Local SOCKS5 controller with verified exit IP, automatic WebRTC Shield,
// live protection guard, event diagnostics and browser-level hard kill switch.
// No remote code, analytics or DIRECT fallback.

"use strict";

const CONFIG = Object.freeze({
  PROXY_HOST: "127.0.0.1",
  DEFAULT_PORT: 2080,
  DEFAULT_PROFILE: "GeniaProxy / NekoBox / Hysteria / VLESS",
  DEFAULT_TRUSTED_EXIT_IPS: Object.freeze(["83.147.232.178", "46.8.182.247"]),
  TRUSTED_EXIT_META: Object.freeze({
    "83.147.232.178": Object.freeze({ name: "Netherlands", expectedCountry: "NL", expectedTimeZone: "Europe/Amsterdam" }),
    "46.8.182.247": Object.freeze({ name: "Germany", expectedCountry: "DE", expectedTimeZone: "Europe/Berlin" })
  }),
  TRACE_URL: "https://1.1.1.1/cdn-cgi/trace",
  FALLBACK_URL: "https://www.gstatic.com/generate_204",
  HEALTH_TIMEOUT_MS: 7000,
  HEALTH_VERIFIED_MINUTES: 3,
  HEALTH_RECOVERY_MINUTES: 0.5,
  HEALTH_ALARM: "genia_proxy_health_v5_5_6",
  LEGACY_HEALTH_ALARMS: Object.freeze([
    "genia_proxy_health_v5_5_5",
    "genia_proxy_health_v5_3_6",
    "genia_proxy_health_v5_3_5",
    "genia_proxy_health_v5_3_4",
    "genia_proxy_health_v5_3_3",
    "genia_proxy_health_v5_3_2",
    "genia_proxy_health_v5_3_1",
    "genia_proxy_health_v5_3",
    "genia_proxy_health_v5_2",
    "genia_proxy_health_v5_1",
    "proxy_health_check",
    "deviantart_proxy_health"
  ]),
  NOTIFICATION_ID: "genia-proxy-status",
  PROXY_ERROR_RECHECK_MS: 1200,
  POST_VERIFY_TRANSIENT_MS: 2200,
  PROXY_ERROR_LOG_COALESCE_MS: 30 * 1000,
  LIVE_GUARD_DEBOUNCE_MS: 350,
  RECENT_CHECK_MS: 15000,
  EVENT_LOG_LIMIT: 50,
  AUDIT_HISTORY_LIMIT: 30,
  ROUTE_DAYS_LIMIT: 30,
  ROUTE_LATENCY_SAMPLES_PER_DAY: 96,
  TRUSTED_EXIT_TEST_MAX_MS: 30 * 60 * 1000,
  TRUSTED_EXIT_TEST_RESTART_COOLDOWN_MS: 15 * 1000,
  SELF_TEST_CACHE_MS: 60 * 1000,
  NOTIFICATION_COOLDOWN_MS: 12000,
  KILL_RULE_IDS: Object.freeze([53101, 53102, 53103, 53104, 53105]),
  MANAGER_DIRECT_URL: "http://127.0.0.1:47831/v1/status",
  MANAGER_FETCH_TIMEOUT_MS: 2200,
  MANAGER_PROTOCOL: 1,
  MANAGER_POLL_TRANSITION_MS: 350,
  MANAGER_POLL_DISCONNECTED_MS: 1500,
  MANAGER_POLL_INACTIVE_MS: 3000,
  MANAGER_POLL_STABLE_MS: 5000,
  MANAGER_TRANSITION_FAST_WINDOW_MS: 10 * 1000,
  MANAGER_STALE_MS: 45 * 1000,
  MANAGER_HEALTH_ALARM: "genia_manager_bridge_health_v5_5_6",
  MANAGER_HEALTH_MINUTES: 0.5,
  MANAGER_TUN_MONITOR_MIN_MS: 30 * 1000,
  // Stable adaptive warm-up: keep the fail-closed loop active long enough for slower first TUN starts.
  // Delays are progressive; total retry wait is ~25 s plus probe time.
  MANAGER_TUN_TRANSITION_RETRY_DELAYS_MS: [1200, 1500, 1800, 2200, 2700, 3200, 3800, 4300, 4800],
  MANAGER_VERIFIED_FRESHNESS_GRACE_MS: 2500

});

const STATUS = Object.freeze({
  OFF: "off",
  CHECKING: "checking",
  VERIFIED: "verified",
  DEGRADED: "degraded",
  WRONG_EXIT: "wrong_exit",
  UNREACHABLE: "unreachable",
  BLOCKED: "blocked",
  ERROR: "error"
});

const VALID_STATUSES = new Set(Object.values(STATUS));

const DEFAULT_STATE = Object.freeze({
  enabled: false,
  status: STATUS.OFF,
  port: CONFIG.DEFAULT_PORT,
  profileName: CONFIG.DEFAULT_PROFILE,
  strictLocal: true,
  trustedExitIps: [...CONFIG.DEFAULT_TRUSTED_EXIT_IPS],
  expectedExitIp: null, // legacy 5.3.4 storage migration only
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
  routeDailyStats: {},
  routeStatsSummary: {},
  trustedExitTest: { active: false, startedAt: null, completedAt: null, seenIps: [] },
  managerBridgeEnabled: false,
  managerBridgeStatus: "off",
  managerBridgeConnected: false,
  managerProtocol: null,
  managerVersion: null,
  managerBridgeHostVersion: null,
  managerBridgeGoVersion: null,
  managerProfile: null,
  managerEngine: null,
  managerMode: null,
  managerLocalProxy: null,
  managerEndpoint: null,
  managerExpectedExitIp: null, // compatibility alias from Bridge <= RC4
  managerVerifiedExitIp: null,
  managerVerifiedExitAt: null,
  managerVerifiedExitSucceeded: null,
  managerMonitorStatus: "off",
  managerMonitorObservedExitIp: null,
  managerMonitorLastCheckAt: null,
  managerMonitorLatencyMs: null,
  managerMonitorSource: null,
  managerMonitorTraceLocation: null,
  managerMonitorTraceColo: null,
  managerUptimeSeconds: null,
  managerLastSeenAt: null,
  managerConnectAttemptAt: null,
  managerLastError: null,
  managerCoherence: "unknown",
  tunRestoreLocalProxy: false,
  managerTransition: null,
  managerTransitionStartedAt: null,
  lastStateTransitionAt: null
});

let operationQueue = Promise.resolve();
let proxyErrorTimer = null;
let liveGuardTimer = null;
let actionRefreshTimer = null;
let lastProxyErrorLogKey = null;
let lastProxyErrorLogAt = 0;
let suppressedProxyErrorLogs = 0;
let notificationState = new Map();
let operationGeneration = 0;
let managerEverConnected = false;

// Stable Direct Bridge lane. Bridge polling never waits behind operationQueue.
// Only authoritative browser route changes are coalesced back into operationQueue.
let managerBridgePollTimer = null;
let managerBridgeRefreshInFlight = null;
let managerBridgeRefreshPending = false;
let managerBridgeEpoch = 0;
let managerBridgeAbortController = null;
let managerBridgeFastPollUntil = 0;
// Changes whenever the authoritative Manager route/session changes. TUN probes must
// never release the guard if they started against an older Manager session.
let managerRouteGeneration = 0;
let managerRouteReconcilePromise = null;
let managerRouteReconcilePending = false;
let managerRouteReconcileHints = { routeChanged: false, modeChanged: false, tunMonitorDue: false };
// Stable: TUN route verification has its own single-in-flight lane.
// It must never hold operationQueue while waiting for system-TUN warm-up/retry probes.
let tunMonitorInFlight = null;
let tunMonitorPending = false;
let tunMonitorHints = { force: false, transition: false };
let pendingProxyError = null;
let proxyErrorWorkPromise = null;
let eventLogQueue = Promise.resolve();


function enqueue(operation) {
  const result = operationQueue.then(operation, operation);
  operationQueue = result.catch(error => {
    console.error("Genia Proxy Switcher operation error:", error);
  });
  return result;
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, Math.max(0, Number(ms) || 0)));
}

function normalizePort(value) {
  const port = Number(value);
  return Number.isInteger(port) && port >= 1 && port <= 65535
    ? port
    : CONFIG.DEFAULT_PORT;
}

function normalizeProfileName(value) {
  if (typeof value !== "string") {
    return CONFIG.DEFAULT_PROFILE;
  }
  const profileName = value.trim().slice(0, 80);
  return profileName || CONFIG.DEFAULT_PROFILE;
}

function normalizeNullableNumber(value) {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

function canonicalizeIp(value) {
  if (typeof value !== "string") {
    return null;
  }

  const candidate = value.trim();
  if (!candidate || candidate.length > 80) {
    return null;
  }

  const v4Parts = candidate.split(".");
  if (v4Parts.length === 4 && v4Parts.every(part => /^\d{1,3}$/.test(part))) {
    const numbers = v4Parts.map(Number);
    if (numbers.every(number => number >= 0 && number <= 255)) {
      return numbers.join(".");
    }
    return null;
  }

  if (candidate.includes(":")) {
    try {
      const parsed = new URL(`http://[${candidate}]/`);
      const hostname = parsed.hostname.replace(/^\[/, "").replace(/\]$/, "");
      return hostname.toLowerCase();
    }
    catch (_error) {
      return null;
    }
  }

  return null;
}

function normalizeExpectedExitIp(value, throwOnInvalid = false) {
  if (value === null || value === undefined || String(value).trim() === "") {
    return null;
  }

  const normalized = canonicalizeIp(String(value));
  if (!normalized && throwOnInvalid) {
    throw new Error("Ожидаемый выходной IP должен быть корректным IPv4 или IPv6 адресом.");
  }
  return normalized;
}

function normalizeTrustedExitIps(value, throwOnInvalid = false) {
  const source = Array.isArray(value)
    ? value
    : typeof value === "string"
      ? value.split(/[\s,;]+/)
      : [];

  const result = [];
  const invalid = [];
  for (const item of source) {
    const text = String(item ?? "").trim();
    if (!text) continue;
    const ip = canonicalizeIp(text);
    if (!ip) {
      invalid.push(text);
      continue;
    }
    if (!result.includes(ip)) result.push(ip);
    if (result.length >= 8) break;
  }

  if (throwOnInvalid && invalid.length) {
    throw new Error(`Некорректный Trusted Exit IP: ${invalid[0]}. Укажите IPv4/IPv6 через запятую.`);
  }
  return result;
}

function normalizeNullableString(value, maxLength = 160) {
  if (typeof value !== "string") {
    return null;
  }
  const text = value.trim();
  return text ? text.slice(0, maxLength) : null;
}

function normalizeShieldState(value) {
  return ["off", "protected", "compatibility", "compromised"].includes(value)
    ? value
    : "off";
}

function normalizeSelfTestStatus(value) {
  return ["passed", "warning", "failed"].includes(value) ? value : null;
}

function normalizeAuditHistory(value) {
  if (!Array.isArray(value)) return [];
  return value.slice(-CONFIG.AUDIT_HISTORY_LIMIT).map(item => ({
    at: normalizeNullableNumber(item?.at) || Date.now(),
    day: normalizeNullableString(item?.day, 20) || new Date().toISOString().slice(0, 10),
    status: ["pass", "warn", "fail"].includes(item?.status) ? item.status : "warn",
    score: Math.max(0, Math.min(10, Math.floor(Number(item?.score) || 0))),
    exitIp: canonicalizeIp(item?.exitIp),
    trustedMatch: Boolean(item?.trustedMatch),
    latencyMs: normalizeNullableNumber(item?.latencyMs),
    failures: Math.max(0, Math.floor(Number(item?.failures) || 0)),
    webRtc: Boolean(item?.webRtc),
    killSwitch: Boolean(item?.killSwitch),
    timezoneMatch: item?.timezoneMatch === true ? true : item?.timezoneMatch === false ? false : null,
    summary: normalizeNullableString(item?.summary, 500)
  }));
}

function normalizeTrustedExitTest(value) {
  const seen = normalizeTrustedExitIps(value?.seenIps || []);
  return {
    active: Boolean(value?.active),
    startedAt: normalizeNullableNumber(value?.startedAt),
    completedAt: normalizeNullableNumber(value?.completedAt),
    seenIps: seen
  };
}

function normalizeRouteDailyStats(value) {
  if (!value || typeof value !== "object" || Array.isArray(value)) return {};
  const days = Object.keys(value).sort().slice(-CONFIG.ROUTE_DAYS_LIMIT);
  const result = {};
  for (const day of days) {
    const byIp = value[day];
    if (!byIp || typeof byIp !== "object") continue;
    result[day] = {};
    for (const [ipRaw, stats] of Object.entries(byIp)) {
      const ip = canonicalizeIp(ipRaw);
      if (!ip || !stats || typeof stats !== "object") continue;
      const samples = Array.isArray(stats.latencies)
        ? stats.latencies.map(Number).filter(Number.isFinite).slice(-CONFIG.ROUTE_LATENCY_SAMPLES_PER_DAY)
        : [];
      result[day][ip] = {
        checks: Math.max(0, Math.floor(Number(stats.checks) || 0)),
        verified: Math.max(0, Math.floor(Number(stats.verified) || 0)),
        failures: Math.max(0, Math.floor(Number(stats.failures) || 0)),
        latencies: samples
      };
    }
  }
  return result;
}

function normalizeEventLog(value) {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.slice(-CONFIG.EVENT_LOG_LIMIT).map(item => ({
    at: normalizeNullableNumber(item?.at) || Date.now(),
    type: normalizeNullableString(item?.type, 30) || "info",
    message: normalizeNullableString(item?.message, 300) || "Событие"
  }));
}

function normalizeState(rawState) {
  const enabled = Boolean(rawState.enabled);
  const legacyStatus = rawState.status === "connected" ? STATUS.VERIFIED : rawState.status;
  let status = VALID_STATUSES.has(legacyStatus)
    ? legacyStatus
    : enabled
      ? STATUS.CHECKING
      : STATUS.OFF;

  if (!enabled) {
    status = STATUS.OFF;
  }

  return {
    enabled,
    status,
    port: normalizePort(rawState.port),
    profileName: normalizeProfileName(rawState.profileName),
    strictLocal: rawState.strictLocal === undefined ? true : Boolean(rawState.strictLocal),
    trustedExitIps: normalizeTrustedExitIps(
      rawState.trustedExitIps?.length !== undefined
        ? rawState.trustedExitIps
        : rawState.expectedExitIp
          ? [rawState.expectedExitIp]
          : CONFIG.DEFAULT_TRUSTED_EXIT_IPS
    ),
    expectedExitIp: normalizeExpectedExitIp(rawState.expectedExitIp),
    webRtcShieldEnabled: rawState.webRtcShieldEnabled === undefined ? true : Boolean(rawState.webRtcShieldEnabled),
    webRtcCompatibility: Boolean(rawState.webRtcCompatibility),
    hardKillSwitch: rawState.hardKillSwitch === undefined ? true : Boolean(rawState.hardKillSwitch),
    killSwitchEngaged: Boolean(rawState.killSwitchEngaged),
    killSwitchReason: normalizeNullableString(rawState.killSwitchReason, 300),
    shieldState: normalizeShieldState(rawState.shieldState),
    webRtcPolicy: normalizeNullableString(rawState.webRtcPolicy, 80),
    webRtcLevelOfControl: normalizeNullableString(rawState.webRtcLevelOfControl, 80),
    predictionLevelOfControl: normalizeNullableString(rawState.predictionLevelOfControl, 80),
    consecutiveFailures: Math.max(0, Math.floor(Number(rawState.consecutiveFailures) || 0)),
    lastCheckAt: normalizeNullableNumber(rawState.lastCheckAt),
    lastError: normalizeNullableString(rawState.lastError, 700),
    exitIp: canonicalizeIp(rawState.exitIp),
    latencyMs: normalizeNullableNumber(rawState.latencyMs),
    protectionsApplied: Boolean(rawState.protectionsApplied),
    lastCheckSource: normalizeNullableString(rawState.lastCheckSource, 60),
    traceLocation: normalizeNullableString(rawState.traceLocation, 20),
    traceColo: normalizeNullableString(rawState.traceColo, 20),
    proxyControl: normalizeNullableString(rawState.proxyControl, 80),
    proxyConfigVerified: Boolean(rawState.proxyConfigVerified),
    webRtcProtected: Boolean(rawState.webRtcProtected),
    predictionDisabled: Boolean(rawState.predictionDisabled),
    lastProxyError: normalizeNullableString(rawState.lastProxyError, 500),
    lastProxyErrorAt: normalizeNullableNumber(rawState.lastProxyErrorAt),
    lastProxyFatal: Boolean(rawState.lastProxyFatal),
    lastGuardAt: normalizeNullableNumber(rawState.lastGuardAt),
    lastGuardEvent: normalizeNullableString(rawState.lastGuardEvent, 300),
    lastSelfTestAt: normalizeNullableNumber(rawState.lastSelfTestAt),
    lastSelfTestStatus: normalizeSelfTestStatus(rawState.lastSelfTestStatus),
    lastSelfTestSummary: normalizeNullableString(rawState.lastSelfTestSummary, 500),
    lastSelfTestIceCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestIceCandidates) || 0)),
    lastSelfTestRawIpExposed: Boolean(rawState.lastSelfTestRawIpExposed),
    lastSelfTestMdnsCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestMdnsCandidates) || 0)),
    lastSelfTestLocalRawCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestLocalRawCandidates) || 0)),
    lastSelfTestPublicRawCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestPublicRawCandidates) || 0)),
    lastSelfTestSafeSpecialCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestSafeSpecialCandidates) || 0)),
    lastSelfTestHostUdpCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestHostUdpCandidates) || 0)),
    lastSelfTestHostTcpCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestHostTcpCandidates) || 0)),
    lastSelfTestUnknownCandidates: Math.max(0, Math.floor(Number(rawState.lastSelfTestUnknownCandidates) || 0)),
    eventLog: normalizeEventLog(rawState.eventLog),
    auditHistory: normalizeAuditHistory(rawState.auditHistory),
    lastAuditAt: normalizeNullableNumber(rawState.lastAuditAt),
    lastAuditScore: normalizeNullableNumber(rawState.lastAuditScore),
    lastAuditStatus: ["pass", "warn", "fail"].includes(rawState.lastAuditStatus) ? rawState.lastAuditStatus : null,
    lastAuditSummary: normalizeNullableString(rawState.lastAuditSummary, 500),
    routeDailyStats: normalizeRouteDailyStats(rawState.routeDailyStats),
    routeStatsSummary: rawState.routeStatsSummary && typeof rawState.routeStatsSummary === "object" ? rawState.routeStatsSummary : {},
    trustedExitTest: normalizeTrustedExitTest(rawState.trustedExitTest),
    managerBridgeEnabled: Boolean(rawState.managerBridgeEnabled),
    managerBridgeStatus: ["off", "api_unavailable", "connecting", "connected", "unavailable", "disconnected", "stale", "error"].includes(rawState.managerBridgeStatus) ? rawState.managerBridgeStatus : "off",
    managerBridgeConnected: Boolean(rawState.managerBridgeConnected),
    managerProtocol: normalizeNullableNumber(rawState.managerProtocol),
    managerVersion: normalizeNullableString(rawState.managerVersion, 40),
    managerBridgeHostVersion: normalizeNullableString(rawState.managerBridgeHostVersion, 40),
    managerBridgeGoVersion: normalizeNullableString(rawState.managerBridgeGoVersion, 40),
    managerProfile: normalizeNullableString(rawState.managerProfile, 120),
    managerEngine: normalizeNullableString(rawState.managerEngine, 40),
    managerMode: normalizeNullableString(rawState.managerMode, 40),
    managerLocalProxy: normalizeNullableString(rawState.managerLocalProxy, 100),
    managerEndpoint: normalizeNullableString(rawState.managerEndpoint, 160),
    managerExpectedExitIp: canonicalizeIp(rawState.managerExpectedExitIp),
    managerVerifiedExitIp: canonicalizeIp(rawState.managerVerifiedExitIp),
    managerVerifiedExitAt: normalizeNullableNumber(rawState.managerVerifiedExitAt),
    managerVerifiedExitSucceeded: rawState.managerVerifiedExitSucceeded === null || rawState.managerVerifiedExitSucceeded === undefined ? null : Boolean(rawState.managerVerifiedExitSucceeded),
    managerMonitorStatus: ["off", "idle", "checking", "verified", "mismatch", "degraded", "unreachable"].includes(rawState.managerMonitorStatus) ? rawState.managerMonitorStatus : "off",
    managerMonitorObservedExitIp: canonicalizeIp(rawState.managerMonitorObservedExitIp),
    managerMonitorLastCheckAt: normalizeNullableNumber(rawState.managerMonitorLastCheckAt),
    managerMonitorLatencyMs: normalizeNullableNumber(rawState.managerMonitorLatencyMs),
    managerMonitorSource: normalizeNullableString(rawState.managerMonitorSource, 80),
    managerMonitorTraceLocation: normalizeNullableString(rawState.managerMonitorTraceLocation, 20),
    managerMonitorTraceColo: normalizeNullableString(rawState.managerMonitorTraceColo, 20),
    managerUptimeSeconds: normalizeNullableNumber(rawState.managerUptimeSeconds),
    managerLastSeenAt: normalizeNullableNumber(rawState.managerLastSeenAt),
    managerConnectAttemptAt: normalizeNullableNumber(rawState.managerConnectAttemptAt),
    managerLastError: normalizeNullableString(rawState.managerLastError, 300),
    managerCoherence: ["match", "mismatch", "unknown"].includes(rawState.managerCoherence) ? rawState.managerCoherence : "unknown",
    tunRestoreLocalProxy: Boolean(rawState.tunRestoreLocalProxy),
    managerTransition: ["to_tun", "to_local"].includes(rawState.managerTransition) ? rawState.managerTransition : null,
    managerTransitionStartedAt: normalizeNullableNumber(rawState.managerTransitionStartedAt),
    lastStateTransitionAt: normalizeNullableNumber(rawState.lastStateTransitionAt)
  };
}

function getStorage(defaults) {
  return new Promise((resolve, reject) => {
    chrome.storage.local.get(defaults, result => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve(result);
    });
  });
}

function setStorage(patch) {
  return new Promise((resolve, reject) => {
    chrome.storage.local.set(patch, () => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve();
    });
  });
}

async function hardenStorageAccess() {
  try {
    if (chrome.storage?.local?.setAccessLevel) {
      await chrome.storage.local.setAccessLevel({ accessLevel: "TRUSTED_CONTEXTS" });
    }
  }
  catch (error) {
    console.warn("Could not harden storage.local access level:", error);
  }
}

function removeStorage(keys) {
  return new Promise(resolve => {
    chrome.storage.local.remove(keys, () => {
      void chrome.runtime.lastError;
      resolve();
    });
  });
}

async function readState() {
  const stored = await getStorage(null);
  const merged = { ...DEFAULT_STATE, ...(stored || {}) };

  // Upgrade path from 5.3.4: preserve a previously pinned single Expected Exit IP.
  // If none existed, this personal build starts with both known VPS exits trusted.
  if (!Object.prototype.hasOwnProperty.call(stored || {}, "trustedExitIps")) {
    merged.trustedExitIps = stored?.expectedExitIp
      ? [stored.expectedExitIp]
      : [...CONFIG.DEFAULT_TRUSTED_EXIT_IPS];
  }

  return normalizeState(merged);
}

async function writeState(patch) {
  await setStorage(patch);
  const state = await readState();
  scheduleActionRefresh();
  return state;
}

function appendEvent(type, message) {
  const operation = async () => {
    const stored = await getStorage({ eventLog: [] });
    const eventLog = normalizeEventLog(stored.eventLog);
    eventLog.push({
      at: Date.now(),
      type: normalizeNullableString(type, 30) || "info",
      message: normalizeNullableString(message, 300) || "Событие"
    });
    await setStorage({ eventLog: eventLog.slice(-CONFIG.EVENT_LOG_LIMIT) });
  };
  const result = eventLogQueue.then(operation, operation);
  eventLogQueue = result.catch(error => console.warn("Could not append event:", error));
  return result;
}

async function clearEventLog() {
  await setStorage({ eventLog: [] });
  lastProxyErrorLogKey = null;
  lastProxyErrorLogAt = 0;
  suppressedProxyErrorLogs = 0;
  return readState();
}

function percentile(values, p) {
  if (!values.length) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const idx = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return Math.round(sorted[idx]);
}

function summarizeRouteStats(routeDailyStats, days) {
  const cutoff = new Date();
  cutoff.setHours(0, 0, 0, 0);
  cutoff.setDate(cutoff.getDate() - (days - 1));
  const map = {};
  for (const [day, byIp] of Object.entries(normalizeRouteDailyStats(routeDailyStats))) {
    const at = new Date(`${day}T00:00:00`);
    if (Number.isNaN(at.getTime()) || at < cutoff) continue;
    for (const [ip, stats] of Object.entries(byIp)) {
      const target = map[ip] ||= { checks: 0, verified: 0, failures: 0, latencies: [] };
      target.checks += stats.checks;
      target.verified += stats.verified;
      target.failures += stats.failures;
      target.latencies.push(...stats.latencies);
    }
  }
  const result = {};
  for (const [ip, stats] of Object.entries(map)) {
    result[ip] = {
      checks: stats.checks,
      verified: stats.verified,
      failures: stats.failures,
      availabilityPct: stats.checks ? Math.round((stats.verified / stats.checks) * 1000) / 10 : null,
      medianLatencyMs: percentile(stats.latencies, 50),
      p95LatencyMs: percentile(stats.latencies, 95)
    };
  }
  return result;
}

async function recordRouteSample(state, fallbackIp = null) {
  if (!state.enabled || !state.lastCheckAt) return state;
  const stored = await getStorage({ routeDailyStats: {} });
  const stats = normalizeRouteDailyStats(stored.routeDailyStats);
  const day = new Date(state.lastCheckAt).toISOString().slice(0, 10);
  const ip = state.exitIp || canonicalizeIp(fallbackIp) || null;
  if (!ip) return state;
  stats[day] ||= {};
  stats[day][ip] ||= { checks: 0, verified: 0, failures: 0, latencies: [] };
  const bucket = stats[day][ip];
  bucket.checks += 1;
  if (state.status === STATUS.VERIFIED) bucket.verified += 1;
  else bucket.failures += 1;
  if (Number.isFinite(state.latencyMs) && bucket.checks % 5 === 0) {
    bucket.latencies.push(Math.round(state.latencyMs));
    bucket.latencies = bucket.latencies.slice(-CONFIG.ROUTE_LATENCY_SAMPLES_PER_DAY);
  }
  const normalized = normalizeRouteDailyStats(stats);
  const summary = { days7: summarizeRouteStats(normalized, 7), days30: summarizeRouteStats(normalized, 30) };
  await setStorage({ routeDailyStats: normalized, routeStatsSummary: summary });
  return readState();
}

async function updateTrustedExitTest(state) {
  const test = normalizeTrustedExitTest(state.trustedExitTest);
  if (!test.active || state.status !== STATUS.VERIFIED || !state.exitIp) return state;
  const trusted = normalizeTrustedExitIps(state.trustedExitIps);
  if (!trusted.includes(state.exitIp)) return state;
  const seenIps = [...new Set([...test.seenIps, state.exitIp])];
  const complete = trusted.length > 0 && trusted.every(ip => seenIps.includes(ip));
  const next = {
    active: !complete,
    startedAt: test.startedAt || Date.now(),
    completedAt: complete ? Date.now() : null,
    seenIps
  };
  if (seenIps.length !== test.seenIps.length) {
    const index = trusted.indexOf(state.exitIp);
    await appendEvent("trusted-test", `Trusted Exit Test: MATCH ${index + 1}/${trusted.length} подтверждён (${state.exitIp}).${complete ? " Тест завершён 2/2." : " Переключите NekoBox на второй доверенный выход."}`);
  }
  await setStorage({ trustedExitTest: next });
  return readState();
}

async function startTrustedExitTest() {
  let state = await readState();
  const existing = normalizeTrustedExitTest(state.trustedExitTest);
  const now = Date.now();

  // Idempotent start: a second click while ACTIVE must not reset progress.
  if (existing.active) return state;

  // After a successful 2/2 completion, keep the result stable for a short cooldown.
  // This absorbs double-clicks / delayed popup events without turning COMPLETED back into ACTIVE 1/2.
  if (existing.completedAt && now - existing.completedAt < CONFIG.TRUSTED_EXIT_TEST_RESTART_COOLDOWN_MS) {
    return state;
  }

  const restarting = Boolean(existing.completedAt);
  const seenIps = state.status === STATUS.VERIFIED && state.exitIp && normalizeTrustedExitIps(state.trustedExitIps).includes(state.exitIp)
    ? [state.exitIp]
    : [];
  await setStorage({ trustedExitTest: { active: true, startedAt: now, completedAt: null, seenIps } });
  await appendEvent("trusted-test", `Trusted Exit Test ${restarting ? "запущен заново" : "запущен"}: ${seenIps.length}/${state.trustedExitIps.length} подтверждено.`);
  state = await readState();
  return state;
}

async function cancelTrustedExitTest() {
  await setStorage({ trustedExitTest: { active: false, startedAt: null, completedAt: null, seenIps: [] } });
  return readState();
}

async function appendProxyErrorEvent(fatal, message, qualifier = "") {
  const now = Date.now();
  const key = `${fatal ? "fatal" : "nonfatal"}|${message}|${qualifier}`;

  if (key === lastProxyErrorLogKey && now - lastProxyErrorLogAt < CONFIG.PROXY_ERROR_LOG_COALESCE_MS) {
    suppressedProxyErrorLogs += 1;
    return;
  }

  const repeatNote = key === lastProxyErrorLogKey && suppressedProxyErrorLogs > 0
    ? ` · +${suppressedProxyErrorLogs} повторов`
    : "";

  lastProxyErrorLogKey = key;
  lastProxyErrorLogAt = now;
  suppressedProxyErrorLogs = 0;

  await appendEvent(
    "proxy-error",
    `${fatal ? "FATAL · " : ""}${message}${qualifier ? ` · ${qualifier}` : ""}${repeatNote}`
  );
}


function hasManagerBridgeApi() {
  return typeof fetch === "function";
}

function managerMessageString(value, maxLength = 120) {
  return normalizeNullableString(typeof value === "string" ? value : null, maxLength);
}

function validateManagerStatusMessage(message) {
  if (!message || typeof message !== "object" || Array.isArray(message)) return null;
  if (message.type !== "status") return null;
  const protocol = Math.floor(Number(message.protocol) || 0);
  if (protocol !== CONFIG.MANAGER_PROTOCOL) return null;

  const local = message.localProxy && typeof message.localProxy === "object" ? message.localProxy : {};
  const localHost = managerMessageString(local.host, 80);
  const localPort = normalizePort(local.port);
  const localScheme = managerMessageString(local.scheme, 20) || "socks5";
  const localProxy = localHost ? `${localScheme}://${localHost}:${localPort}` : null;

  const endpoint = message.endpoint && typeof message.endpoint === "object"
    ? [managerMessageString(message.endpoint.host, 120), managerMessageString(message.endpoint.port, 10)].filter(Boolean).join(":")
    : managerMessageString(message.endpoint, 160);

  return {
    protocol,
    connected: Boolean(message.connected),
    version: managerMessageString(message.version, 40),
    bridgeVersion: managerMessageString(message.bridgeVersion, 40),
    goVersion: managerMessageString(message.goVersion, 40),
    profile: managerMessageString(message.profile, 120),
    engine: managerMessageString(message.engine, 40),
    mode: managerMessageString(message.mode, 40),
    localProxy,
    endpoint: endpoint || null,
    expectedExitIp: canonicalizeIp(message.expectedExitIp),
    verifiedExitIp: canonicalizeIp(message.verifiedExitIp) || canonicalizeIp(message.expectedExitIp),
    verifiedExitAt: (() => {
      const parsed = Date.parse(managerMessageString(message.verifiedExitAt, 64) || "");
      return Number.isFinite(parsed) ? parsed : null;
    })(),
    verifiedExitSucceeded: message.verifiedExitSucceeded === undefined || message.verifiedExitSucceeded === null ? null : Boolean(message.verifiedExitSucceeded),
    uptimeSeconds: Math.max(0, Math.floor(Number(message.uptimeSeconds) || 0))
  };
}

async function updateManagerCoherence(state) {
  const expected = state.managerMode === "tun"
    ? freshManagerVerifiedExitForTun(state)
    : (state.managerVerifiedExitIp || state.managerExpectedExitIp);
  const observed = state.enabled
    ? state.exitIp
    : state.managerMode === "tun"
      ? state.managerMonitorObservedExitIp
      : null;
  const coherence = state.managerBridgeConnected && expected && observed
    ? (expected === observed ? "match" : "mismatch")
    : "unknown";
  if (coherence !== state.managerCoherence) {
    await setStorage({ managerCoherence: coherence });
  }
  return coherence;
}


function browserProfileFromState(state) {
  return {
    port: state.port,
    profileName: state.profileName,
    strictLocal: state.strictLocal,
    trustedExitIps: state.trustedExitIps,
    webRtcShieldEnabled: state.webRtcShieldEnabled,
    webRtcCompatibility: state.webRtcCompatibility,
    hardKillSwitch: state.hardKillSwitch
  };
}

function freshManagerVerifiedExitForTun(state) {
  const ip = state.managerVerifiedExitIp || null;
  if (!ip || state.managerVerifiedExitSucceeded === false) return null;

  const transitionAt = Number(state.managerTransitionStartedAt) || Number(state.lastStateTransitionAt) || 0;
  if (!transitionAt) return ip;

  const verifiedAt = Number(state.managerVerifiedExitAt) || 0;
  if (!verifiedAt) return null;
  return verifiedAt + CONFIG.MANAGER_VERIFIED_FRESHNESS_GRACE_MS >= transitionAt ? ip : null;
}

function evaluateTunRoute(state, result) {
  const managerVerified = freshManagerVerifiedExitForTun(state);
  const trusted = normalizeTrustedExitIps(state.trustedExitIps);

  if (result.kind !== "verified" || !result.exitIp) {
    return {
      accepted: false,
      coherence: "unknown",
      managerVerified,
      trustedMatch: false,
      reason: result.kind === "degraded"
        ? "TUN route отвечает, но свежий browser exit IP не подтверждён."
        : "TUN route пока недоступен для свежей browser-проверки."
    };
  }

  if (managerVerified) {
    const accepted = managerVerified === result.exitIp;
    return {
      accepted,
      coherence: accepted ? "match" : "mismatch",
      managerVerified,
      trustedMatch: trusted.includes(result.exitIp),
      reason: accepted
        ? `Browser observed ${result.exitIp} совпадает со свежим Manager verified exit.`
        : `Browser observed ${result.exitIp} не совпадает со свежим Manager verified ${managerVerified}.`
    };
  }

  const trustedMatch = trusted.includes(result.exitIp);
  return {
    accepted: trustedMatch,
    coherence: "unknown",
    managerVerified: null,
    trustedMatch,
    reason: trustedMatch
      ? `Browser observed ${result.exitIp} входит в Trusted Exit; свежий Manager verified exit ещё не опубликован.`
      : `Browser observed ${result.exitIp} не входит в Trusted Exit и свежий Manager verified exit ещё не опубликован.`
  };
}

async function enterTunMonitorMode({ restoreLocalProxy = false, source = "manager" } = {}) {
  let state = await readState();
  const shouldRestore = Boolean(restoreLocalProxy || state.tunRestoreLocalProxy);
  const transitionAt = Date.now();

  // v5.5.6: fail closed BEFORE releasing chrome.proxy. The DNR guard explicitly allows
  // only the extension's Cloudflare/GStatic probes, so we can verify the system TUN while
  // ordinary browser web traffic remains blocked.
  if (state.hardKillSwitch) {
    // Stable: hardKillSwitch is the configured policy; killSwitchEngaged is only
    // the current runtime state. Always close the guard before releasing browser SOCKS.
    await setKillSwitch(true, "Переход Local → TUN: ожидание свежего подтверждения системного маршрута.", false);
  }
  else {
    await setKillSwitch(false, null, false);
  }

  await clearHealthAlarm();
  cancelProxyErrorTimer();
  cancelLiveGuardTimer();
  state = await writeState({
    enabled: false,
    status: STATUS.OFF,
    tunRestoreLocalProxy: shouldRestore,
    managerTransition: "to_tun",
    managerTransitionStartedAt: transitionAt,
    lastStateTransitionAt: transitionAt,
    managerMonitorStatus: "checking",
    managerMonitorObservedExitIp: null,
    managerMonitorLastCheckAt: null,
    managerMonitorLatencyMs: null,
    managerMonitorSource: null,
    managerMonitorTraceLocation: null,
    managerMonitorTraceColo: null,
    managerCoherence: "unknown",
    consecutiveFailures: 0,
    lastError: null,
    exitIp: null,
    latencyMs: null,
    lastCheckSource: null,
    traceLocation: null,
    traceColo: null,
    proxyConfigVerified: false,
    proxyControl: null
  });

  const errors = [];
  try {
    await clearOwnProxySetting();
  }
  catch (error) {
    errors.push(`Не удалось освободить browser SOCKS для TUN: ${errorMessage(error)}`);
  }

  try {
    const protectionResult = await ensurePrivacyProtections(state);
    await writeState({
      webRtcProtected: protectionResult.webRtcProtected,
      predictionDisabled: protectionResult.predictionDisabled,
      webRtcPolicy: protectionResult.webRtcPolicy,
      webRtcLevelOfControl: protectionResult.webRtcLevelOfControl,
      predictionLevelOfControl: protectionResult.predictionLevelOfControl,
      shieldState: protectionResult.shieldState,
      protectionsApplied: true
    });
  }
  catch (error) {
    errors.push(`Не удалось подтвердить browser privacy settings для TUN Monitor: ${errorMessage(error)}`);
  }

  await appendEvent(
    "manager",
    `TUN mode detected: browser SOCKS автоматически выключен (${source}); guard остаётся закрыт до свежей проверки TUN.`
  );
  if (errors.length > 0) {
    await setStorage({ managerLastError: errors.join(" ") });
  }

  // Stable: do not keep operationQueue occupied during TUN warm-up.
  // Storage updates will refresh an open popup while the monitor lane works independently.
  requestTunMonitor({ force: true, transition: true }).catch(error => {
    console.warn("TUN monitor transition check failed:", error);
  });
  return readState();
}

async function restoreLocalProxyAfterTun() {
  let state = await readState();
  if (!state.tunRestoreLocalProxy || state.enabled || !state.managerBridgeConnected || state.managerMode === "tun") {
    return state;
  }

  const profile = browserProfileFromState(state);
  state = await writeState({
    tunRestoreLocalProxy: false,
    managerTransition: "to_local",
    managerTransitionStartedAt: Date.now(),
    managerMonitorStatus: "off",
    managerMonitorObservedExitIp: null,
    managerMonitorLastCheckAt: null,
    managerMonitorLatencyMs: null,
    managerMonitorSource: null,
    managerMonitorTraceLocation: null,
    managerMonitorTraceColo: null,
    managerCoherence: "unknown"
  });
  await appendEvent("manager", "Local mode detected: восстанавливается browser SOCKS, который был активен до перехода в TUN.");
  return enableProxy(profile, false);
}

async function applyManagerStatus(message, source = "direct-http") {
  const parsed = validateManagerStatusMessage(message);
  if (!parsed) {
    await setStorage({ managerBridgeStatus: "error", managerBridgeConnected: false, managerLastError: "Некорректный ответ Direct Bridge." });
    return readState();
  }

  const previous = await readState();
  const now = Date.now();
  const profileChanged = parsed.connected && previous.managerProfile && parsed.profile && previous.managerProfile !== parsed.profile;
  const endpointChanged = parsed.connected && previous.managerEndpoint && parsed.endpoint && previous.managerEndpoint !== parsed.endpoint;
  const modeChanged = parsed.connected && previous.managerMode && parsed.mode && previous.managerMode !== parsed.mode;
  const connectionChanged = parsed.connected && (previous.managerBridgeConnected !== true || previous.managerBridgeStatus !== "connected");
  const disconnected = !parsed.connected && previous.managerBridgeConnected;
  const disconnectedFromTun = disconnected && previous.managerMode === "tun" && !previous.enabled;
  const managerContextChanged = Boolean(profileChanged || endpointChanged || modeChanged || connectionChanged || disconnected);

  if (managerContextChanged) {
    managerRouteGeneration += 1;
  }

  // FIX3: once browser SOCKS was released for system TUN, losing the Manager/TUN
  // session must immediately fail closed. Never leave browser_proxy_off with a
  // previously VERIFIED TUN result after the Manager has gone inactive.
  if (disconnectedFromTun && previous.hardKillSwitch) {
    await setKillSwitch(true, "GeniaProxy TUN остановлен/недоступен: ожидание нового защищённого маршрута.", false);
  }

  await setStorage({
    managerBridgeEnabled: true,
    managerBridgeStatus: parsed.connected ? "connected" : "disconnected",
    managerBridgeConnected: parsed.connected,
    managerProtocol: parsed.protocol,
    managerVersion: parsed.version,
    managerBridgeHostVersion: parsed.bridgeVersion,
    managerBridgeGoVersion: parsed.goVersion,
    managerProfile: parsed.profile,
    managerEngine: parsed.engine,
    managerMode: parsed.connected ? parsed.mode : null,
    managerLocalProxy: parsed.connected ? parsed.localProxy : null,
    managerEndpoint: parsed.connected ? parsed.endpoint : null,
    managerExpectedExitIp: parsed.connected ? parsed.expectedExitIp : null,
    managerVerifiedExitIp: parsed.connected ? parsed.verifiedExitIp : null,
    managerVerifiedExitAt: parsed.connected ? parsed.verifiedExitAt : null,
    managerVerifiedExitSucceeded: parsed.connected ? parsed.verifiedExitSucceeded : null,
    managerUptimeSeconds: parsed.connected ? parsed.uptimeSeconds : null,
    managerLastSeenAt: now,
    managerConnectAttemptAt: null,
    managerLastError: parsed.connected ? null : "Direct Bridge доступен; GeniaProxy Manager сейчас не подтверждён как активный.",
    ...(disconnectedFromTun ? {
      managerMonitorStatus: "unreachable",
      managerMonitorObservedExitIp: null,
      managerMonitorLastCheckAt: null,
      managerMonitorLatencyMs: null,
      managerMonitorSource: null,
      managerMonitorTraceLocation: null,
      managerMonitorTraceColo: null,
      managerCoherence: "unknown",
      managerTransition: null,
      managerTransitionStartedAt: null,
      lastStateTransitionAt: now,
      lastError: "GeniaProxy TUN остановлен/недоступен. Browser web traffic заблокирован до нового VERIFIED TUN или восстановления Local SOCKS."
    } : {})
  });
  managerEverConnected = true;
  scheduleActionRefresh(0);

  if (connectionChanged) {
    await appendEvent("manager", `GeniaProxy Direct Bridge подключён${parsed.version ? ` · v${parsed.version}` : ""}${parsed.engine ? ` · ${parsed.engine}` : ""}.`);
  }
  else if (disconnected) {
    if (disconnectedFromTun) {
      await appendEvent("manager", "GeniaProxy TUN больше не активен: предыдущий TUN VERIFIED инвалидирован; Hard Kill Switch закрыт до нового защищённого маршрута.");
    }
    else {
      await appendEvent("manager", "Direct Bridge доступен, но GeniaProxy Manager/локальный proxy сейчас не активен. Browser Privacy Shield продолжает работать автономно.");
    }
  }

  const routeChanged = Boolean(profileChanged || endpointChanged);
  if (managerContextChanged) {
    // Invalidate any browser route probe that started against the previous Manager
    // route/session. checkConnection() already fences its result with operationGeneration.
    operationGeneration += 1;
  }
  if (routeChanged) {
    await appendEvent("manager", `Manager route changed: ${parsed.profile || "profile"}${parsed.endpoint ? ` · ${parsed.endpoint}` : ""}. Browser route verification is refreshed.`);
  }

  if (modeChanged || (parsed.connected && parsed.mode === "tun" && previous.enabled)) {
    startManagerBridgeFastPolling("mode-transition");
  }

  // Do not perform TUN/SOCKS mutations inside the polling lane. Only enqueue a
  // coalesced route reconciliation when the newest snapshot actually requires work.
  const resumedDetachedTun = parsed.connected && parsed.mode === "tun" && !previous.enabled &&
    previous.managerTransition === "to_tun" && previous.managerMonitorStatus !== "verified";
  const tunMonitorDue = parsed.connected && parsed.mode === "tun" && !previous.enabled && (
    resumedDetachedTun ||
    !previous.managerMonitorLastCheckAt ||
    now - Number(previous.managerMonitorLastCheckAt) >= CONFIG.MANAGER_TUN_MONITOR_MIN_MS
  );
  const needsRouteReconcile = routeChanged || modeChanged || resumedDetachedTun ||
    (parsed.connected && parsed.mode === "tun" && previous.enabled) ||
    (parsed.connected && parsed.mode !== "tun" && previous.tunRestoreLocalProxy && !previous.enabled) ||
    tunMonitorDue;
  if (needsRouteReconcile) {
    scheduleManagerRouteReconcile({ routeChanged, modeChanged: modeChanged || resumedDetachedTun, tunMonitorDue, source });
  }

  const state = await readState();
  await updateManagerCoherence(state);
  return readState();
}

function mergeManagerRouteReconcileHints(hints = {}) {
  managerRouteReconcileHints.routeChanged = managerRouteReconcileHints.routeChanged || Boolean(hints.routeChanged);
  managerRouteReconcileHints.modeChanged = managerRouteReconcileHints.modeChanged || Boolean(hints.modeChanged);
  managerRouteReconcileHints.tunMonitorDue = managerRouteReconcileHints.tunMonitorDue || Boolean(hints.tunMonitorDue);
}

function scheduleManagerRouteReconcile(hints = {}) {
  mergeManagerRouteReconcileHints(hints);
  managerRouteReconcilePending = true;
  if (managerRouteReconcilePromise) return managerRouteReconcilePromise;

  managerRouteReconcilePromise = enqueue(async () => {
    while (managerRouteReconcilePending) {
      managerRouteReconcilePending = false;
      const currentHints = managerRouteReconcileHints;
      managerRouteReconcileHints = { routeChanged: false, modeChanged: false, tunMonitorDue: false };
      await reconcileManagerRouteFromBridge(currentHints);
    }
    return readState();
  }).catch(error => {
    console.warn("Direct Bridge route reconciliation failed:", error);
    return readState();
  }).finally(() => {
    managerRouteReconcilePromise = null;
    if (managerRouteReconcilePending) {
      scheduleManagerRouteReconcile();
    }
  });
  return managerRouteReconcilePromise;
}

async function reconcileManagerRouteFromBridge(hints = {}) {
  let state = await readState();
  if (!state.managerBridgeEnabled || !state.managerBridgeConnected) {
    return state;
  }

  // Local SOCKS and system TUN are mutually exclusive browser routing modes.
  if (state.managerMode === "tun" && state.enabled) {
    state = await enterTunMonitorMode({ restoreLocalProxy: true, source: "Direct Bridge Stable" });
    return state;
  }

  if (state.managerMode !== "tun" && state.tunRestoreLocalProxy && !state.enabled) {
    state = await restoreLocalProxyAfterTun();
    return state;
  }

  const coherence = await updateManagerCoherence(state);
  state = await readState();

  if (hints.routeChanged && state.enabled) {
    return checkConnection({ showChecking: false, notifyTransitions: true });
  }

  if (state.managerMode === "tun" && !state.enabled) {
    if (hints.routeChanged || hints.modeChanged || coherence === "mismatch") {
      if (state.hardKillSwitch) {
        await setKillSwitch(true, "TUN route changed: ожидание свежего browser/Manager подтверждения.", false);
      }
      const transitionAt = Date.now();
      state = await writeState({
        managerTransition: "to_tun",
        managerTransitionStartedAt: transitionAt,
        lastStateTransitionAt: transitionAt,
        managerMonitorStatus: "checking",
        managerMonitorObservedExitIp: null,
        managerCoherence: "unknown"
      });
      startManagerBridgeFastPolling("tun-reconcile");
      requestTunMonitor({ force: true, transition: true }).catch(error => {
        console.warn("TUN monitor reconcile check failed:", error);
      });
      return readState();
    }
    if (hints.tunMonitorDue) {
      requestTunMonitor().catch(error => {
        console.warn("TUN monitor periodic check failed:", error);
      });
      return readState();
    }
    return state;
  }

  if (coherence === "mismatch") {
    await appendEvent("manager", `Manager ожидает exit ${state.managerExpectedExitIp || "—"}, браузер наблюдает ${state.exitIp || "—"}. Trusted Exit остаётся источником блокирующего решения.`);
  }
  return state;
}

function mergeTunMonitorHints(hints = {}) {
  tunMonitorHints.force = tunMonitorHints.force || Boolean(hints.force);
  tunMonitorHints.transition = tunMonitorHints.transition || Boolean(hints.transition);
}

function requestTunMonitor(hints = {}) {
  mergeTunMonitorHints(hints);
  tunMonitorPending = true;
  if (tunMonitorInFlight) return tunMonitorInFlight;

  tunMonitorInFlight = (async () => {
    let latest = await readState();
    while (tunMonitorPending) {
      tunMonitorPending = false;
      const currentHints = tunMonitorHints;
      tunMonitorHints = { force: false, transition: false };
      latest = await checkTunMonitor(currentHints);
    }
    return latest;
  })().catch(error => {
    console.warn("TUN monitor lane failed:", error);
    return readState();
  }).finally(() => {
    tunMonitorInFlight = null;
    if (tunMonitorPending) {
      requestTunMonitor().catch(error => console.warn("TUN monitor follow-up failed:", error));
    }
  });

  return tunMonitorInFlight;
}

async function checkTunMonitor({ force = false, transition = false } = {}) {
  let state = await readState();
  if (!state.managerBridgeEnabled || !state.managerBridgeConnected || state.managerMode !== "tun" || state.enabled) {
    if (state.managerMode !== "tun" || state.enabled) {
      await writeState({ managerMonitorStatus: "off" });
    }
    return readState();
  }

  const routeGeneration = managerRouteGeneration;
  const now = Date.now();
  const lastCheck = Number(state.managerMonitorLastCheckAt) || 0;
  if (!force && !transition && lastCheck > 0 && now - lastCheck < CONFIG.MANAGER_TUN_MONITOR_MIN_MS) {
    return state;
  }

  const transitionRetryDelays = Array.isArray(CONFIG.MANAGER_TUN_TRANSITION_RETRY_DELAYS_MS)
    ? CONFIG.MANAGER_TUN_TRANSITION_RETRY_DELAYS_MS
    : [];
  const attempts = transition ? transitionRetryDelays.length + 1 : 1;
  const previousStatus = state.managerMonitorStatus;
  let lastResult = null;
  let lastEvaluation = null;

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    state = await writeState({
      managerMonitorStatus: "checking",
      managerTransition: transition ? "to_tun" : state.managerTransition,
      managerMonitorObservedExitIp: attempt === 1 ? null : state.managerMonitorObservedExitIp
    });

    const result = await probeConnection();
    state = await readState(); // Manager may publish a fresh verifiedExitIp while the probe is in flight.

    // FIX3 generation fence: a late successful probe from an older TUN session
    // must never release the guard after Manager disconnect/profile/mode changes.
    if (routeGeneration !== managerRouteGeneration ||
        !state.managerBridgeEnabled || !state.managerBridgeConnected ||
        state.managerMode !== "tun" || state.enabled) {
      if (state.hardKillSwitch && !state.enabled && state.managerTransition === "to_tun") {
        await setKillSwitch(true, "TUN Manager state changed while browser verification was in flight.", false);
      }
      return readState();
    }

    const evaluation = evaluateTunRoute(state, result);
    lastResult = result;
    lastEvaluation = evaluation;

    if (evaluation.accepted) {
      if (state.hardKillSwitch) {
        await setKillSwitch(false, null);
      }
      else {
        await setKillSwitch(false, null, false);
      }

      state = await writeState({
        managerMonitorStatus: "verified",
        managerMonitorObservedExitIp: result.exitIp,
        managerMonitorLastCheckAt: Date.now(),
        managerMonitorLatencyMs: result.latencyMs,
        managerMonitorSource: result.source,
        managerMonitorTraceLocation: result.traceLocation,
        managerMonitorTraceColo: result.traceColo,
        managerCoherence: evaluation.coherence,
        managerTransition: null,
        managerTransitionStartedAt: null,
        // FIX1: a Local SOCKS failure is historical once a fresh system-TUN route is VERIFIED.
        // Keep the event in Security Event Log, but do not leave the active UI in FATAL state.
        lastProxyError: null,
        lastProxyErrorAt: null,
        lastProxyFatal: false,
        consecutiveFailures: 0,
        lastError: null
      });
      lastProxyErrorLogKey = null;
      lastProxyErrorLogAt = 0;
      suppressedProxyErrorLogs = 0;

      if (previousStatus !== "verified" || transition) {
        const basis = evaluation.managerVerified
          ? ` · Manager verified ${evaluation.managerVerified}`
          : " · Trusted Exit fallback";
        await appendEvent("manager", `TUN Monitor VERIFIED: browser observed ${result.exitIp}${basis}; guard released.`);
      }
      return state;
    }

    const reason = `TUN Transition Guard: ${evaluation.reason}`;
    if (state.hardKillSwitch) {
      await setKillSwitch(true, reason, false);
    }

    if (attempt < attempts) {
      state = await writeState({
        managerMonitorStatus: "checking",
        managerMonitorObservedExitIp: result.exitIp || null,
        managerMonitorLastCheckAt: Date.now(),
        managerMonitorLatencyMs: result.latencyMs,
        managerMonitorSource: result.source,
        managerMonitorTraceLocation: result.traceLocation,
        managerMonitorTraceColo: result.traceColo,
        managerCoherence: evaluation.coherence,
        lastError: reason
      });
      await appendEvent("manager", `TUN transition probe ${attempt}/${attempts}: ${result.exitIp || result.kind} не подтверждён; guard остаётся закрыт.`);
      const retryDelayMs = transitionRetryDelays[Math.min(attempt - 1, transitionRetryDelays.length - 1)] || 1200;
      await delay(retryDelayMs);
      continue;
    }
  }

  state = await readState();
  const finalKind = lastResult?.kind || "unreachable";
  const finalStatus = finalKind === "verified"
    ? "mismatch"
    : finalKind === "degraded"
      ? "degraded"
      : "unreachable";
  const finalReason = `TUN Transition Guard: ${lastEvaluation?.reason || "маршрут не подтверждён"}`;

  if (state.hardKillSwitch) {
    await setKillSwitch(true, finalReason);
  }
  else {
    await setKillSwitch(false, null, false);
  }

  state = await writeState({
    managerMonitorStatus: finalStatus,
    managerMonitorObservedExitIp: lastResult?.exitIp || null,
    managerMonitorLastCheckAt: Date.now(),
    managerMonitorLatencyMs: lastResult?.latencyMs ?? null,
    managerMonitorSource: lastResult?.source || null,
    managerMonitorTraceLocation: lastResult?.traceLocation || null,
    managerMonitorTraceColo: lastResult?.traceColo || null,
    managerCoherence: lastEvaluation?.coherence || "unknown",
    managerTransition: null,
    managerTransitionStartedAt: null,
    lastError: `${finalReason}${state.hardKillSwitch ? " Browser web traffic остаётся заблокирован до свежего VERIFIED." : ""}`
  });

  const suffix = lastEvaluation?.managerVerified
    ? ` · Manager verified ${lastEvaluation.managerVerified}`
    : " · свежий Manager verified отсутствует";
  await appendEvent("manager", `TUN Monitor ${String(finalStatus).toUpperCase()}: browser observed ${lastResult?.exitIp || finalKind}${suffix}; guard ${state.hardKillSwitch ? "остаётся закрыт" : "не используется"}.`);
  return state;
}

function scheduleManagerHealthAlarm() {
  try {
    chrome.alarms.create(CONFIG.MANAGER_HEALTH_ALARM, { periodInMinutes: CONFIG.MANAGER_HEALTH_MINUTES });
  }
  catch (_error) {}
}

function clearManagerHealthAlarm() {
  try { chrome.alarms.clear(CONFIG.MANAGER_HEALTH_ALARM); } catch (_error) {}
}

function clearManagerBridgePollTimer() {
  if (managerBridgePollTimer !== null) {
    clearTimeout(managerBridgePollTimer);
    managerBridgePollTimer = null;
  }
}

function startManagerBridgeFastPolling(_reason = "transition", durationMs = CONFIG.MANAGER_TRANSITION_FAST_WINDOW_MS) {
  managerBridgeFastPollUntil = Math.max(managerBridgeFastPollUntil, Date.now() + Math.max(0, Number(durationMs) || 0));
  scheduleManagerBridgePoll(CONFIG.MANAGER_POLL_TRANSITION_MS);
}

function managerBridgePollDelay(state) {
  if (!state?.managerBridgeEnabled) return null;
  if (Date.now() < managerBridgeFastPollUntil || state.managerTransition) {
    return CONFIG.MANAGER_POLL_TRANSITION_MS;
  }
  if (!state.managerBridgeConnected) {
    return state.managerBridgeStatus === "disconnected"
      ? CONFIG.MANAGER_POLL_INACTIVE_MS
      : CONFIG.MANAGER_POLL_DISCONNECTED_MS;
  }
  return CONFIG.MANAGER_POLL_STABLE_MS;
}

function scheduleManagerBridgePoll(delayMs = null) {
  clearManagerBridgePollTimer();
  Promise.resolve(readState()).then(state => {
    const adaptiveDelay = delayMs === null ? managerBridgePollDelay(state) : delayMs;
    if (adaptiveDelay === null || !state.managerBridgeEnabled) return;
    managerBridgePollTimer = setTimeout(() => {
      managerBridgePollTimer = null;
      requestManagerBridgeRefresh("adaptive-poll").catch(error => {
        console.warn("Direct Bridge adaptive poll failed:", error);
      });
    }, Math.max(CONFIG.MANAGER_POLL_TRANSITION_MS, Number(adaptiveDelay) || CONFIG.MANAGER_POLL_STABLE_MS));
  }).catch(() => {});
}

async function fetchManagerStatus(controller = new AbortController()) {
  const timer = setTimeout(() => {
    try { controller.abort(); } catch (_error) {}
  }, CONFIG.MANAGER_FETCH_TIMEOUT_MS);
  try {
    const response = await fetch(CONFIG.MANAGER_DIRECT_URL, {
      method: "GET",
      cache: "no-store",
      credentials: "omit",
      redirect: "error",
      signal: controller.signal
    });
    if (!response.ok) {
      throw new Error(`Direct Bridge HTTP ${response.status}`);
    }
    const message = await response.json();
    const parsed = validateManagerStatusMessage(message);
    if (!parsed) {
      throw new Error("Direct Bridge вернул некорректный status payload.");
    }
    return message;
  }
  finally {
    clearTimeout(timer);
  }
}

async function managerWatchdogTick() {
  const state = await readState();
  if (!state.managerBridgeEnabled) {
    clearManagerHealthAlarm();
    clearManagerBridgePollTimer();
    return state;
  }
  return requestManagerBridgeRefresh("alarm-watchdog");
}

function closeManagerPort() {
  clearManagerBridgePollTimer();
  managerBridgeEpoch += 1;
  managerBridgeRefreshPending = false;
  if (managerBridgeAbortController) {
    try { managerBridgeAbortController.abort(); } catch (_error) {}
    managerBridgeAbortController = null;
  }
}

async function connectManagerBridge() {
  if (!hasManagerBridgeApi()) {
    closeManagerPort();
    await setStorage({
      managerBridgeEnabled: false,
      managerBridgeStatus: "api_unavailable",
      managerBridgeConnected: false,
      managerLastError: "Fetch API недоступен в этом браузере/контексте."
    });
    return readState();
  }

  scheduleManagerHealthAlarm();
  await setStorage({
    managerBridgeEnabled: true,
    managerBridgeStatus: "connecting",
    managerBridgeConnected: false,
    managerConnectAttemptAt: Date.now(),
    managerLastError: null
  });
  startManagerBridgeFastPolling("connect", 5000);
  return requestManagerBridgeRefresh("connect", { requireFresh: true });
}

async function disconnectManagerBridge() {
  const previous = await readState();
  const detachedTun = previous.managerBridgeConnected && previous.managerMode === "tun" && !previous.enabled;
  closeManagerPort();
  clearManagerHealthAlarm();

  if (detachedTun) {
    managerRouteGeneration += 1;
    operationGeneration += 1;
    if (previous.hardKillSwitch) {
      await setKillSwitch(true, "Direct Bridge отключён во время TUN: ожидание нового защищённого маршрута.", false);
    }
  }

  await setStorage({
    managerBridgeEnabled: false,
    managerBridgeStatus: "off",
    managerBridgeConnected: false,
    managerConnectAttemptAt: null,
    managerLastError: null,
    managerMode: null,
    managerLocalProxy: null,
    managerEndpoint: null,
    managerExpectedExitIp: null,
    managerVerifiedExitIp: null,
    managerVerifiedExitAt: null,
    managerVerifiedExitSucceeded: null,
    managerUptimeSeconds: null,
    managerTransition: null,
    managerTransitionStartedAt: null,
    ...(detachedTun ? {
      managerMonitorStatus: "unreachable",
      managerMonitorObservedExitIp: null,
      managerMonitorLastCheckAt: null,
      managerMonitorLatencyMs: null,
      managerMonitorSource: null,
      managerMonitorTraceLocation: null,
      managerMonitorTraceColo: null,
      managerCoherence: "unknown",
      managerTransition: null,
      managerTransitionStartedAt: null,
      lastStateTransitionAt: Date.now(),
      lastError: "Direct Bridge отключён во время TUN. Browser web traffic заблокирован до нового подтверждённого маршрута."
    } : {})
  });
  await appendEvent(
    "manager",
    detachedTun
      ? "GeniaProxy Direct Bridge отключён во время TUN: старое TUN подтверждение инвалидировано; Hard Kill Switch закрыт."
      : "GeniaProxy Direct Bridge отключён пользователем. Browser Privacy Shield продолжает работать автономно."
  );
  return readState();
}

async function performManagerBridgeRefresh(reason, epoch) {
  const state = await readState();
  if (!state.managerBridgeEnabled) return state;

  const controller = new AbortController();
  managerBridgeAbortController = controller;
  try {
    const message = await fetchManagerStatus(controller);
    if (epoch !== managerBridgeEpoch) return readState();
    managerEverConnected = true;
    return await applyManagerStatus(message, `direct-http:${reason}`);
  }
  catch (error) {
    if (epoch !== managerBridgeEpoch || error?.name === "AbortError") {
      return readState();
    }

    const lostTunSession = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
    if (lostTunSession) {
      managerRouteGeneration += 1;
      operationGeneration += 1;
      if (state.hardKillSwitch) {
        await setKillSwitch(true, "Direct Bridge/TUN недоступен: ожидание нового защищённого маршрута.", false);
      }
    }

    await setStorage({
      managerBridgeStatus: managerEverConnected ? "disconnected" : "unavailable",
      managerBridgeConnected: false,
      managerConnectAttemptAt: null,
      managerLastError: `Direct Bridge недоступен: ${errorMessage(error)}`,
      ...(lostTunSession ? {
        managerMode: null,
        managerLocalProxy: null,
        managerEndpoint: null,
        managerExpectedExitIp: null,
        managerVerifiedExitIp: null,
        managerVerifiedExitAt: null,
        managerVerifiedExitSucceeded: null,
        managerUptimeSeconds: null,
        managerMonitorStatus: "unreachable",
        managerMonitorObservedExitIp: null,
        managerMonitorLastCheckAt: null,
        managerMonitorLatencyMs: null,
        managerMonitorSource: null,
        managerMonitorTraceLocation: null,
        managerMonitorTraceColo: null,
        managerCoherence: "unknown",
        managerTransition: null,
        managerTransitionStartedAt: null,
        lastStateTransitionAt: Date.now(),
        lastError: "Direct Bridge/TUN недоступен. Browser web traffic заблокирован до нового VERIFIED TUN или восстановления Local SOCKS."
      } : {})
    });
    if (lostTunSession) {
      await appendEvent("manager", "Direct Bridge/TUN потерян после VERIFIED: старое TUN подтверждение инвалидировано; Hard Kill Switch закрыт.");
    }
    return readState();
  }
  finally {
    if (managerBridgeAbortController === controller) {
      managerBridgeAbortController = null;
    }
  }
}

function requestManagerBridgeRefresh(reason = "poll", { requireFresh = false } = {}) {
  if (managerBridgeRefreshInFlight) {
    managerBridgeRefreshPending = managerBridgeRefreshPending || requireFresh;
    return managerBridgeRefreshInFlight;
  }

  const epoch = managerBridgeEpoch;
  managerBridgeRefreshInFlight = performManagerBridgeRefresh(reason, epoch)
    .finally(() => {
      managerBridgeRefreshInFlight = null;
      const followUp = managerBridgeRefreshPending;
      managerBridgeRefreshPending = false;
      if (epoch !== managerBridgeEpoch) return;
      if (followUp) {
        scheduleManagerBridgePoll(CONFIG.MANAGER_POLL_TRANSITION_MS);
      }
      else {
        scheduleManagerBridgePoll();
      }
    });
  return managerBridgeRefreshInFlight;
}

function refreshManagerBridge() {
  return requestManagerBridgeRefresh("manual-refresh", { requireFresh: true });
}

function getProxySettings() {
  return new Promise((resolve, reject) => {
    chrome.proxy.settings.get({ incognito: false }, details => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve(details);
    });
  });
}

function setProxySettings(value) {
  return new Promise((resolve, reject) => {
    chrome.proxy.settings.set({ value, scope: "regular" }, () => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve();
    });
  });
}

function clearProxySettings() {
  return new Promise((resolve, reject) => {
    chrome.proxy.settings.clear({ scope: "regular" }, () => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve();
    });
  });
}

function getChromeSetting(setting) {
  return new Promise((resolve, reject) => {
    setting.get({ incognito: false }, details => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve(details);
    });
  });
}

function setChromeSetting(setting, value) {
  return new Promise((resolve, reject) => {
    setting.set({ value, scope: "regular" }, () => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve();
    });
  });
}

function clearChromeSetting(setting) {
  return new Promise((resolve, reject) => {
    setting.clear({ scope: "regular" }, () => {
      const error = chrome.runtime.lastError;
      if (error) {
        reject(new Error(error.message));
        return;
      }
      resolve();
    });
  });
}

function updateDynamicRules(options) {
  return chrome.declarativeNetRequest.updateDynamicRules(options);
}

function getDynamicRules() {
  return chrome.declarativeNetRequest.getDynamicRules();
}

function killSwitchRules() {
  return [
    {
      id: CONFIG.KILL_RULE_IDS[0],
      priority: 1,
      action: { type: "block" },
      condition: { regexFilter: "^(https?|wss?)://" }
    },
    {
      id: CONFIG.KILL_RULE_IDS[1],
      priority: 1,
      action: { type: "block" },
      condition: {
        regexFilter: "^https?://",
        resourceTypes: ["main_frame"]
      }
    },
    {
      id: CONFIG.KILL_RULE_IDS[2],
      priority: 10,
      action: { type: "allow" },
      condition: {
        urlFilter: "|https://1.1.1.1/cdn-cgi/trace",
        initiatorDomains: [chrome.runtime.id]
      }
    },
    {
      id: CONFIG.KILL_RULE_IDS[3],
      priority: 10,
      action: { type: "allow" },
      condition: {
        urlFilter: "|https://www.gstatic.com/generate_204",
        initiatorDomains: [chrome.runtime.id]
      }
    },
    {
      id: CONFIG.KILL_RULE_IDS[4],
      priority: 20,
      action: { type: "allow" },
      condition: {
        regexFilter: "^http://127\\.0\\.0\\.1:47831/v1/status(?:\\?.*)?$",
        resourceTypes: ["xmlhttprequest"],
        initiatorDomains: [chrome.runtime.id]
      }
    }
  ];
}

async function getKillSwitchRuleState() {
  const rules = await getDynamicRules();
  const ownRules = rules.filter(rule => CONFIG.KILL_RULE_IDS.includes(rule.id));
  const ids = new Set(ownRules.map(rule => rule.id));
  return {
    anyPresent: ownRules.length > 0,
    allPresent: CONFIG.KILL_RULE_IDS.every(id => ids.has(id)),
    presentIds: [...ids]
  };
}

async function isKillSwitchActuallyEngaged() {
  return (await getKillSwitchRuleState()).allPresent;
}

async function setKillSwitch(active, reason = null, logChange = true) {
  const shouldEngage = Boolean(active);
  const before = await getKillSwitchRuleState();
  const actualBefore = before.allPresent;

  if (shouldEngage && !before.allPresent) {
    // updateDynamicRules is atomic: remove any stale/partial rules and install the full set.
    await updateDynamicRules({
      removeRuleIds: [...CONFIG.KILL_RULE_IDS],
      addRules: killSwitchRules()
    });
  }
  else if (!shouldEngage && before.anyPresent) {
    // Important: remove even a partial stale rule set. Older builds could otherwise leave a block rule behind.
    await updateDynamicRules({ removeRuleIds: [...CONFIG.KILL_RULE_IDS] });
  }

  if (actualBefore !== shouldEngage && logChange) {
    await appendEvent(
      shouldEngage ? "kill-switch" : "recovery",
      shouldEngage
        ? `Hard Kill Switch включён${reason ? `: ${reason}` : "."}`
        : "Hard Kill Switch снят после подтверждения защищённого маршрута."
    );
  }

  await setStorage({
    killSwitchEngaged: shouldEngage,
    killSwitchReason: shouldEngage ? normalizeNullableString(reason, 300) : null
  });
}

async function syncKillSwitchForState(state) {
  // Fail closed: while enabled, only VERIFIED is allowed to have the browser web guard released.
  const shouldEngage = state.enabled && state.hardKillSwitch && state.status !== STATUS.VERIFIED;
  await setKillSwitch(
    shouldEngage,
    shouldEngage ? (state.lastError || "маршрут ещё не подтверждён VERIFIED") : null,
    false
  );
}

function clearHealthAlarm() {
  return new Promise(resolve => {
    chrome.alarms.clear(CONFIG.HEALTH_ALARM, () => {
      void chrome.runtime.lastError;
      resolve();
    });
  });
}

async function clearLegacyHealthAlarms() {
  for (const alarmName of CONFIG.LEGACY_HEALTH_ALARMS) {
    await new Promise(resolve => {
      chrome.alarms.clear(alarmName, () => {
        void chrome.runtime.lastError;
        resolve();
      });
    });
  }
}

function scheduleHealthAlarm(state = null) {
  const period = state?.status === STATUS.VERIFIED
    ? CONFIG.HEALTH_VERIFIED_MINUTES
    : CONFIG.HEALTH_RECOVERY_MINUTES;
  chrome.alarms.create(CONFIG.HEALTH_ALARM, {
    delayInMinutes: period,
    periodInMinutes: period
  });
}

function cancelProxyErrorTimer() {
  if (proxyErrorTimer !== null) {
    clearTimeout(proxyErrorTimer);
    proxyErrorTimer = null;
  }
}

function cancelLiveGuardTimer() {
  if (liveGuardTimer !== null) {
    clearTimeout(liveGuardTimer);
    liveGuardTimer = null;
  }
}

function setActionIcon(path) {
  chrome.action.setIcon({
    path: { "16": path, "32": path, "48": path, "128": path }
  }, () => void chrome.runtime.lastError);
}

function setActionBadge(text, color) {
  chrome.action.setBadgeText({ text }, () => void chrome.runtime.lastError);
  chrome.action.setBadgeBackgroundColor({ color }, () => void chrome.runtime.lastError);
}

function scheduleActionRefresh(delayMs = 80) {
  if (actionRefreshTimer !== null) {
    clearTimeout(actionRefreshTimer);
  }
  actionRefreshTimer = setTimeout(() => {
    actionRefreshTimer = null;
    readState()
      .then(state => renderActionFromLatestState(state))
      .catch(error => console.warn("Could not refresh action badge:", error));
  }, Math.max(0, Number(delayMs) || 0));
}

function renderActionFromLatestState(state) {
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  if (tunMonitor) {
    const monitor = state.managerMonitorStatus || "idle";
    const healthy = monitor === "verified" && state.shieldState !== "compromised";
    setActionIcon(healthy ? "on.png" : "off.png");
    if (monitor === "verified") setActionBadge("T✓", "#16A34A");
    else if (monitor === "checking" || monitor === "idle") setActionBadge("T…", "#D97706");
    else if (monitor === "mismatch") setActionBadge("T!", "#DC2626");
    else if (["unreachable", "degraded"].includes(monitor)) setActionBadge("T?", "#D97706");
    else setActionBadge("T", "#64748B");
    const observed = state.managerMonitorObservedExitIp ? ` · ${state.managerMonitorObservedExitIp}` : "";
    chrome.action.setTitle({ title: `Genia Proxy Switcher — TUN Monitor ${String(monitor).toUpperCase()}${observed}` }, () => void chrome.runtime.lastError);
    return;
  }

  if (state.managerTransition === "to_local") {
    setActionIcon("off.png");
    setActionBadge("L…", "#D97706");
    chrome.action.setTitle({ title: "Genia Proxy Switcher — Switching to Local SOCKS…" }, () => void chrome.runtime.lastError);
    return;
  }

  const descriptions = {
    [STATUS.OFF]: "Выключен",
    [STATUS.CHECKING]: "Проверка защищённого маршрута",
    [STATUS.VERIFIED]: "Маршрут подтверждён",
    [STATUS.DEGRADED]: "Маршрут доступен, выходной IP не подтверждён",
    [STATUS.WRONG_EXIT]: "Обнаружен неожиданный выходной IP",
    [STATUS.UNREACHABLE]: "Прокси недоступен — DIRECT-резерв отсутствует",
    [STATUS.BLOCKED]: "Privacy Shield заблокирован или перехвачен",
    [STATUS.ERROR]: "Ошибка"
  };

  setActionIcon(state.status === STATUS.VERIFIED && state.shieldState !== "compromised" ? "on.png" : "off.png");

  if (state.status === STATUS.VERIFIED && state.shieldState !== "compromised") {
    const trusted = normalizeTrustedExitIps(state.trustedExitIps);
    const index = state.exitIp ? trusted.indexOf(state.exitIp) : -1;
    setActionBadge(index >= 0 ? `✓${index + 1}` : "✓", "#16A34A");
  }
  else if ([STATUS.CHECKING, STATUS.DEGRADED].includes(state.status)) {
    setActionBadge(state.status === STATUS.DEGRADED ? "?" : "…", "#D97706");
  }
  else if (state.status === STATUS.WRONG_EXIT) {
    setActionBadge("IP", "#DC2626");
  }
  else if ([STATUS.UNREACHABLE, STATUS.BLOCKED, STATUS.ERROR].includes(state.status)) {
    setActionBadge("!", "#DC2626");
  }
  else {
    setActionBadge("", "#64748B");
  }

  chrome.action.setTitle({
    title: `Genia Proxy Switcher — ${descriptions[state.status] || "Неизвестно"}`
  }, () => void chrome.runtime.lastError);
}

function notify(title, message, success = false) {
  const key = `${title}|${message}`;
  const now = Date.now();
  const previous = notificationState.get(key) || 0;
  if (now - previous < CONFIG.NOTIFICATION_COOLDOWN_MS) return;
  notificationState.set(key, now);
  chrome.notifications.create(CONFIG.NOTIFICATION_ID, {
    type: "basic",
    iconUrl: success ? "on.png" : "off.png",
    title,
    message,
    priority: 1
  }, () => void chrome.runtime.lastError);
}

function expectedBypassList(strictLocal) {
  // Chrome has implicit DIRECT bypasses for localhost and link-local literals.
  // In strict mode subtract them first, then explicitly restore loopback only.
  return strictLocal
    ? ["<-loopback>", "localhost", "*.localhost", "127.0.0.0/8", "[::1]"]
    : ["<local>", "localhost", "127.0.0.1", "[::1]"];
}

function buildProxyConfig(state) {
  return {
    mode: "fixed_servers",
    rules: {
      singleProxy: {
        scheme: "socks5",
        host: CONFIG.PROXY_HOST,
        port: normalizePort(state.port)
      },
      bypassList: expectedBypassList(state.strictLocal)
    }
  };
}

function normalizedBypassList(value) {
  return Array.isArray(value)
    ? [...new Set(value.map(item => String(item).trim()).filter(Boolean))].sort()
    : [];
}

function proxyConfigMatches(details, state) {
  const proxy = details?.value?.rules?.singleProxy;
  const actualBypass = normalizedBypassList(details?.value?.rules?.bypassList);
  const expectedBypass = normalizedBypassList(expectedBypassList(state.strictLocal));

  return details?.value?.mode === "fixed_servers" &&
    proxy?.scheme === "socks5" &&
    proxy?.host === CONFIG.PROXY_HOST &&
    Number(proxy?.port) === normalizePort(state.port) &&
    JSON.stringify(actualBypass) === JSON.stringify(expectedBypass);
}

async function checkProxyControl() {
  const details = await getProxySettings();

  if (details.levelOfControl === "controlled_by_other_extensions") {
    return {
      ok: false,
      details,
      message: "Настройки прокси контролируются другим расширением. Отключите его и повторите попытку."
    };
  }

  if (details.levelOfControl === "not_controllable") {
    return {
      ok: false,
      details,
      message: "Настройки прокси заблокированы политикой Chrome."
    };
  }

  return { ok: true, details };
}

async function ensureStrictProxy(state) {
  const control = await checkProxyControl();
  if (!control.ok) {
    return {
      ok: false,
      message: control.message,
      proxyControl: control.details?.levelOfControl || null,
      proxyConfigVerified: false,
      repaired: false
    };
  }

  if (
    control.details.levelOfControl === "controlled_by_this_extension" &&
    proxyConfigMatches(control.details, state)
  ) {
    return {
      ok: true,
      message: null,
      proxyControl: control.details.levelOfControl,
      proxyConfigVerified: true,
      repaired: false
    };
  }

  await setProxySettings(buildProxyConfig(state));
  const verified = await getProxySettings();
  const matches = verified.levelOfControl === "controlled_by_this_extension" &&
    proxyConfigMatches(verified, state);

  if (!matches) {
    return {
      ok: false,
      message: "Chrome не подтвердил применение строгого SOCKS5-маршрута.",
      proxyControl: verified.levelOfControl || null,
      proxyConfigVerified: false,
      repaired: false
    };
  }

  return {
    ok: true,
    message: null,
    proxyControl: verified.levelOfControl,
    proxyConfigVerified: true,
    repaired: true
  };
}

function desiredWebRtcPolicy(state) {
  if (!state.webRtcShieldEnabled) {
    return null;
  }
  return state.webRtcCompatibility
    ? "default_public_interface_only"
    : "disable_non_proxied_udp";
}

async function ensureSettingValue(setting, value, name) {
  let details = await getChromeSetting(setting);
  let repaired = false;

  // Ownership matters even when the current value happens to match. Otherwise another extension
  // could control the same value now and silently change it later while we incorrectly report PROTECTED.
  if (details.levelOfControl === "controlled_by_other_extensions") {
    throw new Error(`${name} управляется другим расширением.`);
  }
  if (details.levelOfControl === "not_controllable") {
    throw new Error(`${name} заблокировано политикой Chrome.`);
  }

  if (details.value !== value || details.levelOfControl !== "controlled_by_this_extension") {
    await setChromeSetting(setting, value);
    repaired = true;
    details = await getChromeSetting(setting);
  }

  if (details.value !== value || details.levelOfControl !== "controlled_by_this_extension") {
    throw new Error(`Chrome не подтвердил владение настройкой: ${name}.`);
  }

  return { details, repaired };
}

async function ensurePrivacyProtections(state) {
  const targetWebRtc = desiredWebRtcPolicy(state);
  let webRtcDetails = await getChromeSetting(chrome.privacy.network.webRTCIPHandlingPolicy);
  let webRtcRepaired = false;

  if (targetWebRtc) {
    const webRtcResult = await ensureSettingValue(
      chrome.privacy.network.webRTCIPHandlingPolicy,
      targetWebRtc,
      "WebRTC Shield"
    );
    webRtcDetails = webRtcResult.details;
    webRtcRepaired = webRtcResult.repaired;
  }
  else if (webRtcDetails.levelOfControl === "controlled_by_this_extension") {
    await clearChromeSetting(chrome.privacy.network.webRTCIPHandlingPolicy);
    webRtcRepaired = true;
    webRtcDetails = await getChromeSetting(chrome.privacy.network.webRTCIPHandlingPolicy);
  }

  const predictionResult = await ensureSettingValue(
    chrome.privacy.network.networkPredictionEnabled,
    false,
    "отключение сетевого предсказания Chrome"
  );

  const webRtcProtected = targetWebRtc ? webRtcDetails.value === targetWebRtc : false;
  return {
    webRtcProtected,
    predictionDisabled: predictionResult.details.value === false,
    webRtcPolicy: normalizeNullableString(String(webRtcDetails.value || ""), 80),
    webRtcLevelOfControl: webRtcDetails.levelOfControl || null,
    predictionLevelOfControl: predictionResult.details.levelOfControl || null,
    shieldState: !state.webRtcShieldEnabled
      ? "off"
      : state.webRtcCompatibility
        ? "compatibility"
        : "protected",
    repaired: webRtcRepaired || predictionResult.repaired
  };
}

async function releasePrivacyProtections() {
  const settings = [
    chrome.privacy.network.webRTCIPHandlingPolicy,
    chrome.privacy.network.networkPredictionEnabled
  ];
  const errors = [];

  for (const setting of settings) {
    try {
      const details = await getChromeSetting(setting);
      if (details.levelOfControl === "controlled_by_this_extension") {
        await clearChromeSetting(setting);
      }
    }
    catch (error) {
      errors.push(errorMessage(error));
    }
  }

  if (errors.length > 0) {
    throw new Error(errors.join(" "));
  }
}

async function clearOwnProxySetting() {
  const details = await getProxySettings();
  if (details.levelOfControl === "controlled_by_this_extension") {
    await clearProxySettings();
  }
}

async function fetchWithTimeout(url, options = {}) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), CONFIG.HEALTH_TIMEOUT_MS);
  const separator = url.includes("?") ? "&" : "?";

  try {
    return await fetch(`${url}${separator}genia_check=${Date.now()}`, {
      method: "GET",
      cache: "no-store",
      credentials: "omit",
      redirect: "follow",
      signal: controller.signal,
      ...options
    });
  }
  finally {
    clearTimeout(timeoutId);
  }
}

function parseTraceField(traceText, field, maxLength = 80) {
  const expression = new RegExp(`^${field}=(.+)$`, "m");
  const match = expression.exec(traceText);
  return match ? match[1].trim().slice(0, maxLength) : null;
}

async function probeTrace() {
  const startedAt = performance.now();
  const response = await fetchWithTimeout(CONFIG.TRACE_URL);
  const latencyMs = Math.max(0, Math.round(performance.now() - startedAt));

  if (response.status !== 200) {
    throw new Error(`Cloudflare Trace вернул HTTP ${response.status}.`);
  }

  const traceText = await response.text();
  const exitIp = canonicalizeIp(parseTraceField(traceText, "ip"));
  if (!exitIp) {
    throw new Error("Cloudflare Trace не вернул корректный выходной IP.");
  }

  return {
    exitIp,
    source: "Cloudflare Trace",
    latencyMs,
    traceLocation: parseTraceField(traceText, "loc", 20),
    traceColo: parseTraceField(traceText, "colo", 20)
  };
}

async function probeFallback() {
  const startedAt = performance.now();
  const response = await fetchWithTimeout(CONFIG.FALLBACK_URL);
  const latencyMs = Math.max(0, Math.round(performance.now() - startedAt));

  if (response.status !== 204) {
    throw new Error(`Google connectivity вернул HTTP ${response.status} вместо 204.`);
  }

  return {
    exitIp: null,
    source: "Google connectivity",
    latencyMs,
    traceLocation: null,
    traceColo: null
  };
}

function errorMessage(error) {
  if (error?.name === "AbortError") {
    return "время ожидания истекло";
  }
  return error instanceof Error ? error.message : String(error);
}

async function probeConnection() {
  // Privacy-first probing: Cloudflare is the primary verifier. Only contact Google when
  // the primary verifier fails, instead of sending a routine request to both every minute.
  try {
    return {
      kind: "verified",
      ...(await probeTrace()),
      errors: []
    };
  }
  catch (traceError) {
    try {
      return {
        kind: "degraded",
        ...(await probeFallback()),
        errors: [errorMessage(traceError)]
      };
    }
    catch (fallbackError) {
      return {
        kind: "unreachable",
        exitIp: null,
        source: null,
        latencyMs: null,
        traceLocation: null,
        traceColo: null,
        errors: [errorMessage(traceError), errorMessage(fallbackError)]
      };
    }
  }
}

async function applyProtectionDiagnostics(state, proxyResult, protectionResult) {
  return writeState({
    proxyControl: proxyResult?.proxyControl || null,
    proxyConfigVerified: Boolean(proxyResult?.proxyConfigVerified),
    webRtcProtected: Boolean(protectionResult?.webRtcProtected),
    predictionDisabled: Boolean(protectionResult?.predictionDisabled),
    webRtcPolicy: protectionResult?.webRtcPolicy || null,
    webRtcLevelOfControl: protectionResult?.webRtcLevelOfControl || null,
    predictionLevelOfControl: protectionResult?.predictionLevelOfControl || null,
    shieldState: protectionResult?.shieldState || (state.webRtcShieldEnabled ? "compromised" : "off"),
    protectionsApplied: Boolean(protectionResult?.predictionDisabled) &&
      (!state.webRtcShieldEnabled || Boolean(protectionResult?.webRtcProtected))
  });
}

async function setBlockedState(message, showNotification, diagnostics = {}) {
  cancelProxyErrorTimer();
  const current = await readState();
  scheduleHealthAlarm();

  if (current.hardKillSwitch) {
    await setKillSwitch(true, message);
  }
  else {
    await setKillSwitch(false, null);
  }

  const state = await writeState({
    enabled: true,
    status: STATUS.BLOCKED,
    consecutiveFailures: current.consecutiveFailures + 1,
    lastCheckAt: Date.now(),
    lastError: message,
    exitIp: null,
    latencyMs: null,
    protectionsApplied: false,
    lastCheckSource: null,
    traceLocation: null,
    traceColo: null,
    proxyControl: diagnostics.proxyControl || null,
    proxyConfigVerified: false,
    webRtcProtected: false,
    shieldState: current.webRtcShieldEnabled ? "compromised" : "off"
  });

  await appendEvent("compromised", message);
  if (showNotification) {
    notify("Genia Privacy Shield — защита перехвачена", message, false);
  }
  return readState();
}

async function failActivation(error, showNotification = true) {
  const message = errorMessage(error);
  cancelProxyErrorTimer();
  const current = await readState();
  scheduleHealthAlarm();

  if (current.hardKillSwitch) {
    await setKillSwitch(true, message);
  }
  else {
    await setKillSwitch(false, null);
  }

  const state = await writeState({
    enabled: true,
    status: STATUS.ERROR,
    consecutiveFailures: current.consecutiveFailures + 1,
    lastCheckAt: Date.now(),
    lastError: message,
    exitIp: null,
    latencyMs: null,
    protectionsApplied: false,
    lastCheckSource: null,
    traceLocation: null,
    traceColo: null,
    proxyConfigVerified: false,
    webRtcProtected: false,
    shieldState: current.webRtcShieldEnabled ? "compromised" : "off"
  });

  await appendEvent("error", message);
  if (showNotification) {
    notify("Genia Privacy Shield — ошибка защиты", message, false);
  }
  return state;
}

async function checkConnection(options = {}) {
  const { showChecking = false, notifyTransitions = true } = options;
  const generation = operationGeneration;
  let state = await readState();

  if (!state.enabled) {
    await setKillSwitch(false, null, false);
    scheduleActionRefresh(0);
    return state;
  }

  const previousStatus = state.status;
  const previousExitIp = state.exitIp;
  const previousTraceLocation = state.traceLocation;
  const previousTraceColo = state.traceColo;

  if (showChecking) {
    state = await writeState({ status: STATUS.CHECKING, lastError: null });
  }

  let proxyResult;
  let protectionResult;

  try {
    proxyResult = await ensureStrictProxy(state);
    if (!proxyResult.ok) {
      return setBlockedState(proxyResult.message, notifyTransitions, proxyResult);
    }
    protectionResult = await ensurePrivacyProtections(state);
  }
  catch (error) {
    return failActivation(error, notifyTransitions);
  }

  state = await applyProtectionDiagnostics(state, proxyResult, protectionResult);

  if (proxyResult.repaired) {
    await appendEvent("recovery", "Live Guard восстановил конфигурацию SOCKS5.");
  }
  if (protectionResult.repaired) {
    await appendEvent("recovery", "Live Guard восстановил настройки Privacy Shield.");
  }

  const result = await probeConnection();
  if (generation !== operationGeneration) {
    return readState();
  }
  const now = Date.now();

  if (result.kind === "verified") {
    const trustedExitIps = normalizeTrustedExitIps(state.trustedExitIps);
    const exitMatches = trustedExitIps.length === 0 || trustedExitIps.includes(result.exitIp);
    const trustedExitChanged = Boolean(
      exitMatches &&
      previousStatus === STATUS.VERIFIED &&
      previousExitIp &&
      previousExitIp !== result.exitIp
    );

    if (!exitMatches) {
      const message = `Cloudflare видит недоверенный выходной IP ${result.exitIp}. Trusted Exit: ${trustedExitIps.join(", ")}.`;
      if (state.hardKillSwitch) {
        await setKillSwitch(true, message);
      }
      else {
        await setKillSwitch(false, null);
      }
      state = await writeState({
        status: STATUS.WRONG_EXIT,
        consecutiveFailures: state.consecutiveFailures + 1,
        lastCheckAt: now,
        lastError: `${message} ${state.hardKillSwitch ? "Веб-трафик заблокирован Hard Kill Switch." : "SOCKS5 оставлен активным."}`,
        exitIp: result.exitIp,
        latencyMs: result.latencyMs,
        protectionsApplied: true,
        lastCheckSource: result.source,
        traceLocation: result.traceLocation,
        traceColo: result.traceColo
      });
    }
    else {
      const recoveredProxyError = state.lastProxyError;
      cancelProxyErrorTimer();
      await setKillSwitch(false, null);
      state = await writeState({
        status: STATUS.VERIFIED,
        consecutiveFailures: 0,
        lastCheckAt: now,
        lastError: result.errors.length > 0
          ? `Основная проверка подтверждена. Дополнительная проверка: ${result.errors.join(" ")}`
          : null,
        exitIp: result.exitIp,
        latencyMs: result.latencyMs,
        protectionsApplied: true,
        lastCheckSource: result.source,
        traceLocation: result.traceLocation,
        traceColo: result.traceColo,
        // onProxyError is a historical event once the route is VERIFIED again.
        // Keep the event log, but do not leave a recovered error painted red in diagnostics.
        lastProxyError: null,
        lastProxyErrorAt: null,
        lastProxyFatal: false
      });
      if (recoveredProxyError) {
        await appendEvent("recovery", `Proxy error восстановлена: маршрут снова VERIFIED (${result.exitIp}).`);
      }

      if (trustedExitChanged) {
        const fromLocation = [previousTraceLocation, previousTraceColo].filter(Boolean).join("/") || "?";
        const toLocation = [result.traceLocation, result.traceColo].filter(Boolean).join("/") || "?";
        const oldIndex = trustedExitIps.indexOf(previousExitIp);
        const newIndex = trustedExitIps.indexOf(result.exitIp);
        const oldMatch = oldIndex >= 0 ? ` ${oldIndex + 1}/${trustedExitIps.length}` : "";
        const newMatch = newIndex >= 0 ? ` ${newIndex + 1}/${trustedExitIps.length}` : "";
        const message = `Trusted Exit changed: MATCH${oldMatch} ${previousExitIp} (${fromLocation}) → MATCH${newMatch} ${result.exitIp} (${toLocation}). Privacy Shield повторно подтверждён.`;
        await appendEvent("trusted-exit", message);
        state = await writeState({
          lastGuardAt: now,
          lastGuardEvent: `Trusted Exit ${previousExitIp} → ${result.exitIp}; Privacy Shield re-verified`
        });
      }
    }
  }
  else if (result.kind === "degraded") {
    const trustedExitIps = normalizeTrustedExitIps(state.trustedExitIps);
    const reason = trustedExitIps.length
      ? `Trusted Exit IP (${trustedExitIps.join(", ")}) невозможно подтвердить.`
      : "Выходной IP невозможно подтвердить через основной проверочный канал.";
    if (state.hardKillSwitch) {
      await setKillSwitch(true, reason);
    }
    else {
      await setKillSwitch(false, null);
    }
    state = await writeState({
      status: STATUS.DEGRADED,
      consecutiveFailures: state.consecutiveFailures + 1,
      lastCheckAt: now,
      lastError: `Маршрут отвечает, но выходной IP подтвердить не удалось. ${state.hardKillSwitch ? "Fail-closed: веб-трафик заблокирован до VERIFIED. " : ""}${result.errors.join(" ")}`,
      exitIp: null,
      latencyMs: result.latencyMs,
      protectionsApplied: true,
      lastCheckSource: result.source,
      traceLocation: null,
      traceColo: null
    });
  }
  else {
    const message = `SOCKS5 ${CONFIG.PROXY_HOST}:${state.port} или его VPS-маршрут не отвечает.`;
    if (state.hardKillSwitch) {
      await setKillSwitch(true, message);
    }
    else {
      await setKillSwitch(false, null);
    }
    state = await writeState({
      status: STATUS.UNREACHABLE,
      consecutiveFailures: state.consecutiveFailures + 1,
      lastCheckAt: now,
      lastError: `${message} ${state.hardKillSwitch ? "Веб-трафик заблокирован Hard Kill Switch." : "DIRECT-резерв в proxy config не добавлен."} ${result.errors.join(" ")}`,
      exitIp: null,
      latencyMs: null,
      protectionsApplied: true,
      lastCheckSource: null,
      traceLocation: null,
      traceColo: null
    });
  }

  if (state.status !== previousStatus) {
    await writeState({ lastStateTransitionAt: Date.now() });
    await appendEvent("status", `${previousStatus} → ${state.status}${state.exitIp ? ` · ${state.exitIp}` : ""}`);
  }

  if (notifyTransitions && state.status !== previousStatus) {
    if (state.status === STATUS.VERIFIED && [STATUS.UNREACHABLE, STATUS.DEGRADED, STATUS.WRONG_EXIT, STATUS.BLOCKED, STATUS.ERROR].includes(previousStatus)) {
      notify("Genia Proxy Switcher", `Маршрут подтверждён. Выходной IP: ${state.exitIp}.`, true);
    }
    else if (state.status === STATUS.WRONG_EXIT) {
      notify("Genia Proxy Switcher — неверный IP", state.lastError, false);
    }
    else if (state.status === STATUS.UNREACHABLE) {
      notify("Genia Proxy Switcher — маршрут недоступен", state.lastError, false);
    }
    else if (state.status === STATUS.DEGRADED && previousStatus === STATUS.VERIFIED) {
      notify("Genia Proxy Switcher — проверка ослаблена", state.lastError, false);
    }
  }

  state = await readState();
  if (state.managerTransition === "to_local" && state.status !== STATUS.CHECKING) {
    state = await writeState({ managerTransition: null, managerTransitionStartedAt: null });
  }

  state = await recordRouteSample(state, previousExitIp);
  state = await updateTrustedExitTest(state);
  scheduleHealthAlarm(state);
  return state;
}

function normalizeProfile(profile, fallbackState) {
  return {
    port: normalizePort(profile?.port ?? fallbackState.port),
    profileName: normalizeProfileName(profile?.profileName ?? fallbackState.profileName),
    strictLocal: profile?.strictLocal === undefined ? fallbackState.strictLocal : Boolean(profile.strictLocal),
    trustedExitIps: profile?.trustedExitIps === undefined
      ? normalizeTrustedExitIps(fallbackState.trustedExitIps)
      : normalizeTrustedExitIps(profile.trustedExitIps, true),
    expectedExitIp: null,
    webRtcShieldEnabled: profile?.webRtcShieldEnabled === undefined
      ? fallbackState.webRtcShieldEnabled
      : Boolean(profile.webRtcShieldEnabled),
    webRtcCompatibility: profile?.webRtcCompatibility === undefined
      ? fallbackState.webRtcCompatibility
      : Boolean(profile.webRtcCompatibility),
    hardKillSwitch: profile?.hardKillSwitch === undefined
      ? fallbackState.hardKillSwitch
      : Boolean(profile.hardKillSwitch)
  };
}

function profileSettingsMatch(state, profile) {
  return state.port === profile.port &&
    state.profileName === profile.profileName &&
    state.strictLocal === profile.strictLocal &&
    JSON.stringify(state.trustedExitIps) === JSON.stringify(profile.trustedExitIps) &&
    state.webRtcShieldEnabled === profile.webRtcShieldEnabled &&
    state.webRtcCompatibility === profile.webRtcCompatibility &&
    state.hardKillSwitch === profile.hardKillSwitch;
}

async function enableProxy(profile, showNotification = true) {
  operationGeneration += 1;
  const previousState = await readState();
  const selectedProfile = normalizeProfile(profile, previousState);
  let state = await writeState({
    ...selectedProfile,
    enabled: true,
    status: STATUS.CHECKING,
    tunRestoreLocalProxy: false,
    consecutiveFailures: 0,
    lastError: null,
    exitIp: null,
    latencyMs: null,
    protectionsApplied: false,
    lastCheckSource: null,
    traceLocation: null,
    traceColo: null,
    proxyConfigVerified: false,
    webRtcProtected: false,
    shieldState: selectedProfile.webRtcShieldEnabled ? "compromised" : "off"
  });

  await appendEvent("action", `Прокси включается: ${state.profileName}, ${CONFIG.PROXY_HOST}:${state.port}.`);

  // Fail closed before touching proxy/privacy settings, so activation and profile changes have no DIRECT window.
  if (state.hardKillSwitch) {
    await setKillSwitch(true, "Активация: маршрут ещё не подтверждён VERIFIED.");
  }
  else {
    await setKillSwitch(false, null);
  }

  try {
    const proxyResult = await ensureStrictProxy(state);
    if (!proxyResult.ok) {
      return setBlockedState(proxyResult.message, showNotification, proxyResult);
    }

    const protectionResult = await ensurePrivacyProtections(state);
    state = await applyProtectionDiagnostics(state, proxyResult, protectionResult);
  }
  catch (error) {
    return failActivation(error, showNotification);
  }

  scheduleHealthAlarm(state);
  return checkConnection({ showChecking: false, notifyTransitions: showNotification });
}

async function disableProxy() {
  operationGeneration += 1;
  await clearHealthAlarm();
  cancelProxyErrorTimer();
  cancelLiveGuardTimer();
  await writeState({ enabled: false, status: STATUS.OFF });
  const errors = [];

  try {
    await clearOwnProxySetting();
  }
  catch (error) {
    errors.push(`Не удалось освободить прокси: ${errorMessage(error)}`);
  }

  try {
    await releasePrivacyProtections();
  }
  catch (error) {
    errors.push(`Не удалось восстановить настройки приватности: ${errorMessage(error)}`);
  }

  try {
    await setKillSwitch(false, null);
  }
  catch (error) {
    errors.push(`Не удалось снять Hard Kill Switch: ${errorMessage(error)}`);
  }

  const patch = {
    enabled: false,
    status: STATUS.OFF,
    tunRestoreLocalProxy: false,
    consecutiveFailures: 0,
    lastError: errors.length > 0 ? errors.join(" ") : null,
    exitIp: null,
    latencyMs: null,
    protectionsApplied: false,
    lastCheckSource: null,
    traceLocation: null,
    traceColo: null,
    proxyConfigVerified: false,
    webRtcProtected: false,
    predictionDisabled: false,
    shieldState: "off",
    webRtcPolicy: null,
    webRtcLevelOfControl: null,
    predictionLevelOfControl: null,
    proxyControl: null,
    killSwitchEngaged: false,
    killSwitchReason: null
  };

  const state = await writeState(patch);
  await appendEvent("action", errors.length > 0 ? `Прокси выключен с предупреждением: ${errors.join(" ")}` : "Прокси и Privacy Shield выключены.");
  return readState();
}

async function toggleProxy(profile) {
  const state = await readState();
  if (!state.enabled && state.managerBridgeConnected && state.managerMode === "tun") {
    await appendEvent("manager", "TUN Monitor: browser SOCKS5 не включён, потому что активен системный TUN GeniaProxy.");
    requestTunMonitor({ force: true }).catch(error => console.warn("Manual TUN monitor check failed:", error));
    return readState();
  }
  return state.enabled ? disableProxy() : enableProxy(profile, true);
}

async function updateProfile(profile) {
  const previousState = await readState();
  const selectedProfile = normalizeProfile(profile, previousState);

  if (!previousState.enabled) {
    const state = await writeState(selectedProfile);
    if (!state.hardKillSwitch) {
      await setKillSwitch(false, null);
    }
    return readState();
  }

  // Applying an unchanged profile must not restart an already verified proxy.
  // This also prevents duplicate activation events when the user presses Apply twice.
  if (profileSettingsMatch(previousState, selectedProfile)) {
    scheduleHealthAlarm(previousState);
    const checkedRecently = previousState.lastCheckAt &&
      Date.now() - previousState.lastCheckAt < CONFIG.RECENT_CHECK_MS;
    return checkedRecently
      ? previousState
      : checkConnection({ showChecking: true, notifyTransitions: false });
  }

  operationGeneration += 1;
  return enableProxy(selectedProfile, false);
}

async function runLiveGuard(trigger) {
  let state = await readState();
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  if (!state.enabled && !tunMonitor) {
    return state;
  }

  const previousShield = state.shieldState;
  try {
    let proxyResult = { ok: true, repaired: false, proxyControl: null, proxyConfigVerified: false };
    if (!tunMonitor) {
      proxyResult = await ensureStrictProxy(state);
      if (!proxyResult.ok) {
        return setBlockedState(proxyResult.message, true, proxyResult);
      }
    }
    const protectionResult = await ensurePrivacyProtections(state);
    if (tunMonitor) {
      state = await writeState({
        webRtcProtected: protectionResult.webRtcProtected,
        predictionDisabled: protectionResult.predictionDisabled,
        webRtcPolicy: protectionResult.webRtcPolicy,
        webRtcLevelOfControl: protectionResult.webRtcLevelOfControl,
        predictionLevelOfControl: protectionResult.predictionLevelOfControl,
        shieldState: protectionResult.shieldState,
        protectionsApplied: true
      });
    }
    else {
      state = await applyProtectionDiagnostics(state, proxyResult, protectionResult);
    }

    const repaired = proxyResult.repaired || protectionResult.repaired;
    state = await writeState({
      lastGuardAt: Date.now(),
      lastGuardEvent: repaired ? `Восстановлено после события: ${trigger}` : `Проверено: ${trigger}`
    });

    if (repaired || previousShield === "compromised" || [STATUS.BLOCKED, STATUS.ERROR].includes(state.status)) {
      await appendEvent("guard", repaired
        ? `Live Guard восстановил защиту после события: ${trigger}.`
        : `Live Guard перепроверил защиту после события: ${trigger}.`);
      if (tunMonitor) {
        requestTunMonitor({ force: true }).catch(error => console.warn("Live Guard TUN monitor check failed:", error));
        return readState();
      }
      return checkConnection({ showChecking: false, notifyTransitions: true });
    }

    return state;
  }
  catch (error) {
    return failActivation(new Error(`Live Guard: ${errorMessage(error)}`), true);
  }
}

function liveGuardSignalIsCompromised(kind, details, state) {
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  if (!state.enabled && !tunMonitor) {
    return false;
  }

  if (kind === "proxy") {
    if (tunMonitor) return false;
    return details?.levelOfControl !== "controlled_by_this_extension" || !proxyConfigMatches(details, state);
  }

  if (kind === "webrtc") {
    if (!state.webRtcShieldEnabled) {
      return false;
    }
    return details?.levelOfControl !== "controlled_by_this_extension" || details?.value !== desiredWebRtcPolicy(state);
  }

  if (kind === "prediction") {
    return details?.levelOfControl !== "controlled_by_this_extension" || details?.value !== false;
  }

  return false;
}

async function flagLiveGuardCompromise(kind, details, trigger) {
  const state = await readState();
  if (!liveGuardSignalIsCompromised(kind, details, state)) {
    return state;
  }

  const message = `Live Guard обнаружил изменение защищённой настройки: ${trigger}.`;
  if (state.hardKillSwitch) {
    await setKillSwitch(true, message);
  }

  await writeState({
    status: STATUS.BLOCKED,
    shieldState: state.webRtcShieldEnabled ? "compromised" : "off",
    protectionsApplied: false,
    lastError: message,
    lastGuardAt: Date.now(),
    lastGuardEvent: `Обнаружено изменение: ${trigger}`
  });
  await appendEvent("compromised", message);
  return readState();
}

function scheduleLiveGuard(trigger) {
  cancelLiveGuardTimer();
  liveGuardTimer = setTimeout(() => {
    liveGuardTimer = null;
    enqueue(() => runLiveGuard(trigger));
  }, CONFIG.LIVE_GUARD_DEBOUNCE_MS);
}

async function recordSelfTest(payload) {
  const state = await readState();
  const proxyDetails = await getProxySettings();
  const webRtcDetails = await getChromeSetting(chrome.privacy.network.webRTCIPHandlingPolicy);
  const predictionDetails = await getChromeSetting(chrome.privacy.network.networkPredictionEnabled);
  const targetWebRtc = desiredWebRtcPolicy(state);
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  const proxyOk = state.enabled && proxyConfigMatches(proxyDetails, state) && proxyDetails.levelOfControl === "controlled_by_this_extension";
  const browserProxyReleased = !proxyConfigMatches(proxyDetails, state) &&
    proxyDetails.levelOfControl !== "controlled_by_other_extensions" &&
    proxyDetails.levelOfControl !== "not_controllable";
  const tunRouteVerified = tunMonitor && state.managerMonitorStatus === "verified";
  const webRtcOk = !state.webRtcShieldEnabled || (
    webRtcDetails.value === targetWebRtc &&
    webRtcDetails.levelOfControl === "controlled_by_this_extension"
  );
  const predictionOk = predictionDetails.value === false && predictionDetails.levelOfControl === "controlled_by_this_extension";

  const clampCount = value => Math.max(0, Math.min(100, Math.floor(Number(value) || 0)));
  const iceCandidates = clampCount(payload?.iceCandidates);
  const mdnsCandidates = clampCount(payload?.mdnsCandidates);
  const localRawCandidates = clampCount(payload?.localRawCandidates);
  const publicRawCandidates = clampCount(payload?.publicRawCandidates);
  const safeSpecialCandidates = clampCount(payload?.safeSpecialCandidates);
  const hostUdpCandidates = clampCount(payload?.hostUdpCandidates);
  const hostTcpCandidates = clampCount(payload?.hostTcpCandidates);
  const unknownCandidates = clampCount(payload?.unknownCandidates);
  // ICE gathered inside chrome-extension:// is diagnostic only. It is NOT a website leak verdict:
  // extension pages may observe host candidates that are not exposed to ordinary web origins.
  const rawIpExposed = localRawCandidates > 0 || publicRawCandidates > 0;

  let status = "passed";
  let summary = "Privacy Shield policy, ownership, SOCKS5 и network prediction подтверждены.";

  if (tunMonitor) {
    if (!tunRouteVerified) {
      status = "warning";
      summary = "TUN Monitor активен, но системный маршрут ещё не подтверждён VERIFIED.";
    }
    else if (!browserProxyReleased || !webRtcOk || !predictionOk) {
      status = "failed";
      summary = "TUN VERIFIED, но browser proxy release или обязательные Privacy Shield настройки не подтверждены.";
    }
    else if (state.webRtcCompatibility) {
      status = "warning";
      summary = "TUN VERIFIED; Compatibility Mode активен и намеренно слабее Maximum protection.";
    }
    else if (!state.webRtcShieldEnabled) {
      status = "warning";
      summary = "TUN VERIFIED, но WebRTC Shield отключён вручную.";
    }
    else if (publicRawCandidates > 0) {
      status = "warning";
      summary = `TUN VERIFIED; extension-origin ICE увидел public=${publicRawCandidates}. Это не website leak verdict, но требует отдельной проверки.`;
    }
    else {
      const iceInfo = iceCandidates === 0
        ? "Extension-origin ICE: кандидатов нет."
        : `Extension-origin ICE (справочно): total=${iceCandidates}, mDNS=${mdnsCandidates}, local=${localRawCandidates}, public=${publicRawCandidates}, safe=${safeSpecialCandidates}, host UDP=${hostUdpCandidates}, host TCP=${hostTcpCandidates}, unknown=${unknownCandidates}.`;
      status = "passed";
      summary = `TUN Privacy Shield подтверждён: системный маршрут VERIFIED, browser SOCKS освобождён, public ICE не обнаружен. ${iceInfo}`;
    }
  }
  else if (!state.enabled) {
    status = "warning";
    summary = "Прокси выключен; проверка показывает только текущее состояние WebRTC в контексте расширения.";
  }
  else if (!proxyOk || !webRtcOk || !predictionOk) {
    status = "failed";
    summary = "Одна или несколько обязательных настроек Privacy Shield не подтверждены или не принадлежат этому расширению.";
  }
  else if (state.webRtcCompatibility) {
    status = "warning";
    summary = "Compatibility Mode активен: policy подтверждена, но этот режим намеренно слабее Maximum protection.";
  }
  else if (!state.webRtcShieldEnabled) {
    status = "warning";
    summary = "WebRTC Shield отключён вручную.";
  }
  else {
    const iceInfo = iceCandidates === 0
      ? "Extension-origin ICE: кандидатов нет."
      : `Extension-origin ICE (справочно, не leak verdict): total=${iceCandidates}, mDNS=${mdnsCandidates}, local=${localRawCandidates}, public=${publicRawCandidates}, safe=${safeSpecialCandidates}, host UDP=${hostUdpCandidates}, host TCP=${hostTcpCandidates}, unknown=${unknownCandidates}.`;
    summary = `Privacy Shield подтверждён. ${iceInfo}`;
  }

  await writeState({
    lastSelfTestAt: Date.now(),
    lastSelfTestStatus: status,
    lastSelfTestSummary: summary,
    lastSelfTestIceCandidates: iceCandidates,
    lastSelfTestRawIpExposed: rawIpExposed,
    lastSelfTestMdnsCandidates: mdnsCandidates,
    lastSelfTestLocalRawCandidates: localRawCandidates,
    lastSelfTestPublicRawCandidates: publicRawCandidates,
    lastSelfTestSafeSpecialCandidates: safeSpecialCandidates,
    lastSelfTestHostUdpCandidates: hostUdpCandidates,
    lastSelfTestHostTcpCandidates: hostTcpCandidates,
    lastSelfTestUnknownCandidates: unknownCandidates
  });
  await appendEvent("self-test", `WebRTC Shield Check: ${status}. ${summary}`);
  return readState();
}

async function runDailyAudit() {
  let state = await readState();
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  if (!state.enabled && !tunMonitor) throw new Error("Для Daily Audit сначала включите Genia Proxy Switcher или активируйте GeniaProxy TUN Monitor.");

  if (!tunMonitor) {
    state = await checkConnection({ showChecking: true, notifyTransitions: false });
  }
  const proxyDetails = await getProxySettings();
  const webRtcDetails = await getChromeSetting(chrome.privacy.network.webRTCIPHandlingPolicy);
  const predictionDetails = await getChromeSetting(chrome.privacy.network.networkPredictionEnabled);
  const killRules = await getKillSwitchRuleState();
  const trusted = normalizeTrustedExitIps(state.trustedExitIps);
  const observedExit = tunMonitor ? state.managerMonitorObservedExitIp : state.exitIp;
  const routeVerified = tunMonitor ? state.managerMonitorStatus === "verified" : state.status === STATUS.VERIFIED;
  const trustedMatch = Boolean(observedExit && trusted.includes(observedExit));
  const desiredWebRtc = desiredWebRtcPolicy(state);
  const browserProxyReleased = !proxyConfigMatches(proxyDetails, state) &&
    proxyDetails.levelOfControl !== "controlled_by_other_extensions" &&
    proxyDetails.levelOfControl !== "not_controllable";
  const checks = tunMonitor
    ? [
        ["TUN Route VERIFIED", routeVerified],
        ["Trusted Exit", trusted.length === 0 || trustedMatch],
        ["Browser proxy released", browserProxyReleased],
        ["Direct Bridge", state.managerBridgeConnected && state.managerMode === "tun"],
        ["WebRTC policy", !state.webRtcShieldEnabled || (webRtcDetails.value === desiredWebRtc && webRtcDetails.levelOfControl === "controlled_by_this_extension")],
        ["Prediction", predictionDetails.value === false && predictionDetails.levelOfControl === "controlled_by_this_extension"],
        ["Kill Switch state", routeVerified ? !killRules.anyPresent : killRules.allPresent],
        ["No proxy error", !state.lastProxyError],
        ["Failures zero", state.consecutiveFailures === 0],
        ["WebRTC Shield Check", state.lastSelfTestStatus === "passed" && state.lastSelfTestAt && Date.now() - state.lastSelfTestAt < 15 * 60 * 1000]
      ]
    : [
        ["Route VERIFIED", state.status === STATUS.VERIFIED],
        ["Trusted Exit", trusted.length === 0 || trustedMatch],
        ["Proxy ownership", proxyDetails.levelOfControl === "controlled_by_this_extension"],
        ["Proxy config", proxyConfigMatches(proxyDetails, state)],
        ["WebRTC policy", !state.webRtcShieldEnabled || (webRtcDetails.value === desiredWebRtc && webRtcDetails.levelOfControl === "controlled_by_this_extension")],
        ["Prediction", predictionDetails.value === false && predictionDetails.levelOfControl === "controlled_by_this_extension"],
        ["Kill Switch state", state.status === STATUS.VERIFIED ? !killRules.anyPresent : killRules.allPresent],
        ["No proxy error", !state.lastProxyError],
        ["Failures zero", state.consecutiveFailures === 0],
        ["WebRTC Shield Check", state.lastSelfTestStatus === "passed" && state.lastSelfTestAt && Date.now() - state.lastSelfTestAt < 15 * 60 * 1000]
      ];
  const score = checks.filter(([, ok]) => ok).length;
  const status = score === checks.length ? "pass" : score >= 8 ? "warn" : "fail";
  const failed = checks.filter(([, ok]) => !ok).map(([name]) => name);
  const summary = `${score}/10 ${status === "pass" ? "PASSED" : status === "warn" ? "WARNING" : "FAILED"}${failed.length ? ` · проверьте: ${failed.join(", ")}` : ""}`;
  const snapshot = {
    at: Date.now(),
    day: new Date().toISOString().slice(0, 10),
    status,
    score,
    exitIp: observedExit,
    trustedMatch,
    latencyMs: tunMonitor ? state.managerMonitorLatencyMs : state.latencyMs,
    failures: state.consecutiveFailures,
    webRtc: state.lastSelfTestStatus === "passed",
    killSwitch: state.hardKillSwitch,
    timezoneMatch: null,
    summary
  };
  const stored = await getStorage({ auditHistory: [] });
  let history = normalizeAuditHistory(stored.auditHistory);
  history = history.filter(item => item.day !== snapshot.day);
  history.push(snapshot);
  history = history.slice(-CONFIG.AUDIT_HISTORY_LIMIT);
  await setStorage({
    auditHistory: history,
    lastAuditAt: snapshot.at,
    lastAuditScore: score,
    lastAuditStatus: status,
    lastAuditSummary: summary
  });
  await appendEvent("audit", `Daily Security Audit${tunMonitor ? " [TUN]" : ""}: ${summary}${observedExit ? ` · ${observedExit}` : ""}.`);
  return readState();
}

async function selfRepairKillSwitch(state) {
  const actual = await getKillSwitchRuleState();
  const tunMonitor = state.managerBridgeConnected && state.managerMode === "tun" && !state.enabled;
  const tunNeedsGuard = tunMonitor && state.managerMonitorStatus !== "verified";
  const detachedTunNeedsGuard = !state.enabled && !state.managerBridgeConnected &&
    state.managerTransition === "to_tun" && state.managerMonitorStatus !== "verified";
  const localNeedsGuard = state.enabled && state.status !== STATUS.VERIFIED;
  const shouldEngage = state.hardKillSwitch && (localNeedsGuard || tunNeedsGuard || detachedTunNeedsGuard);
  const inconsistent = shouldEngage ? !actual.allPresent : actual.anyPresent;
  if (!inconsistent) return false;
  await setKillSwitch(shouldEngage, shouldEngage ? (state.lastError || "Self-repair: защита должна быть закрыта") : null, false);
  await appendEvent("self-repair", `Kill Switch self-repair: ${shouldEngage ? "восстановлен fail-closed guard" : "удалены stale DNR rules"}.`);
  return true;
}

async function reconcile() {
  operationGeneration += 1;
  await hardenStorageAccess();
  let state = await readState();
  await removeStorage(["failCount", "reconnecting"]);
  await clearLegacyHealthAlarms();

  // FIX3: TUN Monitor intentionally has enabled=false because browser proxy must be OFF.
  // Treat a persisted/ detached TUN context before the generic inactive branch. On
  // service-worker restart a historical TUN VERIFIED is never trusted: close the guard,
  // keep privacy protections, refresh Direct Bridge, then require a fresh TUN probe.
  const startupTunContext = !state.enabled && (
    (state.managerBridgeConnected && state.managerMode === "tun") ||
    state.managerTransition === "to_tun"
  );
  if (startupTunContext) {
    managerRouteGeneration += 1;
    await clearHealthAlarm();
    cancelProxyErrorTimer();
    cancelLiveGuardTimer();

    try {
      await clearOwnProxySetting();
    }
    catch (error) {
      console.warn("Could not keep browser proxy released for TUN startup reconciliation:", error);
    }

    try {
      const protectionResult = await ensurePrivacyProtections(state);
      await writeState({
        webRtcProtected: protectionResult.webRtcProtected,
        predictionDisabled: protectionResult.predictionDisabled,
        webRtcPolicy: protectionResult.webRtcPolicy,
        webRtcLevelOfControl: protectionResult.webRtcLevelOfControl,
        predictionLevelOfControl: protectionResult.predictionLevelOfControl,
        shieldState: protectionResult.shieldState,
        protectionsApplied: true
      });
    }
    catch (error) {
      console.warn("Could not restore TUN privacy settings during startup reconciliation:", error);
    }

    if (state.hardKillSwitch) {
      await setKillSwitch(true, "Startup TUN reconciliation: требуется свежее подтверждение системного маршрута.", false);
    }

    state = await writeState({
      enabled: false,
      status: STATUS.OFF,
      managerMonitorStatus: "checking",
      managerMonitorObservedExitIp: null,
      managerMonitorLastCheckAt: null,
      managerMonitorLatencyMs: null,
      managerMonitorSource: null,
      managerMonitorTraceLocation: null,
      managerMonitorTraceColo: null,
      managerCoherence: "unknown",
      managerTransition: "to_tun",
      managerTransitionStartedAt: Date.now(),
      lastStateTransitionAt: Date.now(),
      lastError: null,
      exitIp: null,
      latencyMs: null,
      proxyConfigVerified: false,
      proxyControl: null
    });

    if (state.managerBridgeEnabled && hasManagerBridgeApi()) {
      scheduleManagerHealthAlarm();
      requestManagerBridgeRefresh("startup-tun-reconcile", { requireFresh: true }).catch(() => {});
    }
    return state;
  }

  if (!state.enabled) {
    await clearHealthAlarm();
    cancelProxyErrorTimer();
    cancelLiveGuardTimer();

    try {
      await clearOwnProxySetting();
    }
    catch (error) {
      console.warn("Could not reconcile inactive proxy setting:", error);
    }

    try {
      await releasePrivacyProtections();
    }
    catch (error) {
      console.warn("Could not reconcile inactive privacy settings:", error);
    }

    try {
      await setKillSwitch(false, null, false);
    }
    catch (error) {
      console.warn("Could not reconcile kill switch:", error);
    }

    const inactiveState = await writeState({
      enabled: false,
      status: STATUS.OFF,
      consecutiveFailures: 0,
      lastError: null,
      exitIp: null,
      latencyMs: null,
      protectionsApplied: false,
      lastCheckSource: null,
      traceLocation: null,
      traceColo: null,
      proxyConfigVerified: false,
      webRtcProtected: false,
      predictionDisabled: false,
      shieldState: "off",
      proxyControl: null,
      killSwitchEngaged: false,
      killSwitchReason: null
    });
    if (inactiveState.managerBridgeEnabled && hasManagerBridgeApi()) {
      scheduleManagerHealthAlarm();
      requestManagerBridgeRefresh("startup-reconcile", { requireFresh: true }).catch(() => {});
    }
    return inactiveState;
  }

  // Fail closed after browser/service-worker restart: a stored VERIFIED result is historical.
  // Re-verify the live route before releasing persisted dynamic DNR rules.
  state = await writeState({ status: STATUS.CHECKING, lastError: null });
  if (state.hardKillSwitch) {
    await setKillSwitch(true, "Startup reconciliation: маршрут ещё не подтверждён VERIFIED.", false);
  }
  await selfRepairKillSwitch(await readState());
  scheduleHealthAlarm(state);
  const checked = await checkConnection({ showChecking: false, notifyTransitions: false });
  if (checked.managerBridgeEnabled && hasManagerBridgeApi()) {
    scheduleManagerHealthAlarm();
    requestManagerBridgeRefresh("startup-reconcile", { requireFresh: true }).catch(() => {});
  }
  return checked;
}

async function handleMessage(message, sender) {
  if (sender?.id !== chrome.runtime.id) {
    throw new Error("Команда отклонена: неизвестный отправитель.");
  }

  switch (message?.action) {
    case "getState": {
      let state = await readState();
      if (state.managerBridgeEnabled && state.managerBridgeConnected) {
        await updateManagerCoherence(state);
        state = await readState();
      }
      return state;
    }
    case "toggle":
      return toggleProxy(message.profile);
    case "setProfile":
      return updateProfile(message.profile);
    case "checkNow": {
      const state = await readState();
      if (!state.enabled) {
        if (state.managerBridgeConnected && state.managerMode === "tun") {
          requestTunMonitor({ force: true }).catch(error => console.warn("Explicit TUN monitor check failed:", error));
          return readState();
        }
        return state;
      }
      scheduleHealthAlarm(state);
      return checkConnection({ showChecking: true, notifyTransitions: false });
    }
    case "recordSelfTest":
      return recordSelfTest(message.result || {});
    case "runDailyAudit":
      return runDailyAudit();
    case "startTrustedExitTest":
      return startTrustedExitTest();
    case "cancelTrustedExitTest":
      return cancelTrustedExitTest();
    case "clearEventLog":
      return clearEventLog();
    case "managerConnect":
      return connectManagerBridge();
    case "managerRefresh":
      return refreshManagerBridge();
    case "managerDisconnect":
      return disconnectManagerBridge();
    default:
      throw new Error("Неизвестная команда расширения.");
  }
}

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const bridgeLaneAction = ["managerConnect", "managerRefresh", "managerDisconnect"].includes(message?.action);
  const request = bridgeLaneAction
    ? handleMessage(message, sender)
    : enqueue(() => handleMessage(message, sender));
  request
    .then(state => sendResponse({ ok: true, state }))
    .catch(error => {
      console.error("Background request error:", error);
      sendResponse({
        ok: false,
        error: errorMessage(error),
        state: error?.state || null
      });
    });
  return true;
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === CONFIG.HEALTH_ALARM) {
    enqueue(() => checkConnection({ notifyTransitions: true }));
  }
  else if (alarm.name === CONFIG.MANAGER_HEALTH_ALARM) {
    managerWatchdogTick().catch(error => console.warn("Direct Bridge watchdog failed:", error));
  }
});

async function handleProxyErrorState(errorRecord) {
  const { fatal, message, errorAt } = errorRecord;
  let state = await readState();
  const alreadyRecovered = state.enabled &&
    state.status === STATUS.VERIFIED &&
    state.lastCheckAt &&
    state.lastCheckAt >= errorAt;

  if (alreadyRecovered) {
    await appendProxyErrorEvent(fatal, message, "уже восстановлено VERIFIED");
    return state;
  }

  if (!state.enabled) {
    return state;
  }

  const guardAlreadyClosed = state.hardKillSwitch && (
    state.killSwitchEngaged ||
    [STATUS.CHECKING, STATUS.BLOCKED, STATUS.UNREACHABLE, STATUS.DEGRADED, STATUS.WRONG_EXIT, STATUS.ERROR].includes(state.status)
  );
  if (guardAlreadyClosed) {
    await appendProxyErrorEvent(fatal, message, "guard already closed; ждём VERIFIED");
    return state;
  }

  const transientAfterVerify = state.status === STATUS.VERIFIED &&
    state.lastCheckAt &&
    errorAt >= state.lastCheckAt &&
    errorAt - state.lastCheckAt <= CONFIG.POST_VERIFY_TRANSIENT_MS;
  if (transientAfterVerify) {
    return state;
  }

  await appendProxyErrorEvent(fatal, message);
  state = await writeState({
    lastProxyError: message,
    lastProxyErrorAt: errorAt,
    lastProxyFatal: fatal
  });

  if (state.hardKillSwitch) {
    const reason = `Chrome proxy error: ${message}. Ожидается повторная проверка VERIFIED.`;
    await setKillSwitch(true, reason);
    state = await writeState({
      status: STATUS.BLOCKED,
      lastError: reason
    });
  }
  return state;
}

function queueProxyErrorState(errorRecord) {
  if (!pendingProxyError || errorRecord.errorAt >= pendingProxyError.errorAt) {
    pendingProxyError = {
      ...errorRecord,
      fatal: Boolean(errorRecord.fatal || pendingProxyError?.fatal)
    };
  }
  else if (errorRecord.fatal) {
    pendingProxyError.fatal = true;
  }

  if (proxyErrorWorkPromise) return proxyErrorWorkPromise;
  proxyErrorWorkPromise = enqueue(async () => {
    while (pendingProxyError) {
      const current = pendingProxyError;
      pendingProxyError = null;
      await handleProxyErrorState(current);
    }
    return readState();
  }).catch(error => {
    console.warn("Could not persist proxy error:", error);
    return readState();
  }).finally(() => {
    proxyErrorWorkPromise = null;
    if (pendingProxyError) queueProxyErrorState(pendingProxyError);
  });
  return proxyErrorWorkPromise;
}

chrome.proxy.onProxyError.addListener(details => {
  const proxyError = [details.error, details.details].filter(Boolean).join(" — ").slice(0, 500);
  const message = proxyError || "Неизвестная ошибка прокси Chrome";
  const fatal = Boolean(details.fatal);
  const errorAt = Date.now();
  console.warn("Chrome proxy error:", proxyError, "fatal:", fatal);

  // Coalesce a burst into one queued security-state operation. Manager polling is
  // intentionally NOT queued: it runs in the dedicated single-in-flight Bridge lane.
  queueProxyErrorState({ fatal, message, errorAt });
  startManagerBridgeFastPolling("proxy-error");
  requestManagerBridgeRefresh("proxy-error", { requireFresh: true }).catch(() => {});

  // Throttle, do not debounce: a continuous error burst must not postpone verification forever.
  if (proxyErrorTimer === null) {
    proxyErrorTimer = setTimeout(() => {
      proxyErrorTimer = null;
      enqueue(async () => {
        const state = await readState();
        if (state.enabled) {
          return checkConnection({ notifyTransitions: true });
        }
        return state;
      });
    }, CONFIG.PROXY_ERROR_RECHECK_MS);
  }
});

chrome.proxy.settings.onChange.addListener(details => {
  const trigger = `proxy.settings.onChange (${details?.levelOfControl || "unknown"})`;
  enqueue(() => flagLiveGuardCompromise("proxy", details, trigger));
  scheduleLiveGuard(trigger);
});

chrome.privacy.network.webRTCIPHandlingPolicy.onChange.addListener(details => {
  const trigger = `WebRTC policy changed (${details?.value || "unknown"})`;
  enqueue(() => flagLiveGuardCompromise("webrtc", details, trigger));
  scheduleLiveGuard(trigger);
});

chrome.privacy.network.networkPredictionEnabled.onChange.addListener(details => {
  const trigger = `network prediction changed (${String(details?.value)})`;
  enqueue(() => flagLiveGuardCompromise("prediction", details, trigger));
  scheduleLiveGuard(trigger);
});

chrome.runtime.onStartup.addListener(() => enqueue(reconcile));
chrome.runtime.onInstalled.addListener(() => enqueue(reconcile));

hardenStorageAccess().catch(() => {});
scheduleActionRefresh(0);

console.log("Genia Proxy Switcher Direct 5.6.0 Stable service worker loaded");
