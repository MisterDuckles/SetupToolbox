# -*- coding: utf-8 -*-
"""Zoekt gebruiker-zichtbare tekst die nog een hardcoded literal is.

Gebouwd tijdens v1.2.5, nadat bleek dat fase 2 er 16 had laten staan — waaronder
InfoBar-bodies waarvan de titel wél vertaald was, dus precies het soort misser dat
je met de hand niet ziet. Draai dit voor en na een vertaalfase.

    py scripts/scan-untranslated.py

Vier passes:
  XAML  tekst-dragende attributen met een letterlijke waarde in plaats van een
        markup-extension ({loc:Localize ...}) of een binding.
  C#    string-literals die aan .Text / .Content / .Title / .Message /
        *ButtonText / .Header worden toegekend, plus named arguments die naar de
        UI stromen (body:, title:, confirmText: ...).
  C#    SWITCH-ARMEN die een string-literal teruggeven (`=> "..."`) — badges en
        statuslabels op een model of in een dialog.  [blinde vlek 3, v1.2.7]
  C#    ELKE zin-achtige literal in Dialogs/, Pages/, Helpers/ en Services/ die
        niet in een Loc-call, een log of een proces-argument zit.
                                                     [blinde vlek 2, v1.2.7]

Uitgebreid in v1.2.6 na drie missers: de C#-pass eiste een aanhalingsteken direct
na het "=", waardoor GEÏNTERPOLEERDE strings ($"Show {n} locations") er compleet
doorheen glipten — en named arguments werden helemaal niet bekeken. Beide vormen
worden nu wel gezien.

Uitgebreid in v1.2.7 met de laatste twee passes, nadat de scanner 0 meldde
terwijl er ~88 hardcoded strings in beeld stonden. Beide vormen ankeren niet op
een toewijzing en waren dus per constructie onzichtbaar:

  public string CategoryLabel => Category switch  // pass 3: geen toewijzing
  {
      DeepCleanCategory.RecycleBin => "Recycle Bin",
      ...
  };

  var label = $"{n} selected · {bytes} to free";  // pass 4: toewijzing aan een
  if (elevated > 0) label += " · ...";            //   lokale var, .Text komt
  SelectionStatusText.Text = label;               //   pas twee regels later

Pass 4 vergt strikt genomen dataflow-analyse. Het pragmatische alternatief dat
hier staat — élke zin-achtige literal in de UI-lagen melden — vangt hetzelfde en
is te overzien, mits de ALLOW-lijst hieronder bijgehouden wordt.

In v1.2.8 is Services/ aan pass 4 toegevoegd; tot dan stond die map er bewust
buiten omdat de foutmeldingen uit de elevated batches nog Engels waren. Bij het
toevoegen bleek de pass daar op drie punten om te vallen, alle drie hier gefixt:

  1. De terugkijk om log-regels te herkennen zocht over 160 tekens rúwe tekst.
     In Dialogs/ ging dat net goed; in Services/ staat er vrijwel altijd wel een
     Diagnostics.Log binnen dat venster, dus de pass meldde daar 0 — hij zou
     structureel groen liegen. Vervangen door enclosing_call(): welke aanroep
     omsluit deze literal daadwerkelijk.
  2. De LITERAL-regex behandelde een verbatim string (@"…") als een gewone, en
     liep daardoor stuk op registry-paden die op een backslash eindigen. Eén
     zo'n pad zette elke match daarna één aanhalingsteken uit de pas.
  3. Char-literals werden niet overgeslagen, dus .Trim('"') liet de scan
     middenin een niet-bestaande string beginnen — met hetzelfde gevolg.

De ruis die daarna overbleef (PowerShell-fragmenten, winget-argumenten,
registry-value-namen) is met categorische regels weggenomen in plaats van met
losse ALLOW-regels — zie SCRIPT_OR_ARG en is_identifier. Bekende prijs van
SCRIPT_OR_ARG: een échte UI-zin die een PowerShell-cmdlet noemt ("Geen output
van Checkpoint-Computer.") wordt niet gemeld. Dat is geaccepteerd; de winst is
dat de ALLOW-lijst leesbaar blijft en dus daadwerkelijk nagelezen wordt.

Exit-code 1 zodra er iets gevonden wordt, zodat het in een check kan hangen.
Valse positieven horen in ALLOW hieronder, mét reden.
"""
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    'src', 'SetupToolbox')

