# -*- coding: utf-8 -*-
"""Bewijst dat elke vertaalbare tekst uit data/apps.json in BEIDE stringtabellen
bestaat — zonder de app te starten.

    py scripts/check-catalog-keys.py

Waarom dit er is (v1.2.6): sinds deze versie staan de categorie-namen, de
categorie- en subcategorie-omschrijvingen en de app-omschrijvingen niet meer in
apps.json maar in data/strings.<taal>.json, met de stabiele id / wingetId als
sleutel. Een app die je aan de catalogus toevoegt zonder de twee tabelregels
levert dan een LOC-MISS op in de draaiende app — dit script vangt dat af vóór
de build, en dekt meteen de hele catalogus in plaats van alleen wat je toevallig
op het scherm krijgt.

Controleert vier dingen:
  1. elke verwachte key bestaat in strings.en.json (de brontaal — verplicht) en
     in strings.nl.json;
  2. er staan geen catalogus-keys in de tabellen die nergens meer bij horen
     (een app verwijderen laat anders stille wees-keys achter);
  3. beide tabellen hebben exact dezelfde keyset;
  4. OMGEKEERD (v1.2.7): welke keys staan wél in de tabel maar worden nergens
     in de broncode aangeroepen?

Die vierde is er bijgekomen omdat 'ie precies de bug vindt die de scanner niet
kán zien. Een ongebruikte key betekent bijna altijd dat de call-site nog een
hardcoded literal heeft: `snapshot.entryLabel` stond drie versies lang netjes
vertaald in beide tabellen terwijl SnapshotBrowserDialog een Nederlandse
literal toonde. Dezelfde controle vond in v1.2.6 de duplicaat-keys.

Exit-code 1 zodra er iets mis is.
"""
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'data')
SRC = os.path.join(ROOT, 'src', 'SetupToolbox')

PREFIXES = ('appCategory.', 'appSubcategory.', 'catalogApp.')

# Key-families die RUNTIME worden opgebouwd en dus nooit letterlijk in de
# broncode staan. Ze zijn niet "ongebruikt" — ze hangen aan een stabiele id:
#   tweak.<Id>.name / .desc / .useCase / .choiceN   uit TweakService.BuildAll()
#   tweak.group.<naam>                              12 const string-declaraties
#   tweakCategory.<naam>.name / .blurb              TweakCategoryExtensions
#   bloatware.<key>.name / .desc, bloatware.cat.*   BloatwareItem.CuratedMetadata
#   appCategory.* / appSubcategory.* / catalogApp.* uit data/apps.json
# De eerste vier zouden een C#-parser vergen om exact af te leiden; de laatste
# drie worden hierboven al exact gecontroleerd tegen apps.json, dus daar voegt
# een tweede controle niets toe.
DYNAMIC_PREFIXES = ('tweak.', 'tweakCategory.', 'bloatware.') + PREFIXES

# Loc.Plural(base, n) plakt zelf .one / .other achter de key.
PLURAL_SUFFIXES = ('.one', '.other')

# Geen key maar een notitie in het bestand zelf.
NOT_A_KEY = ('_comment',)


def load(name):
    with open(os.path.join(DATA, name), encoding='utf-8-sig') as f:
        return json.load(f)


db = load('apps.json')
en = load('strings.en.json')
nl = load('strings.nl.json')

expected = set()
apps_seen = {}
for cat in db['categories']:
    expected.add(f"appCategory.{cat['id']}.name")
    expected.add(f"appCategory.{cat['id']}.desc")
    buckets = [cat.get('apps', [])]
    for sub in cat.get('subcategories', []):
        expected.add(f"appSubcategory.{sub['id']}.name")
        buckets.append(sub['apps'])
    for apps in buckets:
        for app in apps:
            expected.add(f"catalogApp.{app['wingetId']}.desc")
            apps_seen.setdefault(app['wingetId'], 0)
            apps_seen[app['wingetId']] += 1

present = {k for k in en if k.startswith(PREFIXES)}

problems = []

missing_en = sorted(expected - set(en))
missing_nl = sorted(expected - set(nl))
orphans = sorted(present - expected)

