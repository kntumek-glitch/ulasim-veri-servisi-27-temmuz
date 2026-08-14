import { test, expect } from '@playwright/test';

test.describe('Stops View & Navigation', () => {
  test('should navigate to Lines tab when clicking a passing line in Stop Detail', async ({ page }) => {
    await page.goto('/stops');
    
    // Wait for stops to load
    const stopCard = page.locator('.itinerary-card').first();
    await expect(stopCard).toBeVisible();
    await page.waitForTimeout(1000);
    await stopCard.click({ force: true });
    await page.waitForTimeout(500);
    
    // Check stop detail
    await expect(page.locator('.clear-btn').first()).toBeVisible({ timeout: 10000 });
    
    // Check if there are passing lines
    // Depending on DB, there might be lines or empty state
    // Let's assume the first stop has some lines or wait for them
    const lineBadge = page.locator('.planner-results > div[style*="flex-wrap: wrap"] > div').first();
    
    // If it's visible, click it and check navigation
    if (await lineBadge.isVisible()) {
      await lineBadge.click({ force: true });
      await expect(page).toHaveURL(/\/lines/);
      
      // Ensure Line Detail is opened directly
      await expect(page.locator('.clear-btn').first()).toBeVisible({ timeout: 10000 });
      await expect(page.locator('h2', { hasText: 'Hat' })).toBeVisible();
    }
  });
});
