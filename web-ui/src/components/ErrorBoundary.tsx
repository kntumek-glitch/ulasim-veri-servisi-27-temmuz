import { Component, ErrorInfo, ReactNode } from 'react';

interface Props {
  children?: ReactNode;
}

interface State {
  hasError: boolean;
  error?: Error;
}

class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false
  };

  public static getDerivedStateFromError(error: Error): State {
    // Update state so the next render will show the fallback UI.
    return { hasError: true, error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('Uncaught error:', error, errorInfo);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div style={{ 
          width: '100%', 
          height: '100%', 
          display: 'flex', 
          alignItems: 'center', 
          justifyContent: 'center',
          background: '#1a1a1a',
          color: '#ff6b6b',
          flexDirection: 'column',
          padding: 20,
          textAlign: 'center'
        }}>
          <h2>Harita Yüklenirken Bir Hata Oluştu</h2>
          <p style={{ marginTop: 10 }}>Bozuk veri veya render hatası nedeniyle harita çöktü.</p>
          <pre style={{ marginTop: 20, background: '#000', padding: 15, borderRadius: 5, fontSize: 12, maxWidth: '80%', overflowX: 'auto' }}>
            {this.state.error?.message}
          </pre>
          <button 
            style={{ marginTop: 20, padding: '10px 20px', background: 'var(--color-accent-primary)', border: 'none', borderRadius: 4, cursor: 'pointer', fontWeight: 'bold' }}
            onClick={() => this.setState({ hasError: false })}
          >
            Tekrar Dene
          </button>
        </div>
      );
    }

    return this.props.children;
  }
}

export default ErrorBoundary;
