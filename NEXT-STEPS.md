# SetupToolbox — Roadmap

Native Windows 11 app voor het bulk-installeren van apps via `winget`, plus debloat, tweaks en deep clean. **v1.2.9** (rebrand: heette voorheen *WingetAppDeployer*).

> WPF-historie tot en met v1.2.1 is gearchiveerd onder git tag `wpf-final-v1.2.1`. Repo is sinds v0.5.9 WinUI-only. De WinUI-lijn liep v0.5.x → v0.10.x en is bij de rebrand naar **Setup Toolbox** op **v1.0** gezet.
>
> **Let op bij het lezen van tags:** de WinUI-lijn staat sinds 2026-08-16 op **v1.2.1** en overlapt daarmee numeriek met de oude WPF-nummering — inclusief een exacte botsing, want de WPF-archieftag heet `wpf-final-v1.2.1`. De WPF-tags zijn te herkennen aan het `wpf-final-`-voorvoegsel; alles zonder voorvoegsel (`v1.0.0`, `v1.2.0`, `v1.2.1`) is WinUI. **v1.1.0 wordt bewust overgeslagen** — dat nummer is in het WPF-tijdperk al eens vergeven en de release is later verwijderd, dus hergebruik zou de historie dubbelzinnig maken.
>
> **Nummering volgt de uitleververvolgorde, niet het roadmap-vakje.** Een item krijgt zijn patchnummer op het moment dat het uitkomt. De roadmap-labels hieronder zijn dus een *voornemen*: pak je ze in een andere volgorde op, dan schuiven de nummers mee. Zo bleef de reeks na v1.2.0 gewoon aaneengesloten toen de security-fix (oorspronkelijk als v1.2.5 geschetst) als eerste af was.

**Stack:** .NET 10 + Windows App SDK 1.8 + WinUI 3 + unpackaged exe. Mica backdrop, native `Microsoft.UI.Xaml` controls. **Distributie:** publieke GitHub-repo (restrictieve/proprietary licentie) + per-user Inno Setup installer als release-asset; self-update via de GitHub releases-API. `apps.json` is gebundeld met de exe (geen live fetch).

---

## Open

> **Route naar de volgende release, vastgelegd op 2026-08-20 (user).** Drie items achter elkaar — **v1.2.8 → v1.2.9 → v1.2.10** — en zodra die binnen zijn wordt er een **milestone-release** gecut. De website schuift daarachteraan, want die trekt versie en downloadlink live uit de GitHub-API en heeft die Release dus nodig. Elk van de drie in een **eigen chat**: de context van de vertaalreeks is niet meer nodig en NEXT-STEPS.md is de overdracht.
>
> **Stand 2026-08-20: v1.2.8, v1.2.8.1 én v1.2.9 zijn af.** v1.2.8 is door user getest (install, uninstall, geweigerde UAC, reeds-geïnstalleerd); die test leverde v1.2.8.1 op omdat winget's eigen stdout nog Engels op de install-kaart stond. v1.2.9 (config-backup) is gebouwd en op de checkers geverifieerd; de round-trip zelf staat nog bij user open — zie de noot in die sectie. v1.2.8.1 zelf is op verzoek van user **niet meer visueel nagelopen** — zie de noot in die sectie. **Nog open aan verificatie: de export/import-round-trip van v1.2.9.** Volgende aan de beurt: **v1.2.10**, in een eigen chat.

### v1.2.10 — "Wat is er nieuw"-melding na een update

**Gevraagd door user op 2026-08-17.** Na een self-update en de automatische herstart krijgt de gebruiker nu niets te zien: de app komt terug en niets wijst erop dát er iets veranderd is, laat staan wát. Voorstel: bij de eerste start op een nieuw versienummer één compacte melding met de highlights — bv. "Toast-notificaties toegevoegd", "Taalkeuze NL/EN", "Auto-update draait nu ook op accu".

**Wat er al is om op te bouwen:** `GitHubService` kent de huidige `AssemblyVersion` en haalt release-info op; `SettingsService` kan de laatst-getoonde versie onthouden (zelfde patroon als `ParallelInstallsAsked`); `ToastHelper` en de InfoBar in `MainWindow` zijn allebei bestaande kanalen.

**Uit te werken vóór implementatie:**
- **Waar komt de tekst vandaan?** Meegebakken in de app (een `data/whatsnew.json` per versie, offline en voorspelbaar) versus de GitHub release-notes live ophalen (altijd actueel, maar afhankelijk van netwerk én van hoe netjes de release-body geschreven is). Meegebakken sluit aan op hoe `apps.json` en de vertaaltabellen al werken — en is meteen vertaalbaar, wat bij live release-notes niet kan.
- **Toast of in-app?** Een toast is makkelijk te missen en werkt niet elevated (zie het MSIX-onderzoek); de InfoBar bovenaan `MainWindow` staat er al voor de update-melding en is de logische plek. Mogelijk allebei.
- **Trigger-conditie.** "Eerste start nadat het versienummer wijzigde" is de simpele regel, maar die vuurt ook bij een schone eerste installatie — daar wil je 'm juist níét. Onderscheid nodig tussen "geüpdatet" en "nieuw geïnstalleerd".
- **Hoeveel regels?** Compact houden was de expliciete wens: een handvol bullets, niet de volledige changelog.
- **Vertaling.** Valt onder de multi-language-reeks: de highlights moeten in beide talen bestaan, dus het formaat moet daar rekening mee houden.

### v1.3.0 — Milestone-release: v1.2.1 t/m v1.2.10 uitleveren

**Besloten door user op 2026-08-20:** zodra v1.2.8 (install-voortgangsregel), v1.2.9 (config-backup) en v1.2.10 (wat-is-er-nieuw-melding) af zijn, wordt er **een Release gecut**. Niet eerder — deze drie horen als één verhaal naar buiten.

**Nummer:** `v1.3.0`. Volgt de bestaande conventie (milestone = minor bump; v1.2.0 was de vorige) en de CLAUDE.md-regel dat alleen milestones een GitHub Release met exe's krijgen. `v1.1.0` blijft overgeslagen — dat nummer is in het WPF-tijdperk al eens vergeven.

**Wat er dan uitgeleverd wordt** — geïnstalleerde gebruikers zitten sinds v1.2.0 op de oude build en krijgen dit in één keer:
- **v1.2.1** de kwetsbare `System.Drawing.Common` 4.7.0 uit de publish (GHSA-rxg9-xrhp-64gj). Dit is meteen het antwoord op het openstaande besluit onderaan bij *release-cadans*: die security-fix ligt er dan een halve reeks op te wachten.
- **v1.2.2 t/m v1.2.8.1** de complete multi-language-reeks: NL/EN met live wisselen, 1288 strings.
- **v1.2.9** de gebundelde config-backup.
- **v1.2.10** de wat-is-er-nieuw-melding — die is bij déze release meteen zijn eigen eerste testgeval.

**Volgorde-afhankelijkheid, niet vergeten:** v1.2.10 heeft de release-tekst nódig. Als de highlights meegebakken worden in een `data/whatsnew.json` (de voorkeursoptie daar), dan moet het `1.3.0`-blok in dat bestand geschreven zijn vóórdat de Release gepubliceerd wordt — anders levert de eerste update na v1.3.0 een lege melding op. Schrijf dat blok als onderdeel van v1.2.10, niet als losse stap bij het cutten.

**Checklist bij het cutten:** versienummer in de csproj, installer bouwen (`scripts/build-installer.ps1`), Release aanmaken met de exe als asset, en pas dáárna de website (die trekt versie + downloadlink live uit de GitHub-API).

### v1.3.1 — Website professionaliseren + bijwerken

De landingspagina **staat live** op `projects.dpvb.nl/setup-toolbox`.

> Correctie op mijn eigen audit van 2026-08-16: een geautomatiseerde fetch kreeg **403 Forbidden** op zowel de pagina als de bare domain, waaruit ik concludeerde dat de site niet gedeployed was. Dat was fout — user bevestigt dat 'ie gewoon online is. De 403 is vrijwel zeker bot-/WAF-blokkering op de hosting. **Niet opnieuw als "offline" diagnosticeren op basis van een fetch.**

**Wat er moet gebeuren:** de site is gebouwd in mei 2026 bij v1.0.0 en is sindsdien niet meer bijgewerkt, terwijl het project 14 patches verder is (toast-notificaties, install-lanes, deep clean-uitbreidingen, accu-fix). Twee doelen: (1) **professioneler uiterlijk**, (2) **inhoud synchroon met wat de app nu daadwerkelijk kan**. Versie + downloadlink komen al live uit de GitHub-API, dus die lopen automatisch mee — maar alleen als er ook echt een nieuwe Release gepubliceerd wordt (zie de release-cadans-notitie hieronder).

Bewust achteraan gezet, en sinds 2026-08-20 expliciet **ná v1.3.0**: de site trekt versie en downloadlink live uit de GitHub-API, dus zonder gepubliceerde Release valt er niets te synchroniseren. Bovendien kun je dan in één keer een actueel verhaal neerzetten in plaats van twee keer half.

### Zonder versienummer — klein onderhoud, gevonden tijdens v1.2.9

Boven komen tijdens het bouwen van de config-backup, bewust apart gehouden omdat ze bestaande code raken en niet de nieuwe bundel.

- **De losse app-import wist je huidige selectie zonder te vragen.** `SettingsPage.ImportButton_Click` roept `ImportAsync(..., clearFirst: true)` aan en er zit geen bevestiging tussen: één misklik op *Importeren* + een bestand kiezen en de selectie waar je net een kwartier aan gewerkt hebt is weg, zonder undo. Bestaat sinds v0.7.4 en is nooit gemeld, maar het is de enige destructieve knop in de app zonder confirm. Krijgt de bundel-import een voorbeeld-dialog (zie de v1.2.9-beslissing over selectief importeren), dan staat die inconsistentie er straks naast: de ene import vraagt wel, de andere niet. Twee uitwegen: dezelfde voorbeeld-dialog ook voor de losse import, of `clearFirst` naar een keuze in die dialog tillen.

- **Er staat nog een hardcoded Nederlandse alinea in de UI, en geen enkele scanner-pass ziet 'm.** `Dialogs/BackupPromptDialog.xaml:20` heeft zijn uitlegtekst als **inline XAML-inhoud** staan (`<TextBlock>tekst</TextBlock>`) in plaats van als `Text="..."`-attribuut. Pass 1 kijkt alleen naar tekst-*attributen*, dus dit is de vierde vorm die structureel buiten beeld blijft — na geïnterpoleerde strings (v1.2.6), switch-armen en lokale variabelen (v1.2.7) en de kapotte `Services/`-pass (v1.2.8). De alinea is zichtbaar bij **elke** Tweaks-Apply met `BackupBeforeApply = Ask`, dus in de Engelse UI staat er gewoon Nederlands. Het is bewust **niet** in v1.2.9 meegenomen: het is vertaalwerk in een patch over config-backup, en het vraagt een eigen keuze omdat er een `<Run FontWeight="SemiBold">` middenin staat — dat wordt dus óf drie keys (voor, vet, na), óf één key zonder het vet. Neem bij het oppakken meteen pass 1 mee: die moet inline-inhoud gaan zien, anders vindt de volgende ronde ‘m weer niet.

- **Apps buiten de catalogus gaan niet mee in de config-backup.** De export lijst alleen de 108 catalogus-apps; een winget-id dat niet in `apps.json` staat heeft aan de importkant nergens een plek om te landen, want de import zet `IsSelected` op een catalogus-app. Voor "verhuis mijn pc" is dat een echte beperking: de meeste geïnstalleerde apps van een power-user staan niet in de curated lijst. Er is wel een aanknopingspunt — `SelectionHelper.ExtraSelectedApps` bestaat al voor synthetische apps uit winget-repo-zoekresultaten, dus een bundel zou `{ wingetId, name, source }` kunnen bewaren en die bij import als extra’s terugzetten. Eigen patch waard; het raakt de install-flow en niet alleen het bestandsformaat.

### Zonder versienummer — klein onderhoud, gevonden tijdens v1.2.7

Dingen die tijdens de vertaal-restjes boven kwamen en bewust níét in die patch zijn meegenomen, want ze raken werkende logica in plaats van tekst.

- ~~**De voortgangsregel op de install-cards**~~ — **afgerond in v1.2.8.**
- ~~**De foutmeldingen uit de elevated batches zijn nog Engels, en `Services/` valt daarom buiten pass 4 van de scanner.**~~ — **afgerond in v1.2.8.** `Services/` zit nu in pass 4. De verwachting hier klopte grotendeels: het waren 27 gerenderde strings (geschat "een stuk of twaalf"), en het onderscheid gerenderd-vs-alleen-geteld was inderdaad de helft van het werk.
- **De dode weergave-strings in `DeepCleanService` en `LeftoverScannerService` kunnen weg.** Bevestigd tijdens v1.2.8: `DeepCleanDeleteResult.ResultsByPath` en `LeftoverDeleteResult.ResultsByPath` worden door geen enkele Dialog of Page gebonden — de UI leest alleen `SuccessCount` / `FailedCount` / `BytesFreed`. *"Removed registry key"*, *"Deleted folder"*, *"Deleted shortcut"*, *"Removed MUIcache value"*, *"Cleared"* en *"Unknown type"* worden dus opgebouwd en nooit getoond. Ze staan nu met die reden in de `ALLOW`-lijst (blok a) in plaats van vertaald te zijn — onzichtbare tekst kun je niet verifiëren, zelfde regel als de 24 subcategorie-descriptions in v1.2.6. Twee nette uitwegen: het `message`-veld uit die dictionaries slopen (dan is het model weer eerlijk), of de dictionary alsnog ergens tonen. Zolang geen van beide gebeurt draagt de code een weergavelaag die niets weergeeft.
- **`RestorePointService.CreateAsync` geeft een `RestorePointResult` terug waarvan `.Message` nooit gelezen wordt.** Gevonden tijdens v1.2.8. De enige aanroeper is `DebloatPage:574` en die doet `_ = await App.RestorePoint.CreateAsync(rpDescription);` — het resultaat wordt letterlijk weggegooid. Vier Nederlandse zinnen (*"Kon elevated process niet starten."*, *"Restore point aangemaakt."*, *"Geen output van Checkpoint-Computer."*, *"UAC geweigerd."*) worden dus opgebouwd en verdwijnen. Erger dan cosmetisch: **een mislukt herstelpunt vóór een Debloat-batch wordt nergens gemeld**, en dat is precies het veiligheidsnet waar het om gaat. Te beslissen: de uitkomst tonen (dan horen die vier zinnen in de tabel), of `CreateAsync` een `void`/`bool` maken en de zinnen weghalen. Het eerste is waarschijnlijk wat je wilt.
- **Winget's `Found <naam> [<id>] Version <x>`-regel blijft Engels op de install-kaart.** Overgebleven na v1.2.8.1: `FriendlyOutputLine` mapt die regel bewust niet, want *Version* staat midden in de data en half vertalen is slechter dan niet vertalen. Nette uitweg: de regel parsen op naam / id / versie (het formaat is stabiel) en herbouwen als `installCard.found` met drie argumenten. Klein, maar het vraagt eerst een screenshot van de exacte regel om het formaat te bevestigen in plaats van te gokken. Zelfde geldt voor eventuele andere winget-regels die nog niet in de mapping staan — die vallen nu terug op de ruwe Engelse tekst, wat bedoeld is, maar elke keer dat er één opduikt hoort 'ie erbij.
- **De download-regel op de install-kaart toont de volledige URL en breekt over meerdere regels.** Zichtbaar op de v1.2.8-screenshots: *"Downloading https://github.com/HandBrake/HandBrake/releases/download/1.11.2/HandBrake-1.11.2-x86_64-Win_GUI.exe"* loopt over twee regels en laat de kaart verspringen. De Nederlandse variant is nog iets langer. Bestond al vóór de vertaling en is bewust níét meegenomen in v1.2.8.1 (dat was een taalfix, geen layoutfix). Uitweg: alleen de bestandsnaam tonen in plaats van de hele URL — `install.winget.downloading` heeft al een `{0}`, dus het is één `Path.GetFileName`-aanroep.
- **`Take Ownership` en `Open Terminal as Admin` staan in het Windows-contextmenu maar zijn niet vertaald.** Deze twee literals in `TweakService` zijn de naam van het verb *zoals die naar het register geschreven wordt* — de gebruiker ziet ze dus wél, alleen in Explorer en niet in onze app. Vertalen is hier géén vertaling maar een **gedragswijziging**: de revert van die tweak matcht op de geschreven key, dus een taalwissel zou een eerder aangemaakt verb onverwijderbaar maken. Wie dit oppakt moet dus eerst de revert-logica aanpassen. Staat met die reden in de `ALLOW`-lijst (blok e).
- **De cache-namen op de Deep-clean-cards blijven Engels omdat de bundeling op de `DisplayName` tokeniseert.** Besloten in v1.2.7 en onderbouwd in die sectie: *"Tijdelijke bestanden (gebruiker)"* en *"(systeem)"* zouden significante tokens delen die het Engels niet deelt, waarna de cards per taal anders bundelen. De nette uitweg is de bundeling zélf aanpassen: `BundleByTokenOverlap` bestaat voor verweesde mappen (*"VMware"* op drie locaties), niet voor een vaste lijst van tien caches — sluit cache-categorieën uit en de tien namen kunnen alsnog vertaald worden. Klein, maar het raakt scanlogica en hoort daarom in een eigen patch. De acht `ALLOW`-regels in `scan-untranslated.py` kunnen dan weg.
- **De filter-header in de Deep-clean-dialog toont het totaal in plaats van het aantal treffers.** Bij een actief filter staat er *"227 items gevonden — filter actief"* terwijl er 30 kaarten onder staan: `deepclean.foundFiltered` krijgt `_items.Count` mee in plaats van `filteredItems.Count`. Gezien tijdens de v1.2.7-verificatie, bestaat al sinds de filter er is en heeft niets met de vertaling te maken. Eénregelige fix.
- **De ingebouwde WinUI-automation-namen volgen nog steeds de Windows-weergavetaal.** In de Engelse UI heet het InfoBar-icoon in de automation-boom *"Informatiepictogram"*. Niet zichtbaar op het scherm, maar een schermlezer noemt het verkeerd. Dit is dezelfde MRT-beperking als het NavigationView-Settings-item hierboven onder v1.2.4, en dus geen nieuw probleem — wel het tweede geval, dus als je het a11y-item oppakt hoort dit erbij.

### Zonder versienummer — klein onderhoud, gevonden tijdens v1.2.6

- **De 24 subcategorie-`description`-velden in `apps.json` worden nergens getoond.** `CategoryDetailPage` bouwt de `SubcategoryGroup` alleen uit `sub.Name`; de omschrijving wordt wél gedeserialiseerd maar nooit gebonden. Ze zijn daarom in v1.2.6 bewust níét vertaald — onzichtbare tekst kun je niet verifiëren. Twee uitwegen, allebei prima: weghalen uit `apps.json` (dan is het schema weer eerlijk), of alsnog tonen onder de groepsheader (dan horen ze in de vertaaltabel als `appSubcategory.<id>.desc`). Nu staat er een veld dat niets doet naast een `name` die verhuisd is, en dat leest verwarrend voor een contributor.
- **`CONTRIBUTING.md` is op meer punten verouderd dan alleen de apps.json-instructie.** Tijdens v1.2.6 bijgewerkt voor het nieuwe vertaalmechanisme, maar de rest van het bestand klopt nog niet: het noemt **.NET 8** (is 10), **"Main WPF application"** met een `Views/` + `ViewModels/` + `Themes/`-structuur en een `Launcher/`-project (allemaal weg sinds v0.5.9, de app is WinUI-only), **"MIT License"** onderaan (sinds v1.0.0 proprietary — dit is de vervelendste, want het staat er als juridische mededeling aan bijdragers), `YOUR_USERNAME` als placeholder in twee issue-links, en een "Features die we zoeken"-lijst waarvan 8 van de 9 items allang gebouwd zijn (dark mode, installed-indicator, parallel installs, icons, export/import, fuzzy search, multi-language). Eén doorloop volstaat.
- **`WingetService.ParseSearchOutput` neemt de Tag-kolom mee in het versienummer.** In de winget-repo-zoekresultaten staat "Beschikbaar via winget (v4.9.9   Tag: notepad)" — de versie-substring loopt door tot `sourcePos`, en als winget een `Tag`-kolom tussenvoegt komt die mee. Cosmetisch en bestond al vóór v1.2.6 (de oude hardcoded string had hetzelfde), maar het is zichtbaar. Fix is een `.Split()` op whitespace of de kolompositie strakker bepalen.

### Zonder versienummer — klein onderhoud, gevonden tijdens v1.2.4

Losse punten die in de weg lagen of opvielen, geen van alle blokkerend. Los oppakken of meenemen met een volgende patch.

- **Stijlafspraken voor `strings.en.json` staan nergens vast, en het bestand is nu inconsistent.** Drie dingen die per categorie anders uitpakten en één keer besloten moeten worden: (1) **US vs Brits Engels** — geteld in de huidige tabel: 1× `recognise` tegenover 2× `recognize`, 8× `cancelled` (Brits) naast 10× `color` en 5× `behavior` (US); (2) de **`N-op`-notatie** uit de Nederlandse bron is als "N keys" vertaald, maar "N operations" of "N values" is net zo verdedigbaar en komt in tientallen descriptions voor; (3) **`LET OP:`** staat 6× in het Nederlands en is 5× als `NOTE:` en 1× als `WARNING:` gerenderd (die ene is Take Ownership op systeemmappen). `NOTE:` is zwakker dan het Nederlandse `LET OP:`; kies één marker en voer 'm door.
- **Een paar Nederlandse tweak-namen gebruiken een andere term dan hun eigen omschrijving.** De namen zijn nieuw vertaald en volgen de term die Nederlands Windows gebruikt; de omschrijvingen zijn de bestaande tekst en gebruiken soms het leenwoord. Concreet: *navigatiebalk* (naam) vs Windows' *navigatiedeelvenster*, *aanmeldscherm* vs *login-scherm*, *Schuifbalken* vs *scrollbars*, *taakbalkanimaties* vs *taskbar-animaties*, *Deze PC* vs Windows' *Deze pc*. Cosmetisch, maar het staat op dezelfde card onder elkaar. Fixen betekent de Nederlandse omschrijvingen aanpassen — bewust niet gedaan in v1.2.4, want dat is de "bestaande tekst blijft staan"-regel van deze fase.
- **Accessibility: het NavigationView-Settings-item blijft zijn oude automation-naam houden na een taalwissel.** Visueel klopt het (het label wórdt "Settings"), maar de UI-Automation `Name` van dat ene item blijft op de vorige taal staan — WinUI ververst de automation peer niet wanneer je `Content` na het laden zet. Een schermlezer noemt het item dus verkeerd tot de app herstart. Vermoedelijke fix: naast `Content` ook `AutomationProperties.SetName(settingsItem, …)` zetten in `MainWindow.ApplyLanguage()`. Niet gemeten met een echte schermlezer.
- **De geplande auto-update-task op deze dev-machine wijst naar `bin\Debug`.** `Execute = D:\WinAppInstaller\src\SetupToolbox\bin\Debug\…\SetupToolbox.exe /autoupdate`. Precies waar de v1.0.14-notitie voor waarschuwde. Gevolg tijdens deze sessie: die run hield het exe-bestand vast, waardoor `dotnet build` niet kon kopiëren (`MSB3021`), en hij draait ge-eleveerd dus je kunt 'm niet zomaar killen. Opnieuw aanmaken vanuit de geïnstalleerde app.

### Zonder versienummer — release-cadans

**Besloten op 2026-08-16: v1.2.0 is gecut** en als GitHub Release gepubliceerd — zie de v1.2.0-sectie onder *Voltooide versies*. Daarmee is de achterstand weg: self-update levert nu alles uit v1.0.1 t/m v1.0.14 in één keer af. Het openstaande besluit ("wanneer komt de volgende milestone?") is hiermee beantwoord voor deze ronde.

**Wat blijft staan als werkafspraak:** patches (v1.2.1, v1.2.2, …) krijgen géén eigen Release — dat blijft de CLAUDE.md-regel. Maar de les uit deze ronde is dat 14 patches opsparen te lang was: geïnstalleerde gebruikers zaten ruim twee maanden op v1.0.0 zonder de crash-fixes uit v1.0.11/v1.0.12 en zonder de accu-fix uit v1.0.14. **Vuistregel voortaan: cut een milestone zodra er een gebruikers-zichtbare crash- of dataverlies-fix in de patchstapel zit, en anders uiterlijk na een handvol patches** — niet wachten tot de stapel "af" voelt.

**Open besluit — telt een security-fix mee als reden om te cutten?** v1.2.1 haalt een kwetsbare DLL uit de publish, maar is volgens de regel "gewoon" een patch en krijgt dus geen Release. Gevolg: iedereen die op v1.2.0 zit houdt `System.Drawing.Common` 4.7.0 op schijf tot de volgende milestone. Het is geen crash en geen dataverlies, dus de vuistregel hierboven dekt het niet — maar het is wel precies het soort ding waar je niet drie features op wilt wachten. Te beslissen: de vuistregel uitbreiden met "of een security-fix", of per geval wegen.

> **Feitelijk beantwoord door de v1.3.0-planning (2026-08-20), maar niet als regel.** Die security-fix gaat mee zodra v1.2.8 t/m v1.2.10 binnen zijn — dus per geval gewogen, niet via een uitgebreide vuistregel. Let wel op hoe lang het geworden is: v1.2.1 is van 2026-08-16 en wacht dan op drie features. Als deze reeks uitloopt is dat op zichzelf een reden om er alsnog eerder een cut tussen te zetten. De keuze "vuistregel uitbreiden of per geval wegen" staat dus nog steeds open — er is nu alleen één geval concreet beslist.

### Ideeën — nog niet gescoped

Geverifieerd (2026-08-16): niks hiervan is stiekem al gebouwd.

- **Plugin-systeem voor custom app-sources** + **custom app-repositories** — hetzelfde onderliggende gat, dus samengevoegd. `AppDatabaseService` leest precies één gebundelde `data/apps.json`; geen source-abstractie, geen UI om een extra bron toe te voegen.
- **Cloud sync voor settings/selecties** — let op het verschil met v1.2.3 hierboven: dát is een handmatig bestand dat je zelf meeneemt, dit zou **automatische** synchronisatie tussen machines zijn. Vereist een transport (OneDrive-map? eigen backend? account-systeem?) dat nu nergens bestaat.
- **CLI interface** (bv. `install --profile gaming`) — `App.OnLaunched` heeft al 5 herkende command-line-argumenten, maar die zijn stuk voor stuk interne/headless plumbing voor een specifieke feature (`/autoupdate`, `/toasttest`, `/updatecheck`, `--install-runner <pad>`, `setuptoolbox:open`) — geen ervan accepteert een vrije app-naam of profielnaam. Wel een bruikbaar precedent: het dispatch-patroon in `OnLaunched` is uit te breiden, dus lager effort dan vanaf nul.

---

## Voltooide versies

### v1.2.9 — Eén-klik volledige config-backup (apps + tweaks + settings)

Eén bestand met je app-keuze, de tweaks die op deze pc aanstaan en je voorkeuren, plus één import die per onderdeel te kiezen is. De stringtabellen gaan van 1288 naar **1325 keys**, in beide talen even groot.

#### Zeven beslissingen vooraf (user, 2026-08-20)

Vijf stonden er als vraag in de roadmap; **twee zijn er tijdens het uitzoeken bijgekomen**, en dat waren precies de twee die de rest bepaalden.

1. **Tweaks = de live gedetecteerde staat.** Dit was de niet-gestelde vraag. `TweakProfileService.ExportAsync` neemt `App.ProfileSelection` als bron — de handmatige wenslijst uit profiel-modus — en die is **leeg zodra je niet in profiel-modus zit**. Een knop in Settings had er dus niets aan. De bundel legt daarom vast wat er nú aanstaat: `ConfigBackupService.CaptureTweakState` na een `DetectStatesAsync()`. Toggle-tweaks tellen mee bij `Enabled` **én** `Partial` (op de doelmachine wil je ‘m compleet, en `StageDelta` slaat 'm over als 'ie daar al goed staat); choice-tweaks alleen bij `SelectedChoiceIndex >= 0`, want index `-1` betekent een waarde die niet bij onze opties hoort en die is niet reproduceerbaar. **Zelfde bestandsvorm als een tweak-profiel, ander begrip:** een profiel is *wat ik wil*, een backup is *wat het is*.
2. **Apps = wat er geïnstalleerd staat, met vinkjes eromheen.** Ook nieuw. De losse export schrijft je *selectie*; voor "verhuis naar een nieuwe pc" is dat het verkeerde ding. De export opent nu een keuze-dialog met de hele catalogus, waarin de geïnstalleerde apps bovenaan staan en **voorgevinkt** zijn: uitvinken wat niet mee hoeft, extra’s bij vinken. De zoekbalk filtert, en *Alles / Niets selecteren* werkt bewust op wat **zichtbaar** is — anders zet één klik tijdens een actief filter stilletjes 108 apps aan.
3. **Bundel met een eigen versie eromheen**, de bestaande payloads als sub-objecten mét hun eigen versie. De twee losse knoppen **blijven** bestaan, en de nieuwe Importeren slikt **alle drie** de bestandsvormen.
4. **De “is dit al gevraagd”-vlaggen blijven achter — en het zijn er víjf, niet vier.** Naast `ParallelInstallsAsked`, `DontAskAboutScheduling`, `DeepCleanRestorePointConfigured` en `DebloatRestorePointConfigured` hoort **`ShowWelcomeBanner`** in dezelfde categorie: die staat op `false` omdat jíj de banner hebt weggeklikt, en meenemen verbergt op de nieuwe machine een banner die die gebruiker nooit gezien heeft. Blijft dus staan. De echte voorkeuren ernaast (`RestorePointBeforeDeepClean` / `-Debloat`) gaan wél mee, dus daar verschijnt de first-run-prompt één keer met jouw antwoord al ingevuld. Netto: **9 voorkeuren** exporteerbaar van de 15.
5. **Taal gaat mee in het bestand maar krijgt een eigen vinkje**, standaard **uit**, en de rij is alleen zichtbaar als het bestand een andere keuze draagt dan de machine. Een gedeeld profiel zet zo niet stilzwijgend andermans UI om.
6. **Eén melding met een regel per onderdeel**, ernst = de zwaarste van de drie. Samenvoegen tot één totaal zou verbergen wélk onderdeel iets moest overslaan, en dat is precies waar de losse meldingen voor bestaan.
7. **Selectief importeren per onderdeel**, niet per veld. Drie vinkjes met tellingen in een voorbeeld-dialog; 15 losse vinkjes leest niemand na.

#### Het formaat

```
{ version, kind: "setuptoolbox-config", exportedAt,
  apps:     { version, exportedAt, appCount, apps: [wingetId, ...] },
  tweaks:   { version, exportedAt, count, tweaks: [{ id, choice? }] },
  settings: { version, values: { ... , language: "en" | "nl" | "system" } } }
```

De sub-objecten zijn **byte-identiek aan wat de losse exporters schrijven** — knip het `apps`-blok eruit en je hebt een geldige `my-apps.json`. Daardoor kan het tweak-formaat straks doorgroeien zonder de bundelversie te bumpen.

Twee dingen die anders stil fout zouden gaan:

- **`backupBeforeApply` staat als naam in het bestand** (`"Always"`), niet als `1`. De `JsonStringEnumConverter` zit bewust **alleen** op deze service: `settings.json` zelf houdt zijn numerieke vorm, want anders zou een bestaande settings.json na de update niet meer laden.
- **"volg de Windows-weergavetaal" is een expliciete sentinel** (`"system"`) en geen `null`. Met `WhenWritingNull` zou een `null` uit het bestand verdwijnen en is "volg systeem" niet meer te onderscheiden van "dit bestand weet niets van taal".

De drie vormen worden herkend aan de **vorm van de JSON**, niet aan de bestandsnaam: `kind` aanwezig → bundel; `apps` is een array → app-selectie; `tweaks` is een array → tweak-profiel. Iets anders → `io.notAConfigFile`.

#### Wat er in bestaande code moest

- **`TweakProfileService` heeft twee gedeelde statics gekregen** — `MatchEntries` en `EnglishChoiceLabel` — en `ImportAsync`/`ExportAsync` lopen daar nu zelf ook over. Reden: de v1.2.4-eigenschap (een profiel overleeft een taalwissel omdat er altijd het **Engelse** label in gaat en er bij inlezen tegen béíde tabellen gematcht wordt) mocht niet per aanroeper opnieuw goed moeten gaan. Één implementatie, twee gebruikers.
- **`SettingsService.BatchSave()`**, een `using`-scope die `Save()` onderdrukt. Zonder dat schrijft een import van negen voorkeuren negen keer `settings.json` weg — elke setter roept `Save()` aan. Bewust een scope en geen los Begin/End-paar: bij een exception halverwege zou de vlag anders blijven hangen en zouden latere wijzigingen stíl niet meer persisteren.
- **Het sync-blok in `SettingsPage.OnNavigatedTo` is `SyncAllControls()` geworden.** Een import wijzigt voorkeuren onder de besturingen vandaan; zonder hersynchronisatie staat de pagina te liegen.
- **De 26 `Grid.Row`-nummers in `SettingsPage.xaml` zijn hernummerd** om de kaart tussen *Tweak-profielen* en *Backup & herstel* te krijgen (nu 29 rijen).

#### De valkuil: de taalwissel sloopt zijn eigen resultaatmelding

`App.Loc.Set()` vuurt `LanguageChanged`, waarna `MainWindow` de huidige page opnieuw navigeert. Zet je de taal dus toe midden in de import-handler, dan wordt de SettingsPage-instantie vervangen en schrijft de handler daarna zijn resultaat naar een **losgekoppelde** InfoBar — zichtbaar gebeurt er niets. Opgelost door de taal als **laatste** stap te doen en de uitkomst als **data** (`ConfigApplyResult`, static) te parkeren; `OnNavigatedTo` rendert ‘m opnieuw, en dus meteen in de nieuwe taal. En alleen wanneer de **effectieve** taal wijzigt: `Set(English)` op een machine die het systeem al volgt en waar dat Engels oplevert, vuurt niets — dan moet de melding gewoon direct getoond worden.

#### 37 nieuwe keys

`config.export.*` (11), `config.import.*` (12), `config.result.*` (9), `settings.fullConfig.*` + `settings.section.fullConfig` (3), `io.fileType.fullConfig` en `io.notAConfigFile` (2). Hergebruikt zonder nieuwe key: `common.export` / `.import` / `.cancel` / `.selectAll` / `.deselectAll` / `.installed` / `.appCount` / `.tweakCount`, plus `settings.profiles.alreadyGood.*` en `settings.profiles.skipped` voor de tweak-regel in de samenvatting. 1288 → **1325**.

> **Aangetoond (2026-08-20):**
> - Build: **0 errors, 0 warnings**. `Version` / `AssemblyVersion` / `FileVersion` op 1.2.9.
> - `scan-untranslated.py`: **0 / 0 / 0 / 0** over alle vier de passes, inclusief de twee nieuwe dialogs en de nieuwe service.
> - `check-catalog-keys.py`: 152/152 catalogus-keys, **1325 = 1325**, geen wezen, geen lege waardes. De teller "gecontroleerd op aanroep" loopt van 569 naar **606** — exact +37, dus elke nieuwe key wordt aantoonbaar aangeroepen en er is er geen één dood.
> - Beide XAML-dialogs compileren daadwerkelijk (`.g.cs` + `.xbf` aanwezig), dus de `x:Bind`-DataTemplate en de markup-extensies zijn niet alleen syntactisch goed.
>
> **Niet getest, en dat is het hele bewijsgat:** de round-trip zelf. Er is géén export gemaakt en géén bestand geïmporteerd — een import schrijft `settings.json` en wist de huidige app-selectie op de machine van user, en dat is niets om ongevraagd te doen. Wat daardoor onbevestigd blijft: (1) of de app-keuze-dialog de geïnstalleerde apps daadwerkelijk voorgevinkt toont, (2) of de drie bestandsvormen alle drie herkend worden, (3) of de aparte taal-rij verschijnt en de her-navigatie de melding netjes opnieuw rendert, (4) of `BatchSave` echt één keer schrijft. **Klaargezet om dat in vijf minuten te doen:** vijf voorbeeldbestanden in de scratchpad (losse app-selectie, los tweak-profiel, volledige bundel, een niet-Setup-Toolbox-bestand voor de foutmelding, en een bundel met `language: "nl"` voor de taal-rij).

**Werkwijze-notitie — correctie op de v1.2.8-notitie, opnieuw gemeten.** Daar staat dat de `.cs`-bestanden CRLF zijn en alleen `data/apps.json` LF. Dat klopt niet: de bronboom is **gemengd** — geteld over `src/SetupToolbox` (zonder `bin`/`obj`): **34 CRLF tegenover 47 LF**, en het loopt dwars door mappen heen (`TweakProfileService.cs` is CRLF, `SettingsService.cs` in dezelfde map is LF). `NEXT-STEPS.md` en `SetupToolbox.csproj` zijn ook LF; de twee stringtabellen zíjn CRLF, dus dát deel van de notitie klopte wel. Conclusie blijft dezelfde en wordt alleen belangrijker: lees en schrijf **altijd** met expliciete `newline=''` en bepaal de regeleinden **per bestand**, nooit één aanname voor de hele boom. Zo bleef de diff hier overal beperkt tot de daadwerkelijk gewijzigde regels.

### v1.2.8.1 — Winget's eigen stdout stond nog Engels op de install-kaart

**Gevonden door user, direct na de v1.2.8-testrun.** De vier vertaalfasen hadden alle *onze* strings gedekt, maar op de install-kaart stond nog steeds Engels:

> *"Starting package install..."* · *"Successfully verified installer hash"* · *"Downloading https://github.com/HandBrake/…"* · *"The installer will request to run as administrator. Expect a prompt."*

**Dat zijn geen ontbrekende keys — het is winget's console-output.** `RunWingetCommandAsync` leest `winget.exe` stdout karakter-voor-karakter en geeft élke regel door aan `progress?.Report(line)`; die komt via `InstallPhase.Running` ongefilterd op de kaart. Twee gevolgen die dit een aparte categorie maken:

- **Geen enkele scanner kón dit zien.** Er staat geen literal in onze code — de tekst ontstaat pas op runtime, in een ander proces. `scan-untranslated.py` stond terecht op 0 en `install.log` had **0 LOC-MISS**: de tabellen waren compleet, de tekst kwam simpelweg niet uit de tabel.
- **Winget volgt de Windows-weergavetaal, niet onze taalkeuze.** Dezelfde MRT-vs-eigen-tabel-scheiding als bij de WinUI-automation-namen. Onze toggle heeft er per definitie geen invloed op.

**De oplossing is een mapping, geen filter.** Nieuwe `WingetService.FriendlyOutputLine(line)` herkent de regels die daadwerkelijk op het scherm gezien zijn en geeft er onze eigen tekst voor terug; **een onbekende regel gaat ongewijzigd door**. Winget's diagnostiek stilzwijgend inslikken zou erger zijn dan een enkele Engelse regel die we nog niet kennen.

**De valkuil die dit bijna een regressie maakte:** `InstallDialog.ApplyProgressCore` gebruikt exact díé Engelse tekst om de stage-ring te laten opschuiven — `Contains("Starting package install")` → 3/4, `Contains("installer hash")` → 2/4. Vertaal je vóór die detectie, dan blijft de ring op 1/4 staan en merkt niemand dat, want de kaart blijft er verder normaal uitzien. Opgelost met dezelfde scheiding als bij de sentinels in v1.2.8: `item.Message` krijgt de **vertaalde** regel, de switch eronder matcht op de **ruwe**.

**Vier nieuwe keys** (`install.winget.downloading` / `.hashVerified` / `.startingInstall` / `.expectUacPrompt`), plus `install.state.installed` hergebruikt voor winget's *"Successfully installed"*. Tabellen 1284 → **1288**.

> **Bewust niet gemapt: de `Found <naam> [<id>] Version <x>`-regel.** Die bevat het woord *Version* midden in de data, dus een nette vertaling vereist dat je de regel parset in naam/id/versie in plaats van 'm te herkennen. Half vertalen (*"Gevonden: Notion [Notion.Notion] Version 4.19.1"*) is slechter dan niet vertalen. Staat als los punt hieronder.

> **Aangetoond (2026-08-20):** build **0 errors / 0 warnings** op `1.2.8.1`; beide checkers exit 0; de vier keys staan in beide tabellen en worden aangeroepen; en elk van de vijf herkende regels landt aantoonbaar op de bedoelde tak terwijl de byte-teller (*"1.23 MB / 45.6 MB"*) en de `Found`-regel ongewijzigd doorgaan — precedentie dus getest, niet aangenomen.
>
> **Niet visueel getest, en dat blijft zo — bewuste keuze van user (2026-08-20): "ik geloof het wel".** De vertaalde regels zijn dus nooit op het scherm gezien; v1.2.8.1 leunt volledig op de build, de twee checkers en de precedentie-test hierboven. Géén openstaande taak, wél een gat in de bewijsketen, en het staat hier zodat het bij een volgend probleem op de install-kaart als eerste verdachte in beeld komt.
>
> Twee dingen zijn daardoor onbevestigd: (1) of de **stage-ring** nog doorloopt naar 2/4 en 3/4 — dat is het enige wat deze wijziging kón breken, want de detectie matcht op de ruwe regel terwijl de weergave vertaald is; (2) of er nog andere winget-regels langskomen die niet in de mapping staan. Beide zijn in één install-batch te zien.

> **Afwijking van de nummering-conventie, bewust.** De roadmap gebruikt drie delen (`1.2.7`, `1.2.8`). Dit is op verzoek van user een vierde component geworden omdat het een directe hotfix op v1.2.8 is en geen eigen roadmap-item. `AssemblyVersion` is sowieso een viervelds-nummer, dus `GitHubService.TryParseTag` en de self-update-vergelijking hebben er geen last van.

### v1.2.8 — De install-voortgangsregel + de foutmeldingen uit de elevated batches

De stringtabellen gaan van 1265 naar **1284 keys**, in beide talen even groot. Hiermee is `Services/` de laatste map die aan pass 4 van de scanner is toegevoegd; er staat nu geen enkele map meer bewust buiten.

> **Verificatie door user, 2026-08-20 — gedaan.** Een echte install-batch (Notion + PuTTY + HandBrake, parallel=3), een echte uninstall, een geweigerde UAC-prompt en een reeds-geïnstalleerde app. Uitkomst hieronder; het leverde één vervolgpunt op dat **v1.2.8.1** is geworden.
>
> - **De install-flow werkt en de kaart is Nederlands** — behalve winget's eigen stdout, zie v1.2.8.1.
> - **De `Al geïnstalleerd`-tak is echt geraakt:** `SKIP Mozilla.Firefox (already installed, exit=0x8A15002B)` in `install.log`. Dat is precies de sentinel die onvertaald moest blijven, en de weergave ernaast klopte.
> - **De UAC-weigering is echt geraakt:** `RUNNER FAIL UAC declined / elevation impossible (Win32 1223)`, gevolgd door de terugval op de per-app in-process flow — de drie apps installeerden daarna alsnog. Let op: dít pad toont géén `elevated.uacDeclined`; de install-runner valt stil terug (v1.0.5-ontwerp). Die key is dus nog steeds niet visueel bevestigd — daarvoor moet je UAC weigeren bij een **Tweaks-apply, Debloat- of Deep-clean-batch**.
> - **Uninstall werkt** (user bevestigd).
> - **Geen enkele `LOC-MISS`** in `install.log` over de hele sessie, en **geen `crash.log`**. Dat is meteen het bewijs dat de resterende Engelse tekst niet uit onze tabel kwam maar van buiten het proces.

#### Vier beslissingen vooraf (user, 2026-08-20)

1. **Hergebruiken waar het kan.** `:661` en `:674` roepen `install.state.installed` / `install.state.alreadyInstalled` aan in plaats van een eigen `install.progress.*`-familie te krijgen. Scheelt twee keys die vandaag dezelfde waarde zouden dragen. Prijs, expliciet aanvaard: gaan de badge en de voortgangsregel ooit uit elkaar lopen, dan moet dat alsnog gesplitst worden.
2. **Gelijktrekken op de vriendelijke naam.** `:621` toonde de wingetId (*"Installing Mozilla.Firefox..."*) terwijl `:812` de naam toonde (*"Starting Firefox"*) — en de kaart erboven toont sowieso al "Firefox". `InstallAppAsync` heeft er een optionele `displayName`-parameter bij gekregen, **achter** `location` omdat de bestaande aanroepers positioneel binden. Geen diagnostisch verlies: `install.log` logt de exacte wingetId in de `START`-regel.
3. **De elevated-batch-foutmeldingen gaan mee in deze patch.** Daarmee kon `Services/` erbij in pass 4.
4. **`UninstallAppsAsync` weg.** Zie hieronder.

#### De vijf install-strings

| Regel | Was | Werd |
| --- | --- | --- |
| `:621` | `$"Installing {wingetId}..."` | `install.installingApp` met de app-naam |
| `:661` | `"Installed"` (2×: scherm én returnwaarde) | `install.state.installed` |
| `:674` | `progress.Report(AlreadyInstalledMessage)` | `install.state.alreadyInstalled`, sentinel ongemoeid |
| `:695` | `const RequiresLocationMessage` | `static` property → `winget.requiresLocation` |
| `:812` | `$"Starting {app.Name}"` | `install.startingApp` |

**`AlreadyInstalledMessage` blijft een niet-vertaalde `const`** en dat is de kern van deze patch. Het waren geen twee vergelijkingen maar **drie**: `WingetService.InstallOneInBatchAsync`, `InstallDialog.MapPhase` (`:258`) én `InstallDialog.InstallWithLocationAsync` (`:264`) — die laatste stond niet in de inventarisatie. De weergave is ervan losgeknipt, zelfde oplossing als `RestorePointStatus` in v1.2.7.

> **De comment bij die const is scherper gezet dan de roadmap voorstelde.** Daar stond dat vertalen wél kan "zolang schrijven en vergelijken in dezelfde taal gebeuren", zoals bij de drie sentinels van v1.2.3. Dat argument houdt hier niet: de ge-eleveerde install-runner is een **apart proces** dat z'n eigen stringtabel laadt. De modale-dialog-garantie uit v1.2.3 dekt dat niet, dus deze blijft principieel onvertaald.

**`RequiresLocationMessage` is nagetrokken en is géén sentinel:** de enige voorkomens zijn de declaratie plus de twee regels waar 'ie gerapporteerd en geretourneerd wordt. Nergens een vergelijking. Van `const` naar property, met de reden als comment — precies wat v1.2.3 met de andere drie deed.

**`:661` als returnwaarde is opnieuw gecontroleerd** in plaats van aangenomen, zoals de roadmap vroeg: er matcht niets op. Alleen `AlreadyInstalledMessage` en `MsStoreOpenedMessage` worden vergeleken. De vertaalde waarde mag dus ook naar de caller.

#### De elevated batches: 27 gerenderde strings over 7 services

Per string is uitgezocht of 'ie **gerenderd** wordt of **alleen geteld** — dat onderscheid was, zoals voorspeld, de helft van het werk. Uitkomst:

| Service | Wat | Rendert via |
| --- | --- | --- |
| `WingetService` | 8 uninstall-meldingen | `MixedSourceUninstaller` → `AllAppsUninstallDialog.Message` |
| `BloatwareService` | 4 (+ *"No log produced"* / *"Could not read log"*) | `BloatwareUninstallDialog:75` |
| `MixedSourceUninstaller` | 3 + 2 voortgangsregels | `AllAppsUninstallDialog` |
| `TweakService` | 3 | `FailureMessages` → `TweakApplyRunner.ShowOutcome` → InfoBar |
| `SnapshotService` | 4 | `RestoreResult.FailureMessages` → `SnapshotBrowserDialog:181` |
| `TaskSchedulerService` | 4 | `CreateTaskOutcome.ErrorOutput` → `ScheduleDialog:68` |
| `GitHubService` | 1 | `UpdateCheckResult.Error` → `SettingsPage:488` |
| `DeepCleanService` / `LeftoverScannerService` | 0 | **rendert niet** — zie het onderhoudsitem onder *Open* |

**De aangehouden regel, één keer vastgelegd:** wat een gebruiker écht kan uitlokken (UAC geweigerd, proces start niet, log ontbreekt, netwerkfout) wordt vertaald. Wat alleen verschijnt als een **interne invariant** breekt — een hardcoded registry-pad in onze eigen code dat niet parset, een choice-index buiten bereik — blijft Engels, want dat is bugdiagnostiek voor wie het moet fixen en geen UI-tekst. Beide categorieën staan met die reden in de `ALLOW`-lijst.

**Nederlandse bron waar die er was.** `TaskSchedulerService`, `GitHubService` en `SnapshotService` hadden Nederlandse teksten; die zijn letterlijk overgenomen als NL-waarde (inclusief *"Process kon niet starten"* en *"task-definitie"*, die niet mooier gemaakt zijn), en het Engels is nieuw geschreven. Twee bewuste harmonisaties: `SnapshotService` zei *"kon elevated process niet starten"* en *"UAC geweigerd"*, en deelt nu de `elevated.*`-keys met de vier andere services — anders staan er vijf formuleringen voor dezelfde gebeurtenis.

#### Dode code eruit

`Dialogs/UninstallDialog.xaml` + `.xaml.cs`, `WingetService.UninstallAppsAsync`, en de records `UninstallProgress` / `UninstallPhase` zijn verwijderd. Die vier waren elkaars enige gebruikers: de dialog werd sinds v0.8.4 nergens meer geïnstantieerd en was een tweede uninstall-pad naast `AllAppsUninstallDialog` + `MixedSourceUninstaller`. `UninstallAppAsync` (enkelvoud) blijft — dáár draait de levende flow op.

Twee keys werden daardoor wees. `check-catalog-keys.py` ving ze allebei: **`progress.noApps`** (*"No apps to uninstall"*) is verwijderd, en **`common.uninstalled`** is juist behouden en aangesloten — die bleek de ontbrekende vertaling voor `UninstallAppAsync`'s succesmelding, die één woord lang (*"Uninstalled"*) hardcoded in de Nederlandse UI stond.

#### De scanner moest eerst gerepareerd worden

`Services/` toevoegen leverde eerst **0 treffers** op. Dat was geen schone map maar een kapotte pass — en precies het scenario waar de v1.2.7-notitie voor waarschuwde. Drie fouten, alle drie gefixt:

1. **De log-herkenning zocht over 160 tekens rúwe tekst.** In `Dialogs/` ging dat net goed; in `Services/` staat er vrijwel altijd een `Diagnostics.Log` binnen dat venster, dus élke literal viel weg. Vervangen door `enclosing_call()`: welke aanroep omsluit deze literal daadwerkelijk, met de string-literals eerst gemaskeerd zodat haakjes in PowerShell-fragmenten de telling niet slopen.
2. **Verbatim strings werden als gewone behandeld.** `@"HKLM\SOFTWARE\"` eindigt op een backslash; die werd als escape gelezen en de match liep door tot ver in de volgende regels. Eén zo'n registry-pad zette elke match daarna één aanhalingsteken uit de pas — goed voor tientallen halve treffers.
3. **Char-literals werden niet overgeslagen.** `.Trim('"')` in `DeepCleanService` liet de scan middenin een niet-bestaande string beginnen, met hetzelfde gevolg.

De ruis daarna (PowerShell-fragmenten, winget-argumenten, registry-value-namen) is met **twee categorische regels** weggenomen in plaats van met honderd losse `ALLOW`-regels: `SCRIPT_OR_ARG` (verb-noun, `$_`, `--vlag`) en `is_identifier` (geen spatie = geen zin). Een `ALLOW`-lijst van die omvang leest niemand meer na en is dan geen verantwoording meer maar een dempknop. De lijst staat nu op 72 regels, gegroepeerd met per blok een reden.

> **Bekende prijs van `SCRIPT_OR_ARG`, expliciet benoemd:** een échte UI-zin die een PowerShell-cmdlet noemt wordt niet gemeld. Concreet geval in deze codebase: *"Geen output van Checkpoint-Computer."* in `RestorePointService`. Die is hier onschuldig omdat die hele returnwaarde dood is (zie het onderhoudsitem), maar het is een gat en het staat als zodanig in de docstring.

**`is_identifier` geldt bewust alleen voor pass 4.** Pass 3 (de switch-armen) moet juist wél op losse woorden blijven melden — daar stond *"Installed"* als badge, en die heeft geen spatie.

#### De negatieve test vond twee bugs die de scan zelf niet zag

Bij het terugzetten van één vertaalde string (om te bewijzen dat pass 4 in `Services/` écht aanslaat) viel op dat er op regel 45 van datzelfde bestand `$"Uninstalling {entry.DisplayName}..."` stond — hardcoded Engels in de Nederlandse UI, en de scanner meldde het niet. Oorzaak: de drempel stond op "minstens twee echte woorden", en na het weghalen van het interpolatie-gat blijft er één woord over.

Drempel naar **één** woord, en dat kost níéts — met `is_identifier` en `SCRIPT_OR_ARG` erbij blijft de uitkomst 0. Het leverde meteen nog twee echte missers op: `BloatwareService:337` en `MixedSourceUninstaller:235` toonden allebei `$"Removing {naam}..."` terwijl `progress.removing` al in beide tabellen stond. Alle drie sluiten nu aan op bestaande keys, dus zonder nieuwe keys.

> Dit is de derde keer in deze reeks dat de scanner een categorie miste in plaats van een losse string (v1.2.6: geïnterpoleerde strings; v1.2.7: switch-armen en lokale variabelen). Het patroon is telkens hetzelfde: de scan loopt schoon, en dan blijkt de *vorm* niet gedekt. De les die hier bij hoort: een groene scan is bewijs over de gedekte vormen, niet over het scherm.

#### Nieuwe keys: 21, plus 3 hergebruikt

`elevated.*` (6, gedeeld door 5 services), `install.installingApp` / `install.startingApp`, `winget.requiresLocation`, `winget.uninstall.*` (5), `snapshot.notFoundOrCorrupt`, `schedule.err.*` (4), `update.err.httpStatus`. Hergebruikt zonder nieuwe key: `install.state.installed`, `install.state.alreadyInstalled`, `common.uninstalled`, `common.failed`, `progress.removing`, `progress.uninstalling`. Eén key verwijderd (`progress.noApps`). Netto 1265 → **1284**.

> **Aangetoond (2026-08-20) — zónder de app te draaien, want dat kan hier niet:**
> - Build: **0 errors, 0 warnings**. `Version` / `AssemblyVersion` / `FileVersion` op 1.2.8.
> - `scan-untranslated.py`: **0 / 0 / 0 / 0**, nu inclusief `Services/`.
> - `check-catalog-keys.py`: 152/152 catalogus-keys, **1284 = 1284**, geen wezen, geen lege waardes, elke niet-dynamische key aantoonbaar aangeroepen.
> - **Per key aangetoond** dat 'ie in beide tabellen staat én vanuit welk bestand 'ie aangeroepen wordt — alle 24 (21 nieuw + 3 hergebruikt) groen, en de symmetrische verschilverzameling tussen de twee tabellen is leeg.
> - **Negatief getest:** één vertaalde string teruggezet in `MixedSourceUninstaller` → de scanner meldt 'm op de juiste regel. Dat bewijst meteen dat de `ALLOW`-lijst bestand-gebonden werkt: dezelfde zin is toegestaan in `DeepCleanService` en `LeftoverScannerService` en wordt daar terecht niet gemeld.
> - **Aangetoond dat de verwijderde code echt weg is** (niet alleen dat het compileert): de vier namen komen alleen nog voor in de comments die uitleggen waaróm ze weg zijn.
>
> **Nog niet visueel bevestigd na de testrun van user:** de `elevated.*`-keys. De install-runner weigert-UAC-tak valt stil terug en toont ze niet, en een echte **delete-batch** (Deep clean / Leftovers) is niet gedraaid. Die 27 strings staan dus nog steeds op tabel-aanwezigheid en aanroep, niet op scherm. Goedkoopste manier om dat alsnog te dekken: UAC weigeren bij een Tweaks-apply — dan komt `elevated.uacDeclined` via `FailureMessages` in de InfoBar op de Tweaks-pagina.

**Werkwijze-notitie.** Gemeten in plaats van aangenomen: de `.cs`-bestanden in dit project zijn **CRLF**, net als de stringtabellen — alleen `apps.json` is LF. De eerdere werkwijze-notitie suggereerde LF voor de code. Lees en schrijf sowieso met expliciete `newline=''`, dan maakt het niet uit; de diff op de twee tabellen bleef zo 25 regels in plaats van het hele bestand.

### v1.2.7 — Multi-language fase 6: de vertaal-restjes (de vormen die de scanner structureel niet zag)

De stringtabellen gaan van 1197 naar **1265 keys**, in beide talen even groot.

> **Geen "hiermee is het af" deze keer.** Dat stond in v1.2.6 ook en het klopte niet. Wat hier af is, is scherp begrensd: **alle schermen die je zonder iets te veranderen kunt bekijken** — Apps, Tweaks, Debloat, Deep clean, Settings, en de dialogs die daarbij horen. Wat er nog staat, is de voortgangsregel op de install-cards en de foutmeldingen uit de elevated batches; beide zitten in `Services/`, beide zijn pas te zíén als je echt iets installeert of verwijdert, en beide staan als apart item onder *Open*. De scanner dekt `Services/` daarom bewust nog niet — dat staat er ook zo in, in plaats van dat de exit-code groen liegt.

