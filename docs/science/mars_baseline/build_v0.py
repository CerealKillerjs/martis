#!/usr/bin/env python3
"""Construye mars_baseline_gale.v0.json (v0.3) a partir del borrador (mars_baseline_gale.draft.json), el summary de REMS
(rems/summary.json) y las decisiones de MARTIS Lead (2026-11-16). Reproducible: python3 gale-src/build_v0.py"""
import json, copy, os
ROOT = "/workspace/martis-ciencia"
draft = json.load(open(f"{ROOT}/mars_baseline_gale.draft.json"))
rems = json.load(open(f"{ROOT}/rems/summary.json"))
ACC = "2026-11-16"
K = 88775/86400
byid = {p["id"]: p for p in draft["parameters"]}
out = []

def add(p):
    p.setdefault("version", "0.1")
    out.append(p)

def S(citation, where=None, doi=None, url=None, seen=None):
    d = {"citation": citation}
    if where: d["where"] = where
    if doi: d["doi"] = doi
    if url: d["url"] = url
    d["accessed"] = ACC
    if seen: d["seen"] = seen
    return d

def conversion(expression):
    """Objeto `conversion` v0.3 (R5-02): el cargador no calcula el número; solo exige que exista cuando hay derived_from."""
    return {"expression": expression, "factor": K, "factor_classification": "MODEL_ASSUMPTION",
            "assumptions": ["día RAD = 24 h = 86 400 s", "1 sol = 88 775 s (ADR-011, redondeo MODEL_ASSUMPTION)"]}

def E(id, field, value, unit, cls, scope, source, note, status="OK", **kw):
    d = {"id": id, "field": field, "value": value, "range": kw.pop("rng", None), "uncertainty": kw.pop("unc", None),
         "unit": unit, "classification": cls, "scope": scope, "version": "0.1", "status": status,
         "source": source, "note": note}
    d.update(kw)
    return d

