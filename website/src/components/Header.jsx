import { useEffect, useState } from 'react';
import { GITHUB_URL } from '../lib/constants';
import { IconGitHub, IconLogo, IconMoon, IconSun } from './Icons';

const NAV = [
  { href: '#functies', label: 'Functies' },
  { href: '#schermen', label: 'Schermen' },
  { href: '#werkwijze', label: 'Zo werkt het' },
  { href: '#veilig', label: 'Veiligheid' },
  { href: '#vragen', label: 'Vragen' },
];

export function Header({ theme, onToggleTheme, downloadUrl }) {
  const [scrolled, setScrolled] = useState(false);

  // Pas een rand + achtergrond toe zodra de pagina van bovenaf wegscrollt,
  // zodat de balk niet over de inhoud heen zweeft zonder afscheiding.
  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <header
      className={`sticky top-0 z-50 transition-colors duration-300 ${
        scrolled ? 'border-b border-stroke bg-bg/80 backdrop-blur-lg' : 'border-b border-transparent'
      }`}
    >
      <nav aria-label="Hoofdnavigatie" className="mx-auto flex max-w-6xl items-center gap-4 px-4 py-3 sm:px-6 sm:py-4">
        <a href="#top" className="flex shrink-0 items-center gap-2.5 font-semibold">
          <IconLogo className="h-7 w-7" />
          <span className="text-[0.95rem] tracking-tight sm:text-base">Setup&nbsp;Toolbox</span>
        </a>

        <ul className="ml-auto hidden items-center gap-1 lg:flex">
          {NAV.map((item) => (
            <li key={item.href}>
              <a
                href={item.href}
                className="rounded-lg px-3 py-2 text-sm text-muted transition hover:bg-panel-3 hover:text-ink"
              >
                {item.label}
              </a>
            </li>
          ))}
        </ul>

        <div className="ml-auto flex items-center gap-1.5 lg:ml-2">
          <button
            type="button"
            onClick={onToggleTheme}
            aria-pressed={theme === 'light'}
            className="rounded-lg p-2 text-muted transition hover:bg-panel-3 hover:text-ink"
          >
            {theme === 'dark' ? <IconSun className="h-5 w-5" /> : <IconMoon className="h-5 w-5" />}
            <span className="sr-only">
              {theme === 'dark' ? 'Schakel over naar het lichte thema' : 'Schakel over naar het donkere thema'}
            </span>
          </button>

          <a
            href={GITHUB_URL}
            rel="noopener"
            className="rounded-lg p-2 text-muted transition hover:bg-panel-3 hover:text-ink"
          >
            <IconGitHub className="h-5 w-5" />
            <span className="sr-only">Setup Toolbox op GitHub</span>
          </a>

          <a
            href={downloadUrl}
            className="btn-primary hidden rounded-lg px-4 py-2 text-sm font-semibold sm:inline-block"
          >
            Downloaden
          </a>
        </div>
      </nav>
    </header>
  );
}
