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
