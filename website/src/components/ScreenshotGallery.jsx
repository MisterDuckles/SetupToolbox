import { useCallback, useRef, useState } from 'react';

import { GALLERY, SCREENS, SHOT_FRAME_RATIO, SHOT_LANGS, SHOT_SIZE } from '../data/content';
import { shotUrl } from '../lib/constants';
import { IconBroom, IconPackage, IconSliders, IconSparkle } from './Icons';

// Dezelfde vier iconen die de app zelf in zijn navigatie gebruikt, zodat een tab
// herkenbaar is zodra je het beeld ernaast ziet. Geen nieuw icoon, geen nieuwe stijl.
const ICONS = {
  package: IconPackage,
  sliders: IconSliders,
  broom: IconBroom,
  sparkle: IconSparkle,
};

// De langste taalnaam telt mee in de hoogte die het bijschrift kan innemen; zie
// de hoogtereservering in de figcaption hieronder.
const LONGEST_LANG = SHOT_LANGS.reduce((a, b) => (b.label.length > a.label.length ? b : a));

const tabId = (screen) => `schermen-tab-${screen}`;
const panelId = (screen) => `schermen-paneel-${screen}`;

export function ScreenshotGallery() {
  const [active, setActive] = useState(SCREENS[0].screen);
  const [lang, setLang] = useState(SHOT_LANGS[0].code);

  // De vier panelen staan er altijd — anders wijst aria-controls naar een id dat
  // niet bestaat — maar de BEELDEN komen pas in de DOM zodra een tab voor het
  // eerst geopend is, en gaan er daarna nooit meer uit. Standaard staan er dus
  // twee <img> klaar (het eerste scherm in beide talen) in plaats van acht, en
  // een tab die je al gezien hebt komt bij terugkeren meteen terug.
  const [opened, setOpened] = useState(() => new Set([SCREENS[0].screen]));

  const tabRefs = useRef([]);

  const open = useCallback((screen) => {
    setActive(screen);
    setOpened((prev) => (prev.has(screen) ? prev : new Set(prev).add(screen)));
  }, []);

  // Pijltjes, Home en End horen bij een tablist; zonder die toetsen is het geen
  // tablist maar een rij knoppen met de verkeerde rol erop. De index komt uit de
  // knop die de toets krijgt en niet uit `active`, zodat het ook klopt als de
  // focus om wat voor reden dan ook niet op de geselecteerde tab staat.
  const onKeyDown = (e, i) => {
    let next = null;
    if (e.key === 'ArrowRight') next = (i + 1) % SCREENS.length;
    else if (e.key === 'ArrowLeft') next = (i - 1 + SCREENS.length) % SCREENS.length;
    else if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = SCREENS.length - 1;
    if (next === null) return;
    e.preventDefault();
    open(SCREENS[next].screen);
    tabRefs.current[next]?.focus();
  };

  const current = SCREENS.find((s) => s.screen === active) ?? SCREENS[0];
  const currentLang = SHOT_LANGS.find((l) => l.code === lang) ?? SHOT_LANGS[0];

  return (
    // max-w-6xl, gelijk aan #functies erboven en #werkwijze eronder: dit is de
    // sectie waar extra breedte het beeld leesbaar houdt, en een kaartrand die
    // 64 px naar binnen springt ten opzichte van de buren leest als een fout.
    // De kop en de lead houden hun eigen smallere, gecentreerde blok.
    <section id="schermen" aria-labelledby="schermen-titel" className="mx-auto max-w-6xl px-4 py-20 sm:px-6 sm:py-24">
      <div data-reveal className="mx-auto max-w-2xl text-center">
        <p className="text-xs font-semibold tracking-[0.16em] text-accent uppercase">{GALLERY.eyebrow}</p>
        <h2 id="schermen-titel" className="mt-3 text-2xl font-bold sm:text-4xl">
          {GALLERY.title}
        </h2>
        <p className="mt-4 text-base leading-relaxed text-muted">{GALLERY.lead}</p>
      </div>

      {/* Twee losse keuzes op één balk: welk scherm, en in welke taal. Actief en
          inactief verschillen alleen in achtergrond, rand- en tekstkleur — nooit
          in font-weight of font-size, en beide toestanden hebben een `border`.
          De knopbreedte is daardoor in beide toestanden gelijk, dus de balk
          herschikt niet bij een klik. De actieve toestand zit in .shot-tab-on
          (index.css): accentrand plus accent-onderstreping, zodat de aanwijzing
          zelf de 3:1 van WCAG 1.4.11 haalt in plaats van alleen de tekstkleur. */}
      <div data-reveal className="mt-10 flex flex-wrap items-center justify-center gap-x-6 gap-y-3">
        <div role="tablist" aria-label={GALLERY.tablistLabel} className="flex flex-wrap justify-center gap-1.5">
          {SCREENS.map((s, i) => {
            const Icon = ICONS[s.icon];
            const selected = s.screen === active;
            return (
              <button
                key={s.screen}
                ref={(el) => {
                  tabRefs.current[i] = el;
                }}
                type="button"
                role="tab"
                id={tabId(s.screen)}
                aria-selected={selected}
                aria-controls={panelId(s.screen)}
                tabIndex={selected ? 0 : -1}
                onClick={() => open(s.screen)}
                onKeyDown={(e) => onKeyDown(e, i)}
                className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition ${
                  selected ? 'shot-tab-on' : 'border-transparent text-muted hover:bg-panel-3 hover:text-ink'
                }`}
              >
                <Icon className="hidden h-4 w-4 sm:block" />
                {s.title}
              </button>
            );
          })}
        </div>

        <span className="hidden h-6 w-px bg-stroke lg:block" aria-hidden="true" />

        {/* Bewust geen tweede tablist: het paneel blijft hetzelfde, alleen de
            rendering ervan verandert. Twee knoppen met aria-pressed is bovendien
            precies het patroon dat Header.jsx al voor de themawissel gebruikt.
            Dezelfde actieve stijl als de tabs: één toestand, één aanwijzing. */}
        <div
          role="group"
          aria-label={GALLERY.langGroupLabel}
          className="flex items-center gap-1 rounded-xl border border-stroke bg-panel/70 p-1"
        >
          {SHOT_LANGS.map((l) => {
            const on = l.code === lang;
            return (
              <button
                key={l.code}
                type="button"
                aria-pressed={on}
                onClick={() => setLang(l.code)}
                className={`rounded-lg border px-3 py-1.5 text-sm font-medium transition ${
                  on ? 'shot-tab-on' : 'border-transparent text-muted hover:text-ink'
                }`}
              >
                {l.short}
                <span className="sr-only"> — {l.label}</span>
              </button>
            );
          })}
        </div>
      </div>

      {/* data-reveal staat op de figure, niet op de beelden: de figure staat er
          vanaf mount en verdwijnt nooit, dus die tween loopt één keer af en kan
          geen beeld onzichtbaar achterlaten. Een <img> die zowel door GSAP als
          door de taalwissel op opacity gestuurd wordt, vecht met zichzelf — dat
          is precies de klasse fout achter de blanco-paginabug. */}
      <figure data-reveal className="mt-8">
        {/* .shot-card maakt de kaart in BEIDE thema's donker. Alle acht beelden
            tonen de app in zijn donkere thema; in een witte kaart stond daar een
            bijna-zwart vlak middenin, wat als een fout las in plaats van als een
            keuze. Nu is het een passe-partout: het beeld ligt in een donkere
            omkadering, licht of donker thema maakt niet uit. */}
        <div className="card shot-card overflow-hidden rounded-2xl p-1.5 sm:p-2">
          {/* `.card > *` zet in index.css al position: relative — precies het
              anker dat deze verhoudingsdoos nodig heeft. De verhouding staat
              inline en komt uit SHOT_FRAME_RATIO, dat in content.js uit SHOT_SIZE
              wordt afgeleid: één plek voor de maten, geen los getal hier of in de
              CSS. Het frame heeft daardoor zijn eindhoogte vóór er één byte
              binnen is: geen layout-verschuiving bij het laden, en ScrollTrigger
              hoeft niet opnieuw te meten. */}
          <div className="shot-frame overflow-hidden rounded-xl" style={{ aspectRatio: String(SHOT_FRAME_RATIO) }}>
            {SCREENS.map((s) => (
              <div
                key={s.screen}
                role="tabpanel"
                id={panelId(s.screen)}
                aria-labelledby={tabId(s.screen)}
                tabIndex={0}
                hidden={s.screen !== active}
                className="absolute inset-0"
              >
                {opened.has(s.screen)
                  ? SHOT_LANGS.map((l) => {
                      const on = l.code === lang;
                      // opacity: 0 en NIET display:none of visibility:hidden — een
                      // element zonder doos komt de intersectiecontrole van lazy
                      // loading nooit binnen, en dan wordt het inactieve beeld pas
                      // bij de klik opgehaald. Nu liggen beide talen al klaar en
                      // kost de wissel geen netwerkronde. Het verbergen zit op
                      // opacity, de toegankelijkheid op aria-hidden.
                      return (
                        <img
                          key={l.code}
                          src={shotUrl(s.screen, l.code)}
                          alt={s.alt[l.code]}
                          width={SHOT_SIZE.width}
                          height={SHOT_SIZE.height}
                          loading="lazy"
                          decoding="async"
                          draggable="false"
                          aria-hidden={on ? undefined : 'true'}
                          className={`absolute inset-0 h-full w-full object-contain transition-opacity duration-200 ${
                            on ? 'opacity-100' : 'pointer-events-none opacity-0'
                          }`}
                        />
                      );
                    })
                  : null}
              </div>
            ))}
          </div>
        </div>

        {/* Het bijschrift reserveert zijn eigen hoogte, net zoals het frame dat
            doet. Zonder die reservering verschilde het bijschrift op 360 px
            tussen 68 px ('Apps-catalogus') en 91 px ('Deep clean') en sprong de
            hele pagina eronder 23 px op en neer bij elke tabklik.

            Geen vaste min-height in px: die klopt maar op één breedte en laat op
            een breed scherm een gat staan. In plaats daarvan liggen alle vier de
            bijschriften in DEZELFDE gridcel, waarvan er drie op visibility:
            hidden staan. De cel is dus altijd zo hoog als het langste bijschrift
            bij de HUIDIGE breedte, op elke viewport en zonder magisch getal. De
            reserveringen gebruiken de langste taalnaam, zodat ook de taalwissel
            de hoogte niet kan veranderen. visibility: hidden houdt ze uit de
            toegankelijkheidsboom; aria-hidden staat er voor de zekerheid bij. */}
        <figcaption className="mt-4 grid text-center text-sm leading-relaxed text-muted">
          {SCREENS.map((s) => (
            <span key={s.screen} aria-hidden="true" className="invisible col-start-1 row-start-1">
              {s.caption} {GALLERY.shownIn} {LONGEST_LANG.label}.
            </span>
          ))}

          <span className="col-start-1 row-start-1">
            {current.caption}{' '}
            <span className="text-ink">
              {GALLERY.shownIn} {currentLang.label}.
            </span>
          </span>
        </figcaption>
      </figure>

      {/* Een schermlezer hoorde bij een druk op NL of EN alleen "ingedrukt": de
          knop wisselde van toestand, maar niets kondigde aan dat er een ánder
          beeld staat. Deze live region benoemt na elke wissel welk scherm er nu
          te zien is en in welke taal.

          Bewust BUITEN de figure en zonder data-reveal: alles met data-reveal
          gaat kort door autoAlpha: 0 (visibility: hidden), en een live region die
          op dat moment verborgen is, kondigt niets aan. */}
      <p role="status" aria-live="polite" aria-atomic="true" className="sr-only">
        {GALLERY.status(current.title, currentLang.label)}
      </p>
    </section>
  );
}