# ---- 1) Parametros heredados del borrador, con cambios puntuales ------------------------------------------------
for p in draft["parameters"]:
    p = copy.deepcopy(p)
    pid = p["id"]; p["version"] = "0.3"
    if p.get("status") == "PENDIENTE": p["status"] = "PENDING"
    # ---- A(1) GCR
    if pid == "env.gale.radiation.gcr_absorbed_dose_rate":
        # v0.2: esta entrada guarda SOLO el valor nativo medido (sin conversión a sol)
        p["value"] = 0.210; p["unit"] = "mGy/day"; p["range"] = None
        p["uncertainty"] = {"sigma_1_mGy_day": 0.04}
        p.pop("value_native", None)
        p["field"] = "gcr_absorbed_dose_rate_native"
        p["note"] = ("VALOR NATIVO MEDIDO (SCIENTIFIC_MEASURED), sin conversión: 0,210 ± 0,04 mGy/día en los primeros ~300 sols (agosto 2012–2013; máximo solar del ciclo 24), "
                     "detector E (tejido-equivalente), Hassler 2014 Tablas 1–2. Guo 2021: ~200 µGy/día a finales 2012–principios 2015, subiendo a ~320 µGy/día en el mínimo 2019–2020. "
                     "La conversión a sol está en env.gale.radiation.gcr_absorbed_dose_rate_v0 (SCIENTIFIC_ESTIMATED derivado) y el escenario constante en env.gale.radiation.gcr_scenario_v0 (GAME_BALANCE).")
    if pid == "env.gale.radiation.gcr_absorbed_dose_rate_solar_cycle_range":
        p["value"] = [round(0.100e-3*K, 9), round(0.390e-3*K, 9)]
        p["unit"] = "Gy/sol"
        p["range"] = p["value"]
        p["status"] = "FUTURE_VARIATION"
        p["field"] = "gcr_absorbed_dose_rate_solar_cycle_range"
        p["note"] = ("VARIACIÓN FUTURA, no usada en v0 (decisión MARTIS Lead). Valor nativo 0,100–0,390 mGy/día (Guo 2021 §4.3.4), "
                     "convertido ×1,0275 a Gy/sol (MODEL_ASSUMPTION de conversión). " + p["note"])
    if pid == "env.gale.radiation.gcr_dose_equivalent_rate":
        p["note"] += (" [v0] Decisión MARTIS Lead: usar Guo 2021 (0,49 mSv/día → 5,03e-4 Sv/sol) como dosis equivalente; "
                      "constante en v0 (escenario constante = GAME_BALANCE, ver gcr_absorbed_dose_rate_v0).")
        p["value"] = 5.03e-4
        p["derived_from"] = "env.gale.radiation.gcr_dose_equivalent_rate_native"
        p["conversion"] = conversion("0,49 mSv/día × 88775/86400 = 5,0347e-4 Sv/sol, redondeado a 5,03e-4 (mSv → Sv: × 1e-3)")
        p["uncertainty"] = {"native_1sigma_mSv_day": 0.08, "propagated_1sigma_Sv_per_sol": round(0.08e-3*K, 9),
                            "propagation": "σ nativa × factor; no incluye la incertidumbre del supuesto día RAD = 24 h"}
    if pid in ("env.gale.radiation.sep_dose_per_event", "env.gale.radiation.sep_quality_factor"):
        p["status"] = "REFERENCE"
        p["note"] += " [v0] SEP desactivados en v0 (ver env.gale.radiation.sep_enabled); dato solo de referencia."
    # ---- R5-03b (v0.3): fuentes con citation/accessed: la referencia de las entradas nativas que alimentan el cálculo
    if pid in ("env.gale.air_temperature.midrange", "env.gale.pressure.midrange", "env.gale.solar.surface_irradiance_daily_mean_power"):
        nat, where = {
            "env.gale.air_temperature.midrange": ("env.gale.air_temperature.daily_min_extreme",
                "ENTRADAS del cálculo, no fuente del punto medio: mínimo 185,77 K (Tabla A2, MY32) y máximo 276,90 K (Tabla A2, MY33); punto medio (185,77+276,90)/2 = 231,335 K (MODEL_ASSUMPTION)"),
            "env.gale.pressure.midrange": ("env.gale.pressure.daily_mean_min",
                "ENTRADAS del cálculo, no fuente del punto medio: mínimo ~700 Pa y máximo ~925 Pa de la media diaria (§3.1, Fig. 4); punto medio (700+925)/2 = 812,5 Pa (MODEL_ASSUMPTION)"),
            "env.gale.solar.surface_irradiance_daily_mean_power": ("env.gale.solar.surface_daily_insolation_min",
                "ENTRADAS del cálculo, no fuente de la potencia media: insolación diaria ~10 MJ/m² (aphelio) y ~16 MJ/m² (perihelio) en Curiosity (§4 p.118, Fig. 3); potencia media = insolación / 88775 s (MODEL_ASSUMPTION)"),
        }[pid]
        ref = copy.deepcopy(byid[nat]["source"][0])
        assert ref.get("citation") and ref.get("accessed") and (ref.get("doi") or ref.get("url")), nat
        ref["where"] = where
        p["source"] = [ref]
    # ---- A(2) T y P de referencia
    if pid == "env.gale.air_temperature.midrange":
        p["field"] = "air_temperature_midrange_historical"
        p["status"] = "HISTORICAL"; p["used_in_v0"] = False
        p["note"] = ("HISTÓRICO, SIN USO en v0.1 FINAL. Antiguo valor provisional 231,3 K = (185,77+276,90)/2, punto medio de extremos (MODEL_ASSUMPTION); no era media anual. "
                     "Sustituido por env.gale.air_temperature.reference (222,0 K, REMS MY32), decisión MARTIS Lead 2026-11-16.")
    if pid == "env.gale.pressure.midrange":
        p["field"] = "surface_pressure_midrange_historical"
        p["status"] = "HISTORICAL"; p["used_in_v0"] = False
        p["note"] = ("HISTÓRICO, SIN USO en v0.1 FINAL. Antiguo valor provisional 812,5 Pa = (700+925)/2, punto medio de extremos de la media diaria (MODEL_ASSUMPTION); no era media. "
                     "Sustituido por env.gale.pressure.reference (841,8 Pa, REMS MY32), decisión MARTIS Lead 2026-11-16.")
    # ---- A(3) irradiancia
    if pid == "env.gale.solar.surface_irradiance_daily_mean_power":
        p["value"] = [113, 180]; p["range"] = [113, 180]; p["status"] = "OK"
        p["note"] = ("Potencia media por sol = insolación diaria / 88775 s sobre Vicente-Retortillo 2016 (10 MJ/m² → 112,6; 16 MJ/m² → 180,2; redondeado a 113–180). "
                     "MODEL_ASSUMPTION (derivado, no medido). Incluye la noche; NO es flujo de mediodía. Insolaciones de origen aproximadas ('ligeramente superior a 10', 'alrededor de 16').")
    if pid == "env.gale.uv.irradiance_noon_avg":
        p["note"] += " Entra en T04/T05 con factor de escala MODEL_ASSUMPTION; no usar como media diaria."
    if pid in ("env.gale.solar.par_midday", "env.gale.uv.irradiance_noon_avg"):
        p["status"] = "REFERENCE"
        p["note"] += " [v0] Queda como referencia (decisión MARTIS Lead), no como entrada del modelo."
    if pid == "env.gale.radiation.gcr_cumulative_30d_example":
        p["value"] = 8.1e-3; p["unit"] = "Gy per 30 d"; p["classification"] = "SCIENTIFIC_MEASURED"
        p["note"] += " Valor = 8,1 mGy (detector E) en 30 d de fondo GCR en 2017 (16,5 mSv equivalente)."
    out.append(p)

# nuevas entradas A(1), A(3), SEP
add(E("env.gale.radiation.gcr_absorbed_dose_rate_v0", "gcr_absorbed_dose_rate_v0", 2.158e-4, "Gy/sol", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [S("Hassler D.M. et al. (2014) Science 343:1244797, Tabla 1 (0,210 ± 0,04 mGy/día), convertido a sol", doi="10.1126/science.1244797", seen="manuscrito de autor completo")],
      "Valor numérico de la dosis absorbida GCR por sol: conversión DERIVADA del valor nativo medido (env.gale.radiation.gcr_absorbed_dose_rate). "
      "La CLASIFICACIÓN del valor es SCIENTIFIC_ESTIMATED (derivado); el escenario constante es GAME_BALANCE y está en env.gale.radiation.gcr_scenario_v0.",
      derived_from="env.gale.radiation.gcr_absorbed_dose_rate",
      conversion=conversion("0,210 mGy/día × 88775/86400 = 2,1577e-4 Gy/sol, redondeado a 2,158e-4 (mGy → Gy: × 1e-3)"),
      unc={"native_1sigma_mGy_day": 0.04, "propagated_1sigma_Gy_per_sol": round(0.04e-3*K, 9),
           "propagation": "σ nativa × factor; no incluye la incertidumbre del supuesto día RAD = 24 h"},
      scenario_ref="env.gale.radiation.gcr_scenario_v0", status="OK"))
