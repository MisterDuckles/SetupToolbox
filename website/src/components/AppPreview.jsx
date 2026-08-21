import { IconBroom, IconPackage, IconSliders, IconSparkle } from './Icons';

// Bewust GEEN schermafbeelding: er zit geen bruikbare screenshot in de repo en
// een nagemaakte foto van de app zou doen alsof. Dit is een schematische
// weergave van de indeling — echte navigatielabels, abstracte inhoud — en dat
// staat ook zo in het onderschrift.

const NAV = [
  { label: 'Apps', Icon: IconPackage, active: true },
  { label: 'Tweaks', Icon: IconSliders },
  { label: 'Debloat', Icon: IconBroom },
  { label: 'Deep clean', Icon: IconSparkle },
];

const ROWS = [
  { w: 'w-28', status: 'done', tint: 'from-[#3b82f6] to-[#2563eb]' },
  { w: 'w-36', status: 'done', tint: 'from-[#f97316] to-[#ea580c]' },
  { w: 'w-24', status: 'busy', tint: 'from-[#a855f7] to-[#7c3aed]' },
  { w: 'w-32', status: 'wait', tint: 'from-[#22c55e] to-[#16a34a]' },
  { w: 'w-20', status: 'wait', tint: 'from-[#ef4444] to-[#dc2626]' },
];

function Status({ kind }) {
  if (kind === 'done') {
    return (
      <svg viewBox="0 0 24 24" className="h-4 w-4 shrink-0 text-emerald-500" aria-hidden="true" focusable="false">
        <path
          d="m5 12.5 4.5 4.5L19 7.5"
          fill="none"
          stroke="currentColor"
          strokeWidth="2.4"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    );
  }
  if (kind === 'busy') {
    return <span className="h-2.5 w-2.5 shrink-0 rounded-full bg-[var(--color-btn-from)]" aria-hidden="true" />;
  }
  return <span className="h-2.5 w-2.5 shrink-0 rounded-full border border-stroke-strong" aria-hidden="true" />;
}

export function AppPreview() {
  return (
    <figure className="mx-auto mt-14 w-full max-w-4xl">
      <div
        role="img"
        aria-label="Schematische weergave van Setup Toolbox: een venster met links de navigatie Apps, Tweaks, Debloat en Deep clean, en rechts een lijst apps die geïnstalleerd wordt met een voortgangsbalk onderaan."
        className="card overflow-hidden rounded-2xl"
      >
        {/* Titelbalk */}
        <div className="flex items-center gap-2 border-b border-stroke bg-panel-2/60 px-3 py-2.5 sm:px-4">
          <span className="bar bar-strong h-2 w-2 rounded-full" aria-hidden="true" />
          <span className="ml-1 text-[0.7rem] font-medium text-muted sm:text-xs">Setup Toolbox</span>
          <div className="ml-auto flex items-center gap-3 text-muted" aria-hidden="true">
            <span className="block h-px w-3 bg-current" />
            <span className="block h-2.5 w-2.5 border border-current" />
            <svg viewBox="0 0 10 10" className="h-2.5 w-2.5" fill="none" stroke="currentColor" strokeWidth="1.4">
              <path d="m1 1 8 8M9 1 1 9" />
            </svg>
          </div>
        </div>

        <div className="flex">
          {/* Navigatiekolom */}
          <div className="rail hidden w-40 shrink-0 border-r border-stroke p-3 sm:block">
            {NAV.map(({ label, Icon, active }) => (
              <div
                key={label}
                className={`mb-1 flex items-center gap-2.5 rounded-lg px-2.5 py-2 text-xs ${
                  active ? 'bg-panel-3 font-semibold text-ink' : 'text-muted'
                }`}
              >
                <Icon className="h-4 w-4" />
                {label}
              </div>
            ))}
            <div className="mt-4 border-t border-stroke pt-3">
              <span className="bar w-20" aria-hidden="true" />
            </div>
          </div>

          {/* Inhoud */}
          <div className="min-w-0 flex-1 p-4 sm:p-5">
            <div className="mb-4 flex items-center justify-between gap-3">
              <span className="bar bar-strong w-24 sm:w-32" aria-hidden="true" />
              <span className="rounded-full border border-stroke px-2.5 py-1 text-[0.65rem] font-medium text-muted">
                5 geselecteerd
              </span>
            </div>

            <ul className="space-y-2">
              {ROWS.map((row, i) => (
                <li
                  key={i}
                  className="flex items-center gap-3 rounded-xl border border-stroke bg-panel-2/50 px-3 py-2.5"
                >
                  <span
                    className={`h-7 w-7 shrink-0 rounded-lg bg-gradient-to-br ${row.tint} opacity-80`}
                    aria-hidden="true"
                  />
                  <span className="min-w-0 flex-1">
                    <span className={`bar ${row.w} max-w-full`} aria-hidden="true" />
                    <span className="bar mt-1.5 w-12 opacity-60" aria-hidden="true" />
                  </span>
                  <Status kind={row.status} />
                </li>
              ))}
            </ul>

            {/* Voortgangsregel */}
            <div className="mt-4 rounded-xl border border-stroke bg-panel-2/50 px-3 py-3">
              <div className="mb-2 flex items-center justify-between">
                <span className="bar w-16" aria-hidden="true" />
                <span className="text-[0.65rem] font-medium text-muted">2 van 5</span>
              </div>
              <div className="h-1.5 w-full overflow-hidden rounded-full bg-[var(--bar)]">
                <div
                  className="h-full w-2/5 rounded-full bg-gradient-to-r from-[var(--color-btn-from)] to-[var(--color-btn-to)]"
                  aria-hidden="true"
                />
              </div>
            </div>
          </div>
        </div>
      </div>

      <figcaption className="mt-3 text-center text-xs text-muted">
        Schematische weergave van de indeling — geen schermafbeelding.
      </figcaption>
    </figure>
  );
}
