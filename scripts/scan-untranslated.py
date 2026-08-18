# -*- coding: utf-8 -*-
"""Zoekt gebruiker-zichtbare tekst die nog een hardcoded literal is.

Gebouwd tijdens v1.2.5, nadat bleek dat fase 2 er 16 had laten staan — waaronder
InfoBar-bodies waarvan de titel wél vertaald was, dus precies het soort misser dat
je met de hand niet ziet. Draai dit voor en na een vertaalfase.

    py scripts/scan-untranslated.py

Twee passes:
  XAML  tekst-dragende attributen met een letterlijke waarde in plaats van een
        markup-extension ({loc:Localize ...}) of een binding.
  C#    string-literals die aan .Text / .Content / .Title / .Message /
        *ButtonText / .Header worden toegekend.

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

# leeg, puur numeriek/interpunctie, of een markup-extension: nooit vertaalbaar
SKIP_VALUE = re.compile(r'^(?:\{|\s*$|[\d\s.,:;/|+*=x×·—-]*$)')

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


def skip(v):
    return (len(v) < 2 or v in ALLOW or SKIP_VALUE.match(v) or is_glyph(v))


xaml_hits = []
for path in walk('.xaml'):
    for i, line in enumerate(open(path, encoding='utf-8'), 1):
        for attr in TEXT_ATTRS:
            for m in re.finditer(attr + r'="([^"]*)"', line):
                v = m.group(1)
                if not skip(v):
                    xaml_hits.append((os.path.relpath(path, ROOT), i, attr, v))

CS_PROP = re.compile(
    r'\b(?:\w+\.)?(Text|Content|Title|Message|PlaceholderText|PrimaryButtonText|'
    r'SecondaryButtonText|CloseButtonText|Header)\s*=\s*"((?:[^"\\]|\\.){2,})"')

cs_hits = []
for path in walk('.cs'):
    for i, line in enumerate(open(path, encoding='utf-8'), 1):
        s = line.strip()
        if s.startswith('//'):
            continue
        for m in CS_PROP.finditer(line):
            v = m.group(2)
            if not skip(v):
                cs_hits.append((os.path.relpath(path, ROOT), i, m.group(1), v))

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