TEXT_ATTRS = ('Text', 'Content', 'Header', 'Title', 'Message', 'PlaceholderText',
              'PrimaryButtonText', 'SecondaryButtonText', 'CloseButtonText',
              'ToolTipService.ToolTip', 'Description')

# Leeg of puur numeriek/interpunctie: nooit vertaalbaar. Geldt voor beide passes.
SKIP_VALUE = re.compile(r'^(?:\s*$|[\d\s.,:;/|+*=x×·—-]*$)')

# ALLEEN voor XAML: een waarde die met { begint is een markup-extension
# ({Binding …}, {loc:Localize …}, {StaticResource …}) en dus geen tekst.
# Deze regel mag NIET op C# losgelaten worden — daar is { het begin van een
# interpolatie-gat, en dan sla je "$"{n} items geselecteerd"" stil over. Dat
# gebeurde tot v1.2.6 wél, en verborg meerdere echte strings.
SKIP_XAML_VALUE = re.compile(r'^\{')

# Bewust hardcoded gebleven, mét reden. Alles wat hier staat is een IDENTIFIER —
# de letterlijke naam van een Windows-artefact of een product — en geen woord.
# Zelfde regel als de merknamen in v1.2.5 en de app-namen in v1.2.6.
#
# De waarde is een tuple met de bestanden waarin de uitzondering geldt, of None
# voor "overal". Dat onderscheid is nodig: "Recycle Bin" is een terechte
# hardcoded DisplayName in DeepCleanService, maar als BADGE hoort 'ie vertaald
# — met een ALLOW op alleen de waarde zou de scanner die regressie niet meer
# zien.
ALLOW = {
    # app-naam: identiek in beide talen en wordt bij het starten sowieso uit code gezet
    'Setup Toolbox': None,

    # v1.2.7 — badge-armen in DeepCleanItem / LeftoverItem die letterlijke
    # Windows-namen zijn: map-, registry-key- en hive-namen.
    'Prefetch': None,
    'App Paths': None,
    'MUIcache': None,
    'HKCU vendor': None,
    'Registry': None,
    'Program Files': None,
    'AppData': None,

    # v1.2.7 — InstalledAppEntry.SourceBadgeText. Winget is Microsofts package
    # manager, Store is de Microsoft Store (heet in NL Windows ook zo) en Web is
    # in beide talen hetzelfde woord. De tooltip ernaast loopt wél via de tabel.
    'Winget': ('Models',),
    'Store': ('Models',),
    'Web': ('Models',),

    # v1.2.7 — de DisplayName van de vaste cache-targets in DeepCleanService.
    # Die blijven Engels omdat DeepCleanDialog.BundleByTokenOverlap ze
    # TOKENISEERT: een Nederlandse variant kan tokens gaan delen die het Engels
    # niet deelt, waarna de cards per taal anders bundelen. Zie NEXT-STEPS.md.
    'User Temp folder': ('DeepCleanService',),
    'System Temp folder': ('DeepCleanService',),
    'Windows Update cache': ('DeepCleanService',),
    'Edge cache': ('DeepCleanService',),
    'Chrome cache': ('DeepCleanService',),
    'Brave cache': ('DeepCleanService',),
    'Recycle Bin': ('DeepCleanService',),
    'Firefox cache ({profileName})': ('DeepCleanService',),

    # v1.2.7 — restore-point-omschrijving. Die komt in Windows' EIGEN
    # Systeemherstel-scherm terecht, niet in onze UI, en moet daar herkenbaar
    # zijn als van deze app afkomstig.
    'SetupToolbox Deep Clean ({selected.Count} items)': ('DeepCleanDialog',),
    'SetupToolbox Debloat ({totalCount} items)': ('DebloatPage',),

    # v1.2.7 — /toasttest is een debug-switch met nepdata in de brontaal.
    'VLC media player': ('App.xaml.cs',),
    'The installer failed. Try again.': ('App.xaml.cs',),

    # v1.2.7 — registry-waarde, geen UI: dit is de (Default)-value van de
    # protocol-handler-sleutel waarmee Windows setuptoolbox: herkent.
    'URL:Setup Toolbox': ('ToastProtocol',),

    # v1.2.7 — voorgestelde BESTANDSNAAM in de opslaan-dialoog. Een bestandsnaam
    # is geen proza; per taal een andere naam maakt de export minder portabel.
    # Het bestandstype-LABEL ernaast loopt wél via de tabel (io.fileType.*).
    'my-apps-{DateTime.Now:yyyy-MM-dd}': ('SettingsPage',),
    'my-tweaks-{DateTime.Now:yyyy-MM-dd}': ('TweaksPage',),

    # ── v1.2.8: Services/ kwam in pass 4. Wat hieronder staat is per geval
    # nagetrokken op de vraag "wordt dit gerenderd of alleen geteld", want dat
    # onderscheid is de helft van het werk. ────────────────────────────────

    # (a) Resultaat-dictionaries die NERGENS gerenderd worden. Gecontroleerd:
    # DeepCleanDeleteResult.ResultsByPath en LeftoverDeleteResult.ResultsByPath
    # worden door geen enkele Dialog of Page gebonden — de UI leest alleen
    # SuccessCount / FailedCount / BytesFreed. Dit zijn dus dode weergave-
    # strings. Bewust NIET vertaald: onzichtbare tekst kun je niet verifiëren
    # (zelfde regel als de 24 subcategorie-descriptions in v1.2.6). Als ze ooit
    # wél gerenderd gaan worden, hoort deze uitzondering te vervallen.
    'Removed registry key': ('DeepCleanService', 'LeftoverScannerService'),
    'Removed MUIcache value': ('DeepCleanService', 'LeftoverScannerService'),
    'Deleted shortcut': ('DeepCleanService', 'LeftoverScannerService'),
    'Deleted folder': ('DeepCleanService', 'LeftoverScannerService'),
    'Unknown type': ('LeftoverScannerService',),
    # Let op: exact dezelfde drie zinnen wórden wél vertaald in
    # BloatwareService, MixedSourceUninstaller, TweakService en WingetService.
    # Daarom is deze uitzondering bestand-gebonden — een waarde-gebonden ALLOW
    # zou een regressie daar nooit meer melden.
    'Could not start elevated process': ('DeepCleanService', 'LeftoverScannerService'),
    'Cancelled — UAC prompt declined': ('DeepCleanService', 'LeftoverScannerService'),
    'Did not run (interrupted)': ('DeepCleanService', 'LeftoverScannerService'),

    # (b) Interne invarianten. Deze verschijnen alleen als een HARDCODED pad of
    # index in onze eigen code niet klopt — een bug, geen gebruikersfout. Ze
    # blijven Engels zodat een bugmelding leesbaar is voor wie 'm moet fixen.
    # (Een deel ervan landt via FailureMessages wél in een InfoBar, dus het is
    # geen "onbereikbaar" maar een bewuste keuze.)
    'Invalid registry path: {fullPath}': None,
    'Invalid registry path: {fullKeyPath}': None,
    'Unsupported hive: {hiveName}': None,
    'Invalid sub path: {subPath}': None,
    'Parent key not found: {parentPath}': None,
    'Key not found: {subPath}': None,
    'Value name required for MUIcache delete': None,
    'Cannot open key: {entry.Path}': ('SnapshotService',),
    'Invalid choice index': ('TweakService',),


    # (d) Windows' eigen namen: mapnamen uit de protected-list en registry-
    # value-namen waarop gematcht wordt. Zelfde identifier-regel als v1.2.7.
    'User Data': ('DeepCleanService',),
    'Install Path': ('DeepCleanService',),
    'Common Files': ('DeepCleanService',),
    'Internet Explorer': ('DeepCleanService',),
    'Windows Defender': ('DeepCleanService',),
    'Windows Mail': ('DeepCleanService',),
    'Windows Media Player': ('DeepCleanService',),
    'Windows NT': ('DeepCleanService',),
    'Windows Photo Viewer': ('DeepCleanService',),
    'Windows Portable Devices': ('DeepCleanService',),
    'Windows Sidebar': ('DeepCleanService',),
    'Microsoft Update Health Tools': ('DeepCleanService',),
    'Application Verifier': ('DeepCleanService',),
    'Network Shortcuts': ('DeepCleanService',),
    'Start Menu': ('DeepCleanService',),
    'Application Data': ('DeepCleanService',),
    'Local Settings': ('DeepCleanService',),
    'Adobe AIR': ('DeepCleanService',),

    # (e) De naam van een context-menu-verb ZOALS DIE IN HET REGISTER STAAT.
    # Vertalen zou de geschreven registry-waarde veranderen, en de revert van
    # die tweak matcht op de key — dat is dus geen vertaling maar een
    # gedragswijziging. Staat als los punt in NEXT-STEPS.md.
    'Take Ownership': ('TweakService',),
    'Open Terminal as Admin': ('TweakService',),

    # (f) LOC-MISS-redenen in de vertaal-service zelf. Deze gaan naar
    # install.log en zijn per definitie niet vertaalbaar: ze bestaan juist om
    # te melden dát de tabel iets niet kon leveren.
    'geen vertaling': ('LocalizationService',),
    'onbekende key': ('LocalizationService',),

    # (g) De sentinel die NIET vertaald mag worden. Wordt op drie plekken
    # vergeleken; de weergave loopt los via install.state.alreadyInstalled.
    # Zie de comment bij de declaratie in WingetService.
    'Al geïnstalleerd': ('WingetService',),

    # (h) Diagnostische context die als argument aan FriendlyError meegaat: de
    # naam van het winget-commando dat faalde, niet een zin voor de gebruiker.
    'winget upgrade': ('WingetService',),

    # (i) Afkortingen in de Debloat-teller: MS = Microsoft, OEM = de
    # vendor-bundleware. Besloten in v1.2.7 — het woord "apps" ernaast loopt
    # wél via common.appCount. Zichtbaar geworden nu pass 4 ook literals met
    # één woord meldt.
    '{ms} MS': ('DebloatPage',),
    '{oem} OEM': ('DebloatPage',),

    # (j) v1.2.8.1 — het voorvoegsel van een winget-stdout-regel waarop
    # FriendlyOutputLine matcht. Dit is winget's tekst, niet die van ons: we
    # vergelijken erop om 'm te kunnen vertálen. Het voorkomen achter
    # StartsWith( wordt al door NOT_UI_CALL gefilterd; dit is het tweede, in de
    # index-expressie die de URL erachter afknipt.
    'Downloading ': ('WingetService',),
}


