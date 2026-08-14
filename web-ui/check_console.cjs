const { chromium } = require('playwright');

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  
  page.on('console', msg => {
    if (msg.type() === 'error')
      console.log(`PAGE ERROR: ${msg.text()}`);
    else
      console.log(`PAGE LOG: ${msg.text()}`);
  });

  page.on('pageerror', exception => {
    console.log(`UNCAUGHT EXCEPTION: ${exception.stack || exception}`);
  });

  try {
    await page.goto('http://localhost:5173/', { waitUntil: 'networkidle' });
  } catch (err) {
    console.log(`GOTO ERROR: ${err}`);
  }
  
  await browser.close();
})();
