#!/usr/bin/env python3
"""Valida mars_baseline_gale.v0.json (v0.4): carga, sin duplicados, campos obligatorios, ningún PROPOSED, ningún valor de Glavin,
v0.1, v0.2 y v0.3 intactas (hash), cambios de v0.4 (R6-01) respecto a v0.3, cambios de v0.3 (conversion/derived_from, fuentes, porosidad, used_in_v0, rationale) y emulación del cargador C#."""
import json, sys, hashlib, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, HERE)
import loader_emulation as LE
# Historical files stay beside this script. The engine reads data/scenarios.
R = HERE
MD5_V01 = "88567494bb620effeb14a4ffd122a037"
MD5_V02 = "5881462cafddbb0e8a50465fe94bdcbe"
SHA256_V03 = "17bc66f63c2372b215c70173efdfc3220e9b0ae5ea073c20cce38f1a88949aa8"
V04_INTERVALS = {"env.gale.air_temperature.diurnal_amplitude": [45, 75], "env.gale.ground_temperature.diurnal_amplitude": [55, 100],
                 "env.gale.radiation.gcr_absorbed_dose_rate_cycle_range": [0.1, 0.39], "env.gale.radiation.sep_dose_per_event": [17, 445]}
f = os.path.join(ROOT, "data", "scenarios", "MarsBaseline", "mars_baseline_gale.v0.json")
raw = open(f, encoding="utf-8").read(); d = json.loads(raw)
errs = []; ids = set()
for p in d["parameters"]:
    i = p.get("id")
    if i in ids: errs.append(f"duplicado {i}")
    ids.add(i)
    for k in ("unit", "classification", "scope", "version", "status"):
        if p.get(k) in (None, ""): errs.append(f"{i}: falta {k}")
    pend = p.get("status") == "PENDING"
    if pend and p.get("value") is not None: errs.append(f"{i}: PENDING con valor")
    if not pend and p.get("value") is None: errs.append(f"{i}: sin value y no PENDING")
    if p.get("classification") == "GAME_BALANCE":
        if not p.get("rationale"): errs.append(f"{i}: GAME_BALANCE sin rationale")
    elif not p.get("source") and not ((p.get("note") or "").strip() or (p.get("rationale") or "").strip()): errs.append(f"{i}: sin source ni note/rationale")
    if p.get("status") in ("PROPOSED", "PROVISIONAL"): errs.append(f"{i}: estado {p['status']} no permitido en FINAL")
ids_ = {p["id"]: p for p in d["parameters"]}
m = d["metadata"]
if m.get("version") != "0.4" or m.get("status") != "FINAL" or not m.get("date"): errs.append("metadata version/status/date")
if "loader_rule_T03" not in m: errs.append("falta loader_rule_T03")
if "glavin" in raw.lower(): errs.append("aparece 'Glavin' en el JSON")
chk = {"env.gale.air_temperature.reference": 222.0, "env.gale.pressure.reference": 841.8, "env.gale.ground_temperature.annual_mean": 223.5, "env.gale.regolith.perchlorate_default_v0": 0.55}
for k, v in chk.items():
    if ids_.get(k, {}).get("value") != v: errs.append(f"{k} != {v}")
for k in ("env.gale.air_temperature.midrange", "env.gale.pressure.midrange"):
    if ids_[k]["status"] != "HISTORICAL" or ids_[k].get("used_in_v0") is not False: errs.append(f"{k} no histórico")
for p in d["parameters"]:
    if "perchlorate" in p["id"] and p["id"] not in ("env.gale.regolith.perchlorate_gale_range", "env.gale.regolith.perchlorate_default_v0", "env.analog.phoenix.perchlorate"):
        errs.append("perclorato inesperado " + p["id"])
# --- v0.2: mapa de campos y used_in_v0 ---
fm = m.get("v0_field_map", {})
if not fm: errs.append("falta metadata.v0_field_map")
mapped = set()
for fld, e in fm.items():
    i = e.get("id")
    if i is None:
        if e.get("status") != "UNASSIGNED": errs.append(f"{fld}: sin id y sin UNASSIGNED")
        continue
    mapped.add(i)
    if i not in ids_: errs.append(f"{fld}: id inexistente {i}"); continue
    if ids_[i].get("used_in_v0") is not True: errs.append(f"{fld}: {i} sin used_in_v0=true")
    if ids_[i]["status"] == "PENDING": errs.append(f"{fld}: {i} es PENDING")
