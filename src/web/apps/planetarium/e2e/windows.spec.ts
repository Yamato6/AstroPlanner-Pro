// The window manager, the rail, Settings and Help against the mock host (spec §3.3, §12).
import { type Locator, type Page, expect, test } from '@playwright/test';
import { MockHost } from './mockHost';

let host: MockHost;

// The viewport is 1440 × 900; windows use x 64…1432, y 60…892 (below the top bar, right of the rail).
const area = { x: 64, y: 60, width: 1368, height: 832 };

test.beforeEach(async ({ page }) => {
  host = await MockHost.attach(page);
  await page.goto('/planetarium.html');
  await expect(page.getByRole('navigation', { name: 'Windows' })).toBeVisible();
});

test.afterEach(async () => {
  expect(host.rejected, 'every request matches its C# contract').toEqual([]);
  expect(await host.cspViolations(), 'no Content Security Policy violations').toEqual([]);
});

const rail = (page: Page, name: string): Locator =>
  page.getByRole('navigation', { name: 'Windows' }).getByRole('button', { name, exact: true });

const dialog = (page: Page, name: string): Locator => page.getByRole('dialog', { name });

async function box(locator: Locator) {
  const b = await locator.boundingBox();
  if (!b) {
    throw new Error('not visible');
  }
  return b;
}

/** Drags with the mouse from a point by (dx, dy), in steps like a hand. */
async function drag(page: Page, from: { x: number; y: number }, to: { x: number; y: number }) {
  await page.mouse.move(from.x, from.y);
  await page.mouse.down();
  await page.mouse.move(to.x, to.y, { steps: 8 });
  await page.mouse.up();
}

/** Waits until the layout the page saved has this window like this. */
async function expectSaved(id: string, expected: Record<string, unknown>) {
  await expect.poll(() => host.layout.windows.find((w) => w.id === id)).toMatchObject(expected);
}

test('a rail button opens its window in front, with the focus; Esc closes it', async ({ page }) => {
  await rail(page, 'Settings').click();

  const settings = dialog(page, 'Settings');
  await expect(settings).toBeVisible();
  await expect(settings).toBeFocused();
  await expect(rail(page, 'Settings')).toHaveAttribute('aria-pressed', 'true');
  expect(await box(settings)).toMatchObject({ x: 518, y: 164, width: 460, height: 520 });

  await page.keyboard.press('Escape');
  await expect(settings).toHaveCount(0);
  await expect(rail(page, 'Settings')).toHaveAttribute('aria-pressed', 'false');
});

test('the rail brings a window to the front, minimises the one in front and restores it', async ({
  page,
}) => {
  await rail(page, 'Settings').click();
  await rail(page, 'Help').click();
  const settings = dialog(page, 'Settings');

  await rail(page, 'Settings').click(); // behind Help: to the front
  await expect(settings).toHaveAttribute('data-arrangement', 'floating');
  expect(Number(await settings.evaluate((e) => getComputedStyle(e).zIndex))).toBeGreaterThan(
    Number(await dialog(page, 'Help').evaluate((e) => getComputedStyle(e).zIndex)),
  );

  await rail(page, 'Settings').click(); // in front: minimised
  await expect(settings).toHaveCount(0);
  await expect(rail(page, 'Settings (minimised)')).toBeVisible();

  await rail(page, 'Settings (minimised)').click();
  await expect(settings).toBeVisible();
  await expectSaved('settings', { isOpen: true, isMinimized: false });
});

test('dragging the title bar moves the window, and the place is saved', async ({ page }) => {
  await rail(page, 'Settings').click();
  const title = dialog(page, 'Settings').locator('header h2');
  const start = await box(title);

  await drag(page, { x: start.x + 20, y: start.y + 10 }, { x: start.x - 180, y: start.y + 110 });

  await expect.poll(async () => (await box(dialog(page, 'Settings'))).x).toBe(518 - 200);
  await expectSaved('settings', { arrangement: 'floating', x: 318, y: 264 });
});

test('dragging to a side edge docks the window as a half, to the top maximises it', async ({ page }) => {
  await rail(page, 'Help').click();
  const help = dialog(page, 'Help');
  let title = await box(help.locator('header h2'));

  await drag(page, { x: title.x + 20, y: title.y + 10 }, { x: area.x + area.width - 2, y: 400 });
  await expect(help).toHaveAttribute('data-arrangement', 'dockRight');
  expect(await box(help)).toMatchObject({
    x: area.x + area.width / 2,
    y: area.y,
    width: area.width / 2,
    height: area.height,
  });

  title = await box(help.locator('header h2'));
  await drag(page, { x: title.x + 20, y: title.y + 10 }, { x: 700, y: area.y + 2 });
  await expect(help).toHaveAttribute('data-arrangement', 'maximized');
  expect(await box(help)).toMatchObject({ x: area.x + 24, y: area.y + 24, width: area.width - 48 });

  await help.getByRole('button', { name: 'Restore' }).click();
  await expect(help).toHaveAttribute('data-arrangement', 'floating');
  await expectSaved('help', { arrangement: 'floating' });
});

