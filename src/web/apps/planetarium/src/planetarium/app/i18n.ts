// UI chrome strings (spec §1.5: domain text comes formatted from C#; only the chrome is here).
import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from '../i18n/en.json';

void i18next.use(initReactI18next).init({
  lng: 'en',
  fallbackLng: 'en',
  resources: { en: { translation: en } },
  interpolation: { escapeValue: false }, // React escapes
  initAsync: false,
});

export default i18next;
