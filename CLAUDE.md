# Claude Guardrails - SetupToolbox

## NEXT-STEPS.md is de single source of truth

- Bij het begin van elk gesprek of nieuwe taak: LEES EERST `NEXT-STEPS.md` volledig
- Na elke implementatie: update `NEXT-STEPS.md` met wat er gedaan is (afvinken) en wat er nieuw bij is gekomen
- Als de gebruiker iets benoemt dat niet direct wordt uitgevoerd: ZET HET METEEN op de lijst in `NEXT-STEPS.md` en bevestig dit expliciet ("Ik heb X op de lijst gezet in NEXT-STEPS.md")
- Niets mag verloren gaan — alles wat besproken wordt moet vastgelegd worden

## Verificatie voordat je "klaar" meldt

- Check ALTIJD of het werk echt klopt voordat je zegt dat iets af is
- Geef een korte samenvatting van wat er gedaan is
- Bevestig dat alles volgens de afspraak is uitgevoerd
- Geen halve oplossingen als "klaar" melden

## Commits en versienummering

- Bij elk afgerond issue of feature: commit + push naar GitHub
- Versienummering volgt de items in NEXT-STEPS.md: `1.0.1`, `1.0.2`, `1.0.3` etc.
- Elke afgevinkte taak uit de geplande features lijst = een nieuwe patch versie
- Als alle items van een milestone (bijv v1.1.0) af zijn → die versie wordt de release
- Alleen committen als een issue/feature ECHT af is, niet halverwege (tenzij de gebruiker dit expliciet vraagt)
- GEEN "Co-Authored-By" of andere AI-attributie in commit messages
- Commit messages bevatten alleen relevante info over wat er is gewijzigd
- Versienummer in code (csproj, AssemblyName, SettingsPage) updaten als ONDERDEEL van dezelfde commit — NIET als aparte commit
- Alles in 1 commit: code wijziging + versienummer update + NEXT-STEPS.md update
- 1 build check is voldoende — bouw pas NADAT alle wijzigingen (inclusief versienummer) zijn gedaan
- GitHub Releases (met exe's) ALLEEN bij milestone versies (1.1.0, 1.2.0, 2.0.0 etc.), NIET bij patches (1.0.1, 1.0.2 etc.)

## Context window bewaking

- Geef PROACTIEF aan als het context window vol begint te raken
- Meld als de kwaliteit van het werk dreigt af te nemen door context-verlies
- Stel voor om een nieuwe chat te beginnen als dat nodig is
- Niet doormodderen als je merkt dat je dingen begint te vergeten of missen

## Werkwijze

- Lees relevante bestanden VOORDAT je wijzigingen maakt
- Geen hardcoded kleuren in UI code — gebruik `ThemeResource` keys uit het Fluent design system (zoals `CardBackgroundFillColorDefaultBrush`, `AccentFillColorDefaultBrush`)
- Bouw altijd na wijzigingen (`dotnet build src/SetupToolbox/SetupToolbox.csproj -c Debug`) en meld het resultaat
- Bij UI-wijzigingen ook `dotnet run` om visueel te verifiëren — build succeeded != feature werkt
- Bij twijfel: vraag de gebruiker, maak geen aannames
