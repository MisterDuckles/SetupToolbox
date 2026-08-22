import { useRef } from 'react';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import { useGSAP } from '@gsap/react';

import { Header } from './components/Header';
import { Hero } from './components/Hero';
import { ScreenshotGallery } from './components/ScreenshotGallery';
import { DownloadCta, Faq, Features, Footer, HowItWorks, Safety } from './components/Sections';
import { useLatestRelease } from './hooks/useLatestRelease';
import { useTheme } from './hooks/useTheme';

gsap.registerPlugin(useGSAP, ScrollTrigger);

export default function App() {
  const root = useRef(null);
  const { theme, toggle } = useTheme();
  const { version, url: downloadUrl, size } = useLatestRelease();

  useGSAP(
    () => {
      // Wie beweging heeft uitgezet krijgt de pagina meteen in eindtoestand.
      // Belangrijk: dan wordt er ook geen enkele from()-tween aangemaakt, dus
      // niets kan onzichtbaar blijven hangen.
      const reduced =
        typeof window !== 'undefined' &&
        window.matchMedia &&
        window.matchMedia('(prefers-reduced-motion: reduce)').matches;
      if (reduced) return;

      // De kleurvlakken op de achtergrond traag laten drijven.
      gsap.to('.blob-1', { x: 80, y: 60, scale: 1.08, duration: 16, repeat: -1, yoyo: true, ease: 'sine.inOut' });
      gsap.to('.blob-2', { x: -70, y: 45, scale: 1.12, duration: 19, repeat: -1, yoyo: true, ease: 'sine.inOut' });
      gsap.to('.blob-3', { x: 55, y: -50, scale: 1.15, duration: 22, repeat: -1, yoyo: true, ease: 'sine.inOut' });

      // ── Intro ──────────────────────────────────────────────────────────────
      // Eén tijdlijn, zodat er precies één ding is dat af moet lopen. useGSAP
      // draait in een layout-effect en from() zet zijn begintoestand meteen,
      // dus dit gebeurt vóór de eerste schilderbeurt: geen sprong bij het laden.
      const intro = gsap.timeline();
      intro.from('.word', { yPercent: 118, duration: 0.85, ease: 'power4.out', stagger: 0.045 });
      intro.from(
        '[data-hero]',
        { y: 20, autoAlpha: 0, duration: 0.75, ease: 'power3.out', stagger: 0.1 },
        0.3,
      );

      // Vangnet. De intro is het enige stuk dat inhoud tijdelijk onzichtbaar
      // maakt; blijft de tijdlijn om wat voor reden dan ook steken (stilgezette
      // rAF-ticker in een achtergrondtab, een fout verderop), dan zetten we 'm
      // alsnog op de eindstand. Een pagina die niet beweegt is prima, een
      // pagina die leeg blijft niet.
      const guard = window.setTimeout(() => intro.progress(1), 2500);
      intro.eventCallback('onComplete', () => window.clearTimeout(guard));

      // ── Onthullen bij het scrollen ─────────────────────────────────────────
      // Elk blok schuift omhoog en fadet in zodra 'ie in beeld komt. once: true,
      // dus terugscrollen speelt niets opnieuw af — een sectie die bij elke
      // passage opnieuw begint leest als een storing, niet als een effect.
      //
      // De vorige ronde animeerde hier bewust GEEN opacity, na een bug waarbij de
      // pagina leeg bleef. De oorzaak daarvan was de intro-tijdlijn die in een
      // achtergrondtab op progress 0 bleef hangen — niet het scrollen zelf. Die
      // faalmodus wordt hieronder gericht afgevangen, dus de reveals mogen wel
      // een echte fade doen.
      const reveals = [];

      const trigger = (el, start) => ({
        trigger: el,
        start: `top ${start}`,
        once: true,
      });

      // Losse blokken: sectiekoppen, de veiligheidstekst, de download-kaart.
      gsap.utils.toArray('[data-reveal]').forEach((el) => {
        reveals.push(
          gsap.from(el, {
            y: 34,
            autoAlpha: 0,
            duration: 0.6,
            ease: 'power2.out',
            scrollTrigger: trigger(el, '88%'),
          }),
        );
      });

      // Groepen: alles met dezelfde ouder krijgt één tween met stagger, zodat
      // kaarten ná elkaar binnenkomen in plaats van als blok. Per ouder groeperen
      // en niet in één selector, anders speelt de tweede rij kaarten al af
      // terwijl 'ie nog buiten beeld staat.
      const groups = new Map();
      gsap.utils.toArray('[data-reveal-item]').forEach((el) => {
        const parent = el.parentElement;
        if (!groups.has(parent)) groups.set(parent, []);
        groups.get(parent).push(el);
      });

      groups.forEach((els, parent) => {
        reveals.push(
          gsap.from(els, {
            y: 30,
            autoAlpha: 0,
            duration: 0.55,
            ease: 'power2.out',
            stagger: 0.07,
            scrollTrigger: trigger(parent, '85%'),
          }),
        );
      });

      // Vangnet, gericht op precies de faalmodus van de vorige ronde: een
      // rAF-ticker die in een achtergrondtab stilstaat laat een from()-tween op
      // progress 0 staan, en dan blijft de inhoud ONZICHTBAAR in plaats van
      // alleen niet-bewegend. Zodra de tab weer zichtbaar is: opnieuw opmeten, en
      // alles wat al getriggerd is maar niet afgerond op de eindstand zetten.
      const onVisible = () => {
        if (document.visibilityState !== 'visible') return;
        ScrollTrigger.refresh();
        reveals.forEach((t) => {
          if (t.scrollTrigger && t.scrollTrigger.progress > 0 && t.progress() < 1) t.progress(1);
        });
      };
      document.addEventListener('visibilitychange', onVisible);

      return () => {
        window.clearTimeout(guard);
        document.removeEventListener('visibilitychange', onVisible);
      };
    },
    { scope: root },
  );

  return (
    <div ref={root} className="relative min-h-screen">
      <a href="#inhoud" className="skip-link btn-ghost rounded-lg px-4 py-2 text-sm font-medium">
        Naar de inhoud
      </a>

      <div className="aurora" aria-hidden="true">
        <div className="blob blob-1" />
        <div className="blob blob-2" />
        <div className="blob blob-3" />
      </div>
      <div className="grid-overlay" aria-hidden="true" />

      <Header theme={theme} onToggleTheme={toggle} downloadUrl={downloadUrl} />

      <main id="inhoud">
        <Hero version={version} downloadUrl={downloadUrl} size={size} />
        <Features />
        <ScreenshotGallery />
        <HowItWorks />
        <Safety />
        <Faq />
        <DownloadCta version={version} downloadUrl={downloadUrl} size={size} />
      </main>

      <Footer />
    </div>
  );
}
