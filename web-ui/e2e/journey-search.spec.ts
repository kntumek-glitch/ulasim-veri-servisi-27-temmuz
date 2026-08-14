import { test, expect } from '@playwright/test';

test.describe('Journey Search Flow', () => {
  test('should search for a journey and display results', async ({ page }) => {
    // Open the app (assumes dev server running at http://localhost:5173)
    await page.goto('http://localhost:5173');

    // Fill origin and destination inputs (using placeholder selectors)
    await page.fill('[data-testid="origin-input"]', 'İstanbul');
    await page.fill('[data-testid="destination-input"]', 'Ankara');

    // Choose DEPART_AT mode
    await page.click('[data-testid="mode-depart-at"]');

    // Submit search
    await page.click('[data-testid="search-button"]');

    // Wait for route list to appear and verify at least one result
    await page.waitForSelector('[data-testid="route-list-item"]');
    const routes = await page.$$('[data-testid="route-list-item"]');
    expect(routes.length).toBeGreaterThan(0);

    // Click first route and verify map renders
    await routes[0].click();
    await page.waitForSelector('[data-testid="map-canvas"]');
    const mapVisible = await page.isVisible('[data-testid="map-canvas"]');
    expect(mapVisible).toBeTruthy();
  });
});