for p in d["parameters"]:
    if p.get("used_in_v0") is True and p["id"] not in mapped: errs.append(f"{p['id']}: used_in_v0=true fuera del mapa")
    if p["status"] == "PENDING" and p.get("used_in_v0") is True: errs.append(f"{p['id']}: PENDING con used_in_v0")
for k in ("env.gale.uv.irradiance_noon_avg", "env.gale.regolith.porosity"):
    if ids_[k].get("used_in_v0") is True: errs.append(f"{k} marcada used_in_v0")
if "T04/T05" not in ids_["env.gale.uv.irradiance_noon_avg"]["note"]: errs.append("UV sin nota T04/T05")
if "PerchlorateMassKg" not in fm or fm["PerchlorateMassKg"].get("id") is not None: errs.append("PerchlorateMassKg debe estar sin asignar")
for k in ("TemperatureKelvin","PressurePascal","SolarIrradiance","IonizingAbsorbedDoseGyPerSol","IonizingEquivalentDoseSvPerSol","PerchlorateWtPercent","WaterIceWtPercent","SepEnabled"):
    if fm.get(k, {}).get("id") is None: errs.append(f"{k} debe estar mapeada")
for k in ("PerchlorateMassKg","UvRadiation","SoilPorosity","WaterVapor"):
    if k not in fm or fm[k].get("id") is not None or fm[k].get("status") != "UNASSIGNED": errs.append(f"{k} debe ser UNASSIGNED")
for k in ("Temperature","Pressure","SolarSurfaceIrradianceWm2","WaterIceSurface"):
    if k in fm: errs.append(f"clave antigua {k} en el mapa")
# GCR: valor nativo MEASURED, conversión ESTIMATED derivada, escenario GAME_BALANCE
g = ids_
if (g["env.gale.radiation.gcr_absorbed_dose_rate"]["classification"], g["env.gale.radiation.gcr_absorbed_dose_rate"]["value"]) != ("SCIENTIFIC_MEASURED", 0.210): errs.append("GCR nativo")
v = g["env.gale.radiation.gcr_absorbed_dose_rate_v0"]
if v["classification"] != "SCIENTIFIC_ESTIMATED" or v["value"] != 2.158e-4 or "conversion" not in v: errs.append("GCR v0 valor")
if abs(0.210e-3*88775/86400 - 2.158e-4) > 5e-8: errs.append("conversión GCR no cuadra")
sc = g["env.gale.radiation.gcr_scenario_v0"]
if sc["classification"] != "GAME_BALANCE" or not sc.get("rationale"): errs.append("GCR escenario")
if "T y P constantes" not in m.get("v0_temporal_policy", ""): errs.append("falta política temporal T/P")
import os
prev = json.load(open(f"{R}/mars_baseline_gale.v0.1.json"))
if prev["metadata"]["version"] != "0.1": errs.append("v0.1 alterada")