add(E("env.gale.radiation.gcr_dose_equivalent_rate_native", "gcr_dose_equivalent_rate_native", 0.49, "mSv/day", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [copy.deepcopy(byid["env.gale.radiation.gcr_dose_equivalent_rate"]["source"][0])],
      "VALOR NATIVO de Guo 2021 Tabla 2 (estimación empírica del equivalente de dosis GCR en superficie, Φ=650 MV 'medium', con blindaje y presión tipo MSL/Gale): 0,49 ± 0,08 mSv/día. "
      "Es una ESTIMACIÓN empírica del artículo (no una medición directa de RAD), por eso SCIENTIFIC_ESTIMATED. Sin conversión a sol: la conversión está en env.gale.radiation.gcr_dose_equivalent_rate (derivado, derived_from).",
      unc={"native_1sigma_mSv_day": 0.08}, status="OK"))
add(E("env.gale.radiation.gcr_scenario_v0", "gcr_scenario", "constant_in_time", "n/a", "GAME_BALANCE", "regional:gale",
      [], "Escenario de v0: dosis GCR constante en el tiempo, sin ciclo solar y sin SEP. Aplica a la dosis absorbida y a la equivalente.",
      rationale="Decisión MARTIS Lead 2026-11-16: en v0 la dosis GCR es constante (valor medido en los primeros ~300 sols) para no modelar la modulación solar (rango 1,03e-4–4,01e-4 Gy/sol, Guo 2021) ni los eventos SEP (sep_enabled = false). Es una simplificación de juego, no un resultado científico.",
      status="OK"))
add(E("env.gale.radiation.sep_enabled", "sep_events_enabled", False, "bool", "GAME_BALANCE", "regional:gale",
      [], "SEP desactivados en v0.",
      rationale="Decisión MARTIS Lead 2026-11-16: los eventos SEP quedan desactivados en v0. Solo hay 5 eventos medidos en ~8 años en un ciclo solar débil (Guo 2021) y no son generalizables; dosis por evento disponibles en env.gale.radiation.sep_dose_per_event como referencia.",
      status="OK"))
add(E("env.gale.solar.surface_irradiance_fixed_v0", "surface_irradiance_sol_mean_fixed", 146.5, "W/m2", "GAME_BALANCE", "regional:gale",
      [], "Valor fijo de v0: potencia media por sol = punto medio de 113–180 W/m² (equivale a ~13,0 MJ/m²/sol).",
      rationale="Decisión MARTIS Lead 2026-11-16: valor fijo de 146,5 W/m² = (113+180)/2 para v0, sin estacionalidad ni opacidad de polvo. El rango MODEL_ASSUMPTION (env.gale.solar.surface_irradiance_daily_mean_power) queda como variación futura.",
      status="OK"))

# ---- 2) B: medias anuales REMS ------------------------------------------------------------------------------------
DS = ("NASA PDS Atmospheres Node, MSL REMS MODRDR (dataset ID MSL-M-REMS-5-MODRDR-V1.0, volumen mslrem_1001, productos RMD), "
      "https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/")
RS = S("MSL REMS MODRDR, " + DS, where="MY32, soles impares 351–1017 (318 soles muestreados); script rems/fetch_reduce.py + rems/aggregate.py; resultado rems/summary.json",
       url="https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/", seen="datos descargados y reducidos en caja (3,31 GB)")
def stat(key, desc):
    r = rems[key]
    return dict(n_sols=r["n_sols"], sol_range=[r["sol_first"], r["sol_last"]], ls_range_deg=[r["ls_min"], r["ls_max"]],
                max_ls_gap_deg=r["max_ls_gap_deg"], std_of_daily_means=r["std_of_daily_means"], sem=r["sem"],
                min_daily_mean=r["min_daily_mean"], max_daily_mean=r["max_daily_mean"], mean_of_ls30_bins=r["mean_of_ls30_bins"],
                filter=desc)
META_METHOD = ("Media de los promedios diarios por sol. Promedio diario: 24 cajas horarias LMST de MODRDR a 1 Hz, cajas vacías interpoladas linealmente (circular); "
               "se exige ≥20 cajas con dato y ningún hueco >2 h. Ls de Allison & McEwen 2000. Solo soles impares de MY32 (sol 351–1017). ")
SALV_AIRE = ("Salvedades: ATS a ~0,6 m sobre la cubierta del rover, con calentamiento por rover/RTG (Martínez 2017 §2.5); precisión <5 K. Un solo año marciano (MY32) y 1 de cada 2 soles; filtros de calidad propios (rems/process_rems.py).")
add(E("env.gale.air_temperature.reference", "air_temperature_reference", 222.0, "K", "SCIENTIFIC_ESTIMATED", "regional:gale", [RS],
      "T de aire de referencia v0 (decisión MARTIS Lead 2026-11-16). Media de los promedios diarios por sol, REMS MY32 (calculada 222,01 K, redondeada a 222,0). "
      + META_METHOD + "Filtro: AMBIENT_TEMP 150–300 K con bits de confianza b2,b7,b8 = 1. Sensibilidad 221,97–222,09 K. " + SALV_AIRE + " Sustituye al provisional 231,3 K.",
      status="ACCEPTED", value_computed=rems["ta"]["mean_of_daily_means"],
      uncertainty=stat("ta", "ATS conf b2,b7,b8; 150–300 K") | {"sensor_accuracy_K": 5},
      range=[rems["ta"]["min_daily_mean"], rems["ta"]["max_daily_mean"]]))
