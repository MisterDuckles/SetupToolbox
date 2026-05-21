import { useEffect, useRef, useState } from 'react';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import { useGSAP } from '@gsap/react';

gsap.registerPlugin(useGSAP, ScrollTrigger);

const REPO = 'MisterDuckles/SetupToolbox';
const GITHUB_URL = `https://github.com/${REPO}`;
const RELEASES_PAGE = `${GITHUB_URL}/releases/latest`;

// Nieuwste release via de publieke GitHub-API → versie-label + directe .exe.
// Faalt 'ie, dan vallen we terug op de releases-pagina + statisch versielabel.
function useLatestRelease() {
  const [release, setRelease] = useState(null);
  useEffect(() => {
    let active = true;
    fetch(`https://api.github.com/repos/${REPO}/releases/latest`)
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error('fetch failed'))))
      .then((j) => {
        if (!active) return;
        const asset = (j.assets ?? []).find((a) => /^SetupToolbox-v.*\.exe$/i.test(a.name));
        setRelease({ version: j.tag_name ?? 'v1.0.0', url: asset?.browser_download_url ?? RELEASES_PAGE });
      })
      .catch(() => {});
    return () => { active = false; };
  }, []);
  return release;
}

// Splitst tekst in woorden, elk in een overflow-masker zodat ze "omhoog
// onthullen" (GSAP animeert de binnenste .word).
function SplitWords({ text, className = '' }) {
  return text.split(' ').map((w, i) => (
    <span key={i} className="inline-block overflow-hidden align-bottom pb-[0.12em]">
      <span className={`word inline-block ${className}`}>{w}&nbsp;</span>
    </span>
  ));
}

const FEATURES = [
  { icon: '📦', title: 'Apps in bulk', desc: 'Installeer je favoriete apps in één keer via winget — selecteren, klikken, klaar.' },
  { icon: '🎛️', title: '~74 Tweaks', desc: 'Privacy, performance, UI, taskbar en meer. Live state-detectie en volledig omkeerbaar.' },
  { icon: '🧹', title: 'Debloat', desc: 'Verwijder Microsoft- en OEM-bloatware met een paar klikken.' },
  { icon: '✨', title: 'Deep clean', desc: 'Ruim orphans, caches en achtergebleven registry-resten op.' },
  { icon: '💾', title: 'Profielen', desc: "Sla je tweak-set op en pas 'm toe op een andere PC. Snapshots + restore points als vangnet." },
  { icon: '🔄', title: 'Self-update', desc: 'De app houdt zichzelf up-to-date via GitHub-releases.' },
];

