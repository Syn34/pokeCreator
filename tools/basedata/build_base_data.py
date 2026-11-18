#!/usr/bin/env python3
"""
Builds PokeCreator's built-in database (data/base/*.json).

Starts from Pokémon Essentials v21.1 data (imported with PokeCreatorImport, Gens 1-8)
and adds everything Essentials is missing from the PokeAPI dataset
(https://github.com/PokeAPI/pokeapi, data/v2/csv): the Legends: Arceus and Gen 9
Pokémon (#899-1025), Paldean forms, and the moves, abilities and items they need.

Usage:
  python3 build_base_data.py <essentials-import Data folder> <pokeapi csv folder> <output folder>

The output is plain JSON in PokeCreator's format. Run `PokeCreatorImport --normalize`
style tooling (or just open/save in the Studio) to re-format it if needed.
"""
import csv, json, os, re, sys, collections

ess_dir, api_dir, out_dir = sys.argv[1], sys.argv[2], sys.argv[3]
EN = '9'  # PokeAPI English language id

def load_json(name):
    with open(os.path.join(ess_dir, name), encoding='utf-8') as f:
        return json.load(f)

def rows(name):
    with open(os.path.join(api_dir, name + '.csv'), encoding='utf-8') as f:
        return list(csv.DictReader(f))

def norm(identifier):
    """PokeAPI identifier -> Essentials-style ID (double-edge -> DOUBLEEDGE)."""
    special = {'nidoran-f': 'NIDORANfE', 'nidoran-m': 'NIDORANmA'}
    if identifier in special: return special[identifier]
    return re.sub(r'[^A-Z0-9]', '', identifier.upper())

def clean(text):
    return re.sub(r'\s+', ' ', (text or '').replace('\f', ' ').replace('­', '')).strip()

def latest_text(table, key_col, text_col, version_col):
    """Latest English flavour text per key."""
    best = {}
    for r in rows(table):
        if r.get('language_id') != EN: continue
        k, v = r[key_col], int(r[version_col] or 0)
        if k not in best or v >= best[k][0]:
            best[k] = (v, clean(r[text_col]))
    return {k: v[1] for k, v in best.items()}

def names(table, key_col, col='name'):
    return {r[key_col]: r[col] for r in rows(table) if r.get('local_language_id') == EN}

# ------------------------------------------------------------------ Essentials base
species = load_json('species.json')
moves = load_json('moves.json')
abilities = load_json('abilities.json')
items = load_json('items.json')
types = load_json('types.json')
trainer_types = load_json('trainer_types.json')
have_species = {s['Id'] for s in species}
have_moves = {m['Id'] for m in moves}
have_abilities = {a['Id'] for a in abilities}
have_items = {i['Id'] for i in items}

# ------------------------------------------------------------------ PokeAPI lookups
type_by_id = {r['id']: norm(r['identifier']) for r in rows('types')}
move_rows = {r['id']: r for r in rows('moves') if int(r['id']) < 10000}
move_name = names('move_names', 'move_id')
move_desc = latest_text('move_flavor_text', 'move_id', 'flavor_text', 'version_group_id')
move_id_by_api = {k: norm(r['identifier']) for k, r in move_rows.items()}
move_flags = collections.defaultdict(list)
FLAG = {'1': 'Contact', '4': 'CanProtect', '7': 'CanMirrorMove', '8': 'Punching', '9': 'Sound', '11': 'ThawsUser',
        '15': 'Powder', '16': 'Biting', '17': 'Pulse', '18': 'Bomb', '21': 'Dance'}
for r in rows('move_flag_map'):
    if r['move_flag_id'] in FLAG: move_flags[r['move_id']].append(FLAG[r['move_flag_id']])
TARGET = {'1': 'None', '2': 'NearOther', '3': 'NearAlly', '4': 'UserSide', '5': 'UserOrNearAlly', '6': 'FoeSide', '7': 'User',
          '8': 'RandomNearFoe', '9': 'AllNearOthers', '10': 'NearOther', '11': 'AllNearFoes', '12': 'BothSides',
          '13': 'UserAndAllies', '14': 'AllBattlers', '15': 'AllAllies', '16': 'None'}
CATEGORY = {'1': 'Status', '2': 'Physical', '3': 'Special'}

ability_rows = {r['id']: r for r in rows('abilities') if r['is_main_series'] == '1' and int(r['id']) < 10000}
ability_name = names('ability_names', 'ability_id')
ability_desc = latest_text('ability_flavor_text', 'ability_id', 'flavor_text', 'version_group_id')
ability_id_by_api = {k: norm(r['identifier']) for k, r in ability_rows.items()}

