// Inline SVG in plaats van een icon-library of emoji: geen dependency, geen
// externe request, en het icoon erft gewoon currentColor uit de tekstkleur.
// Alles is decoratief naast een echte tekstkop, dus aria-hidden.

const base = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 1.6,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': 'true',
  focusable: 'false',
};

function Svg({ children, className = 'h-6 w-6', ...rest }) {
  return (
    <svg {...base} {...rest} className={className}>
      {children}
    </svg>
  );
}

export const IconPackage = (p) => (
  <Svg {...p}>
    <path d="M21 8.5v7a1.5 1.5 0 0 1-.79 1.32l-7.5 4a1.5 1.5 0 0 1-1.42 0l-7.5-4A1.5 1.5 0 0 1 3 15.5v-7a1.5 1.5 0 0 1 .79-1.32l7.5-4a1.5 1.5 0 0 1 1.42 0l7.5 4A1.5 1.5 0 0 1 21 8.5Z" />
    <path d="m3.3 7.5 8.7 4.6 8.7-4.6M12 12.1V21" />
  </Svg>
);

export const IconSliders = (p) => (
  <Svg {...p}>
    <path d="M4 6h9M17 6h3M4 12h4M12 12h8M4 18h9M17 18h3" />
    <circle cx="15" cy="6" r="2" />
    <circle cx="10" cy="12" r="2" />
    <circle cx="15" cy="18" r="2" />
  </Svg>
);

export const IconBroom = (p) => (
  <Svg {...p}>
    <path d="M14.5 3.5 10 8M9.2 7.2l3.6 3.6" />
    <path d="M12.8 10.8 5.4 18.2a2 2 0 0 0-.5 2l.3 1.1 1.1.3a2 2 0 0 0 2-.5l7.4-7.4" />
    <path d="M8 15.5 11 18M18 3l1 2 2 1-2 1-1 2-1-2-2-1 2-1Z" />
  </Svg>
);

export const IconSparkle = (p) => (
  <Svg {...p}>
    <path d="M11 3 8.8 8.8 3 11l5.8 2.2L11 19l2.2-5.8L19 11l-5.8-2.2Z" />
    <path d="M18.5 15.5 17.7 18l-2.2.8 2.2.8.8 2.2.8-2.2 2.2-.8-2.2-.8Z" />
  </Svg>
);

export const IconArchive = (p) => (
  <Svg {...p}>
    <rect x="3" y="4" width="18" height="4" rx="1" />
    <path d="M5 8v10a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8" />
    <path d="M10 12h4" />
  </Svg>
);

export const IconLanguages = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M3.5 9h17M3.5 15h17" />
    <path d="M12 3a15 15 0 0 1 0 18a15 15 0 0 1 0-18Z" />
  </Svg>
);

export const IconShield = (p) => (
  <Svg {...p}>
    <path d="M12 3 5 6v5.5c0 4.3 2.9 7.7 7 9.5 4.1-1.8 7-5.2 7-9.5V6l-7-3Z" />
    <path d="m9 12 2 2 4-4" />
  </Svg>
);

export const IconRefresh = (p) => (
  <Svg {...p}>
    <path d="M20 12a8 8 0 0 1-13.7 5.6L4 15.4" />
    <path d="M4 12a8 8 0 0 1 13.7-5.6L20 8.6" />
    <path d="M20 4v4.6h-4.6M4 20v-4.6h4.6" />
  </Svg>
);

export const IconDownload = (p) => (
  <Svg {...p}>
    <path d="M12 3v12" />
    <path d="m7.5 10.5 4.5 4.5 4.5-4.5" />
    <path d="M4 20h16" />
  </Svg>
);

export const IconArrow = (p) => (
  <Svg {...p}>
    <path d="M5 12h13M13 6.5 18.5 12 13 17.5" />
  </Svg>
);

export const IconChevron = (p) => (
  <Svg {...p}>
    <path d="m7 10 5 5 5-5" />
  </Svg>
);

export const IconSun = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.2 5.2l1.4 1.4M17.4 17.4l1.4 1.4M18.8 5.2l-1.4 1.4M6.6 17.4l-1.4 1.4" />
  </Svg>
);

export const IconMoon = (p) => (
  <Svg {...p}>
    <path d="M20 14.2A8.2 8.2 0 0 1 9.8 4a8.5 8.5 0 1 0 10.2 10.2Z" />
  </Svg>
);

// Merkiconen zijn gevuld i.p.v. lijnwerk, dus die krijgen hun eigen svg.
export const IconGitHub = ({ className = 'h-5 w-5' }) => (
  <svg viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" focusable="false" className={className}>
    <path d="M8 0C3.58 0 0 3.58 0 8a8 8 0 0 0 5.47 7.59c.4.07.55-.17.55-.38l-.01-1.49c-2.01.36-2.53-.5-2.7-.96-.09-.24-.48-.96-.82-1.16-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82a7.4 7.4 0 0 1 2-.27c.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48l-.01 2.19c0 .21.15.46.55.38A8 8 0 0 0 16 8c0-4.42-3.58-8-8-8Z" />
  </svg>
);

export const IconWindows = ({ className = 'h-4 w-4' }) => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false" className={className}>
    <path d="M3 5.6 10.4 4.6v7.1H3V5.6Zm0 12.8 7.4 1v-7H3v6ZM11.4 4.4 21 3v8.7h-9.6V4.4Zm0 8.3H21V21l-9.6-1.4v-6.9Z" />
  </svg>
);

// Beeldmerk van de app zelf: een gereedschapskist-abstractie. Bewust geen
// screenshot of gestolen logo — puur geometrie.
export const IconLogo = ({ className = 'h-8 w-8' }) => (
  <svg viewBox="0 0 32 32" aria-hidden="true" focusable="false" className={className}>
    <rect x="3" y="10.5" width="26" height="17.5" rx="4" className="fill-[var(--color-logo-body)]" />
    <path
      d="M11 11V8.5A3.5 3.5 0 0 1 14.5 5h3A3.5 3.5 0 0 1 21 8.5V11"
      fill="none"
      stroke="var(--color-logo-edge)"
      strokeWidth="2.2"
      strokeLinecap="round"
    />
    <rect x="3" y="16" width="26" height="3.2" fill="var(--color-logo-edge)" opacity="0.35" />
    <rect x="13.4" y="14.4" width="5.2" height="6.4" rx="1.6" fill="var(--color-logo-edge)" />
  </svg>
);