def allowed(v, rel):
    """Staat deze literal in ALLOW, en zo ja: geldt dat ook in dit bestand?"""
    if v not in ALLOW:
        return False
    files = ALLOW[v]
    return files is None or any(f in rel for f in files)

# Technische waarden die in beide nieuwe passes nooit tekst zijn: SCREAMING_CASE
# constanten (REG_DWORD, HKLM), paden, en Segoe-glyph-namen.
TECHNICAL = re.compile(r'^[A-Z][A-Z0-9_]+$|[\\/]|^%[A-Z]|^shell:|^ms-')


def is_glyph(v):
    """Segoe Fluent Icons-glyphs en emoji zijn geen tekst."""
    return v.strip() != '' and all(ord(c) > 0x2000 or c.isspace() for c in v)


def walk(ext):
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in filenames:
            if fn.endswith(ext):
                yield os.path.join(dirpath, fn)


def skip(v, rel, xaml=False):
    # Bij een geïnterpoleerde string telt alleen wat BUITEN de {…} staat: een
    # $"({item.SizeLabel})" draagt geen vertaalbare tekst, alleen haakjes.
    literal = re.sub(r'\{[^{}]*\}', '', v)
    if not re.search(r'[A-Za-zÀ-ſ]', literal):
        return True
    if xaml and SKIP_XAML_VALUE.match(v):
        return True
    return (len(v) < 2 or allowed(v, rel) or SKIP_VALUE.match(v) or is_glyph(v))


