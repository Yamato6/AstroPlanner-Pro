// The page outside the app (no WebView2 host): it says so instead of waiting forever.
import { expect, test } from '@playwright/test';

test('without the app the top bar says it is not connected', async ({ page }) => {
  await page.goto('/planetarium.html');

  const bar = page.getByRole('banner', { name: 'Top bar' });
  await expect(bar).toContainText('Not connected to the app');
  await expect(bar.locator('time')).toHaveCount(0);
});
