#!/usr/bin/env python3
"""Emulación en Python de ParameterCatalog.Load (C#, repo CerealKillerjs/martis) para comprobar que un JSON de parámetros carga.

Modos:
  "real_7f2fb77" = el cargador del PR #6 (T03a-fix), head 7f2fb77, leído línea a línea (ParameterCatalog.cs, ParameterConversion.cs). ES EL QUE VALE.
                   Reglas nuevas respecto a main: R5-01 (derivado), R5-02 (conversion objeto), R5-03a (uncertainty texto u objeto), R5-04
                   (SUPERADO no se entrega), R5-06 (no científico sin fuente => note o rationale), rango cerrado (:373-385 y LiesInside :734-755).
  "main"      = el cargador TAL COMO ESTÁ en main 1f3c7f3 (== head 75399ae del PR #5, fusionado el 2026-11-16 03:49 hora de Roma;
                src/Martis.Domain/Parameters/ParameterCatalog.cs idéntico byte a byte). Reglas leídas línea a línea.
  "main_unc"  = diagnóstico: main + solo R5-03a (para ver qué más rechazaría main una vez aceptado `uncertainty` objeto).
  "r5_target" = el cargador con los cambios que Motor aceptó en la revisión R5 (review-pr5-75399ae.md): R5-01 (derivado nunca
                SCIENTIFIC_MEASURED; SCIENTIFIC_* solo si la nativa es SCIENTIFIC_* y lleva DOI/URL), R5-02 (`conversion` objeto
                {expression, factor, factor_classification, assumptions}), R5-03a (`uncertainty` texto u objeto), R5-06 (no científico sin
                fuente => rationale o note no vacíos). NO está publicado en ningún head a fecha de hoy: es la especificación, no el código.
No es el cargador C#: no se ha ejecutado dotnet. Hay que confirmarlo con el test real de T03b.
"""
import json, math

CLASSES = {"SCIENTIFIC_MEASURED", "SCIENTIFIC_ESTIMATED", "MODEL_ASSUMPTION", "GAME_BALANCE", "SPECULATIVE", "UNCLASSIFIED_PENDING"}
SCI = {"SCIENTIFIC_MEASURED", "SCIENTIFIC_ESTIMATED"}

class Fail(Exception): pass

def _isnum(x): return isinstance(x, (int, float)) and not isinstance(x, bool)
def _pairs(pairs):
    d = {}
    for k, v in pairs:
        if k in d: raise Fail(f"duplicate property '{k}'")
        d[k] = v
    return d
def _req_str(o, k):
    if k not in o: raise Fail(f"missing {k}")
    if not isinstance(o[k], str): raise Fail(f"{k} must be a string")
    if not o[k].strip(): raise Fail(f"{k} is empty")
    return o[k]
def _opt_text(o, k):
    v = o.get(k)
    if v is None: return None
    if not isinstance(v, str): raise Fail(f"{k} is not a string")
    return v
def _opt_str(o, k):
    v = _opt_text(o, k); return None if v is None or not v.strip() else v
def _interval(v):
    if not (isinstance(v, list) and len(v) == 2 and all(_isnum(x) and math.isfinite(x) for x in v)): raise Fail("array value is not [min, max]")

def load_text(raw, mode="main"):
    """Devuelve (errores, definiciones). errores = lista de (id, mensaje); [] = el documento carga."""
    errors = []
    try: root = json.loads(raw, object_pairs_hook=_pairs)
    except Exception as e: return [("$", f"JSON no válido: {e}")], []
    for k, kind in (("metadata", dict), ("location", dict), ("parameters", list)):
        if not isinstance(root.get(k), kind): return [("$", f"falta o tipo incorrecto: {k}")], []
    drafts = []
    for idx, e in enumerate(root["parameters"]):
        pid = e.get("id", f"parameters[{idx}]") if isinstance(e, dict) else f"parameters[{idx}]"
        try: drafts.append(_read_draft(e, mode))
        except Fail as ex: errors.append((pid, str(ex)))
    ids = [d["id"] for d in drafts]
    seen = set()
    for d in drafts:
        if d["id"] in seen: errors.append((d["id"], "Duplicate parameter id")); continue
        seen.add(d["id"])
        try: _resolve_derivation(d, drafts, mode)
        except Fail as ex: errors.append((d["id"], str(ex)))
    return errors, drafts

