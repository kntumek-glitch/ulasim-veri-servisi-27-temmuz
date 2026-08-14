import { test, expect } from '@playwright/test';

test.describe('Journey Search Error Handling', () => {
  test('shows "No route" message for NO_ROUTE_FOUND', async ({ page }) => {
    // Intercept the journey‑plan request and return 400 with NO_ROUTE_FOUND title
    await page.route('**/v2/journey-plans/search', route => {
      route.fulfill({
        status: 400,
        contentType: 'application/json',
        body: JSON.stringify({ title: 'NO_ROUTE_FOUND' }),
      });
    });

    await page.goto('http://localhost:5173');
    await page.fill('[data-testid="origin-input"]', 'İstanbul');
    await page.fill('[data-testid="destination-input"]', 'Bilinmeyen');
    await page.click('[data-testid="search-button"]');

    const errMsg = await page.waitForSelector('text=Bu iki nokta arasında uygun toplu taşıma rotası bulunamadı.');
    await expect(errMsg).toBeVisible();
  });

  test('shows rate‑limit message for 429', async ({ page }) => {
    await page.route('**/v2/journey-plans/search', route => {
      route.fulfill({ status: 429, contentType: 'application/json', body: '{}' });
    });

    await page.goto('http://localhost:5174'); // assume dev server runs on another port if needed
    await page.fill('[data-testid="origin-input"]', 'İstanbul');
    await page.fill('[data-testid="destination-input"]', 'Ankara');
    await page.click('[data-testid="search-button"]');

    const errMsg = await page.waitForSelector('text=İstek sınırını aştınız, lütfen daha sonra tekrar deneyiniz.');
    await expect(errMsg).toBeVisible();
  });
});
