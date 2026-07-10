const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

(async () => {
  const outDir = __dirname;
  const html = path.join(outDir, 'chat-preview.html');
  const fileUrl = `file:///${html.replace(/\\/g, '/')}`;
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage({
    viewport: { width: 1260, height: 950 },
    deviceScaleFactor: 1,
  });

  await page.goto(fileUrl, { waitUntil: 'load' });
  await page.screenshot({
    path: path.join(outDir, 'chat-preview-overview.png'),
    fullPage: true,
  });

  for (const [selector, name] of [
    ['#inbox-phone', 'chat-inbox.png'],
    ['#thread-phone', 'chat-thread.png'],
    ['#profile-phone', 'chat-profile.png'],
  ]) {
    const element = await page.$(selector);
    if (!element) throw new Error(`Missing preview element ${selector}`);
    await element.screenshot({ path: path.join(outDir, name) });
  }

  await browser.close();

  for (const name of [
    'chat-preview-overview.png',
    'chat-inbox.png',
    'chat-thread.png',
    'chat-profile.png',
  ]) {
    const stat = fs.statSync(path.join(outDir, name));
    console.log(`${name} ${stat.size} bytes`);
  }
})().catch((error) => {
  console.error(error);
  process.exit(1);
});
