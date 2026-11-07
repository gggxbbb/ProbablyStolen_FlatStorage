const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

async function main() {
  const workspace = path.resolve(process.argv[2]);
  const { chromium } = require(process.argv[3]);
  const directory = path.join(workspace, '.work', 'performance-browser');
  fs.mkdirSync(directory, { recursive: true });
  process.env.TEMP = directory;
  process.env.TMP = directory;
  const report = process.argv[4] || fs.readFileSync(path.join(workspace, '.work', 'performance-tests', 'latest-report.txt'), 'utf8');
  process.chdir(directory);
  const browser = await chromium.launch({ headless: true, downloadsPath: directory });
  try {
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(pathToFileURL(report).href);
    await page.waitForFunction(() => window.flatStorageReport && document.querySelector('#cards strong'));
    const initial = await page.evaluate(() => ({ frames: flatStorageReport.data.frames.length,
      visible: flatStorageReport.visible.length, metrics: document.querySelectorAll('#metrics tr').length,
      painted: document.querySelector('#chart').getContext('2d').getImageData(0, 0, 200, 100).data.some((value, index) => index % 4 === 3 && value > 0) }));
    assert.ok(initial.frames > 0);
    assert.equal(initial.visible, initial.frames);
    assert.ok(initial.metrics > 0);
    assert.ok(initial.painted);
    await page.click('#worst');
    assert.ok(await page.evaluate(() => {
      const report = flatStorageReport, column = report.data.frameColumns.indexOf('intervalMs');
      return report.data.frames[report.selected][column] === Math.max(...report.data.frames.map(row => row[column]));
    }));
    await page.selectOption('#operation', '2');
    assert.ok(await page.evaluate(() => flatStorageReport.visible.every(index => flatStorageReport.data.frames[index][4] & 2)));
    await page.selectOption('#metricScope', 'range');
    await page.click('#reset');
    await page.fill('#from', '1'); await page.locator('#from').dispatchEvent('change');
    await page.fill('#to', '3'); await page.locator('#to').dispatchEvent('change');
    assert.equal(await page.evaluate(() => flatStorageReport.visible.length), Math.min(initial.frames - 1, 3));
    await page.click('#reset');
    const downloadPromise = page.waitForEvent('download');
    await page.click('#download');
    const download = await downloadPromise;
    const downloaded = path.join(directory, download.suggestedFilename());
    await download.saveAs(downloaded);
    assert.equal(fs.readFileSync(downloaded, 'utf8'), await page.locator('#capture').textContent());
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForFunction(() => document.querySelector('#chart').width > 0);
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth));
    assert.deepEqual(errors, []);
    console.log(JSON.stringify({ report, frames: initial.frames, checks: 10, pageErrors: errors.length }));
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error); process.exitCode = 1; });
