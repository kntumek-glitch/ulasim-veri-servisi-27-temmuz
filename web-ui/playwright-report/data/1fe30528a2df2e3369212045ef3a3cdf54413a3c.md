# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: error-handling.spec.ts >> Journey Search Error Handling >> shows rate‑limit message for 429
- Location: e2e\error-handling.spec.ts:23:3

# Error details

```
Error: page.goto: net::ERR_CONNECTION_REFUSED at http://localhost:5174/
Call log:
  - navigating to "http://localhost:5174/", waiting until "load"

```

# Test source

```ts
  1  | import { test, expect } from '@playwright/test';
  2  | 
  3  | test.describe('Journey Search Error Handling', () => {
  4  |   test('shows "No route" message for NO_ROUTE_FOUND', async ({ page }) => {
  5  |     // Intercept the journey‑plan request and return 400 with NO_ROUTE_FOUND title
  6  |     await page.route('**/v2/journey-plans/search', route => {
  7  |       route.fulfill({
  8  |         status: 400,
  9  |         contentType: 'application/json',
  10 |         body: JSON.stringify({ title: 'NO_ROUTE_FOUND' }),
  11 |       });
  12 |     });
  13 | 
  14 |     await page.goto('http://localhost:5173');
  15 |     await page.fill('[data-testid="origin-input"]', 'İstanbul');
  16 |     await page.fill('[data-testid="destination-input"]', 'Bilinmeyen');
  17 |     await page.click('[data-testid="search-button"]');
  18 | 
  19 |     const errMsg = await page.waitForSelector('text=Bu iki nokta arasında uygun toplu taşıma rotası bulunamadı.');
  20 |     await expect(errMsg).toBeVisible();
  21 |   });
  22 | 
  23 |   test('shows rate‑limit message for 429', async ({ page }) => {
  24 |     await page.route('**/v2/journey-plans/search', route => {
  25 |       route.fulfill({ status: 429, contentType: 'application/json', body: '{}' });
  26 |     });
  27 | 
> 28 |     await page.goto('http://localhost:5174'); // assume dev server runs on another port if needed
     |                ^ Error: page.goto: net::ERR_CONNECTION_REFUSED at http://localhost:5174/
  29 |     await page.fill('[data-testid="origin-input"]', 'İstanbul');
  30 |     await page.fill('[data-testid="destination-input"]', 'Ankara');
  31 |     await page.click('[data-testid="search-button"]');
  32 | 
  33 |     const errMsg = await page.waitForSelector('text=İstek sınırını aştınız, lütfen daha sonra tekrar deneyiniz.');
  34 |     await expect(errMsg).toBeVisible();
  35 |   });
  36 | });
  37 | 
```