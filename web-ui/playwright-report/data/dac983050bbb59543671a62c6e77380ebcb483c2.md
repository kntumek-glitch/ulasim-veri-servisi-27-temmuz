# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: journey-search.spec.ts >> Journey Search Flow >> should search for a journey and display results
- Location: e2e\journey-search.spec.ts:4:3

# Error details

```
Test timeout of 30000ms exceeded.
```

```
Error: page.fill: Test timeout of 30000ms exceeded.
Call log:
  - waiting for locator('[data-testid="origin-input"]')

```

# Test source

```ts
  1  | import { test, expect } from '@playwright/test';
  2  | 
  3  | test.describe('Journey Search Flow', () => {
  4  |   test('should search for a journey and display results', async ({ page }) => {
  5  |     // Open the app (assumes dev server running at http://localhost:5173)
  6  |     await page.goto('http://localhost:5173');
  7  | 
  8  |     // Fill origin and destination inputs (using placeholder selectors)
> 9  |     await page.fill('[data-testid="origin-input"]', 'İstanbul');
     |                ^ Error: page.fill: Test timeout of 30000ms exceeded.
  10 |     await page.fill('[data-testid="destination-input"]', 'Ankara');
  11 | 
  12 |     // Choose DEPART_AT mode
  13 |     await page.click('[data-testid="mode-depart-at"]');
  14 | 
  15 |     // Submit search
  16 |     await page.click('[data-testid="search-button"]');
  17 | 
  18 |     // Wait for route list to appear and verify at least one result
  19 |     await page.waitForSelector('[data-testid="route-list-item"]');
  20 |     const routes = await page.$$('[data-testid="route-list-item"]');
  21 |     expect(routes.length).toBeGreaterThan(0);
  22 | 
  23 |     // Click first route and verify map renders
  24 |     await routes[0].click();
  25 |     await page.waitForSelector('[data-testid="map-canvas"]');
  26 |     const mapVisible = await page.isVisible('[data-testid="map-canvas"]');
  27 |     expect(mapVisible).toBeTruthy();
  28 |   });
  29 | });
  30 | 
```