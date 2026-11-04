// End-to-end test of the admin UI against a running stack (see tests/integration/run.py).
// It covers what component tests cannot: the real container image, static assets, styles, and the
// browser talking to the Blazor circuit.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

const BASE = process.env.MCPLAB_ADMIN_URL || 'http://127.0.0.1:9890';
const PASSWORD = process.env.MCPLAB_ADMIN_PASSWORD;
const WAIT = 30000;

const ARTIFACTS = process.env.MCPLAB_E2E_ARTIFACTS;
const results = [];
let currentPage;

async function step(name, action) {
  try {
    await action();
    results.push({ name, ok: true });
    console.log(`[PASS] ${name}`);
  } catch (error) {
    results.push({ name, ok: false });
    console.log(`[FAIL] ${name}: ${error.message.split('\n')[0]}`);
    if (ARTIFACTS && currentPage) {
      const file = `${ARTIFACTS}/${results.length.toString().padStart(2, '0')}-failed.png`;
      await currentPage.screenshot({ path: file, fullPage: true }).catch(() => {});
      console.log(`       screenshot: ${file}`);
    }
    // Close a dialog left open by the failure so the next step starts clean.
    if (currentPage) {
      await currentPage.keyboard.press('Escape').catch(() => {});
    }
  }
}

async function openContext(browser, options = {}) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 860 }, ...options });
  const page = await context.newPage();
  currentPage = page;
  page.problems = [];
  page.on('pageerror', error => page.problems.push(`page error: ${error.message}`));
  page.on('response', response => {
    if (response.status() >= 400 && !response.url().includes('/account/login')) {
      page.problems.push(`HTTP ${response.status()} ${response.url()}`);
    }
  });
  return { context, page };
}

async function signIn(page) {
  await page.goto(`${BASE}/login`);
  await page.fill('#secret', PASSWORD);
  await page.click('button[type=submit]');
  await page.waitForURL(`${BASE}/`);
  await page.locator('.service-card .mud-chip').first().waitFor({ timeout: WAIT });
  await ready(page);
}

function field(scope, label) {
  return scope.locator('.mud-input-control').filter({ has: scope.page().locator(`label:text-is("${label}")`) }).locator('input').first();
}

// Blazor pages render on the server first; clicks before the circuit connects are lost.
async function ready(page) {
  await page.waitForLoadState('networkidle');
  await page.waitForFunction(() => window.Blazor !== undefined, null, { timeout: WAIT });
  await page.waitForTimeout(1000);
}

// Closes the previous result so the next one is fresh. Call it before opening a dialog,
// because the dialog's backdrop covers the close button.
async function clearResult(page) {
  const close = page.locator('.page-stack > .mud-alert .mud-alert-close-button').first();
  if (await close.count()) {
    await close.click();
    await page.locator('.page-stack > .mud-alert').first().waitFor({ state: 'detached', timeout: WAIT }).catch(() => {});
  }
}

async function resultAfter(page, action) {
  await action();
  await page.locator('.mud-overlay-scrim').waitFor({ state: 'detached', timeout: WAIT }).catch(() => {});
  const alert = page.locator('.page-stack > .mud-alert').first();
  await alert.waitFor({ timeout: WAIT });
  return alert.innerText();
}

function connectionRow(page, alias) {
  return page.locator('.content-card', { hasText: '新增連線' }).locator('tbody tr', { hasText: alias }).first();
}