def _read_draft(e, mode):
    if not isinstance(e, dict): raise Fail("entry must be an object")
    d = {}
    d["id"] = _req_str(e, "id"); d["field"] = _req_str(e, "field")
    if "value" not in e: raise Fail("missing value. No default was invented.")
    v = e["value"]
    if isinstance(v, bool) or v is None or isinstance(v, str): pass
    elif _isnum(v):
        if not math.isfinite(v): raise Fail("number is not a finite double")
    elif isinstance(v, list): _interval(v)
    else: raise Fail("value kind is not number, boolean, string, array or null")
    d["value"] = v
    d["unit"] = _req_str(e, "unit")
    c = e.get("classification")
    if "classification" not in e: raise Fail("missing classification")
    if not isinstance(c, str): raise Fail("classification must be a string")
    if c not in CLASSES: raise Fail(f"unknown classification '{c}'")
    d["classification"] = c
    sc = _req_str(e, "scope")
    if not (sc == "planetary_mean" or (sc.startswith("regional:") and sc[9:].strip()) or (sc.startswith("analog:") and sc[7:].strip())):
        raise Fail(f"scope '{sc}'. Expected regional:<site>, planetary_mean or analog:<name>")
    d["scope"] = sc
    if "version" not in e: raise Fail("missing version")
    ver = e["version"]
    if _isnum(ver): vv = float(ver)
    elif isinstance(ver, str):
        try:
            if not ver or ver != ver.strip(): raise ValueError
            vv = float(ver)
        except ValueError: raise Fail(f"version '{ver}' is not a positive number")
    else: raise Fail("version must be a number or a numeric string")
    if vv <= 0: raise Fail(f"version {vv} is not greater than 0")
    d["status"] = _req_str(e, "status")
    u = e.get("used_in_v0")
    if u is not None and not isinstance(u, bool): raise Fail("used_in_v0 is not a boolean")
    d["used_in_v0"] = u
    src = e.get("source")
    sources = []
    if src is not None:
        if not isinstance(src, list): raise Fail("source is not an array or null")
        for i, s in enumerate(src):
            if not isinstance(s, dict): raise Fail(f"source[{i}] must be an object")
            _req_str(s, "citation"); _req_str(s, "accessed")
            for k in ("doi", "url", "where"): _opt_text(s, k)
            sources.append({"doi": _opt_str(s, "doi"), "url": _opt_str(s, "url")})
    d["sources"] = sources
    r = e.get("range")
    if r is not None and not isinstance(r, str):
        if isinstance(r, list): _interval(r)
        else: raise Fail("range is not a string or [min, max]")
    # uncertainty
    unc = e.get("uncertainty")
    if mode == "main": _opt_text(e, "uncertainty")
    elif mode == "real_7f2fb77":
        if unc is not None and not isinstance(unc, (str, dict)): raise Fail("uncertainty is not a string or an object")
    elif mode == "main_unc": pass   # diagnóstico: cargador de main con SOLO el cambio R5-03a
    elif unc is not None and not isinstance(unc, (str, dict)): raise Fail("uncertainty is not text or object")
    note = _opt_text(e, "note"); rat = _opt_text(e, "rationale")
    d["note"], d["rationale"] = note, rat
    d["derived_from"] = _opt_str(e, "derived_from")
    if mode in ("main", "main_unc"):
        d["conversion"] = _opt_text(e, "conversion")
    elif mode == "real_7f2fb77":
        d["conversion"] = _read_conversion_real(e.get("conversion"))
    else:
        d["conversion"] = e.get("conversion")
    if d["classification"] == "UNCLASSIFIED_PENDING" and (d["status"] != "PENDING" or v is not None):
        raise Fail("UNCLASSIFIED_PENDING is allowed only when status is PENDING and value is null")
    if d["status"] == "PENDING" and v is not None: raise Fail("status PENDING, so its value must be null")
    if d["classification"] in SCI and not any(s["doi"] or s["url"] for s in sources):
        raise Fail(f"{d['classification']} needs a source with a DOI or a URL")
    if mode == "real_7f2fb77":
        if d["classification"] not in SCI and not sources and not ((note or "").strip() or (rat or "").strip()):
            raise Fail("without a source and needs a rationale or a note")
        if isinstance(r, list) and r[0] > r[1]: raise Fail("range whose minimum is greater than its maximum")
        if isinstance(r, list):
            if _isnum(v) and not isinstance(v, bool):
                if not (r[0] <= v <= r[1]): raise Fail("is outside its range")
            elif isinstance(v, list):
                if v[0] > v[1]: raise Fail("interval value whose minimum is greater than its maximum")
                if not (v[0] >= r[0] and v[1] <= r[1]): raise Fail("is outside its range")
            else:
                raise Fail("has a numeric range and a value that is not a number or an interval")
    if mode == "r5_target" and d["classification"] not in SCI and not sources:
        if not ((note or "").strip() or (rat or "").strip()):
            raise Fail("non-scientific value without source needs a non-empty rationale or note (R5-06)")
    return d