add(E("env.gale.ground_temperature.annual_mean", "ground_temperature_annual_mean", 223.5, "K", "SCIENTIFIC_ESTIMATED", "regional:gale", [RS],
      "T de suelo (temperatura de brillo GTS), campo aparte de la T de aire. Media de promedios diarios por sol, REMS MY32 (calculada 223,48 K, redondeada a 223,5 ±2 K). "
      + META_METHOD + "Filtro: GTS BRIGHTNESS_TEMP confianza '1111' y 150–300 K. COBERTURA MENOR: 184 soles y huecos de Ls hasta 18,4° (solo 14 soles en Ls 300–360°); sesgo hacia la mitad fría del año: media por cajas de Ls 225,48 K; sensibilidad 222,4–224,3 K, de ahí ±2 K. "
      "Salvedades: precisión GTS ±5 (Ruíz 2025) o <10 K (Martínez 2016); hasta 4 K de contribución del RTG (Hamilton 2014, citado por Martínez 2017); un solo año y 1 de cada 2 soles.",
      status="ACCEPTED", value_computed=rems["tg"]["mean_of_daily_means"],
      uncertainty=stat("tg", "GTS 1111; 150–300 K") | {"practical_pm_K": 2, "sensor_accuracy_K": "5–10 (según fuente)", "rtg_contribution_K_max": 4},
      range=[221.5, 225.5]))
add(E("env.gale.pressure.reference", "surface_pressure_reference", 841.8, "Pa", "SCIENTIFIC_ESTIMATED", "regional:gale", [RS],
      "P de referencia v0 (decisión MARTIS Lead 2026-11-16). Media de promedios diarios por sol, REMS MY32 (841,80 Pa). "
      + META_METHOD + "Filtro: PS confianza '11', 300–1400 Pa. Sensibilidad 841,0–842,0 Pa; media por cajas de Ls 845,6 Pa. "
      "Salvedades: precisión PS ±3,5 Pa (Ruíz 2025) / <2,7 Pa (Martínez 2016); incluye el efecto de la cota del rover en MY32; un solo año y 1 de cada 2 soles. Sustituye al provisional 812,5 Pa.",
      status="ACCEPTED", value_computed=rems["p"]["mean_of_daily_means"],
      uncertainty=stat("p", "PS conf 11; 300–1400 Pa") | {"sensor_accuracy_Pa": 3.5},
      range=[rems["p"]["min_daily_mean"], rems["p"]["max_daily_mean"]]))
# Placeholders PENDING del borrador para medias anuales: eliminados (cubiertos por las entradas *.reference y ground annual_mean)
out[:] = [p for p in out if p["id"] not in ("env.gale.air_temperature.annual_mean", "env.gale.pressure.annual_mean")]

# ---- 3) C: vapor de agua, perclorato, hielo, porosidad -----------------------------------------------------------------
M17 = lambda w: S("Martínez G.M. et al. (2017) Space Sci. Rev., 'The Modern Near-Surface Martian Climate: A Review of In-situ Meteorological Data from Viking to Curiosity'", where=w,
                  doi="10.1007/s11214-017-0360-x", url="https://link.springer.com/article/10.1007/s11214-017-0360-x", seen="texto completo (acceso abierto) leído 2026-11-16")
HARRI = S("Harri A.-M. et al. (2014) J. Geophys. Res. Planets, 'Mars Science Laboratory relative humidity observations: Initial results'", doi="10.1002/2013JE004514",
          where="abstract y Key Points", seen="solo abstract/Key Points (vía OpenAlex); texto completo no accesible")
add(E("env.gale.humidity.rh_daily_max_range", "relative_humidity_daily_max_annual_range", [10, 70], "%", "SCIENTIFIC_MEASURED", "regional:gale",
      [M17("§3.6 (texto): máximo diario ~70 % en Ls≈93°, mínimo ~10 % en primavera tardía/inicio de verano; Fig. 14")],
      "Máximo diario de HR (REMS-H, recalibración agosto 2016, solo segundos 1–4 tras encender el sensor), primeros 1526 sols (MY31–33). Se da casi siempre entre 04:00 y 06:00 LMST. "
      "Cifras de texto aproximadas ('about 70 %', '~10 %'). Controlada sobre todo por la temperatura, no por el vapor. El PDS REMS de hoy tiene flags de HR preliminares ('0X1'): no se reprocesó aquí.",
      rng=[10, 70], status="OK"))
add(E("env.gale.humidity.rh_daytime_upper_bound", "relative_humidity_daytime_max", 5, "%", "SCIENTIFIC_MEASURED", "regional:gale",
      [M17("§3.6: 'daytime values <5 % between 10:00 and 18:00 LMST throughout the year'")],
      "Cota superior de HR diurna (10:00–18:00 LMST). Los VMR diurnos derivados tienen grandes incertidumbres.", status="OK"))
add(E("env.gale.water_vapor.vmr_surface_layer", "water_vapor_mixing_ratio_surface_layer", [30, 75], "ppm", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [HARRI, M17("§3.6: VMR nocturno solo en Fig. 14; sin cifra en texto")],
      "Rango del abstract de Harri 2014: 30–75 ppm en la capa superficial durante los primeros ~100 sols (MY31 tardío–32 temprano). INCONSISTENCIA INTERNA: los Key Points de la misma fuente dicen 30–140 ppm y HR máxima 75 %, el abstract dice 75 ppm; no se ha podido leer el texto completo para resolverlo. "
      "Martínez 2017 muestra la serie estacional (más bajo en otoño-invierno, más alto en invierno tardío–verano) pero sin cifras. Ruíz 2025 (leído): VMR máx. nocturno hasta ~60 ppm en MY≤33. Cobertura temporal parcial: no es media anual.",
      rng=[30, 140], status="OK_PARCIAL"))
