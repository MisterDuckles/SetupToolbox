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

// ── Schermafbeeldingen ─────────────────────────────────────────────────────
// Acht bestanden in website/public/screenshots/: <scherm>-nl.webp en
// <scherm>-en.webp.
//
// ⚠ BIJWERKEN BIJ NIEUWE SCREENSHOTS ⚠
// SHOT_SIZE hieronder is de ENIGE plek in de hele site waar de pixelmaten van de
// beelden staan. Die twee getallen voeden drie dingen tegelijk:
//   1. de width/height-attributen op de <img>, waarmee de browser de plek
//      alvast vrijhoudt;
//   2. SHOT_FRAME_RATIO — de vaste verhouding van het frame, zodat de doos zijn
//      eindhoogte heeft vóór er één byte binnen is en er tijdens het laden
//      niets verschuift;
//   3. de hoogte die het frame op elke schermbreedte inneemt.
// Nergens anders — niet in de CSS, niet in de JSX en niet per scherm hieronder —
// staat nog een breedte, een hoogte of een verhouding. Komt er een nieuwe reeks
// exports met een andere verhouding, dan zijn deze twee getallen het enige dat
// mee hoeft; alles eromheen rekent zich opnieuw uit. Lees ze uit de VP8-header
// van de .webp-bestanden zelf, niet uit de crop-maten van het manifest.
//
// Dat één maat voor alle acht volstaat, is geen toeval: de beelden zijn bewust
// PIXELGELIJK uitgesneden — niet alleen binnen een taalpaar maar over de vier
// schermen heen, met het app-icoon in de titelbalk als anker. Daardoor springt
// er niets, niet bij het wisselen van taal en niet bij het wisselen van scherm.
// Een nieuwe reeks moet dat aanhouden; verschillen de acht straks onderling in
// formaat, dan keert de layout-sprong terug en moet de maat weer per scherm
// bijgehouden worden (SHOT_FRAME_RATIO wordt dan de laagste breedte/hoogte van
// de reeks, dus de hoogste doos). object-contain op de <img> is het vangnet:
// een afwijkend beeld wordt dan niet vervormd maar met een band in --shot-mat-2
// ingepast.
//
// LET OP bij het opnieuw schieten: de vier navigatielabels zijn in beide talen
// IDENTIEK ("Apps", "Tweaks", "Debloat", "Deep clean"). De navigatierail toont
// het taalverschil dus niet — elke opname moet de paginakop en de omschrijvingen
// in beeld hebben, want daar zit het verschil.
//
// De alt-teksten citeren de app-strings LETTERLIJK uit data/strings.nl.json en
// data/strings.en.json, inclusief trema's ("categorieën", "geïnstalleerde") en
// de em-dash in "Debloat — Apps". Elke alt begint bij het scherm zelf en niet
// bij "dezelfde als hierboven": wie op EN drukt zonder de NL-versie gehoord te
// hebben, moet er evengoed uit kunnen opmaken waar hij naar kijkt. NL en EN
// noemen bewust dezelfde elementen in dezelfde volgorde, zodat de twee talen
// naast elkaar te leggen zijn. De aantallen tussen haakjes (35, 86, 6 / 11)
// komen uit de huidige opnames en moeten bij een nieuwe reeks nagelopen worden.

// De werkelijke maat van alle acht de .webp-bestanden. Zie de waarschuwing
// hierboven: dit is de plek om aan te passen als er nieuwe opnames komen.
export const SHOT_SIZE = { width: 1600, height: 935 };

// De vaste verhouding van het beeldframe in de galerij, AFGELEID uit SHOT_SIZE
// en nergens anders herhaald. Een los getal in de CSS of in de JSX zou een
// tweede waarheid zijn die bij de eerstvolgende nieuwe reeks uit de pas loopt —
// precies de fout die deze constante voorkomt. Bijwerken hoeft dus nooit: pas
// SHOT_SIZE aan en dit rekent zich mee.
export const SHOT_FRAME_RATIO = SHOT_SIZE.width / SHOT_SIZE.height;

export const GALLERY = {
  eyebrow: 'In beeld',
  title: 'Vier schermen, twee talen',
  lead:
    'Dit is de app zelf. Zet de schakelaar op EN en dezelfde vier schermen staan er in het Engels — dat is geen ' +
    'keuze die je bij de installatie vastlegt, maar een knop in de app.',
  tablistLabel: 'Kies een scherm',
  langGroupLabel: 'Taal van de schermafbeeldingen',
  shownIn: 'Getoond in het',

  // Wat de live region onder de figure voorleest zodra er een ander scherm of
  // een andere taal gekozen wordt. Zonder deze regel hoort iemand die op EN
  // drukt alleen "ingedrukt" — dat de knop aan staat, maar niet DAT er een
  // ander beeld voor in de plaats is gekomen.
  status: (screen, language) => `Schermafbeelding: ${screen}, getoond in het ${language}.`,
};

export const SHOT_LANGS = [
  { code: 'nl', short: 'NL', label: 'Nederlands' },
  { code: 'en', short: 'EN', label: 'Engels' },
];

