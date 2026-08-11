import { chromium, webkit } from '@playwright/test';
import axe from 'axe-core';
import { readFile } from 'node:fs/promises';

const baseUrl = process.env.HPD_GATEWAY_STUDIO_E2E_URL;
const token = process.env.HPD_GATEWAY_STUDIO_E2E_TOKEN;
const targetNodeId = process.env.HPD_GATEWAY_STUDIO_E2E_TARGET ?? 'node-b';
const configurationPath = process.env.HPD_GATEWAY_STUDIO_E2E_CONFIGURATION;
if (!baseUrl || !token || !configurationPath) throw new Error('Gateway Studio browser evidence requires its URL, bearer token, and configuration fixture.');
const configurationText = await readFile(configurationPath, 'utf8');

let browser;
try {
  browser = await chromium.launch({ channel: 'chrome', headless: true });
} catch {
  try {
    browser = await chromium.launch({ headless: true });
  } catch {
    browser = await webkit.launch({ headless: true });
  }
}

const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
const page = await context.newPage();
const failures = [];
const gatewayResponses = [];
let submittedMutation = null;
let lastObservedNode = null;
let lastObservedEffective = null;
let expectingProtectedNotFound = false;
let expectingAmbiguousMutation = false;
const expectedBrowserHttpFailures = new Set();
page.on('console', message => {
  if (message.type() === 'error' &&
      !(expectingProtectedNotFound && message.text().includes('404 (Not Found)')) &&
      ![...expectedBrowserHttpFailures].some(status => message.text().includes(`${status} (`))) {
    failures.push(`console: ${message.text()}`);
  }
});
page.on('pageerror', error => failures.push(`page: ${error.message}`));
page.on('requestfailed', request => {
  if (!(expectingAmbiguousMutation && request.url().includes('/revisions:submitAndActivate')))
    failures.push(`network: ${request.url()} ${request.failure()?.errorText ?? ''}`);
});
page.on('request', request => {
  if (new URL(request.url()).origin !== new URL(baseUrl).origin) failures.push(`foreign origin: ${request.url()}`);
  if (request.method() === 'POST' && request.url().includes('/revisions:submitAndActivate')) {
    submittedMutation = { url: request.url(), body: request.postData(), headers: request.headers() };
  }
});
page.on('response', async response => {
  if (!response.url().includes('/management/gateway/v1/')) return;
  let body = '';
  try {
    const completeBody = await response.text();
    body = completeBody.slice(0, 2_000);
    if (response.url().endsWith('/status')) {
      const parsed = JSON.parse(completeBody);
      if (parsed.node !== null) lastObservedNode = structuredClone(parsed.node);
    } else if (response.url().endsWith('/effective')) {
      lastObservedEffective = structuredClone(JSON.parse(completeBody));
    }
  } catch { /* bounded diagnostic only */ }
  gatewayResponses.push({ status: response.status(), url: response.url(), body });
});