add(E("env.gale.water_vapor.precipitable_water", "precipitable_water_column", "unos pocos a 6", "pr-µm", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [HARRI], "Columna de agua precipitable con mezcla uniforme (supuesto), primeros ~100 sols. Solo abstract. No se ha recuperado dato de media anual.", status="OK_PARCIAL"))
add(E("env.gale.regolith.soil_water_content_sam_rocknest", "regolith_water_content_rocknest_sam", [1.5, 3], "wt%", "SCIENTIFIC_MEASURED", "regional:gale",
      [S("Leshin L.A. et al. (2013) Science 341:1238937", where="abstract: 'Water abundance (1.5 to 3 weight percent) ... bound within an amorphous component'", doi="10.1126/science.1238937",
         url="https://www.science.org/doi/10.1126/science.1238937", seen="abstract y página leídos 2026-11-16")],
      "H2O liberada por el suelo eólico de Rocknest hasta ~835 °C (SAM). Es agua ligada a la componente amorfa, NO hielo. Otras muestras de Gale (Sutter 2017): 0,9 ± 0,3 – 2,5 ± 1,6 wt % H2O (abstract Crossref).",
      rng=[1.5, 3], status="OK"))
add(E("env.gale.regolith.dan_water_equivalent", "regolith_water_equivalent_dan_gale", [1.5, 3.3], "wt% H2O", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [S("Mitrofanov I.G. et al. (2014) J. Geophys. Res. Planets, 'Water and chlorine content in the Martian soil along the first 1900 m of the Curiosity rover traverse as estimated by the DAN instrument'",
         doi="10.1002/2013JE004553", where="abstract: capa superior 1,5–1,7 wt %, inferior 2,2–3,3 wt %, puntos ~5 wt %; 154 puntos, primeros 361 sols", seen="abstract leído (OpenAlex)")],
      "Contenido de agua del subsuelo somero (modelo de dos capas) a lo largo de 1900 m de traza. Modelo de dos capas; los datos son el contenido en H2O equivalente y no distinguen hielo de agua ligada a minerales. Sin hielo en superficie. Rango [1,5; 3,3] = extremos de las medias de capas (no incluye puntos ~5 %). "
      "Un tramo (odómetro 455–638 m) favorece el modelo inverso (2,1 ± 0,1 arriba / 1,4 ± 0,04 abajo).",
      rng=[1.4, 5], status="OK"))
add(E("env.gale.regolith.perchlorate_gale_range", "regolith_perchlorate_gale_range", [0.05, 1.05], "wt% ClO4", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [S("Sutter B. et al. (2017) J. Geophys. Res. Planets (JGR Planets, e-location 2574)", doi="10.1002/2016JE005225", where="abstract: 'oxychlorine phases (chlorate/perchlorate) (0.05 ± 0.025–1.05 ± 0.44 wt % ClO4)'",
         seen="abstract leído (Crossref)")],
      "Rango de especies oxicloradas expresadas como ClO4 en 2 sedimentos eólicos y 9 rocas sedimentarias de Gale (SAM-EGA, desde Yellowknife Bay hasta Namib Dune). ESTIMADO porque se infiere del O2 evolucionado (supone oxicloro). Máximo 1,05 wt % en Cumberland (roca). "
      "Mezcla rocas y eólicos: no es un dato de regolito suelto.", rng=[0.05, 1.05], status="OK_ABSTRACT"))
add(E("env.gale.regolith.perchlorate_default_v0", "regolith_perchlorate_default", 0.55, "wt% ClO4", "MODEL_ASSUMPTION", "regional:gale",
      [S("Sutter B. et al. (2017) J. Geophys. Res. Planets, doi 10.1002/2016JE005225 (rango 0,05–1,05 wt % ClO4)", doi="10.1002/2016JE005225",
         where="abstract: 'oxychlorine phases (chlorate/perchlorate) (0.05 ± 0.025–1.05 ± 0.44 wt % ClO4)'", seen="solo abstract (Crossref)")],
      "Valor de simulación v0: punto medio del rango de Gale (0,05+1,05)/2 = 0,55 wt % ClO4. No es una medida de regolito suelto.",
      rationale="Decisión MARTIS Lead 2026-11-16: el único rango verificado para Gale es el de Sutter 2017 (rocas sedimentarias y eólicos, SAM-EGA); se toma su punto medio como valor único de v0 porque el modelo necesita un escalar y no hay dato regional de regolito suelto verificado. Sustituir si se verifica un valor regional de regolito.",
      status="ACCEPTED"))
add(E("env.analog.phoenix.perchlorate", "regolith_perchlorate_phoenix_analog", None, "wt% ClO4", "SCIENTIFIC_ESTIMATED", "analog:phoenix_arctic",
      [S("Hecht M.H. et al. (2009) Science 325:64–67 (editorial 'Phoenix Ascending')", doi="10.1126/science.1172466", where="resumen editorial: la mayor parte del cloro soluble en superficie es perclorato",
         seen="solo resumen editorial; sin cifra verificada")],
      "ANÁLOGO (68,2°N, no Gale): perclorato detectado como principal especie del cloro soluble. Cifra cuantitativa NO verificada en esta sesión, por tanto PENDING. No usar como dato regional (TDD §10.3).",
      status="PENDING"))