test('the corner resizes the window, never below its minimum', async ({ page }) => {
  await rail(page, 'Settings').click();
  const settings = dialog(page, 'Settings');
  const corner = await box(settings.locator('[data-edge="se"]'));

  await drag(page, { x: corner.x + 6, y: corner.y + 6 }, { x: corner.x + 86, y: corner.y + 46 });
  await expect.poll(async () => (await box(settings)).width).toBe(540);
  expect((await box(settings)).height).toBe(560);

  const again = await box(settings.locator('[data-edge="se"]'));
  await drag(page, { x: again.x + 6, y: again.y + 6 }, { x: again.x - 600, y: again.y - 600 });
  await expect.poll(async () => (await box(settings)).width).toBe(340);
  expect((await box(settings)).height).toBe(260);
});

test('the focused window moves, resizes and docks with the keyboard', async ({ page }) => {
  await page.keyboard.press('Control+,');
  const settings = dialog(page, 'Settings');
  await expect(settings).toBeFocused();

  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async () => box(settings)).toMatchObject({ x: 534, y: 180, width: 476 });

  await page.keyboard.press('Control+ArrowLeft');
  await expect(settings).toHaveAttribute('data-arrangement', 'dockLeft');
  await page.keyboard.press('Control+ArrowDown');
  await expect(settings).toHaveAttribute('data-arrangement', 'floating');
});

test('F1, Ctrl+, Ctrl+Tab and Ctrl+W open, cycle and close windows', async ({ page }) => {
  await page.keyboard.press('F1');
  await page.keyboard.press('Control+,');
  await expect(dialog(page, 'Settings')).toBeFocused();

  await page.keyboard.press('Control+Tab');
  await expect(dialog(page, 'Help')).toBeFocused();
  await page.keyboard.press('Control+w');
  await expect(dialog(page, 'Help')).toHaveCount(0);
  await expect(dialog(page, 'Settings')).toBeFocused();
});

test('the layout comes back after a restart', async ({ page }) => {
  host.layout = MockHost.goldenLayout(); // Help docked right, Settings minimised
  await page.reload();

  const help = dialog(page, 'Help');
  await expect(help).toHaveAttribute('data-arrangement', 'dockRight');
  expect((await box(help)).width).toBe(400);
  await expect(dialog(page, 'Settings')).toHaveCount(0);
  await expect(rail(page, 'Settings (minimised)')).toBeVisible();
});

test('H hides the windows with the rest of the interface', async ({ page }) => {
  await rail(page, 'Help').click();
  await page.locator('body').click({ position: { x: 1300, y: 850 } }); // the sky
  await page.keyboard.press('h');
  await expect(dialog(page, 'Help')).toHaveCount(0);
  await page.keyboard.press('h');
  await expect(dialog(page, 'Help')).toBeVisible();
});

test('a window that has not moved here yet says where it is meanwhile', async ({ page }) => {
  await rail(page, 'Targets').click();

  await expect(dialog(page, 'Targets')).toContainText('Targets is coming to this interface');
  await expect(dialog(page, 'Targets')).toContainText('the classic window');
});