xaml_hits = []
for path in walk('.xaml'):
    for i, line in enumerate(open(path, encoding='utf-8'), 1):
        for attr in TEXT_ATTRS:
            for m in re.finditer(attr + r'="([^"]*)"', line):
                v = m.group(1)
                rel = os.path.relpath(path, ROOT)
                if not skip(v, rel, xaml=True):
                    xaml_hits.append((rel, i, attr, v))

# De C#-pass werkt op STATEMENTS, niet op regels. Een regel-gebaseerde pass mist
# twee vormen die allebei echt in de app stonden (v1.2.6-les):
#   Text = $"Deleting {n} item(s)..."          <- $ vóór het aanhalingsteken
#   Text = cond                                <- ternary over drie regels; op de
#       ? $"Safe to clean"                        anker-regel staat geen literal
#       : $"Caution — review carefully",
# Vandaar: zoek het anker, pak alles tot het einde van het statement, en kijk
# naar élke literal daarbinnen.
CS_ANCHOR = re.compile(
    r'\b(?:\w+\.)?(Text|Content|Title|Message|PlaceholderText|PrimaryButtonText|'
    r'SecondaryButtonText|CloseButtonText|Header|Description|DisplayName)\s*=(?!=)'
    r'|\b(body|title|header|caption|confirmText|cancelText|message|placeholder|label)\s*:')

