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
  C#    ELKE zin-achtige literal in Dialogs/ en Pages/ die niet in een Loc-call,
        een log of een proces-argument zit.          [blinde vlek 2, v1.2.7]

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
hier staat — élke zin-achtige literal in Dialogs/ en Pages/ melden — vangt
hetzelfde en is te overzien, mits de ALLOW-lijst hieronder bijgehouden wordt.

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

LITERAL = re.compile(r'\$?@?"((?:[^"\\]|\\.)*)"')

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
            v = m.group(1)
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
SWITCH_ARM = re.compile(r'=>\s*(\$?@?"(?:[^"\\]|\\.)*")')

arm_hits = []
for path in walk('.cs'):
    text = strip_comments(open(path, encoding='utf-8').read())
    for m in SWITCH_ARM.finditer(text):
        v = LITERAL.match(m.group(1)).group(1)
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
# PowerShell-fragmenten, registry-paden en exit-code-teksten, en de foutmeldingen
# uit de elevated batches ("Cancelled — UAC prompt declined") zijn nog niet
# vertaald — die staan als apart item op NEXT-STEPS.md. Zet Services/ er pas bij
# als dat item af is, anders is de exit-code structureel rood.
UI_DIRS = ('Dialogs', 'Pages', 'Helpers')

# Een literal die direct achter een van deze aanroepen staat is een sleutel, een
# resourcenaam of een technisch argument — geen weergavetekst.
NOT_UI_CALL = re.compile(
    r'(?:Loc\.(?:S|Plural|Raw|Has)|Resources|nameof|GetValue|SetValue|OpenSubKey|'
    r'StartsWith|EndsWith|Contains|Equals|IndexOf|Split|Replace|Combine|Join|'
    r'Log|LogInstall|Trim\w*|ToString|Format|Parse|Append\w*|WriteLine|Write|'
    r'Match|IsMatch|Regex\.\w+)\s*[\(\[]\s*$')

# Let op: de terugkijk gaat over de rúwe tekst, niet over de regel. Een
# Diagnostics.Log("x.log",\n  $"...") staat op TWEE regels, en dan zit de marker
# niet op dezelfde regel als de literal.
NOT_UI_LOOKBACK = re.compile(
    r'Diagnostics\.Log|LogInstall|\blog\(|FileName\s*=|Arguments\s*=')


def real_words(v):
    """Woorden BUITEN de interpolatie-gaten — $"({item.SizeLabel})" telt er nul."""
    return re.findall(r'[A-Za-zÀ-ſ]{2,}', re.sub(r'\{[^{}]*\}', ' ', v))


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
    for m in LITERAL.finditer(text):
        v = m.group(1)
        if len(real_words(v)) < 2 or is_artifact(v):
            continue
        if allowed(v, rel) or KEY_LIKE.match(v) or TECHNICAL.search(v) or is_glyph(v):
            continue
        # XAML-resourcenamen (BodyTextBlockStyle, AccentButtonStyle, …)
        if re.match(r'^[A-Z][A-Za-z]*(Style|Brush)$', v):
            continue
        back = text[max(0, m.start() - 160):m.start()]
        if NOT_UI_CALL.search(back) or NOT_UI_LOOKBACK.search(back.split('\n')[-1]) \
                or NOT_UI_LOOKBACK.search(back):
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
print('Let op: Services/ valt buiten pass 4 — zie de toelichting hierboven.')
