import { useRef } from 'react';
import { gsap } from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
import { useGSAP } from '@gsap/react';

import { Header } from './components/Header';
import { Hero } from './components/Hero';
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
      // Bewust alléén een transform, geen opacity: inhoud onder de vouw is dus
      // ook zichtbaar als een tween nooit afgaat. Het schuift hooguit niet.
      gsap.utils.toArray('[data-feature]').forEach((el, i) => {
        gsap.from(el, {
          scrollTrigger: { trigger: el, start: 'top 90%', once: true },
          y: 26,
          duration: 0.55,
          ease: 'power2.out',
          delay: (i % 4) * 0.06,
        });
      });

      gsap.from('[data-download]', {
        scrollTrigger: { trigger: '#downloaden', start: 'top 88%', once: true },
        y: 22,
        scale: 0.985,
        duration: 0.65,
        ease: 'power3.out',
      });

      return () => window.clearTimeout(guard);
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
        <HowItWorks />
        <Safety />
        <Faq />
        <DownloadCta version={version} downloadUrl={downloadUrl} size={size} />
      </main>

      <Footer />
    </div>
  );
}
