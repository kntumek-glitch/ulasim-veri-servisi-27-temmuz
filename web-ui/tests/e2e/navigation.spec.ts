import { test, expect } from '@playwright/test';

test.describe('Navigation & Layout', () => {
  test('should navigate between tabs via sidebar and highlight active link', async ({ page }) => {
    await page.goto('/');

    // Sidebar should have links
    const linesLink = page.getByRole('link', { name: 'Hatlar' });
    const stopsLink = page.getByRole('link', { name: 'Duraklar' });
    const statusLink = page.getByRole('link', { name: 'Sistem Durumu' });

    // Click Lines
    await linesLink.click();
    await expect(page).toHaveURL('/lines');
    await expect(page.locator('h2', { hasText: 'Hatlar' })).toBeVisible();

    // Click Stops
    await stopsLink.click();
    await expect(page).toHaveURL('/stops');
    await expect(page.locator('h2', { hasText: 'Duraklar' })).toBeVisible();

    // Click Status
    await statusLink.click();
    await expect(page).toHaveURL('/status');
    await expect(page.locator('h2', { hasText: 'Sistem Durumu' })).toBeVisible();
  });
});