# ================= v0.3 =================
def md5(path): return hashlib.md5(open(path, "rb").read()).hexdigest()
if md5(f"{R}/mars_baseline_gale.v0.1.json") != MD5_V01: errs.append("v0.1 alterada (md5)")
if md5(f"{R}/mars_baseline_gale.v0.2.json") != MD5_V02: errs.append("v0.2 alterada (md5)")
v02 = json.load(open(f"{R}/mars_baseline_gale.v0.2.json"))
if v02["metadata"]["version"] != "0.2": errs.append("v0.2: metadata.version != 0.2")
if m.get("previous_version_file") != "mars_baseline_gale.v0.3.json": errs.append("previous_version_file != v0.3")
if not m.get("changelog_v0_4"): errs.append("falta metadata.changelog_v0_4")
# --- v0.3 intacta (sha256) y v0.4 = v0.3 salvo version, changelog_v0_4, previous_version_file y los 4 value de R6-01 ---
sha256 = lambda path: hashlib.sha256(open(path, "rb").read()).hexdigest()
if sha256(f"{R}/mars_baseline_gale.v0.3.json") != SHA256_V03: errs.append("v0.3 alterada (sha256)")
v03 = json.load(open(f"{R}/mars_baseline_gale.v0.3.json"))
if v03["metadata"]["version"] != "0.3": errs.append("v0.3: metadata.version != 0.3")
m3 = {k: v for k, v in v03["metadata"].items() if k not in ("version", "previous_version_file")}
m4 = {k: v for k, v in m.items() if k not in ("version", "previous_version_file", "changelog_v0_4")}
if m3 != m4: errs.append("v0.4: metadata distinta de v0.3 (aparte de version, previous_version_file, changelog_v0_4): " + str(sorted(k for k in set(m3) | set(m4) if m3.get(k) != m4.get(k))))
if d["location"] != v03["location"]: errs.append("v0.4: location distinta de v0.3")
if [p["id"] for p in d["parameters"]] != [p["id"] for p in v03["parameters"]]: errs.append("v0.4: ids/orden distintos de v0.3")
else:
    for p4, p3 in zip(d["parameters"], v03["parameters"]):
        i = p4["id"]
        a = {k: v for k, v in p4.items() if k != "version"}; b = {k: v for k, v in p3.items() if k != "version"}
        if i in V04_INTERVALS:
            if p3["value"] != {"env.gale.air_temperature.diurnal_amplitude": "45–75", "env.gale.ground_temperature.diurnal_amplitude": "55–100",
                               "env.gale.radiation.gcr_absorbed_dose_rate_cycle_range": "0,100–0,390", "env.gale.radiation.sep_dose_per_event": "17–445"}[i]: errs.append(f"{i}: v0.3 no tenía el texto esperado")
            if p4["value"] != V04_INTERVALS[i] or p4["range"] != V04_INTERVALS[i] or not all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in p4["value"]): errs.append(f"{i}: value debe ser {V04_INTERVALS[i]} numérico")
            a.pop("value"); b.pop("value")
        if a != b: errs.append(f"{i}: cambió algo más que value/version respecto a v0.3")
if not m.get("changelog_v0_3"): errs.append("falta metadata.changelog_v0_3")
if m.get("scope_id") != "regional:gale": errs.append("metadata.scope_id")
for p in d["parameters"]:
    if p["version"] != "0.4": errs.append(f"{p['id']}: version {p['version']} != 0.4")
    if "derivation" in p: errs.append(f"{p['id']}: queda `derivation` (v0.3: `conversion`)")
# v0_field_map: igual que en v0.2 y con la anotación de WaterIce/Scope
if m["v0_field_map"] != v02["metadata"]["v0_field_map"]: errs.append("v0_field_map cambió respecto a v0.2")
if len(fm) != 12: errs.append("v0_field_map debe tener 12 claves (8 mapeadas + 4 UNASSIGNED)")
if "WaterIce" not in m["v0_field_map_keys"] or "Scope" not in m["v0_field_map_keys"] or "metadata.scope" not in m["v0_field_map_keys"]: errs.append("v0_field_map_keys sin la anotación WaterIce/Scope")
# ids de v0.2 siguen y solo se añade la nativa de Guo; valores/unidades/clasificación intactos salvo lo declarado
old_ids = {p["id"]: p for p in v02["parameters"]}
added = set(ids_) - set(old_ids); removed = set(old_ids) - set(ids_)
if added != {"env.gale.radiation.gcr_dose_equivalent_rate_native"} or removed: errs.append(f"ids: añadidas {added}, quitadas {removed}")
for i, o in old_ids.items():
    n = ids_[i]
    for k in ("value", "unit", "scope", "range"):
        if k == "value" and i in V04_INTERVALS: continue  # v0.4 (R6-01), comprobado arriba contra v0.3
        if n.get(k) != o.get(k): errs.append(f"{i}: cambió {k} respecto a v0.2")
    if n["classification"] != o["classification"] and i != "env.gale.regolith.porosity": errs.append(f"{i}: cambió classification respecto a v0.2")
    if n["status"] != o["status"]: errs.append(f"{i}: cambió status respecto a v0.2")
