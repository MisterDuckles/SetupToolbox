import { FAQ, FEATURES, SAFETY, STEPS } from '../data/content';
import { GITHUB_URL, ISSUES_URL, LICENSE_URL, RELEASES_PAGE } from '../lib/constants';
import {
  IconArchive,
  IconArrow,
  IconBroom,
  IconChevron,
  IconDownload,
  IconGitHub,
  IconLanguages,
  IconLogo,
  IconPackage,
  IconRefresh,
  IconShield,
  IconSliders,
  IconSparkle,
} from './Icons';

const ICONS = {
  package: IconPackage,
  sliders: IconSliders,
  broom: IconBroom,
  sparkle: IconSparkle,
  archive: IconArchive,
  languages: IconLanguages,
  shield: IconShield,
  refresh: IconRefresh,
};

function SectionHead({ eyebrow, title, lead, id }) {
  return (
    <div className="mx-auto max-w-2xl text-center">
      <p className="text-xs font-semibold tracking-[0.16em] text-accent uppercase">{eyebrow}</p>
      <h2 id={id} className="mt-3 text-2xl font-bold sm:text-4xl">
        {title}
      </h2>
      {lead ? <p className="mt-4 text-base leading-relaxed text-muted">{lead}</p> : null}
    </div>
  );
}

// Radiale gloed die de cursor volgt; de CSS in index.css leest --mx / --my.
function trackCursor(e) {
  const el = e.currentTarget;
  const r = el.getBoundingClientRect();
  el.style.setProperty('--mx', `${e.clientX - r.left}px`);
  el.style.setProperty('--my', `${e.clientY - r.top}px`);
}

export function Features() {
  return (
    <section id="functies" aria-labelledby="functies-titel" className="mx-auto max-w-6xl px-4 py-20 sm:px-6 sm:py-24">
      <SectionHead
        id="functies-titel"
        eyebrow="Wat het doet"
        title="Vier gereedschappen in één app"
        lead="Installeren, tweaken, debloaten en opschonen — met een back-up van je hele configuratie eromheen."
      />

      <ul className="mt-14 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {FEATURES.map((f) => {
          const Icon = ICONS[f.icon];
          return (
            <li key={f.title} data-feature onMouseMove={trackCursor} className="card rounded-2xl p-5 sm:p-6">
              <span className="inline-flex h-11 w-11 items-center justify-center rounded-xl border border-stroke bg-panel-3 text-accent">
                <Icon className="h-5 w-5" />
              </span>
              <h3 className="mt-4 text-base font-semibold">{f.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-muted">{f.desc}</p>
              {f.meta ? (
                <p className="mt-3 border-t border-stroke pt-3 text-[0.7rem] font-medium tracking-wide text-muted">
                  {f.meta}
                </p>
              ) : null}
            </li>
          );
        })}
      </ul>
    </section>
  );
}

export function HowItWorks() {
  return (
    <section id="werkwijze" aria-labelledby="werkwijze-titel" className="mx-auto max-w-6xl px-4 py-20 sm:px-6 sm:py-24">
      <SectionHead
        id="werkwijze-titel"
        eyebrow="Zo werkt het"
        title="Van lege installatie naar jouw pc"
        lead="Vier stappen, en de laatste hoef je op je volgende machine niet opnieuw te doen."
      />

      <ol className="mt-14 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {STEPS.map((s, i) => (
          <li key={s.title} data-feature onMouseMove={trackCursor} className="card rounded-2xl p-5 sm:p-6">
            <span
              aria-hidden="true"
              className="block text-3xl font-bold text-transparent [-webkit-text-stroke:1px_var(--color-stroke-strong)]"
            >
              {String(i + 1).padStart(2, '0')}
            </span>
            <h3 className="mt-3 text-base font-semibold">{s.title}</h3>
            <p className="mt-2 text-sm leading-relaxed text-muted">{s.desc}</p>
          </li>
        ))}
      </ol>
    </section>
  );
}

export function Safety() {
  return (
    <section id="veilig" aria-labelledby="veilig-titel" className="mx-auto max-w-6xl px-4 py-20 sm:px-6 sm:py-24">
      <div className="card overflow-hidden rounded-3xl p-6 sm:p-10 lg:p-14">
        <div className="grid gap-10 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.15fr)] lg:gap-16">
          <div>
            <span className="inline-flex h-12 w-12 items-center justify-center rounded-xl border border-stroke bg-panel-3 text-accent">
              <IconShield className="h-6 w-6" />
            </span>
            <h2 id="veilig-titel" className="mt-5 text-2xl font-bold sm:text-3xl">
              Niets gebeurt achter je rug om
            </h2>
            <p className="mt-4 text-base leading-relaxed text-muted">
              Een opschoontool die zelf beslist wat weg mag, is een opschoontool die je niet wilt. Setup Toolbox toont
              altijd eerst wat het van plan is en legt de oude situatie vast voordat het iets wijzigt.
            </p>
          </div>

          <ul className="grid gap-x-8 gap-y-6 sm:grid-cols-2">
            {SAFETY.map((s) => (
              <li key={s.title}>
                <h3 className="flex items-start gap-2 text-sm font-semibold">
                  <svg
                    viewBox="0 0 24 24"
                    className="mt-0.5 h-4 w-4 shrink-0 text-accent"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    aria-hidden="true"
                    focusable="false"
                  >
                    <path d="m5 12.5 4.5 4.5L19 7.5" />
                  </svg>
                  {s.title}
                </h3>
                <p className="mt-1.5 pl-6 text-sm leading-relaxed text-muted">{s.desc}</p>
              </li>
            ))}
          </ul>
        </div>
      </div>
    </section>
  );
}

