import { test, expect } from '@playwright/test';

test.describe('System Status', () => {
  test('should display healthy engine status', async ({ page }) => {
    await page.goto('/status');
    
    await expect(page.locator('h2', { hasText: 'Sistem Durumu' })).toBeVisible();
    await expect(page.locator('.status-value.success', { hasText: 'Sağlıklı' })).toBeVisible();
  });
});
