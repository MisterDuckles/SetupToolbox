import { useEffect, useState } from 'react';
import {
  ASSET_PATTERN,
  FALLBACK_SIZE_BYTES,
  FALLBACK_VERSION,
  RELEASES_PAGE,
  RELEASE_API,
} from '../lib/constants';

// MiB, want dat is wat Windows in Verkenner en in de download-balk toont.
export function formatSize(bytes) {
  if (!Number.isFinite(bytes) || bytes <= 0) return null;
  return `${Math.round(bytes / (1024 * 1024))} MB`;
}

const FALLBACK = {
  version: FALLBACK_VERSION,
  url: RELEASES_PAGE,
  size: formatSize(FALLBACK_SIZE_BYTES),
  publishedAt: null,
  live: false,
};

// Haalt de nieuwste release op uit de publieke GitHub-API en leest daar
// tag_name + de browser_download_url én de size van de installer-asset uit.
// Start met de fallback zodat er nooit een leeg versielabel in beeld staat:
// mislukt de fetch, dan blijft die gewoon staan en wijst de knop naar de
// releases-pagina. Slaagt 'ie, dan loopt de pagina vanzelf mee met elke
// nieuwe Release zonder dat hier iets gewijzigd hoeft te worden.
export function useLatestRelease() {
  const [release, setRelease] = useState(FALLBACK);

  useEffect(() => {
    const controller = new AbortController();

    fetch(RELEASE_API, {
      signal: controller.signal,
      headers: { Accept: 'application/vnd.github+json' },
    })
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`GitHub API ${r.status}`))))
      .then((json) => {
        const asset = (json.assets ?? []).find((a) => ASSET_PATTERN.test(a?.name ?? ''));
        setRelease({
          version: json.tag_name || FALLBACK_VERSION,
          url: asset?.browser_download_url ?? RELEASES_PAGE,
          size: formatSize(asset?.size) ?? FALLBACK.size,
          publishedAt: json.published_at ?? null,
          live: true,
        });
      })
      .catch(() => {
        // Bewust stil: de fallback in de state is het vangnet.
      });

    return () => controller.abort();
  }, []);

  return release;
}
