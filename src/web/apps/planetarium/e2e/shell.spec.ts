// The planetarium shell against the mock host (spec §12): real page, real bundle, fake C#.
import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { MockHost } from './mockHost';

let host: MockHost;

test.beforeEach(async ({ page }) => {
  host = await MockHost.attach(page);
  await page.goto('/planetarium.html');
  await expect(page.getByRole('banner', { name: 'Top bar' })).toBeVisible();
});

test.afterEach(async () => {
  expect(host.rejected, 'every request matches its C# contract').toEqual([]);
  expect(await host.cspViolations(), 'no Content Security Policy violations').toEqual([]);
});

test('the top bar shows the site, tonight and the sky time', async ({ page }) => {
  const bar = page.getByRole('banner', { name: 'Top bar' });

  await expect(bar).toContainText('Aguascalientes');
  await expect(bar).toContainText('Astronomical night 19:39–05:58');
  // 2026-01-16 04:00 UTC is 22:00 in Mexico City.
  await expect(bar.locator('time')).toHaveText('22:00');
  await expect(bar).toContainText('Paused');
});

test('Space plays and pauses time; J and L change its speed', async ({ page }) => {
  const bar = page.getByRole('banner', { name: 'Top bar' });

  await page.keyboard.press('Space');
  await expect(bar).toContainText('Playing ×60');
  await page.keyboard.press('l');
  await expect(bar).toContainText('Playing ×600');
  await page.keyboard.press('j');
  await page.keyboard.press('j');
  await expect(bar).toContainText('Playing ×1');
  await page.keyboard.press('Space');
  await expect(bar).toContainText('Paused');

  expect(host.calls('sky.setRate')).toEqual([{ rate: 600 }, { rate: 60 }, { rate: 1 }]);
});

test('layer and view-mode keys go to the host', async ({ page }) => {
  await page.keyboard.press('c'); // constellation lines: off in the golden snapshot
  await page.keyboard.press('2');
  await page.keyboard.press('F5');

  await expect.poll(() => host.calls('sky.now').length).toBe(1);
  expect(host.calls('sky.setLayer')).toEqual([{ key: 'constellationLines', enabled: true }]);
  expect(host.calls('sky.setViewMode')).toEqual([{ mode: 'allSky' }]);
});

test('H hides the interface and brings it back', async ({ page }) => {
  await page.keyboard.press('h');
  await expect(page.getByRole('banner')).toHaveCount(0);
  await page.keyboard.press('h');
  await expect(page.getByRole('banner')).toBeVisible();
});

test('changes made in the app reach the page', async ({ page }) => {
  host.sky = { ...host.sky, isPlaying: true, rate: 3600, revision: host.sky.revision + 1 };
  await host.emit('sky.changed', { revision: host.sky.revision });
  await expect(page.getByRole('banner')).toContainText('Playing ×3600');

  host.observer = {
    ...host.observer,
    locationName: 'San Pedro Mártir',
    revision: host.observer.revision + 1,
  };
  await host.emit('observer.changed', { revision: host.observer.revision });
  await expect(page.getByRole('banner')).toContainText('San Pedro Mártir');
});

test('night vision recolours the shell', async ({ page }) => {
  await host.emit('theme.changed', { theme: 'night' });
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'night');
});

for (const theme of ['dark', 'night'] as const) {
  test(`no accessibility violations (${theme} theme)`, async ({ page }) => {
    if (theme === 'night') {
      await host.emit('theme.changed', { theme: 'night' });
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'night');
    }
    await expect(page.getByRole('banner')).toContainText('Aguascalientes');

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();

    expect(
      results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
    ).toEqual([]);
  });
}
