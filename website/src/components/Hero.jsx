import { STATS } from '../data/content';
import { GITHUB_URL } from '../lib/constants';
import { AppPreview } from './AppPreview';
import { IconDownload, IconGitHub, IconWindows } from './Icons';

// Splitst tekst in woorden, elk in een overflow-masker zodat GSAP ze van
// onderaf kan onthullen. Zonder JS staan de woorden gewoon op hun plek.
function SplitWords({ text, className = '' }) {
  return text.split(' ').map((w, i) => (
    <span key={`${w}-${i}`} className="inline-block overflow-hidden align-bottom pb-[0.12em]">
      <span className={`word inline-block ${className}`}>{w}&nbsp;</span>
    </span>
  ));
}

export function Hero({ version, downloadUrl, size }) {
  return (
    <section id="top" className="mx-auto max-w-6xl px-4 pt-12 pb-16 sm:px-6 sm:pt-16 lg:pt-24">
      <div className="mx-auto max-w-3xl text-center">
        <p data-hero className="flex flex-wrap items-center justify-center gap-x-2 gap-y-1 text-xs">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-stroke bg-panel/70 px-3 py-1 font-medium text-muted">
            <IconWindows className="h-3.5 w-3.5" />
            Windows 11
          </span>
          <span className="inline-flex items-center gap-1.5 rounded-full border border-stroke bg-panel/70 px-3 py-1 font-medium text-muted">
            Gratis
          </span>
          <span className="inline-flex items-center gap-1.5 rounded-full border border-stroke bg-panel/70 px-3 py-1 font-medium text-muted">
            Nederlands &amp; Engels
          </span>
        </p>

        <h1 className="mt-6 text-[2.1rem] leading-[1.1] font-bold sm:text-5xl lg:text-6xl">
          <SplitWords text="Een verse pc," />
          <SplitWords text="in een paar klikken" className="text-shiny" />
          <SplitWords text="ingericht." />
        </h1>

        <p data-hero className="mx-auto mt-6 max-w-2xl text-base leading-relaxed text-muted sm:text-lg">
          Setup Toolbox installeert je apps in bulk via winget, ruimt bloatware en achtergebleven rommel op, en zet
          124 Windows-tweaks precies zoals jij ze wilt. Alles in één app, alles omkeerbaar.
        </p>

        <div data-hero className="mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row sm:gap-4">
          <a
            href={downloadUrl}
            className="btn-primary inline-flex w-full items-center justify-center gap-2 rounded-xl px-7 py-3.5 font-semibold sm:w-auto"
          >
            <IconDownload className="h-5 w-5" />
            Download voor Windows
          </a>
          <a
            href={GITHUB_URL}
            rel="noopener"
            className="btn-ghost inline-flex w-full items-center justify-center gap-2 rounded-xl px-7 py-3.5 font-medium sm:w-auto"
          >
            <IconGitHub className="h-5 w-5" />
            Bekijk op GitHub
          </a>
        </div>

        <p data-hero className="mt-5 text-xs text-muted">
          {version}
          {size ? ` · ${size}` : ''} · installatie per gebruiker, geen beheerdersrechten nodig
        </p>
      </div>

      <div data-hero>
        <AppPreview />
      </div>

      <dl
        data-hero
        className="mx-auto mt-14 grid max-w-4xl grid-cols-2 gap-px overflow-hidden rounded-2xl border border-stroke bg-stroke sm:grid-cols-4"
      >
        {STATS.map((s) => (
          <div key={s.label} className="bg-bg2 px-4 py-6 text-center">
            <dt className="sr-only">{s.label}</dt>
            <dd>
              <span className="block text-2xl font-bold tracking-tight sm:text-3xl">{s.value}</span>
              <span className="mt-1 block text-xs leading-snug text-muted">{s.label}</span>
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