# v1.2.8 — een VERBATIM string (@"…") kent geen backslash-escapes: daar is ""
# de enige escape. De oude vorm behandelde beide hetzelfde, en liep daardoor
# stuk op @"HKLM\SOFTWARE\" — de afsluitende \" werd als escape gelezen en de
# match liep door tot ver in de volgende regels. Onschuldig in Dialogs/, maar
# Services/ staat vol registry-paden die op een backslash eindigen, en dat
# leverde tientallen halve literals op. Nu twee aparte takken; lit() geeft de
# tak terug die daadwerkelijk gevuld is.
# De eerste tak eet CHAR-literals op zonder ze te melden. Dat moet vóór de rest
# staan: in DeepCleanService komt .Trim('"') voor, en zonder deze tak begint de
# match op het aanhalingsteken bínnen die char-literal. Alles ná dat punt loopt
# dan één quote uit de pas — je krijgt matches die van een AFSLUITEND
# aanhalingsteken tot het volgende OPENENDE lopen. Eén zo'n literal in het
# bestand vervuilde zo tientallen treffers verderop.
LITERAL = re.compile(r"'(?:[^'\\]|\\.)*'"
                     r'|\$?@"((?:[^"]|"")*)"'
                     r'|\$?"((?:[^"\\]|\\.)*)"')


def lit(m):
    """De inhoud van de literal, of None als het een char-literal was."""
    return m.group(1) if m.group(1) is not None else m.group(2)

# Einde van het statement: een ';', of het begin van de volgende property in een
# object-initializer, of een sluitende accolade. Zonder die grens zou de scan
# doorlopen tot de volgende ';' en resource-keys als "BodyTextBlockStyle"
# meepikken.
STATEMENT_END = re.compile(r';|\n\s*(?:\w+\s*=(?!=)|\}|\)\s*;)')

# Regels die naar een logfile of een proces gaan zijn geen UI.
NOT_UI = re.compile(r'Diagnostics\.Log|LogInstall|FileName\s*=|Arguments\s*=')

# Een literal die hier direct achteraan komt is géén weergavetekst maar een
# sleutel: de key van een vertaling, of een XAML-resourcenaam.
KEY_ARG = re.compile(
    r'(?:Loc\.(?:S|Plural|Raw|Has)|Resources|nameof|GetValue|StartsWith|EndsWith|'
    r'Contains|Equals|Split|Replace|Combine|Log|ToString)\s*[\(\[]\s*$')

# Een dotted identifier zonder spaties is een sleutel, geen zin. Vangt de keys op
# die binnen een ternary in Loc.S(...) staan, waar KEY_ARG niet bij kan, plus de
# losse suffixen die aan een key geplakt worden (Key + ".desc").
KEY_LIKE = re.compile(r'^\.\w+$|^[A-Za-z][\w]*(?:\.[\w]+)+$')


def line_of(text, pos):
    return text.count('\n', 0, pos) + 1


def strip_comments(text):
    """Vervangt //-commentaar door spaties zodat posities kloppen blijven."""
    out = list(text)
    for m in re.finditer(r'//[^\n]*', text):
        # niet binnen een string-literal? grove maar afdoende check: een even
        # aantal aanhalingstekens vóór de // op dezelfde regel
        start = text.rfind('\n', 0, m.start()) + 1
        if text.count('"', start, m.start()) % 2 == 0:
            for i in range(m.start(), m.end()):
                out[i] = ' '
    return ''.join(out)