add(E("env.gale.water_ice.surface_present", "water_ice_surface_present", 0, "wt%", "MODEL_ASSUMPTION", "regional:gale",
      [M17("§3.6 y §4.3: 'Ground frost has not been observed in situ at ... MSL'; 'at equatorial and low latitudes ... water ice is not thermodynamically stable in the shallow subsurface (Mellon & Jakosky 1993; Mellon et al. 2004; Schorghofer & Aharonson 2005)'; 'as of February 2017 frost has not been detected by the MSL Curiosity rover'")],
      "Valor v0 = 0 (sin hielo de agua en superficie ni subsuelo somero estable). Se basa en no-detección + estabilidad termodinámica citada por una revisión; NO es una medida de contenido de hielo. DAN mide H2O total (1,5–3,3 wt %), que no se interpreta como hielo. "
      "Escarcha nocturna: evidencia INDIRECTA en pocos soles de otoño-invierno tardío de MY32 (Martínez et al. 2016, Icarus, doi 10.1016/j.icarus.2015.12.004; fuente no accesible aquí, referenciada solo a través de Martínez 2017); no confirmada visualmente.",
      status="OK"))
add(E("env.gale.regolith.porosity", "regolith_porosity", None, "fraction", "UNCLASSIFIED_PENDING", "regional:gale", [],
      "PENDING: no se encontró una cifra de porosidad del regolito/suelo de Gale en las fuentes leídas. NO se usa la media planetaria. Candidatas no leídas: Hamilton 2014 (inercia térmica), DAN, MAHLI/Rocknest. Contexto: ver inercia térmica y densidad de lecho rocoso.",
      rationale="Sin fuente primaria verificada para la porosidad del regolito suelto de Gale: no hay valor que clasificar (R5-03c). Si un sistema v0 la lee, el cargador debe fallar nombrando el id.",
      status="PENDING"))
add(E("env.gale.regolith.thermal_inertia_soil", "thermal_inertia_soil_sites", [295, 452], "J m-2 K-1 s-1/2", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [S("Hamilton V.E. et al. (2014) J. Geophys. Res. Planets, 'Surface energy budget and thermal inertia at Gale Crater: Calculations from ground-based measurements'", doi="10.1002/2014JE004618",
         where="abstract: Rocknest 295, Point Lake 306, Yellowknife Bay 452", seen="abstract leído (OpenAlex)"),
       M17("§3.4: Vasavada et al. (2017) 170–550 en los primeros 1250 sols (citado); 300→420 en sol ~120")],
      "Inercia térmica (SI). Rocknest (suelo arenoso eólico) 295; Point Lake 306; Yellowknife Bay (lecho rocoso) 452. Rango de traza Vasavada 2017 (citado por Martínez 2017): 170–550. Es un contexto físico para la porosidad, no una porosidad.",
      rng=[170, 550], status="CONTEXTO"))
add(E("env.gale.bedrock.bulk_density", "bedrock_bulk_density_gale_floor", 1680, "kg/m3", "SCIENTIFIC_ESTIMATED", "regional:gale",
      [S("Lewis K.W. et al. (2019) Science 363, p. 535, 'A surface gravity traverse on Mars indicates low bedrock density at Gale crater'", doi="10.1126/science.aat0738",
         where="valor 1680 ± 180 kg/m³ visto citado en el abstract de un artículo de Icarus 2021 (doi 10.1016/j.icarus.2021.114539), que además propone 2300 ± 130 con porosidad de impacto", seen="abstract de Lewis 2019 = resumen editorial sin cifra; el número viene de la cita en Icarus 2021")],
      "Densidad de masa de los estratos/lecho rocoso (NO regolito, no es porosidad). Solo como contexto; la revisión de Icarus 2021 sugiere hasta 2300 ± 130 kg/m³.", unc={"sigma_kg_m3": 180}, status="CONTEXTO"))

# ---- Normalizar y escribir -------------------------------------------------------------------------------------------
# v0.4 (R6-01): el cargador real de 7f2fb77 rechaza texto con `range` numérico; estas 4 entradas pasan a value = [min, max] (mismos números, misma unidad)
V04_INTERVALS = {
    "env.gale.air_temperature.diurnal_amplitude": [45, 75],
    "env.gale.ground_temperature.diurnal_amplitude": [55, 100],
    "env.gale.radiation.gcr_absorbed_dose_rate_cycle_range": [0.1, 0.39],
    "env.gale.radiation.sep_dose_per_event": [17, 445],
}
_seen04 = set()
for p in out:
    p["version"] = "0.4"
    if p["id"] in V04_INTERVALS:
        assert isinstance(p["value"], str) and p.get("range") == V04_INTERVALS[p["id"]], p["id"]
        p["value"] = list(V04_INTERVALS[p["id"]]); _seen04.add(p["id"])
    if p.get("status") == "PENDIENTE": p["status"] = "PENDING"
    if p["status"] == "PENDING":
        p["value"] = None
        if not p.get("classification"): p["classification"] = "UNCLASSIFIED_PENDING"
        if not p.get("unit"): p["unit"] = "n/a"
    if not p.get("unit"): p["unit"] = "n/a"
    if p.get("classification") is None: p["classification"] = "UNCLASSIFIED_PENDING"
    if p["status"] in ("SUPERADO", "HISTORICAL"): p["used_in_v0"] = False
    # alias consistente
    if "uncertainty" in p and p["uncertainty"] is None: p.pop("uncertainty") if False else None


