const WIRE_VERSION = "hpd.payments.api.v1";
const MAX_RESPONSE_BYTES = 16 * 1024;
const ROUTES = Object.freeze({
  health: "/hpd/payments/v1/health",
  manifest: "/hpd/payments/v1/manifest",
});
const EFFECT_STATES = new Set([
  "Prepared",
  "Dispatched",
  "PossibleDispatch",
  "Succeeded",
  "Failed",
  "Cancelled",
]);

export class PaymentsBrowserProtocolError extends Error {
  constructor(code) {
    super(code);
    this.name = "PaymentsBrowserProtocolError";
    this.code = code;
  }
}

function requireBaseUrl(value) {
  const url = new URL(value);
  const localHttp = url.protocol === "http:" && (url.hostname === "127.0.0.1" || url.hostname === "localhost");
  if (url.protocol !== "https:" && !localHttp) throw new PaymentsBrowserProtocolError("payments.browser.baseUrlUnsupported");
  if (url.username || url.password || url.search || url.hash || (url.pathname !== "/" && url.pathname !== "")) {
    throw new PaymentsBrowserProtocolError("payments.browser.baseUrlInvalid");
  }
  return url.origin;
}

function requireExactObject(value, keys, code) {
  if (value === null || typeof value !== "object" || Array.isArray(value)) throw new PaymentsBrowserProtocolError(code);
  const actual = Object.keys(value).sort();
  const expected = [...keys].sort();
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) {
    throw new PaymentsBrowserProtocolError(code);
  }
  return value;
}

async function readJson(response) {
  if (!response.ok) throw new PaymentsBrowserProtocolError("payments.browser.httpFailure");
  const text = await response.text();
  if (new TextEncoder().encode(text).byteLength > MAX_RESPONSE_BYTES) {
    throw new PaymentsBrowserProtocolError("payments.browser.responseTooLarge");
  }
  try {
    return JSON.parse(text);
  } catch {
    throw new PaymentsBrowserProtocolError("payments.browser.invalidJson");
  }
}

function requireVersion(value) {
  if (value !== WIRE_VERSION) throw new PaymentsBrowserProtocolError("payments.browser.versionUnsupported");
}

export function decodeEvidence(value) {
  const body = requireExactObject(value, ["operationId", "state", "externalReference", "wireVersion"], "payments.browser.evidenceInvalid");
  if (typeof body.operationId !== "string" || body.operationId.length === 0 || body.operationId.length > 256) {
    throw new PaymentsBrowserProtocolError("payments.browser.evidenceInvalid");
  }
  if (typeof body.state !== "string" || !EFFECT_STATES.has(body.state)) {
    throw new PaymentsBrowserProtocolError("payments.browser.evidenceInvalid");
  }
  if (body.externalReference !== null && typeof body.externalReference !== "string") {
    throw new PaymentsBrowserProtocolError("payments.browser.evidenceInvalid");
  }
  requireVersion(body.wireVersion);
  return Object.freeze({
    operationId: body.operationId,
    state: body.state,
    externalReference: body.externalReference,
    wireVersion: body.wireVersion,
  });
}

export class PaymentsBrowserClient {
  constructor(options) {
    if (options === null || typeof options !== "object") throw new PaymentsBrowserProtocolError("payments.browser.optionsInvalid");
    this.baseUrl = requireBaseUrl(options.baseUrl);
    this.fetch = options.fetch ?? globalThis.fetch;
    if (typeof this.fetch !== "function") throw new PaymentsBrowserProtocolError("payments.browser.fetchUnavailable");
  }

  async #get(route, signal) {
    return readJson(await this.fetch(`${this.baseUrl}${route}`, {
      method: "GET",
      headers: Object.freeze({
        accept: "application/json",
        "x-hpd-payments-version": WIRE_VERSION,
      }),
      redirect: "error",
      credentials: "omit",
      cache: "no-store",
      signal,
    }));
  }

  async health(options = {}) {
    const body = requireExactObject(await this.#get(ROUTES.health, options.signal), ["status", "version"], "payments.browser.healthInvalid");
    if (body.status !== "ready") throw new PaymentsBrowserProtocolError("payments.browser.healthInvalid");
    requireVersion(body.version);
    return Object.freeze({ status: body.status, version: body.version });
  }

  async manifest(options = {}) {
    const body = requireExactObject(await this.#get(ROUTES.manifest, options.signal), ["version", "authorityLogic"], "payments.browser.manifestInvalid");
    requireVersion(body.version);
    if (body.authorityLogic !== false) throw new PaymentsBrowserProtocolError("payments.browser.authorityBoundaryInvalid");
    return Object.freeze({ version: body.version, authorityLogic: false });
  }
}

export const PaymentsApiWireVersion = WIRE_VERSION;
export const PaymentsApiRoutes = ROUTES;