item_rows = {r['id']: r for r in rows('items')}
item_name = names('item_names', 'item_id')
item_desc = latest_text('item_flavor_text', 'item_id', 'flavor_text', 'version_group_id')
item_pocket = {r['id']: int(r['pocket_id']) for r in rows('item_categories')}
item_id_by_api = {k: norm(r['identifier']) for k, r in item_rows.items()}

sp_rows = {r['id']: r for r in rows('pokemon_species')}
sp_names = {r['pokemon_species_id']: r for r in rows('pokemon_species_names') if r['local_language_id'] == EN}
sp_dex = latest_text('pokemon_species_flavor_text', 'species_id', 'flavor_text', 'version_id')
species_id_by_api = {k: norm(r['identifier']) for k, r in sp_rows.items()}
pk_rows = {r['id']: r for r in rows('pokemon')}
default_pk = {r['species_id']: r['id'] for r in pk_rows.values() if r['is_default'] == '1'}
stats = collections.defaultdict(dict)
for r in rows('pokemon_stats'): stats[r['pokemon_id']][r['stat_id']] = (int(r['base_stat']), int(r['effort']))
pk_types = collections.defaultdict(list)
for r in sorted(rows('pokemon_types'), key=lambda r: int(r['slot'])): pk_types[r['pokemon_id']].append(type_by_id[r['type_id']])
pk_abil = collections.defaultdict(list)
for r in sorted(rows('pokemon_abilities'), key=lambda r: int(r['slot'])): pk_abil[r['pokemon_id']].append((r['ability_id'], r['is_hidden'] == '1'))
egg_groups = collections.defaultdict(list)
EGG = {'1': 'Monster', '2': 'Water1', '3': 'Bug', '4': 'Flying', '5': 'Field', '6': 'Fairy', '7': 'Grass', '8': 'Humanlike', '9': 'Water3',
       '10': 'Mineral', '11': 'Amorphous', '12': 'Water2', '13': 'Ditto', '14': 'Dragon', '15': 'Undiscovered'}
for r in rows('pokemon_egg_groups'): egg_groups[r['species_id']].append(EGG.get(r['egg_group_id'], 'Undiscovered'))
GROWTH = {'1': 'Slow', '2': 'Medium', '3': 'Fast', '4': 'Parabolic', '5': 'Erratic', '6': 'Fluctuating'}
GENDER = {'-1': 'Genderless', '0': 'AlwaysMale', '1': 'FemaleOneEighth', '2': 'Female25Percent', '4': 'Female50Percent',
          '6': 'Female75Percent', '7': 'FemaleSevenEighths', '8': 'AlwaysFemale'}
COLOR = {'1': 'Black', '2': 'Blue', '3': 'Brown', '4': 'Gray', '5': 'Green', '6': 'Pink', '7': 'Purple', '8': 'Red', '9': 'White', '10': 'Yellow'}
SHAPE = {'1': 'Head', '2': 'Serpentine', '3': 'Finned', '4': 'HeadArms', '5': 'HeadBase', '6': 'BipedalTail', '7': 'HeadLegs', '8': 'Quadruped',
         '9': 'Winged', '10': 'Multiped', '11': 'MultiBody', '12': 'Bipedal', '13': 'MultiWinged', '14': 'Insectoid'}
HABITAT = {'1': 'Cave', '2': 'Forest', '3': 'Grassland', '4': 'Mountain', '5': 'Rare', '6': 'RoughTerrain', '7': 'Sea', '8': 'Urban', '9': 'WatersEdge'}

# Learnsets: prefer Scarlet/Violet (25), then Legends: Arceus (24), Legends: Z-A (30)
learn = collections.defaultdict(lambda: collections.defaultdict(list))
for r in rows('pokemon_moves'):
    learn[r['pokemon_id']][r['version_group_id']].append(r)

def learnset(pokemon_id):
    vgs = learn.get(pokemon_id, {})
    for vg in ['25', '24', '30'] + sorted(vgs.keys(), key=int, reverse=True):
        if vg in vgs and vgs[vg]: return vgs[vg]
    return []

# ------------------------------------------------------------------ moves / abilities
needed_moves, needed_abilities, needed_items = set(), set(), set()