if missing_en:
    problems.append(('Ontbreekt in strings.en.json (brontaal)', missing_en))
if missing_nl:
    problems.append(('Ontbreekt in strings.nl.json', missing_nl))
if orphans:
    problems.append(('Wees-keys: staan in de tabel maar niet meer in apps.json', orphans))

only_en = sorted(set(en) - set(nl))
only_nl = sorted(set(nl) - set(en))
if only_en:
    problems.append(('Alleen in strings.en.json', only_en))
if only_nl:
    problems.append(('Alleen in strings.nl.json', only_nl))

# Een lege vertaling resolvet naar een lege regel in de UI in plaats van naar
# een zichtbare fout, dus die zou je nooit opmerken.
blanks = sorted(k for k in expected if not en.get(k, '').strip() or not nl.get(k, '').strip())
if blanks:
    problems.append(('Lege waarde in een van beide tabellen', blanks))

# ── 4. Omgekeerd: welke keys roept niemand aan? ────────────────────────────
# Elke key die letterlijk in een .cs of .xaml voorkomt telt als gebruikt. Voor
# Loc.Plural(base, n) staat alleen `base` in de code, dus daarvoor wordt ook de
# variant zonder .one/.other geprobeerd.
referenced = set()
for dirpath, dirnames, filenames in os.walk(SRC):
    dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
    for fn in filenames:
        if not (fn.endswith('.cs') or fn.endswith('.xaml')):
            continue
        with open(os.path.join(dirpath, fn), encoding='utf-8') as f:
            text = f.read()
        # in C# tussen aanhalingstekens, in XAML als Key=… in de extension
        referenced.update(re.findall(r'"([a-zA-Z][\w]*(?:\.[\w]+)+)"', text))
        referenced.update(re.findall(r'Key=([a-zA-Z][\w]*(?:\.[\w]+)+)', text))


def is_used(key):
    if key in referenced:
        return True
    for suffix in PLURAL_SUFFIXES:
        if key.endswith(suffix) and key[:-len(suffix)] in referenced:
            return True
    return False


dynamic = sorted(k for k in en if k.startswith(DYNAMIC_PREFIXES))
checkable = [k for k in en
             if k not in NOT_A_KEY and not k.startswith(DYNAMIC_PREFIXES)]
unused = sorted(k for k in checkable if not is_used(k))
if unused:
    problems.append(
        ('Ongebruikte keys: staan in de tabel maar worden nergens aangeroepen '
         '— sterk signaal dat de call-site nog een hardcoded literal heeft',
         unused))

cats = len(db['categories'])
subs = sum(len(c.get('subcategories', [])) for c in db['categories'])
records = sum(apps_seen.values())
duplicates = {k: v for k, v in apps_seen.items() if v > 1}

print(f'Catalogus : {cats} categorieën, {subs} subcategorieën, '
      f'{records} app-records ({len(apps_seen)} unieke wingetIds)')
if duplicates:
    print('            gekruiste apps (delen één vertaling): '
          + ', '.join(f'{k} ×{v}' for k, v in sorted(duplicates.items())))
print(f'Verwacht  : {len(expected)} keys')
print(f'Tabellen  : en={len(en)} keys, nl={len(nl)} keys')
print(f'Gebruik   : {len(checkable)} keys gecontroleerd op aanroep, '
      f'{len(dynamic)} overgeslagen omdat ze runtime opgebouwd worden '
      f'({", ".join(DYNAMIC_PREFIXES)})')

if problems:
    print()
    for title, items in problems:
        print(f'=== {title}: {len(items)} ===')
        for k in items[:40]:
            print(f'  {k}')
        if len(items) > 40:
            print(f'  ... en {len(items) - 40} meer')
    sys.exit(1)

print()
print('Compleet: elke catalogus-tekst bestaat in beide talen, geen wezen, '
      'geen lege waardes. Een LOC-MISS op de Apps-pagina is dus onmogelijk.')
print('Elke niet-dynamische key wordt ook daadwerkelijk aangeroepen.')