try {
  await page.goto(`${baseUrl}/studio/`, { waitUntil: 'networkidle' });
  await page.getByRole('heading', { name: 'Operational truth' }).waitFor();

  await page.keyboard.press('Tab');
  const focusVisible = await page.evaluate(() => {
    const active = document.activeElement;
    return active instanceof HTMLElement && active !== document.body && active.getClientRects().length > 0;
  });
  if (!focusVisible) throw new Error('Keyboard navigation did not produce visible focus.');

  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('dialog', { name: 'Sign in to Gateway Studio' }).waitFor();
  await page.getByRole('textbox', { name: 'Access token' }).fill(token);
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByText(/Signed in/).waitFor();

  await page.getByRole('textbox', { name: 'Namespace', exact: true }).fill('foreign');
  await page.getByRole('textbox', { name: 'Target', exact: true }).fill(targetNodeId);
  expectingProtectedNotFound = true;
  await page.getByRole('button', { name: 'Observe target' }).click();
  await page.getByRole('heading', { name: 'Target unavailable or not yet provisioned' }).waitFor();
  expectingProtectedNotFound = false;

  await page.getByRole('textbox', { name: 'Namespace', exact: true }).fill('namespace-a');
  await page.getByRole('textbox', { name: 'Target', exact: true }).fill(targetNodeId);
  await page.getByRole('button', { name: 'Observe target' }).click();
  try {
    await page.getByRole('heading', { name: 'Lifecycle' }).waitFor({ timeout: 10_000 });
    await page.getByRole('heading', { name: 'Applied Upstream discovery' }).waitFor();
  } catch (error) {
    throw new Error(`Lifecycle observation failed. Responses: ${JSON.stringify(gatewayResponses)} Visible page: ${await page.locator('body').innerText()}`, { cause: error });
  }

  for (const workspace of ['Configure', 'Operate', 'Diagnose']) {
    await page.getByRole('link', { name: workspace, exact: true }).first().click();
    await page.waitForURL(new RegExp(`#\\/gateway\\/${workspace.toLowerCase()}$`));
  }

  // Execute the real governed authoring path rather than treating route visibility as product evidence.
  await page.getByRole('link', { name: 'Configure', exact: true }).first().click();
  await page.getByRole('button', { name: 'Authored raw' }).click();
  await page.getByLabel('Exact authored JSON').fill(configurationText);
  const rawDiagnostics = await page.getByRole('alert').allTextContents();
  if (rawDiagnostics.length) throw new Error(`The released standalone candidate is not editor-compatible: ${JSON.stringify(rawDiagnostics)}`);
  await page.getByRole('button', { name: 'Visual', exact: true }).click();
  await page.getByRole('button', { name: '1', exact: true }).click();
  await page.getByRole('button', { name: '2', exact: true }).click();
  await page.getByRole('button', { name: '3', exact: true }).click();
  await page.getByRole('button', { name: '4', exact: true }).click();
  await page.getByRole('button', { name: '5', exact: true }).click();
  const quickCommit = page.getByRole('button', { name: 'Validate and atomically apply' });
  if (await quickCommit.isDisabled()) {
    const values = await page.locator('aside').filter({ hasText: 'Quick Route' }).locator('input').evaluateAll(inputs => inputs.map(input => ({ name: input.parentElement?.innerText, value: input.value })));
    throw new Error(`Quick Route proposal remained incomplete: ${JSON.stringify(values)} ${await page.locator('body').innerText()}`);
  }
  await quickCommit.click();
  try {
    await page.getByText('Validated Route, Upstream, and destination committed atomically.').waitFor();
  } catch (error) {
    throw new Error(`Quick Route did not commit. Responses: ${JSON.stringify(gatewayResponses)} Visible page: ${await page.locator('body').innerText()}`, { cause: error });
  }
  await page.getByRole('button', { name: 'Review submit and activate' }).click();
  await page.getByRole('link', { name: 'Operate', exact: true }).first().click();
  await page.getByRole('button', { name: 'Continue to explicit confirmation' }).click();
  let ambiguousMutationInjected = false;
  expectingAmbiguousMutation = true;
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/revisions:submitAndActivate', async route => {
    if (!ambiguousMutationInjected) {
      ambiguousMutationInjected = true;
      await route.fetch();
      await route.abort('connectionfailed');
      return;
    }
    await route.continue();
  });
  await page.getByRole('button', { name: 'Confirm submit-and-activate' }).click();
  await page.getByText('Outcome not observed').waitFor({ timeout: 15_000 });
  await page.getByRole('button', { name: 'Retry exact identified command' }).click();
  await page.getByText(/Accepted delivery · exact duplicate replay/).waitFor({ timeout: 15_000 });
  expectingAmbiguousMutation = false;
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/revisions:submitAndActivate');
  try {
    for (let attempt = 0; attempt < 30 && await page.getByText(/ActiveAcknowledged/).count() === 0; attempt++) {
      await page.getByRole('button', { name: 'Refresh activation history' }).click();
      await page.waitForTimeout(1_000);
    }
    await page.getByText(/ActiveAcknowledged/).first().waitFor({ timeout: 5_000 });
  } catch (error) {
    throw new Error(`Accepted browser mutation did not reach tracked acknowledgement. Responses: ${JSON.stringify(gatewayResponses)} Visible page: ${await page.locator('body').innerText()}`, { cause: error });
  }
  if (!submittedMutation?.body) throw new Error('The governed submit-and-activate request was not observed.');

  // Open a second real Studio workflow from current desired truth, then make its
  // reviewed CAS stale at the transport seam. The generated client and Studio
  // must retain and visibly project the server's closed 409 result.
  await page.getByRole('button', { name: 'Close', exact: true }).last().click();
  await page.getByRole('link', { name: 'Gateway', exact: true }).first().click();
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByRole('link', { name: 'Configure', exact: true }).first().click();
  await page.getByRole('button', { name: 'Review submit and activate' }).click();
  await page.getByRole('link', { name: 'Operate', exact: true }).first().click();
  await page.getByRole('button', { name: 'Continue to explicit confirmation' }).click();
  expectedBrowserHttpFailures.add(409);
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/revisions:submitAndActivate', async route => {
    await route.continue({ headers: { ...route.request().headers(), 'if-match': '"stale-desired-token"' } });
  });
  await page.getByRole('button', { name: 'Confirm submit-and-activate' }).click();
  await page.getByText('management.desired-token.conflict').waitFor({ timeout: 15_000 });
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/revisions:submitAndActivate');
  expectedBrowserHttpFailures.delete(409);

  // Exercise every closed discovery presentation state through the real generated
  // client and Svelte views. The fixture remains redacted and contains no endpoint
  // address because the product contract does not expose one.
  if (lastObservedNode === null || lastObservedEffective === null) throw new Error('Applied runtime evidence was not retained for discovery projection.');
  const discoveryStates = [
    ['fresh', 'AppliedFresh', 'fresh', 2],
    ['empty', 'AppliedFreshEmpty', 'fresh', 0],
    ['degraded', 'AppliedLastKnownDegraded', 'lastKnownMembership', 1],
    ['unavailable', 'AppliedUnavailable', 'unavailableWhenStale', 0],
    ['failed', 'RefreshFailed', 'refreshFailed', 1],
    ['resolving', 'Resolving', null, 0],
    ['indeterminate', 'Indeterminate', null, 0],
    ['not-observed', 'NotObserved', null, 0],
  ];
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/status', async route => {
    const response = await route.fetch();
    const body = await response.json();
    body.node = structuredClone(lastObservedNode);
    body.node.upstreams = discoveryStates.map(([id, state, _disposition, count], index) => discoveryNative(id, state, count, index, body.node.publication.stamp));
    await route.fulfill({ response, json: body });
  });
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/effective', async route => {
    const response = await route.fetch();
    const body = structuredClone(lastObservedEffective);
    body.upstreams = discoveryStates.filter(value => value[2] !== null).map(([id, _state, disposition, count], index) => discoveryApplied(id, disposition, count, index));
    await route.fulfill({ response, json: body });
  });
  await page.getByRole('link', { name: 'Gateway', exact: true }).first().click();
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  for (const state of discoveryStates.map(value => value[1])) await page.getByText(state, { exact: true }).first().waitFor();
  await page.getByRole('link', { name: 'Diagnose', exact: true }).first().click();
  await page.getByText('Aligned', { exact: true }).first().waitFor();
  await page.getByText('Incomplete', { exact: true }).first().waitFor();
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/status');
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/effective');

  await page.getByRole('link', { name: 'Diagnose', exact: true }).first().click();
  await page.getByRole('heading', { name: 'Discovery and native membership' }).waitFor();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Download safe diagnostic observation' }).click();
  await download;

  // Project the two closed host/publication failure states over an otherwise real,
  // authenticated status response. This verifies the browser projection without
  // introducing a test-only mutation endpoint into the standalone product.
  let statusProjection = 'publication-indeterminate';
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/status', async route => {
    const response = await route.fetch();
    const body = await response.json();
    if (body.node === null) {
      if (lastObservedNode === null) throw new Error('No real node observation is available for controlled status projection.');
      body.node = structuredClone(lastObservedNode);
    }
    if (statusProjection === 'publication-indeterminate') {
      body.node.publication.state = 'PublicationIndeterminate';
    } else if (statusProjection === 'restart-required') {
      body.node.host.state = 'RestartRequired';
    }
    await route.fulfill({ response, json: body });
  });
  await page.getByRole('link', { name: 'Gateway', exact: true }).first().click();
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByText('Publication is indeterminate. Serving truth remains unknown until a correlated acknowledgement or recovery is observed.').waitFor();
  statusProjection = 'restart-required';
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByText('The Gateway host reports RestartRequired. A dynamic candidate activation cannot satisfy the pending host change.').waitFor();
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/status');

  // Exercise authorization through the generated client and visible Studio
  // state. A 403 preserves the authenticated session but denies the operation;
  // a 401 invalidates the memory-only session and clears authorized truth.
  let authorizationStatus = 403;
  expectedBrowserHttpFailures.add(403);
  await page.route('**/management/gateway/v1/namespaces/*/targets/*/status', async route => {
    await route.fulfill({
      status: authorizationStatus,
      contentType: 'application/json',
      body: JSON.stringify({ code: authorizationStatus === 401 ? 'gateway.admin.authentication.required' : 'gateway.admin.authorization.denied', title: 'Gateway request denied.', correlationId: null }),
    });
  });
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByRole('heading', { name: 'Gateway operation access denied' }).waitFor();
  expectedBrowserHttpFailures.delete(403);
  expectedBrowserHttpFailures.add(401);
  authorizationStatus = 401;
  await page.getByRole('button', { name: 'Refresh', exact: true }).click();
  await page.getByRole('heading', { name: 'Authentication required' }).waitFor();
  expectedBrowserHttpFailures.delete(401);
  await page.unroute('**/management/gateway/v1/namespaces/*/targets/*/status');

  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('dialog', { name: 'Sign in to Gateway Studio' }).waitFor();
  await page.getByRole('textbox', { name: 'Access token' }).fill(token);
  await page.getByRole('button', { name: 'Continue' }).click();
  await page.getByText(/Signed in/).waitFor();

  await page.evaluate(source => {
    // Test-only accessibility analysis; it does not become part of the product bundle.
    (0, eval)(source);
  }, axe.source);
  const accessibility = await page.evaluate(async () => {
    const result = await globalThis.axe.run(document, { resultTypes: ['violations'] });
    return result.violations.filter(item => item.impact === 'serious' || item.impact === 'critical');
  });
  if (accessibility.length) throw new Error(`Accessibility violations: ${JSON.stringify(accessibility)}`);

  await page.setViewportSize({ width: 390, height: 844 });
  const narrowOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  if (narrowOverflow) throw new Error('The narrow Studio layout overflows horizontally.');

  await page.setViewportSize({ width: 720, height: 900 });
  await page.evaluate(() => { document.documentElement.style.zoom = '2'; });
  const zoomOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  if (zoomOverflow) throw new Error('The 200-percent Studio layout overflows horizontally.');

  await page.getByRole('button', { name: 'Sign out' }).click();
  await page.getByText('Signed out', { exact: true }).waitFor();
  await page.getByRole('link', { name: 'Gateway', exact: true }).first().click();
  await page.getByRole('heading', { name: 'Authentication required' }).waitFor();
  if (failures.length) throw new Error(failures.join('\n'));
  console.log('HPD Gateway Studio real-browser evidence passed');
} finally {
  await browser.close();
}