def default_flags(r):
    # PokeAPI has no flag data for recent moves: assume moves aimed at others can be Protected against and mirrored
    return ['CanProtect', 'CanMirrorMove'] if TARGET.get(r['target_id']) not in ('User', 'UserSide', 'FoeSide', 'BothSides', 'AllAllies', 'UserAndAllies', 'None') else []

def add_move(api_id):
    mid = move_id_by_api.get(api_id)
    if not mid or mid in have_moves: return mid
    r = move_rows[api_id]
    m = {'Id': mid, 'Name': move_name.get(api_id, mid.title()), 'Type': type_by_id.get(r['type_id'], 'NORMAL'),
         'Category': CATEGORY.get(r['damage_class_id'], 'Status'), 'Power': int(r['power'] or 0), 'Accuracy': int(r['accuracy'] or 0),
         'TotalPP': int(r['pp'] or 5), 'Target': TARGET.get(r['target_id'], 'NearOther'), 'Priority': int(r['priority'] or 0),
         'FunctionCode': 'None', 'EffectChance': int(r['effect_chance'] or 0), 'Flags': move_flags.get(api_id) or default_flags(r),
         'Description': move_desc.get(api_id, '')}
    moves.append(m); have_moves.add(mid)
    return mid

def add_ability(api_id):
    aid = ability_id_by_api.get(api_id)
    if not aid or aid in have_abilities: return aid
    abilities.append({'Id': aid, 'Name': ability_name.get(api_id, aid.title()), 'Description': ability_desc.get(api_id, '')})
    have_abilities.add(aid)
    return aid

def add_item(api_id, field_use=None):
    if api_id not in item_rows: return None
    iid = item_id_by_api[api_id]
    if iid in have_items: return iid
    r = item_rows[api_id]
    name = item_name.get(api_id, iid.title())
    it = {'Id': iid, 'Name': name, 'NamePlural': name + ('es' if name.endswith(('s', 'x', 'h')) else 's'),
          'Pocket': item_pocket.get(r['category_id'], 1), 'Price': int(r['cost'] or 0), 'Description': item_desc.get(api_id, '')}
    if field_use: it['FieldUse'] = field_use
    items.append(it); have_items.add(iid)
    return iid

# Every main-series move and ability that Essentials doesn't have yet
for api_id in sorted(move_rows, key=int): add_move(api_id)
for api_id in sorted(ability_rows, key=int): add_ability(api_id)

# Gen 9 held items and evolution items
EXTRA_ITEMS = ['ability-shield', 'clear-amulet', 'mirror-herb', 'punching-glove', 'covert-cloak', 'loaded-dice', 'booster-energy', 'fairy-feather',
               'auspicious-armor', 'malicious-armor', 'scroll-of-darkness', 'scroll-of-waters', 'syrupy-apple', 'unremarkable-teacup',
               'masterpiece-teacup', 'metal-alloy', 'leaders-crest', 'gimmighoul-coin', 'black-augurite', 'peat-block', 'linking-cord',
               'galarica-cuff', 'galarica-wreath', 'tera-orb', 'ability-patch']
api_item_by_ident = {r['identifier']: k for k, r in item_rows.items()}
for ident in EXTRA_ITEMS:
    if ident in api_item_by_ident: add_item(api_item_by_ident[ident], 'OnPokemon' if 'armor' in ident or 'scroll' in ident or 'apple' in ident or 'teacup' in ident or ident in ('metal-alloy', 'black-augurite', 'peat-block', 'linking-cord', 'galarica-cuff', 'galarica-wreath') else None)

