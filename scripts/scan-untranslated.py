# -*- coding: utf-8 -*-
"""Zoekt gebruiker-zichtbare tekst die nog een hardcoded literal is.

Gebouwd tijdens v1.2.5, nadat bleek dat fase 2 er 16 had laten staan — waaronder
InfoBar-bodies waarvan de titel wél vertaald was, dus precies het soort misser dat
je met de hand niet ziet. Draai dit voor en na een vertaalfase.

    py scripts/scan-untranslated.py

Drie passes:
  XAML  tekst-dragende attributen met een letterlijke waarde in plaats van een
        markup-extension ({loc:Localize ...}) of een binding.
  C#    string-literals die aan .Text / .Content / .Title / .Message /
        *ButtonText / .Header worden toegekend.
  C#    named arguments die naar de UI stromen (body:, title:, confirmText: ...)
        en model-properties die in XAML gebonden worden (Description, DisplayName).

Uitgebreid in v1.2.6 na drie missers: de C#-pass eiste een aanhalingsteken direct
na het "=", waardoor GEÏNTERPOLEERDE strings ($"Show {n} locations") er compleet
doorheen glipten — en named arguments werden helemaal niet bekeken. Beide vormen
worden nu wel gezien.

Exit-code 1 zodra er iets gevonden wordt, zodat het in een check kan hangen.
Valse positieven horen in ALLOW hieronder, mét reden.

LET OP — hij meldt op dit moment BEWUST 3 treffers. Dat zijn echte, nog niet
omgezette strings die pas zichtbaar werden nadat de `^{`-bug hierboven gefixt was;
ze staan met de rest van de restjes ingepland als v1.2.7 in NEXT-STEPS.md. Zet ze
NIET in ALLOW om de exit-code groen te krijgen — dan liegt de scanner weer.
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

ALLOW = {
    # app-naam: identiek in beide talen en wordt bij het starten sowieso uit code gezet
    'Setup Toolbox',
}


def is_glyph(v):
    """Segoe Fluent Icons-glyphs en emoji zijn geen tekst."""
    return v.strip() != '' and all(ord(c) > 0x2000 or c.isspace() for c in v)


def walk(ext):
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in filenames:
            if fn.endswith(ext):
                yield os.path.join(dirpath, fn)


def skip(v, xaml=False):
    # Bij een geïnterpoleerde string telt alleen wat BUITEN de {…} staat: een
    # $"({item.SizeLabel})" draagt geen vertaalbare tekst, alleen haakjes.
    literal = re.sub(r'\{[^{}]*\}', '', v)
    if not re.search(r'[A-Za-zÀ-ſ]', literal):
        return True
    if xaml and SKIP_XAML_VALUE.match(v):
        return True
    return (len(v) < 2 or v in ALLOW or SKIP_VALUE.match(v) or is_glyph(v))


xaml_hits = []
for path in walk('.xaml'):
    for i, line in enumerate(open(path, encoding='utf-8'), 1):
        for attr in TEXT_ATTRS:
            for m in re.finditer(attr + r'="([^"]*)"', line):
                v = m.group(1)
                if not skip(v, xaml=True):
                    xaml_hits.append((os.path.relpath(path, ROOT), i, attr, v))

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
            if skip(v) or KEY_LIKE.match(v):
                continue
            pos = anchor.end() + m.start()
            if KEY_ARG.search(text[max(0, pos - 40):pos]):
                continue
            cs_hits.append((os.path.relpath(path, ROOT), line_of(text, pos), name, v))

print('=== XAML literals in tekst-attributen: %d ===' % len(xaml_hits))
for f, i, a, v in xaml_hits:
    print('  %s:%d  %s="%s"' % (f, i, a, v[:110]))

print()
print('=== C# literals op tekst-properties: %d ===' % len(cs_hits))
for f, i, a, v in cs_hits:
    print('  %s:%d  .%s = "%s"' % (f, i, a, v[:110]))

print()
if xaml_hits or cs_hits:
    print('GEVONDEN: er staat nog niet-vertaalde gebruiker-zichtbare tekst in de app.')
    sys.exit(1)
print('Schoon: geen hardcoded gebruiker-zichtbare tekst meer.')