test('Settings switches the interface options and resets the layout', async ({ page }) => {
  await rail(page, 'Settings').click();
  const settings = dialog(page, 'Settings');

  await settings.getByText('Reduce transparency', { exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-transparency', 'reduced');
  await settings.getByText('Night vision', { exact: true }).click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'night');
  await settings.getByText('Full screen', { exact: true }).click();
  await expect(settings.getByRole('switch', { name: 'Full screen' })).toBeChecked();
  await expect(settings.getByRole('switch', { name: 'Inertia when dragging' })).toBeChecked();
  await settings.getByText('Inertia when dragging', { exact: true }).click();
  await expect(settings.getByRole('switch', { name: 'Inertia when dragging' })).not.toBeChecked();

  expect(host.calls('ui.setReduceTransparency')).toEqual([{ enabled: true }]);
  expect(host.calls('ui.setFullScreen')).toEqual([{ enabled: true }]);
  expect(host.calls('ui.setSkyInertia')).toEqual([{ enabled: false }]);

  // Twinkling needs High quality.
  await expect(settings.getByRole('switch', { name: 'Twinkling stars' })).toBeDisabled();
  await settings.getByRole('radio', { name: 'High' }).click();
  await expect(settings.getByRole('switch', { name: 'Twinkling stars' })).toBeEnabled();
  await settings.getByText('Twinkling stars', { exact: true }).click();
  await settings.getByRole('radio', { name: 'Sodium' }).click();
  expect(host.calls('ui.setSkyQuality')).toEqual([{ quality: 'high' }]);
  expect(host.calls('ui.setScintillation')).toEqual([{ enabled: true }]);
  expect(host.calls('ui.setLightPollutionTint')).toEqual([{ tint: 'sodium' }]);

  await rail(page, 'Help').click(); // opens over Settings
  await rail(page, 'Settings').click(); // Settings back in front
  await settings.getByRole('button', { name: 'Reset' }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect.poll(() => host.layout.windows).toEqual([]);
});

test("Settings › Landscape chooses the site's landscape and uses its horizon after a confirmation", async ({
  page,
}) => {
  await rail(page, 'Settings').click();
  const settings = dialog(page, 'Settings');
  const landscapes = settings.getByRole('radiogroup', { name: 'Around Home' });

  // The golden snapshot: Open field chosen for "Home", its horizon not in use yet.
  await expect(landscapes.getByRole('radio', { name: 'Open field' })).toBeChecked();
  await expect(landscapes).toContainText('Greg Zaal, Poly Haven · CC0 1.0');

  await landscapes.getByText('Mountains', { exact: true }).click();
  await landscapes.getByText('Horizon profile', { exact: true }).click();
  await landscapes.getByText('Flat ground', { exact: true }).click();
  expect(host.calls('landscape.set')).toEqual([
    { landscapeId: 'mountains' },
    { landscapeId: null },
    { landscapeId: 'flat' },
  ]);
  await expect(settings.getByRole('button', { name: 'Use…' })).toHaveCount(0);

  await landscapes.getByText('Mountains', { exact: true }).click();
  await settings.getByRole('button', { name: 'Use…' }).click();
  const confirm = settings.getByRole('group', { name: 'Use the landscape horizon for calculations' });
  await expect(confirm).toContainText('Replace the horizon of Home with the one of Mountains?');
  await confirm.getByRole('button', { name: 'Cancel' }).click();
  expect(host.calls('landscape.useHorizon')).toEqual([]);

  await settings.getByRole('button', { name: 'Use…' }).click();
  await confirm.getByRole('button', { name: 'Replace' }).click();
  expect(host.calls('landscape.useHorizon')).toEqual([{}]);
  await expect(settings).toContainText("The site's horizon already is this landscape's.");
  await expect(settings.getByRole('button', { name: 'Use…' })).toBeDisabled();
});

test('Settings › Survey chooses the survey, its contrast and the tile cache', async ({ page }) => {
  await rail(page, 'Settings').click();
  const settings = dialog(page, 'Settings');
  const surveys = settings.getByRole('radiogroup', { name: 'Sky photographs shown as you zoom in' });

  // The golden snapshot: DSS2 colour, realistic, 312 MB of a 1 GB cache.
  await expect(surveys.getByRole('radio', { name: 'DSS2 colour' })).toBeChecked();
  await expect(surveys).toContainText('Pan-STARRS1: STScI/MAST · HiPS by CDS');
  await expect(settings).toContainText('312 MB in use');

  await surveys.getByText('Pan-STARRS DR1 colour', { exact: true }).click();
  await surveys.getByText('None', { exact: true }).click();
  expect(host.calls('survey.set')).toEqual([{ surveyId: 'PanSTARRS-DR1-color' }, { surveyId: null }]);
  // Without a survey there is no contrast to choose.
  await expect(settings.getByRole('radio', { name: 'Survey', exact: true })).toBeDisabled();

  await surveys.getByText('DSS2 colour', { exact: true }).click();
  await settings.getByRole('radio', { name: 'Survey', exact: true }).click();
  expect(host.calls('survey.setMode')).toEqual([{ mode: 'survey' }]);

  await settings.getByRole('radio', { name: '5 GB' }).click();
  expect(host.calls('survey.setCacheLimit')).toEqual([{ cacheLimitMegabytes: 5120 }]);

  await settings.getByRole('button', { name: 'Clear' }).click();
  expect(host.calls('survey.clearCache')).toEqual([{}]);
  await expect(settings).toContainText('0 MB in use');
  await expect(settings.getByRole('button', { name: 'Clear' })).toBeDisabled();
});

test('Help lists every shortcut and shows About from the app', async ({ page }) => {
  await page.keyboard.press('F1');
  const help = dialog(page, 'Help');

  await expect(help.getByRole('tabpanel')).toContainText('Ctrl+Shift+N');
  await expect(help.getByRole('tabpanel')).toContainText('Night vision');
  await expect(help.getByRole('tabpanel')).toContainText('Dock it to the left or right half');

  await help.getByRole('tab', { name: 'About' }).click();
  await expect(help).toContainText('version 2.0.0');
  await expect(help).toContainText('Catalogs');
  await help.getByRole('link', { name: /Open-Meteo\.com/ }).click();
  await help.getByRole('button', { name: 'Open folder' }).click();

  await expect.poll(() => host.openedLinks).toEqual(['https://open-meteo.com/']);
  await expect.poll(() => host.calls('app.openDataFolder').length).toBe(1);
});