# a) conversion / derived_from
derived = [p for p in d["parameters"] if p.get("derived_from") or p.get("conversion")]
if {p["id"] for p in derived} != {"env.gale.radiation.gcr_absorbed_dose_rate_v0", "env.gale.radiation.gcr_dose_equivalent_rate"}: errs.append("entradas derivadas inesperadas: " + str([p["id"] for p in derived]))
K = 88775 / 86400
for p in derived:
    c = p.get("conversion"); i = p["id"]
    if not isinstance(c, dict) or set(c) != {"expression", "factor", "factor_classification", "assumptions"}: errs.append(f"{i}: conversion debe ser {{expression, factor, factor_classification, assumptions}}"); continue
    if c["factor_classification"] != "MODEL_ASSUMPTION" or abs(c["factor"] - K) > 1e-15: errs.append(f"{i}: factor/clasificación de conversion")
    if "88775/86400" not in c["expression"]: errs.append(f"{i}: expression sin la fracción exacta 88775/86400")
    if c["assumptions"] != ["día RAD = 24 h = 86 400 s", "1 sol = 88 775 s (ADR-011, redondeo MODEL_ASSUMPTION)"]: errs.append(f"{i}: assumptions")
    if p["classification"] != "SCIENTIFIC_ESTIMATED": errs.append(f"{i}: derivado debe ser SCIENTIFIC_ESTIMATED")
    nat = ids_.get(p["derived_from"])
    if nat is None or nat.get("derived_from"): errs.append(f"{i}: derived_from no apunta a una nativa"); continue
    if nat["classification"] not in ("SCIENTIFIC_MEASURED", "SCIENTIFIC_ESTIMATED"): errs.append(f"{i}: nativa no científica")
    if not any(s.get("doi") for s in p["source"]): errs.append(f"{i}: derivado sin DOI en source")
# valores derivados = nativo × K con el redondeo declarado
nat_abs, v_abs = ids_["env.gale.radiation.gcr_absorbed_dose_rate"], ids_["env.gale.radiation.gcr_absorbed_dose_rate_v0"]
if v_abs["derived_from"] != nat_abs["id"] or nat_abs["unit"] != "mGy/day" or round(nat_abs["value"]*1e-3*K, 7) != v_abs["value"]: errs.append("Gy/sol != 0,210 mGy/día × 88775/86400 (a 4 cifras)")
nat_sv, v_sv = ids_["env.gale.radiation.gcr_dose_equivalent_rate_native"], ids_["env.gale.radiation.gcr_dose_equivalent_rate"]
if v_sv["derived_from"] != nat_sv["id"] or nat_sv["unit"] != "mSv/day" or round(nat_sv["value"]*1e-3*K, 6) != v_sv["value"]: errs.append("Sv/sol != 0,49 mSv/día × 88775/86400 (a 3 cifras)")
if nat_abs["classification"] != "SCIENTIFIC_MEASURED" or nat_sv["classification"] != "SCIENTIFIC_ESTIMATED": errs.append("clasificación de las nativas GCR")
# b) uncertainty puede ser objeto (se verifica con la emulación r5_target más abajo)
# c) tres fuentes
for i in ("env.gale.air_temperature.midrange", "env.gale.pressure.midrange", "env.gale.solar.surface_irradiance_daily_mean_power"):
    srcs = ids_[i]["source"]
    if len(srcs) != 1 or not all(srcs[0].get(k) for k in ("citation", "accessed", "doi", "where")) or "ENTRADAS del cálculo" not in srcs[0]["where"]: errs.append(f"{i}: fuente mal formada")
    if "derived_from" in srcs[0]: errs.append(f"{i}: queda derived_from dentro de source")
# d) porosidad
po = ids_["env.gale.regolith.porosity"]
if (po["classification"], po["status"], po["value"], po["source"]) != ("UNCLASSIFIED_PENDING", "PENDING", None, []) or not po.get("rationale"): errs.append("porosidad debe ser UNCLASSIFIED_PENDING/PENDING/null/sin source y con rationale")
# e) SUPERADO y HISTORICAL => used_in_v0 false
for p in d["parameters"]:
    if p["status"] in ("SUPERADO", "HISTORICAL") and p.get("used_in_v0") is not False: errs.append(f"{p['id']}: {p['status']} sin used_in_v0=false")
