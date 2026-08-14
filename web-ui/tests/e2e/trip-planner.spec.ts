import { test, expect } from '@playwright/test';

test.describe('Trip Planner', () => {
  test('should show empty state initially', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByPlaceholder('Başlangıç (örn. Bostanlı)')).toBeVisible();
  });

  test('should handle validation errors', async ({ page }) => {
    await page.goto('/');
    
    // Fill origin but not destination
    const fromInput = page.getByPlaceholder('Başlangıç (örn. Bostanlı)');
    await fromInput.fill('Bostanlı');
    
    const searchBtn = page.getByRole('button', { name: 'Rota Bul' });
    await searchBtn.click({ force: true });
    
    // Check if error state or alert is shown
    await expect(page.locator('.error-message', { hasText: 'Varış noktası seçmelisiniz.' })).toBeVisible();
  });
});
