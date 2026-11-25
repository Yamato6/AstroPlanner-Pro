// The dock (darkness bar, time, layers, view), the object card, the command palette, toasts, the
// HUD and the idle toolbars against the mock host (spec §3.2, §9).
import AxeBuilder from '@axe-core/playwright';
import { type Page, expect, test } from '@playwright/test';
import { MockHost } from './mockHost';

let host: MockHost;

// The golden night starts at 18:30 local (00:30 UTC) and is sampled every 10 min; the sky shows 22:00.
const NIGHT_START_MS = 1768523400000;
const MINUTE_MS = 60_000;

test.beforeEach(async ({ page }) => {
  host = await MockHost.attach(page);
  await page.goto('/planetarium.html');
  await expect(page.getByRole('region', { name: 'Time and view' })).toBeVisible();
});

test.afterEach(async () => {
  expect(host.rejected, 'every request matches its C# contract').toEqual([]);
  expect(await host.cspViolations(), 'no Content Security Policy violations').toEqual([]);
});

const dock = (page: Page) => page.getByRole('region', { name: 'Time and view' });
const card = (page: Page) => page.getByRole('region', { name: 'Selected object' });
const scrubber = (page: Page) => dock(page).getByRole('slider', { name: 'Sky time' });

test('the darkness bar shows the sky time and moves it from the keyboard', async ({ page }) => {
  await expect(scrubber(page)).toHaveAttribute('aria-valuetext', 'Thu 15 Jan 22:00');

  await scrubber(page).focus();
  await page.keyboard.press('ArrowRight');

  await expect
    .poll(() => host.calls('sky.setTime'))
    .toEqual([{ utcMilliseconds: NIGHT_START_MS + 220 * MINUTE_MS }]);
  await expect(page.getByRole('banner')).toContainText('22:10');
});

test('dragging the darkness bar sets the time when released', async ({ page }) => {
  const track = await scrubber(page).locator('xpath=ancestor::*[@data-orientation][1]').boundingBox();
  if (!track) {
    throw new Error('no track');
  }
  await page.mouse.click(track.x + track.width * 0.25, track.y + 7);

  await expect.poll(() => host.calls('sky.setTime').length).toBe(1);
  const [{ utcMilliseconds }] = host.calls('sky.setTime') as [{ utcMilliseconds: number }];
  // A quarter of 790 min from the start, to the 10-minute step.
  expect(Math.abs(utcMilliseconds - (NIGHT_START_MS + 197 * MINUTE_MS))).toBeLessThanOrEqual(10 * MINUTE_MS);
});

test('the steps go back and forward 10 min, an hour with Shift and a day with Ctrl', async ({ page }) => {
  const forward = dock(page).getByRole('button', { name: /^Forward 10 min/ });

  await forward.click();
  await forward.click({ modifiers: ['Shift'] });
  await forward.click({ modifiers: ['Control'] });
  await dock(page)
    .getByRole('button', { name: /^Back 10 min/ })
    .click();

  await expect
    .poll(() => host.calls('sky.step'))
    .toEqual([{ minutes: 10 }, { minutes: 60 }, { minutes: 1440 }, { minutes: -10 }]);
});

test('a speed plays time at that rate; the view mode and the layers go to the host', async ({ page }) => {
  await dock(page).getByRole('radio', { name: '×600' }).click();
  await expect(page.getByRole('banner')).toContainText('Playing ×600');
  await dock(page).getByRole('button', { name: 'Pause time' }).click();
  await dock(page).getByRole('radio', { name: 'Finder' }).click();
  await dock(page).getByRole('button', { name: 'Constellation lines' }).click();

  expect(host.calls('sky.setRate')).toEqual([{ rate: 600 }]);
  await expect.poll(() => host.calls('sky.setViewMode')).toEqual([{ mode: 'finder' }]);
  await expect.poll(() => host.calls('sky.setLayer')).toEqual([{ key: 'constellationLines', enabled: true }]);
  await expect(dock(page).getByRole('radio', { name: 'Finder' })).toBeChecked();
});

test('the date and time field moves the sky when Enter is pressed', async ({ page }) => {
  const hour = dock(page).getByRole('spinbutton', { name: /^hour/ });
  await expect(dock(page).getByRole('group', { name: 'Sky date and time' })).toContainText('15/01/2026');

  await hour.click();
  await page.keyboard.type('23');
  await page.keyboard.press('Enter');

  // 23:00 in Mexico City is 05:00 UTC.
  await expect
    .poll(() => host.calls('sky.setTime'))
    .toEqual([{ utcMilliseconds: Date.UTC(2026, 0, 16, 5, 0) }]);
});

