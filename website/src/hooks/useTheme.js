import { useCallback, useEffect, useState } from 'react';

const STORAGE_KEY = 'stb-theme';

function readStored() {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    return v === 'light' || v === 'dark' ? v : null;
  } catch {
    return null; // private mode / geblokkeerde storage
  }
}

function systemTheme() {
  if (typeof window === 'undefined' || !window.matchMedia) return 'dark';
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

// Donker is de basis-look van de pagina; licht is een volwaardige tweede modus.
// Zonder expliciete keuze volgt de pagina het systeem (puur via CSS-media-query,
// dus ook zonder JS goed). Zodra de bezoeker wisselt, zet dit data-theme op
// <html> en dat overruled de media-query in beide richtingen.
export function useTheme() {
  const [theme, setTheme] = useState(() => readStored() ?? systemTheme());
  const [explicit, setExplicit] = useState(() => readStored() !== null);

  // Volg het systeem zolang er geen eigen keuze gemaakt is.
  useEffect(() => {
    if (explicit || typeof window === 'undefined' || !window.matchMedia) return undefined;
    const mq = window.matchMedia('(prefers-color-scheme: light)');
    const onChange = () => setTheme(mq.matches ? 'light' : 'dark');
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [explicit]);

  useEffect(() => {
    const root = document.documentElement;
    if (explicit) root.setAttribute('data-theme', theme);
    else root.removeAttribute('data-theme');
  }, [theme, explicit]);

  const toggle = useCallback(() => {
    setExplicit(true);
    setTheme((prev) => {
      const next = prev === 'dark' ? 'light' : 'dark';
      try {
        localStorage.setItem(STORAGE_KEY, next);
      } catch {
        // Niet kunnen onthouden is geen reden om niet te wisselen.
      }
      return next;
    });
  }, []);

  return { theme, toggle };
}
