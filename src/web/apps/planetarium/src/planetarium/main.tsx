// The v3 planetarium UI (planetarium.html, opened with --ui=web).
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import '@astroplanner/ui-kit/fonts.css';
import '@astroplanner/ui-kit/tokens.css';
import '@astroplanner/sky-engine/style.css';
import './app/i18n';
import { App } from './App';

const root = document.getElementById('root');
if (!root) {
  throw new Error('planetarium.html is missing #root');
}

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