**De aanleiding: `scan-untranslated.py` liep schoon en de app stond vol Engels.** Drie blinde vlekken, alle drie een *categorie* en niet een losse misser. Nummer 1 (`^\{` losgelaten op de C#-pass) was al gefixt in de v1.2.6-fix-commit; 2 en 3 zijn hier gedicht.

**De inventarisatie in de roadmap was te klein — het waren geen ~33 strings maar ~88.** Een brede sweep over álle C#-literals bracht drie extra plekken boven water die niemand geïnventariseerd had:

| Plek | Aantal | Wat |
| --- | --- | --- |
| `InstallDialog.StageLabel` | 4 | *Downloading / Verifying / Installing / Done* — `x:Bind`, staat op élke install-card |
| `InstallDialog.StateLabel` | 7 | *Waiting / Installed / **Al geïnstalleerd** / Failed / Browser opened / **Geopend in Store** / Skipped* — twee talen in één switch |
| `LeftoverCleanupDialog.SectionTitle` | 8 | de sectiekoppen: *Registry uninstall keys (3)*, *AppData folders (2)*, … |
| `DeepCleanService` browser-omschrijvingen | 3 | hardcoded **Nederlands**, terwijl de Firefox-tak er pal naast al `deepclean.desc.browserCache` gebruikte |
| `DeepCleanService` scan-locatie-regels | 6 | de "Gescand: …"-regel bovenaan de dialog |
| `DeepCleanPage:177` | 1 | `$"{n} HKCU vendor keys"` — de tiende van tien, de negen buren gebruikten allemaal `deepclean.part.*` |
| file-picker bestandstype-labels | 2 | *"SetupToolbox selection"* tegenover *"SetupToolbox tweak-profiel"* — dezelfde feature, twee talen, zichtbaar in de "Opslaan als type"-dropdown |
| `SnapshotBrowserDialog:163`, `TweakCardFactory:125`, `TweakApplyRunner:184-185`, `DeepCleanItem.SizeLabel`, 3× pad-tooltip | 6 | losse restjes |

Daarmee waren er **vier** plekken waar Engels en Nederlands in dezelfde besturing door elkaar liepen, niet één.

#### De grens tussen identifier en woord

Besloten (user, 2026-08-19): **splitsen op identifier**, dezelfde regel als de merknamen in v1.2.5 en de app-namen in v1.2.6. Letterlijke namen van Windows-artefacten blijven hardcoded, alles wat een woord is gaat naar de tabel.

- **Blijft staan:** `Prefetch`, `Windows.old`, `App Paths`, `MUIcache`, `HKCU vendor`, `Registry`, `Program Files`, `AppData` — map-, registry-key- en hive-namen. Plus `Winget` / `Store` / `Web`: Winget is Microsofts package manager, Store heet in Nederlands Windows ook Store, en Web is in beide talen hetzelfde woord. De tooltip ernaast (`source.*.desc`) is proza en liep al wél via de tabel.
- **Naar de tabel:** *Recycle Bin → Prullenbak*, *Browser cache → Browsercache*, *Shortcut → Snelkoppeling*, *Scheduled task → Geplande taak*, *Firewall rule → Firewallregel*, *Class handler → Bestandskoppeling*, *High/Possible/Loose match → Sterke/Mogelijke/Zwakke match*, en de elf install-card-labels.

Elke uitzondering staat **met reden** in de `ALLOW`-lijst van de scanner én als comment op de switch zelf.

#### `CategoryLabel` mocht vertaald, `DisplayName` niet — en dat verschil is gemeten

`DeepCleanItem.CategoryLabel` wordt óók gebruikt om op te zoeken (`DeepCleanDialog:289`) en om bundel-labels te bouwen (`:364`). Dat is veilig: groeperen en sorteren gaat op de **enum** (`GroupBy(i => i.Category)`, `OrderBy((int)i.Category)`), niet op de tekst — de `GroupKey`-vs-`Group`-afweging uit v1.2.4 speelt hier dus niet. Zoeken op de vertaling is juist gewenst: je zoekt op wat je ziet.

**`DeepCleanItem.DisplayName` is dat níét, en daarom blijven de tien cache-namen Engels.** `BundleByTokenOverlap` tokeniseert de DisplayName en clustert items die een significant token delen. In het Engels leveren *"User Temp folder"* en *"System Temp folder"* na de stopwoordenfilter (`user`, `temp`, `folder` staan er alle drie in) een **lege** tokenset op, en `HashSet.Overlaps` op lege sets is false — twee losse cards. Een Nederlandse variant als *"Tijdelijke bestanden (gebruiker)"* / *"(systeem)"* deelt wél twee significante tokens en zou in het Nederlands op één bundle-card belanden en in het Engels niet. Dat is een gedragsverschil per taal, en dat is een te hoge prijs voor tien kaarttitels.

> Het alternatief is niet weg: cache-items uitsluiten van token-bundeling (die bundeling bestaat voor verweesde mappen — "VMware" op drie locaties — niet voor een vaste lijst van tien caches) en de namen dan alsnog vertalen. Bewust niet in deze patch gedaan, want dat raakt werkende scanlogica in dezelfde diff als het vertaalwerk. Staat als los onderhoudsitem hieronder.

#### De vervelendste: `RestorePointService` was óók een sentinel

`RestorePointStatus.BlockedReason` werd door **beide** consumers zowel getoond als vergeleken: `status.BlockedReason.Contains("System Protection")`. De zin vertalen zou die vergelijking in het Nederlands stil breken — en de ontsnapping die de `WingetService`-sentinels in v1.2.3 kregen ("schrijven en vergelijken gebeurt in dezelfde taal") helpt hier niet, want de Nederlandse tekst bevat de woorden *System Protection* helemaal niet.

Opgelost door het veld te vervangen door een `bool ProtectionOff`. Daarmee zijn drie dingen tegelijk weg: de sentinel, de string-concatenatie in `RestorePointConfigDialog` (`BlockedReason + protectionOffSuffix`), en de tweede Nederlandse zin.

**Die tweede zin was inderdaad dood** — nagelopen zoals gevraagd. Voor het 24-uur-geval neemt `SettingsPage` de `settings.restore.rateLimited.tooltip`-tak en `RestorePointConfigDialog` de `restorePoint.lastWas`-tak; geen van beide raakte ooit aan `BlockedReason`. Weggehaald in plaats van vertaald.

#### De scanner: van twee naar vier passes

- **Pass 3 — switch-armen.** `=> "..."` heeft geen toewijzing en dus geen anker. Dit is de vorm waarin ~50 badges stonden.
- **Pass 4 — zin-achtige literals in `Dialogs/`, `Pages/` en `Helpers/`.** Blinde vlek 2 vergt strikt genomen dataflow-analyse; het pragmatische alternatief is élke literal met twee of meer echte woorden melden, met een `ALLOW`-lijst voor de rest.

Twee zwakke plekken die pass 4 zelf blootlegde en meteen gefixt zijn: geneste aanhalingstekens binnen een interpolatie (`$"…{string.Join(", ", parts)}…"`) braken de literal-regex en leverden halve strings op — nu herkend aan een ongebalanceerde `{` — en een `Diagnostics.Log(` die over twee regels loopt zette de log-marker op een ándere regel dan de literal, waardoor logregels als UI-tekst gemeld werden.

**De `ALLOW`-lijst is bestand-gebonden geworden, niet waarde-gebonden.** Dat bleek nodig bij een negatieve test: `"Recycle Bin"` is een terechte hardcoded `DisplayName` in `DeepCleanService`, maar als *badge* hoort 'ie vertaald — met een `ALLOW` op alleen de waarde zou de scanner die regressie nooit meer zien.

> **Bewuste beperking, expliciet benoemd:** pass 4 dekt `Services/` **niet**. Daar staan honderden PowerShell-fragmenten, registry-paden en exit-code-teksten, en de foutmeldingen uit de elevated batches zijn nog niet vertaald. Zie het losse onderhoudsitem hieronder; zet `Services/` er pas bij als dat af is, anders is de exit-code structureel rood.
>
> **Ingehaald door v1.2.8:** `Services/` zit er sindsdien wél bij. En de waarschuwing bleek terecht om een reden die hier niet voorzien was — de pass meldde daar aanvankelijk 0, niet omdat de map schoon was maar omdat drie fouten in de pass zelf alles wegfilterden. Zie de v1.2.8-sectie.

#### `check-catalog-keys.py`: de omgekeerde controle

Nieuw: **welke keys staan in de tabel maar worden nergens aangeroepen?** Dat is precies de bug die geen enkele scanner kán zien — een ongebruikte key betekent bijna altijd dat de call-site nog een literal heeft. Op de tabel van vóór deze patch leverde dat, met meervoud-resolutie (`Loc.Plural` plakt zelf `.one`/`.other`) en de zes runtime-opgebouwde families uitgesloten, **precies één** treffer op: `snapshot.entryLabel` — de key die drie versies lang netjes vertaald in beide tabellen stond terwijl de dialog een Nederlandse literal toonde.

Het script meldt hoeveel keys het overslaat en waarom, zodat "718 niet gecontroleerd" niet als "alles gedekt" leest.

#### Kleinere bewuste keuzes

- **`snapshot.entryLabel` is een meervoud-paar geworden** en heeft er een `snapshot.entryDateFormat` naast gekregen. Het datumformaat kon niet in dezelfde key: het bevat het woord `'om'`. En "1 registry values" klopte niet.
- **`OperationName` hergebruikt `nav.debloat` en `nav.debloat.deepClean`** in plaats van twee nieuwe literals — die keys bestonden al en zijn in beide talen gelijk.
- **De teller op DebloatPage** gebruikt nu `common.appCount` in plaats van een eigen `"{n} apps"`; `MS` en `OEM` blijven afkortingen.
- **De voorgestelde bestandsnaam** (`my-apps-2026-08-19`) blijft Engels: een bestandsnaam is geen proza, en per taal een andere naam maakt een export minder portabel. Het bestandstype-**label** ernaast loopt wél via de tabel.
- **Twee bewuste tekstcorrecties**, in de lijn van "resetten"→"reset" (v1.2.4) en "deze"→"dit" (v1.2.6): de drie browser-omschrijvingen zeiden *"Browser cache van …"* terwijl de al vertaalde Firefox-buur *"Browsercache van …"* zegt — geharmoniseerd. En `restorePoint.protectionOff` zegt nu *"Systeembeveiliging staat uit op deze pc"* in plaats van het half-Engelse *"System Protection is uit op deze PC"*, conform de InfoBar ernaast die dat al zo doet.

**Totaal 68 nieuwe keys**, plus `snapshot.entryLabel` vervangen door een meervoud-paar.

> **Geverifieerd (2026-08-19) — draaiende app, beide talen, via UI-Automation en niet alleen een geslaagde build:**
> - Build: **0 errors, 0 warnings**. `AssemblyVersion` 1.2.7, in de app zichtbaar als *"Huidige versie: 1.2.7"*.
> - **De cache-scan gerenderd in beide talen.** De badges wisselen mee: *Prullenbak / Update-cache / Browsercache* ↔ *Recycle Bin / Update cache / Browser cache*. De statusregel uit blinde vlek 2 klopt inclusief meervoud én culture: *"2 geselecteerd · 31,2 GB vrij te maken · 2 vereisen beheerdersrechten"* ↔ *"2 selected · 31.2 GB to free · 2 need administrator rights"*.
> - **De scan-locatieregel** vertaalt de proza-delen en laat de kale paden staan: *"Gescand: %TEMP% (gebruikers-temp) · %WINDIR%\Temp · … · Edge- / Chrome- / Brave- / Firefox-caches · Prullenbak"* ↔ *"Scanned: %TEMP% (user temp) · … · Edge / Chrome / Brave / Firefox caches · Recycle Bin"*.
> - **De restanten-scan (227 items) gerenderd in beide talen**, met een **identieke telling per badge** — dat is meteen het bewijs dat de identifier-uitzonderingen werken:
>
>   | EN | NL | × |
>   | --- | --- | --- |
>   | Orphaned folder | Verweesde map | 30 |
>   | MUIcache | MUIcache | 23 |
>   | Firewall rule | Firewallregel | 4 |
>   | Shortcut | Snelkoppeling | 2 |
>   | Orphaned registry | Verweesd registeritem | 2 |
>   | App Paths | App Paths | 1 |
>   | HKCU vendor | HKCU vendor | 1 |
>   | empty | leeg | 20 |
>
> - **Zoeken op een vertaald label werkt** — de afweging achter `CategoryLabel`. Filteren op `firewallregel` levert 30 treffers op, allemaal met de chip *Firewallregel*. In het Engels doet `firewall rule` hetzelfde.
> - **De bundeling is aantoonbaar taal-onafhankelijk gebleven:** de twee Firefox-profielen komen in beide talen op één card terecht (*Firefox / Browsercache ×2* ↔ *Firefox / Browser cache ×2*), en de cache-`DisplayName`s staan er in beide talen Engels bij. Precies waarvoor die uitzondering gemaakt is.
> - Live wisselen NL → EN → NL zonder herstart, inclusief de complete SettingsPage.
> - **Geen enkele `LOC-MISS`** in `install.log` over de hele sessie — alleen de `LAUNCH`-regel staat erin — en geen `crash.log`.
> - `scan-untranslated.py`: **0 / 0 / 0 / 0** over alle vier de passes. Negatief getest in een aparte boom: een teruggezette badge-literal en een teruggezette lokale-variabele-statusregel worden allebei w**él** gemeld, en `Prefetch` / `REG_DWORD` / een `Diagnostics.Log`-regel / een `Loc.S`-key terecht niet.
> - `check-catalog-keys.py`: 152/152 catalogus-keys in beide tabellen, **1265 = 1265**, geen wezen, geen lege waardes, en **546 keys aantoonbaar aangeroepen** (718 overgeslagen als runtime-opgebouwd, met vermelding).
>
> **Niet getest:** een echte delete-batch (de dialogs zijn met *Overslaan* gesloten), een echte install en een echte uninstall. Daardoor zijn drie blokken alleen via de tabel- en aanroepcontrole geverifieerd en niet visueel: de 11 `InstallDialog`-labels, de 8 sectiekoppen van `LeftoverCleanupDialog` (die dialog verschijnt pas ná een uninstall) en de statusregel daar. Ook niet visueel: de **`ProtectionOff`-tak** — op deze machine staat Systeembeveiliging aan en zijn er geen herstelpunten, dus zowel de waarschuwings-tak als de 24-uur-tak blijven onbereikbaar zonder Systeemherstel uit te zetten. De logica erachter is wel teruggebracht tot één bool, wat precies de reden was om 'm te verbouwen.

### v1.2.6 — Multi-language fase 5: de catalogus (`data/apps.json`)

**Vijfde van vijf geplande fasen.** De stringtabellen gaan van 1018 naar **1199 keys**, in beide talen even groot.

> **Correctie achteraf:** hier stond "de vertaalreeks is hiermee af". Dat klopte niet. Doorklikken na het committen leverde nog ~88 hardcoded strings op in drie vormen die de scanner per constructie niet zag — dat is v1.2.7 geworden, een zesde fase. De vijf-fasen-planning dekte het **corpus** (XAML, tweaks, bloatware, catalogus) maar niet de vormen waarin tekst buiten dat corpus in de code zit.

**De scope-telling in de roadmap klopte op drie punten niet.** Nageteld met een script over `apps.json` in plaats van met de hand:

| | Roadmap zei | Werkelijk |
| --- | --- | --- |
| Categorie-namen | 34 | **34** — maar dat is 10 top-level + 24 sub, niet 34 categorieën |
| Categorie-descriptions | 34 | 34 aanwezig, **maar 24 daarvan worden nergens gerenderd** |
| App-namen | 144 | **110 records → 108 unieke** wingetIds |
| App-descriptions | 144 | idem |

De 144 was een restant van vóór v1.0.2 (catalogus ging toen van 112 naar 110). Zelfde soort stale telling als bij v1.2.4 (117 → 124 tweaks) en v1.2.5 (15 → 17 categorie-chips). Reëel: **284** unieke strings als je alles zou doen; **152** voor wat er daadwerkelijk op het scherm staat en vertaald hoort te worden.

**110 app-records zijn 108 producten.** Proton VPN en Proton Drive staan elk twee keer — bewust gekruist sinds v1.0.2 (functie-categorie + Proton Suite). Zelfde patroon als de 68→55 bij bloatware: de key hangt aan de `wingetId`, dus die twee delen één vertaling in plaats van dat je dezelfde zin twee keer onderhoudt. **Eén bewuste tekstwijziging:** Proton VPN had twee *verschillende* omschrijvingen ("Privacy-focused Swiss VPN by Proton" in Security → VPN, "Encrypted VPN by Proton" in de Proton Suite). Eén moest winnen; de informatievere is gekozen, dus in de Proton Suite staat nu de eerste.

#### De gekozen aanpak: proza naar de tabel, identifiers blijven in de catalogus

Van de drie opties die op tafel lagen is het de derde geworden — dezelfde als fase 3 en 4 — maar in een verzachte variant.

- **`description` verhuist** naar `strings.*.json`, met de stabiele id als sleutel: `appCategory.<id>.name` / `.desc`, `appSubcategory.<id>.name`, `catalogApp.<wingetId>.desc`.
- **`name` van een app blijft gewoon in `apps.json`.** App-namen worden niet vertaald (zie hieronder), en dan is een key die in beide talen dezelfde waarde heeft alleen maar onderhoud. De regel is daarmee: *identifiers en eigennamen horen bij de catalogus, proza hoort in de vertaaltabel.*
- **Categorie- en subcategorie-`name` verhuizen wél**, want dat zijn generieke woorden ("Utilities", "Password Managers"), geen eigennamen.

**Waarom niet de twee opties die de roadmap noemde.** `"description"` + `"descriptionNl"` naast elkaar houdt de catalogus self-contained en laat een vergeten vertaling visueel opvallen, maar draagt een tweede vertaalmechanisme naast `strings.*.json`, valt buiten `LOC-MISS`, en een derde taal betekent een derde veld per record. Een parallel `apps.nl.json` was de zwakste: als enige vereist het echte merge-code in `AppDatabaseService`, en een app die je aan de catalogus toevoegt krijgt stilzwijgend geen NL-tekst zonder dat iets dat meldt. Met de gekozen aanpak is er **één mechanisme in de hele app**, dekt `LOC-MISS` het automatisch, en is een derde taal één extra bestand.

**De prijs, eerlijk benoemd:** `apps.json` is niet meer te lezen als catalogus — je ziet wat een app ís pas als je de stringtabel erbij pakt. Een app toevoegen raakt nu 3 bestanden in plaats van 1. `README.md` en `CONTRIBUTING.md` zijn daarop bijgewerkt, met een nieuwe stap in de contributor-flow en een verwijzing naar de checker.

**De icon-waarschuwing in de roadmap klopte niet.** Die zei dat de schemawijziging "`AppDatabaseService` en de icon-koppeling raakt". Het tweede is onjuist: `App.IconImage` bouwt `ms-appx:///Icons/{WingetId.Replace('.','-')}.png` en leest `Name` noch `Description`. Een vertaling-alleen wijziging raakt iconen dus nergens. Het eerste klopt maar half — `AppDatabaseService` is een kale `JsonSerializer.Deserialize` van 40 regels en is **ongewijzigd gebleven**; de wijziging zit volledig in het model.

**App-namen zijn bewust níét vertaald** (108 keys bespaard). Het zijn stuk voor stuk officiële productnamen, ook de 19 met een beschrijvend woord erin — "Brave Browser", "VLC Media Player", "Adobe Acrobat Reader", "TreeSize Free" heten in het Nederlands niet anders. Het precedent uit v1.2.4 (Foto's, Kladblok, Kaarten) wijst hier niet dezelfde kant op: dát ging over Microsoft-apps die Nederlands Windows zélf hernoemt, zodat je de regel in je eigen Startmenu terugkent. Bij third-party apps bestaat dat argument niet.

**Het mechanisme.** `Category.Name`/`.Description` en `SubCategory.Name` zijn lookups geworden op de `Id`; `App.Description` is een lookup op de `WingetId`. Eén verschil met fase 3/4, en het is belangrijk: **niet elke `App` komt uit de catalogus.** `WingetService.ParseSearchOutput` (winget-repo-zoekresultaten) en `ElevatedInstallRunner` (reconstructie in het ge-eleveerde kind) bouwen `App`-objecten die geen key hebben — bij een `Tweak` speelde dat niet, want die komen altijd uit `BuildAll()`. `App.Description` heeft daarom een settable backing field: wie zelf tekst zet wint, en alleen als niemand iets zet volgt de tabel-lookup. Bewust géén `Has()`-fallback, want dan zou een écht ontbrekende catalogus-key stil de Engelse tekst tonen in plaats van een `LOC-MISS` te geven.

**Twee omschrijvingen waren Nederlands, niet Engels.** De aanname "alles in apps.json is Engels" klopte op twee na: Adobe Creative Cloud en Adobe Acrobat Pro. Zelfde als de "het ging twee kanten op"-bevinding bij v1.2.5. Voor die twee is de Nederlandse tekst letterlijk overgenomen en het Engels nieuw geschreven; voor alle overige 106 is scriptmatig gecontroleerd dat de Engelse waarde **byte-voor-byte** gelijk is aan wat er in `apps.json` stond.

#### Bijvangst: 13 strings die de scanner niet kón zien

`scripts/scan-untranslated.py` liep aan het begin van deze fase schoon (0/0) en tóch stonden er nog 13 hardcoded gebruiker-zichtbare strings in de app. De scanner had drie blinde vlekken, alle drie nu gedicht:

1. **Geïnterpoleerde strings.** De C#-regex eiste een aanhalingsteken direct na het `=`, dus `Text = $"Deleting {n} item(s)..."` glipte er compleet doorheen. Dit was de grootste: het is precies de `item(s)`-constructie die v1.2.3 zou hebben opgeruimd.
2. **Meerregelige ternaries.** `Text = cond ? $"Safe to clean" : $"Caution — review carefully"` heeft op de anker-regel geen literal staan. De pass is daarom van regel-gebaseerd naar **statement-gebaseerd** gegaan: zoek het anker, pak alles tot het einde van het statement, kijk naar élke literal daarbinnen.
3. **Named arguments.** `body: "…"` staat niet op een property en werd dus niet bekeken.

Om de bredere vangst bruikbaar te houden filtert de scanner nu ook actief: literals die argument zijn van `Loc.S`/`Resources[`/`ToString(` zijn sleutels en geen tekst, een dotted identifier zonder spaties idem, en bij een geïnterpoleerde string telt alleen wat búiten de `{…}` staat (`$"({item.SizeLabel})"` draagt geen vertaalbare tekst).

Wat er gevonden is: **10 in `DeepCleanDialog` en `LeftoverCleanupDialog`** (de twee cleanup-dialogs die fase 2 duidelijk maar half heeft gehad — headers, tier-koppen, "Deleting N item(s)...", de klaar-meldingen, "from {app}"), **4 Nederlandse zinnen in `RestorePointConfigDialog`**, de `item(s)`-teller op `DebloatPage`, twee literals in `SnapshotBrowserDialog` (één Nederlands) en de `"Available via winget"` van de repo-zoekresultaten. Nieuwe keys daarvoor: 29.

> Eén bewuste tekstcorrectie meegenomen: "als je **deze** veiligheidsnet wil gebruiken" → "**dit** veiligheidsnet". Bestaande grammaticafout, geen vertaalfout, maar hij staat in de UI — zelfde afweging als "resetten" → "reset" in v1.2.4.

**Totaal 181 nieuwe keys:** 152 catalogus + 29 bijvangst.

#### Nieuw gereedschap: `scripts/check-catalog-keys.py`

Leidt uit `apps.json` af welke keys er moeten bestaan en bewijst dat ze in **beide** tabellen staan, zonder de app te starten. Controleert ook op wees-keys (een app verwijderen laat anders stille keys achter) en op lege waardes — die zouden als lege regel renderen in plaats van als zichtbare fout, dus die merk je nooit op. Draait in de contributor-flow.

> **Geverifieerd (2026-08-18) — draaiende app, beide talen:**
> - Build: **0 errors, 0 warnings**.
> - **Alle 10 categorie-tegels in beide talen**, naam én omschrijving: *Beveiliging & privacy / "Wachtwoordmanagers, VPN's en anti-malware"* ↔ *Security & Privacy / "Password managers, VPNs, and anti-malware"*, *Hulpprogramma's*, *Creatief & ontwerp*, *App-suites* — compleet in beeld gebracht door het venster te verkleinen tot 3 kolommen.
> - **Subcategorie-headers renderen**, o.a. alle zes van Ontwikkeling: *IDE's & editors / Versiebeheer / Runtimes / Virtuele machines & containers / Databasetools / API- & testtools*, plus *Wachtwoordmanagers* en *VPN* op Beveiliging.
> - **De zoekbalk is hier ook de volledige sweep**, net als bij de tweaks — maar via een andere route: `FuzzyMatcher` scoort op naam **+ wingetId** (bewust niet op description sinds v0.5.10), en élke wingetId bevat een punt. Eén punt typen levert dus de hele catalogus op in één platte lijst mét omschrijvingen. De 7 msstore-apps hebben een puntloze id (`9NKSQGP7F2NH`) en vallen buiten die ene query; die zijn met een tweede letter-query opgehaald (*Bitdefender / "Antivirus and threat protection (Microsoft Store)"*, *ChatGPT*).
> - **De gekruiste Proton-entries delen aantoonbaar één vertaling**: Proton Drive staat twee keer in de resultaten, beide keren met exact dezelfde omschrijving.
> - **De twee nieuw geschreven Engelse bronteksten renderen**: *"Full PDF editor by Adobe (license required)"* en *"Hub for Photoshop, Premiere, Illustrator, etc. (license required)"*.
> - **Het synthetische-App-pad is apart nagelopen** — dat was het enige echte risico van deze aanpak. Winget-repo-zoekopdracht op "notepad" geeft 10 resultaten die géén catalogus-key hebben; die tonen *"Beschikbaar via winget (v4.9.9…)"* ↔ *"Available via winget (v4.9.9…)"* en vallen dus terecht **niet** terug op de tabel-lookup.
> - Live wisselen NL → EN → NL zonder herstart, inclusief de complete SettingsPage.
> - **Geen enkele `LOC-MISS`** in `install.log` over de hele sessie (alleen de `LAUNCH`-regel staat erin), en geen `crash.log`.
> - `scan-untranslated.py`: **0 XAML-literals, 0 C#-literals** — nu met het uitgebreide net.
> - `check-catalog-keys.py`: **152/152 keys in beide tabellen**, geen wezen, geen lege waardes. Daarmee is een `LOC-MISS` op de Apps-pagina onmogelijk, ongeacht wat je aanklikt.
>
> **Niet getest:** een echte install-batch, en de Deep-clean- en Leftover-dialogs zijn niet end-to-end gedraaid — de 10 omgezette strings daarin zijn geverifieerd op tabel-aanwezigheid, niet visueel. Dat vereist een echte scan + delete.

**Werkwijze-notitie.** `apps.json` gebruikt **LF**, de stringtabellen **CRLF**. Beide zijn met expliciete `newline=''` gelezen én geschreven, en er is vooraf getest dat `json.dumps(indent=2, ensure_ascii=False)` de bestaande opmaak van `apps.json` exact reproduceert — zonder die check had een round-trip het hele bestand als gewijzigd getoond. De diff is nu 294 van de 854 regels, precies de weggehaalde velden.

### v1.2.5 — Multi-language fase 4: bloatware-metadata (+ de fase-2-restjes)

**Vierde van vijf fasen.** De stringtabellen gaan van 881 naar **1018 keys**, in beide talen even groot.

**68 dict-entries zijn 55 producten.** Dertien packages zijn aliassen van hetzelfde product — dezelfde app heet op verschillende Windows-versies anders (`HP.JumpStart` / `HPInc.HPJumpStart`, `MicrosoftTeams` / `MSTeams`, `MSI.MSICenter` / `9099B36F.MSICenter`, `AD2F1837.HPSmart` / `HPInc.HPSmart`, …). De key is daarom **per product**, niet per package: die aliassen delen één vertaling in plaats van dat je dezelfde zin twee keer onderhoudt.

**Twee categorie-labels stonden niet in de dict.** `BloatwareService.CategoryFromVendor` gaf een niet-gecureerd package de categorie `"Microsoft"` of `"OEM"` — óók zichtbare chips, en ze stonden in geen enkele scope-lijst. Meegenomen, dus 17 chips in plaats van 15.

**Totaal 127 nieuwe keys:** 55 namen + 55 omschrijvingen + 17 categorie-chips.

**Het ging twee kanten op.** De bestaande metadata was een mix: **25** omschrijvingen waren Nederlands (Engelse bron geschreven), **30** waren Engels (Nederlandse vertaling geschreven). Per product is de bestaande kant letterlijk overgenomen; een scriptmatige check vergelijkt elke overgenomen regel met de oude metadata en faalt bij één karakter verschil.

**Het mechanisme,** hetzelfde patroon als v1.2.4:
- `BloatwareMetadata(DisplayName, Description, Category)` → `(Key, CategoryKey)`.
- `BloatwareItem.DisplayName` / `.Description` / `.Category` zijn lookups geworden. Een **onbekend** package heeft geen vertaling en valt terug op de opgeschoonde package-naam — dat is een identifier en dus taalonafhankelijk, die blijft een gewone string.
- Sorteren gebruikt nu `IsCurated` in plaats van `!string.IsNullOrEmpty(Description)`. Zelfde volgorde, maar de sortering hangt niet meer af van of een vertáálde string leeg is.
- **Nederlandse app-namen volgen wat Nederlands Windows zelf toont**, want het doel is dat je de regel in je eigen Startmenu terugkent: Foto's, Kladblok, Kaarten, Films en tv, Plaknotities, Personen, Mobiel verbonden, Snelle hulp, Geluidsrecorder, Hulp vragen, Feedback-hub, Mail en Agenda, 3D-viewer, Mixed Reality-portal. Merknamen blijven staan (Xbox, Skype, Clipchamp, HP Smart, MyASUS, Lenovo Vantage, MSI Center …).
- `Games` en `Gaming` zijn in deze app twee verschillende categorieën (échte spellen vs. gaming-plumbing zoals de Xbox-overlays en de identity-broker). In het Nederlands blijft dat onderscheid overeind als **Spellen** en **Gaming**.

#### Bijvangst: 16 strings die fase 2 gemist had

Tijdens het naspelen van de Debloat-pagina viel op dat de InfoBar-**body** Nederlands bleef terwijl de **titel** wel meewisselde. Dat bleek geen incident, dus is er een scanner op de hele app gezet — alle XAML-tekstattributen die nog een letterlijke waarde hebben in plaats van een markup-extension, plus alle C#-literals die aan een `.Text` / `.Content` / `.Title` / `.Message` / `*ButtonText` worden toegekend. Uitkomst: **16 treffers**, nu allemaal om.

- **7 InfoBar-bodies** waarvan de titel al wél vertaald was: de welkomstbanner op Apps, de laadfout op Apps, de intro op Debloat, de intro op Deep clean, de profiel-modus-banner op Tweaks, de veiligheidsuitleg in `BackupPromptDialog` en de restore-point-uitleg in `RestorePointConfigDialog`. Zes waren Nederlands (Engelse bron geschreven), één was Engels (`apps.loadFailed.body`).
- **9 C#-toekenningen**, waarvan 3 een key hergebruiken die al bestond (`common.close` ×2, `common.done`, `common.ok`) en de rest nieuw: het `admin`-badge en de twee UAC-geweigerd-statusregels in `DeepCleanDialog` en `LeftoverCleanupDialog`.
- Eerder in dezelfde ronde waren op dezelfde manier al `"Select all"` / `"Deselect all"` / `"Nothing selected"` gevonden op 3 plekken (`DebloatPage`, `DeepCleanDialog`, `LeftoverCleanupDialog`) — hardcoded Engels terwijl `common.selectAll` / `.deselectAll` / `.nothingSelected` allang in beide tabellen stonden.

**De scanner staat er nu**, dus fase 5 kan ermee beginnen in plaats van erop te stuiten. Hij loopt nu schoon: 0 XAML-literals, 0 C#-literals.

> **Geverifieerd (2026-08-18) — draaiende app, beide talen:**
> - Build: **0 errors, 0 warnings**.
> - Debloat → Apps in **Nederlands én Engels**, met de Microsoft-bloatware-sectie uitgeklapt (35 gedetecteerde packages op deze machine). Cards tonen de vertaalde naam, omschrijving én categorie-chip: *Camera / Hardware / "Microsoft Camera — overbodig zonder webcam."* ↔ *Camera / Hardware / "Microsoft Camera — pointless without a webcam."*, en *Kladblok / Hulpprogramma's* ↔ *Notepad / Tools*.
> - **Het fallback-pad voor een onbekend package is ook echt gezien:** `aimgr` staat er met de opgeschoonde package-naam en de `Microsoft`-chip, zonder omschrijving — precies zoals bedoeld.
> - De omgezette InfoBar-bodies wisselen mee (Debloat-intro en Deep-clean-intro nagelopen in beide talen), en `Alles selecteren` / `Niets geselecteerd` ↔ `Select all` / `Nothing selected`.
> - **Geen enkele `LOC-MISS`**, geen `crash.log`.
> - **Volledigheid is niet op de UI-run gebaseerd** — die toont alleen de packages die op déze machine staan. Er is een checker die de keys uit de *code* haalt (de 68 dict-entries plus de twee vendor-fallbacks in `BloatwareService`) en aantoont dat alle 127 in beide tabellen bestaan. Daarmee is een `LOC-MISS` op dit scherm onmogelijk, ongeacht wat er geïnstalleerd is.
>
> **Niet getest:** een echte uninstall-batch (dat verwijdert apps), en de OEM-sectie — op deze machine staat geen HP/Dell/Lenovo/ASUS/Acer/MSI-bundleware, dus die 20 producten zijn alleen via de tabelcheck geverifieerd, niet visueel.

### v1.2.4 — Multi-language fase 3: het tweak-corpus

**Derde van vijf fasen**, en het enige blok dat **auteurswerk** was in plaats van vertaalwerk: de Engelse zinnen bestónden niet. De stringtabellen groeiden van **439 naar 881 keys**, in beide talen even groot.

**De inventarisatie klopte niet — het zijn 124 tweaks, geen 117.** Nageteld met een parser over `BuildAll()` in plaats van met de hand: 115 directe `new Tweak(`-registraties plus 9 via twee lokale factories (`BlockedTweak` ×7, `HideNavPaneItem` ×2). Die 9 zaten niet in de oude telling. Definitieve cijfers: 124 × `name`, 124 × `description`, **122** × `useCase` (de twee `HideNavPaneItem`-tweaks hebben er geen), 27 `TweakChoice`-labels over 7 choice-tweaks.

**Niet eerder geïnventariseerd: 12 sub-groep-headers.** `Tweak.Group` was een Nederlandse literal (`"Thema & kleuren"`, `"Items verwijderen"`, …) uit twaalf `const string`-declaraties in `BuildAll()`. Die staan als sub-headers op de detail-pagina en waren dus gewoon zichtbaar Nederlands; ze stonden in geen enkele scope-lijst. Meegenomen.

**Totaal 442 nieuwe keys:** 124 name + 124 desc + 122 useCase + 27 choice-labels + 12 sub-groepen + 12 categorie-namen + 12 categorie-blurbs + 4 status-pills + 1 admin-tooltip + 4 import/export-foutmeldingen.

**Het mechanisme.** `Tweak.Name`/`Description`/`UseCase` zijn van constructor-velden naar lookups op de stabiele `Id` gegaan (`tweak.<Id>.name` / `.desc` / `.useCase`). Daarmee verdwenen 350 argument-regels uit `TweakService.cs` (het bestand ging van 3412 naar 3037 regels) en is de tekst niet langer verdeeld over code en tabel. Verder:

- **`UseCase` is optioneel**, dus "key ontbreekt" is daar een geldige toestand. Nieuwe `LocalizationService.Has(key)` kijkt in de brontabel zónder een `LOC-MISS` te loggen — anders zouden die twee tweaks elke sessie een valse melding opleveren.
- **`TweakChoice` kent zijn eigen label niet meer.** De constructor is `new TweakChoice(values)`; de `Tweak`-constructor deelt bij het opbouwen de key uit op index (`tweak.<Id>.choice<N>`). De choice kende zijn index niet, de tweak wel.
- **`Tweak.Group` is gesplitst in `GroupKey` (stabiel) en `Group` (vertaald).** Groeperen en sorteren gaat op `GroupKey`; alleen de header-tekst komt uit `Group`. Groeperen op de vertaling zou twee groepen samenvoegen zodra beide talen dezelfde tekst opleveren, en de groepsvolgorde laten verspringen bij een taalwissel.
- **`StateLabel`, `AdminTooltip` en `TweakCategoryExtensions.DisplayName`/`Blurb`** lopen nu ook via de tabel; `Icon()` blijft hardcoded (emoji, taalonafhankelijk).

**`PropertyChanged` bleek niet het mechanisme.** De roadmap ging ervan uit dat een taalwissel opgelost is door `PropertyChanged` te vuren voor die drie properties. Dat klopt niet: `TweakCardFactory` bouwt de cards imperatief op (`Text = tweak.Name`, geen binding), dus er is niets om aan te hangen. Wat het écht doet is de bestaande v1.2.2-mechaniek — `MainWindow` navigeert de huidige page opnieuw, de cards worden vers opgebouwd en lezen dan de nieuwe tabel. `RaiseLocalizedTextChanged()` is er alsnog (en `TweakService` roept 'm aan op `LanguageChanged`), maar als vangnet voor toekomstige `x:Bind`-consumers, niet als het werkende pad.

**Tweak-profielen zouden stukgaan op vertaalde labels — opgelost.** `TweakProfileService` bewaart bij een multi-choice tweak het gekozen optie-**label** (bewuste v0.9.20-keuze: label i.p.v. index, zodat herordening het profiel niet corrumpeert). Met vertaalde labels zou een profiel dat je in het Nederlands exporteert niet meer importeren in het Engels. Nu: **export schrijft altijd het Engelse label** (brontaal, bestaat gegarandeerd), en `ResolveChoiceIndex` matcht bij het inlezen tegen de actieve taal **én** beide tabellen. Daarmee blijven ook profielen van vóór v1.2.4 werken — die bevatten de toen hardcoded mix van Engels ("Search box") en Nederlands ("5 seconden (standaard)"). Nieuwe `LocalizationService.Raw(key, language)` doet die taal-expliciete lookup zonder fallback en zonder `LOC-MISS`, want het is een vergelijking en geen weergave.

**Twee restjes uit fase 2 opgeruimd.** `TweakProfileService` en `SelectionImportExportService` gooiden nog vier hardcoded Nederlandse foutmeldingen naar de UI ("Bestand niet gevonden.", "Kon JSON niet lezen: …", "Bestand bevat geen apps./tweaks."). Nu `io.*`-keys.

**De Nederlandse tekst is één-op-één overgezet** — geverifieerd met een vergelijking tegen de vorige commit: 124/124 descriptions en 122/122 use-cases byte-voor-byte identiek. Eén bewuste uitzondering: in `Security.DisableBitLockerAutoEncryption` stond "vóór je Windows opnieuw installeert of *resetten*", dat is "of *reset*" geworden. Bestaande fout, geen vertaalfout, maar hij staat wel in de UI.

**Eén echte UI-bug gevonden en gefixt.** De categorie-tile voor Notifications & Lock Screen toonde na het vertalen van de categorie-naam twee keer dezelfde regel: naam én blurb waren "Meldingen & vergrendelscherm" (en in het Engels "Notifications & Lock Screen" / "Notifications & lock screen"). Zichtbaar geworden doordat de naam vertaald werd — daarvóór maskeerde het Engels/Nederlands-verschil het. De blurb is aangepast naar de vorm van zijn buren, die opsommen wat er ín de categorie zit: "Pop-ups, sounds & lock screen options" / "Pop-ups, geluiden & vergrendelscherm-opties".

> **Geverifieerd (2026-08-17) — draaiende app, beide talen, niet alleen een geslaagde build:**
> - Build: **0 errors, 0 warnings**.
> - **Alle 124 tweak-cards daadwerkelijk gerenderd, in beide talen.** De zoekbalk op de Tweaks-landing is daarvoor de volledige sweep: `Matches()` leest `Name` + `Description` + `UseCase` + categorie-naam van élke tweak, en elke treffer bouwt een echte card (inclusief status-pill, admin-tooltip en de ComboBox-labels van de choice-tweaks). Eén letter typen levert **"124 tweaks gevonden" / "124 tweaks found"** op — het hele corpus resolvet dus in één actie.
> - **Geen enkele `LOC-MISS`** in `install.log` over de hele sessie, en geen `crash.log`.
> - Detail-pagina UI/Thema met de vijf sub-groepen in beide talen: *Thema & kleuren / Desktop & vensters / Boot & login / Geluid / Invoer & weergave* ↔ *Theme & colors / Desktop & windows / Boot & login / Sound / Input & display*, mét de "1 / 4 actief"-tellers.
> - Choice-labels in de ComboBox: *Light / Dark / Custom (dark apps, light shell)*.
> - Live wisselen NL → EN → NL zonder herstart; de taalkeuze blijft in `settings.json` staan.
> - **De v1.2.2-valkuil is niet teruggekomen:** het NavigationView-Settings-item wisselt netjes mee (visueel gecontroleerd op een screenshot — zie de a11y-noot in *Open*). Op de Tweaks-pagina's staan geen `ToggleSwitch`-controls, dus het On/Off-probleem speelt daar niet.
>
> **Niet getest:** een echte Apply op een tweak (dat schrijft naar het register), en export/import van een tweak-profiel over een taalwissel heen. De export/import-code is wel op de vergelijkingslogica nagelopen, maar niet end-to-end gedraaid.

**Werkwijze-notitie.** De 350 argument-regels en 27 choice-labels zijn niet met de hand of met een regex weggehaald maar met een **C#-string-literal-parser** die per `new Tweak(`-blok de named arguments op diepte 1 opzoekt en op karakteroffset knipt. Reden: verbatim strings (`@"..."`), escapes (`\\`) en `//`-commentaar binnen de blokken maken een regex-aanpak onbetrouwbaar, en bij 124 registraties merk je één stille misser niet op. Dezelfde parser leverde ook de corpus-extractie voor het vertaalwerk. **Let op bij het herhalen hiervan:** lees en schrijf met expliciete `newline=''`, anders converteert Python het hele bestand stilletjes van CRLF naar LF en is de diff onleesbaar.

### Zonder versienummer — MSIX-onderzoek: afgerond, conclusie is **niet doen**

**Geen code-wijziging, dus geen patchnummer** — dit was een onderzoeksitem (stond eerder geschetst als v1.2.1; dat nummer is naar de security-fix gegaan die als eerste af was). Uitkomst: Setup Toolbox blijft op de Inno Setup-exe. Onderzocht op 2026-08-16, alléén blokker 1 (elevatie) en 2 (virtualisatie), volgens de eigen afspraak dat 3 (code signing) en 4 (Store) niet meer relevant zijn als één van die twee omvalt. **Ze vallen allebei om, en ze versterken elkaar.**

#### Blokker 1 — Elevatie: werkt alleen door de app *altijd* als admin te draaien

Elevatie van een MSIX-app is technisch mogelijk (sinds Windows 11; op Windows 10 vereiste het OS-wijzigingen die er nooit gekomen zijn — relevant want onze `TargetPlatformMinVersion` staat op 10.0.17763). Het vereist `rescap:allowElevation` in het package-manifest **plus** `requestedExecutionLevel="requireAdministrator"` in `app.manifest`; zonder dat tweede wordt de capability genegeerd.

En daar zit de klap: `requireAdministrator` geldt voor de **hele app**, altijd. Ons model is precies het omgekeerde — de app draait onge-eleveerd en vraagt één UAC voor de install-batch door zichzelf ge-eleveerd te herstarten (`ShellExecute` `runas` → `--install-runner`, v1.0.5). Dat patroon is onder MSIX niet na te bouwen: de exe staat in het ACL-beschermde `C:\Program Files\WindowsApps\` en een packaged app herstart zichzelf niet ge-eleveerd via een pad-launch.

Altijd-elevated sloopt drie bewuste ontwerpbesluiten tegelijk:
- **v1.0.6** zette `asInvoker` expliciet omdat de UAC Installer-Detectie-heuristiek anders toesloeg. Nu zou élke start een UAC-prompt geven.
- **v0.10.0** koos per-user install zónder UAC, wat ook de self-update zonder UAC mogelijk maakt.
- **Alle toasts vallen weg.** Microsoft documenteert het onomwonden: *"App notifications are not supported when your app is running with administrator privileges (elevated). Show will fail silently and no notification will be displayed."*

Dat laatste is de ironie van dit hele onderzoek: de **belangrijkste reden** om MSIX te overwegen was "echte package identity → toasts nátief via `AppNotificationManager`, zonder COM-activator-ellende". Maar om de rest van de app te laten werken moet je elevated draaien, en elevated werken toasts sowieso niet — packaged of niet. De aanname uit de oorspronkelijke roadmap-schets klopt niet.

#### Blokker 2 — Virtualisatie: het probleem is niet de container, het is de split-brain met child-processen

Standaardgedrag voor een full-trust (Centennial) packaged app, uit de Microsoft-documentatie:

| Locatie | Runtime-gedrag |
| --- | --- |
| **HKCU** | Schrijfacties gaan naar een **per-app private hive**; lezen is een merge van die hive met de echte HKCU. Weg bij uninstall, onzichtbaar voor andere apps. |
| **HKLM** | Schrijven mag *alleen* als de key niet in de package-hive zit én de gebruiker rechten heeft — "effectively only available to a Centennial app running elevated". **Verwijst dus terug naar blokker 1.** |
| **AppData** | Nieuwe bestanden naar een private per-app locatie. (De docs spreken zichzelf hier tegen: de containerization-overview van 2026 zegt "pass through for full-trust apps", de flexible-virtualization-pagina zegt van niet. Niet uitgezocht — verandert de conclusie niet.) |

Dit is deels te ontwijden met `unvirtualizedResources` + `RegistryWriteVirtualization=disabled`, en op Windows 11 met fijnmazige `ExcludedKeys`. Maar die fijnmazige variant dekt **alleen HKCU** en **alleen paden binnen `%USERPROFILE%\AppData`** — precies niet de HKLM-policies waar de helft van Tweaks op leunt.

**Het echte struikelblok is specifieker en erger.** Child-processen erven de package-identity **niet**: de kernel stript de token-claims van elk kind dat niet dezelfde app-identity heeft, en de virtualisatie-hive (`\Registry\WC\Silo{GUID}`) is alleen voor het hoofdproces. Onze architectuur is daar volledig op gebouwd — 17 bestanden met `Process.Start`, waarvan 11 aanroepen naar `powershell.exe`, plus `schtasks.exe`, `winget.exe` en `cmd.exe`. Gevolg: **de app schrijft naar twee verschillende registries tegelijk.**

Concreet waar dat stukloopt:
- **`TweakService`** splitst bewust: HKCU-ops in-process via `Microsoft.Win32.Registry` (→ gevirtualiseerd), HKLM + HKCU-Policies via een ge-eleveerde `reg.exe`-batch (→ echte registry). `DetectStatesAsync` leest in-process. De hele Enabled/Disabled/**Partial**-toestandsmachine — het fundament van de Tweaks-tab — zou naar de verkeerde hive kijken.
- **`SnapshotService`** legt de vorige staat in-process vast maar herstelt deels via ge-eleveerde `reg.exe`. Het registry-vangnet vóór elke Apply wordt dan onbetrouwbaar; dat is een dataveiligheidsprobleem, geen UX-smetje.
- **`LeftoverScannerService`** / **`DeepCleanService`** verwijderen HKCU-vendor-keys in-process — dat zou uit de private hive verwijderen en de echte key laten staan. Stil falen, precies het type bug dat v1.0.14 zo duur maakte.
- **`ToastProtocol`** schrijft `setuptoolbox:` naar `HKCU\Software\Classes`; gevirtualiseerd ziet de shell die registratie niet. (Onder MSIX zou je dat in het manifest declareren, dus dit ene punt is oplosbaar.)

#### Advies

**Niet migreren.** Blokker 1 is alleen te omzeilen door een permanente UAC-prompt bij elke start te accepteren en alle notificaties op te geven; blokker 2 vergt dat de complete apply/detect-architectuur van Tweaks, Snapshots en Deep Clean herschreven wordt om de virtualisatie te ontwijken. Samen is dat een herbouw van de kern van de app, in ruil voor een schonere uninstall en een update-mechanisme dat we al hebben en dat aantoonbaar werkt (v1.2.0). Blokker 3 en 4 zijn hiermee niet meer onderzocht — conform de afspraak.

> **Eerlijk over de methode:** dit is beslist op basis van de Microsoft-documentatie plus een inventarisatie van onze eigen call-sites, **niet** met een daadwerkelijk gebouwd en geïnstalleerd MSIX-package. Dat was de oorspronkelijk voorgestelde aanpak, maar de bewijsketen liep al dood vóór dat nodig was: elevatie-model en toast-ondersteuning spreken elkaar tegen ongeacht wat een PoC zou laten zien. Een PoC zou nog wél de AppData-tegenspraak in de docs kunnen beslechten en het split-brain-gedrag hard aantonen — de moeite waard alleen als we hier ooit op terugkomen.
>
> **Wat de conclusie zou kúnnen omdraaien:** als Microsoft app-notificaties voor elevated apps gaat ondersteunen, óf als er een manier komt om een packaged app on-demand te eleveren zonder `requireAdministrator`. Tot die tijd: niet heropenen zonder nieuw bewijs.

### v1.2.3 — Multi-language fase 2: alle overige XAML + code-behind en services

**Tweede van vijf fasen.** De infra uit v1.2.2 is nu over de hele app uitgerold, op het tweak-corpus, de bloatware-metadata en de catalogus na (v1.2.4 t/m v1.2.6). De stringtabellen groeiden van **~180 naar 439 keys**, in beide talen even groot.

**Wat er omgezet is:**
- **Alle 17 resterende XAML-bestanden** — 141 tekstattributen. Enige literal die bewust blijft staan is `Title="Setup Toolbox"` in `MainWindow.xaml`: dat is de app-naam, identiek in beide talen, en wordt bij het starten sowieso vanuit code gezet.
- **Code-behind van alle Pages en Dialogs** — inclusief de samengestelde voortgangsteksten ("Installing — 3 of 12 done"), de statuslabels van de uninstall-dialogs (`Waiting` / `Working...` / `Removed`) en de samenvattingsregels die met `string.Join` opgebouwd worden.
- **`WingetService.FriendlyError`** — de complete exit-code-mapping (13 meldingen). Deze stromen door naar de InstallDialog en waren het meest zichtbare stuk Nederlands dat overbleef.
- **`DeepCleanService`** — alle 17 item-descriptions, waarvan 10 runtime met interpolatie worden samengesteld; die zijn geparametriseerde format-strings geworden.
- **`RestorePointService.FormatAgo`**, `SnapshotService`, `ScheduleDialog`-omschrijvingen en de `InstalledAppEntry`-brontoelichtingen.

**De drie sentinel-constanten in `WingetService` zijn van `const` naar `static` property gegaan.** `RequiresUnelevatedMessage`, `CancelledMessage` en `MsStoreOpenedMessage` worden zowel getóónd als vergeleken (`message == MsStoreOpenedMessage`). Vertaald blijft die vergelijking kloppen zolang schrijven en vergelijken in dezelfde taal gebeuren, en dat is gegarandeerd: de install-dialog is modaal, dus de taalkeuze in Settings is tijdens een batch niet bereikbaar. Staat als comment bij de declaratie.

**Meervoud opgelost, niet ontweken.** De `item(s)`-constructie is vervangen door `Loc.Plural(key, count)` met `.one`/`.other`-varianten — die haakjes-ontwijking werkt in het Nederlands sowieso niet.

> **Geverifieerd (2026-08-17):** build 0 errors; app gestart en door de Tweaks-pagina gelopen met de taal op *Windows volgen* → Engels. Alle fase-2-strings resolven (`Restart Explorer`, `Search tweaks`, `6 / 11 active`, `Fully applied`, `Discard`). **Geen enkele `LOC-MISS`** in `install.log`, geen `crash.log`. De nog Nederlandse tekst die je op dat scherm ziet — de categorie-blurbs zoals "File Explorer weergave & gedrag" — is exact het corpus dat in v1.2.4 aan de beurt is.
>
> **Niet getest:** de install-, uninstall- en deep-clean-flows zijn niet end-to-end gedraaid; die vereisen echte installs. De strings erin zijn wel geverifieerd op resolutie via de tabelvergelijking (beide tabellen 439 keys, geen verschil).

**Werkwijze-notitie voor de volgende fasen:** de vervangingen zijn gedaan met een mappingbestand (`FILE|||OUD|||NIEUW`) dat door één perl-pass wordt toegepast, met letterlijke matching. Twee lessen die tijd kostten: een multi-byte delimiter (`§`) breekt `IFS`-splitsing in bash en produceerde onbruikbare bestanden — gebruik ASCII; en patronen met em-dashes moeten uit een als UTF-8 gedecodeerd bestand komen, niet uit de perl-broncode, anders matchen ze niet.

### v1.2.2 — Multi-language fase 1: infra + live toggle + SettingsPage als proof

**Eerste van vijf fasen.** De vertaal-infra staat en werkt end-to-end; de resterende ~1160 strings zijn v1.2.3 t/m v1.2.6 (zie *Open*). De onderbouwing van de gekozen aanpak staat verderop in deze sectie en is de referentie voor die fasen.

**Wat er is gebouwd:**
- **`Services/LocalizationService.cs`** — laadt `data/strings.en.json` + `data/strings.nl.json` naast de exe (zelfde `<Content>`-patroon als `apps.json`). Fallback-keten: actieve taal → Engels (brontaal) → de key zelf, zichtbaar in de UI zodat een gat opvalt tijdens het vertalen. Ontbrekende keys worden **één keer per key** als `LOC-MISS` gelogd, zodat een key in een ItemsRepeater niet 100 logregels oplevert.
- **`Helpers/Localize.cs`** — XAML-markup-extension `{loc:Localize Key=…}` als vervanger van `x:Uid`. Klasse heet bewust niet `LocalizeExtension`: UWP/WinUI-XAML kent de WPF-conventie van het weglaten van het achtervoegsel niet betrouwbaar.
- **Taalkeuze in Settings** — drie opties: *Windows volgen* (default), *English*, *Nederlands*. De systeem-optie toont welke taal dat nú oplevert ("Follow Windows — English"), zodat de keuze niet blind is.
- **`SettingsService.Language`** — `"en"` / `"nl"` / **`null` = volg systeem**. Dat null-onderscheid is functioneel: zonder dat is "gebruiker koos Engels" niet te scheiden van "systeem is Engels", en zou een latere systeemwissel stil zijn keuze overschrijven.
- **Live wisselen** — `MainWindow` luistert op `LanguageChanged`, zet de shell-teksten opnieuw en navigeert de huidige page opnieuw. Geen page zet `NavigationCacheMode`, dus de Frame maakt gegarandeerd een verse instantie; markup-extensions evalueren alleen bij het parsen, dus dat is precies wat nodig is.
- **Opmaak-helpers per taal** — `ToastHelper.JoinDutch` is `LocalizationService.JoinList` geworden ("A, B **en** C" / "A, B **and** C"). `FormatBytes` stond **vier** keer in de codebase (`DeepCleanDialog`, `DeepCleanItem`, `LeftoverItem`, `DeepCleanPage`), elk op de systeem-culture; nu één implementatie, gekoppeld aan de **gekozen** taal — anders zag een gebruiker op Nederlands Windows die Engels kiest nog steeds "1,5 GB". Afrondgedrag is identiek gehouden aan de originelen. Nieuw `Loc.Plural(key, count)` vervangt de `item(s)`-ontwijking, die in het Nederlands sowieso niet werkt.
- **Ingebouwde WinUI-strings overgenomen** — zie de valkuil hieronder.
- **`/toasttest` logt nu de toast-tékst** (`lang=` + `text="…"`) in plaats van alleen `Show() OK`. Die debug-switch bestaat om de toast-tekst te verifiëren, maar dat kon alleen door naar het scherm te kijken; met twee talen is juist die tekst wat je wilt narekenen.

**Valkuil die twee ronden kostte: ingebouwde WinUI-teksten volgen onze keuze niet.** Het `NavigationView`-Settings-item en de `ToggleSwitch` On/Off-labels komen uit WinUI's eigen resources via MRT. Op de testmachine leverde dat een Nederlands **"Instellingen"** naast een verder volledig Engelse UI, en bleef de toggle "On" tonen in het Nederlands. Beide moeten expliciet gezet worden (`settingsItem.Content`, `OnContent`/`OffContent`). Let op dat `NavView.SettingsItem` in de constructor nog `null` is — dat item bestaat pas nadat het control-template is toegepast, dus de shell-teksten worden ook na `NavView.Loaded` nog een keer gezet.

**Bijvangst — MRT en .NET zijn het oneens over "de systeemtaal".** Op de testmachine: Windows-**weergavetaal** = en-GB (`InstallLanguage=0809`), **regio-instelling** = nl-NL, **taallijst** = nl-NL. `CultureInfo.CurrentUICulture` volgt de weergavetaal (en-GB), MRT volgt de taallijst (nl-NL). Wij volgen bewust `CurrentUICulture`, want dat is de taal waarin de rest van Windows tegen de gebruiker praat. Dit verschil is precies waarom de toggle een eigen detectie nodig had.

> **Geverifieerd (2026-08-16) — draaiende app, niet alleen een geslaagde build:**
> - **Live wisselen werkt beide kanten op**, zonder herstart: EN → NL → EN, met de complete SettingsPage vertaald tot en met de onderste sectie (Windows-herstelpunten, App-updates, Diagnostiek), inclusief radiobuttons en de "Aan"/"Uit"-toggles.
> - **De markup-extension resolvet daadwerkelijk** — dit was het hele risico van deze aanpak.
> - **Taalkeuze is persistent**: `"language": "nl"` in `settings.json`, en na herstart staat de app in die taal.
> - **Default voor een verse gebruiker klopt**: `language`-key verwijderd → combo staat op "Follow Windows — English" en de UI is Engels, conform de weergavetaal van de machine.
> - **Geen enkele `LOC-MISS`** in `install.log` na het doorlopen van alle schermen, en geen `crash.log`.
> - **Toasts in beide talen**, met het juiste voegwoord uit `JoinList`:
>   `lang=Dutch text="Vivaldi en VLC media player zijn bijgewerkt. Notion is mislukt — …"`
>   `lang=English text="Vivaldi and VLC media player have been updated. Notion failed — …"`
>   Twee `Show() OK`, de tweede vervangt de eerste, proces sluit netjes af.
>
> **Niet getest:** de Release-build en de installer. Debug-build only, net als bij de meeste patches.

**Nog Nederlands en dus zichtbaar half-af tot v1.2.3:** alle andere schermen. Dat is bewust — de app was vóór deze wijziging al half Nederlands / half Engels, dus dit is geen regressie.

#### Resource-loading op unpackaged: gemeten, niet aangenomen (2026-08-16)

Twee wegwerp-projecten in een scratchpad (`PriSpike` = headless, `UidSpike` = echt WinUI-venster), allebei met exact onze pins: `net10.0-windows10.0.26100.0`, WinAppSDK `1.8.260710003`, `WindowsPackageType=None`, en een assembly-naam die níét "resources" is — zodat de PRI net als bij ons `<AssemblyName>.pri` heet. Repo ongemoeid gebleven.

**Wat wél werkt:**

| Test | Uitkomst |
| --- | --- |
| `.resw` → PRI in een unpackaged build | **Werkt.** De RESW-indexer zit al in onze `priconfig.xml` (`convertDotsToSlashes="true"`, default-qualifier `Language=en-US`) |
| `.resw` expliciet als `<PRIResource>` includen | **Niet doen** — `error NETSDK1022`, de SDK neemt ze al impliciet op |
| `new ResourceLoader()` (parameterloos) unpackaged | **Werkt**, óók met een PRI die `PriSpike.pri` heet. De v0.6.0-bestandsnaam-vloek speelt hier niet |
| Fallback naar `en-US` bij een key die alleen in de bron-taal bestaat | **Werkt** — key uitsluitend in `en-US` resolved netjes onder een `nl-NL`-context |
| `ResourceContext` + `QualifierValues["Language"]` per lookup | **Werkt**, beide talen naast elkaar in één proces, zonder herstart |
| Punt-keys (`CloseButton.Content`) | Worden `CloseButton/Content` in de resource-map; ook vanuit code leesbaar |

**Wat NIET werkt — en dit is de beslissende bevinding:**

Er is op een unpackaged WinUI 3-app **geen enkele manier om de app-brede taal te overrulen.** Alle drie de gedocumenteerde routes zijn gemeten en vallen om:

| Route | Resultaat |
| --- | --- |
| `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` | Gooit `InvalidOperationException` |
| `SetProcessPreferredUILanguages` (Win32) | Slaagt op Win32-niveau (`ok=True`, `Get…` geeft `en-US` terug) maar **MRT negeert 'm** — de default context blijft de Windows-weergavetaal. Ook getest mét auto-bootstrap uít, dus taal gezet vóór WinAppSDK-init: geen verschil. "MRT was al warm" is dus geen verklaring |
| `Windows.ApplicationModel.Resources.Core.ResourceContext.SetGlobalQualifierValue` | **Kill't het proces hard.** Geen vangbare exception, log stopt midden in de call |

**Gevolg: `x:Uid` is onbruikbaar voor een taalkeuze in deze app.** In het WinUI-venster met een `x:Uid`-TextBlock en `x:Uid`-Button kwam er onder een gevraagde `en-US` gewoon Nederlands uit — `x:Uid` volgt de Windows-weergavetaal en is niet om te buigen. Een gebruiker op Nederlands Windows zou de app nooit op Engels kunnen zetten; de toggle zou voor álle XAML-strings stil niets doen. In hetzelfde proces gaf een expliciete `ResourceContext` wél netjes beide talen.

> Dit is precies de aanname die getest moest worden. `.resw` blijft bruikbaar als *opslagformaat* met een expliciete context per lookup — maar `x:Uid`, de enige reden om voor `.resw` te kiezen boven een gewone tabel, valt weg.

#### Inventarisatie bijgesteld na narekenen

> **Nogmaals bijgesteld in v1.2.4:** de tweak-regel hieronder klopt niet. Het zijn **124** tweaks, niet 117 — 9 registraties lopen via twee lokale factories en werden met de hand niet meegeteld. Zie de v1.2.4-sectie voor de definitieve cijfers.

De eerdere telling was te laag; hij dekte alleen het tweak-blok. Gemeten aantallen:

| Bron | Aantal | Waarvan Engelse bron nog te schrijven |
| --- | --- | --- |
| `TweakService`: 117 × name / 117 × description / 115 × useCase | ~349 | ~232 (description + useCase) |
| `TweakService`: 27 × `TweakChoice`-label (verspreid over **7** choice-tweaks, niet 27 groepen) + descriptions | ~40 | deels — labels zijn gemengd ("Search box" vs "5 seconden (standaard)") |
| XAML: 166 unieke literals / 208 voorkomens over 18 bestanden | 166 | 54 (de Nederlandse) |
| Code-behind + services buiten `TweakService` | ~160 | ~160 |
| `TweakCategory`: 12 × DisplayName + 12 × Blurb + StateLabel/AdminTooltip | 29 | 12 blurbs |
| `BloatwareItem.CuratedMetadata`: 68 × naam + 68 × description + categorie-labels | ~151 | ~40 |

Ruwweg **900+ strings**, waarvan **~470 nieuw Engels auteurswerk** — dus fors meer dan de ~234 uit de eerste schatting.

**De ~624 uit de eerste telling was een overschatting:** dat was elk `Attribuut="waarde"`-paar. Filter je bindings (`{x:Bind …}`), lege waarden en niet-tekst attributen eruit, dan blijven er 208 voorkomens over.

#### Niet eerder geïnventariseerd

- **`DeepCleanService`** — 12 statische `DeepCleanItem`-definities plus descriptions die **runtime met interpolatie** worden samengesteld (`$"Uninstall registry-entry zonder werkende paden. {aliveResult.Reason} — …"`). Die kunnen geen platte string worden; ze hebben een geparametriseerde format-string nodig.
- **`WingetService`** — de gebruiker-zichtbare foutmeldingen (`FriendlyError`, `CancelledMessage`) stromen door naar de InstallDialog.

**Valkuilen:** `ToastHelper.JoinDutch` bouwt "X, Y en Z" — dat is grammatica, geen string. Maar het is niet de enige opmaak-helper: `FormatBytes` bestaat **twee keer** (`DeepCleanDialog` én `DeepCleanItem`) en formatteert op de actieve culture, `item(s)` ontwijkt meervoudsvormen met haakjes, en `RestorePointConfigDialog` bouwt `"{ago} geleden"`. Let op de inconsistentie die dit oplevert: kiest een gebruiker op Nederlands Windows voor Engels, dan blijft `FormatBytes` "1,5 GB" tonen tenzij de opmaak aan de gekozen taal wordt gekoppeld in plaats van aan de systeem-culture.

#### Besloten (2026-08-16, user)

- **Infra: eigen string-tabel als gebundelde JSON** (`data/strings.en.json` + `data/strings.nl.json`), plus een XAML-markup-extension `{loc:S Key=…}` als vervanger van `x:Uid`. Volgt exact het `data/apps.json`-patroon dat in deze app al bewezen werkt (`<Content>` + `PreserveNewest`). `.resw` is afgevallen: zonder `x:Uid` — dat op bewijs onbruikbaar is — houdt het alleen nadelen over (verbose XML voor 1250 strings, PRI-rebuild per wijziging, punt-in-sleutel wordt schuine streep). Een combinatie van beide is expliciet verworpen: dat draagt twee sleutelconventies en twee faalmodi voor hetzelfde resultaat.
- **Live wisselen**, via "taal zetten → huidige page opnieuw navigeren". `MainWindow` bouwt pages toch al opnieuw op via `ContentFrame.Navigate`. Bijkomend werk: `Tweak.Name`/`Description`/`UseCase` van constructor-velden naar lookups (`Tweak` implementeert al `INotifyPropertyChanged`), plus expliciete refresh van de NavigationView-labels — die leven in `MainWindow.xaml` en worden níét opnieuw opgebouwd.
- **`apps.json` valt volledig binnen scope** — afwijkend van mijn advies om 'm uit te stellen. Dat is ~356 strings extra (34 categorie-namen + 34 categorie-descriptions + 144 app-namen + 144 app-descriptions) plus een schemawijziging, en brengt het totaal op **~1250 strings**.
- **Default: Windows-weergavetaal volgen, Engels als fallback.** In `settings.json` wordt het onderscheid bewaard tussen "gebruiker koos expliciet" en "volg systeem" (afwezig/`null` = volg systeem), anders is "gebruiker koos Engels" niet te onderscheiden van "systeem is Engels" en overschrijf je later stil zijn keuze.

#### Fasering — vijf patches (besloten 2026-08-16, user)

Eén diff van ~1250 strings is niet te reviewen en niet te bisecten, en het mechanisme is ander werk dan het auteurswerk: bij een fout mechanisme merk je dat na ~90 strings in plaats van na 1250. Elke fase is los te bouwen én te starten. De app is nu al half-en-half, dus een tussenstand is geen regressie.

| Versie | Inhoud | Omvang |
| --- | --- | --- |
| **v1.2.2** | Loc-service + `{loc:S}` markup-extension + live-switch mechaniek + taalkeuze in Settings + **SettingsPage volledig tweetalig als proof**. Plus de opmaak-helpers: `JoinDutch` → taalafhankelijke `JoinList`, `FormatBytes` ontdubbeld en aan de gekozen taal gekoppeld, ToastHelper-teksten | ~90 strings |
| **v1.2.3** | Overige XAML (17 bestanden) + code-behind/services, incl. `WingetService`-foutmeldingen en de geïnterpoleerde `DeepCleanService`-descriptions | ~270 |
| **v1.2.4** | Tweak-corpus: 117 × name/description/useCase, 27 choice-labels, 12 categorie-blurbs | ~390 |
| **v1.2.5** | 68 `BloatwareItem.CuratedMetadata`-entries | ~150 |
| **v1.2.6** | `data/apps.json` (~356) incl. schemawijziging | ~356 |

SettingsPage is bewust fase 1: dat scherm *bevat* de toggle, dus de omschakeling is zichtbaar op de plek waar je 'm indrukt. De catalogus staat apart van bloatware omdat de `apps.json`-schemawijziging `AppDatabaseService` en de icon-koppeling raakt — los houden maakt terugdraaien makkelijker.

> **Achteraf (v1.2.6):** de fasering heeft gewerkt, maar de omvang-kolom klopte structureel te hoog en de icon-zorg was ongegrond. Elke fase telde bij aankomst anders uit dan hier geschat (117→124 tweaks, 68 entries→55 producten, 356→152 catalogus-strings). Definitieve uitkomst: **1199 keys** in beide tabellen, niet de ~1250 uit de schatting — en dat inclusief 29 keys voor strings die pas in fase 5 gevonden zijn. De les voor een volgende meerfasen-klus: tel elke fase opnieuw met een script, en behandel de schatting in de roadmap als een orde van grootte, niet als een scope.

**Gevolg voor de rest van de roadmap:** config-backup schuift naar **v1.2.7**, website naar **v1.2.8**.

> **Achteraf:** het zijn zes fasen geworden en die nummers zijn nog een keer opgeschoven — de restjes werden v1.2.7, config-backup v1.2.8 en de website v1.2.9. Precies het "nummering volgt de uitleververvolgorde"-principe uit de kop van dit bestand.

### v1.2.1 — Kwetsbare transitieve dependency weggepind: `System.Drawing.Common`

Elke `dotnet restore` gaf **`warning NU1904: Package 'System.Drawing.Common' 4.7.0 has a known critical severity vulnerability`** ([GHSA-rxg9-xrhp-64gj](https://github.com/advisories/GHSA-rxg9-xrhp-64gj), CVE-2021-24112). Opgelost met één directe `PackageReference` op **10.0.11**.

> **Stond op de roadmap geschetst als v1.2.5**, maar was als eerste af en is dus als v1.2.1 uitgeleverd — nummers volgen de uitleververvolgorde, niet het roadmap-vakje. De rest van de reeks is meegeschoven; zie de notitie bovenaan dit bestand.

**Herkomst — geverifieerd, niet aangenomen.** `dotnet nuget why` geeft precies één pad: `Microsoft.Toolkit.Uwp.Notifications` 7.1.3 → `System.Drawing.Common` 4.7.0. Het vermoeden uit de v1.2.0-notitie klopte, maar het was het narekenen waard omdat de rest van de conclusie er wél anders uitkwam dan verwacht.

**Het was geen papieren waarschuwing.** `System.Drawing.Common.dll` stond gewoon in `bin/Release/…/win-x64/publish/` — we leverden de kwetsbare DLL mee. En de code is *live*: een metadata-scan van de toolkit-DLL laat `ExtractAssociatedIcon` → `ToBitmap` → `Save` + `System.Drawing.Imaging` zien. Dat is de icoon-extractie die `ToastNotificationManagerCompat` bij élke `Show()` doet voor de AUMID-registratie (het resultaat is de `Icon.png` waar de `IconUri` in `HKCU\Software\Classes\AppUserModelId\<exe-pad>` naar wijst). Praktische exploiteerbaarheid blijft nihil — CVE-2021-24112 vereist een geprepareerde afbeelding en de input is ons eigen exe-icoon — maar "we leveren 'm mee en elke scan gaat erop af" was reden genoeg.

**Migreren naar `CommunityToolkit.WinUI.Notifications` lost het NIET op** — dit was de aanname vooraf en die sneuvelde. De nuspec van 7.1.2 (de nieuwste; er is géén 8.x) declareert op z'n `net5.0-windows10.0.18362`-target exact dezelfde `System.Drawing.Common [4.7.0, )`. Zelfde code, andere naam, en één versie *ouder* dan de 7.1.3 die we al hadden. Dat zou een namespace-rename in drie bestanden zijn zonder enige winst. **Niet nog eens onderzoeken.**

**Waarom pinnen wél schoon werkt.** De dependency-range van de toolkit is `[4.7.0, )` — een minimum, geen exact. Een directe `PackageReference` wint dus zonder `NU1605`-downgrade-conflict. Vooraf getest in een wegwerp-project (repo ongemoeid): 4.7.3, 8.0.30 én 10.0.11 laten NU1904 alle drie verdwijnen, zonder extra NuGet-warnings.

**Waarom 10.0.11 en niet 4.7.3.** 4.7.3 is de behoudende keuze (zelfde 4.x-lijn waartegen de toolkit gecompileerd is, alleen de securityfix erin), maar die lijn krijgt geen onderhoud meer — dat dekt déze advisory, niet de volgende. 10.0.x loopt mee met onze `net10.0`-target en blijft gepatcht. Het risico dat daar tegenover staat: de toolkit is gecompileerd tegen assembly-versie 4.0.0.1 en bindt runtime aan 10.x. .NET Core lost dat via `deps.json` op, maar dat is een aanname die getest moest worden — zie hieronder.

**Nevenwinst:** `Microsoft.NETCore.Platforms` (3.1.0) valt volledig uit de graph en `Microsoft.Win32.SystemEvents` gaat van 4.7.0 → 10.0.11.

> **Geverifieerd (2026-08-16):**
> - `dotnet restore --force` op de échte csproj: **NU1904 weg**, geen andere NuGet-warnings. Build: 0 warnings, 0 errors. De DLL in `bin/Debug` is `10.0.1126.37416` / `10.0.11`.
> - **De `System.Drawing`-route is aantoonbaar uitgevoerd**, niet omzeild door een gecachete registratie. Bewijs: de AUMID-registratie (`HKCU\…\AppUserModelId\<exe-pad>`) en de bijbehorende `Icon.png` zijn verwijderd, daarna `/toasttest` opnieuw gedraaid → registry-key én `Icon.png` (3653 bytes) zijn opnieuw aangemaakt. `ExtractAssociatedIcon → ToBitmap → Save` draait dus gewoon onder 10.0.11. Dit was het enige echte risico van deze wissel.
> - `/toasttest`: beide toasts `Show() OK` in `SetupToolbox_toast.log`, tweede vervangt de eerste, proces sluit netjes af.
> - `/updatecheck`: **23 entries** geparsed, msstore-bron erbij, beide toasts OK. `winget upgrade` meldt zelf ook "23 upgrades available" — dus 23/23, niets gemist. (v1.0.14 telde er 24; er staat nu simpelweg geen tweede "explicit targeting"-tabel in de output. `ParseUpgradeTables` is niet aangeraakt.)
> - App start normaal, venster reageert, geen `crash.log`.
>
> **Bevinding voor het MSIX-onderzoek:** de aanname "MSIX geeft package identity → toasts native via `AppNotificationManager` → toolkit kan eruit → kwetsbaarheid weg" gaat waarschijnlijk **niet** op. De Microsoft-docs stellen expliciet: *"App notifications are not supported when your app is running with administrator privileges (elevated). Show will fail silently and no notification will be displayed."* Onze auto-update-task draait op `RunLevel=Highest` (v1.0.14) — precies de plek waar de toasts vandaan komen. Package identity verandert daar niets aan. Dat maakt "de toolkit eruit slopen" een slecht idee, los van MSIX.

### v1.2.0 — Milestone-release: v1.0.1 t/m v1.0.14 uitgeleverd

**Geen nieuwe functionaliteit — dit is een release-cut.** De enige code-wijziging is de versiebump in `SetupToolbox.csproj` (`1.0.14` → `1.2.0`, incl. `AssemblyVersion` + `FileVersion`). Alles wat hier uitgeleverd wordt staat inhoudelijk beschreven in de v1.0.1 t/m v1.0.14-secties hieronder.

**Waarom nu.** Sinds v1.0.0 (21 mei) was er geen GitHub Release meer, terwijl de code 14 patches verder liep. `GitHubService` vergelijkt de `AssemblyVersion` met de nieuwste stabiele release die een `SetupToolbox-v*.exe`-asset heeft — zonder Release ziet self-update dus letterlijk niets. Geïnstalleerde gebruikers zaten ruim twee maanden op v1.0.0, inclusief de bugs die daarna gefixt zijn.

**Waarom v1.2.0 en niet v1.1.0.** v1.1.0 is in het WPF-tijdperk al eens als Release gepubliceerd en later bij de opschoning verwijderd. Dat nummer hergebruiken zou de tag-historie dubbelzinnig maken, dus overgeslagen.

**Wat gebruikers hiermee binnenkrijgen** (de zwaarste posten uit de stapel):
- **De app crasht niet meer weg tijdens een geslaagde install** — globaal `UnhandledException`-vangnet + crash-safe progress-callbacks (v1.0.11), en de `ContentDialog`-gate die de post-install prompt-crash oploste (v1.0.12).
- **Geplande auto-updates draaiden nooit op een laptop op accu** — stil, zonder enig signaal (v1.0.14). Dit is de fix die het cutten van deze milestone urgent maakte.
- **Eén UAC-prompt per install-batch** i.p.v. per app (v1.0.5/v1.0.6), plus de abort-knop voor een hangende batch (v1.0.7).
- **Toast-notificaties rond de auto-update** met de namen van de bijgewerkte apps (v1.0.13).
- Install-lanes voor apps die niet in de standaard-batch passen: Spotify onge-eleveerd, Battle.net met locatiekeuze (v1.0.10), MSI-serialisatie tegen lock-botsingen (v1.0.12).

**Roadmap-hernummering:** de openstaande items zijn meegenummerd, `v1.0.15`–`v1.0.18` → **`v1.2.1`–`v1.2.4`** (MSIX-onderzoek, config-backup, multi-language, website). Inhoudelijk ongewijzigd.

> **Geverifieerd (2026-08-16):**
> - Release **live**: https://github.com/MisterDuckles/SetupToolbox/releases/tag/v1.2.0 — geen draft, geen pre-release, asset `SetupToolbox-v1.2.0.exe` (69,0 MB, `state=uploaded`).
> - **Self-update-pad nagespeeld tegen de live API** met exact de filterlogica uit `GitHubService` (draft/prerelease overslaan → asset matchen op `^SetupToolbox-v.*\.exe$` → `TryParseTag` → hoogste versie winnen). Uitkomst: hoogste kandidaat **v1.2.0**; een gebruiker op v1.0.0 én op v1.0.14 krijgt de update aangeboden, op v1.2.0 niet. Dit was het hele doel van de milestone, dus niet op gegokt.
> - Release-build **start daadwerkelijk** (venster "Setup Toolbox"), `FileVersion = 1.2.0`. Bewust gecontroleerd omdat v1.0.14 liet zien dat een niet-startende build geen enkel spoor achterlaat.
> - `origin/main` staat op de release-commit; de acht commits v1.0.8 t/m v1.2.0 zijn gepusht.
>
> **Niet getest:** de installer zelf (install → self-update → herstart) is niet op een schone VM gedraaid. De Inno Setup-flow is sinds v0.10.0 ongewijzigd, dus het risico is laag, maar het is geen bewijs.
>
> **Omgevings-hobbel, geen projectbug:** de 1Password SSH-agent weigerde tijdens deze sessie de push (`communication with agent failed`) terwijl het ondertekenen via `op-ssh-sign.exe` — een andere code-route — wél werkte. Gepusht via HTTPS met `gh` als credential-helper voor die ene aanroep; geen blijvende config-wijziging.

### v1.0.14 — Auto-update draait niet op accu + explicit targeting + WindowsAppSDK gepind

Drie bevindingen uit de elevated-verificatie van v1.0.13 (2026-08-16).

- **De geplande auto-update draaide niet op accustroom.** `TaskSchedulerService.CreateUpdateTaskAsync` maakte de task met `schtasks.exe /create`, en dat zet standaard **`DisallowStartIfOnBatteries = True`** (plus `StopIfGoingOnBatteries`). Gemeten op een laptop op accu: Task Scheduler meldt `LastTaskResult = 0` en werkt `LastRunTime` bij, maar start **geen proces** — volledig stil en niet te onderscheiden van een geslaagde run. Pas na het uitzetten van die vlag startte dezelfde task wél. Op een laptop draaiden de auto-updates dus feitelijk nooit, zonder enig signaal.
  - **Fix:** task aanmaken via `schtasks /create /xml` met een eigen definitie (`WriteTaskXml`), waarin beide accu-vlaggen op `false` staan. Meteen ook `StartWhenAvailable=true` (haalt een gemiste run in als de machine sliep — juist op een laptop relevant), `ExecutionTimeLimit=PT2H` (default was 72 uur) en `MultipleInstancesPolicy=IgnoreNew`. Eén UAC blijft behouden; de XML gaat als UTF-16 naar `%TEMP%` en wordt na `/create` opgeruimd.
  - **`RunOnlyIfNetworkAvailable` bewust op `false`.** Een run zonder netwerk faalt netjes en meldt dat via een notificatie; een niet-gestarte task is volledig stil — en precies dat stille falen is de bug die we hier fixen. Geen tweede zwijgende voorwaarde erbij.
- **Pakketten die "explicit targeting" vereisen werden overgeslagen.** winget zet die in een **tweede tabel** met eigen kolombreedtes; `ParseListOutput` rekent met de kolomposities van één header, dus die rijen vielen door de lengte-check.
  - **Fix:** nieuwe `ParseUpgradeTables` knipt de output in blokken per header en draait de bestaande `ParseListOutput` per blok, met dedup op Id. De parser zelf blijft ongemoeid — die wordt ook door `GetInstalledAppsListAsync` gebruikt. "Explicit targeting" is precies wat onze per-app-lus al doet (`--id … --exact`), dus deze pakketten zijn wel degelijk bij te werken.
  - **Nog te volgen:** bij zulke pakketten kan winget de geïnstalleerde versie soms niet betrouwbaar vaststellen, waardoor het elke run opnieuw "bijwerkt" → de app zou dan dagelijks in de melding verschijnen. Een paar runs meekijken in `install.log`.
- **WindowsAppSDK exact gepind (`1.8.260710003`, was `1.8.*`).** Tijdens deze sessie trok een gewone rebuild ongevraagd `1.8.260804001` binnen, die runtime-package `>= 8000.946.1701.0` eist terwijl de machine `8000.921.1539.0` had. Gevolg: **de app startte niet meer** — en omdat het proces sterft vóór de eerste regel eigen code was er geen `LAUNCH`-regel, geen `crash.log`, niets. Alleen een OS-dialoog "Required components of the Windows App Runtime are missing". Dat kostte een half uur zoeken in de verkeerde hoek. Nu gepind, dus builds zijn reproduceerbaar en een SDK-bump is een bewuste actie die je daarna test.

> **Geverifieerd (2026-08-16):**
> - Task via de app aangemaakt → `DisallowStartIfOnBatteries=False`, `StopIfGoingOnBatteries=False`, `StartWhenAvailable=True`, `ExecutionTimeLimit=PT2H`, `RunLevel=Highest`, actie `…\SetupToolbox.exe /autoupdate`, dagelijks 09:00. Dat een task mét die vlag uit wél start op accu was al aangetoond met de `SetupToolbox_ToastTest`-run.
> - `/updatecheck` levert nu **24** entries i.p.v. 22 — `Discord.Discord` staat erbij, geparsed uit de tweede tabel.
> - App start weer normaal na de SDK-pin.
>
> De task is **niet** echt gedraaid: dat zou 24 apps daadwerkelijk bijwerken. Let op dat een task die je vanuit de dev-build aanmaakt naar `bin\Debug\…` wijst — voor dagelijks gebruik opnieuw aanmaken vanuit de geïnstalleerde app.

### v1.0.13 — Toast-notificaties rond de auto-update

User-wens: Windows-toasts rechtsonder rond de geplande `winget upgrade`-run — bij inschakelen, tijdens het zoeken, en mét de namen van de bijgewerkte apps.

**Waarom er werk nodig was.** De toast-infra stond er al (`Microsoft.Toolkit.Uwp.Notifications` 7.x), maar er was **één** toast, alleen aan het eind, generiek: *"All apps have been updated."* De oorzaak zat een laag dieper: `UpdateAllAppsAsync()` draaide `winget upgrade --all --silent` en **gooide de output weg** (`var (exitCode, _, _)`) → alleen een bool. Daardoor waren app-namen onbekend en was "niets te updaten" niet te onderscheiden van "5 apps bijgewerkt".

**Twee-fasen auto-update (`WingetService`).** `upgrade --all` is vervangen door:
1. `GetUpgradableAppsAsync()` — `winget upgrade` als list-only. Hergebruikt `ParseListOutput` (die de `Available`-kolom al kende, want de upgrade-tabel heeft exact dezelfde kolommen als `winget list`) met `ParseSimpleIds` als locale-fallback. Non-zero exit + lege output → throw, zodat een kapotte bron als fout gemeld wordt i.p.v. als "alles up-to-date".
2. Per app `upgrade --id <id> --exact --silent`, met `--source` uit de parse-entry (zonder pin raakt winget óók de msstore-bron — zie v1.0.1). Exit 0 → bijgewerkt; `IsAlreadyInstalled` → stil overslaan; anders `FriendlyError` als reden. Per-app try/catch zodat één kapotte app de run niet stopt.

Nieuwe records `AutoUpdateResult(Updated, Failed, ListError)` + `AutoUpdateFailure(Name, Reason)`, met `HasListError` / `NothingToDo` zodat de toast-tekst geen eigen logica hoeft.

**Eén zelf-overschrijvende toast (`ToastHelper`, herschreven).** Alle toasts gaan door één private `Show(tag, build)` die de setting-gate, de `Tag`/`Group`, de klik-actie en de logging op één plek regelt. Gelijke Tag + Group ⇒ Windows **vervangt** de vorige toast, dus "Zoeken naar updates…" wórdt het resultaat i.p.v. een tweede melding — bij een dagelijkse run blijft het Action Center schoon. Teksten: `Zoeken naar updates…` → `Alles is up-to-date.` / `X, Y en Z zijn bijgewerkt.` Nederlandse opsomming via `JoinDutch`; boven 5 namen `… en N andere` zodat een run van 30 apps de toast niet onleesbaar maakt. Bij één mislukking de reden erbij, bij meerdere alleen de namen (reden past dan niet meer).

**Overige onderdelen:**
- **N1** — `ScheduleDialog` toont na `CreateTaskResult.Success` een toast met dezelfde schedule-omschrijving als de InfoBar.
- **Setting** `UpdateNotificationsEnabled` (default aan) — toggle in Settings → Auto-updates, binnen de bestaande card (geen `Grid.Row`-hernummering nodig). `ToastHelper` leest 'm voor élke toast.
- **Klik opent de app — via een eigen URI-protocol, niet via de COM-activator.** De toolkit registreert netjes een COM-activator (AUMID + `CustomActivator` + `LocalServer32`, alle drie geverifieerd correct in het register), maar op een **unpackaged** app routeert Windows de klik daar niet naartoe zolang er geen Start Menu-snelkoppeling met de `System.AppUserModel.ToastActivatorCLSID`-eigenschap bestaat. Gemeten: klik liet de toast verdwijnen maar startte de exe niet — géén `LAUNCH`-regel in `install.log`, geen DCOM-fout, geen crash. De enige aanwezige snelkoppeling was die van de Inno Setup-installer, zónder die eigenschap; dus ook de release-build zou hierop stukgelopen zijn.
  - **Fix:** nieuwe `Helpers/ToastProtocol` registreert `setuptoolbox:` in HKCU (`shell\open\command` → `"<exe>" "%1"`, idempotent, geen admin) en de toast krijgt `SetProtocolActivation(new Uri("setuptoolbox:open"))`. Windows doet dan een gewone ShellExecute — een normale proces-start, geen COM. Werkt identiek in dev- en release-build en heeft geen snelkoppeling nodig.
  - `App.OnLaunched` vangt de `setuptoolbox:`-tak af en haalt via `TryFocusExistingInstance()` (`ShowWindow` + `SetForegroundWindow`) een al draaiend venster naar voren i.p.v. een tweede te openen. Bewust een gerichte check alléén voor deze tak: de app doet elders géén AppInstance-redirectie, omdat de ge-eleveerde install-runner zichzelf juist als tweede proces moet kunnen starten. Processen zonder `MainWindowHandle` (de headless takken) worden overgeslagen.
  - De `ToastNotificationManagerCompat.OnActivated`-subscriptie blijft staan: die houdt de toolkit-registratie (AUMID, weergavenaam, icoon) in stand. Hij vuurt in de praktijk niet meer.
- **Diagnostiek** — `App.OnLaunched` logt bij élke start een `LAUNCH args=[…]`-regel. Dat was precies wat de toast-activatie-diagnose besliste (start Windows de exe überhaupt?) en blijft nuttig voor de headless takken.
- **`/updatecheck` debug-switch** — draait alléén de inventarisatie-fase en logt wat er bijgewerkt zóu worden. Installeert niets; bedoeld om parser + toast-tekst te verifiëren zonder een echte run van tientallen apps los te laten. `/toasttest` toont nu de volledige twee-staps flow met nepdata.

### v1.0.7 — Install-UX vervolg na v1.0.6 VM-test: abort, elevation-refused auto-retry, NumberBox-prompt, msstore Store-app fallback

Vier gebundelde install-UX-verbeteringen uit de v1.0.6 VM-test (v1.0.6 fix werkt: één UAC voor de batch, geen *"can't run on your PC"* meer). Maar de test legde meerdere pijnpunten bloot — alle in deze patch opgelost.

- **A. Abort-knop** voor een lopende batch. Battle.net liet zien hoe pijnlijk de oude situatie was: één hangende winget-installer maakte de hele app onbruikbaar (alleen taakbeheer als uitweg). Nu: tijdens een batch is de Primary-knop **"Annuleren"** (een nieuwe `PrimaryButtonClick`-handler cancelt een `CancellationTokenSource` en houdt de dialog open via `args.Cancel = true`). De CTS propageert door de hele install-stack:
  - In-process flow → nieuwe `CancellationToken` parameter in `WingetService.InstallAppsAsync` / `InstallOneInBatchAsync` / `InstallAppAsync` / `RunWingetCommandAsync`. `RunWingetCommandAsync` registreert `ct.Register(() => process.Kill(entireProcessTree: true))` zodat niet alleen `winget.exe` maar ook **de installer die winget zelf spawnt** (MSI / setup.exe) sterft. Resterende apps die nog niet aan de semaphore zaten krijgen `CancelledMessage` ("Geannuleerd") en gaan via `InstallPhase.Failed` door de progress-stroom. De v1.0.4-retry-knop ziet ze daarna als gewone failures en kan ze opnieuw oppakken.
  - Elevated runner → medium-IL parent kan een high-IL kind niet betrouwbaar killen (integriteits-check). Oplossing: **`cancel.flag` file-IPC**. Parent schrijft `cancel.flag` in de workDir bij CTS-cancel; kind pollt 'm elke 500ms (`Task.Run` monitor naast de install-task) en cancelt z'n eigen `innerCts` → propageert weer door de install-stack in het kind. Self-cancel via flag = de schone weg.
  - UI: na cancel wordt de Primary "Annuleren..." (disabled) tot de wind-down klaar is, daarna normaal "Sluiten".
- **B. `0x8A150056` auto-retry onge-eleveerd.** Sommige installers (Spotify is de bekendste; exit code `0x8A150056` *"The installer cannot be run from an administrator context"*) **weigeren** elevated te draaien — in v1.0.4 werkten ze omdat onze parent unelevated was; vanaf v1.0.5's elevated runner faalden ze. Fix is exit-code-based, geen hardcoded blocklist:
  - `WingetService.IsAdminContextRefused(exitCode, output)` checkt `0x8A150056` taal-onafhankelijk + de Engelstalige tekst als vangnet.
  - Nieuwe sentinel `WingetService.RequiresUnelevatedMessage` — `InstallAppAsync` returnt deze i.p.v. `FriendlyError` (dus de log toont `REJECT-ADMIN … needs unelevated retry` i.p.v. een misleidende "install mislukt").
  - `InstallDialog.RunWingetInstallsAsync` ná de elevated batch: scant `_items` op deze sentinel-message → reset die items → roept `App.Winget.InstallAppsAsync` *in-process* (onge-eleveerd) nog een keer aan met alleen die apps. `_completedCount` wordt vooraf gedecrementeerd zodat de `OnProgress`-teller niet dubbelt. Volledig automatisch + silent (user's keuze in de Q&A); brief flicker Failed → Pending → Installing → Success.
- **C. NumberBox-prompt voor parallelisme.** De one-time prompt vroeg eerder Yes (=2) / No (=1) — user wilde direct 1-4 kunnen kiezen. Vervangen door een ContentDialog met `NumberBox` (Min=1, Max=4, default 2, Compact spin-buttons + integer-formatter zodat NL-locale geen "2,0" laat zien). Cancel via dialog-X bewaart niets (komt volgende keer terug); OK slaat `MaxParallelInstalls` op en zet `ParallelInstallsAsked=true`.
- **D. msstore cert-error → Store-app fallback.** VM-diagnose bracht aan het licht: `winget source update` werkt (CDN-laag oké) maar `winget install` voor msstore-products faalt op een **aparte cert-pin in de winget→Store-IPC** (`0x8A15005E` "server certificate did not match"). Reset van de source helpt vaak niet — komt ook voor op stale Windows-installs / corporate proxies / AV-interferentie buiten VMs. **Fix:** in `WingetService.InstallAppAsync` detecteren we `0x8A15005E` specifiek voor `source=="msstore"` apps + openen via `Process.Start` het `ms-windows-store://pdp/?productid=<id>` URI → Store-app gaat direct naar de productpagina, user klikt 1× op "Halen". Updates lopen daarna gewoon mee: via winget op gezonde machines, of via de Store-app's eigen auto-update op de achtergrond. Nieuwe sentinel `WingetService.MsStoreOpenedMessage`, nieuwe `InstallPhase.OpenedInStore` + `InstallItemState.OpenedInStore` (gele caution-badge "Geopend in Store", patroon mirror van de bestaande manual-download flow). `HadSuccessfulInstall` telt deze als success. Bij Stores die ook stuk zijn faalt `Process.Start` netjes → terugval op de bestaande `FriendlyError`.

> **VM-test v1.0.7 bevestigd (2026-06-12):**
> - ✅ **A (Abort):** `RUNNER CANCEL flag written → CHILD CANCEL flag detected → CANCEL Blizzard.BattleNet` — perfect.
> - ⚠️ **B (Spotify B-retry):** Mechanisme werkt correct (in-process retry na elevated batch, geen nieuwe RUNNER START). Twee resterende issues → v1.0.8 F1: (1) geen UI-feedback tijdens retry, (2) Spotify faalt in de unelevated retry met `0x8A150011` (hash-mismatch — winget-catalogus-bug, lost zich vanzelf op).
> - ✅ **C (NumberBox):** Werkt.
> - ✅ **D (msstore):** Werkt op frisse Windows-install — Apple Music (`9PFHDD62MXS1`) en `XP8JNQFBQH6PVF` allebei OK via elevated runner.

### v1.0.12 — ContentDialog-gate (post-install prompt-crash) + MSI-serialisatie

VM-test van v1.0.11 (install-batch van ~50 apps over meerdere categorieën). Twee problemen uit `crash.log` + `install.log`:

- **ContentDialog-crash (crash.log 09:13:10).** `ScheduleAutoUpdatePrompt.MaybeShowAsync` (de post-install "Schedule auto-updates?"-prompt) gooide `COMException: Only a single ContentDialog can be open at any time`. WinUI 3 staat per thread maar één open ContentDialog toe — en de race treedt óók op bij twee dialogs vlák ná elkaar: zodra de `await InstallDialog.ShowAsync()` voltooit ben je terug in de continuation, maar WinUI heeft de InstallDialog-popup dan nog niet uit de visual tree gehaald, dus de direct-volgende `ScheduleAutoUpdatePrompt.ShowAsync()` botst erop. Het v1.0.11-vangnet ving de crash op (app overleeft), maar de prompt verscheen alsnog niet.
  - **Fix — centrale `Helpers.DialogService.ShowAsync(dialog)`-gate.** Serialiseert alle dialogs die er doorheen gaan (`SemaphoreSlim`, één tegelijk) én vangt de teardown-race op: bij de COMException krijgt de UI-thread een low-priority tick om de vorige popup op te ruimen, daarna retry (max 10×). De hele install-knop-flow loopt nu via de gate: `ParallelInstallsPrompt` → `LocationPrompt` → `InstallDialog` → `ScheduleAutoUpdatePrompt` (+ de `ScheduleDialog` daarbinnen), in zowel `AppsPage` als `CategoryDetailPage`.
- **MSI-lock-botsingen (install.log).** VirtualBox, PostgreSQL en MySQL Workbench (alle drie MSI met VCRedist-dependency) draaiden parallel (`parallel=4`) in de ge-eleveerde batch → botsten op de globale Windows Installer-mutex (`Waiting for another install/uninstall to complete...`) → faalden met `0x8A150006` / `0x8A150102`. Een tweede poging hielp deels (MySQL Workbench daarna `SKIP already installed`), maar VirtualBox + PostgreSQL bleven falen.
  - **Fix — proactieve serialisatie via apps.json-vlag** (zelfde patroon als `requiresUnelevated` / `requiresLocation`): nieuwe `App.SerializeInstall` (`serializeInstall: true`). `WingetService` heeft een process-wide `_serialInstallGate` (`SemaphoreSlim(1,1)`); serialize-apps moeten die gate BINNEN de parallelisme-semaphore nemen → ze draaien nooit gelijktijdig met elkaar, terwijl losse EXE-installers gewoon parallel doorlopen. De vlag propageert door de ge-eleveerde runner (job → kind). Getagd: `Oracle.VirtualBox`, `PostgreSQL.PostgreSQL.17`, `Oracle.MySQLWorkbench`. Eén UAC blijft behouden (geen tweede prompt zoals een retry-achteraf zou kosten).

> **Niet (door de fix) opgelost — losse installer-problemen, geen SetupToolbox-bug:** RoboForm (`0x8A150011` enterprise-MSI), NordVPN (`0x8A150110`), Microsoft.Office (`0x8A150006`, winget-Office is notoir), Proton Drive (`0x8A150102`), Foxit (`0x8A150006`). Firefox/Vivaldi faalden eerder op `0x80072F05` (transiente netwerk/SSL) en slaagden bij herpoging — daar is de bestaande retry-knop voor.

> **Lokaal getest (2026-08-12):** ContentDialog-gate (install → schedule-prompt keten, geen crash meer) en MSI-serialisatie (VirtualBox + PostgreSQL + MySQL Workbench in 1 batch, 1 UAC, geen lock-botsingen) — beide bevestigd werkend.

### v1.0.11 — Globale crash-vangnet + crash-safe progress-callbacks

VM-test van v1.0.10: Battle.net (location-popup) en Spotify (onge-eleveerd) werken. Maar **TreeSize Free installeren deed de hele app verdwijnen** terwijl de install zélf slaagde (`OK JAMSoftware.TreeSize.Free` in de log). Diagnose via `install.log`: de runner-regel `RUNNER EXIT` ontbrak voor die run terwijl elke andere run 'm wél had → de UI-parent stierf vóór z'n `finally`-block.

- **Root cause.** `ElevatedInstallRunner.DrainProgress` rapporteert voortgang via `Progress<T>.Report`, die `OnProgress` **async post** op de UI-thread. Een exception in die callback (transiente WinUI binding-/layout-fout bij snelle property-updates) propageert NIET terug naar de drain-loop maar landt als onbehandelde exception op de UI-message-pump. De app had **geen `UnhandledException`-handler**, dus het hele proces werd stilzwijgend gekilld — geen window, geen log, install al geslaagd.
- **Fix 1 — globale vangnet.** `App` registreert nu `UnhandledException` → logt volledige stack naar `crash.log` + een regel naar `install.log`, en zet `e.Handled = true` zodat herstelbare managed exceptions de app niet meer nekken.
- **Fix 2 — crash-safe progress.** `InstallDialog.ApplyProgress` is opgesplitst: de eigenlijke UI-update zit nu in `ApplyProgressCore` met een `try/catch` eromheen die naar `install.log` logt (`APPLYPROGRESS-EX <id> phase=…`). Een UI-hik tijdens een geslaagde install kan die install niet meer omverhalen, en we krijgen de exacte fase + exception-type in de log.

> Bewust defensief i.p.v. één specifieke regel patchen: de crash was niet-deterministisch (TreeSize wél, TeamViewer niet, beide Success). De handler + try/catch maken de progress-pijplijn robuust én leveren bij een volgende hit een concrete stack op.

### v1.0.10 — Spotify proactief onge-eleveerd + Battle.net location-popup (proactieve install-lanes)

VM-test van v1.0.9 onthulde dat de detectie-achteraf aanpak (B-retry + E1) **niet betrouwbaar triggert**. Root cause: beide leunden op het parsen van winget's fout-uitkomst ná een gefaalde install in de ge-eleveerde batch, en die uitkomst is taal-/manifest-afhankelijk.

- **Spotify retry'de niet.** De B-retry vuurt alleen op de sentinel `RequiresUnelevatedMessage`, die enkel ontstaat bij exit `0x8A150056` ("installer prohibits elevation"). Spotify's winget-manifest zet die elevation-flag niet betrouwbaar → in admin-context faalt de installer met een **generieke/hash-fout** i.p.v. een nette prohibits-elevation-code. De sentinel ontstond dus nooit, `finalMessages` kreeg een gewone failure-message, en de retry-scan vond niets. De v1.0.9 race-fix was correct maar irrelevant — de sentinel was er nooit om te vinden.
- **Location-popup verscheen niet.** Zelfde fragiliteit: `IsLocationRequired` parst winget's fouttekst ná een gefaalde batch-install. Bovendien was Battle.net in v1.0.9 een `downloadUrl` geworden, dus die raakte winget niet eens meer.

**Fix — proactieve install-lanes o.b.v. apps.json-vlaggen** (geen fout-detectie meer nodig):
- Twee nieuwe `App`-properties: `RequiresUnelevated` (`requiresUnelevated`) en `RequiresLocation` (`requiresLocation`).
- `apps.json`: Spotify → `requiresUnelevated: true`; Battle.net → `downloadUrl` verwijderd, `requiresLocation: true`.
- `InstallDialog.RunWingetInstallsAsync` splitst nu in **vier lanes**: `community` (ge-eleveerde batch, één UAC) → `unelevated` (Spotify, proactief in-process onge-eleveerd) → `locationRequired` (Battle.net, install met `--location`) → `msstore` (in-process). Spotify gaat dus **nooit** meer door de batch; bij een Spotify-only batch is er zelfs geen UAC.
- **Crash-fix (Battle.net):** InstallDialog is zélf een `ContentDialog`, en WinUI 3 staat maar één open ContentDialog tegelijk toe — een pad-dialog binnen InstallDialog tonen gooide *"Only a single ContentDialog can be open at any time"* → app-crash zodra je Battle.net selecteerde. Opgelost door het pad **vooraf** te vragen: nieuwe helper `Helpers.LocationPrompt.CollectAsync(apps, xamlRoot)` draait in de calling page (AppsPage + CategoryDetailPage), vóór `new InstallDialog`, en geeft een `Dictionary<wingetId,pad>` door aan de constructor. InstallDialog installeert location-apps met dat vooraf gekozen pad via `InstallWithLocationAsync` (geen dialog meer); geen pad gekozen → app als `Skipped`.
- De B-retry op `RequiresUnelevatedMessage` blijft als **vangnet** (geen dialog, dus veilig). De E1-detectie-achteraf (location-sentinel na de batch) is **verwijderd**: een pad-dialog mid-batch kan niet zonder crash, dus `requiresLocation` in apps.json is de enige bron van waarheid voor locatie-apps.

### v1.0.9 — B-retry race-conditie fix + Battle.net downloadUrl (E3)

- **B-retry race-conditie fix** — `_items` wordt bijgewerkt via `DispatcherQueue.TryEnqueue` (async); bij een snelle batch (Spotify-only, ~1s) kon de scan al lopen vóór de TryEnqueue-callbacks verwerkt waren → geen retry. Fix: `ElevatedInstallRunner.InstallAppsElevatedAsync` retourneert nu naast de `ElevatedRunResult` ook een `Dictionary<string, string> finalMessages` (wingetId → definitieve message), gesynchroniseerd gevuld in `DrainProgress` vóór de dispatch. `InstallDialog` scant voortaan op `finalMessages` i.p.v. `_items` voor zowel de B-retry (REJECT-ADMIN) als de E1-location retry. Zelfde fix voor E1 locatie-scan meegenomen.
- **E3. Battle.net → `downloadUrl`** — `"downloadUrl": "https://www.blizzard.com/en-us/apps/battle.net/desktop"` toegevoegd aan `apps.json`. Battle.net's winget-installer respecteert `--silent` niet en hangt op interactieve dialogs; de bestaande manual-download flow (browser opent de downloadpagina) is de betere UX. Geen nieuwe code — `wingetId` behouden voor het icoon-pad.

### v1.0.8 — B-retry UI-feedback (F1) + install-location dialog (E1)

- **F1. B-retry UI-feedback** — Na `Reset()` en vóór de unelevated `InstallAppsAsync`-call wordt `item.State = Installing` + `item.Message = "Herprobeert zonder admin..."` gezet. User ziet nu een duidelijke "Herprobeert..." melding i.p.v. een stille `Failed → Pending → Installing` flicker. (~5 regels in `InstallDialog.xaml.cs`)
- **E1. Install-location dialog** — Nieuwe sentinel `WingetService.RequiresLocationMessage` + `IsLocationRequired`-detectie (tekst-gebaseerd). Wanneer de elevated batch een app als "locatie vereist" teruggeeft, toont `InstallDialog` een `ContentDialog` met `TextBox` (default `%LOCALAPPDATA%\Programs\<AppName>`). Na bevestiging herstart de install in-process met `--location "<gekozen pad>"` via de nieuwe optionele `location`-param op `InstallAppAsync`. Overslaan laat de app als Failed staan. De detectie-sentinel voorkomt een infinite loop (alleen gefired als `location == null`). (~55 regels)

### v1.0.6 — Hotfix: `app.manifest` asInvoker + runner-diagnostiek

VM-test van v1.0.5 onthulde twee gerelateerde launch-failures, beide met dezelfde root cause: het `app.manifest` had **geen expliciete `requestedExecutionLevel`** declaratie. Combinatie met onze exe-naam (`SetupToolbox.exe` — begint met *"Setup"*) raakte Windows' UAC **Installer Detection** heuristiek → de exe werd stilzwijgend als "vereist admin" gestempeld.

- **Bug #1 — Inno Setup's auto-launch faalt** met `CreateProcess failed; code 740` ("The requested operation requires elevation"): Inno Setup draait als asInvoker en kan een impliciet-elevation-gemarkeerde exe niet via CreateProcess starten. **Verklaart waarom de installer's "launch app"-stap brak op de VM.**
- **Bug #2 — "This app can't run on your PC"** bij de v1.0.5 elevated install-runner: onze `ShellExecute` met `Verb="runas"` triggert UAC netjes, maar Windows probeert daarna een al-impliciet-elevation-gemarkeerde exe nóg eens te eleveren → bootstrapper-context raakt corrupt → kind crasht direct. **Verklaart waarom de v1.0.5 batch-UAC niet zichtbaar was: het kind faalde meteen na de UAC-prompt; mijn parent-code zag een proces dat "compleet" was met non-zero exit en runde geen fallback (alle apps bleven Pending).**

**Fix** in `app.manifest` (één canonical XML-blok):
```xml
<trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
  <security>
    <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
      <requestedExecutionLevel level="asInvoker" uiAccess="false" />
    </requestedPrivileges>
  </security>
</trustInfo>
```
`asInvoker` schakelt de Installer Detection heuristiek uit en zegt expliciet "draai op het level van wie me start". Resultaat: (1) Inno Setup kan de exe gewoon CreateProcessen na install, (2) onze v1.0.5 `ShellExecute runas` triggert nu één nette UAC + de elevated kind-exe start zonder bootstrapper-conflict. App blijft per-user, geen UAC bij normale launch — alleen wanneer v1.0.5 expliciet om elevatie vraagt voor de install-batch.

**Runner-diagnostiek** in `install.log` om VM-debug te versnellen: parent logt `RUNNER START exe=… apps=N parallel=K`, daarna `RUNNER EXIT pid=X exitCode=0x… elapsed=Ns progressLines=N`. Kind logt `CHILD START job=… pid=X` en `CHILD EXIT ok …` (of exception). Nu kun je in één blik in de log zien: kreeg de parent het kind ge-eleveerd? Heeft het kind überhaupt z'n job gelezen? Kwamen er progress-regels door?

> **Niet meegenomen, los oppakken:** de `0x8A150006` / `0x80004004` install-failures in de v1.0.5-VM-log zijn een aparte categorie (installer-conflicts onder elevation + parallel-botsingen + msstore-bron stuk op VM + één `0x8A150105` DISK_FULL voor Proton Drive). Eerst v1.0.6 verifiëren — pas dán zinvol om hier dieper in te duiken.

### v1.0.5 — UAC eenmalig per batch (ge-eleveerde install-runner)

De architecturaal grote uit de v1.0.4-roadmap: één UAC-prompt voor de héle install-batch i.p.v. een prompt per machine-scope app.

- **Waarom een apart proces.** UAC triggeren vereist `Process.Start` met `Verb="runas"` + `UseShellExecute=true`, en dát verbiedt stdout-redirect — we kunnen de output van een elevated winget dus niet lezen zoals `WingetService` in-process doet. Oplossing: we relaunchen onze **eigen exe** ge-eleveerd in een headless `--install-runner <jobPath>`-modus (nieuwe tak in `App.OnLaunched`, vóór window-creatie, net als `/autoupdate`). Geen single-instance/AppInstance-redirectie in de app, dus de tweede elevated kopie draait zelfstandig.
- **IPC via JSONL temp-bestand (geen named pipe).** Ouder schrijft `job.json` (`{ MaxParallelism, Apps:[{Id,Name,Source}] }`) naar `%TEMP%\SetupToolbox\runner-<guid>\`, start het elevated kind, en **tailt** `progress.jsonl` (~elke 250ms, `FileShare.ReadWrite`, splitst op `\n` en slaat de onvolledige trailing-regel over). Kind draait de bestaande `WingetService.InstallAppsAsync` met een thread-safe (lock'd) `IProgress`-writer die elke `InstallProgress` als JSON-regel append't. Ouder reconstrueert `InstallProgress` via een `wingetId→App`-lookup → exact dezelfde progress-stroom, **InstallDialog merkt geen verschil**. Bewust file-polling i.p.v. pipe: crash-robuust (partiële resultaten blijven leesbaar), geen pipe-ACL-gedoe tussen medium- en high-integrity processen.
- **Drie install-lanes.** Manual-download (browser) → ouder, geen elevatie (al zo). **Community-winget** → ge-eleveerde runner (één UAC). **msstore** → blijft onge-eleveerd in-process (Store-backend werkt niet betrouwbaar elevated, prompt toch zelden). `InstallDialog.RunWingetInstallsAsync` splitst nu op `Source`.
- **Fallback bij geweigerde UAC.** `Win32Exception` (1223 / elevatie onmogelijk) → `ElevatedRunResult.UacDeclined` → caller valt terug op de in-process per-app flow zodat de user niet vastzit (user-scope apps installeren dan alsnog; machine-scope her-prompten per app). Kind-crash → partiële progress blijft, rest = failed, de v1.0.4-retry-knop werkt nog (en gaat opnieuw via één UAC).

> **Te verifiëren op de VM (echte UAC):** (1) één UAC-prompt voor een gemengde batch, (2) de ge-eleveerde **eigen exe** boot headless OK (WindowsAppSDK-bootstrapper onder elevatie is het voornaamste risico), (3) progress streamt vloeiend terug, (4) UAC-weigeren valt netjes terug op de per-app flow, (5) msstore-apps installeren nog onge-eleveerd. Lokaal alleen de opstart + dialog-flow getest (geen echte install getriggerd om niet ongevraagd software te installeren).

### v1.0.4 — Install-UX: "Al geïnstalleerd"-status, instelbaar parallelisme, retry-knop

Drie gebundelde install-UX-verbeteringen na de VM-test:

- **"Al geïnstalleerd" i.p.v. "Installed".** Reeds-aanwezige apps (bv. Edge, preinstalled) toonden "Installed" (groen) terwijl er niets geïnstalleerd werd. Nieuwe `InstallPhase.AlreadyInstalled` + `InstallItemState.AlreadyInstalled` via gedeelde sentinel `WingetService.AlreadyInstalledMessage` (geen magic string). Per-app label nu **"Al geïnstalleerd"** (groen vinkje) en de samenvatting splitst `X installed, Y already installed, Z failed`.
- **Parallelisme instelbaar (1-4).** Bool `ParallelInstalls` → int **`MaxParallelInstalls`** (default 2). Settings toont nu een `NumberBox` (1-4) i.p.v. een toggle; de one-time prompt zet 2 (Yes) of 1 (No). `InstallDialog` leest de int; `InstallAppsAsync` capt sowieso op 4. MSI-installers serialiseren op de globale Windows-installer-mutex, dus hoger helpt vooral losse EXE-installers.
- **"Retry failed"-knop.** Na een batch verschijnt — alleen bij gefaalde winget-apps — een Secondary-knop die alléén de gefaalde apps reset (`InstallItem.Reset()`) + opnieuw draait, zonder de dialog te sluiten. Run + samenvatting zijn nu herbruikbaar (`RunWingetInstallsAsync` / `UpdateSummaryAndButtons`) en cumulatief afgeleid uit de item-lijst. Handig bij tijdelijke fouten (UAC-cancel, VM-pauze, hash, parallel-botsing).

> **✅ Opgepakt in v1.0.5:** UAC eenmalig per batch — ge-eleveerde install-runner (`--install-runner` headless via `runas`, één UAC) + JSONL-IPC om progress terug te streamen. Zie de v1.0.5-sectie hierboven.

### v1.0.3 — Icon-regressie fix + accurate install-foutmeldingen

Vervolg op de volledige VM-test van v1.0.2 (alle apps geïnstalleerd). **Werkt bevestigd:** Edge → `SKIP` (exit `0x8A15002B`), Vivaldi.Vivaldi → OK, ~30 apps OK.

- **Icon-regressie (v1.0.2 neveneffect):** door de winget-ID-wijzigingen verdwenen de Vivaldi- en NordVPN-iconen. Het icoon-pad is `ms-appx:///Icons/<wingetId>.png`, dus een ID-wijziging vereist een hernoemd icoon-bestand. `data/icons/VivaldiTechnologies-Vivaldi.png` → `Vivaldi-Vivaldi.png` en `NordVPN-NordVPN.png` → `NordSecurity-NordVPN.png` (via `git mv`). *Let op voor toekomstige ID-wijzigingen: icoon mee-hernoemen.*
- **Accurate install-foutmeldingen (`FriendlyError`):** mapt nu op de échte winget exit-codes i.p.v. een misleidende "niet compatibel". Tijdelijke fouten krijgen "Probeer opnieuw": `0x800704C7`/`0x8A15010C` (UAC niet geaccepteerd), `0x8A150006` (installer-fout), `0x8A150011` (hash-mismatch), `0x8A150101/0103/0111` (in gebruik), `0x8A150102` (al bezig), `0x8A150105` (schijf vol), `0x8A150001` (interne fout), `0x8A15005E` (msstore-certfout → `winget source reset --force`).

> **Uit de VM-test, géén code-bugs:** de 7 msstore-apps (Norton/Bitdefender/iCloud/ChatGPT/Perplexity/WhatsApp/Apple Music) faalden met `0x8A15005E` door de **kapotte msstore-bron op de VM** → fix op de VM met `winget source reset --force`. Een cluster fouten na een **VM-pauze van ~2,5u** (Dropbox/Notion/Steam/EA/Adobe/Foxit/Signal/Discord/Spotify/VLC/OBS/…) kwam door onderbroken installs + UAC-timeouts + parallel-botsingen, niet door de catalogus. Alle winget-IDs resolveden (geen "no package found" meer).

> **Nog open (ontwerpkeuze, v1.0.4):** UAC-aanpak (per-app prompt vs. één keer admin per batch), parallelisme (nu 2; MSI's serialiseren toch op de globale installer-mutex), en een "retry mislukte apps"-knop.

### v1.0.2 — "Already installed" error-based + catalogus-IDs geverifieerd

Vervolg op de v1.0.1-test (browsers op verse install): ná de `--source winget`-fix installeerde alles **behalve Edge en Vivaldi**.

- **Edge "already installed" — niet gehardcode, op winget's eigen response.** Edge is voorgeïnstalleerd op Windows; `winget install Microsoft.Edge` vindt niets nieuwers en geeft een "zit er al"-code. `InstallAppAsync` herkent dit nu via `IsAlreadyInstalled(exitCode, output)` → behandelt als succes/overslaan ("Al geïnstalleerd"), geen rode failure. **Taal-onafhankelijk** op de gedocumenteerde winget exit codes `0x8A150061` (PACKAGE_ALREADY_INSTALLED), `0x8A15010D` (INSTALL_ALREADY_INSTALLED) en `0x8A15002B` (UPDATE_NOT_APPLICABLE = al op nieuwste versie), met de Engelstalige output-tekst `"already installed"` als extra vangnet. Bewust géén hardcoded app-namen — elke reeds-aanwezige app valt hieronder. De `FAIL`-logregel bevat al `exit=0x…` + ruwe stdout/stderr zodat onverwachte gevallen diagnoseerbaar blijven.
- **Catalogus-IDs geverifieerd tegen de échte winget-bron** (`winget show --exact`, niet gegokt). 6 kapotte IDs gevonden + opgelost:
  - **Vivaldi**: `VivaldiTechnologies.Vivaldi` → **`Vivaldi.Vivaldi`** (verklaart de tweede test-failure).
  - **NordVPN**: `NordVPN.NordVPN` → **`NordSecurity.NordVPN`**.
  - **DaVinci Resolve**: staat niet in winget → **`downloadUrl`** naar blackmagicdesign.com (opent in browser, zoals VMware/ON1). `wingetId` behouden voor het icoon-pad.
  - **Proton Calendar / Wallet / Docs / Sheets**: geen winget-pakket én geen Windows-installer (web/mobiel-only) → **`downloadUrl`** naar hun `proton.me`-pagina (browser). **Docs + Sheets stonden dubbel** (Office Suites + Proton Suite) → duplicaten uit Office Suites verwijderd, één entry in Proton Suite. Catalogus nu 110 entries (was 112).
  - *Niet aangeraakt:* `Proton VPN` / `Proton Drive` staan bewust 2× (echte winget-apps, gekruist in hun functie-categorie + Proton Suite).

> **Te (her)verifiëren op de schone VM:** browser-test opnieuw draaien — Edge moet nu "Al geïnstalleerd" tonen i.p.v. rood; Vivaldi/NordVPN/DaVinci/Proton-web-apps controleren. `install.log` (Settings → Diagnostiek → Open logmap) bevestigt de exacte Edge-exit-code in de `SKIP`-regel.

### v1.0.1 — Fix: winget-installs falen op een schone install (msstore-bronfout)

**Bug** (gevonden bij test op verse Windows-install): álle winget-installs faalden identiek. **Oorzaak:** het install-commando gaf geen `--source` mee → winget doorzocht óók de `msstore`-bron, die op verse installs een certificaat-fout geeft (`0x8a15005e` "server certificate did not match any of the expected values"). Winget weigert dan de winget-source-match automatisch te kiezen en stopt met *"specify --source"*. Dus een bron-laag-probleem, niet per app.

- **Fix:** winget-apps pinnen nu op **`--source winget`** in `InstallAppAsync` → de `msstore`-bron wordt niet meer aangeraakt. (msstore-apps behouden hun eigen install-pad.)
- **Foutlogging-toggle** (Settings → **Diagnostiek**, `ErrorLoggingEnabled`, default aan) + knop **"Open logmap"**: install- + scan-fouten (commando + exit-code + ruwe winget stdout/stderr) → `%LocalAppData%\SetupToolbox\logs\install.log`. De `Diagnostics`-gate leest nu deze runtime-toggle (was een compile-time const) en schrijft naar een eigen logmap i.p.v. `%TEMP%`.
- **Betere UI-foutmeldingen:** echte winget exit-code (`0x…`) + verwijzing naar de log; specifieke melding bij de bron-/certificaatfout (hint: `winget source reset --force`).

### v1.0.0 — Rebrand naar "Setup Toolbox" + self-update + proprietary licentie

**Rebrand** WingetAppDeployer → **Setup Toolbox** (oude naam te lang, botste met WingetUI/UniGetUI, dekte de lading niet meer: install + debloat + tweaks + deep clean). Volledige rename, ook intern: namespace `WingetAppDeployer_WinUI` → `SetupToolbox`, assembly/exe → `SetupToolbox.exe`, projectmap `src/SetupToolbox/`, solution `SetupToolbox.sln`, data-map `%LocalAppData%\SetupToolbox`, install-map `…\Programs\SetupToolbox`, installer-asset `SetupToolbox-v{ver}.exe`, repo-constante → `MisterDuckles/SetupToolbox`. Display-naam "Setup Toolbox" (met spatie), interne identifiers zonder spatie. AppId-GUID behouden (stabiele installer-identiteit). 87 bestanden via scripted literal find-replace (langste varianten eerst, BOM-preserving) + `git mv` voor map/csproj/sln/iss; `winget`-CLI-refs (WingetService etc.) ongemoeid.

**Self-update** (`GitHubService`) — startup-check + handmatige "Check for updates now" (Settings → App-updates); vergelijkt `AssemblyVersion` met de nieuwste stabiele GitHub-release, filtert op de installer-asset `^SetupToolbox-v…\.exe`. Update-InfoBar in MainWindow → download (met voortgang) → silent installer (`/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`) → Restart Manager sluit + vervangt + herstart de app. Welcome-banner op AppsPage (dismissible via X + setting). Nieuwe settings `CheckForUpdatesOnStartup` + `ShowWelcomeBanner`.

**Distributie-keuze** — repo wordt **publiek** met een **proprietary `LICENSE`** (MIT vervangen): code is in te zien + PR's mogen voorgesteld worden, maar niet kopiëren/hergebruiken/herdistribueren zonder toestemming; de **app/exe** is vrij te downloaden + gebruiken. Git-historie gescand op secrets — clean (`data-source.local.txt` nooit gecommit, 0 token-patterns in 87 commits).

> **Vóór live vereist — allemaal afgerond:** GitHub-repo hernoemen naar `SetupToolbox` + publiek zetten ✅, eerste v1.0.0-release publiceren met `setup.exe` ✅, en de website (`projects.dpvb.nl/setup-toolbox`, React+Tailwind+GSAP) bouwen ✅ + deployen ✅. Self-update werkt live (privé-repo gaf eerder de `404` waardoor we deze hele beslissing namen). De site vraagt nog wel om een opfrisbeurt — zie **v1.2.4** in `## Open`.

### v0.10.0 — Inno Setup installer (per-user) + Tweaks-padding fix

**Inno Setup installer** — naar voren gehaald uit v1.0 omdat het de *enabler* is voor self-update (v0.10.1): een draaiende unpackaged folder-app kan z'n eigen geladen DLL's niet overschrijven, een installer wél (Restart Manager: in-use replace + relaunch).

- `installer/SetupToolbox.iss` pakt de self-contained Release-publish (`win-x64.pubxml`, 631 files / ~267 MB) in tot **`SetupToolbox-Setup-v{versie}.exe`** (~69 MB, lzma2).
- **Per-user install** (`{autopf}` + `PrivilegesRequired=lowest` → `%LocalAppData%\Programs\SetupToolbox`): GEEN UAC bij install én bij toekomstige self-update. Start Menu-entry + uninstaller + optionele desktop-icon-task.
- `CloseApplications=yes` + `RestartApplications=yes` → ondersteunt `/SILENT /VERYSILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS` (wat self-update straks aanroept).
- Vast `AppId` (GUID) zodat een update dezelfde install vervangt. Versie via ISCC `/DAppVersion` uit de csproj (single source of truth), of fallback `GetFileVersion` van de exe.
- `scripts/build-installer.ps1` — one-shot: `dotnet publish` (self-contained, geen single-file) → ISCC-compile. ISCC user-scope geïnstalleerd (`JRSoftware.InnoSetup`). `installer/Output/` is gitignored (setup.exe gaat naar GitHub Releases, niet de repo).
- **Getest**: silent install (geen UAC) naar LocalAppData, geïnstalleerde app start, silent uninstall ruimt map + Start Menu volledig op.

**Tweaks-tab padding fix**: de afstand tussen het "Tweaks"-kopje en de category-cards stond ruimer dan op de Apps-tab. Twee oorzaken weggehaald: (1) de v0.9.20 profiel-banner stond als eerste body-child en kreeg — hoewel `IsOpen=false` — toch 16px `StackPanel`-spacing omdat een gesloten `InfoBar` Visible-met-0-hoogte blijft → nu `Visibility=Collapsed` in normale modus; (2) de `ResultBar` (rij boven de cards) reserveerde z'n 12px onder-marge ook dicht → marge verwijderd. Nu gelijk aan de Apps-tab.

### v0.9.20 — Tweak-profielen (export / import)

**Apps-stijl profiel-bouwer** voor de Tweaks-tab. Vooraf-bedachte presets ("Privacy basics" etc.) bewust GESCHRAPT (user-keuze) — in plaats daarvan stel je zélf een set tweaks samen, slaat 'm op naar een bestand, en past 'm later toe (op deze of een andere PC). De apply/detect-logica in TweakService is ongemoeid; dit is een UI- + IO-laag eromheen.

**Profiel-bouwer (`TweakProfileService`):**
- Export-format `{ version, exportedAt, count, tweaks: [{ id, choice? }] }` — tweak-`Id` + bij multi-choice het optie-**label** (label i.p.v. index zodat herordening het profiel niet corrupt maakt). camelCase + WriteIndented, mirror van `SelectionImportExportService`.
- `ImportAsync` matcht op Id tegen `TweakService.All`; onbekende Id's of verdwenen choice-labels → `SkippedIds` (geteld + gemeld).
- `StageDelta` (static) zet alléén de **delta** in TweakPending: tweaks die al in de gewenste staat staan worden overgeslagen — geen redundante write, geen onnodige UAC.

**Profiel-modus op de Tweaks-tab** (clean slate, los van de normale live-state weergave):
- Een aparte selectie-store `App.ProfileSelection` (tweede `TweakPendingService`-instance, los van `TweakPending` om mode-bleed te voorkomen) + globale `App.ProfileMode` flag.
- `TweakCardFactory.Build(tweak, profileMode)` — clean-slate renderpad: 2-state checkbox altijd initieel UIT (negeert `tweak.State`), multi-choice ComboBox met "— niet in profiel —" sentinel op index 0, status-pill verborgen.
- TweaksPage rendert in profiel-modus een **vlakke checklist** van alle tweaks gegroepeerd per categorie (i.p.v. de tile-grid) — daardoor geen profiel-modus nodig op de detail-pagina. Eigen **banner** + **footer** ("N geselecteerd" · Sluiten · Opslaan profiel · Toepassen). Zoekbalk filtert de checklist. Wegnavigeren annuleert de (niet-opgeslagen) bouw.
- "Toepassen" = `StageDelta` → verlaat profiel-modus → bestaande `TweakApplyRunner` (backup-prompt + 1 UAC + re-detect).

**Settings — nieuwe sectie "Tweak-profielen"** (naast de app-export/import, zelfde card-patroon): "Profiel maken" (→ `MainWindow.EnterTweakProfileMode` zet de flag + selecteert de Tweaks-nav) + "Importeren" (→ match + detect + `StageDelta`, daarna ContentDialog "Naar Tweaks" → `NavigateToTweaks` → Apply).

### v0.9.19 — UI & Performance misc + battery %

**8 tweaks** (laatste tweak-uitbreiding vóór Presets). Network-versie geschrapt (user vond 'm niet nuttig).

**UI/Theme** (nieuwe subgroep "Invoer & weergave"):
- Disable Sticky Keys shortcut — `Accessibility\StickyKeys\Flags=506`
- Always show scrollbars — `Accessibility\DynamicScrollbars=0`. SignOut
- Faster menu show delay — `Desktop\MenuShowDelay=200` (van 400). SignOut

**Performance:**
- Disable hibernation — `Control\Power\HibernateEnabled=0`. Reboot. (Reclaimt hiberfil.sys niet — vereist powercfg)
- Disable Fullscreen Optimizations — 4-op HKCU `GameConfigStore`: GameDVR_FSEBehaviorMode=2, HonorUserFSEBehaviorMode=1, DXGIHonorFSEWindowsCompatible=1, EFSEFeatureFlags=0
- Allow frequent restore points — `SystemRestore\SystemRestorePointCreationFrequency=0`
- Enable periodic registry backup — `Configuration Manager\EnablePeriodicBackup=1`. Reboot

**Taskbar:**
- Show battery percentage in tray — `Advanced\IsBatteryPercentageEnabled=1`

### v0.9.18 — Telemetrie-hardening + Privacy-restjes + Office

**7 tweaks** (6 Privacy + 1 NotifLock) uit gap-analyse ronde 2. HKLM-policies batchen in 1 UAC.

- Extra telemetry hardening (bundle, 7-op) — `AppCompat\AITEnable=0` + `DisableInventory=1`, `PolicyManager\…\System\AllowExperimentation=0`, `DataCollection\DisableOneSettingsDownloads=1` + `LimitDiagnosticLogCollection=1`, `dmwappushservice\Start=4`, `AutoLogger-Diagtrack-Listener\Start=0`. Reboot
- Disable Windows Error Reporting — `Windows Error Reporting\Disabled=1`
- Disable handwriting data sharing — 2-op: `TabletPC\PreventHandwritingDataSharing=1` + `HandwritingErrorReports\PreventHandwritingErrorReports=1`
- Disable sending typing info (TIPC) — `Input\TIPC\Enabled=0`
- Disable settings sync — `SettingSync\SyncPolicy=5`
- Disable lock screen camera (NotifLock) — `Personalization\NoLockScreenCamera=1`
- Disable Microsoft Office telemetry & privacy (bundle, 13-op HKCU `Policies\…\Office\16.0`) — ClientTelemetry, QMEnable, LinkedIn, OSM logging/upload/obfuscation, Feedback/surveys, Privacy connected-experiences, Outlook InlineTextPrediction. No-op zonder Office

### v0.9.17 — Edge-debloat-bundle

**1 OFGB-stijl bundle** in AdsBloat: 19 HKLM `Policies\Microsoft\Edge`-keys onder één toggle, batchend in 1 UAC (`DisabledValue=null` → revert deletet de policy). Bron: O&O ShutUp10 Edge-set + parking-lot Edge-debloat (gap-analyse ronde 2, winutil + ShutUp10).

Keys: ConfigureDoNotTrack=1, EdgeShoppingAssistantEnabled, HubsSidebarEnabled, AddressBarMicrosoftSearchInBingProviderEnabled, UserFeedbackAllowed, AutofillCreditCardEnabled, LocalProvidersEnabled, SearchSuggestEnabled, WebWidgetAllowed, NetworkPredictionOptions=2, PersonalizationReportingEnabled, PaymentMethodQueryEnabled, StartupBoostEnabled, BackgroundModeEnabled, ShowRecommendationsEnabled, SpotlightExperiencesAndRecommendationsEnabled, NewTabPageContentEnabled, NewTabPageHideDefaultTopSites=1, WalletDonationEnabled.

### v0.9.16 — Window management + Ads + misc gaps (gap-fill 3/3)

**8 tweaks** uit de gap-analyse over 4 categorieën — laatste gap-fill-ronde.

**Window management** (UiTheme, groep "Desktop & vensters"):
- Disable Snap Layouts — `Advanced\EnableSnapBar=0`. Layout-grid-flyout uit
- Disable window snapping entirely — `Control Panel\Desktop\WindowArrangementActive="0"` (REG_SZ). Grof middel — alle snapping uit
- Alt+Tab browser tabs — multi-choice: `Advanced\MultiTaskingAltTabFilter` — 20 tabs (absent, standaard) / 5 (2) / 3 (1) / alleen vensters (3)

**Ads & Bloat:**
- Disable Microsoft 365 ads in Settings — HKLM `CloudContent\DisableConsumerAccountStateContent=1`
- Disable Spotlight desktop background — HKCU `Policies\…\CloudContent\DisableSpotlightCollectionOnDesktop=1` (geen UAC)
- Hide Settings 'Home' page — HKLM `Policies\Explorer\SettingsPageVisibility="hide:home"` (REG_SZ)

**Misc:**
- Disable mouse acceleration (Performance) — 3-op REG_SZ: `Control Panel\Mouse` MouseSpeed/Threshold1/Threshold2=0 (revert 1/6/10). SignOut
- Disable AI service auto-start (AiCopilot) — `Services\WSAIFabricSvc\Start=3` (manual; default 2). Alleen Copilot+ PC's, anders no-op. Reboot

Daarmee zijn alle ~21 gap-tweaks uit de Winhance / Win11Debloat-analyse verwerkt (gap-fill 1+2+3 = v0.9.14/15/16).

### v0.9.15 — Privacy + Security gaps (gap-fill 2/3)

**6 tweaks** uit de gap-analyse: 5 Privacy + 1 nieuwe **Security**-categorie. De 4 HKLM-policies batchen in 1 UAC.

**Privacy:**
- Disable Location Services — HKLM `LocationAndSensors\DisableLocation=1`. SignOut
- Disable Find My Device — HKLM `FindMyDevice\AllowFindMyDevice=0`
- Disable device search history — `SearchSettings\IsDeviceSearchHistoryEnabled=0`
- Set telemetry to minimum — HKLM `DataCollection\AllowTelemetry=0` (Home/Pro klemt naar Basic; vult `Privacy.DisableDiagTrackService` aan)
- Disable online speech recognition — `Speech_OneCore\Settings\OnlineSpeechPrivacy\HasAccepted=0`

**Security** (nieuwe categorie, 🛡 shield-icoon — user-keuze: losse categorie ondanks 1 tweak; vult later met geparkeerde caution-tier items):
- Disable automatic BitLocker device encryption — HKLM `Control\BitLocker\PreventDeviceEncryption=1`. Raakt alleen toekomstige auto-encryptie (24H2 OOBE); al versleutelde schijf blijft versleuteld

### v0.9.14 — Explorer + Taskbar gaps (gap-fill 1/3)

**7 tweaks** uit de Winhance / Win11Debloat gap-analyse (20 mei 2026). Geen nieuwe categorieën — aangevuld bij Explorer (4) en Taskbar (3); beide groeploos → renderen plat op naam.

**Explorer:**
- Drive letter position — multi-choice: `Explorer\ShowDriveLettersFirst` — Na de naam (absent, standaard) / Vóór de naam (4) / Alleen netwerkschijven vóór (1) / Verbergen (2)
- Hide 'Home' in navigation pane — `CLSID\{f874310e-…}\System.IsPinnedToNamespaceTree=0`
- Hide 'Gallery' in navigation pane — `CLSID\{e88865ea-…}\System.IsPinnedToNamespaceTree=0`
- Enable item checkboxes — `Advanced\AutoCheckSelect=1`

**Taskbar:**
- Hide Chat / Teams button — `Advanced\TaskbarMn=0`
- Disable share drag-tray — `CDP\DragTrayEnabled=0` (24H2+)
- Disable taskbar badges — `Advanced\TaskbarBadges=0`

**Geparkeerd:** auto-hide taskbar — zit in `StuckRects3\Settings` binary-blob (byte 8 bit-flip), niet veilig in het scalar TweakOperation-model (zelfde reden als UserPreferencesMask).

### v0.9.13 — Gaming tweaks (→ Performance-categorie)

**3 tweaks** toegevoegd. Geen aparte Gaming-categorie: een eigen tile voor maar 3 tweaks is mager naast categorieën met 9-13 — de 3 zijn stuk voor stuk achtergrond-overhead reducties en horen functioneel in **Performance**. De `TweakCategory.Gaming` enum-waarde is verwijderd (incl. DisplayName/Icon/Blurb-cases). Research mei 2026 (3 web-passes, Win11 24H2/25H2).

- Disable background game recording (Game DVR) — 2-op HKCU: `System\GameConfigStore\GameDVR_Enabled=0` + `CurrentVersion\GameDVR\AppCaptureEnabled=0`. Stopt de continue achtergrond-capture
- Disable Xbox Game Bar overlay — 2-op HKCU: `Software\Microsoft\GameBar\UseNexusForGameBarEnabled=0` + `ShowStartupPanel=0`. Win+G-overlay + opstart-tips uit
- Disable Xbox services — 4-op HKLM (1 UAC, 🔁 reboot): `Services\{XblAuthManager, XblGameSave, XboxNetApiSvc, XboxGipSvc}\Start=4` (default 3). Caveat in omschrijving: breekt Xbox-app / Game Pass / Xbox Live-aanmelding; XboxGipSvc = controller-accessoires

De 3 tweaks staan vlak in Performance (geen sub-groep, zoals de bestaande "schone 9") → Performance telt nu 12 tweaks. Tweak-id's gebruiken het `Performance.`-prefix.

**Bewust niet opgenomen:**
- **Disable Game Mode** (`GameBar\AllowAutoGameMode=0`) — Game Mode is op moderne Windows juist nuttig (prioriteert de game); uitzetten is geen verbetering
- **HKLM `AllowGameDVR` system-wide policy** — de HKCU Game DVR-tweak dekt het al zonder UAC
- **Xbox-services splitsen** (3 Live + GipSvc apart) — user-keuze: één gecombineerde tweak

### v0.9.12 — Updates uitbreidingen

**6 tweaks** in de Updates-categorie (was leeg), 2 sub-groepen. Research mei 2026 (5 web-passes, Win11 24H2/25H2 geverifieerd). Alles HKLM → batcht in 1 UAC. Side-effect: het Windows Update-scherm toont "Some settings are managed by your organization" — cosmetisch.

**Eerlijke scope-keuze:** Windows Update tweaken is een mijnenveld — Microsoft faseert update-beleid actief uit. Bewust **alléén de betrouwbaar-werkende set**, geen deprecated rommel.

**Groep "Updates & herstart"** (4):
- Disable auto-restart while logged on — `WindowsUpdate\AU\NoAutoRebootWithLoggedOnUsers=1`. Geen geforceerde herstart tijdens gebruik
- Disable update restart notifications — `WindowsUpdate\UX\Settings\RestartNotificationsAllowed2=0`. Geen herstart-nag-meldingen
- Disable 'Get latest updates as soon as available' — `UX\Settings\IsContinuousInnovationOptedIn=0`. Geen voorrang voor non-security previews
- Active hours — multi-choice — `UX\Settings\SmartActiveHoursState` + `ActiveHoursStart/End` — Automatisch (alle 3 absent) / 08:00–23:00 / 06:00–middernacht

**Groep "Drivers & netwerk"** (2):
- Disable driver updates via Windows Update — 3-op: `WindowsUpdate\ExcludeWUDriversInQualityUpdate=1` + `DriverSearching\SearchOrderConfig=0` + `Device Metadata\PreventDeviceMetadataFromNetwork=1`. Sluit alle 3 WU-driver-paden af
- Disable Delivery Optimization (P2P) — `DeliveryOptimization\DODownloadMode=0`. Geen peer-to-peer update-upload/download

**Bewust niet opgenomen (research-onderbouwd):**
- **Defer feature / quality updates** — `DeferFeatureUpdatesPeriodInDays` / `DeferQualityUpdatesPeriodInDays`: Microsoft heeft de UI van verse 24H2-installs verwijderd, registry-policy nog maar deels gehonoreerd en niet toekomstvast (user-keuze: niet opnemen)
- **Disable automatic updates entirely** (`NoAutoUpdate=1`) — heavy-handed, mist makkelijk security-patches (user-keuze: niet opnemen)
- **Pause updates N dagen** — `PauseUpdatesExpiryTime` is een datum-timestamp, geen toggle
- **Pin to Windows-versie** (`TargetReleaseVersionInfo`) — vereist runtime versie-string; TweakOperation-model is statisch-data
- **Ethernet als metered** — `DefaultMediaCost` is TrustedInstaller-protected (ownership-change nodig); te fragiel
- **Windows Update-service uitschakelen** (`wuauserv Start=4`) — moderne Windows herstelt dit + breekt updates compleet

### v0.9.11 — Notifications & Lock Screen tweaks

**7 tweaks** in de NotificationsLock-categorie (was leeg), 3 sub-groepen. Research mei 2026 (4 web-passes, Win11 24H2/25H2 geverifieerd). De twee HKLM-policy-tweaks batchen in 1 UAC; de rest is HKCU → geen UAC.

**Schets-correcties uit research:**
- Schets-item "Disable 'Suggest ways to finish setup' notifications" is bewust NIET opgenomen — `ScoobeSystemSettingEnabled` zit al in v0.9.4 (`Ads.DisableScoobePrompt` + OFGB-bundle). Niet gedupliceerd.
- Schets-items "Disable Action Center / Notification Center" en "Hide Calendar from systray click" zijn op Win11 dezelfde unified flyout → samengevoegd tot één tweak (`DisableNotificationCenter`).

**Groep "Meldingen"** (3):
- Disable all notifications — `PushNotifications\ToastEnabled=0`. Master-toggle, alle toasts uit. SignOut
- Disable notification sounds — `Notifications\Settings\NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND=0`. Toasts blijven visueel, geen geluid
- Notification display time — multi-choice — `HKCU\Control Panel\Accessibility\MessageDuration` — 5s (standaard, value-absent) / 7s / 15s / 30s / 1min / 5min. SignOut

**Groep "Notificatiecentrum"** (1):
- Disable Notification Center & Calendar flyout — `HKCU\Software\Policies\...\Explorer\DisableNotificationCenter=1`. Bel-icoon weg + klok opent geen kalender/meldingen-paneel meer (Win10-stijl tray-klok). ExplorerRestart

**Groep "Vergrendelscherm"** (3):
- Disable lock screen — HKLM `Policies\...\Personalization\NoLockScreen=1`. Direct naar inlogscherm. Caveat in omschrijving: bij Secure Sign-In niet 100% overslaanbaar. SignOut
- Disable notifications on lock screen — 2-op HKCU: `PushNotifications\LockScreenToastEnabled=0` + `Notifications\Settings\NOC_GLOBAL_SETTING_ALLOW_TOASTS_ABOVE_LOCK=0`. SignOut
- Disable lock screen background on sign-in — HKLM `Policies\...\System\DisableLogonBackgroundImage=1`. Effen accent-kleur i.p.v. foto op het wachtwoord-scherm

`DisabledValue=null` bij de policy-tweaks (NoLockScreen / DisableNotificationCenter / DisableLogonBackgroundImage) — de policy-value is absent by default, revert deletet 'm.

**Bewust niet opgenomen:**
- **Spotlight "fun facts/tips" op lockscreen** — `RotatingLockScreenOverlayEnabled` + `SubscribedContent-338387` zitten al in de OFGB-bundle (v0.9.4). Geen standalone duplicaat (user-keuze).
- **Focus / Do Not Disturb auto-regels** (full-screen, games) — per-regel registry-structuur onder `Notifications\Settings` is fragiel/ongedocumenteerd, past niet in het toggle-model
- **Disable Quick Settings panel** — geen betrouwbare losse registry-key op Win11

### v0.9.10 — Context Menu tweaks

**10 Context Menu tweaks** in de ContextMenu-categorie, 2 sub-groepen (collapsible). Research mei 2026 (3 web-passes) — alle CLSID's geverifieerd. Alles HKCU → geen UAC; `ExplorerRestart` zodat het menu de wijziging direct oppikt.

**Win11 context-menu realiteit**: custom `shell\`-verbs verschijnen alleen onder "Toon meer opties" — TENZIJ de Classic context menu tweak (Explorer-categorie, v0.9.1) aanstaat, dan staan ze direct in het menu. De verwijder-tweaks voor Photos/Paint raken het hoofdmenu direct (via de Shell Extensions Blocked-lijst).

**Groep "Items verwijderen"** (7) — onderdrukt shell-extensions via een String-value (CLSID-naam, lege data) in `Shell Extensions\Blocked`. Niet-destructief, reversible:
- Remove 'Edit with Photos' — `{BFE0E2A4-…}` (hoofdmenu)
- Remove 'Edit with Paint' — `{2430F218-…}` (hoofdmenu)
- Remove 'Scan with Microsoft Defender' — `{09A47860-…}` (Blocked-lijst i.p.v. key-delete; Defender herstelt een verwijderde key anders)
- Remove 'Restore previous versions' — `{596AB062-…}`
- Remove 'Cast to Device' — `{7AD84985-…}` (Play To)
- Remove 'Include in library' — `{3dad6c5d-…}`
- Remove 'Give access to' / sharing — `{f81e9010-…}` (netwerk-shares blijven werken)

**Groep "Items toevoegen"** (3) — multi-op verb-keys onder `HKCU\Software\Classes`; verb-root krijgt `DeleteKeyOnAbsent` zodat revert de subtree opruimt:
- Add 'Take Ownership' — `runas`-verb (auto-elevate) op bestanden + mappen, takeown + icacls. Beschrijving waarschuwt: nooit op systeemmappen gebruiken
- Add 'Move to folder' / 'Copy to folder' — klassieke shell-handlers via `ContextMenuHandlers` (CLSID `{C2FBB631-…}` / `{C2FBB630-…}`)
- Add 'Open Terminal as Admin' — `wt.exe` elevated via PowerShell `Start-Process -Verb RunAs`, op mapachtergrond + mappen

### v0.9.9 — Performance tweaks ("de schone 9") + Tweaks sidebar-nav fix

**9 Performance tweaks** in de Performance-categorie. Research mei 2026 (2 passes via web): de roadmap-schets is uitgewerkt + extra tweaks onderzocht. Eerlijke conclusie: veel "performance-tweaks" zijn placebo of riskant — daarom alléén de geverifieerde solide set ("de schone 9"), geen snake-oil. Meeste HKLM-policies/keys → batchen in 1 UAC.

- **Disable Fast Startup** — `Session Manager\Power\HiberbootEnabled=0`. Reboot
- **Disable power throttling** — `Power\PowerThrottling\PowerThrottlingOff=1`. Reboot
- **Disable Storage Sense** — `Policies\...\StorageSense\AllowStorageSenseGlobal=0`
- **Disable background apps (UWP/Store)** — `Policies\...\AppPrivacy\LetAppsRunInBackground=2` (Force Deny). SignOut
- **Enable long path support** — `FileSystem\LongPathsEnabled=1`. Reboot
- **Prefer IPv4 over IPv6** — `Tcpip6\Parameters\DisabledComponents=0x20` (IPv6 blijft functioneel; bewust 0x20, niet 0xFF). Reboot
- **Disable Multiplane Overlay** — `Dwm\OverlayTestMode=5` (flikker/tearing-fix; undocumented debug-waarde). Reboot
- **Remove startup-app delay** — HKCU `Explorer\Serialize\StartupDelayInMSec=0` (geen UAC). SignOut
- **Disable NTFS last-access timestamps** — `FileSystem\NtfsDisableLastAccessUpdate=1`. Reboot

`DisabledValue=null` bij de tweaks waar de Windows-default "value absent" is — revert deletet de value dan i.p.v. een waarde te forceren.

**Bewust niet opgenomen** (research-onderbouwd):
- **powercfg-afhankelijk** — Ultimate Performance power plan (`powercfg /duplicatescheme`) en hibernation-met-hiberfil.sys-reclaim (`powercfg /hibernate off`) passen niet in het registry-only TweakOperation-model. Geparkeerd; eventueel later een command-tweak model-uitbreiding
- **Startup-apps cleanup** — vereist enumeratie van Run-keys + StartupApproved + Startup-folders + Task Scheduler; aparte feature, geen flat toggle
- **Visual-effects 'best performance' preset** — overlapt al met de UI/Theme animaties/transparency tweaks (v0.9.8); de UserPreferencesMask binary is onveilig om blind te schrijven
- **Placebo/riskant** — WSearch uitzetten (breekt Start-menu search), SysMain/Prefetcher/Win32PrioritySeparation/8.3-names/HwSchMode (verwaarloosbaar of debated op moderne hardware), curated services (al trigger-started → bijna geen idle-kosten). Xbox-services horen in v0.9.13 Gaming

**Tweaks sidebar-nav fix**: klikken op "Tweaks" in de NavigationView terwijl je op een `TweakCategoryDetailPage` staat → terug naar de category-grid landing. Op de landing met actieve search → search gewist (`ResetToRoot`). Spiegelt het bestaande gedrag van de Apps-sidebar.

### v0.9.8 — Tweaks-tab herstructurering (category-grid) + UI / Theme tweaks

Grote UI-herbouw van de Tweaks-tab naar het Apps-tab patroon, plus 13 UI/Theme tweaks en 2 Explorer tweaks. **De apply/detect-logica in TweakService is volledig ongemoeid** — alleen de UI-laag is herbouwd.

**Tweaks-tab herstructurering** (was: één lange pagina met expander-lijst per categorie — user vond dat "lelijk"). Nieuwe structuur, gekozen via user-Q&A:
- **Category-grid landing** — grote tiles (emoji-icoon + naam + blurb + "N/M actief"), 2-3 per rij, net als de Apps-tab. Klik tile → detail-pagina
- **`TweakCategoryDetailPage`** (nieuw) — toont de tweaks van één categorie. **Hybride sub-groepen**: geen Group → platte lijst; Group + <8 tweaks → vaste sub-headers; Group + ≥8 → inklapbare Expander per sub-groep (default dicht)
- **Globale zoekbalk** op de landing — doorzoekt alle tweaks (naam / omschrijving / use-case / categorie), toont platte resultaten met categorie-label
- **Globale footer** (pending count + Discard + Apply) op zowel de landing als elke detail-pagina
- **Progress-status per tile**: groene "Volledig actief"-pill bij 100% toegepast, anders "N / M actief"-tekst. Pending-badge wanneer een categorie openstaande wijzigingen heeft

**Nieuwe infra** (UI-laag, geen wijziging aan apply/detect):
- `TweakPendingService` — cross-page pending-store (App.TweakPending), overleeft navigatie landing ↔ detail
- `TweakCardFactory` — gedeelde card-renderer voor detail-pagina én zoekresultaten. **Checkbox-fix**: Enabled/Disabled tweaks zijn nu schone 2-state checkboxes (geen verwarrende indeterminate-minus meer bij toggle); alleen tweaks die al Partial zíjn krijgen een 3-state checkbox (daar is de minus legitiem)
- `TweakApplyRunner` — gedeelde apply-orchestratie (backup-prompt + batching + explorer-restart + re-detect), aangeroepen vanaf beide footers
- `Tweak.Group` veld (nullable string, puur cosmetisch — apply/detect leest 't nooit)

**Infra — HKU hive support**: `ParsePath` (TweakService + SnapshotService) ondersteunt nu de **HKU / HKEY_USERS** hive, nodig voor `HKU\.DEFAULT` (login-scherm profiel).

**13 UI / Theme tweaks** in 4 sub-groepen:

**Groep "Thema & kleuren":**
- **System theme** — multi-choice (Light / Dark / Custom). `Themes\Personalize\AppsUseLightTheme` + `SystemUsesLightTheme`. Custom = donkere apps + lichte shell
- **Disable transparency effects** — `Personalize\EnableTransparency=0`
- **Accent color on title bars & borders** — `DWM\ColorPrevalence=1` (toggle, geen color-picker)
- **Accent color on Start & taskbar** — `Themes\Personalize\ColorPrevalence=1` (alleen zichtbaar in Dark mode)

**Groep "Desktop & vensters":**
- **Disable window & taskbar animations** — 2-op: `WindowMetrics\MinAnimate=0` (REG_SZ) + `TaskbarAnimations=0`
- **Show This PC / Network / Control Panel on desktop** — 3 GUID-keys onder `HideDesktopIcons\NewStartPanel`
- **Disable Snap Assist suggestions** — `EnableSnapAssistFlyout` + `SnapAssist` = 0
- **Disable Aero Shake** — `DisallowShaking=1`

**Groep "Boot & login":**
- **Verbose logon / shutdown messages** — HKLM `Policies\System\verbosestatus=1`
- **Show detailed Blue Screen info** — HKLM `CrashControl\DisplayParameters=1`
- **NumLock on at boot** — 2-op: HKCU + `HKU\.DEFAULT` `Control Panel\Keyboard\InitialKeyboardIndicators=2`. SignOut
- **Disable login screen background blur** — HKLM `Policies\System\DisableAcrylicBackgroundOnLogon=1`

**Groep "Geluid":**
- **Disable Windows startup sound** — HKLM `LogonUI\BootAnimation\DisableStartupSound=1`

**+ 2 Explorer tweaks** (Explorer-categorie, geen sub-groepen):
- **Compact view in File Explorer** — `Explorer\Advanced\UseCompactMode=1`
- **Show full path in File Explorer** — `Explorer\CabinetState\FullPath=1`

**Bewust niet opgenomen / geparkeerd:**
- **Accent color override** — vereist een color-picker UI; past niet in het toggle/multi-choice TweakOperation-model. (De accent-*toggles* hierboven zijn wél opgenomen — die zetten alleen aan/uit, kiezen geen kleur)
- **Restore classic Photo Viewer** — ~15 registry-values onder `Windows Photo Viewer` + `HKLM\SOFTWARE\Classes`; fragiel op Win11 24H2+ (UCPD blokkeert programmatische default-app changes — user zou 't alsnog handmatig als default moeten kiezen)

### v0.9.7 — Privacy uitbreidingen

**8 Privacy tweaks** in de Privacy-categorie. Mix van HKLM-policies (RequiresElevation, batchen in 1 UAC dankzij de v0.9.6 batching-fix) en HKCU user-keys (geen UAC). Tailored Experiences is bewust niet gedupliceerd — zit al in Ads & Tracking (v0.9.4).

- **Disable Activity History** — 3-op HKLM policy: `System\EnableActivityFeed` + `PublishUserActivities` + `UploadUserActivities` = 0. Geen Timeline-verzameling van apps/documenten meer. SignOut-requirement
- **Disable inking & typing personalization** — 4-op HKCU: `InputPersonalization\RestrictImplicitInkCollection` + `RestrictImplicitTextCollection` = 1, `TrainedDataStore\HarvestContacts` = 0, `Personalization\Settings\AcceptedPrivacyPolicy` = 0. Stopt handschrift/typ-data verzameling
- **Disable Feedback Hub prompts** — `Siuf\Rules\NumberOfSIUFInPeriod` = 0. Geen feedback-popups
- **Disable CEIP** — HKLM `SQMClient\Windows\CEIPEnable` = 0. Schakelt het Customer Experience Improvement Program uit; de CEIP scheduled tasks blijven bestaan maar zijn inert. (Bewust geen scheduled-task disable — de TweakOperation-model is registry-only; CEIPEnable=0 dekt de telemetrie-zorg functioneel)
- **Disable Suggested Actions on clipboard** — HKCU `SmartActionPlatform\SmartClipboard\Disabled` = 1. Verwijdert de Win11 22H2+ pop-up bij kopiëren van telefoonnummers/datums
- **Disable clipboard cloud sync** — HKLM `System\AllowCrossDeviceClipboard` = 0. Clipboard verlaat de PC niet meer; lokale history blijft werken
- **Disable DiagTrack telemetry service** — HKLM `Services\DiagTrack\Start` = 4 (disabled; 2 = automatic default). De centrale Windows telemetrie-service start niet meer. Reboot-requirement
- **Disable WPBT** — HKLM `Session Manager\DisableWpbtExecution` = 1. Blokkeert OEM/firmware boot-binary injectie. Reboot-requirement

### v0.9.6 — AI / Copilot tweaks (Win11 24H2+)

**5 AI / Copilot tweaks** in de AiCopilot-categorie. Allemaal HKLM group-policy keys → `RequiresElevation=true`, batchen in 1 UAC-prompt. Research mei 2026 via Microsoft Learn Policy CSP (WindowsAI) + Manage Recall / Click to Do / Notepad docs.

- **Disable Recall** — `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI\DisableAIDataAnalysis=1`. Schakelt de Copilot+ PC screenshot-snapshotting feature uit. SignOut-requirement. Alternates: HKCU user-policy + `AllowRecallEnablement=0` (de policy die Recall als optionele component blokkeert)
- **Disable Click to Do** — `WindowsAI\DisableClickToDo=1`. Verwijdert de 24H2+ AI-acties uit het right-click menu. SignOut-requirement. Alternate: HKCU user-policy
- **Disable AI features in Paint** — 3-op tweak: `CurrentVersion\Policies\Paint` met `DisableCocreator` + `DisableGenerativeFill` + `DisableImageCreator` = 1. Restart Paint om effect te zien
- **Disable AI features in Notepad** — `HKLM\SOFTWARE\Policies\WindowsNotepad\DisableAIFeatures=1` (app-level policy, let op: NIET onder `Policies\Microsoft\Windows`). Kill alle Copilot-features in Notepad. Restart Notepad
- **Disable generative AI access for apps** — `AppPrivacy\LetAppsAccessGenerativeAI=2` (Force Deny; 0=user-controlled). System-brede AppPrivacy policy die generatieve AI-toegang voor alle apps blokkeert — dekt o.a. Image Creator in de Photos-app. SignOut-requirement

**Bewust niet opgenomen** (research-onderbouwd):
- **Win+C Copilot hotkey** — `TurnOffWindowsCopilot` is door Microsoft gedeprecateerd en grotendeels inert op 24H2/25H2 (targett de legacy Copilot-pane). De bestaande "Hide Copilot button" (v0.9.2 Taskbar) dekt de zichtbare kant al
- **Copilot AppX removal** — hoort thuis in de Debloat-tab (AppX uninstall), niet bij de registry-toggles. `Microsoft.Copilot` is een normale Store-app, veilig te verwijderen via Debloat; system-packages (`MicrosoftWindows.Client.CoreAI` / `AIX`) zijn NIET veilig te verwijderen

**SignOut-melding in ResultBar**: post-apply InfoBar meldt nu expliciet "Sommige tweaks (zoals AI-policies) hebben pas effect nadat je uitlogt of de PC herstart" wanneer een toegepaste tweak `RestartRequirement.SignOut` of `Reboot` heeft — voorheen zei 'ie alleen "Done." waardoor user dacht dat er niets gebeurd was.

### v0.9.5 — Backup & Restore infrastructuur (Tweaks snapshots + System Restore Points)

User-feedback v0.9.4 → "Zijn alle changes wel veilig en stabiel voor het systeem?". v0.9.5 voegt twee complementaire veiligheidsnetten toe — registry-snapshots voor de lichte registry-mutaties (Tweaks) en Windows System Restore Points voor de zware delete-operaties (Deep Clean + Debloat).

**SnapshotService** (`Services/SnapshotService.cs`) — JSON-snapshots in `%LOCALAPPDATA%\SetupToolbox\snapshots\`:
- `CaptureAsync(tweaks, description)` leest vóór elke Apply de actuele registry-state van alle ops die zouden worden geschreven (incl. `WasAbsent` markers) en parkeert die als JSON
- `RestoreAsync(snapshotId)` zet de exacte staat terug: schrijft `PreviousValue` waar mogelijk, deletet de value als die origineel absent was. Splitst lokale (HKCU non-policy) van elevated (HKLM + HKCU Policies) ops — elevated via 1 UAC reg.exe batch, zelfde patroon als TweakService.ApplyAsync
- Auto-prune op 20 snapshots; oudere worden silent verwijderd. Per snapshot: id (timestamp + guid-fragment), description (user-defined of auto-gegenereerd uit tweak-namen), createdAt, tweakIds, entries

**Tweaks tab integratie**:
- Nieuwe **"Vorige staat herstellen"** knop bovenaan (naast Apply/Discard/Restart Explorer) — alleen zichtbaar als er minstens 1 snapshot bestaat. Opent de SnapshotBrowserDialog die alle snapshots toont met description + timestamp + count, met per-snapshot **Herstel** + **Verwijder** knoppen. Confirm-dialog vóór de daadwerkelijke restore zodat een misklik geen schrijfacties triggert
- **BackupPromptDialog** vóór elke Apply (Nederlandstalig) — bevat tekstveld voor custom snapshot-naam ("Bijv. 'Voor Bing search uit'") + checkbox "Vraag dit niet meer en maak voortaan altijd backup" die de SettingsService-mode aanpast naar Always (bij Primary) of Never (bij Secondary)
- Apply-flow: respecteert de 3-state `BackupBeforeApplyMode` setting (Ask/Always/Never)

**RestorePointService** (`Services/RestorePointService.cs`) — wrapt `Checkpoint-Computer` + `Get-ComputerRestorePoint`:
- `CreateAsync(description)` runt Checkpoint-Computer in elevated PS (1 UAC), met RestorePointType=MODIFY_SETTINGS
- `GetStatusAsync()` returnt (canCreate, hoursSinceLast, blockedReason) — checkt System Protection state + 24h rate-limit
- `BuildInlineCheckpointScript(description)` is een static helper voor het embedden van de checkpoint-call in een externe elevated PS-batch (zodat 1 UAC dekt voor checkpoint + delete samen)

**Deep Clean integratie**:
- `DeepCleanService.DeleteAsync(items, restorePointDescription)` accepteert nu optionele description. Wanneer non-null prepend `RunElevatedBatchAsync` een `Checkpoint-Computer` call vóór de delete — 1 UAC, geen extra dialog
- `DeepCleanDialog` leest `Settings.RestorePointBeforeDeepClean` en stuurt description door naar DeleteAsync
- `DeepCleanPage` toont **first-run popup** (`RestorePointConfigDialog`) bij allereerste scan: vraagt eenmalig "ja restore point" / "nee skip" en zet `DeepCleanRestorePointConfigured=true`. Volgende keer geen popup — wijzigen kan via Settings

**Debloat integratie**:
- `BloatwareService.UninstallBatchAsync(..., restorePointDescription)` accepteert nu optionele description met dezelfde prepend-checkpoint logica
- `BloatwareUninstallDialog` propageert de description door
- `DebloatPage` doet dezelfde first-run flow als DeepCleanPage. RPdescription wordt bij bloatware-batch verbruikt; wanneer alleen apps-batch volgt (geen bloatware geselecteerd), aparte upfront `App.RestorePoint.CreateAsync` call

**Settings — nieuwe sectie "Backup & herstel"**:
- 3-state radio buttons voor `BackupBeforeApplyMode`: "Vraag elke keer (aanbevolen)" / "Altijd automatisch backup" / "Nooit backup maken"
- **"Bekijk snapshots..."** knop (met count) opent SnapshotBrowserDialog

**Settings — nieuwe sectie "System Restore Points"**:
- 2 toggles: "Restore point voor Deep Clean" + "Restore point voor Debloat". Toggle aanpassen markeert ook automatisch de Configured-flag zodat de first-run popup niet meer triggert
- ⚠ **Uitroepteken-glyph** naast de toggles wanneer:
  - System Protection uit staat op systeemschijf — tooltip wijst naar System Properties > System Protection
  - Laatste restore point < 24u geleden — tooltip legt uit dat Windows nieuwe punten skipt binnen 24u
- InfoBar onder de toggles voor de System Protection blokkering-melding

**SettingsService extensies** (`SettingsService.cs`):
- `BackupBeforeApplyMode` enum (Ask/Always/Never) + property (default Ask)
- `RestorePointBeforeDeepClean` + `DeepCleanRestorePointConfigured` bools
- `RestorePointBeforeDebloat` + `DebloatRestorePointConfigured` bools
- JSON-persisted naar `%LOCALAPPDATA%\SetupToolbox\settings.json` zoals bestaande settings

### v0.9.4 — Ads & Tracking + Win11 24H2+ Policies-ACL fix + Partial-state UX

**5 Ads & Tracking tweaks** (category-rename van "Ads & Bloat" — "Bloat" was verwarrend i.c.m. de Debloat-tab; deze categorie gaat puur over marketing/tracking-toggles binnen Windows). Allemaal HKCU, geen UAC:
- **Mega-bundle "Disable all suggested & sponsored content"** — 18 HKCU keys in 1 toggle: lock-screen tips, Start menu suggesties, Settings-ads (3 ID's), Welcome experience, "Finish setting up" popup, auto-install OEM/Store apps, generic content delivery, Tailored Experiences, notification suggestions. Mirror van xM4ddy/OFGB. Partial-state in pill als user al subset handmatig had
- **Disable advertising ID** — `AdvertisingInfo\Enabled=0`
- **Hide File Explorer OneDrive ads** — `ShowSyncProviderNotifications=0`
- **Disable 'Finish setting up your device' popup** — `ScoobeSystemSettingEnabled=0`
- **Disable Tailored Experiences** — `TailoredExperiencesWithDiagnosticDataEnabled=0`

HKLM CloudContent policies geparkeerd voor latere iteratie (Pro/Edu only, vereisen UAC).

**Win11 24H2+ Policies-ACL fix**: `HKCU\Software\Policies\Microsoft\Windows\Explorer` is op 24H2+ ACL-hardened — user-token heeft ReadKey only, alleen `BUILTIN\Administrators` heeft FullControl. In-process `Microsoft.Win32.Registry` writes faalden met "Access to the registry key is denied". Surfaced doordat user de DisableBingSearch tweak probeerde te applyen → de diagnostic ResultBar liet `Disable web search in Start → Explorer\DisableSearchBoxSuggestions: Access to the registry key '...\Explorer' is denied. (20 writes wel succesvol)` zien. Fix: `RequiresElevation=true` op de policy-ops (HideRecommendedSection + DisableSearchBoxSuggestions). Routet via bestaande elevated reg.exe batch — admin-token passeert de ACL via de Administrators-ACE. Eén UAC-prompt per Apply-batch.

**IsThreeState CheckBox voor Partial-state UX**: pre-fix kon user een Partial-state tweak niet "completen" via de checkbox. Partial telde als IsToggleOn → checkbox visueel checked → klikken = unchecken = pending revert (niet apply) → Apply button bleef grayed-out. Nu: `IsThreeState=true` op CheckBox, Partial maps naar indeterminate vierkant. Klik op indeterminate → checked → pending=apply → Apply activeert. Cycle false→null→true→false; voor Disabled state vangt CheckBox_Toggled de null-intermediate op en springt door naar true zodat 1 klik = apply blijft (anders waren 2 klikken nodig om de cycle door te lopen).

### v0.9.3 — Start Menu category + collapsible categories + multi-path detection

**Multi-path detection** opgelost (kern-architectuur fix, niet in initiële scope): single-path detection miste de actual state als user de tweak via een ander mechanisme had geactiveerd. Voorbeelden uit user-feedback: "Disable web search in Start" en "Hide Recommended section" stonden bij de user al aan via Settings/manual regedit maar onze UI toonde 'm als off (checkbox ongekruist) — gevolg: user dacht dat-ie 'm nog moest applyen terwijl 'ie al actief was.

Architectuur:
- Nieuwe `TweakAlternateSignal` record (Path / ValueName / Kind / EnabledValue) — read-only signalen die ALLEEN voor state-detectie gebruikt worden. Schrijf-logica blijft single-path (alleen `TweakOperation.Path`)
- `TweakOperation` extended met `AlternateEnabledPaths` (default empty) en `AbsenceMeans` (default Disabled, voor zeldzame tweaks waar Windows-default exact onze EnabledValue is)
- `MatchOpState` in TweakService: walk eerst alle alternates — als ÉÉN matcht → return Enabled direct. Anders fall-through naar bestaande primary-comparison met `AbsenceMeans` ipv hardcoded Disabled. Alternates kunnen alleen voor Enabled stemmen, nooit voor Disabled (advisory reads, niet authoritative writes)
- Backward-compatible: bestaande 14 Explorer/Taskbar tweaks compileren ongewijzigd

Alternates gewired voor de problematische tweaks (research-driven, niet hardcoded):
- **HideRecommendedSection**: HKLM-policy (gpedit Pro/Ent), PolicyManager (MDM/Intune), Start_IrisRecommendations=0 (Win11 Home Settings-toggle die hetzelfde visuele effect heeft)
- **DisableBingSearch**: HKLM DisableSearchBoxSuggestions + ConnectedSearchUseWeb=0 (HKLM én HKCU policy). BingSearchEnabled bewust GEEN alternate — research wijst uit dat 'ie onbetrouwbaar werkt op 24H2+
- **HideMostUsedApps (Start_TrackProgs)**: ShowOrHideMostUsedApps=2 (alternative tweaker-key), NoInstrumentation policy
- **HideCopilot (ShowCopilotButton)**: HKCU + HKLM TurnOffWindowsCopilot=1 (vooral Enterprise/Education-honored op 24H2+)
- **HideWidgets (TaskbarDa)**: HKLM Dsh\AllowNewsAndInterests=0 + MDM PolicyManager equivalent

Bron-research (mei 2026 via web): Microsoft Learn policy-CSP docs, ElevenForum threads, Winaero / WindowsLatest writeups voor Win11 24H2/25H2-specifieke gedragsveranderingen — zie commit-log voor link-lijst.

**7 Start Menu tweaks** in TweakService.BuildAll():
- **Layout** (multi-choice ComboBox: Default / More pins / More recommendations) — mirror van Settings > Personalization > Start > Layout. Schrijft `Start_Layout` DWORD 0/1/2 onder Explorer\Advanced
- **Hide Recommended section** — policy `HKCU\Software\Policies\Microsoft\Windows\Explorer\HideRecommendedSection=1`. Verbergt de hele Recommended sectie onderaan Start (MRU + tips). Werkt op Win11 22H2+ ook op Home (policy-engine is decoupled van Pro-only UI)
- **Hide most-used apps** — `Start_TrackProgs=0`. Start toont alleen pinned items, geen automatische top-rij meer
- **Hide recently opened items** — `Start_TrackDocs=0`. MRU-files verdwijnen uit Recommended + uit taskbar/Start jump-lists
- **Disable web search in Start** — schrijft 3 HKCU keys in 1 toggle: `Policies\...\Explorer\DisableSearchBoxSuggestions=1` (modern policy) + `Search\BingSearchEnabled=0` (legacy, sub-builds 22H2/23H2) + `Search\CortanaConsent=0` (Win10 companion). Verschillende Win11 builds honoreren verschillende keys; alle drie schrijven dekt 22H2 t/m 25H2. Aggregate-detection toont Partial als user maar 1 van de 3 keys had gezet
- **Disable Search Highlights** — `Feeds\DSB\ShowDynamicContent=0` + `SearchSettings\IsDynamicSearchBoxEnabled=0` (legacy + modern key in 1 toggle). Verwijdert de roterende 'today in history' / Bing trending content
- **Disable Cortana** — HKLM `Policies\...\Windows Search\AllowCortana=0`. Vereist UAC. SignOut-requirement zodat user weet dat dit niet live live update

**Shell-host restart logica uitgebreid** in ApplyAsync + ApplyChoiceAsync:
- Was: `needsTaskbarRebind` — restart SearchHost.exe + StartMenuExperienceHost.exe alleen voor Taskbar-categorie of `\Search\` paden
- Nu: `needsShellHostRestart` — ook voor StartMenu-categorie én voor tweaks die het `\Policies\Microsoft\Windows\Explorer` pad raken (HideRecommendedSection / DisableSearchBoxSuggestions). Anders zou een Start-tweak een F5 in Settings nodig hebben om te tonen

**Categories collapsed by default** voor overzicht — was: per-categorie een TextBlock-header met cards eronder direct in beeld; nu: Expander per categorie, default `IsExpanded=false`. Header toont category-naam + "N/M active" count (Enabled + Partial geteld) zodat user direct ziet welke categorieën al deels actief zijn zonder open te klikken. `HorizontalContentAlignment=Stretch` op de Expander zodat cards de volle width pakken

**Diagnostic failure-messages** in `TweakApplyResult` + ResultBar — was: "1 change(s) failed" + "Check tweak state per item" (geen info wat). Nu: `TweakApplyResult.FailureMessages` lijst met per-op `<key>: <reason>` strings. InfoBar toont tot 4 regels met `<tweak.Name> → <key>: <reason>` formaat; bij meer failures: "…en N meer". Helpt diagnose wanneer een specifieke key op een edge-case build niet writable is (UCPD / ACL / type-mismatch / etc.)

**Disable web search in Start: multi-op write** — was: alleen `DisableSearchBoxSuggestions=1` (policy-key, niet consistent gehonoreerd op 24H2+). Nu: 3 HKCU ops in 1 toggle — Explorer-policy + `Search\BingSearchEnabled=0` + `Search\CortanaConsent=0`. Geen UAC (allemaal HKCU). Aggregate-detectie toont nu Partial als user maar 1 van de 3 keys eerder had gezet → zichtbaar dat de tweak alleen deels gedraaid is

**Diagnostic failure-messages** in `TweakApplyResult` + ResultBar — was: "1 change(s) failed" + "Check tweak state per item" (geen info wat). Nu: `TweakApplyResult.FailureMessages` lijst met per-op `<key>: <reason>` strings; InfoBar toont tot 4 regels met `<tweak.Name> → <key>: <reason>`. Bij meer failures: "...en N meer". Help diagnose wanneer een specifieke key op een edge-case build niet writable is (UCPD / ACL / type-mismatch / etc.)

### v0.9.2 — Taskbar category + Search ComboBox + checkbox-batch UX

**UX herontwerp na user-feedback** (per-toggle apply gaf race-condities + breekbaarheid op shell-cached tweaks zoals TaskbarAl). Nieuwe flow:
- **CheckBoxes ipv ToggleSwitches** voor toggle-tweaks; ComboBoxes blijven voor multi-choice (Search-mode)
- **"Apply (N)" knop top-right** — pending changes worden gestaged, niets schrijft tot user klikt. Disabled wanneer N=0, accent-styled met count erop
- **"Discard" knop top-right** (alleen zichtbaar als N>0) — reset alle pending changes terug naar systeem-state via re-detect
- **Apply All flow**: schrijft alle changes sequentieel (Choice via ApplyChoiceAsync, Toggle via ApplyAsync). Per tweak doet WM_SETTINGCHANGE broadcast + SearchHost-restart (voor taskbar). Aan einde één enkele `RestartExplorerSilent()` als minstens één pending tweak `Restart=ExplorerRestart` had. Daarna re-detect + cards rebuild + pending clear
- **`Process.Start("explorer.exe")` weggehaald uit `RestartExplorerSilent`** — opende een File Explorer venster (Documenten / Home). Windows Shell Watchdog respawnt explorer.exe binnen ~1s zonder window. Geen Process.Start fallback meer — als watchdog ooit faalt, user opent Task Manager → Run new task → explorer
- **Manual "Restart Explorer" knop** blijft als laatste escape

**Taskbar tweaks (7)** in TweakService.BuildAll():
- **Hide buttons**: Search box (multi-choice met 4 modes), Task View, Widgets, Copilot
- **Behavior**: End task in right-click menu (`TaskbarEndTask` onder `TaskbarDeveloperSettings` subkey), Never combine taskbar buttons (`TaskbarGlomLevel=2` + multi-monitor companion `MMTaskbarGlomLevel=2`)
- **Display**: Show seconds in tray clock (`ShowSecondsInSystemClock=1`)
- Verwijderd: Chat/MeetNow (deprecated in 23H2+), Battery % (`EstimatedTimeText` is Win10-only)

**Search-tweak als multi-choice ComboBox** (mirror Windows Settings > Personalization > Taskbar):
- 4 modes: Hide / Search icon only / Search box / Search icon and label
- Schrijft 4 keys per mode (legacy `\Search\SearchboxTaskbarMode` + `Cache` + nieuwe `\Advanced\ShowSearchBox` + `BingSearchEnabled`) zodat zowel oude als nieuwe Win11 taskbar code 't pickt
- Auto-restart SearchHost.exe + StartMenuExperienceHost.exe na de write (functioneel equivalent met wat Settings intern via private `TaskbarSettingsHelper` WinRT API doet — die API is niet redistributable, dus process-restart is de pragmatische aanpak die ook winutil / Winaero gebruiken)

**Multi-choice support in het model**:
- Nieuwe `TweakChoice` + `TweakChoiceValue` records — een tweak heeft óf `Operations` (toggle) óf `Choices` (ComboBox), nooit beide
- Tweak constructor variant voor choices, `IsChoice` discriminator, `SelectedChoiceIndex` (INPC) voor live binding
- `TweakService.ApplyChoiceAsync(tweak, choiceIndex)` parallel met bestaande `ApplyAsync(tweaks, apply)` — zelfde local/elevated split + broadcast
- State-detect zoekt eerste choice waar alle Values matchen — bij geen match SelectedChoiceIndex=-1 (custom state)
- TweaksPage rendert automatisch ComboBox voor `tweak.IsChoice` items, CheckBox voor de rest

**Lag fix (kritiek)**: WM_SETTINGCHANGE broadcast deed 5× synchroon `SendMessageTimeout` met 100ms = tot 500ms UI-thread blokkade per toggle. Nu fire-and-forget via `Task.Run(ShellRefresh.NotifySettingsChanged)` na registry-write + state-redetect

**ShellRefresh helper** (per Windows best-practice research):
- `null` lParam toegevoegd vóór de specifieke categorieën — legacy listeners filteren alleen op NULL
- `ShellState` lParam toegevoegd — voor hidden files / show extensions tweaks die nu correct live-refreshen
- `WindowsSearchSettingChanged` + `SearchSettingsChanged` lParams (nieuw in 24H2/25H2)
- `SMTO_NOTIMEOUTIFNOTHUNG` flag erbij — well-behaved windows krijgen onbeperkte tijd, alleen hung windows worden geskipped
- Nieuwe `NotifyAssociationsChanged()` helper (SHChangeNotify SHCNE_ASSOCCHANGED) — automatisch aangeroepen na tweaks die `\Classes\` paden raken (Classic context menu CLSID)
- `RestartSearchHost()` helper — kill SearchHost.exe + StartMenuExperienceHost.exe voor search + taskbar-rebind
- `RestartExplorerSilent()` helper — alleen kill, geen Process.Start fallback (anders File Explorer venster opent)

**Per-tweak RestartRequirement metadata** (None / ExplorerRestart / SignOut / Reboot) wordt gebruikt om te bepalen of de Apply-batch een single explorer-restart triggert aan het einde (zodat alleen wanneer een shell-cached tweak in de pending zit, de flicker plaatsvindt)

**Research bronnen** (mei 2026):
- Win11 22H2+ search-UI gebruikt nieuwe `\Advanced\ShowSearchBox` naast legacy `\Search\Mode` — beide schrijven nodig
- `SearchboxTaskbarModeCache` companion key — Windows valideert Mode tegen Cache bij login en reset Mode als ze niet matchen
- `BingSearchEnabled` als safety net — op 25H2 wordt SearchHost suspended als Bing uitstaat
- TaskbarAl + andere shell-cached tweaks vereisen op 25H2 een explorer-restart (Settings.exe doet 't via private WinRT API niet beschikbaar voor third-party); SearchHost-restart triggert Start menu re-read maar NIET taskbar re-render
- Microsoft heeft Copilot in 25H2 omgebouwd naar pinned Appx app — `ShowCopilotButton` is daar no-op

### v0.9.1 — Tweaks tab foundation + Explorer category

- **Architectuur**: nieuwe `Models/Tweak.cs` met `TweakCategory` enum (12 buckets — Explorer / Taskbar / StartMenu / AdsBloat / AiCopilot / Privacy / UiTheme / Performance / ContextMenu / NotificationsLock / Updates / Gaming), `TweakOperation` record met `EnabledValue` + `DisabledValue` voor full reversibility, `RestartRequirement` enum (None/ExplorerRestart/SignOut/Reboot), `TweakState` enum (Disabled/Enabled/Partial/Unknown), `Tweak` class met INPC voor state-binding. Multi-op support: één tweak kan meerdere registry-ops bundelen (bv OFGB later met 22 keys). Special `DeleteKeyOnAbsent` flag voor tweaks die een hele key-tree aanmaken bij apply (classic context menu CLSID-subkey)
- **`Services/TweakService.cs`**: data-driven `BuildAll()` registreert alle tweak-definities (geen per-tweak UI code). `DetectStatesAsync` walkt elke op's registry-pad en zet `Tweak.State` op Enabled/Disabled/Partial/Unknown door actual vs EnabledValue/DisabledValue te vergelijken. `ApplyAsync(tweaks, apply: bool)` splitst user-ops (HKCU, in-process via Microsoft.Win32.Registry) van elevated-ops (HKLM, 1 UAC voor de hele subset via PowerShell + reg.exe batch — zelfde patroon als DeepCleanService). `RestartExplorerAsync()` static helper voor de "Restart Explorer?" dialog
- **`TweaksPage` rebuild**: was placeholder InfoBar, nu volledige page met per-tweak Border-cards gegroepeerd per categorie. Per card: naam + status-pill (Active/Default/Partial/Unknown met groen/grijs/geel/grijs achtergrond) + omschrijving + use-case (italic), rechts admin/UAC lock-icoon (alleen bij HKLM-tweaks), uiterst rechts `ToggleSwitch`. Loading-overlay met spinner tijdens initial registry-walk. Toggle-event triggert immediate apply/revert + InfoBar-feedback ("Restart Explorer to see the effect" voor explorer-restart tweaks) + optionele "Restart Explorer?" dialog
- **5 Explorer-tweaks geregistreerd**: show file extensions (`HideFileExt`), show hidden files (`Hidden`), taskbar aligned left (`TaskbarAl`), launch File Explorer to This PC (`LaunchTo`), classic context menu (CLSID `{86ca1aa0-…}\InprocServer32` key-add/delete via DeleteKeyOnAbsent)
- **`App.Tweaks` singleton** toegevoegd
- **Geen restart-icoon per card** — initial draft had Refresh/SignOut/Power glyphs naast elke ToggleSwitch, weggehaald omdat 't verwarrend was. Restart-info komt via de InfoBar-message direct na toggle (contextueel duidelijker). Admin/Lock icoon blijft wel voor HKLM-tweaks omdat dat aankondigt dat UAC gaat triggeren (niet-evident anders)

### v0.8.11 — Diagnostic logs gated + multi-badge + empty-state + auto-refresh + dialog search

- **Diagnostic logs uit voor productie**: nieuwe `Helpers/Diagnostics.cs` met `Enabled` static-readonly bool (false in productie). Alle persistent diagnostic logfiles (`SetupToolbox_deepclean.log` / `_leftovers.log` / `_debloat.log` / `_toast.log`) lopen nu via `Diagnostics.Log(fileName, msg)` — no-op wanneer Enabled=false. Geen rommel meer in `%TEMP%` op user-systemen. Voor dev: flip de readonly naar true om de full per-scan trace weer aan te zetten. Load-bearing IPC logs (timestamped per-batch elevated PS-batches voor delete-progress + `_schtasks.log` voor schtasks stderr capture) lopen NIET via deze gate — die hebben hun eigen lifecycle en zijn nodig voor de UI om progress te tonen
- **Multi-badge op gemixte bundles** in `DeepCleanDialog.BuildBundleCard`: voorheen toonde een bundle alleen de category-badge van het eerste item + een generieke "N folders" count-badge. Voor een gemixte bundle (bv. folder + 2× HKCU vendor van dezelfde app) zag user dus alleen "Orphaned folder" als badge, de HKCU-items verborgen in de Expander. Nu: één badge per unieke category in de bundle, met `×N` count-suffix wanneer er meer dan 1 item per category is. Voorbeeld: bundle "Brave" toont nu naast elkaar `Orphaned folder` + `HKCU vendor ×2`. Replacement van de oude dubbele badge (category + folder-count), netto cleaner
- **Empty-state UI op DeepCleanPage**: nieuw `EmptyStatePanel` border met groen `&#xE73E;` checkmark-icon + heading + uitleg. Wordt zichtbaar wanneer een scan 0 items oplevert, i.p.v. de "nothing to clean" InfoBar. Voelt meer als positieve feedback ("looking clean!") dan als een dichtklapbare success-bar
- **Auto-refresh na delete**: nieuwe `isAutoRefresh` parameter op `RunScanAsync`. Na een succesvolle delete-batch (`SuccessCount > 0`) triggert de page automatisch dezelfde scan-kind opnieuw als verify-pass. Drie outcomes: (1) 0 items remaining → empty-state met "cleanup verified" heading, success-InfoBar van de vorige delete blijft staan, (2) items remaining → warning-InfoBar "N items still present after cleanup", geen automatische tweede dialog (anders wordt het opdringerig na een failed delete), (3) auto-refresh roept geen nieuwe auto-refresh aan (geen loop)
- **Search box in DeepCleanDialog**: nieuwe `AutoSuggestBox` bovenaan de dialog (boven de items-lijst). Filtert bundles + items op DisplayName, Path én CategoryLabel — case-insensitive substring + genormaliseerde fallback (alfanum-only) zodat "bravesoftware" ook matcht met `BraveSoftware · Promo` (de `·` separator wegfilteren via Normalize). Bij actieve filter zonder matches toont de lijst "Geen items matchen \"...\"". Selecties op out-of-filter items blijven gerespecteerd — IsSelected zit op het DeepCleanItem model dus persist tussen filter-changes. Use case: user heeft 143 items in de dialog (8+ categorieën, veel orphaned folders), zoekt "brave" → ziet direct alleen de Brave-cluster met folder + 2 HKCU vendor entries gegroepeerd

### v0.8.10 — Orphan services + HKCU vendor-residue

- Twee high-risk leftover-types toegevoegd aan deep clean, beide met strikt filter zodat we geen system-state per ongeluk raken
- **Orphan services**: nieuwe `OrphanedService` `DeepCleanCategory` + `DeepCleanService.ScanOrphanedServicesAsync`. Roept `Get-CimInstance Win32_Service` aan via base64-encoded PS-script, parst pipe-separated `Name|DisplayName|State|StartMode|PathName` lines. Strikt 6-criterium filter: (a) ImagePath dood (na `Environment.ExpandEnvironmentVariables` + `ExtractExePathFromCommandLine`), (b) State=Stopped, (c) StartMode=Manual of Disabled (geen Auto/Boot/System), (d) PathName start niet met svchost.exe (DLL-hosted services), (e) ImagePath niet onder `%SystemRoot%` of `C:\Windows\`, (f) geen overlap met winget/AppX token cross-set. Service-name (NIET DisplayName) opgeslagen in `RegistryValueName` voor sc.exe-delete dispatch. Default unchecked + caution-tier
- **HKCU vendor**: nieuwe `OrphanedHkcuVendor` category + `DeepCleanService.ScanOrphanedHkcuVendorAsync`. Walk `HKCU\Software\<Vendor>\<App>` (top 2 levels). Per app-key zoek pad-values: value-name in vaste set (`InstallPath`, `InstallDir`, `InstallLocation`, `Path`, `Program`, `ProgramPath`, `ExecutablePath`, `Executable`, `Exe`, `ExePath`, `AppPath`, `InstallationDirectory`, `Location`, `AppDir`, `BinPath`, `BinaryPath`, `RootDir`, `WorkingDirectory`) ÓF value-data lijkt op drive-rooted path (`X:\...` of `%EnvVar%\...`). Orphan-flag alleen als ≥1 pad-value gevonden EN alle dood EN geen winget/AppX cross-match. Protected top-level vendor-keys altijd skipped: Microsoft, Classes, Policies, Wow6432Node, RegisteredApplications, Clients, driver-vendors (Intel/AMD/NVIDIA/Realtek), Google, Mozilla (eigen uninstallers). IsSafe=false, default unchecked
- `DeepCleanService.DeleteAsync` uitgebreid: services altijd in elevated batch (sc.exe delete vereist admin), HKCU vendor-keys via in-process `DeleteSubKeyTree` (HKCU = user-context, geen UAC nodig). Bestaande `DeleteRegistryKey` werkt al voor HKCU dus extra category gewoon toegevoegd aan de switch
- `RunElevatedBatchAsync` PS-batch: nieuwe branch voor `OrphanedService` met `& sc.exe delete '<name>' | Out-Null` + `$LASTEXITCODE` check + RESULT-line log
- `DeepCleanPage.RunScanAsync` Leftovers-tak uitgebreid van 8 naar 10 parallel scans (services + HKCU vendor toegevoegd). Per-categorie breakdown counts in `CleanupResultBar` (`{n} services`, `{n} HKCU vendor keys`). XAML-beschrijving op de "Scan leftovers" card vermeldt nu beide types expliciet zodat user weet wat de knop doet
- `DeepCleanDialog.PathLink_Click`: `OrphanedService` → `services.msc` (Services console — niet rechtstreeks navigeerbaar naar één service, alleen open), `OrphanedHkcuVendor` → regedit op de HKCU-key via bestaande `OpenInRegedit` helper
- `DeepCleanService.GetOrphanedScanLocations()` voegt vier soft-categorie labels toe (registry+App Paths+MUIcache+class handlers / shortcuts / scheduled tasks+firewall / services+HKCU) zodat de dialog scan-locations-tekst dekkend is

### v0.1.0 — Sandbox foundation
- WinUI 3 project + solution-koppeling, unpackaged exe (`WindowsPackageType=None`)
- MainWindow met native Fluent TitleBar, content stack, ToggleSwitch, Accent button, SymbolIcon, InfoBar
- DesktopAcrylicController met `DesktopAcrylicKind.Thin` via WindowsSystemDispatcherQueueHelper
- Build + run verified op .NET 10

### v0.2.0 — NavigationView shell
- Sidebar Apps / Tweaks / Debloat / Settings (WinUI 3 Gallery patroon)
- Drie lege Pages als routing targets
- Settings via `FooterMenuItems`
- AppTitleBar drag regions afgestemd

### v0.3.0 — Apps pagina met categorieën
- Categorie-data uit `apps.json` ingelezen
- Settings-style category rows (i.p.v. kleurige tile grid)
- Click → detail page met app list + selectie

### v0.3.1 — MicaBackdrop polish
- MicaBackdrop BaseAlt + simplified backdrop code

### v0.4.0 — Install flow + winget
- WingetService geport
- InstallDialog met Fluent ProgressBar + log
- ScheduleDialog voor auto-update

### v0.4.1 — Install UX polish + installed-state
- InstallDialog redesign (overall progress bar weg, per-app indeterminate bar, live winget stdout)
- DispatcherQueue-marshalling voor `Progress<T>` callbacks
- Expliciete `Visibility`-typed binding properties
- `WingetService.GetInstalledAppIdsAsync()` parseert `winget list`, gecachet
- "Installed" badge in CategoryDetailPage

### v0.4.2 — Stage progress per app
- Pending: kleine ProgressRing (20px, spinning)
- Installing zonder %: indeterminate bar; met %: determinate bar uit "X MB / Y MB" parser
- Card-based layout, theme-aware success/error brushes

### v0.4.3 — Quick uninstall onder Debloat (tijdelijk)
- Lijst van geïnstalleerde catalogus-apps onder Debloat tab
- Per app uninstall-button met bevestigings-dialog
- `WingetService.UninstallAppAsync()` via `winget uninstall --silent`
- Wordt in v0.8.0 vervangen door full Debloat-pagina

### v0.4.4 — Stage ring + Fluent polish
- 4-stage ProgressRing rechts per app: Downloading → Verifying → Installing → Done
- Indeterminate bar altijd zichtbaar tijdens install (= "er gebeurt iets")
- Char-per-char stream reader die ook op `\r` splitst (winget gebruikt carriage returns voor live progress)
- ContentDialog sizing via resource keys + afgeronde hoeken via OverlayCornerRadius

### v0.4.5 — Schedule dialog
- TaskSchedulerService geport (WinUI-specifieke taaknaam)
- `WingetService.UpdateAllAppsAsync()` via `winget upgrade --all --silent`
- `/autoupdate` command-line handler in App.xaml.cs (runt update + Environment.Exit zonder window)
- ScheduleDialog met Daily/Weekly/OnStartup + TimePicker
- SettingsPage status-card met Set up / Change / Disable

### v0.5.1 — Catalog search + globale selectie footer
- AutoSuggestBox in AppsPage (filter categorie-grid) en CategoryDetailPage (filter app-lijst)
- Match op categorie-naam + app-naam + beschrijving + winget ID. Category-match slaat ook aan wanneer een app in die category matcht
- `SelectionHelper` service voor cross-category selectie (count / clear / collect)
- Footer met "X apps selected" + Clear all + Install knop op AppsPage én CategoryDetailPage, beide tonen globale count
- Install pakt alle selected apps over álle categorieën

### v0.5.2 — Winget-repo search + klikbare cards
- `WingetService.SearchWingetRepoAsync` parst `winget search` output, debounced 300ms met epoch-check
- "Results from winget repository" sectie op AppsPage onder catalog-results
- `SelectionHelper.ExtraSelectedApps` voor synthetische winget-search apps (tellen mee in footer + install)
- Search-modus: categorie-grid weg, platte "In your curated list" + winget secties
- Klik op sidebar Apps reset naar root (clear search, leave detail page) via `ItemInvoked`
- Hele app-card klikbaar (`Tag="{x:Bind}"`, CheckBox `IsHitTestVisible=False`)
- Hover-effect via `PointerEntered/Exited` → `CardBackgroundFillColorSecondaryBrush`
- Padding rechts van scrollbar zodat cards niet aan de rail plakken

### v0.5.3 — Fuzzy search
- FuzzySharp 2.0.2 NuGet
- `Helpers/FuzzyMatcher.cs` met `WeightedRatio` (token-set + partial + full combi), threshold 55/100
- Exacte case-insensitive substring shortcut naar score 100
- Resultaten gesort op score DESC, naam als tie-breaker
- Toegepast op AppsPage catalog-results en CategoryDetailPage filter (winget-repo blijft ongefuzzyt)

### v0.5.5 — Local-only apps.json
- Distributie-model: private repo + public GitHub Releases
- `data/apps.json` wordt gebundeld met de exe (csproj `<Content>` + PreserveNewest)
- AppDatabaseService gestript van HttpClient + remote URL — leest alleen bundled file
- Werkt offline, geen auth-tokens, geen GHSAT-tokens die vervallen

### v0.5.6 — Subcategorie grouping
- `SubcategoryGroup` model (UI-only) met Name + Apps + HasName Visibility
- Nested ItemsRepeater (outer = groepen, inner = apps per groep)
- Categorieën zonder subcats krijgen één lege-name groep zonder header
- Search filtert per groep, lege groepen na filter onzichtbaar
- Select all werkt op zichtbare apps in álle visible groepen

### v0.5.7 — INotifyPropertyChanged op App
- INPC op `IsSelected` en `IsInstalled` met `[CallerMemberName]` helper
- Verwijderd alle `ItemsSource = null; ItemsSource = ...` rebind hacks — TwoWay x:Bind reageert nu automatisch
- Sneller (geen full card-rebuild per click), geen valse hover-events op buren

### v0.5.8 — Modern ScrollView + 20ms scroll
- `ScrollView` (modern WinUI 3) i.p.v. `ScrollViewer` (legacy UWP-erfgenaam) → werkt via Parsec en andere remote desktop tools
- `Helpers/ScrollViewSpeedup.cs` zet scroll-animation duration op 20ms (default ~350ms voelt traag)
- Toegepast op CategoryDetailPage, AppsPage, DebloatPage, SettingsPage

### v0.5.10 — Strictere fuzzy search
- `FuzzyMatcher` vervangt `WeightedRatio` door substring → prefix(90) → `PartialRatio` ladder. WeightedRatio's token_set matchte anagrammen ("steam" ↔ "teams" / "signal" / "keepass")
- `MinScore` 55 → 75
- `Description` niet meer mee-gescoord op AppsPage en CategoryDetailPage — alleen naam + winget ID

### v0.5.11 — Filter opties op CategoryDetailPage
- `ComboBox` met All / Popular / Installed naast de SearchBox
- Filter-mode chained mét fuzzy search: eerst mode-filter (`Popular` → `App.Popular`, `Installed` → `App.IsInstalled`), dan optioneel fuzzy zoekquery
- Lege subcat-headers verdwijnen zodra een filter actief is (geen kale section-headers meer)
- Mode-aware "no results" message: "No popular apps in this category" / "No installed apps in this category matching 'X'"
- Installed-filter refresht automatisch wanneer `winget list` async binnenkomt
- `_uiReady` guard voorkomt dat `SelectionChanged` (vuurt al tijdens XAML-parse via `IsSelected="True"`) een redundante render-cycle triggert vóór `OnNavigatedTo` de UI heeft opgezet

### v0.8.6 — Deep clean (los van uninstall)

- Nieuwe `Models/DeepCleanItem` met `DeepCleanCategory` enum (UserTemp / SystemTemp / UpdateCache / Prefetch / RecycleBin / WindowsOld / BrowserCache / OrphanedFolder). Properties: DisplayName, Path, SizeBytes, LastModified (alleen voor orphaned folders), RequiresElevation, IsSafe (true voor Temp / Recycle / Update / Prefetch — default checked, content > 0; false voor caution categorieën — default unchecked). UI helpers voor category-badge, size-label, last-modified-label
- Nieuwe `Services/DeepCleanService` met twee scan-flows: `ScanWindowsCachesAsync` (predefined paden — Temp folders, Update cache, Prefetch, Windows.old, browser caches voor Edge / Chrome / Brave + alle Firefox profiles, Recycle Bin via shell) parallel met size-walk per target, en `ScanOrphanedFoldersAsync` die `InstalledAppsService.DetectAllAsync` gebruikt om een normalized name-set te bouwen en folders in Program Files / Program Files (x86) / %LOCALAPPDATA% / %APPDATA% / %PROGRAMDATA% flag die NIET matchen met enige installed app. Brede protected-list (Microsoft / Windows / WindowsApps / Common Files / Packages / Temp / etc.) voorkomt dat we vendor-of-system folders als orphan voorstellen
- `DeepCleanService.DeleteAsync` splitst in user-context (HKCU AppData, user temp, browser caches) en elevated batch (system temp / Update cache / Windows.old / Program Files / ProgramData / Recycle Bin via `Clear-RecycleBin`). Eén UAC prompt voor de hele admin-required subset. Cache-folders krijgen een "clear contents" delete (folder zelf blijft staan want Windows / browsers maken 'm opnieuw aan), orphaned folders krijgen een volledige `Remove-Item -Recurse -Force` delete. Bytes-freed wordt geretourneerd zodat de UI "X MB freed" kan tonen
- Nieuwe `Dialogs/DeepCleanDialog` mirror van LeftoverCleanupDialog met preview + delete fases, items gegroepeerd op IsSafe-tier (Safe to clean → Caution — review carefully), per item: checkbox + naam + category badge + path + omschrijving + size + admin-marker + last-modified (alleen orphaned). Footer toont totaal-vrij-te-maken-ruimte zodra user iets aanvinkt. Caution-tier items hebben gele rand, safe-tier groene rand
- Sidebar-restructure: NavigationView "Debloat" wordt nu een parent met `SelectsOnInvoked="False"` + `IsExpanded="True"` en twee sub-items: **Apps** (de bestaande MS / OEM / All apps flows op DebloatPage) en **Deep clean** (nieuwe DeepCleanPage). Klik op de parent klapt de groep in/uit zonder zelf te navigeren — sub-items hebben hun eigen Tag → page mapping in MainWindow.NavView_SelectionChanged. Deep clean kreeg een eigen pagina i.p.v. een in-page Expander zodat de pagina-titel + InfoBar + scan-cards eigen ruimte krijgen
- `App.DeepClean` singleton toegevoegd
- Diagnostic log per scan in `%TEMP%\\SetupToolbox_deepclean.log` met per-target size-trace + per-folder match-decision

### v0.8.5 — Restant-opruiming direct na uninstall

- Nieuwe `Models/LeftoverItem` met `LeftoverType` enum (`RegistryKey` / `ProgramFilesFolder` / `AppDataFolder`) en `LeftoverConfidence` (`High` / `Medium` / `Low`). Confidence bepaalt of het item default aangevinkt staat in de cleanup-dialog: high = checked (exact-match), medium/low = unchecked. Properties: Path, SourceAppName, SizeBytes (lazy folder-walk), RequiresElevation. UI helpers voor type-badge + size-label
- Nieuwe `Models/UninstalledAppRef` record (DisplayName + Publisher + PackageName + WingetId) als input voor de scanner. Lichtgewicht alternatief voor het meegeven van zware UI-models — scanner heeft genoeg aan deze 4 velden om matches te vinden
- Nieuwe `Services/LeftoverScannerService` scant drie locatie-types parallel: (1) registry uninstall keys (HKLM 64-bit + WOW6432Node + HKCU) — match op DisplayName + Publisher, (2) Program Files / Program Files (x86) folder-namen, (3) `%LOCALAPPDATA%` / `%APPDATA%` / `%PROGRAMDATA%` folders. Match-tier-systeem: exact-na-normalisatie = high, substring-bidirectional = medium (skipt korte namen om vendor-collisions zoals "MS"/"HP" te voorkomen), publisher-only = low. Protected-list voor AppData-folders die we nooit voorstellen (`Microsoft`, `Windows`, `Packages`, `Temp`, `WindowsApps`, `INetCache` etc.) zodat een Microsoft-bloatware uninstall niet de hele `%LOCALAPPDATA%\Microsoft` map suggereert. Diagnostic log per scan in `%TEMP%\\SetupToolbox_leftovers.log` met per-item match-trace voor debugging
- `LeftoverScannerService.DeleteAsync` splitst per RequiresElevation: HKCU + AppData (user) gaan in-process via `Registry.DeleteSubKeyTree` / `Directory.Delete`; HKLM + Program Files + ProgramData gaan in één elevated PS-batch met `reg.exe delete /f` of `Remove-Item -Recurse -Force`. Eén UAC prompt voor de hele admin-required subset, log-tail-pattern voor result-parsing zoals BloatwareService / MixedSourceUninstaller
- Nieuwe `Dialogs/LeftoverCleanupDialog` met preview-fase + delete-fase. Preview groepeert items per LeftoverType (Registry → Program Files → AppData), per item: checkbox + path + size + confidence-label + "from <app>" badge + admin-marker. Confidence-tier kleurt de border subtiel (high = success-green, medium/low = neutral). "Select all" toggle + selection-status footer ("X selected · Y need administrator rights"). Delete-fase swap UI naar progress-bar + status-tekst, na voltooiing wordt Primary een Close-knop met summary. **Always preview, never auto-delete** — secondary "Skip" sluit zonder iets te verwijderen
- `SettingsService.ScanLeftoversAfterUninstall` (default true) + nieuwe **"Uninstall"** sectie op SettingsPage met ToggleSwitch. Wanneer false: na uninstall geen scan, geen dialog — user kan handmatig nog v0.8.6 deep-clean draaien
- DebloatPage: `ConfirmAndRemoveBloatwareAsync` (Microsoft + OEM bloatware) en `InstalledUninstallButton_Click` (unified all-apps) triggeren na succesvolle uninstall een `RunLeftoverScanAsync` op de **SuccessfulItems** uit de dialog (nieuwe property op zowel BloatwareUninstallDialog als AllAppsUninstallDialog). Failed/cancelled items hebben hun sporen nog gewoon op disk staan en zijn dus geen leftover — alleen apps die echt weg zijn voeren in de scan. UninstalledAppRef-bouwer voor InstalledAppEntry mapt source-afhankelijk: Store krijgt PackageName uit het eerste segment van PackageFullName, Winget krijgt WingetId, Web heeft alleen DisplayName + Publisher als hint
- `App.LeftoverScanner` singleton toegevoegd

### v0.8.4 — Unified all-installed-apps sectie

- Nieuwe `Models/InstalledAppEntry` met `InstalledSource` enum (`Winget` / `Store` / `Web`). Properties: DisplayName, Identifier (winget ID / PackageFullName / registry key path), Publisher, Version, IsSelected (INPC), `IsSystemComponent` flag, source-aware UI helpers (badge text + brush + tooltip, IconVisibility, GenericIconVisibility, SystemBadgeVisibility, Subtitle). Voor Winget-apps die ook in apps.json staan houden we een referentie naar de App-instance zodat we het bundled icon kunnen tonen; voor Store/Web (en Winget zonder catalog match) tonen we een generieke OEM-icon glyph. Source-namen: `Winget` = winget kan de app managen (Source-kolom uit `winget list`), `Store` = Microsoft Store / AppX, `Web` = vendor-installer download (MSI/EXE niet bekend bij winget of Store)
- Nieuwe `Services/InstalledAppsService` detecteert uit drie bronnen parallel via `Task.WhenAll`: (1) `winget list` (deelt cache met `WingetService` — één gezamenlijke call i.p.v. twee), gefilterd op `Source=winget` óf catalog-match — entries met `Source=msstore` / leeg vallen door naar AppX/Registry detectie, (2) `Get-AppxPackage` voor Microsoft Store / AppX met framework + resource packages eruit gefilterd, system-AppX (`SignatureKind=System`) wordt mét `IsSystemComponent` flag bewaard zodat user ze achter de "Show system components" checkbox kan tonen, (3) registry uninstall keys (`HKLM\\SOFTWARE\\...\\Uninstall` 64-bit + 32-bit `WOW6432Node` + `HKCU` equivalent), filtert Windows Updates / hotfixes / SystemComponent eruit. Cross-source dedup op DisplayName met prioriteit Winget > Store > Web. Diagnostic log per refresh in `%TEMP%\\SetupToolbox_debloat.log` met per-bron count + duration zodat detectie-issues debugbaar zijn
- `Services/BloatwareService` omgebouwd naar **detection-driven** i.p.v. hardcoded curated lijst — gebruikt dezelfde `Get-AppxPackage` call en classificeert via vendor-patronen (Microsoft.* + MicrosoftCorporationII.* + MSTeams voor Microsoft, publisher CN= patterns + PFN-prefixes voor HP/Dell/Lenovo/ASUS/Acer/MSI). Curated `BloatwareItem.CuratedMetadata` dict (~50 entries) verrijkt bekende packages met friendly DisplayName + Description; onbekende krijgen de raw package-name. Voorkomt dat we hardcoded bloatware lists moeten onderhouden ("anders kan je aan de gang blijven")
- Nieuwe `Services/MixedSourceUninstaller` runt een gemengde batch met per-source dispatch: Winget-items via bestaande `WingetService.UninstallAppAsync` sequentieel (geen UAC), Store + Web items in één gecombineerde elevated PS-batch zodat user maar één UAC prompt ziet voor alle admin-required items. `MsiExec /X{GUID}` patronen krijgen `/quiet /norestart` toegevoegd zodat MSI-uninstalls silent lopen — non-MSI installers vertrouwen op hun eigen `QuietUninstallString` (anders krijg je gewoon de installer's UI, dezelfde caveat als bij v0.8.1)
- Nieuwe `Dialogs/AllAppsUninstallDialog` consumeert `MixedUninstallProgress` events met source-badge per card, gecombineerde "X of Y done" header, UAC-hint die alleen verschijnt als de batch ook elevated items bevat (pure Winget-batches niet). Cancelled-state bij UAC denial (zelfde patroon als v0.8.2 BloatwareUninstallDialog)
- DebloatPage: v0.8.1 catalog-sectie **vervangen** door unified "All installed apps" sectie. Microsoft + OEM bloatware secties blijven als curated quick-actions. Nieuwe sectie heeft `AutoSuggestBox` voor fuzzy search (op DisplayName + Publisher + winget ID voor Winget-source), `ComboBox` filter (All sources / Winget / Store / Web / **System**) en "Show system components" checkbox. **Sortering**: zonder search-query gegroepeerd op source (Winget eerst → Store → Web) en alfabetisch binnen elke groep — echte managed apps bovenaan vóór de Store/Web bagger. System-filter toont enkel system components ongeacht checkbox. Triple dedup tussen bloatware-secties en unified lijst (PackageFullName + AppX Name uit FullName + DisplayName) zodat een Store-app niet in Microsoft-bloatware én All-apps verschijnt. Counts in header reflecteren huidige zichtbaarheid (system-toggle + filter aware)
- `WingetService` shared cache: één `_appsListCache` + `_installedIdsCache` achter `SemaphoreSlim` zodat de Source-kolom uit `winget list` één keer geparset wordt en zowel `GetInstalledAppIdsAsync` als `GetInstalledAppsListAsync` ervan kunnen lenen. ParseListOutput skipt `ARP\\` / `MSIX\\` prefix entries (laat door naar Registry/AppX detectie), ParseSimpleIds fallback voor non-English Windows headers
- `App.InstalledApps` + `App.MixedUninstaller` singletons toegevoegd. Bloatware-uninstall en unified-uninstall refreshen elkaar's lijsten — Store-apps die in beide voorkomen blijven consistent

### v0.8.3 — OEM bloatware sectie + counts in section-headers

- Nieuwe `BloatwareVendor` enum (`Microsoft` / `Oem`) op `BloatwareItem`. Bestaande Microsoft-items behouden hun gedrag, nieuwe OEM curated lijst (~17 items) toegevoegd voor HP / Dell / Lenovo / ASUS / Acer / MSI bundleware. Multi-package items per vendor (varianten van dezelfde app onder verschillende publisher-prefixes — bv. HP Smart als `AD2F1837.HPSmart` of `HPInc.HPSmart` afhankelijk van Win11-versie) onder één checkbox. Helper `BloatwareItem.CuratedFor(vendor)` filtert de unified curated bron per sectie
- Nieuwe **OEM bloatware** sectie op DebloatPage tussen Microsoft en Catalog. Volledig collapsed wanneer geen OEM-items gedetecteerd — meeste users hebben geen HP/Dell/Lenovo AppX-bloat en zouden anders alleen een lege sectie zien. Detectie loopt in dezelfde `Get-AppxPackage` call als Microsoft (niet 2× duren) — Microsoft-prefixes (`Microsoft.*`) en OEM-prefixes (`HPInc.*` / `DellInc.*` / `LenovoCorporation.*` / `AsusTekComputerInc.*` / `AcerInc.*` / `MSI.*`) zijn disjunct dus geen kruisverontreiniging
- **Counts in section-headers**: alle drie de secties (Microsoft / OEM / Catalog) tonen nu een grijze count naast de titel — `"Microsoft bloatware (5)"` / `"OEM bloatware (2)"` / `"Installed apps from this catalog (12)"`. Lege count = geen tekst zodat het niet als "(0)" een lege sectie suggereert
- Refactor van DebloatPage card-template: Microsoft + OEM secties delen nu dezelfde `BloatwareCardTemplate` resource zodat we 'm niet hoeven te dupliceren. `BloatwareCard_Tapped` is shared en routeert naar de juiste selection-update via `BloatwareItem.Vendor`. `ConfirmAndRemoveBloatwareAsync(items, vendorLabel)` is gedeeld tussen Microsoft + OEM Remove buttons — zelfde confirm dialog + UninstallDialog flow, alleen de title-tekst varieert
- Windows11-Unattended-Debloat integratie (registry-tweaks / scripts) **niet** in v0.8.3 — past logischer bij v0.9.x Tweaks tab en zou hier alleen scope-creep zijn

### v0.8.2 — Microsoft bloatware section op DebloatPage

- Nieuwe `Models/BloatwareItem.cs` met INPC + curated lijst van ~22 Microsoft AppX bloatware items (Solitaire, Xbox suite, Skype, Teams consumer, Mail/Calendar, Bing News/Weather, Cortana, Mixed Reality, 3D Viewer, Paint 3D, Get Help, Tips, Feedback Hub, Office Hub, Maps, OneNote, Groove Music, Movies & TV, Sticky Notes, Phone Link, People). Per item: DisplayName, Description (waarom is dit bloat / wanneer is het misschien wel handig), Category, en lijst PackageNames om tegen `Get-AppxPackage` te matchen. Multi-package items (Xbox = 7 packages) onder één checkbox zodat de hele suite in één klik weg kan
- Nieuwe `Services/BloatwareService.cs`. `DetectInstalledAsync` runt `Get-AppxPackage` (normale user, base64-encoded PS-script om escaping issues te omzeilen) en parst Name + PackageFullName per regel pipe-separated. Vult `BloatwareItem.IsInstalled` + `InstalledPackageFullNames` zodat de UI weet wat te tonen en de uninstall later weet welke FullName te targeten. `UninstallBatchAsync` schrijft een PowerShell-script naar `%TEMP%` dat per item `Remove-AppxPackage` runt en per actie een marker (PROGRESS / RESULT) naar een logfile schrijft. Run via `Process.Start(verb="runas")` zodat één UAC prompt de hele batch dekt; tijdens de run wordt de logfile gepolld zodat de dialog live progress kan tonen ondanks dat verb=runas stdout-redirect uitsluit. Eindresultaat (success/failure per item) wordt na proces-exit uit dezelfde logfile gelezen en als `BloatwareUninstallResult` teruggegeven
- Nieuwe `Dialogs/BloatwareUninstallDialog.xaml` + `.xaml.cs`. Spiegel van UninstallDialog maar consumeert `BloatwareProgress` events i.p.v. `UninstallProgress`. Header toont "X of Y done" tijdens de batch + UAC-hint die verdwijnt zodra de batch klaar is. Per-item: ProgressRing tijdens Pending, indeterminate ProgressBar tijdens Running, checkmark/error op terminal state, label "Removed" / "Failed". Final flush sync-t state nog een keer voor het geval dat een progress-tick miste tussen log-poll cycles
- `DebloatPage` gerefactored: voorheen één lijst, nu twee secties in een ScrollView. Sectie 1 = "Microsoft bloatware" (alleen items die daadwerkelijk geïnstalleerd zijn — anders ruis), Sectie 2 = bestaande catalog uninstall lijst. Elke sectie heeft eigen "Select all" + accent "Remove/Uninstall selected" knop in de header. Beide secties laden parallel via `Task.WhenAll` zodat de pagina sneller responsive is (Get-AppxPackage en winget list zijn beide ~1-2s, sequentieel zou 3-4s zijn). Globale Refresh-knop rechtsboven herlaadt beide
- `App.Bloatware` singleton toegevoegd naast de bestaande `App.Database` / `App.Winget` etc.
- v0.8.4 entry in NEXT-STEPS.md uitgebreid: was eerder alleen "ALLES op de PC search" — nu expliciet beschreven als de unified "alle geïnstalleerde apps van álle bronnen" lijst (registry uninstall keys + Get-AppxPackage + winget list, source-tag per card, filter ComboBox, dispatch per source). User-feedback: deze feature mag niet vergeten worden, behoort uiteindelijk de v0.8.1 + v0.8.2 lijsten te vervangen

### v0.8.1 — Bulk uninstall + UninstallDialog

- Nieuwe `App.IsSelectedForUninstall` (INPC) los van `IsSelected` zodat de Debloat-pagina selectie-state niet kruist met de install-selectie van AppsPage / CategoryDetailPage. Beide pagina's gebruiken dezelfde App-instances dankzij `AppDatabaseService` caching, dus zonder aparte flag zou een Debloat-checkbox de install-footer count vervuilen
- `WingetService.UninstallAppsAsync(IReadOnlyList<App>, IProgress<UninstallProgress>)` — sequential batch met per-app progress events, mirror van `InstallAppsAsync` API. Sequential by design omdat parallel uninstall meer kans geeft op Windows Installer locks (MSI-engine = single-instance) zonder noemenswaardig snelheidsvoordeel — uninstall is sowieso snel. Nieuwe `UninstallProgress` record + `UninstallPhase` enum (Pending / Running / Success / Failed)
- Nieuwe `Dialogs/UninstallDialog.xaml` + `.xaml.cs` als spiegel van `InstallDialog`. Geen 4-stage ring zoals bij install (geen Downloading/Verifying/Installing — uninstall is één action), wel: ProgressRing tijdens Pending, indeterminate ProgressBar tijdens Running, checkmark op Success, error glyph op Failed. Per-app live message, header met "X of Y done" tijdens batch, summary "X uninstalled, Y failed" bij voltooiing. `HadSuccessfulUninstall` property zodat de page kan reageren op een geslaagde batch
- `DebloatPage` herontworpen: card-based lijst (mirror van CategoryDetailPage card layout — icon + naam + winget ID + checkbox), Tapped op de hele card toggelt selectie (CheckBox `IsHitTestVisible=False`), hover-effect via `CardBackgroundFillColorSecondaryBrush`. Footer met selection count + Clear all + Uninstall button, plus Select all toggle in de toolbar. Confirm ContentDialog ("Uninstall N apps?") voor de batch start. `OnNavigatedFrom` cleared `IsSelectedForUninstall` zodat een vergeten selectie niet later terug-popt
- `SetupToolbox.csproj` krijgt nu wel een `<Version>` / `<AssemblyVersion>` / `<FileVersion>` zodat exe metadata en assembly version mee-bumpen per release. Eerste set op 0.8.1

### v0.7.8 — Toast notificatie fix via Microsoft.Toolkit.Uwp.Notifications
- v0.7.7's `Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Register()` faalde silent op unpackaged WinUI 3 apps met `COMException: Class not registered` — vereist een COM activator class die WinAppSDK 1.8 niet auto-registreert
- Switch naar `Microsoft.Toolkit.Uwp.Notifications` 7.x (NuGet `Microsoft.Toolkit.Uwp.Notifications`). `ToastNotificationManagerCompat` doet bij eerste `ToastContentBuilder().Show()` automatisch de AUMID-registratie in HKCU op basis van het exe-pad — geen COM activator class of Start Menu shortcut nodig. Werkt out-of-the-box voor unpackaged Win32/WinUI apps
- `Helpers/ToastHelper.cs` herschreven met `ToastContentBuilder`. Geen `Register()` call meer in App constructor — registratie is implicit bij Show
- Nieuw `/toasttest` debug command-line switch in App.xaml.cs voor snelle dev-verificatie zonder eerst `winget upgrade --all` (~30-60s) te wachten. Toont meteen de success-toast en exit
- Diagnostic logfile in `%TEMP%\SetupToolbox_toast.log` met Show()-resultaat (OK / exception). Aangetoond effectief tijdens debug van het v0.7.7 issue
- Bekend issue: `System.Drawing.Common` 4.7.0 transient dep van het toolkit-pakket heeft een NU1904 vulnerability warning. Niet uitbuitbaar via toast-content (we passen geen images aan via System.Drawing). Wordt opgelost zodra toolkit een nieuwere versie release of we naar WinAppSDK's eigen API switchen wanneer die voor unpackaged apps gerepareerd is

### v0.7.7 — Toast notificatie na /autoupdate
- Nieuwe `Helpers/ToastHelper.cs` met `ShowAutoUpdateResult(bool success)`. Gebruikt `Microsoft.Windows.AppNotifications.AppNotificationManager` + `AppNotificationBuilder` uit WinAppSDK 1.4+. Voor unpackaged WinUI 3 vereist `AppNotificationManager.Default.Register()` één keer voor de eerste Show — registreert AUMID + COM activator in HKCU. Best-effort: try/catch op zowel Register als Show zodat een geweigerde notificatie de auto-update flow nooit kan crashen
- `App.xaml.cs` `/autoupdate` handler captured nu de bool result van `UpdateAllAppsAsync()`, post een toast ("All apps have been updated." / "Update finished with errors. Open the app for details.") en wacht 1.5s voor `Environment.Exit(0)` zodat het OS tijd heeft om de toast door te geven voor het proces stopt
- Use case: scheduled task draait stil in achtergrond, user ziet via Action Center (Win+A) dat de update gelopen heeft. Geen window, geen interruptie — pure feedback

### v0.7.6 — First-time prompt voor parallel installs
- Nieuwe `Helpers/ParallelInstallsPrompt.cs` met static `MaybeShowAsync(XamlRoot)`. Toont 1× een ContentDialog "Install apps in parallel?" wanneer user op Install klikt en `ParallelInstallsAsked` nog false is. 2 knoppen: **Yes, install faster** (Primary, accent) → zet `ParallelInstalls = true`, **No, one at a time** (Close, neutral) → zet `ParallelInstalls = false`. Beide zetten `ParallelInstallsAsked = true` zodat de vraag nooit meer terugkomt
- Nieuwe `SettingsService.ParallelInstallsAsked` setting (default false). Bedoeld voor users die niet zelf naar Settings navigeren — bewuste keuze tijdens hun eerste install
- Aangeroepen vanuit `AppsPage.InstallButton_Click` én `CategoryDetailPage.InstallButton_Click` voor de InstallDialog opent. User kan in Settings altijd nog wisselen via de bestaande toggle

### v0.7.5 — Parallel installs + msstore snelheidsfix
- Nieuwe `SettingsService.ParallelInstalls` setting (default `false`) + ToggleSwitch "Run installs in parallel" in de Installation sectie van SettingsPage. Caption waarschuwt: "Install up to 2 apps at the same time. Roughly halves install time, but some MSI installers fail when run concurrently". Bij testing bevestigd: msstore apps in parallel werken vlot, maar twee MSI-based installers (bv. Brave + Chrome) blokkeren elkaar via de Windows Installer single-instance lock — fundamentele platform-beperking, geen bug
- `WingetService.InstallAppsAsync` parallelism via `SemaphoreSlim`. `ConcurrentDictionary` voor results, daarna terug-orderen op input volgorde voor deterministic UI summary. Hard-cap op 4 parallel (`Math.Clamp`) — meer dan 2 sowieso te risky op typische Windows-machines
- `InstallDialog`: header trackt nu `_completedCount` (incremented op Success/Failed phase) i.p.v. flickerende `CurrentIndex` per progress event. Format: `"Installing — 3 of 8 done (2 in parallel)"`. Sequential mode toont dezelfde count-based text zonder de parallel-suffix
- **msstore snelheidsfix**: `winget install --silent --source msstore <id>` ging onder water via een trage COM-pad waardoor msstore-apps (WhatsApp, ChatGPT, etc.) bizar lang duurden. `WingetService.InstallAppAsync` gebruikt nu voor source=msstore een aangepaste command line equivalent met handmatig `winget install <productID> --accept-source-agreements --accept-package-agreements` — geen `--silent`, geen `--source` flag, geen `--exact`. Winget detecteert msstore productIDs (formaat 9XXX / XPXXX) zelf. Resulteert in dezelfde snelle install-ervaring die user in PowerShell ziet
- Eerdere experimentele `ms-windows-store://` Store URI fix (zelfde commit) gerevert — winget zelf is sneller én volautomatisch zodra de juiste flags gebruikt worden

### v0.7.4 — Export / import selectie naar JSON
- Nieuwe `SelectionImportExportService` (singleton via `App.SelectionIO`) — schrijft de huidige selection naar JSON (`my-apps-YYYY-MM-DD.json`) en leest die terug. Format: `{ version, exportedAt, appCount, apps: [wingetId, ...] }`. Alleen WingetIds worden gepersist; bij import wordt elke ID case-insensitive gematcht tegen de huidige catalog en `IsSelected = true` gezet
- Nieuwe `Helpers/FilePickerHelper.cs` met `PickSaveFileAsync` / `PickOpenFileAsync`. Voor unpackaged WinUI 3 zijn FileSavePicker / FileOpenPicker zonder `WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd)` onbruikbaar — pickers eisen een window-handle. HWND uit `App.Window` gehaald
- Nieuwe **"App selection"** sectie op SettingsPage met Export + Import knoppen + InfoBar voor feedback. Export zonder selectie → warning "Niets om te exporteren". Import met onbekende WingetIds → warning "X geselecteerd, Y niet gevonden in catalog" zodat user weet dat een paar IDs niet meer matchen (bv. JSON van oudere catalog-versie). Import doet by default `clearFirst: true` — overschrijft huidige selectie
- Use case: power user maakt op zijn main PC een `my-apps.json`, kopieert die naar een nieuwe machine, importeert via Settings, klikt 1× Install op AppsPage. Of deelt het bestand met een vriend voor dezelfde setup. Profiles (vooraf-gedefinieerd) niet gebouwd — user-gedefinieerde export/import vervangt die behoefte

### v0.7.3 — Post-install schedule prompt + dialog polish
- **Post-install "Schedule auto-updates?" prompt**: nieuwe `Helpers/ScheduleAutoUpdatePrompt.cs` met static `MaybeShowAsync(XamlRoot)`. Triggert na een succesvolle InstallDialog (alleen als `HadSuccessfulInstall == true` én er nog geen scheduled task is én user heeft niet eerder "Don't ask again" geklikt). 3 knoppen: **Schedule** → opent ScheduleDialog, **Don't ask again** → zet `DontAskAboutScheduling = true`, **Not now** → niets. Aangeroepen vanuit zowel `AppsPage.InstallButton_Click` als `CategoryDetailPage.InstallButton_Click`. Nieuwe `InstallDialog.HadSuccessfulInstall` property (set wanneer winget `successCount > 0`)
- **SettingsService** uitgebreid met `DontAskAboutScheduling` (default `false`)
- **`TaskSchedulerService.CreateUpdateTaskAsync`** refactor: schtasks-aanroep gewrapt in `cmd.exe /c "schtasks ... > log 2>&1"` zodat we stdout+stderr kunnen capturen ondanks `UseShellExecute=true` (vereist voor `Verb=runas`). Resolved silent quoting issues — schtasks lijkt een andere quote-parsing te volgen wanneer direct via UseShellExecute aangeroepen vs via cmd. Logfile in `%TEMP%\SetupToolbox_schtasks.log`. Return type van `CreateTaskResult` enum naar nieuw `CreateTaskOutcome` record (`Result` + `ErrorOutput`). InfoBar in ScheduleDialog toont nu de echte schtasks output bij `Failed`
- **ScheduleDialog success-feedback**: na `CreateTaskResult.Success` blijft de dialog open, toont `InfoBarSeverity.Success` "Scheduled task created" met de schedule-omschrijving (Daily at HH:MM / Weekly on Monday / On user logon), primary disabled, Close-tekst → "Done"
- **Rounded ContentDialog footer buttons**: WinUI 3 default geeft footer buttons 0 corner radius (snap-fit aan dialog edges). Nieuwe `DialogPrimaryButtonStyle` (BasedOn `AccentButtonStyle`) + `DialogDefaultButtonStyle` (BasedOn `DefaultButtonStyle`) in App.xaml met `CornerRadius="4"`. Toegepast via `PrimaryButtonStyle` / `SecondaryButtonStyle` / `CloseButtonStyle` op ScheduleDialog, InstallDialog, ScheduleAutoUpdatePrompt, en de SettingsPage Disable confirm/result dialogs
- **`DefaultButton = None` fix** voor de Disable-confirm dialog: ContentDialog's `DefaultButton` property forceert AccentButtonStyle op de aangewezen knop en overschrijft custom `CloseButtonStyle`. Was `Close` (Cancel werd dus blauw), nu `None` zodat Disable accent blijft en Cancel neutraal grijs is. Voor destructive actions sowieso veiliger: geen Enter-shortcut

### v0.7.2 — Settings-toggle voor manual download fallback
- Nieuwe `SettingsService` (singleton via `App.Settings`) — JSON-backed store in `%LOCALAPPDATA%\SetupToolbox\settings.json`. Minimal start: alleen `FallbackToDownloadPage` (default `true` = bestaand v0.7.1 gedrag). Best-effort persist (try/catch op disk IO, in-memory state altijd consistent), camelCase JSON serializer. Wordt in v0.10.0 uitgebreid met de andere settings (`CheckForUpdatesOnStartup`, `ShowWelcomeBanner`, etc.)
- Nieuwe **"Installation"** sectie op SettingsPage met `ToggleSwitch` "Open vendor download pages". Initial sync via `_suppressToggleEvent` guard zodat page-navigatie niet elke keer settings.json terugschrijft
- `InstallDialog` respecteert de toggle: wanneer UIT worden manual-download apps geskipt met nieuwe `InstallItemState.Skipped` ("Skipped" label, secondary text colour) i.p.v. dat de browser geopend wordt. Final summary text combineert nu winget + manual-opened + skipped: "X installed, Y failed, Z manual downloads opened, N skipped"
- Bonus fix: `manualOpenedCount` telt nu alleen state `ManualOpened` (i.p.v. raw count van manual apps), zodat een Failed manual-app niet dubbel in de summary verschijnt

### v0.7.1 — Fallback download URL voor non-winget apps
- `App.DownloadUrl` (nullable string) JSON-veld + `IsManualDownload` + `ManualDownloadVisibility` properties op het model
- Badge **"Manual download"** (caution-orange + Globe glyph E71B) in CategoryDetailPage en AppsPage app-cards naast de andere status badges
- `InstallDialog` splitst geselecteerde apps in winget-installable + manual-download. Voor manual-apps wordt de `downloadUrl` geopend in de default browser via `Process.Start(...UseShellExecute=true)` — geen winget call. Failure (URL invalid) → `InstallItemState.Failed` met "Could not open URL: ..." message
- Nieuwe `InstallItemState.ManualOpened` met label "Browser opened" (caution color) en de message "Opened vendor download page in browser". Final-summary in dialog combineert winget-results + manual-opened count: "X installed, Y failed, Z manual downloads opened"
- 3 voorbeeld-apps toegevoegd aan apps.json: **VMware Workstation Pro** (Development → VMs), **ON1 Photo RAW** (Creative → Graphics), **Nvidia App** (Utilities → System Tools, popular). Alle 3 met icons gefetched
- Roadmap-swap meegenomen: install-features (v0.7.0) zijn nu de core focus i.p.v. Debloat eerst. Volgorde: v0.7=install polish, v0.8=Debloat, v0.9=Tweaks, v0.10=Settings + self-update

### v0.6.3 — AppIcon size + spacing polish
- Cards opgeschaald van 28×20 → 38×26 (~3.5× oppervlakte) zodat icon visueel even groot oogt als andere taskbar-icons (Edge/VSCode/etc.). Cards blijven landscape (1.46:1 ratio)
- Symmetric 5×5 stack-offset i.p.v. 4×4 — duidelijker zichtbare "stap" tussen gestapelde cards (~19% per card-rand zichtbaar i.p.v. 14%) zonder te krap te ogen
- Cards verticaal gecentreerd in canvas (6 padding boven/onder), volledig vullend horizontaal

### v0.6.2 — Custom AppIcon (taskbar + Explorer + alt-tab)
- Vervangt placeholder ICO door custom design dat de "app-installer" metaphor uitstraalt: 3 gestapelde blauwe app-cards (analogous monochrome palette) met Fluent download-icoon (verticale shaft + V-tip + horizontale tray-lijn) gecentreerd in de witte front card. Volgt MS Windows 11 Fluent Design guidelines (single literal metaphor, subtle 120° gradient, layered drop shadows, light from top-left, geen typografie, no background tile)
- `scripts/generate-app-icon.ps1` — programmatische generator via System.Drawing. Bouwt master 256×256 PNG en downscaled naar 16/24/32/48/64/128/256 px PNG-encoded entries in een Vista+ multi-resolution ICO. Reproduceerbaar — gewoon opnieuw runnen om iconen aan te passen
- Cards 36×28 op 48-base grid → vult ~95% van canvas zodat het icoon visueel even groot oogt als andere taskbar-icons
- `<ApplicationIcon>` toegevoegd in csproj zodat MSBuild de ICO als exe-resource embed (Explorer/taskbar/alt-tab pikken het op). `MainWindow` zet runtime window-icon via `AppWindow.SetIcon("Assets/AppIcon.ico")`
- `scripts/generate-icon-variants.ps1` als design-exploratie tool met meerdere alternatives (tilted Photos-style stack, vertical stack, V-chevron+lijn variant) — niet opgenomen in repo final, alleen het script blijft beschikbaar voor toekomstige iteratie. Afgewezen V29 (tilted) PNG ligt in `data/app-icon-backups/`

### v0.6.1 — Icons in AppsPage global search
- CatalogResultsList DataTemplate uitgebreid met 3-column grid (40×40 `Image` / text / checkbox), zelfde patroon als CategoryDetailPage card
- `AppIcon_ImageFailed` handler in AppsPage code-behind verbergt de Image bij missing icon zonder layout-break
- Curated catalog matches in de globale search tonen nu hun icon. Winget-repo search results blijven iconless (dynamisch, niet gebundeld — placeholder is een aparte v0.10.x optie)

### v0.6.0 — Icon system milestone
- `scripts/fetch-icons.ps1`: PowerShell pipeline die per app een 128×128 PNG ophaalt en normaliseert naar transparante canvas (gecentreerd, aspect preserved). Ladder: `iconUrl` override → `iconFile` lokaal → Google favicon API (`sz=128`) → icon.horse fallback (scrapet `apple-touch-icon`, vaak 180-512px). Wikipedia REST API (`/api/rest_v1/page/summary/<title>`) gebruikt om voor probleemgevallen hi-res logo-URLs te vinden — Wikipedia eist gedetailleerde User-Agent met contact-info anders 400
- Curated mix van bronnen: dashboard-icons (Homarr Labs), selfhst/icons, Wikipedia Commons, icons8, Steam GridDB, en `scripts/local-icons/` voor user-supplied PNGs (Everything via Gemini-render)
- Post-processing pipeline op elke icon:
  1. **Auto-crop** (opt-in via `autoCrop = $true` per app) — trimt whitespace borders rond het logo. Default OFF zodat designed icons (Claude, PowerToys, Office, Teams) hun bewuste padding behouden. Aan voor whitespace-heavy sources (Everything, WinRAR icons8 PNG)
  2. **Scale-to-fit** — bewaart aspect ratio, centreert op 128×128 transparante canvas
  3. **White-to-transparent** — BFS flood-fill vanaf canvas-corners met threshold 225 (R,G,B all ≥225 = "near-white"), maakt witte achtergronden transparant zonder logo-witte details kapot te maken
  4. **Squircle rounded corners** (Apple-style, ~22% radius) — alleen toegepast als ALLE 4 hoek-pixels (bijna) volledig opaque zijn (alpha ≥250). Voorkomt clipping van designed icons met breathing room (Claude burst rays, CCleaner broom) en is redundant op al-ronde logos (Discord, Spotify)
- Result: **92/92 icons OK**, 0 failed, 2 LAAG (LibreOffice + Insomnia, acceptabel op 48px UI)
- 3 nieuwe Proton apps toegevoegd aan `apps.json`: Calendar, Wallet, Authenticator. Plus Sheets/Docs/Meet via een tweede ronde
- `data/icons/<wingetId-met-hyphens>.png` gebundeld via `<Content>` + `PreserveNewest`. Filename gebruikt hyphens i.p.v. dots — Windows PRI parser ziet anders bv. `.64-bit.png` als scale qualifier en weigert te resolven
- `App.IconImage` getter returnt `BitmapImage` (lazy + gecached) i.p.v. string-pad — x:Bind doet geen automatische `string` → `ImageSource` conversie (die werkt alleen via XAML markup TypeConverter)
- CategoryDetailPage card layout uitgebreid met 3e column: 40×40 `Image` links, daarna text+badges, dan checkbox. `ImageFailed` event verbergt de Image bij missing icon zonder layout te breken
- WinAppSDK pin: `1.8.*` (was `*` → restored 2.0.1 en crashte met "Required components of the Windows App Runtime are missing — Version 2.x" omdat alleen 1.8 systeembreed is geïnstalleerd)

### v0.5.12 — Self-contained publish configuratie
- `WindowsAppSDKSelfContained=true` in alle drie publish profiles (win-x64, win-arm64, win-x86) — exe heeft de WinAppSDK runtime nu meegebundeld, geen aparte WinAppSDK installer nodig op de doelmachine
- `PublishTrimmed` uit gezet (was conditioneel aan voor non-Debug). Trimming brak `JsonSerializer.Deserialize<AppDatabase>` doordat de reflection-paden van System.Text.Json statisch onbereikbaar lijken — "Could not load categories" bij startup. WinUI 3 unpackaged is sowieso fragiel voor trim (XAML compiler, x:Bind, WinRT bridge leunen op reflection)
- Resultaat: `dotnet publish -c Release -p:PublishProfile=win-x64` levert een drop-and-run folder van ~262 MB op (ZIP'd ~70-80 MB voor distributie)

### v0.5.9 — WPF gearchiveerd
- WPF source (`src/SetupToolbox/`) + Launcher (`src/Launcher/`) uit de repo verwijderd
- Code blijft recoverable via git tag `wpf-final-v1.2.1`
- Solution opgeschoond — alleen `SetupToolbox` blijft over
- README, INTEGRATIE.md, CHANGELOG.md, CLAUDE.md, NEXT-STEPS.md herschreven naar WinUI-only context

### Curated dataset (apps.json v2.0.0)
- Trim van 125 → ~60 apps op basis van wishlist
- Nieuwe top-level "Gaming" categorie (was subcat)
- Nieuwe top-level "App Suites" — Proton (5 apps) + Adobe (Creative Cloud + Acrobat Pro)
- Productivity uitgebreid: AI Assistants subcat (ChatGPT msstore + Claude), Cloud Storage subcat (OneDrive)
- Security met aparte subcats: Password Managers / VPN / Antivirus
- `App.Source` veld + `WingetService` `--source` flag voor msstore-only apps (WhatsApp, Apple Music, ChatGPT)

---

## Parking-lot & ideeën (niet gescoped, geen versienummer)

> Dit is een **referentie-lijst**, geen actuele takenlijst — voor wat écht openstaat, zie **## Open** bovenaan dit bestand. Alle v0.6.x t/m v0.9.20 roadmap-items die hier ooit stonden zijn afgerond (zie **Voltooide versies**) en zijn 2026-08-16 uit deze sectie verwijderd om duplicatie te voorkomen; niets inhoudelijks is verloren gegaan, het staat al gedetailleerd in de bijbehorende Voltooide-versies-entries. Wat overblijft is ruw onderzoeksmateriaal voor toekomstige tweaks — elk item moet nog gescoord/uitgewerkt worden vóór implementatie.

### Tweaks parking-lot extension — Michael-Matta UWT lijst (mei 2026)

Bron: [Michael-Matta1/windows-utilities-tweaks](https://github.com/Michael-Matta1/windows-utilities-tweaks) — open-source Win-tweaker met ~200 toggles. Hieronder gecategoriseerd naar **onze** category-buckets, met dubbelen tegenover onze huidige roadmap weggefilterd. **Verifieer per item voor implementatie** — UWT is een legacy WinUI tool, sommige tweaks zijn Win7/8/10-era en mogelijk deprecated op Win11 24H2/25H2.

**Caveat**: items die je security verlagen (Disable Defender / Disable UAC / Disable Registry Editor / Disable Task Manager) zijn IT-admin lockdown tooling — out-of-scope voor onze user-quality-of-life focus. Niet implementeren tenzij user expliciet vraagt.

**Explorer** (boven onze huidige 5 tweaks):
- Show Windows version on Desktop (paint.exe-trick of DWM)
- Tweak Drive Letters / Remove duplicate drive letter entry
- Remove shortcut arrows from icons
- Remove "-Shortcut" suffix for new shortcuts
- Enable check boxes to select items
- Enable auto-complete + auto-suggest in address bar
- Show User Folder in nav pane
- Choose which folder File Explorer opens on (Home / This PC / custom) — al gepland v0.9.1 als LaunchTo, mogelijk uitbreiden met custom path
- Disable Aero Peek / Aero Shake / Aero Snap
- Show status bar in File Explorer
- Launch folders in a separate process
- Disable info tips for shortcuts
- Disable full row select items
- Hide preview pane (default off?)
- Always show icons never thumbnails / Display file icons on thumbnails
- Hide protected OS files / Show drive letters
- Show encrypted/compressed NTFS files in color
- Use sharing wizard
- Disable Windows startup sound
- Increase System Restore Points frequency
- Enable automatic registry backups
- Don't show low-disk-space warnings

**Taskbar** (boven onze huidige 9 tweaks):
- Use small taskbar icons (Win10-style — werkt mogelijk niet meer op 11)
- Hide inactive icons from notification area
- Remove individual systray icons: volume / network / action center / clock / battery / notification area
- Customization of taskbar buttons grouping (overlap met onze TaskbarGlomLevel)
- Customization of taskbar thumbnail size + delay
- Make taskbar button switch to last active window
- Show Windows Defender icon in tray
- Customization of taskbar content alignment (= onze TaskbarAl)
- Taskbar size customization

**Start Menu** (boven onze v0.9.3 plan):
- Lock start screen tiles so they can't be rearranged
- Disable taskbar + start jumplists (privacy: app-history wegfilteren)
- Increase jump list items displayed
- Enable accent color for Start menu + taskbar
- Enable / disable Stickers feature
- Disable Bing web search in Start (al gepland)

**Ads & Bloat** (boven onze v0.9.4 OFGB):
- Remove Windows Spotlight "Learn more" desktop icon
- Disable "Look for an app in the store" prompt
- Disable "You have new apps that can open this type of file" notification
- Disable Cortana entirely (al gepland v0.9.3)
- Disable handwriting data sharing
- Disable Wi-Fi Sense

**AI / Copilot** (boven onze v0.9.5):
- (UWT heeft hier niet veel — onze v0.9.5 plan is al uitgebreider)

**Privacy** (boven onze v0.9.6):
- Disable application telemetry
- Disable inventory collector
- Disable steps recorder
- Disable biometrics
- Disable password reveal button
- Disable Windows update sharing (P2P upload)
- Disable Windows feedback requests (al deels in onze plan)
- Disable synchronization of settings (cross-device sync uit)
- Disable App access to: location / calendar / messages / microphone / camera / user account info

**UI / Theme** (boven onze v0.9.7):
- Disable login screen blur (al gepland)
- Disable lock screen / disable changing lock screen image
- Enable + customize lock screen slideshow
- Enable / disable first sign-in animation
- Toggle apps / system light/dark theme (al gepland)
- Increased taskbar transparency
- Change inactive title bar color
- Customize blinking cursor width + time
- Customize scroll bar width
- Disable transparency effects globally (al gepland)
- Disable / enable transparency selectively
- Show / hide power button options (Lock / Sleep / Hibernate / Sign Out)
- Enable verbose logon messages (al gepland)
- Customize logon message (custom-text vóór login screen)

**Performance** (boven onze v0.9.8):
- Waiting times tweakable: kill apps timeout / end services / non-responsive apps
- Auto-end non-responsive programs
- Restart shell automatically after error
- Always unload DLLs to free memory
- Disable automatic folder-view discovery (al gepland)
- Turn off Search Indexer
- Increase IRQ8 priority
- Disable smooth scrolling
- Disable Windows Time service
- Disable Tablet Input service
- Disable Windows Security Center service
- Disable Prefetch / Superfetch service (gevaarlijk op SSD/HDD verschil)
- Disable Printer Spooler (security tweak ook)
- Disable Edge/tab preloading
- Disable all background apps (al gepland)
- Delete pagefile at shutdown

**Context Menu** (boven onze v0.9.9):
- Remove "Open in Windows Terminal" from Desktop context (counterintuitive — meeste users willen 'm juist)
- Add over 15 modern UWP apps to desktop context (Edge / OneNote / Store / Music / Mail)
- Add Take ownership / Copy to / Move to / Open with (al gepland)
- Remove "Cast to Device" / "Edit with Paint 3D" / "Scan with Windows Defender" / "3D Print with 3D Builder"
- Toggle Quick Access menu items
- Add "Phone" / "Gaming Settings" / "Character Map" / "Control Panel" / "Windows Update" to context menu
- Add Windows Defender quick-actions (Open / Quick Scan / Full Scan / Settings / Update) to context

**Notifications & Lock Screen** (boven onze v0.9.10):
- Disable toast notifications globally (al gepland)
- Set notifications display time (Win-specific timeout per toast)
- Disable Action Center quick-action buttons
- Enable / disable Edge "Do you want to close all tabs?" prompt

**Updates** (boven onze v0.9.11):
- Disable Windows Update service entirely (al gepland deels)

**Browsers / Edge**:
- Change Edge default download location
- Adjust tab preview show/hide delay
- Disable Edge tab preview entirely
- Enable Edge "close all tabs?" prompt (= een non-default warning toggle)

**Out-of-scope (security-lockdown — IT-admin tools, niet user QoL)**:
- Disable Registry Editor / Control Panel / Task Manager / CMD / WinKey shortcuts
- Disable folder options menu / display personalization
- Disable shutdown / log off ability
- Disable encrypting file system
- Disable Defender / Windows Store / Mobility Center / Media Center / Update Service
- Disable system restore / MMC snap-ins
- Disable color & appearance settings
- Disable internet communication
- Restrict access to taskbar + start menu properties
- Disable explorer's context menu / taskbar context menu
- Disable changing wallpaper
- Disable user tracking
- Hide entire network / prevent network auto-discovery / disable admin shares
- Disable NTLM 2 / set global network offline
- Disable Anonymous Connections access
- Disable Print Spooler (security-relevant)

**Out-of-scope (UWT-app-specific tweaks die niet ons doel zijn)**:
- Option to add UWT to startup / Integrate UWT with desktop context menu
- Export / import tweaks (we hebben SelectionImportExportService voor app-selecties; tweak-import voor v0.9.13 presets)
- Edit OEM Information (registered owner / organization) — niche
- Customize "New" menu in context — power-user-only

**Mogelijk-interessant maar verifieer-bij-implementatie**:
- Customize UAC settings (security implication — onze v0.9.x parking-lot heeft al "Security caution-tier")
- Enable Admin Approval Mode for built-in Administrator
- Disable switching to Secure Desktop while elevating
- Enable virtualize File and Registry write failures (per-user redirect)
- Display Last Logon Information on logon screen
- Make user enter username while logging on (security)
- Require Ctrl+Alt+Del to logon (security)
- Show Windows Photo Viewer (klassieke viewer terug — al gepland v0.9.7)
- Reset Live Tile cache
- Enable Stickers feature (decoratie op desktop)
- Restore last opened folders at startup

### Tweaks parking-lot (uit research, niet ingepland — voor v1.x feature pack)

Bewust niet meegenomen om v0.9.x scope hanteerbaar te houden. Bij interesse later oppakken:

- **Network tweaks**: DNS swap (Cloudflare/Google/Quad9 per-adapter), Disable IPv6 entirely, Disable Teredo tunneling, Disable NetBIOS-over-TCP/IP, Disable LLMNR — niche, raakt netwerkstack
- **Edge browser debloat** (17 keys onder `HKLM\SOFTWARE\Policies\Microsoft\Edge`): Startup Boost, Background mode, Hubs sidebar, Bing chat, Shopping, Wallet donation, Personalized ads, Address-bar Bing, Search suggestions, Auto-launch on logon
- **Security caution-tier** (achter "I know what I'm doing"-checkbox): Set UAC to Never notify, Set UAC notify-without-dimming, Disable SmartScreen apps, Disable Defender real-time (vereist Tamper Protection off — Safe Mode)
- **ExplorerPatcher integratie** (taskbar grouping labels = naast icon ook app-name) — vereist third-party tool install, scope-creep
- **Remove Edge entirely** — destructive `setup.exe --force-uninstall` flag, kan apps breken
- **Disable SMB1** — security, maar legacy network shares (NAS, oude printers) breken
- **Disable Sticky Keys prompt + Toggle Keys + Filter Keys prompts** — accessibility quality-of-life, lage prio
- **System clock to UTC** (Linux dual-boot users)
- **Disable Razer Synapse auto-install on USB-connect**
- **Disable downloaded-exe security warning** (zone 3 `1806=0`) — security implicatie
- **Always show scrollbars in UWP/Settings apps** (`DynamicScrollbars=0`) — kleine UX-preference
- **Disable folder auto-type discovery** (voorkomt dat folders ineens als "pictures" weergegeven worden)
- **Auto-folder LaunchTo backup** — alternatieve setting voor User Files start-folder

### Out of scope

- Decoratieve themes (Sunset / Aurora / OceanBreeze) — historisch alleen in WPF
- Windows-only apps die geen winget hebben — opgelost via `downloadUrl` + de "Fallback to download page"-toggle (v0.7.1/v0.7.2), geen losse feature nodig

> MSIX packaging stond hier eerder als "out of scope, voorlopig unpackaged", daarna als actief onderzoek. Dat onderzoek is **afgerond op 2026-08-16 met de conclusie: niet doen** — zie de MSIX-sectie onder *Voltooide versies*. Weer out of scope, nu mét onderbouwing.

---

## Development Notes

### Quick commands

```bash
# Build
dotnet build src/SetupToolbox/SetupToolbox.csproj -c Debug

# Run
dotnet run --project src/SetupToolbox/SetupToolbox.csproj -c Debug

# Self-contained release publish
dotnet publish src/SetupToolbox/SetupToolbox.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -o ./release

# GitHub release
gh release create v0.X.Y ./release/SetupToolbox.exe --title "SetupToolbox v0.X.Y"
```

### Project structuur

```
src/
└── SetupToolbox/             # Native Win11 app (WinUI 3 + WinAppSDK 1.8)
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / .xaml.cs        # MicaBackdrop BaseAlt + NavigationView
    ├── Models/AppModels.cs               # App (INPC), Category, SubcategoryGroup
    ├── Pages/                            # AppsPage, CategoryDetailPage, DebloatPage, SettingsPage, TweaksPage
    ├── Dialogs/                          # InstallDialog, ScheduleDialog
    ├── Services/                         # AppDatabaseService, WingetService, TaskSchedulerService, SelectionHelper
    ├── Helpers/                          # FuzzyMatcher, ScrollViewSpeedup
    └── Assets/

data/
└── apps.json                             # Curated app catalog (v2.0.0, ~60 apps, gebundeld met exe)
```