export function Faq() {
  return (
    <section id="vragen" aria-labelledby="vragen-titel" className="mx-auto max-w-3xl px-4 py-20 sm:px-6 sm:py-24">
      <SectionHead id="vragen-titel" eyebrow="Veelgesteld" title="Vragen vooraf" />

      <div className="mt-12 divide-y divide-stroke border-y border-stroke">
        {FAQ.map((item) => (
          <details key={item.q} className="group">
            <summary className="flex items-center justify-between gap-4 py-5 text-left text-[0.95rem] font-semibold">
              {item.q}
              <IconChevron className="faq-chevron h-5 w-5 shrink-0 text-muted" />
            </summary>
            <p className="pr-9 pb-5 text-sm leading-relaxed text-muted">{item.a}</p>
          </details>
        ))}
      </div>
    </section>
  );
}

export function DownloadCta({ version, downloadUrl, size }) {
  return (
    <section id="downloaden" aria-labelledby="downloaden-titel" className="mx-auto max-w-4xl px-4 py-16 sm:px-6 sm:py-20">
      <div data-download onMouseMove={trackCursor} className="card overflow-hidden rounded-3xl p-8 text-center sm:p-14">
        <h2 id="downloaden-titel" className="text-2xl font-bold sm:text-4xl">
          Klaar om je pc in te richten?
        </h2>
        <p className="mx-auto mt-4 max-w-lg text-base leading-relaxed text-muted">
          Download de installer en start hem. Per gebruiker geïnstalleerd, dus je hebt geen beheerdersrechten nodig.
        </p>

        <div className="mt-8">
          <a
            href={downloadUrl}
            className="btn-primary inline-flex items-center justify-center gap-2.5 rounded-xl px-8 py-4 text-base font-semibold sm:text-lg"
          >
            <IconDownload className="h-5 w-5" />
            Download {version}
          </a>
        </div>

        <p className="mt-4 text-xs text-muted">
          Windows 11{size ? ` · ${size}` : ''} · gratis ·{' '}
          <a href={RELEASES_PAGE} rel="noopener" className="text-accent underline-offset-4 hover:underline">
            alle releases
          </a>
        </p>
      </div>
    </section>
  );
}

export function Footer() {
  return (
    <footer className="border-t border-stroke">
      <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
        <div className="flex flex-col gap-8 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="flex items-center gap-2.5 font-semibold">
              <IconLogo className="h-6 w-6" />
              Setup Toolbox
            </p>
            <p className="mt-2 max-w-xs text-sm leading-relaxed text-muted">
              Windows 11 inrichten, opschonen en afstellen — in het Nederlands of het Engels.
            </p>
          </div>

          <nav aria-label="Voettekst" className="flex flex-wrap gap-x-8 gap-y-3 text-sm">
            <a href="#functies" className="text-muted transition hover:text-ink">
              Functies
            </a>
            <a href={RELEASES_PAGE} rel="noopener" className="text-muted transition hover:text-ink">
              Releases
            </a>
            <a href={ISSUES_URL} rel="noopener" className="text-muted transition hover:text-ink">
              Probleem melden
            </a>
            <a
              href={GITHUB_URL}
              rel="noopener"
              className="inline-flex items-center gap-1.5 text-muted transition hover:text-ink"
            >
              <IconGitHub className="h-4 w-4" />
              GitHub
              <IconArrow className="h-3.5 w-3.5" />
            </a>
          </nav>
        </div>

        <div className="mt-10 flex flex-col gap-2 border-t border-stroke pt-6 text-xs text-muted sm:flex-row sm:items-center sm:justify-between">
          <p>&copy; 2026 Daan · Setup Toolbox</p>
          <p>
            Broncode in te zien op GitHub onder een{' '}
            <a href={LICENSE_URL} rel="noopener" className="text-accent underline-offset-4 hover:underline">
              proprietary licentie
            </a>{' '}
            — de app is gratis te gebruiken, de code niet vrij te hergebruiken.
          </p>
        </div>
      </div>
    </footer>
  );
}
