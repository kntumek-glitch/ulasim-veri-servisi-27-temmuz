import React from 'react';
import { useTheme } from '../context/ThemeContext';
import './Header.css'; // simple CSS (uses design tokens)

const Header: React.FC = () => {
  const { theme, toggleTheme } = useTheme();

  return (
    <header className="app-header glass-panel">
  <div className="logo">🚍</div>
</header>
  );
};

export default Header;