cs_hits = []
for path in walk('.cs'):
    text = strip_comments(open(path, encoding='utf-8').read())
    for anchor in CS_ANCHOR.finditer(text):
        name = anchor.group(1) or anchor.group(2)
        tail = text[anchor.end():anchor.end() + 600]
        end = STATEMENT_END.search(tail)
        span = tail[:end.start()] if end else tail
        if NOT_UI.search(text[max(0, anchor.start() - 40):anchor.end()]):
            continue
        for m in LITERAL.finditer(span):
            v = lit(m)
            if v is None:
                continue
            if skip(v, os.path.relpath(path, ROOT)) or KEY_LIKE.match(v):
                continue
            pos = anchor.end() + m.start()
            if KEY_ARG.search(text[max(0, pos - 40):pos]):
                continue
            cs_hits.append((os.path.relpath(path, ROOT), line_of(text, pos), name, v))

# ── Pass 3: switch-armen (blinde vlek 3) ───────────────────────────────────
# `=> "..."` heeft geen toewijzing en dus geen anker voor de pass hierboven.
# Dit is de vorm waarin ~50 badge- en statuslabels stonden: CategoryLabel,
# TypeBadgeText, ConfidenceLabel, SourceBadgeText, StageLabel, StateLabel en
# de sectiekoppen van LeftoverCleanupDialog.
SWITCH_ARM = re.compile(r'=>\s*(\$?@"(?:[^"]|"")*"|\$?"(?:[^"\\]|\\.)*")')

arm_hits = []
for path in walk('.cs'):
    text = strip_comments(open(path, encoding='utf-8').read())
    for m in SWITCH_ARM.finditer(text):
        v = lit(LITERAL.match(m.group(1)))
        if v is None:
            continue
        rel = os.path.relpath(path, ROOT)
        if skip(v, rel) or KEY_LIKE.match(v) or TECHNICAL.search(v):
            continue
        arm_hits.append((rel, line_of(text, m.start()), v))

# ── Pass 4: zin-achtige literals in de UI-lagen (blinde vlek 2) ────────────
# Echte dataflow-analyse zou nodig zijn om "opgebouwd in een lokale var, pas
# later aan .Text toegewezen" te herkennen. In plaats daarvan: in de mappen die
# per definitie UI zijn, ELKE literal met twee of meer echte woorden melden.
#
# BEWUSTE BEPERKING: Services/ valt hier NIET onder. Daar staan honderden
# PowerShell-fragmenten, registry-paden en exit-code-teksten. Sinds v1.2.8 zit
# Services/ er wél bij: de foutmeldingen uit de elevated batches zijn toen
# vertaald, en de ruis wordt niet met een ALLOW-lijst van honderden regels
# weggepoetst maar met de aanroep-context hieronder (een literal die een
# argument van Log(…) of AppendLine(…) is, is per definitie geen UI-tekst).
UI_DIRS = ('Dialogs', 'Pages', 'Helpers', 'Services')

# Een literal die direct achter een van deze aanroepen staat is een sleutel, een
# resourcenaam of een technisch argument — geen weergavetekst.
NOT_UI_CALL = re.compile(
    r'(?:Loc\.(?:S|Plural|Raw|Has)|Resources|nameof|GetValue|SetValue|OpenSubKey|'
    r'StartsWith|EndsWith|Contains|Equals|IndexOf|Split|Replace|Combine|Join|'
    r'Log|LogInstall|Trim\w*|ToString|Format|Parse|Append\w*|WriteLine|Write|'
    r'Match|IsMatch|Regex\.\w+)\s*[\(\[]\s*$')

# Toewijzingen die naar een proces gaan in plaats van naar het scherm. Deze
# blijft op de RÉGEL van de literal kijken — het zijn eenregelige toewijzingen.
NOT_UI_LOOKBACK = re.compile(r'FileName\s*=|Arguments\s*=')