export const SCREENS = [
  {
    screen: 'apps',
    icon: 'package',
    title: 'Apps-catalogus',
    caption: 'Tien categorieën met een zoekveld erboven; onderin telt de balk mee hoeveel apps je hebt aangevinkt.',
    alt: {
      nl:
        "De Apps-pagina van Setup Toolbox in het Nederlands, met links de navigatie Apps / Tweaks / Debloat. Tien " +
        "categorietegels: Browsers, Ontwikkeling, Beveiliging & privacy, Productiviteit, Communicatie, Media & " +
        "entertainment, Gaming, Hulpprogramma's, Creatief & ontwerp en App-suites. Rechtsboven het zoekveld 'Zoek " +
        "apps of categorieën'. De laatste tegel is maar deels zichtbaar; de lijst scrollt door. Onderaan de balk " +
        "'0 apps geselecteerd' met de knoppen 'Selectie wissen' en " +
        "'Geselecteerde apps installeren'.",
      en:
        "De Apps-pagina van Setup Toolbox in het Engels, met links de navigatie Apps / Tweaks / Debloat. Tien " +
        "categorietegels: Browsers, Development, Security & Privacy, Productivity, Communication, Media & " +
        "Entertainment, Gaming, Utilities, Creative & Design en App Suites. Rechtsboven het zoekveld 'Search apps " +
        "or categories'. De laatste tegel is maar deels zichtbaar; de lijst scrollt door. Onderaan de balk " +
        "'0 apps selected' met de knoppen 'Clear all' en 'Install selected apps'.",
    },
  },
  {
    screen: 'tweaks',
    icon: 'sliders',
    title: 'Tweaks',
    caption:
      'Twaalf categorieën, elk met een teller die laat zien hoeveel tweaks er nú actief zijn — rechtstreeks ' +
      'uitgelezen uit het register.',
    alt: {
      nl:
        "De Tweaks-pagina van Setup Toolbox in het Nederlands, met twaalf categorietegels: Verkenner, Taakbalk, " +
        "Startmenu, Advertenties & tracking, AI / Copilot, Privacy, UI / Thema, Prestaties, Contextmenu, " +
        "Meldingen & vergrendelscherm, Updates en Beveiliging. De tegels tonen een teller zoals '6 / 11 actief'; " +
        "Startmenu draagt het groene label 'Volledig actief'. Rechtsboven de knop 'Explorer herstarten' en het " +
        "zoekveld 'Zoek tweaks'. De onderste tegelrij valt deels buiten beeld. Onderaan de balk 'Geen openstaande " +
        "wijzigingen' met de knoppen 'Verwerpen' en " +
        "'Toepassen'.",
      en:
        "De Tweaks-pagina van Setup Toolbox in het Engels, met twaalf categorietegels: Explorer, Taskbar, Start " +
        "Menu, Ads & Tracking, AI / Copilot, Privacy, UI / Theme, Performance, Context Menu, Notifications & Lock " +
        "Screen, Updates en Security. De tegels tonen een teller zoals '6 / 11 active'; Start Menu draagt het " +
        "groene label 'Fully applied'. Rechtsboven de knop 'Restart Explorer' en het zoekveld 'Search tweaks'. " +
        "De onderste tegelrij valt deels buiten beeld. Onderaan de balk 'No pending changes' met de knoppen " +
        "'Discard' en 'Apply'.",
    },
  },
  {
    screen: 'debloat',
    icon: 'broom',
    title: 'Debloat',
    caption:
      'De lijsten staan standaard dicht: eerst uitklappen en aanvinken, pas daarna verwijdert de app iets.',
    alt: {
      nl:
        "De pagina 'Debloat — Apps' van Setup Toolbox in het Nederlands, met bovenaan de uitlegkaart 'Apps " +
        "debloat' over het verwijderen van Microsoft pre-install bloat, OEM bundleware en apps uit de catalogus. " +
        "Daaronder twee ingeklapte secties: 'Microsoft-bloatware (35)' en 'Alle geïnstalleerde apps (86)', elk met " +
        "de knop 'Alles selecteren' en een uitklappijl. Rechtsboven de knop 'Vernieuwen'. Onderaan de balk 'Niets " +
        "geselecteerd' met de knop 'Geselecteerde verwijderen'.",
      en:
        "De pagina 'Debloat — Apps' van Setup Toolbox in het Engels, met bovenaan de uitlegkaart 'Apps debloat' " +
        "over het verwijderen van Microsoft pre-install bloat, OEM bundleware en apps uit de catalogus. Daaronder " +
        "twee ingeklapte secties: 'Microsoft bloatware (35)' en 'All installed apps (86)', elk met de knop 'Select " +
        "all' en een uitklappijl. Rechtsboven de knop 'Refresh'. Onderaan de balk 'Nothing selected' met de knop " +
        "'Uninstall selected'.",
    },
  },
  {
    screen: 'deepclean',
    icon: 'sparkle',
    title: 'Deep clean',
    caption:
      'Twee scans om zelf te starten — Windows-caches en achtergebleven restanten. Scannen is nog niet opruimen: ' +
      'de lijst komt eerst.',
    alt: {
      nl:
        "De pagina 'Debloat — Deep clean' van Setup Toolbox in het Nederlands, met bovenaan de uitleg " +
        "'Systeembrede opruiming'. Daaronder twee blokken: 'Windows-caches' (temp-mappen, Update-cache, " +
        "Prullenbak, Prefetch, Windows.old en browsercaches) met de knop 'Caches scannen', en 'Restanten " +
        "(volledige deep clean)' (mappen zonder bijbehorende app, dode registry-keys, verweesde services) met de " +
        "knop 'Restanten scannen'. Deze pagina heeft geen actiebalk onderaan.",
      en:
        "De pagina 'Debloat — Deep clean' van Setup Toolbox in het Engels, met bovenaan de uitleg 'System-wide " +
        "cleanup'. Daaronder twee blokken: 'Windows caches' (temp folders, Update cache, Recycle Bin, Prefetch, " +
        "Windows.old en browsercaches) met de knop 'Scan caches', en 'Leftovers (full deep clean)' (mappen zonder " +
        "bijbehorende app, dode registry-keys, verweesde services) met de knop 'Scan leftovers'. Deze pagina heeft " +
        "geen actiebalk onderaan.",
    },
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
