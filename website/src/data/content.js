// Alle paginatekst op één plek. Elk cijfer hieronder komt uit de repo, niet uit
// een schatting — de bronversie staat er per blok bij zodat het bij een
// volgende ronde na te lopen is.
//
//   108 apps          -> apps.json telt 110 records, waarvan Proton VPN en
//                        Proton Drive bewust dubbel staan (v1.0.2 / v1.2.6)
//   124 tweaks        -> nageteld over BuildAll() in TweakService (v1.2.4)
//   12 categorieen    -> enum TweakCategory in Models/Tweak.cs
//   1347 strings      -> stringtabellen NL + EN (v1.2.9.5)
//   55 bloatware      -> 68 entries, 55 unieke producten (v1.2.5)

export const STATS = [
  { value: '108', label: 'apps in de catalogus' },
  { value: '124', label: 'tweaks in 12 categorieën' },
  { value: '1', label: 'toestemmingsvraag per batch' },
  { value: 'NL / EN', label: 'live te wisselen' },
];

// De sterkste punten voor een bezoeker, niet de volledige changelog.
export const FEATURES = [
  {
    icon: 'package',
    title: 'Apps in bulk installeren',
    desc:
      'Kies uit 108 gecureerde apps of zoek rechtstreeks in de winget-repo. Ze installeren parallel, ' +
      'met voortgang per app en een knop om alleen de mislukte opnieuw te proberen.',
    meta: 'winget · Microsoft Store · directe downloads',
  },
  {
    icon: 'shield',
    title: 'Eén UAC-prompt per batch',
    desc:
      'Windows vraagt één keer om toestemming voor de hele reeks in plaats van bij elke app opnieuw. ' +
      'Weiger je de prompt, dan valt de app netjes terug op de installatie per app.',
    meta: 'sinds v1.0.5',
  },
  {
    icon: 'sliders',
    title: '124 tweaks, allemaal omkeerbaar',
    desc:
      'Privacy, prestaties, verkenner, taakbalk, startmenu, contextmenu, updates en meer. De app leest de ' +
      'huidige stand rechtstreeks uit het register, dus je ziet per tweak wat er nú aanstaat.',
    meta: '12 categorieën met uitleg en use-case',
  },
  {
    icon: 'broom',
    title: 'Debloat en deep clean',
    desc:
      'Verwijder Microsoft- en OEM-bloatware, en ruim op wat achterblijft: verweesde mappen, services, ' +
      'geplande taken, firewallregels, registersleutels en caches. Altijd eerst een lijst om aan te vinken.',
    meta: '17 opruimcategorieën',
  },
  {
    icon: 'archive',
    title: 'Je hele configuratie in één bestand',
    desc:
      'Exporteer je apps, je tweaks en je voorkeuren als één back-up en zet je volgende pc in één import klaar. ' +
      'Bij het importeren kies je per onderdeel wat je overneemt.',
    meta: 'sinds v1.2.9',
  },
  {
    icon: 'languages',
    title: 'Nederlands en Engels',
    desc:
      'De hele app spreekt beide talen — inclusief de app-catalogus en de omschrijving van elke tweak. ' +
      'Wisselen kan tijdens het gebruik, zonder herstart. Standaard volgt de app je Windows-weergavetaal.',
    meta: '1347 vertaalde teksten',
  },
  {
    icon: 'sparkle',
    title: 'Veiligheidsnet vooraf',
    desc:
      'Vóór het toepassen van tweaks maakt de app een registermomentopname die je per stuk kunt terugzetten. ' +
      'Vóór debloat en deep clean kan er een Windows-systeemherstelpunt gezet worden.',
    meta: 'sinds v0.9.5',
  },
  {
    icon: 'refresh',
    title: 'Houdt zichzelf bij',
    desc:
      'De app werkt zichzelf bij via GitHub-releases. Optioneel draait er dagelijks een taak die je ' +
      'winget-apps bijwerkt en het resultaat als Windows-melding toont — ook op accustroom.',
    meta: 'v1.0.13 · v1.0.14',
  },
];

export const STEPS = [
  {
    title: 'Downloaden en starten',
    desc:
      'De installer is per gebruiker, dus je hebt geen beheerdersrechten nodig om Setup Toolbox te installeren.',
  },
  {
    title: 'Aanvinken wat je wilt',
    desc:
      'Blader door de catalogus of zoek. Bij de tweaks zie je per regel wat er nu aanstaat, wat het doet en waarvoor je het zou willen.',
  },
  {
    title: 'Eén keer uitvoeren',
    desc:
      'Windows vraagt één keer om toestemming. Daarna zie je per app en per tweak wat er gebeurt, en wat er eventueel misging.',
  },
  {
    title: 'Meenemen naar de volgende pc',
    desc:
      'Exporteer de hele configuratie naar één bestand en importeer die elders — per onderdeel te kiezen.',
  },
];

export const SAFETY = [
  {
    title: 'Nooit verwijderen zonder voorbeeld',
    desc: 'Debloat en deep clean tonen altijd eerst de volledige lijst met paden en groottes. Jij vinkt aan, de app raadt niets.',
  },
  {
    title: 'Momentopname vóór elke tweak',
    desc: 'De app leest de bestaande registerwaarden uit en parkeert die, inclusief "deze waarde bestond nog niet". Terugzetten kan per momentopname.',
  },
  {
    title: 'Systeemherstelpunt bij zwaar werk',
    desc: 'Voor de opruimacties die echt bestanden weggooien kan Windows eerst een herstelpunt zetten — één keer instellen, daarna automatisch.',
  },
  {
    title: 'Fouten worden gemeld, niet verstopt',
    desc: 'Mislukt een installatie, dan zie je de reden en de echte foutcode van winget, met een logbestand dat je kunt openen.',
  },
];

export const FAQ = [
  {
    q: 'Heb ik beheerdersrechten nodig?',
    a:
      'Niet om te installeren: Setup Toolbox installeert per gebruiker. Tweaks, debloat en deep clean schrijven wél in ' +
      'systeemonderdelen, dus daarvoor vraagt Windows om toestemming — één keer voor de hele reeks, niet per onderdeel.',
  },
  {
    q: 'Kan ik wijzigingen terugdraaien?',
    a:
      'Ja. Elke tweak is omkeerbaar, en de app maakt vóór het toepassen een momentopname van de registerwaarden die ' +
      'hij gaat wijzigen. Voor debloat en deep clean kan er daarnaast een Windows-systeemherstelpunt gezet worden.',
  },
  {
    q: 'Waar komen de apps vandaan?',
    a:
      'Uit winget, de pakketbeheerder die in Windows 11 ingebouwd zit, en uit de Microsoft Store. Apps zonder ' +
      'winget-pakket krijgen een directe downloadlink naar de leverancier. Setup Toolbox host zelf geen software.',
  },
  {
    q: 'Welke versie van Windows heb ik nodig?',
    a: 'Windows 11. De app is gebouwd op .NET 10 met WinUI 3; de benodigde onderdelen zitten in de installer.',
  },
  {
    q: 'Wat kost het?',
    a:
      'Niets. De app is gratis te downloaden en te gebruiken, privé en zakelijk. Er zit geen account, geen ' +
      'abonnement en geen telemetrie in.',
  },
  {
    q: 'Is dit open source?',
    a:
      'De broncode staat publiek op GitHub en is vrij in te zien; verbeteringen mogen als pull request voorgesteld ' +
      'worden. De licentie is wél proprietary: hergebruik of herdistributie van de code mag niet zonder toestemming. ' +
      'De app zelf mag iedereen gebruiken.',
  },
];