test('the card shows the selection and its feasibility, and acts on it', async ({ page }) => {
  await expect(card(page)).toContainText('M42 — Orion Nebula');
  await expect(card(page)).toContainText('Nebula · Orion · mag 4.0');
  await expect(card(page)).toContainText('Excellent');
  const reason = card(page).getByText('High in the sky for 6 h in darkness');
  await expect(reason).toBeHidden();

  await card(page).getByRole('button', { name: 'Why?' }).click();
  await expect(reason).toBeVisible();

  await card(page).getByRole('button', { name: 'Add to my plan' }).click();
  await expect(card(page).getByRole('button', { name: 'Remove from my plan' })).toBeVisible();
  await card(page).getByRole('button', { name: 'Center' }).click();
  await card(page).getByRole('button', { name: 'Frame with the camera' }).click();

  await expect.poll(() => host.calls('selection.frame').length).toBe(1);
  expect(host.calls('selection.addToPlan')).toHaveLength(1);
  expect(host.calls('selection.center')).toHaveLength(1);
});

test('Esc clears the selection; a selection made in the app reaches the card', async ({ page }) => {
  await page.locator('body').click({ position: { x: 900, y: 500 } });
  await page.keyboard.press('Escape');
  await expect(card(page)).toHaveCount(0);

  host.selection = {
    revision: host.selection.revision + 1,
    target: {
      id: null,
      title: 'Moon',
      typeName: null,
      constellation: null,
      magnitude: null,
      coordinates: null,
      altitudeDegrees: null,
      azimuthDegrees: null,
      transitLocal: null,
      maximumAltitudeDegrees: null,
      bestWindow: null,
      verdict: null,
      verdictText: null,
      score: null,
      recommended: null,
      reasons: [],
      isInPlan: false,
    },
  };
  await host.emit('selection.changed', { revision: host.selection.revision });
  await expect(card(page)).toContainText('Moon');
  await expect(card(page).getByRole('button', { name: 'Center' })).toHaveCount(0);
});

test('the palette finds an object and turns the view to it', async ({ page }) => {
  await page.keyboard.press('Control+k');
  const palette = page.getByRole('dialog', { name: 'Search objects and commands' });
  await expect(palette).toBeVisible();

  await page.keyboard.type('m43');
  await palette.getByRole('menuitem', { name: /M43 — De Mairan/ }).click();

  await expect(palette).toHaveCount(0);
  expect(host.calls('search.go')).toEqual([{ id: 'M43' }]);
  await expect(card(page)).toContainText('M43');
});

test('the palette searches online for a name the catalog lacks, and says what it found', async ({ page }) => {
  await page.keyboard.press('/');
  await page.keyboard.type('Sh2-155');
  await page.getByRole('menuitem', { name: 'Search online for “Sh2-155”' }).click();

  const notices = page.getByRole('region', { name: 'Notifications' });
  await expect(notices).toContainText('resolved by SIMBAD');
  await notices.getByRole('button', { name: 'Close' }).click();
  await expect(notices.getByRole('listitem')).toHaveCount(0);

  await page.keyboard.press('/');
  await page.keyboard.type('Nowhere 1');
  await page.getByRole('menuitem', { name: 'Search online for “Nowhere 1”' }).click();
  await expect(notices).toContainText('Nowhere 1 was not found.');
});

test('the palette runs commands and opens windows', async ({ page }) => {
  await page.keyboard.press('Control+k');
  await page.keyboard.type('night vis');
  await page.keyboard.press('Enter');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'night');

  await page.keyboard.press('Control+k');
  await page.keyboard.type('settings');
  await page.getByRole('menuitem', { name: 'Settings' }).first().click();
  await expect(page.getByRole('dialog', { name: 'Settings' })).toBeVisible();
});

test('the HUD shows the zenith sky quality and the star limit', async ({ page }) => {
  const hud = page.getByRole('group', { name: 'Sky under the pointer' });
  await expect(hud).toContainText('SQM20.40');
  await expect(hud).toContainText('Limit6.5');
});

test('idle toolbars fade after 3 s and come back with the pointer', async ({ page }) => {
  host.ui = { ...host.ui, autoHideToolbars: true, revision: host.ui.revision + 1 };
  await host.emit('ui.changed', { revision: host.ui.revision });
  await page.mouse.move(700, 400);

  await expect(dock(page)).toHaveAttribute('data-idle', 'true', { timeout: 6000 });
  await expect(page.getByRole('banner')).toHaveAttribute('data-idle', 'true');
  await page.mouse.move(720, 420);
  await expect(dock(page)).not.toHaveAttribute('data-idle');
});

for (const theme of ['dark', 'night'] as const) {
  test(`no accessibility violations with the card, the dock and the palette (${theme})`, async ({ page }) => {
    if (theme === 'night') {
      await page.keyboard.press('Control+Shift+N');
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'night');
    }
    await card(page).getByRole('button', { name: 'Why?' }).click();
    await page.keyboard.press('Control+k');
    await page.keyboard.type('m4');
    await expect(page.getByRole('menuitem', { name: /M42/ })).toBeVisible();

    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
      .analyze();

    expect(
      results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(' ')).join(', ')}`),
    ).toEqual([]);
  });
}
