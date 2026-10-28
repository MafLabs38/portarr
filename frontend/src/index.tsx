import '@fontsource/fredoka/600.css';
import '@fontsource/fredoka/700.css';
import '@fontsource/jetbrains-mono/500.css';
import '@fontsource/nunito/500.css';
import '@fontsource/nunito/700.css';
import '@fontsource/nunito/800.css';
import 'Styles/Themes/themes.css';
import 'Styles/Variables/variables.css';
import 'Styles/globals.css';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import App from 'App/App';

const container = document.getElementById('root');

if (!container) {
  throw new Error('Root element #root is missing from index.html');
}

createRoot(container).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