# v1.2.8 — de aanroep waarvan de literal een ARGUMENT is. Dit verving een
# terugkijk over 160 tekens ruwe tekst, die in Dialogs/ nog net kon maar in
# Services/ alles wegvaagde: daar staat vrijwel altijd wel een Diagnostics.Log
# binnen 160 tekens, dus pass 4 zou daar structureel 0 melden en dus groen
# liegen. Nu wordt de bijbehorende haakjes-opening opgezocht en de naam ervóór
# gelezen, zodat alleen de literals die écht in zo'n aanroep zitten wegvallen.
#
#   Log / LogInstall / Diagnostics.Log  → gaat naar een logfile, niet naar het scherm.
#   Append / AppendLine / AppendFormat  → bouwt een PowerShell-script of een
#       reg-batch. Nagerekend: in Dialogs/, Pages/ en Helpers/ staan drie
#       Append-aanroepen, geen ervan bouwt UI-tekst (een char-normalisatie in
#       DeepCleanDialog en File.AppendAllText in Diagnostics). UI-tekst wordt in
#       deze app nooit met een StringBuilder opgebouwd; string.Join wel, en die
#       staat hier bewust NIET tussen.
#
# Let op de hoofdletter-ongevoeligheid op de log-namen: de scan-methodes in
# DeepCleanService en LeftoverScannerService krijgen hun logger als lokale
# `Action<string> log` mee, dus daar heet de aanroep `log(` met een kleine
# letter. Zonder die vlag zou elke scan-trace als UI-tekst gemeld worden.
NOT_UI_ENCLOSING = re.compile(
    r'(?:^|\.)(?:[Ll]og|LogInstall|Append|AppendLine|AppendFormat|'
    r'WriteLine|WriteAllText|AppendAllText)$')

CALL_NAME = re.compile(r'([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*$')


def mask_strings(text):
    """Vervangt de INHOUD van elke string- en char-literal door 'x', met behoud
    van lengte en dus van elke positie. Nodig voor de haakjes-telling hieronder:
    de PowerShell-fragmenten in Services/ staan vol met losse haakjes
    ($"if ((Test-Path …)") die de telling anders volledig uit de pas laten
    lopen."""
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '@' and i + 1 < n and text[i + 1] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1
            for k in range(i + 2, min(j, n)):
                out[k] = 'x'
            i = j + 1
        elif c in '"\'':
            j = i + 1
            while j < n:
                if text[j] == '\\':
                    j += 2
                    continue
                if text[j] == c:
                    break
                j += 1
            for k in range(i + 1, min(j, n)):
                out[k] = 'x'
            i = j + 1
        else:
            i += 1
    return ''.join(out)


def enclosing_call(masked, pos, window=800):
    """Naam van de aanroep waarvan de literal op `pos` een argument is, of ''.
    Loopt terug tot de haakjes-opening die nog niet gesloten is."""
    depth = 0
    i = pos - 1
    stop = max(0, pos - window)
    while i >= stop:
        c = masked[i]
        if c == ')':
            depth += 1
        elif c == '(':
            if depth == 0:
                m = CALL_NAME.search(masked[max(0, i - 80):i])
                return m.group(1) if m else ''
            depth -= 1
        i -= 1
    return ''


# v1.2.8 — categorische filters voor pass 4, gemaakt toen Services/ erbij kwam.
# Bewust regels in plaats van losse ALLOW-regels: die map levert honderden
# PowerShell-fragmenten, winget-argumenten en registry-value-namen op, en een
# ALLOW-lijst van die omvang leest niemand meer na — dan is 'ie geen
# verantwoording meer maar een dempknop.
#
# 1. Een PowerShell-fragment of een command-line: herkenbaar aan een verb-noun
#    ("Get-AppxPackage"), een PS-variabele ($_ / $ErrorActionPreference) of een
#    --vlag. Geen van drieën komt in weergavetekst voor.
SCRIPT_OR_ARG = re.compile(
    r'(?:^|\s)--\w|\$_|\$Error|-ErrorAction|'
    r'\b(?:Get|Set|New|Remove|Add|Clear|Checkpoint|Where|Out|Select|Start|Stop|'
    r'Test|Import|Export|Invoke|Write)-[A-Z]\w+')


def real_words(v):
    """Woorden BUITEN de interpolatie-gaten — $"({item.SizeLabel})" telt er nul."""
    return re.findall(r'[A-Za-zÀ-ſ]{2,}', re.sub(r'\{[^{}]*\}', ' ', v))