# ---- v0.2: used_in_v0 y v0_field_map -------------------------------------------------------------------------------
V0_FIELD_MAP = {
  "TemperatureKelvin": "env.gale.air_temperature.reference",
  "PressurePascal": "env.gale.pressure.reference",
  "IonizingAbsorbedDoseGyPerSol": "env.gale.radiation.gcr_absorbed_dose_rate_v0",
  "IonizingEquivalentDoseSvPerSol": "env.gale.radiation.gcr_dose_equivalent_rate",
  "SolarIrradiance": "env.gale.solar.surface_irradiance_fixed_v0",
  "PerchlorateWtPercent": "env.gale.regolith.perchlorate_default_v0",
  "WaterIceWtPercent": "env.gale.water_ice.surface_present",
  "SepEnabled": "env.gale.radiation.sep_enabled",
  "PerchlorateMassKg": None,
  "UvRadiation": None,
  "SoilPorosity": None,
  "WaterVapor": None,
}
UNASSIGNED_NOTES = {
  "PerchlorateMassKg": "Sin asignar hasta H-07/T04. Los depósitos iniciales en kg serán GAME_BALANCE de Motor, no ciencia.",
  "UvRadiation": "Decisión de Lead: la UV no entra en v0. Entra en T04/T05 con 33,8 W/m² de mediodía (env.gale.uv.irradiance_noon_avg) y factor de escala MODEL_ASSUMPTION explícito; no usar como media diaria.",
  "SoilPorosity": "env.gale.regolith.porosity está PENDING (sin fuente verificada). Sin asignar en v0; el cargador falla si un sistema la lee.",
  "WaterVapor": "Depósito en kg por celda: GAME_BALANCE de Motor o sin asignar (depende de H-07). Las referencias de vapor (ppm, HR) del JSON no son un valor en kg.",
}
FIELD_NOTES = {
  "SepEnabled": "No es campo de EnvironmentState: configuración de radiación del escenario (bool).",
  "PerchlorateWtPercent": "Campo nuevo de EnvironmentState que crea T03b (wt % ClO4, en [0,100]).",
  "WaterIceWtPercent": "Campo nuevo de EnvironmentState que crea T03b (wt %). EnvironmentState.WaterIce (kg por celda) es otro campo: depósito GAME_BALANCE de Motor.",
  "SolarIrradiance": "Media por sol en superficie (W/m²), misma semántica que surface_irradiance_fixed_v0.",
}
_ids = {p["id"]: p for p in out}
for fld, pid in V0_FIELD_MAP.items():
    if pid is not None:
        assert pid in _ids, f"id inexistente en el v0_field_map: {pid}"
        assert _ids[pid]["status"] != "PENDING"
        _ids[pid]["used_in_v0"] = True
for p in out:  # ninguna otra entrada puede quedar marcada
    if p["id"] not in V0_FIELD_MAP.values() and p.get("used_in_v0") is True:
        del p["used_in_v0"]
for k in ("env.gale.uv.irradiance_noon_avg", "env.gale.regolith.porosity"):
    assert _ids[k].get("used_in_v0") is not True