# ------------------------------------------------------------------ species
def build_species(api_sp, api_pk, sid, form=0, form_name=None):
    s = sp_rows[api_sp]; p = pk_rows[api_pk]; st = stats[api_pk]
    nm = sp_names.get(api_sp, {})
    base = [st.get(k, (1, 0))[0] for k in ('1', '2', '3', '6', '4', '5')]
    ev_names = {'1': 'HP', '2': 'ATTACK', '3': 'DEFENSE', '4': 'SPECIAL_ATTACK', '5': 'SPECIAL_DEFENSE', '6': 'SPEED'}
    evs = [{'Stat': ev_names[k], 'Value': v[1]} for k, v in sorted(st.items(), key=lambda kv: int(kv[0])) if v[1] > 0]
    ab = [add_ability(a) for a, hidden in pk_abil[api_pk] if not hidden]
    hab = [add_ability(a) for a, hidden in pk_abil[api_pk] if hidden]
    ls = learnset(api_pk)
    level_moves = sorted([r for r in ls if r['pokemon_move_method_id'] == '1'], key=lambda r: (int(r['level'] or 0), int(r['order'] or 0)))
    lm = []
    for r in level_moves:
        mid = add_move(r['move_id'])
        if mid: lm.append({'Level': int(r['level'] or 0), 'Move': mid})
    egg = sorted({add_move(r['move_id']) for r in ls if r['pokemon_move_method_id'] == '2'} - {None})
    tutor = sorted({add_move(r['move_id']) for r in ls if r['pokemon_move_method_id'] in ('3', '4')} - {None})
    genus = (nm.get('genus') or '').replace(' Pokémon', '').strip()
    d = {'Id': sid if form == 0 else sid + '_' + str(form), 'Species': sid, 'Name': nm.get('name', sid.title()),
         'Category': genus, 'Pokedex': sp_dex.get(api_sp, ''), 'Types': pk_types[api_pk], 'BaseStats': base,
         'Abilities': [a for a in ab if a], 'HiddenAbilities': [a for a in hab if a], 'EVs': evs, 'BaseExp': int(p['base_experience'] or 100),
         'GrowthRate': GROWTH.get(s['growth_rate_id'], 'Medium'), 'CatchRate': int(s['capture_rate'] or 45),
         'Happiness': int(s['base_happiness'] or 50), 'Moves': lm, 'TutorMoves': tutor, 'EggMoves': egg, 'Evolutions': [],
         'GenderRatio': GENDER.get(s['gender_rate'], 'Female50Percent'), 'EggGroups': egg_groups.get(api_sp, ['Undiscovered']),
         'HatchSteps': int(s['hatch_counter'] or 20) * 256, 'Height': int(p['height']) / 10.0, 'Weight': int(p['weight']) / 10.0,
         'Color': COLOR.get(s['color_id'], 'Red'), 'Shape': SHAPE.get(s['shape_id'], 'Head'), 'Generation': int(s['generation_id'])}
    if s.get('habitat_id'): d['Habitat'] = HABITAT.get(s['habitat_id'], 'None')
    if form: d['Form'] = form; d['FormName'] = form_name
    return d

added_species = []
for api_sp in sorted(sp_rows, key=int):
    sid = species_id_by_api[api_sp]
    if int(api_sp) > 1025 or sid in have_species or api_sp not in default_pk: continue
    d = build_species(api_sp, default_pk[api_sp], sid)
    added_species.append((api_sp, d))

# Regional forms of older species that Essentials v21.1 doesn't include (Hisuian and Paldean)
REGIONAL = [(i, i.split('-')[0].upper(), 'Hisuian Form') for i in
            ['growlithe-hisui', 'arcanine-hisui', 'voltorb-hisui', 'electrode-hisui', 'typhlosion-hisui', 'qwilfish-hisui', 'sneasel-hisui',
             'samurott-hisui', 'lilligant-hisui', 'zorua-hisui', 'zoroark-hisui', 'braviary-hisui', 'sliggoo-hisui', 'goodra-hisui',
             'avalugg-hisui', 'decidueye-hisui']] + [
            ('basculin-white-striped', 'BASCULIN', 'White-Striped'),
            ('tauros-paldea-combat-breed', 'TAUROS', 'Paldean Form (Combat Breed)'),
            ('tauros-paldea-blaze-breed', 'TAUROS', 'Paldean Form (Blaze Breed)'),
            ('tauros-paldea-aqua-breed', 'TAUROS', 'Paldean Form (Aqua Breed)'),
            ('wooper-paldea', 'WOOPER', 'Paldean Form')]
pk_by_ident = {r['identifier']: k for k, r in pk_rows.items()}
form_entries = []
form_id_by_ident = {}
have_form_names = {(x['Species'], (x.get('FormName') or '').lower()) for x in species}
for ident, sid, fname in REGIONAL:
    if ident not in pk_by_ident or sid not in have_species or (sid, fname.lower()) in have_form_names: continue
    taken = [x.get('Form', 0) for x in species if x['Species'] == sid] + [x['Form'] for x in form_entries if x['Species'] == sid]
    form = max(taken) + 1
    api_pk = pk_by_ident[ident]
    d = build_species(pk_rows[api_pk]['species_id'], api_pk, sid, form, fname)
    form_entries.append(d)
    form_id_by_ident[ident] = d['Id']

