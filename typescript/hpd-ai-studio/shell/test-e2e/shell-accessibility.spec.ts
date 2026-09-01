import { expect, test } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const axeSource = readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');

test('fails closed in an accessible shell when host authority is absent', async ({ page }) => {
  await page.route('**/control/shell', route => route.fulfill({ json: {
    shellContractChecksum: '0'.repeat(64), editionAssetGraphChecksum: '1'.repeat(64), runtimeClientChecksum: '2'.repeat(64),
    bootstrapRoute: '/control/bootstrap', sessionRoute: '/control/session', loginRoute: '/control/login', logoutRoute: '/control/logout',
    authentication: { kind: 'cookieBff', authorizationRoute: '/control/authorize', descriptorChecksum: '3'.repeat(64) },
    modules: [{ moduleId: 'base', moduleVersion: 1, entryModulePath: '/modules/base.js', assetGraphChecksum: '4'.repeat(64) }],
  } }));
  await page.route('**/control/session', route => route.fulfill({ status: 401 }));
  await page.route('http://127.0.0.1:4173/', async route => {
    const response = await route.fetch();
    await route.fulfill({ response, body: (await response.text()).replace('__HPD_STUDIO_BASE__/', '') });
  });
  await page.goto('/');
  await expect(page.locator('main')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sign in to continue' })).toBeVisible();
  await page.addScriptTag({ content: axeSource });
  const violations = await page.evaluate(async () => {
    const axe = (globalThis as typeof globalThis & { axe: { run(): Promise<{ violations: unknown[] }> } }).axe;
    return (await axe.run()).violations;
  });
  expect(violations).toEqual([]);
});
