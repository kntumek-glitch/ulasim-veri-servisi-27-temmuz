import React from 'react';

interface ErrorBannerProps {
  message: string;
  type: 'success' | 'warning' | 'error';
  onRetry?: () => void;
}

/**
 * Global error/info banner displayed at the top of pages.
 * Uses the glass‑panel aesthetic defined in index.css.
 * Supports optional retry action for transient errors.
 */
export const ErrorBanner: React.FC<ErrorBannerProps> = ({ message, type, onRetry }) => {
  const bgClass = {
    success: 'success-banner',
    warning: 'warning-banner',
    error: 'error-banner',
  }[type];

  return (
    <div className={`glass-panel ${bgClass}`} style={{
      marginBottom: '16px',
      padding: '12px',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
    }}>
      <span>{message}</span>
      {onRetry && (
        <button
          onClick={onRetry}
          style={{
            background: 'transparent',
            border: '1px solid var(--color-text-main)',
            borderRadius: '4px',
            color: 'var(--color-text-main)',
            padding: '4px 8px',
            cursor: 'pointer',
          }}
        >
          Tekrar Dene
        </button>
      )}
    </div>
  );
};

export default ErrorBanner;