if ids_["env.gale.radiation.gcr_dose_equivalent_rate_hassler2014"]["status"] != "SUPERADO": errs.append("Hassler 0,64 mSv/día debe seguir SUPERADO")
# f) no científico sin fuente => rationale o note; sin fuentes de relleno
for p in d["parameters"]:
    for s_ in p["source"]:
        if s_.get("citation") in ("ver note", "Sin fuente verificada (ver note)"): errs.append(f"{p['id']}: fuente de relleno")
    if p["classification"] not in ("SCIENTIFIC_MEASURED", "SCIENTIFIC_ESTIMATED") and not p["source"] and not ((p.get("note") or "").strip() or (p.get("rationale") or "").strip()):
        errs.append(f"{p['id']}: no científica sin fuente y sin rationale/note")
# ================= emulación del cargador C# =================
raw3 = open(f, encoding="utf-8").read()
res = {}
for mode in ("r5_target", "main_unc", "main"):
    e_, defs_ = LE.load_text(raw3, mode); res[mode] = e_
if res["r5_target"]: errs += [f"cargador(r5_target) {i}: {msg}" for i, msg in res["r5_target"]]
# todos los campos mapeados se entregan con RequireNumber/RequireBool y los UNASSIGNED no se entregan
_, defs = LE.load_text(raw3, "r5_target")
for fld, e_ in fm.items():
    if e_.get("id") is None: continue
    why = LE.deliverable(defs, e_["id"], "bool" if fld == "SepEnabled" else "number")
    if why: errs.append(f"cargador: {fld} -> {e_['id']} no se entrega ({why})")
for i in ("env.gale.regolith.porosity", "env.gale.uv.daily_irradiation"):
    if LE.deliverable(defs, i, "number") != "Pending": errs.append(f"cargador: {i} debe fallar como Pending")
for i in ("env.gale.radiation.gcr_dose_equivalent_rate_hassler2014", "env.gale.air_temperature.midrange", "env.gale.pressure.midrange"):
    if LE.deliverable(defs, i, "number") not in ("NotUsedInV0", "Historical", "Superseded"): errs.append(f"cargador: {i} no debe entregarse")
# v0.2 sigue cargando como antes (informativo) y el cargador de main se resume
print(f"cargador r5_target (especificado en la revisión R5): {len(d['parameters'])-len(res['r5_target'])}/{len(d['parameters'])} entradas cargan; fallan {len(res['r5_target'])}")
print(f"cargador main 1f3c7f3 (== PR #5 75399ae, SIN cambios R5): fallan {len(res['main'])} (uncertainty objeto: {sum('uncertainty' in m_ for _, m_ in res['main'])}); con solo R5-03a aún fallan: {[i for i, _ in res['main_unc']]}")
e_real, _ = LE.load_text(raw3, "real_7f2fb77")
n_ok = len(d['parameters']) - len(e_real)
print(f"cargador REAL 7f2fb77 (PR #6): {n_ok}/{len(d['parameters'])} entradas cargan; fallan {len(e_real)}")
for i, msg in e_real:
    print("   REAL 7f2fb77:", i, "->", msg); errs.append(f"cargador(real_7f2fb77) {i}: {msg}")
if len(d['parameters']) != 51 or n_ok != 51: errs.append(f"cargador real_7f2fb77: {n_ok}/{len(d['parameters'])}, se exige 51/51")
# con el cargador real también se entregan los campos del mapa
_, defs_real = LE.load_text(raw3, "real_7f2fb77")
for fld, e_ in fm.items():
    if e_.get("id") is None: continue
    why = LE.deliverable(defs_real, e_["id"], "bool" if fld == "SepEnabled" else "number")
    if why: errs.append(f"cargador(real_7f2fb77): {fld} -> {e_['id']} no se entrega ({why})")
for i in ("env.gale.regolith.porosity", "env.gale.uv.daily_irradiation"):
    if LE.deliverable(defs_real, i, "number") != "Pending": errs.append(f"cargador(real_7f2fb77): {i} debe fallar como Pending")
print(len(d["parameters"]), "entradas;", "OK" if not errs else errs)
print("PENDING:", m["pending"])
sys.exit(1 if errs else 0)