export default function App() {
  const root = useRef(null);
  const release = useLatestRelease();
  const version = release?.version ?? 'v1.0.0';
  const downloadUrl = release?.url ?? RELEASES_PAGE;

  useGSAP(
    () => {
      // Aurora-blobs traag laten drijven.
      gsap.to('.blob-1', { x: 90, y: 70, scale: 1.1, duration: 13, repeat: -1, yoyo: true, ease: 'sine.inOut' });
      gsap.to('.blob-2', { x: -80, y: 50, scale: 1.15, duration: 15, repeat: -1, yoyo: true, ease: 'sine.inOut' });
      gsap.to('.blob-3', { x: 60, y: -60, scale: 1.2, duration: 17, repeat: -1, yoyo: true, ease: 'sine.inOut' });

      // Titel: woorden omhoog onthullen.
      gsap.from('.word', { yPercent: 118, duration: 0.9, ease: 'power4.out', stagger: 0.05, delay: 0.1 });
      // Overige hero-elementen.
      gsap.from('[data-hero]', { y: 24, opacity: 0, duration: 0.8, ease: 'power3.out', stagger: 0.12, delay: 0.5 });

      // Features onthullen bij scrollen.
      gsap.from('[data-feature]', {
        scrollTrigger: { trigger: '#features', start: 'top 78%' },
        y: 36, opacity: 0, duration: 0.6, ease: 'power2.out', stagger: 0.09,
      });
      gsap.from('[data-download]', {
        scrollTrigger: { trigger: '#download', start: 'top 82%' },
        y: 28, opacity: 0, scale: 0.97, duration: 0.7, ease: 'power3.out',
      });
    },
    { scope: root },
  );

  // Spotlight-cursor op de feature-kaarten.
  const onCardMove = (e) => {
    const el = e.currentTarget;
    const r = el.getBoundingClientRect();
    el.style.setProperty('--mx', `${e.clientX - r.left}px`);
    el.style.setProperty('--my', `${e.clientY - r.top}px`);
  };

  return (
    <div ref={root} className="relative min-h-screen">
      <div className="aurora">
        <div className="blob blob-1" />
        <div className="blob blob-2" />
        <div className="blob blob-3" />
      </div>
      <div className="grid-overlay" />

      {/* ── Hero ── */}
      <header className="relative">
        <nav className="mx-auto flex max-w-6xl items-center justify-between px-6 py-6">
          <span className="text-lg font-semibold">🧰 Setup Toolbox</span>
          <a href={GITHUB_URL} className="text-sm text-muted transition hover:text-ink">GitHub ↗</a>
        </nav>

        <div className="mx-auto max-w-4xl px-6 pt-12 pb-24 text-center">
          <span data-hero className="inline-block rounded-full border border-stroke bg-panel/60 px-3 py-1 text-xs text-muted">
            Windows 11 · gratis · {version}
          </span>
          <h1 className="mt-6 text-5xl font-bold leading-[1.12] sm:text-6xl">
            <SplitWords text="Een verse PC," />
            <SplitWords text="in een paar klikken" className="text-shiny" />
            <SplitWords text="ingericht." />
          </h1>
          <p data-hero className="mx-auto mt-6 max-w-2xl text-lg text-muted">
            Setup Toolbox installeert je apps in bulk, ruimt bloatware op en past ~74 tweaks toe —
            alles in één app. Per-user install, geen admin nodig.
          </p>
          <div data-hero className="mt-9 flex flex-wrap items-center justify-center gap-4">
            <a href={downloadUrl} className="btn-primary rounded-xl px-7 py-3.5 font-semibold text-white">
              Download voor Windows
            </a>
            <a href={GITHUB_URL} className="rounded-xl border border-stroke px-7 py-3.5 font-medium text-ink transition hover:bg-panel">
              Bekijk op GitHub
            </a>
          </div>
          <p data-hero className="mt-5 text-xs text-muted">{version} · ~69 MB · gratis</p>
        </div>
      </header>

      {/* ── Features ── */}
      <section id="features" className="mx-auto max-w-6xl px-6 py-20">
        <h2 className="text-center text-3xl font-bold sm:text-4xl">Alles voor een schone start</h2>
        <p className="mx-auto mt-3 max-w-xl text-center text-muted">
          Vier tools in één: installeren, tweaken, debloaten en opschonen.
        </p>
        <div className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {FEATURES.map((f) => (
            <div key={f.title} data-feature onMouseMove={onCardMove} className="card rounded-2xl p-6">
              <div className="relative text-3xl">{f.icon}</div>
              <h3 className="relative mt-4 text-lg font-semibold">{f.title}</h3>
              <p className="relative mt-2 text-sm leading-relaxed text-muted">{f.desc}</p>
            </div>
          ))}
        </div>
      </section>

      {/* ── Download CTA ── */}
      <section id="download" className="mx-auto max-w-4xl px-6 py-16">
        <div data-download onMouseMove={onCardMove} className="card relative overflow-hidden rounded-3xl p-10 text-center sm:p-14">
          <h2 className="relative text-3xl font-bold sm:text-4xl">Klaar om je PC op te zetten?</h2>
          <p className="relative mx-auto mt-4 max-w-lg text-muted">
            Download de installer en draai 'm — per-user, geen admin nodig.
          </p>
          <div className="relative mt-8">
            <a href={downloadUrl} className="btn-primary inline-block rounded-xl px-8 py-4 text-lg font-semibold text-white">
              Download {version}
            </a>
          </div>
          <p className="relative mt-4 text-xs text-muted">
            of bekijk alle releases op{' '}
            <a href={RELEASES_PAGE} className="text-accent hover:underline">GitHub</a>
          </p>
        </div>
      </section>

      {/* ── Footer ── */}
      <footer className="border-t border-stroke">
        <div className="mx-auto flex max-w-6xl flex-col items-center justify-between gap-3 px-6 py-8 text-sm text-muted sm:flex-row">
          <span>© 2026 Daan · Setup Toolbox</span>
          <span className="text-center">Proprietary — broncode in te zien, niet vrij te hergebruiken.</span>
          <a href={GITHUB_URL} className="transition hover:text-ink">GitHub ↗</a>
        </div>
      </footer>
    </div>
  );
}
