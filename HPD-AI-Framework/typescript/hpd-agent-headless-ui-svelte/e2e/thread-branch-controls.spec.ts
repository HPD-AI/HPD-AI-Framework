import { expect, test } from '@playwright/test';

test('message fork controls stay keyed and path-scoped while switching branches', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByTestId('selected-thread')).toHaveText('fork-a');
  await expect(page.getByTestId('label-main@m2')).toHaveText('Fork 2 / 3');
  await expect(page.getByTestId('label-fork-a@m4')).toHaveText('Source (1 / 2)');
  await expect(page.getByTestId('message-m4')).toBeVisible();

  await page.getByTestId('next-main@m2').click();

  await expect(page.getByTestId('selected-thread')).toHaveText('fork-b');
  await expect(page.getByTestId('selection-count')).toHaveText('1');
  await expect(page.getByTestId('label-main@m2')).toHaveText('Fork 3 / 3');
  await expect(page.getByTestId('fork-control-fork-a@m4')).toHaveCount(0);
  await expect(page.getByTestId('message-m4')).toHaveCount(0);
  await expect(page.getByTestId('message-m3b')).toBeVisible();

  await page.getByTestId('previous-main@m2').click();

  await expect(page.getByTestId('selected-thread')).toHaveText('fork-a');
  await expect(page.getByTestId('selection-count')).toHaveText('2');
  await expect(page.getByTestId('label-main@m2')).toHaveText('Fork 2 / 3');
  await expect(page.getByTestId('label-fork-a@m4')).toHaveText('Source (1 / 2)');
  await expect(page.getByTestId('message-m4')).toBeVisible();
});

test('nested controls keep their own handlers after top-level rehydration', async ({ page }) => {
  await page.goto('/');

  await page.getByTestId('next-fork-a@m4').click();

  await expect(page.getByTestId('selected-thread')).toHaveText('fork-a-retry');
  await expect(page.getByTestId('selection-count')).toHaveText('1');
  await expect(page.getByTestId('label-main@m2')).toHaveText('Fork 2 / 3');
  await expect(page.getByTestId('label-fork-a@m4')).toHaveText('Fork 2 / 2');
  await expect(page.getByTestId('message-m5')).toBeVisible();

  await page.getByTestId('previous-fork-a@m4').click();

  await expect(page.getByTestId('selected-thread')).toHaveText('fork-a');
  await expect(page.getByTestId('selection-count')).toHaveText('2');
  await expect(page.getByTestId('label-fork-a@m4')).toHaveText('Source (1 / 2)');
  await expect(page.getByTestId('message-m5')).toHaveCount(0);
});