# Evolutions between regional forms
for a, b, method, param in [('growlithe-hisui', 'arcanine-hisui', 'Item', 'FIRESTONE'), ('voltorb-hisui', 'electrode-hisui', 'Item', 'LEAFSTONE'),
                            ('zorua-hisui', 'zoroark-hisui', 'Level', '30'), ('sliggoo-hisui', 'goodra-hisui', 'LevelRain', '50')]:
    if a in form_id_by_ident and b in form_id_by_ident:
        src = next(x for x in form_entries if x['Id'] == form_id_by_ident[a])
        tgt = next(x for x in form_entries if x['Id'] == form_id_by_ident[b])
        src['Evolutions'] = [{'Species': tgt['Species'], 'Method': method, 'Parameter': param}]  # form carries over, like Essentials

# ------------------------------------------------------------------ evolutions
def evo_method(r):
    trig = r['evolution_trigger_id']
    item = item_id_by_api.get(r['trigger_item_id']) if r['trigger_item_id'] else None
    held = item_id_by_api.get(r['held_item_id']) if r['held_item_id'] else None
    if item: add_item(r['trigger_item_id'], 'OnPokemon')
    if held: add_item(r['held_item_id'])
    tod = r.get('time_of_day') or ''
    sfx = 'Day' if tod == 'day' else 'Night' if tod == 'night' else ''
    if trig == '1':
        if r['minimum_level']:
            if r['gender_id'] == '1': return 'LevelFemale', r['minimum_level']
            if r['gender_id'] == '2': return 'LevelMale', r['minimum_level']
            return 'Level' + sfx, r['minimum_level']
        if r['minimum_happiness'] or r.get('minimum_affection'): return 'Happiness' + sfx, ''
        if r['known_move_id']: return 'HasMove', move_id_by_api.get(r['known_move_id'], '')
        if r['known_move_type_id']: return 'HasMoveType', type_by_id.get(r['known_move_type_id'], '')
        if held: return ('DayHoldItem' if sfx == 'Day' else 'NightHoldItem' if sfx == 'Night' else 'HoldItem'), held
        if r['party_species_id']: return 'HasInParty', species_id_by_api.get(r['party_species_id'], '')
        return 'Event', ''
    if trig == '2':
        if held: return 'TradeItem', held
        if r.get('trade_species_id'): return 'TradeSpecies', species_id_by_api.get(r['trade_species_id'], '')
        return 'Trade', ''
    if trig == '3' and item:
        if r['gender_id'] == '1': return 'ItemFemale', item
        if r['gender_id'] == '2': return 'ItemMale', item
        return 'Item' + sfx, item
    return 'Event', ''

# Some new evolutions come from a regional form of the old species
FROM_FORM = {t: form_id_by_ident[i] for t, i in [('CLODSIRE', 'wooper-paldea'), ('SNEASLER', 'sneasel-hisui'), ('OVERQWIL', 'qwilfish-hisui'), ('BASCULEGION', 'basculin-white-striped')] if i in form_id_by_ident}
by_id = {s['Id']: s for s in species}
for _, d in added_species: by_id[d['Id']] = d
for d in form_entries: by_id[d['Id']] = d
added_ids = {d['Id'] for _, d in added_species}
seen = set()
for r in rows('pokemon_evolution'):
    target = species_id_by_api.get(r['evolved_species_id'])
    if target not in added_ids or target in seen: continue
    seen.add(target)
    pre = sp_rows[r['evolved_species_id']]['evolves_from_species_id']
    if not pre: continue
    src = FROM_FORM.get(target, species_id_by_api[pre])
    if src not in by_id: src = species_id_by_api[pre]
    method, param = evo_method(r)
    by_id[src].setdefault('Evolutions', []).append({'Species': target, 'Method': method, 'Parameter': str(param)})

# ------------------------------------------------------------------ assemble (forms right after their base species)
species.extend(d for _, d in added_species)
for d in form_entries:
    idx = max(i for i, s in enumerate(species) if s.get('Species') == d['Species'])
    species.insert(idx + 1, d)

os.makedirs(out_dir, exist_ok=True)
for name, data in [('species', species), ('moves', moves), ('abilities', abilities), ('items', items), ('types', types), ('trainer_types', trainer_types)]:
    with open(os.path.join(out_dir, name + '.json'), 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)

base_count = sum(1 for s in species if not s.get('Form'))
print('species entries %d (%d base species, %d added, %d forms added)' % (len(species), base_count, len(added_species), len(form_entries)))
print('moves %d, abilities %d, items %d' % (len(moves), len(abilities), len(items)))