function discoveryNative(id, state, count, index, stamp) {
  const value = `${String(index + 1).padStart(64, '0')}`;
  return {
    upstreamId: `browser-${id}`, allDestinationCount: count, availableDestinationCount: count,
    activeHealthyCount: 0, activeUnhealthyCount: 0, activeUnknownCount: count,
    passiveHealthyCount: 0, passiveUnhealthyCount: 0, passiveUnknownCount: count,
    eligibility: count > 0 ? 'EligibleDestinationsPresent' : 'NoEligibleDestinations',
    availabilityPolicy: 'HealthyOrUnknown', countsTruncated: false, reasons: [], stamp,
    discovery: {
      state, profile: 'aspire', service: `browser-${id}`, endpoint: 'https',
      membershipGeneration: String(index + 1), membershipIdentity: { algorithm: 'sha-256', value },
      appliedDestinationCount: count, appliedAt: '2026-08-09T00:00:00Z', safeDiagnostic: `browser-${id}-diagnostic`,
    },
  };
}

function discoveryApplied(id, disposition, count, index) {
  return {
    upstreamId: `browser-${id}`, kind: 'serviceDiscovery', discoveryProfile: 'aspire', service: `browser-${id}`,
    endpoint: 'https', membershipGeneration: String(index + 1),
    membershipIdentity: { algorithm: 'sha-256', value: `${String(index + 1).padStart(64, '0')}` },
    destinationCount: count, disposition, safeDiagnostic: `browser-${id}-diagnostic`,
  };
}