(async () => {
  assert.ok(PASSWORD, 'Set MCPLAB_ADMIN_PASSWORD.');
  const browser = await chromium.launch();
  const { page } = await openContext(browser);

  await step('unauthenticated visits redirect to sign-in', async () => {
    await page.goto(`${BASE}/`);
    assert.match(page.url(), /\/login/);
  });

  await step('wrong password is rejected', async () => {
    await page.fill('#secret', 'not-the-password');
    await page.click('button[type=submit]');
    await page.waitForURL(/failed=1/);
    assert.match(await page.locator('.mud-alert').innerText(), /密碼不正確/);
  });

  await step('sign-in shows every service as healthy', async () => {
    await signIn(page);
    await page.waitForFunction(() => document.querySelectorAll('.service-card .mud-chip').length >= 11, null, { timeout: WAIT });
    const chips = await page.locator('.service-card .mud-chip').allInnerTexts();
    assert.equal(chips.length, 11);
    assert.deepEqual([...new Set(chips.map(text => text.trim()))], ['正常']);
  });

  await step('app bar title is readable (text color differs from its background)', async () => {
    const colors = await page.locator('.mud-appbar').evaluate(element => {
      const title = element.querySelector('h6');
      return [getComputedStyle(title).color, getComputedStyle(element).backgroundColor];
    });
    assert.notEqual(colors[0], colors[1]);
  });

  await step('page loads without missing assets or script errors', async () => {
    assert.deepEqual(page.problems, []);
  });

  await page.goto(`${BASE}/services/mcp-sql-server`);
  await connectionRow(page, 'LAB').waitFor({ timeout: WAIT });
  await ready(page);

  await step('new connection with missing fields is blocked in the form', async () => {
    await page.getByRole('button', { name: '新增連線' }).click();
    const dialog = page.locator('.mud-dialog');
    await dialog.waitFor();
    await field(dialog, 'Alias').fill('E2E');
    await field(dialog, 'HOST *').fill('test-mssql');
    await dialog.getByRole('button', { name: '儲存', exact: true }).click();
    await dialog.locator('.mud-alert').waitFor({ timeout: WAIT });
    assert.match(await dialog.locator('.mud-alert').innerText(), /MSSQL_CONN_E2E_USER is required/);
  });

  await step('completed connection is saved and applied without a restart', async () => {
    const dialog = page.locator('.mud-dialog');
    await field(dialog, 'USER *').fill('sa');
    await field(dialog, 'PASSWORD *').fill('Lab_Passw0rd!');
    const message = await resultAfter(page, () => dialog.getByRole('button', { name: '儲存', exact: true }).click());
    assert.match(message, /已儲存並生效/);
    assert.match(await connectionRow(page, 'E2E').innerText(), /已載入/);
  });

  await step('connection test reaches SQL Server through the MCP service', async () => {
    await page.click('button[aria-label="測試 E2E"]');
    const result = page.locator('.test-row', { hasText: 'E2E' });
    await result.waitFor({ timeout: WAIT });
    assert.match(await result.innerText(), /連線成功/);
  });

  await step('copy suggests a new alias and keeps the password', async () => {
    await clearResult(page);
    await page.click('button[aria-label="複製 E2E"]');
    const dialog = page.locator('.mud-dialog');
    await dialog.waitFor();
    assert.equal(await field(dialog, 'Alias').inputValue(), 'E2E2');
    const message = await resultAfter(page, () => dialog.getByRole('button', { name: '儲存', exact: true }).click());
    assert.match(message, /已儲存並生效/);
    await page.click('button[aria-label="測試 E2E2"]');
    const result = page.locator('.test-row', { hasText: 'E2E2' });
    await result.waitFor({ timeout: WAIT });
    assert.match(await result.innerText(), /連線成功/);
  });

  await step('out-of-range setting is rejected and a valid one takes effect', async () => {
    await clearResult(page);
    const maxRows = field(page.locator('.settings-grid'), 'MSSQL_MAX_ROWS');
    await maxRows.fill('99999');
    await page.getByRole('button', { name: '儲存服務設定' }).click();
    await page.locator('.settings-grid', { hasText: '允許範圍 1 到 5000' }).waitFor({ timeout: WAIT });
    await maxRows.fill('250');
    await clearResult(page);
    const message = await resultAfter(page, () => page.getByRole('button', { name: '儲存服務設定' }).click());
    assert.match(message, /已儲存並生效/);
    await page.locator('.settings-grid', { hasText: '目前生效：250' }).waitFor({ timeout: WAIT });
  });

  await step('deleting connections removes them', async () => {
    for (const alias of ['E2E2', 'E2E']) {
      await clearResult(page);
      await page.click(`button[aria-label="刪除 ${alias}"]`);
      const message = await resultAfter(page, () => page.locator('.mud-dialog').getByText('確認刪除', { exact: true }).click());
      assert.match(message, /已儲存並生效/);
    }
    assert.equal(await connectionRow(page, 'E2E').count(), 0);
  });

  await step('history lists the changes and the newest backup can be restored', async () => {
    await clearResult(page);
    const history = page.locator('.content-card', { hasText: '版本紀錄' });
    await history.locator('tbody tr').first().getByRole('button', { name: '還原此版本' }).click();
    const message = await resultAfter(page, () => page.locator('.mud-dialog').getByText('確認還原', { exact: true }).click());
    assert.match(message, /已儲存並生效/);
    await connectionRow(page, 'E2E').waitFor({ timeout: WAIT });
    await page.goto(`${BASE}/history`);
    await page.locator('tbody tr').first().waitFor({ timeout: WAIT });
    await ready(page);
    const rows = await page.locator('tbody tr').allInnerTexts();
    assert.ok(rows.some(row => row.includes('還原至')), 'restore is recorded');
    assert.ok(rows.some(row => row.includes('新增連線 E2E')), 'add is recorded');
  });

  await step('import preview flags typos and hides secret values', async () => {
    await page.goto(`${BASE}/import`);
    await ready(page);
    await page.locator('textarea').fill('REDIS_CONN_IMP_HOST=test-redis\nREDIS_CONN_IMP_HOTS=typo\nREDIS_CONN_IMP_PASSWORD=hunter2\n');
    await page.getByRole('button', { name: '預覽' }).click();
    const preview = page.locator('.content-card', { hasText: '將匯入' });
    await preview.waitFor({ timeout: WAIT });
    const text = await preview.innerText();
    assert.match(text, /Unknown field HOTS/);
    assert.match(text, /值已隱藏/);
    assert.doesNotMatch(text, /hunter2/);
  });

  const phone = await openContext(browser, { viewport: { width: 390, height: 844 } });
  await step('service page fits a phone screen without horizontal scrolling', async () => {
    await signIn(phone.page);
    await phone.page.goto(`${BASE}/services/mcp-redis`);
    await connectionRow(phone.page, 'LAB').waitFor({ timeout: WAIT });
    await ready(phone.page);
    const overflow = await phone.page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    assert.ok(overflow <= 0, `page overflows by ${overflow}px`);
  });

  const dark = await openContext(browser, { colorScheme: 'dark' });
  await step('dark mode uses the dark palette', async () => {
    await signIn(dark.page);
    await dark.page.waitForTimeout(1000);
    const background = await dark.page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    assert.equal(background, 'rgb(28, 27, 31)');
  });

  await browser.close();
  const failed = results.filter(result => !result.ok).length;
  console.log(`Summary: passed=${results.length - failed} failed=${failed}`);
  process.exit(failed === 0 ? 0 : 1);
})().catch(error => {
  console.error(error);
  process.exit(1);
});