assert _seen04 == set(V04_INTERVALS), _seen04
pend = [p["id"] for p in out if p["status"] == "PENDING"]
abstract_only = [p["id"] for p in out if any("abstract" in str(x.get("seen", "")).lower() or "solo resumen" in str(x.get("seen", "")).lower() for x in p.get("source", []) if isinstance(x, dict))]
meta = {
 "schema": "martis.environment_baseline", "name": "MarsBaseline-Gale", "version": "0.4", "status": "FINAL", "date": ACC, "author": "MARTIS Ciencia",
 "scope": "regional:gale (cráter Gale, MSL Curiosity, ~4,6°S 137,4°E). Entradas con scope planetary_mean o analog:* son solo fallback/análogo y no deben usarse como dato regional (TDD §10.3).",
 "tick": {"unit": "sol", "seconds": 88775},
 "decisions_applied": [
   "GCR: valor nativo 0,210 mGy/día SCIENTIFIC_MEASURED; conversión 2,158e-4 Gy/sol SCIENTIFIC_ESTIMATED derivada (×88775/86400, día RAD = 24 h); escenario constante sin ciclo solar ni SEP = GAME_BALANCE; rango de ciclo solar 0,10–0,39 mGy/día como variación futura; SEP desactivados; dosis equivalente Guo 5,03e-4 Sv/sol.",
   "T de aire de referencia 222,0 K y P de referencia 841,8 Pa (SCIENTIFIC_ESTIMATED, REMS MY32); T de suelo 223,5 ±2 K como campo aparte. Los provisionales 231,3 K y 812,5 Pa quedan como HISTORICAL sin uso.",
   "Irradiancia: potencia media 113–180 W/m²/sol MODEL_ASSUMPTION; valor fijo 146,5 W/m² GAME_BALANCE; UV y PAR de mediodía como referencia.",
   "Perclorato: solo el rango de Sutter 2017 (0,05–1,05 wt % ClO4); valor de simulación v0 0,55 wt % (punto medio, MODEL_ASSUMPTION)."],
 "rems": {"dataset_id": "MSL-M-REMS-5-MODRDR-V1.0", "volume": "mslrem_1001", "url": "https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/",
          "coverage": "MY32, soles impares 351–1017", "air_K": rems["ta"]["mean_of_daily_means"], "ground_K": rems["tg"]["mean_of_daily_means"], "pressure_Pa": rems["p"]["mean_of_daily_means"],
          "reproduce": "cd rems && python3 fetch_reduce.py 351 1018 --step 2 --workers 10 && python3 aggregate.py && cd .. && python3 gale-src/build_v0.py"},
 "previous_version_file": "mars_baseline_gale.v0.3.json",
 "v0_temporal_policy": "T y P constantes en v0 (MODEL_ASSUMPTION); ciclos D(t)/S(t) en T03c.",
 "v0_field_map": {k: ({"id": v, "unit": _ids[v]["unit"], **({"note": FIELD_NOTES[k]} if k in FIELD_NOTES else {})} if v else {"id": None, "status": "UNASSIGNED", "note": UNASSIGNED_NOTES[k]}) for k, v in V0_FIELD_MAP.items()},
 "v0_field_map_keys": ("Claves = nombre exacto de la propiedad C# de EnvironmentState en main (confirmado por MARTIS Motor 2026-11-16), salvo SepEnabled (configuración de escenario) y los dos campos nuevos PerchlorateWtPercent y WaterIceWtPercent (los crea T03b). "
                       "El mapa cubre todos los campos de EnvironmentState EXCEPTO WaterIce (kg por celda: depósito GAME_BALANCE de Motor, no es dato de Ciencia) y Scope (sale de metadata.scope / metadata.scope_id, no de un parámetro)."),
 "scope_id": "regional:gale",
 "changelog_v0_4": [
   "R6-01 (revisión del PR #6, head 7f2fb77): el cargador real rechaza un `value` de texto cuando `range` es un intervalo numérico. Cuatro entradas pasan de texto a `value: [min, max]` numérico, con los mismos números y la misma unidad: air_temperature.diurnal_amplitude [45, 75] K, ground_temperature.diurnal_amplitude [55, 100] K, gcr_absorbed_dose_rate_cycle_range [0.1, 0.39] mGy/día (nativo), sep_dose_per_event [17, 445] µGy/evento. Nada más cambia (clasificaciones, fuentes, v0_field_map); `version` de metadata y de las entradas pasa a 0.4."],
 "changelog_v0_3": [
   "R5-02: `derivation` (objeto libre) pasa a `conversion` = {expression, factor, factor_classification = MODEL_ASSUMPTION, assumptions[]}; el valor derivado sigue siendo SCIENTIFIC_ESTIMATED y apunta a su nativo con `derived_from`. Entradas: gcr_absorbed_dose_rate_v0 y gcr_dose_equivalent_rate.",
   "Nueva entrada nativa env.gale.radiation.gcr_dose_equivalent_rate_native (Guo 2021 Tabla 2, 0,49 ± 0,08 mSv/día, SCIENTIFIC_ESTIMATED) para que el equivalente de dosis por sol tenga a qué apuntar con derived_from. Las dos entradas derivadas llevan la incertidumbre nativa propagada (σ × factor).",
   "R5-03b: las 3 entradas con `source` sin citation/accessed (air_temperature.midrange, pressure.midrange, solar.surface_irradiance_daily_mean_power) citan ahora la fuente de las entradas nativas que alimentan el cálculo (Ruíz 2025; Vicente-Retortillo 2016), con `where` que dice que son entradas del cálculo y no la fuente del valor.",
   "R5-03c: env.gale.regolith.porosity pasa a UNCLASSIFIED_PENDING (PENDING, value null, sin source; rationale).",
   "R5-04: used_in_v0 = false en las entradas SUPERADO (Hassler 2014 0,64 mSv/día) y HISTORICAL.",
   "R5-06: ninguna entrada no científica queda con fuente de relleno (`ver note`); sin fuente => `source: []` con `rationale` o `note` no vacíos.",
   "v0_field_map sin cambios (nombres C#, cuatro UNASSIGNED); se anota que WaterIce (kg) y Scope quedan fuera del mapa. Se añade metadata.scope_id = regional:gale (legible por máquina; metadata.scope sigue siendo texto)."],
 "v0_not_used": {"env.gale.uv.irradiance_noon_avg": "entra en T04/T05 con factor de escala MODEL_ASSUMPTION; no usar como media diaria",
                 "env.gale.regolith.porosity": "PENDING: el cargador falla si un sistema v0 la lee"},
 "loader_rule_T03": "PENDING solo se acepta en campos que ningún sistema v0 lea. Si un sistema v0 necesita un campo con status PENDING (value = null), el cargador debe fallar con un error claro que nombre el id del campo; nunca debe inventar, rellenar ni inferir un valor por defecto.",
 "pending": pend,
 "pending_notes": {
   "env.analog.phoenix.perchlorate": "Cifra de Hecht 2009 no verificada (solo resumen editorial); análogo, no dato regional",
   "env.gale.regolith.porosity": "Sin fuente verificada de regolito",
   "env.gale.solar.surface_noon_irradiance": "Flujo de mediodía en banda total sin fuente",
   "env.gale.uv.daily_irradiation": "Solo en figura",
 },
 "verification_level_flags": {
   "abstract_or_summary_only": abstract_only,
   "notes": ["env.gale.water_vapor.vmr_surface_layer: abstract (30–75 ppm) vs Key Points (30–140 ppm) de la misma fuente, sin resolver", "env.gale.bedrock.bulk_density: cifra vista citada en otro abstract, no en el artículo de Lewis 2019"]},
 "adr011_found": False,
}
final = {"metadata": meta, "location": draft["location"], "parameters": out}
json.dump(final, open(f"{ROOT}/mars_baseline_gale.v0.json", "w"), ensure_ascii=False, indent=2)
print(len(out), "entradas; pendientes:", pend)