def _read_conversion_real(c):
    if c is None: return None
    if not isinstance(c, dict): raise Fail("conversion is not an object")
    _req_str(c, "expression")
    f = c.get("factor")
    if not _isnum(f): raise Fail("conversion needs a numeric factor")
    if not math.isfinite(f) or f <= 0.0: raise Fail("conversion factor must be finite and greater than 0")
    if "factor_classification" not in c: raise Fail("conversion is missing factor_classification")
    fc = c["factor_classification"] if isinstance(c["factor_classification"], str) else ""
    if fc != "MODEL_ASSUMPTION": raise Fail("conversion factor_classification must be MODEL_ASSUMPTION")
    a = c.get("assumptions")
    if not isinstance(a, list): raise Fail("conversion needs an assumptions array")
    for x in a:
        if not isinstance(x, str) or not x.strip(): raise Fail("conversion assumptions must be non-empty strings")
    if not a: raise Fail("conversion assumptions must not be empty")
    return c

def _resolve_derivation(d, drafts, mode):
    df, cv = d["derived_from"], d["conversion"]
    if df is None:
        if cv is not None: raise Fail("sets conversion without derived_from")
        return
    if df == d["id"]: raise Fail("cannot be derived from itself")
    nat = next((x for x in drafts if x["id"] == df), None)
    if nat is None: raise Fail(f"derives from '{df}', which is not in the document")
    if nat["derived_from"] is not None: raise Fail(f"must derive from a native value, not from '{df}'")
    if mode == "real_7f2fb77":
        if d["classification"] == "SCIENTIFIC_MEASURED": raise Fail("is derived and cannot be SCIENTIFIC_MEASURED")
        if d["classification"] in SCI and nat["classification"] not in SCI: raise Fail("is SCIENTIFIC_* and derives from a native that is not SCIENTIFIC_*")
        if cv is None: raise Fail("has derived_from and needs a conversion object")
        return
    if mode in ("main", "main_unc"):
        if d["classification"] != "MODEL_ASSUMPTION":
            raise Fail(f"is derived from '{df}' and its classification must be MODEL_ASSUMPTION")
        if cv is not None and cv != "MODEL_ASSUMPTION":
            raise Fail(f"has conversion '{cv}'. A derived conversion is MODEL_ASSUMPTION")
        return
    # r5_target
    if d["classification"] == "SCIENTIFIC_MEASURED":
        raise Fail("a derived value cannot be SCIENTIFIC_MEASURED (R5-01)")
    if d["classification"] in SCI and nat["classification"] not in SCI:
        raise Fail(f"derived {d['classification']} from a non-scientific native '{df}' (R5-01)")
    if not isinstance(cv, dict): raise Fail("derived_from needs a conversion object (R5-02)")
    for k in ("expression", "factor_classification"):
        if not isinstance(cv.get(k), str) or not cv[k].strip(): raise Fail(f"conversion.{k} missing or empty (R5-02)")
    if cv["factor_classification"] != "MODEL_ASSUMPTION": raise Fail("conversion.factor_classification must be MODEL_ASSUMPTION (R5-02)")
    if not _isnum(cv.get("factor")) or not math.isfinite(cv["factor"]): raise Fail("conversion.factor must be a finite number (R5-02)")
    a = cv.get("assumptions")
    if not (isinstance(a, list) and a and all(isinstance(x, str) and x.strip() for x in a)): raise Fail("conversion.assumptions must be a non-empty list of strings (R5-02)")

def deliverable(defs, pid, kind):
    """RequireNumber/RequireBool: None si entrega, o el motivo del fallo."""
    d = next((x for x in defs if x["id"] == pid), None)
    if d is None: return "Missing"
    if d["status"] == "PENDING" or d["classification"] == "UNCLASSIFIED_PENDING": return "Pending"
    if d["status"] == "HISTORICAL": return "Historical"
    if d["status"] == "SUPERADO": return "Superseded"
    if d["used_in_v0"] is False: return "NotUsedInV0"
    v = d["value"]
    if kind == "number" and not (_isnum(v)): return "WrongKind"
    if kind == "bool" and not isinstance(v, bool): return "WrongKind"
    return None

if __name__ == "__main__":
    import sys
    path = sys.argv[1] if len(sys.argv) > 1 else "/workspace/martis-ciencia/mars_baseline_gale.v0.json"
    for mode in ("real_7f2fb77", "main", "main_unc", "r5_target"):
        errs, defs = load_text(open(path, encoding="utf-8").read(), mode)
        print(f"[{mode}] {len(defs)} leídas; {len(errs)} entradas fallan")
        for i, m in errs: print("   ", i, "->", m)
