// Eén plek voor alle repo-/release-constanten. De landingspagina haalt versie,
// downloadlink en bestandsgrootte live uit de GitHub-API; onderstaande
// FALLBACK_*-waarden zijn puur het vangnet als die fetch niet slaagt
// (offline, rate limit, netwerkblokkade).

export const REPO = 'MisterDuckles/SetupToolbox';
export const GITHUB_URL = `https://github.com/${REPO}`;
export const RELEASES_PAGE = `${GITHUB_URL}/releases/latest`;
export const RELEASE_API = `https://api.github.com/repos/${REPO}/releases/latest`;
export const LICENSE_URL = `${GITHUB_URL}/blob/main/LICENSE`;
export const ISSUES_URL = `${GITHUB_URL}/issues`;

// De installer-asset heet altijd SetupToolbox-v<versie>.exe (vastgelegd bij
// v1.0.0, zie de rebrand-sectie in NEXT-STEPS.md).
export const ASSET_PATTERN = /^SetupToolbox-v.*\.exe$/i;

// Laatst gepubliceerde GitHub-release op het moment van schrijven. Bijwerken
// bij een nieuwe milestone is niet strikt nodig — de live fetch wint altijd —
// maar houdt de pagina eerlijk als GitHub onbereikbaar is.
export const FALLBACK_VERSION = 'v1.2.0';
export const FALLBACK_SIZE_BYTES = 72384084; // SetupToolbox-v1.2.0.exe

// ── Schermafbeeldingen ─────────────────────────────────────────────────────
// De site draait op projects.dpvb.nl/setup-toolbox, dus vite.config.js zet base
// op '/setup-toolbox/'. Vite herschrijft die base wél in index.html en in assets
// die het zelf emit, maar NIET in stringliterals in .jsx — het kan niet weten dat
// zo'n string een URL is. Een hardgecodeerde '/screenshots/x.webp' resolveert live
// dus naar projects.dpvb.nl/screenshots/x.webp en 404't, terwijl 'ie in
// `npm run dev` (base '/') gewoon werkt: precies de "lokaal goed, live stuk"-val.
// import.meta.env.BASE_URL wordt bij het bouwen statisch vervangen en klopt in
// beide gevallen. Een pad ZÓNDER leidende slash is ook fout: dat resolveert tegen
// de document-URL en gaat mis op /setup-toolbox zonder afsluitende slash.
//
// De trailing slash wordt hieronder defensief genormaliseerd, zodat een latere
// bewerking van vite.config.js die de slash weglaat niet in één klap alle acht de
// beelden sloopt.
const BASE = import.meta.env.BASE_URL.endsWith('/')
  ? import.meta.env.BASE_URL
  : `${import.meta.env.BASE_URL}/`;

export const asset = (path) => `${BASE}${String(path).replace(/^\/+/, '')}`;

// website/public/screenshots/<scherm>-<taal>.webp
export const shotUrl = (screen, lang) => asset(`screenshots/${screen}-${lang}.webp`);
