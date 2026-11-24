// While the host has not answered yet, the top bar says it is connecting.
import { expect, test } from '@playwright/test';
import { MockHost } from './mockHost';

test('the top bar shows that it is connecting until the host answers', async ({ page }) => {
  const host = await MockHost.attach(page);
  host.hold();
  await page.goto('/planetarium.html');

  const bar = page.getByRole('banner', { name: 'Top bar' });
  await expect(bar).toContainText('Connecting to the app…');
  await expect.poll(() => host.requests.length).toBeGreaterThan(1);

  await host.release();
  await expect(bar).toContainText('Aguascalientes');
  expect(host.rejected).toEqual([]);
});