def is_identifier(v):
    """Geen enkele spatie buiten de interpolatie-gaten = een identifier, geen zin.
    Vangt Start_Layout, SubscribedContent-338387Enabled, browser_download_url,
    SetupToolbox_tweaks_{…}.ps1, nl-NL en --install-runner in één regel.

    ALLEEN voor pass 4, en dat is essentieel: pass 4 zoekt naar ZIN-achtige
    literals en een zin heeft spaties. Pass 3 (de switch-armen) moet juist wél
    op losse woorden blijven melden — daar stond "Installed" als badge, en die
    heeft ook geen spatie."""
    return not re.search(r'\s', re.sub(r'\{[^{}]*\}', '', v))


def is_artifact(v):
    """Geneste aanhalingstekens binnen een interpolatie ($"…{string.Join(", ",
    parts)}…") breken de LITERAL-regex: die knipt op het eerste binnenste
    aanhalingsteken en levert een halve string op. Herkenbaar aan een { zonder
    bijbehorende }."""
    return v.count('{') != v.count('}')


sentence_hits = []
for path in walk('.cs'):
    rel = os.path.relpath(path, ROOT)
    if not rel.startswith(UI_DIRS):
        continue
    text = strip_comments(open(path, encoding='utf-8').read())
    masked = mask_strings(text)
    for m in LITERAL.finditer(text):
        v = lit(m)
        if v is None:
            continue
        # v1.2.8: was "< 2". Die drempel miste een hele vorm — een literal met
        # één woord en een interpolatie-gat. Concreet stond in
        # MixedSourceUninstaller `$"Uninstalling {entry.DisplayName}..."` in de
        # Nederlandse UI: na het weghalen van het gat blijft er één woord over,
        # dus de pass sloeg 'm over. Gevonden tijdens de negatieve test van deze
        # patch, niet door de scan zelf. Nu op "< 1", en dat kost níéts: met de
        # is_identifier- en SCRIPT_OR_ARG-regels erbij blijft de uitkomst 0.
        if len(real_words(v)) < 1 or is_artifact(v):
            continue
        if allowed(v, rel) or KEY_LIKE.match(v) or TECHNICAL.search(v) or is_glyph(v):
            continue
        if is_identifier(v) or SCRIPT_OR_ARG.search(v):
            continue
        # XAML-resourcenamen (BodyTextBlockStyle, AccentButtonStyle, …)
        if re.match(r'^[A-Z][A-Za-z]*(Style|Brush)$', v):
            continue
        back = text[max(0, m.start() - 160):m.start()]
        if NOT_UI_CALL.search(back) or NOT_UI_LOOKBACK.search(back.split('\n')[-1]):
            continue
        if NOT_UI_ENCLOSING.search(enclosing_call(masked, m.start())):
            continue
        sentence_hits.append((rel, line_of(text, m.start()), v))

# Pass 3 en 4 overlappen op een switch-arm in Dialogs/ — één keer melden.
seen = {(f, i) for f, i, _ in arm_hits}
sentence_hits = [h for h in sentence_hits if (h[0], h[1]) not in seen]

print('=== XAML literals in tekst-attributen: %d ===' % len(xaml_hits))
for f, i, a, v in xaml_hits:
    print('  %s:%d  %s="%s"' % (f, i, a, v[:110]))

print()
print('=== C# literals op tekst-properties: %d ===' % len(cs_hits))
for f, i, a, v in cs_hits:
    print('  %s:%d  .%s = "%s"' % (f, i, a, v[:110]))

print()
print('=== C# switch-armen met een literal: %d ===' % len(arm_hits))
for f, i, v in arm_hits:
    print('  %s:%d  => "%s"' % (f, i, v[:110]))

print()
print('=== C# zin-achtige literals in %s: %d ==='
      % ('/'.join(UI_DIRS), len(sentence_hits)))
for f, i, v in sentence_hits:
    print('  %s:%d  "%s"' % (f, i, v[:110]))

print()
if xaml_hits or cs_hits or arm_hits or sentence_hits:
    print('GEVONDEN: er staat nog niet-vertaalde gebruiker-zichtbare tekst in de app.')
    sys.exit(1)
print('Schoon: geen hardcoded gebruiker-zichtbare tekst meer.')
print('Pass 4 dekt sinds v1.2.8 ook Services/; %d uitzonderingen staan met reden '
      'in ALLOW.' % len(ALLOW))
