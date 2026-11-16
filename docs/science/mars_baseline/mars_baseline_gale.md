# MarsBaseline – cráter Gale (MSL Curiosity) para T03

Fecha: 2026-11-16 (Europe/Rome). Solo investigación: no se ha tocado el repo. Borrador JSON: `mars_baseline_gale.draft.json` (32 entradas). **Versión vigente v0.4: `mars_baseline_gale.v0.json` (51 entradas), generada por `gale-src/build_v0.py`; ver la sección 0 (changelog v0.4, v0.3, v0.2 y v0.1). Copias: `mars_baseline_gale.v0.1.json`, `mars_baseline_gale.v0.2.json`, `mars_baseline_gale.v0.3.json`.** Fuentes de trabajo y script generador en `gale-src/`.

**Aviso previo:** ADR-011 no existe en el repo (ramas docs-initial, main, t01-hardening, t01-infrastructure, t02-domain-model; PRs #1–#4). Las unidades siguen lo que se indicó: SI, K, Pa, W/m², J/m²/sol, Gy/sol, Sv/sol; tick = 1 sol = 88 775 s. Cuando el dato original viene en mGy/día o mSv/día se convierte con ×88775/86400 = 1,0275 (**MODEL_ASSUMPTION**: se asume "día" = 24 h; no verificado en las fuentes) y se conserva el valor nativo.

**Sitio:** latitud/longitud según fuente: Ruíz 2025 4,49°S 137,42°E; Vicente-Retortillo 2016 4,59°S 137,44°E (tu referencia ≈4,6°S 137,4°E es coherente). Cota: −4501 m al aterrizar (Ruíz 2025); ascendió ~740 m hasta −3765 m (sol 3967). Los extremos de Ruíz Tabla A2/A3 de MY31–33 corresponden al suelo del cráter (ΔZ ≤ ~118 m); MY34+ ya incluye ascenso.

## 0. MarsBaseline Gale v0.4 FINAL (2026-11-16, autor MARTIS Ciencia)

### Changelog v0.4 (esta versión)

La v0.3 se conserva sin cambios en `mars_baseline_gale.v0.3.json` (sha256 `17bc66f63c2372b215c70173efdfc3220e9b0ae5ea073c20cce38f1a88949aa8`; `metadata.previous_version_file` apunta a ella). Motivo: R6-01 de la revisión del PR #6 (`review-pr6-7f2fb77.md`, head 7f2fb77). El cargador real rechaza un `value` de texto cuando `range` es un intervalo numérico, y la v0.3 cargaba 47/51 con él.

| # | Cambio | Entradas |
|---|---|---|
| 1 | `value` pasa de texto a `[min, max]` numérico, con los mismos números y la misma unidad. El `range` no cambia. | `air_temperature.diurnal_amplitude` `"45–75"` → `[45, 75]` K; `ground_temperature.diurnal_amplitude` `"55–100"` → `[55, 100]` K; `radiation.gcr_absorbed_dose_rate_cycle_range` `"0,100–0,390"` → `[0.1, 0.39]` mGy/día (nativo); `radiation.sep_dose_per_event` `"17–445"` → `[17, 445]` µGy/evento |
| 2 | `metadata.version` y la `version` de cada entrada pasan a `0.4`; `previous_version_file` = `mars_baseline_gale.v0.3.json`; se añade `metadata.changelog_v0_4`. | `metadata` y las 51 entradas |

Sin cambios: `v0_field_map`, clasificaciones, fuentes, unidades, estados, `conversion`, `uncertainty`. `validate_v0.py` comprueba que la v0.4 es la v0.3 salvo esos campos.

**Verificación.** `python3 gale-src/validate_v0.py`: la emulación `real_7f2fb77` (el cargador del PR #6, en `gale-src/loader_emulation.py`) carga 51/51 y todos los campos del mapa se entregan; si falla alguna entrada, el código de salida es distinto de 0. Es una emulación en Python, no el cargador C#.

### Changelog v0.3

La v0.2 se conserva sin cambios en `mars_baseline_gale.v0.2.json` (`metadata.previous_version_file` apunta a ella). Motivo: la revisión R5 del PR #5 (`review-pr5-75399ae.md`), aceptada por Motor, y la carga real que hará T03b con este fichero como sustituto exacto. Cambios:

| # | Cambio | Entradas |
|---|---|---|
| 1 | `derivation` (objeto libre) pasa a `conversion` = `{expression, factor, factor_classification: "MODEL_ASSUMPTION", assumptions[]}`. El factor es 88775/86400 (guardado como número, 1,0274884…, y como fracción exacta en `expression`). Supuestos: «día RAD = 24 h = 86 400 s» y «1 sol = 88 775 s (ADR-011, redondeo MODEL_ASSUMPTION)». El valor derivado sigue siendo `SCIENTIFIC_ESTIMATED` y apunta a su nativo con `derived_from`. | `gcr_absorbed_dose_rate_v0` (← `gcr_absorbed_dose_rate`, 0,210 mGy/día, `SCIENTIFIC_MEASURED`); `gcr_dose_equivalent_rate` (← `gcr_dose_equivalent_rate_native`) |
| 2 | **Entrada nueva** `gcr_dose_equivalent_rate_native`: 0,49 ± 0,08 mSv/día (Guo 2021, Tabla 2, Φ = 650 MV), `SCIENTIFIC_ESTIMATED` (es una estimación empírica del artículo, no una medición directa). Sin ella el equivalente por sol no tenía nativo al que apuntar. Los dos derivados llevan la σ nativa propagada (σ × factor; no incluye la incertidumbre del supuesto día RAD = 24 h). | 51 entradas (50 + 1) |
| 3 | Las 3 entradas con `source` sin `citation` ni `accessed` citan ahora la fuente de las entradas nativas que alimentan el cálculo (Ruíz 2025, doi 10.3390/rs17030368; Vicente-Retortillo 2016, doi 10.5209/rev_FITE.2016.v28.53900). El campo `where` dice que son ENTRADAS del cálculo, no la fuente del punto medio ni de la potencia media: siguen siendo `MODEL_ASSUMPTION`. | `air_temperature.midrange`, `pressure.midrange`, `solar.surface_irradiance_daily_mean_power` |
| 4 | Porosidad: `UNCLASSIFIED_PENDING`, status `PENDING`, `value` null, sin `source` (las candidatas no leídas pasan a `note`) y con `rationale`. | `regolith.porosity` |
| 5 | `used_in_v0: false` en las entradas `SUPERADO` (Hassler 2014, 0,64 mSv/día) y `HISTORICAL`. | `gcr_dose_equivalent_rate_hassler2014`, `air_temperature.midrange`, `pressure.midrange` |
| 6 | Sin fuentes de relleno («ver note», «Sin fuente verificada»): una entrada no científica sin fuente lleva `source: []` y `rationale` o `note` no vacíos. | `uv.daily_irradiation`, `solar.surface_noon_irradiance` y las `GAME_BALANCE` |
| 7 | `v0_field_map` sin cambios (nombres C#, cuatro `UNASSIGNED`). `v0_field_map_keys` anota que **WaterIce (kg) y Scope quedan fuera del mapa**: WaterIce es depósito `GAME_BALANCE` de Motor; Scope sale de `metadata.scope`. Se añade `metadata.scope_id = "regional:gale"` (legible por máquina; `scope` sigue siendo texto). | `metadata` |
| 8 | `uncertainty` sigue siendo un objeto (R5-03a). | 19 entradas |

**Verificación.** `python3 gale-src/validate_v0.py` (incluye la emulación Python de `ParameterCatalog.Load`, en `gale-src/loader_emulation.py`): 0 entradas fallan con las reglas R5 aceptadas por Motor. Con el cargador que hay hoy en `main` (1f3c7f3, idéntico al head 75399ae del PR #5, fusionado sin cambios R5) fallan 19 entradas por `uncertainty` objeto y, con solo R5-03a, aún fallan los dos derivados (`conversion` objeto y clasificación derivada). Es decir: **la v0.3 solo carga cuando Motor publique los cambios R5-01/02/03a del cargador**. La emulación no es el cargador C#; la prueba definitiva es el test de T03b.

### v0.2 (2026-11-16)

**v0.2:** la v0.1 se conserva en `mars_baseline_gale.v0.1.json`. Cambios: (1) `used_in_v0=true` solo en las 8 entradas de `metadata.v0_field_map` (Temperature, Pressure, IonizingAbsorbedDoseGyPerSol, IonizingEquivalentDoseSvPerSol, SolarSurfaceIrradianceWm2, PerchlorateWtPercent, WaterIceSurface, SepEnabled); `PerchlorateMassKg` figura sin asignar hasta H-07/T04 (los depósitos en kg serán GAME_BALANCE de Motor). UV de mediodía y porosidad no llevan `used_in_v0` (UV: «entra en T04/T05 con factor de escala MODEL_ASSUMPTION; no usar como media diaria»). (2) GCR separada en tres capas: valor nativo 0,210 mGy/día SCIENTIFIC_MEASURED (`gcr_absorbed_dose_rate`); conversión 2,158e-4 Gy/sol SCIENTIFIC_ESTIMATED derivada, ×88775/86400 con día RAD = 24 h (`gcr_absorbed_dose_rate_v0`, campo `derivation`); escenario constante sin ciclo solar ni SEP GAME_BALANCE (`gcr_scenario_v0`). (3) Política temporal: T y P constantes en v0 (MODEL_ASSUMPTION); ciclos D(t)/S(t) en T03c. (4) `validate_v0.py` comprueba mapa, `used_in_v0`, PENDING, UV/porosidad y la clasificación GCR. Los nombres de campo SolarSurfaceIrradianceWm2, PerchlorateWtPercent, WaterIceSurface y SepEnabled son míos (no se dieron): confirmar contra EnvironmentState.

*Lo que sigue en esta sección 0 se redactó en v0.1; donde hable de «v0.1» o de GCR como una sola entrada, prevalece el párrafo anterior.*

Archivo final: `mars_baseline_gale.v0.json` (50 entradas en v0.2, `metadata.status = FINAL`). Se genera con `python3 gale-src/build_v0.py` y se valida con `python3 gale-src/validate_v0.py` (carga, sin duplicados, campos obligatorios, ningún PROPOSED/PROVISIONAL, sin referencias a Glavin). Las secciones 1–4 son del borrador previo; si hay discrepancia, prevalece esta sección 0 y el JSON.

### 0.1 Decisiones de MARTIS Lead aplicadas
| Decisión | Entradas | Clase / estado |
|---|---|---|
| GCR absorbida constante 2,158e-4 Gy/sol | `gcr_absorbed_dose_rate` (medido) y `gcr_absorbed_dose_rate_v0` (escenario constante) | SCIENTIFIC_MEASURED / GAME_BALANCE |
| Ciclo solar 0,10–0,39 mGy/día | `gcr_absorbed_dose_rate_solar_cycle_range` | `FUTURE_VARIATION` |
| SEP desactivados | `sep_enabled = false`; dosis y Q de SEP solo referencia | GAME_BALANCE |
| Dosis equivalente Guo 5,03e-4 Sv/sol | `gcr_dose_equivalent_rate` | SCIENTIFIC_ESTIMATED |
| **T aire de referencia 222,0 K** | `env.gale.air_temperature.reference` | SCIENTIFIC_ESTIMATED, regional:gale, ACCEPTED |
| **P de referencia 841,8 Pa** | `env.gale.pressure.reference` | SCIENTIFIC_ESTIMATED, regional:gale, ACCEPTED |
| **T suelo 223,5 K (±2 K), campo aparte** | `env.gale.ground_temperature.annual_mean` | SCIENTIFIC_ESTIMATED, regional:gale, ACCEPTED |
| 231,3 K y 812,5 Pa | `air_temperature.midrange`, `pressure.midrange` | `HISTORICAL`, `used_in_v0 = false` |
| Irradiancia 113–180 W/m²/sol; fijo 146,5 W/m² | `surface_irradiance_daily_mean_power`; `surface_irradiance_fixed_v0` | MODEL_ASSUMPTION / GAME_BALANCE |
| UV y PAR de mediodía | estado `REFERENCE` | – |
| **Perclorato: solo rango Sutter 2017; v0 = 0,55 wt %** | `perchlorate_gale_range` (0,05–1,05, OK_ABSTRACT); `perchlorate_default_v0` (0,55) | SCIENTIFIC_ESTIMATED / MODEL_ASSUMPTION, ACCEPTED |

### 0.2 Medias anuales REMS (MY32), aceptadas
- **Dataset:** NASA PDS Atmospheres Node, MSL REMS MODRDR, ID `MSL-M-REMS-5-MODRDR-V1.0`, volumen `mslrem_1001`, productos RMD (1 Hz). URL: https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/
- **Cobertura:** MY32, soles impares 351–1017 (318 soles, 3,31 GB, streaming sin guardar crudo). Un solo año marciano y 1 de cada 2 soles.
- **Método:** media de los promedios diarios por sol; 24 cajas horarias LMST, huecos interpolados (circular), ≥20 cajas y ningún hueco >2 h; Ls de Allison & McEwen (2000). Filtros propios: aire 150–300 K con bits de confianza b2,b7,b8 = 1; suelo GTS confianza `1111` y 150–300 K; presión confianza `11` y 300–1400 Pa.

| Magnitud | Valor en JSON | Calculado | Desv. est. | SEM | nº soles | Ls (máx. hueco) | Sensibilidad |
|---|---|---|---|---|---|---|---|
| Aire (reference) | 222,0 K | 222.01 | 7.99 | 0.46 | 306 | 0.6–359.2° (7.6°) | 221,97–222,09 K |
| Suelo | 223,5 ±2 K | 223.48 | 8.01 | 0.59 | 184 | 0.6–352.0° (18.4°) | 222,4–224,3 K; cajas de Ls 225,5 K |
| Presión (reference) | 841,8 Pa | 841.8 | 56.95 | 3.24 | 309 | 0.6–359.2° (6.2°) | 841,0–842,0 Pa |

- **Salvedades de sensor:** ATS a ~0,6 m sobre la cubierta, con calentamiento por rover/RTG (Martínez 2017 §2.5), precisión <5 K. GTS: hasta 4 K de contribución del RTG (Hamilton 2014, citado por Martínez 2017), precisión ±5 o <10 K según fuente. PS: ±3,5 Pa (Ruíz 2025) o <2,7 Pa (Martínez 2016).
- **Suelo:** cobertura menor (solo 14 soles en Ls 300–360°), de ahí el ±2 K práctico.
- La desviación estándar refleja el ciclo estacional, no ruido. Estos valores sustituyen a 231,3 K y 812,5 Pa (diferencia −9,3 K y +29 Pa).
- Reproducir: `cd rems && python3 fetch_reduce.py 351 1018 --step 2 --workers 10 && python3 aggregate.py && cd .. && python3 gale-src/build_v0.py`.

### 0.3 Agua, perclorato, hielo y porosidad
| Campo | Valor | Clase | Fuente | Nivel de verificación |
|---|---|---|---|---|
| HR máx. diaria | ~10–70 % (máx. en Ls≈93°) | SCIENTIFIC_MEASURED | Martínez 2017 §3.6 | texto completo; cifras aproximadas |
| HR diurna (10–18 LMST) | <5 % | SCIENTIFIC_MEASURED | Martínez 2017 §3.6 | texto completo |
| Vapor, capa superficial | 30–75 ppm (primeros ~100 sols) | SCIENTIFIC_ESTIMATED | Harri 2014 | **solo abstract/Key Points**; los Key Points dicen 30–140 ppm (inconsistencia sin resolver) |
| Agua precipitable | unos pocos–6 pr-µm | SCIENTIFIC_ESTIMATED | Harri 2014 | **solo abstract** |
| Agua en suelo Rocknest (SAM) | 1,5–3 wt % H2O (ligada, no hielo) | SCIENTIFIC_MEASURED | Leshin 2013 | **solo abstract** |
| Agua equivalente subsuelo (DAN) | 1,5–1,7 (capa sup.) / 2,2–3,3 wt % (inf.) | SCIENTIFIC_ESTIMATED | Mitrofanov 2014 | **solo abstract** |
| Perclorato, rango Gale | 0,05–1,05 wt % ClO4 | SCIENTIFIC_ESTIMATED | Sutter 2017 | **solo abstract** |
| Perclorato, valor v0 | **0,55 wt %** (punto medio) | MODEL_ASSUMPTION | Sutter 2017 + rationale | **solo abstract** |
| Hielo de agua somero | 0 | MODEL_ASSUMPTION | Martínez 2017 §3.6, §4.3 | texto completo; no es una medida |
| Porosidad del regolito | PENDING | – | – | sin fuente |
| Contexto: inercia térmica | 295 (Rocknest), 306, 452 (Hamilton 2014); 170–550 (Vasavada 2017, vía Martínez 2017) | SCIENTIFIC_ESTIMATED | – | Hamilton: **solo abstract** |
| Contexto: densidad de roca madre | 1680 ± 180 kg/m³ | SCIENTIFIC_ESTIMATED | Lewis 2019 | **cifra vista citada en un abstract de Icarus 2021**; abstract de Lewis = resumen editorial |

### 0.4 Regla del cargador de T03 (también en `metadata.loader_rule_T03`)
`PENDING` solo se acepta en campos que ningún sistema v0 lea. Si un sistema v0 necesita un campo `PENDING` (value = null), el cargador falla con un error claro que nombra el id del campo; nunca inventa, rellena ni infiere un valor.

### 0.5 PENDING y pendientes de verificación
- **PENDING en el JSON:** `env.gale.solar.surface_noon_irradiance`, `env.gale.uv.daily_irradiation`, `env.analog.phoenix.perchlorate`, `env.gale.regolith.porosity`.
- **Pendientes de verificación (fuera del JSON):** (a) perclorato de Rocknest, 0,3–0,5 wt % atribuido a Glavin 2013 (cifra vista solo en una síntesis de búsqueda; el abstract no la trae); (b) valor derivado 0,4 wt % (punto medio de (a)), descartado; (c) Hecht 2009, cifra de perclorato de Phoenix; (d) Harri 2014, 75 vs 140 ppm en el texto completo; (e) Lewis 2019, 1680 ± 180 kg/m³ en el artículo; (f) Zeitlin 2019 (Q revisada); (g) ADR-011 (no existe en el repo); (h) todas las fuentes marcadas «solo abstract» frente al texto completo.

## 1. Tabla resumen (borrador, previa a v0)

Clasificación: M = SCIENTIFIC_MEASURED, E = SCIENTIFIC_ESTIMATED, A = MODEL_ASSUMPTION. Todos los accesos: 2026-11-16.

| Campo | Valor | Rango / incertidumbre | Unidad | Clase | Ámbito | Fuente (dónde) |
|---|---|---|---|---|---|---|
| Aire T mín diaria (extremo del ciclo) | 185,77 | MY31–33: 185,77–190,69; sensor <5 K | K | M | regional:gale | Ruíz 2025 doi:10.3390/rs17030368, Tabla A2 (MY32, Ls 78,4°, sol 535) |
| Aire T máx diaria (extremo del ciclo) | 276,90 | MY31–33: 269,53–276,90 | K | M | regional:gale | Ruíz 2025, Tabla A2 (MY33, Ls 204,7°, sol 1432) |
| Suelo T mín (brillo, GTS) | 171,74 | MY31–33: 171,74–185,63; sensor ±5 (Ruíz) o <10 K (Martínez) | K | M | regional:gale | Ruíz 2025 Tabla A3; Martínez 2016 doi:10.5209/rev_FITE.2016.v28.53902 §2.3 (≈173 K, sol 534) |
| Suelo T máx | 289,37 | MY31–33: 287,93–289,37 | K | M | regional:gale | Ruíz 2025 Tabla A3 (MY33); Martínez 2016 §2.3 (≈289 K) |
| Rango diurno aire | 45–75 | ~45 K (Ls~93°) a ~75 K (Ls~173°) | K | M | regional:gale | Martínez 2016 §2.3 p.172 |
| Rango diurno suelo | 55–100 | ~55 K (Ls~48°) a 100 K (Ls~173°) | K | M | regional:gale | Martínez 2016 §2.3 p.172 |
| Aire T punto medio de extremos (T_base provisional) | 231,3 | **no es media anual** | K | A | regional:gale | (185,77+276,90)/2 sobre Ruíz Tabla A2 |
| Aire T media anual | PENDIENTE | – | K | – | – | solo en Ruíz 2025 Fig. 6 |
| Presión media diaria mín | ~700 | sensor ±3,5 Pa; "~" del texto | Pa | M | regional:gale | Ruíz 2025 §3.1 (texto, Fig. 4) |
| Presión media diaria máx | ~925 | idem | Pa | M | regional:gale | Ruíz 2025 §3.1 |
| Oscilación diurna de presión | ~60 (ambigua: ±60 o total) | – | Pa | M | regional:gale | Ruíz 2025 §3.1 |
| Fase estacional presión | máx Ls≈250° (abs.), mín Ls≈150–157°, mín rel. ≈344–360°, máx rel. ≈35–60° | – | °Ls | M | regional:gale | Martínez 2016 §2.1; Ruíz 2025 §3.1 |
| Presión punto medio (provisional) | 812,5 | **no es media**; sesgado por ascenso | Pa | A | regional:gale | (700+925)/2 |
| Presión media anual | PENDIENTE | – | Pa | – | – | Ruíz Tabla A1 no recuperada |
| GCR dosis absorbida | 0,210 mGy/día = 2,158e-4 | ±0,04 mGy/día; ciclo 0,100–0,390 mGy/día (1,03e-4–4,01e-4 Gy/sol) | Gy/sol | M (medido 2012-13); rango ciclo E | regional:gale | Hassler 2014 doi:10.1126/science.1244797 Tablas 1–2; Guo 2021 doi:10.1007/s00159-021-00136-5 §4.3.4 |
| GCR dosis equivalente (preferido) | 0,49 mSv/día = 5,03e-4 | ±0,08; ciclo 0,25–0,95 mSv/día (2,57e-4–9,76e-4 Sv/sol); mín solar 0,86±0,11; máx solar 0,33±0,10 | Sv/sol | E | regional:gale | Guo 2021 Tabla 2 p.44 y §4.3.4 |
| GCR dosis equivalente original Hassler | 0,64 ± 0,12 mSv/día = 6,58e-4 | <Q>=3,05±0,26 (**superado**) | Sv/sol | M | regional:gale | Hassler 2014 Tabla 1 |
| SEP dosis absorbida por evento | 17–445 µGy | detector E 17–298; B 25–445 | µGy/evento | M | regional:gale | Guo 2021 §7.3.1–7.3.2 pp.51–53 |
| SEP factor de calidad | 1,17 (pico sept. 2017); ≈1 (Hassler) | GCR: 2,3 (Guo) / 3,05 (Hassler) | – | M | regional:gale | Guo 2021 §7.3.1; Hassler 2014 texto |
| Irradiancia solar TOA (distancia media) | 586,2 | Vicente-Retortillo usa 590 | W/m² | M | planetary_mean | NSSDC Mars Fact Sheet; Vicente-Retortillo 2016 doi:10.5209/rev_FITE.2016.v28.53900 eq. (1) |
| Insolación diaria superficie, mín (afelio) | ≳10 | cifra "slightly above 10" | MJ/m²/sol = 1,0e7 J/m²/sol | E | regional:gale | Vicente-Retortillo 2016 §4 p.118 |
| Insolación diaria superficie, máx (perihelio) | ~16 | máx ≈ 60 % > mín; T/E 0,7–0,9 | MJ/m²/sol = 1,6e7 J/m²/sol | E | regional:gale | Vicente-Retortillo 2016 §4 p.118, §4.3 p.123 |
| Irradiancia media por sol (derivada) | 112,6 a 180,2 | = I/88775 s | W/m² | A | regional:gale | 1,0e7/88775 y 1,6e7/88775 |
| PAR a mediodía | 240 | 195–300; 1σ 8,2 % | W/m² | E | regional:gale | Viúdez-Moreiras 2025 doi:10.1073/pnas.2426611122 Tabla 1 |
| UV total a mediodía | 33,8 | 26,9–42,3; 1σ 7,1 % | W/m² | M | regional:gale | Viúdez-Moreiras 2025 Tabla 1 y 2 |
| UVA / UVB / UVC a mediodía | 27,0 / 5,19 / 1,75 | 21,4–33,7 / 4,21–6,45 / 1,39–2,21; 1σ 7,1/7,8/11,2 % | W/m² | M | regional:gale | Viúdez-Moreiras 2025 Tabla 1 y 2 |
| UV irradiación diaria | PENDIENTE | – | J/m²/sol | – | – | solo en figuras |

Datos NSSDC de respaldo (solo `scope=planetary_mean`, `MODEL_ASSUMPTION`, nunca como dato regional según TDD §10.3): presión 636 Pa (6,36 mb al radio medio; 4,0–8,7 mb según estación), T media ~214 K, irradiancia solar 586,2 W/m². Gale está ~4,5 km por debajo del datum: 636 Pa no representa su presión.

## 2. Notas por bloque

### Temperatura
- Los extremos de Ruíz 2025 son el mínimo de los mínimos y el máximo de los máximos diarios de cada año marciano (ATS a ~1,5 m; GTS = temperatura de brillo del suelo). No son medias diarias ni anuales.
- Los extremos coinciden en estación con la literatura: mínimos en Ls ~80–105°, máximos en Ls ~175–220°. Efecto polvo: la gran tormenta de MY34 (Ls 185–210°) baja los máximos y sube ligeramente los mínimos.
- Los mínimos de aire suben ~5 K al subir por Mt. Sharp (hipótesis de bolsa de aire frío, Ruíz §4.1). Para un suelo de cráter se usan MY31–33; MY35 suelo 154,02 K (ΔZ 367 m) es atípico.
- Suelo–aire: de noche el suelo puede estar hasta ~15 K más frío que el aire a 1,6 m (Martínez 2016 §2.4). La ATS tiene contaminación térmica del RTG y del rover (Martínez 2016 §2.3).
- Precisión del sensor: Martínez 2016 Tabla 1 (Gómez-Elvira 2012): Ta <5 K, Tg <10 K, P <2,7 Pa; Ruíz 2025 Tabla 1: Tg ±5 K, P ±3,5 Pa. Las dos fuentes difieren; no he abierto Gómez-Elvira 2012.
- Las cifras de media diaria por estación y la amplitud anual de la media diaria no están dadas en un texto que haya verificado (ver pendientes). Una cifra de "amplitud ≈20 K" del review de Martínez 2017 SSR (doi:10.1007/s11214-017-0360-x) la leí en una sesión anterior y no la he vuelto a abrir, así que **no la incluyo**.

### Presión
- 700–925 Pa son extremos de la presión media diaria sobre 3967 sols (MY31 tardío–MY37), con ascenso de ~740 m incluido; el salto de las cumbres/mínimos anuales por ~350 m de ascenso es ~25 Pa (Ruíz §4.1). Para valores de suelo de cráter hay que usar Ruíz Tabla A1 (máx/mín por año), que el fetch no devolvió, o los datos PDS.
- Ciclo estacional por CO2 polar: Martínez 2016 §2.1 (máx Ls~250°, mín Ls~157°, mín relativo Ls~344°, máx relativo Ls~57°); Ruíz §3.1 (máx 240–270°, relativo 35–60°, mín ~150° y ~360°). Concuerdan.
- La marea térmica en Gale es mayor que en misiones anteriores por la baja cota (Martínez 2016 §2.1). Para amplitud diurna, Ruíz da "~60 Pa" sin aclarar si es semi-amplitud o rango; la figura 2 de Martínez 2016 (ejes 650–1000 Pa) no se usa como dato.

### Radiación (MSL/RAD)
- Hassler 2014 (primeros ~300 sols, máximo del ciclo 24, columna ~21 g/cm²): dosis tisular 0,21 ± 0,04 mGy/día; <Q>=3,05 ± 0,26; 0,64 ± 0,12 mSv/día. Tabla 3: 76 mGy/año y 232 mSv/año en superficie (0,21×365 = 76,7; 0,64×365 = 233,6).
- Guo et al. 2021 (revisión completa, texto leído): dosis ~200 µGy/día (finales 2012–principios 2015) subiendo hasta ~320 µGy/día en el mínimo 2019–2020; modelo empírico: 100–390 µGy/día entre máximo y mínimo solar (modulación Φ medida 370–710 MV; extrapolar a Φ alto es incierto). <Q>=2,4 ± 0,3 (Zeitlin et al. 2019, citado por Guo). Con Q≈2,4: 0,21×2,4≈0,50 mSv/día, coherente con 0,49 ± 0,08 (Φ=650 MV). **El 0,64 mSv/día de Hassler 2014 ha sido revisado a la baja**; usar Guo 2021 como defecto y mantener el original solo como trazabilidad.
- Escenarios Guo Tabla 2 (superficie, similar presión a Gale): mín solar (350 MV) 0,86 ± 0,11; medio (650 MV) 0,49 ± 0,08; máx solar (1000 MV) 0,33 ± 0,10 mSv/día. Para un juego conviene elegir un escenario de actividad solar (parámetro de diseño, GAME_BALANCE).
- SEP: solo 5 eventos prominentes en ~8 años, ciclo 24 débil. Dosis absorbida por evento (con GCR restado), detector B (piel, convertido a agua) / E (tejido): 2013-04-11: 42/26 µGy; 2013-10-11: 77/61; 2014-01-07: 25/17; 2014-09-01: 214/168; 2017-09-10: 445/298 µGy (Guo 2021 pp.51–53). La cifra de Hassler (50 µGy en el texto; 0,025 mGy en Tabla 2) coincide con el evento 1 de Guo (42 µGy B, 26 µGy E): el texto usa el detector menos blindado y la Tabla 2 el más blindado. Una versión anterior de mis notas la trataba como inconsistencia: no lo es.
- Dosis equivalente SEP: Hassler usa Q≈1; Guo da <Q>=1,17 en el pico de sept. 2017. Dosis equivalente de un evento ≈ Q×dosis (**MODEL_ASSUMPTION**, no publicado por evento).
- Contexto 2017: la tasa de dosis se duplicó durante horas; dosis integrada de 30 d, 7,3–8,2 mGy y ~16,5 mSv. Los SEP **no** se pueden expresar como Gy/sol sin un modelo de frecuencia: son por evento.

### Irradiancia solar y UV
- REMS no mide la irradiancia solar total en superficie; el UVS está en la cubierta, con campo de visión limitado, y Martínez 2016 §2.2 no usa REMS para la irradiación diaria (usa modelo radiativo + opacidad de Mastcam). Por eso la insolación diaria es SCIENTIFIC_ESTIMATED y no medida.
- Vicente-Retortillo 2016 (modelo radiativo + Mastcam, MY31–32): insolación diaria total (directa+difusa) en superficie ≳10 MJ/m² en el afelio y ~16 MJ/m² en el perihelio; máximo anual ≈ 60 % superior al mínimo; T/E (superficie/TOA) entre 0,7 (perihelio) y 0,9 (estación clara). TOA diario (eq. 1) con E0 = 590 W/m² y día de 88 775 s.
- **Conversión derivada (A, no medida):** potencia media por sol = I/88 775 s → 10 MJ/m² = 112,6 W/m²; 16 MJ/m² = 180,2 W/m². Es media de 24,66 h con noche incluida. Para flujo instantáneo de mediodía haría falta un modelo (directa ≈ E0·(a/r)²·cosθ·exp(−τ/cosθ) + difusa); lo marco PENDIENTE y no lo calculo porque la difusa domina en la estación polvorienta (Vicente-Retortillo §4.3, B/D) y un cálculo solo-directa sería engañoso. La irradiancia NSSDC de 586,2 W/m² es TOA a distancia media y no debe usarse como valor de superficie.
- UV/PAR a mediodía (Viúdez-Moreiras 2025, REMS UVS, >5 años marcianos, hasta el sol ~4000, sin la GDS MY34): UV 26,9–42,3 W/m² (orbital 33,8; afelio 28,9; estación polvorienta 36,8; GDS 4,64); UVA 27,0, UVB 5,19, UVC 1,75; BED 51,3 (42,2–61,0); PAR 240 (195–300). Son flujos de mediodía en horizontal, no medias diarias. Atenuación atmosférica media del UV: 22,8 %. Incertidumbre 1σ de Tabla 2: UV 7,1 %, UVA 7,1 %, UVB 7,8 %, UVC 11,2 %, BED 11,5 %, PAR 8,2 %.
- Opacidad: tau (880 nm) ~1,25 (año 1) y ~1,6 (año 2) en Ls~230° (Martínez 2016 §2.2); tau UV de mediodía típicamente 0,3–1,4, hasta ~7 (UV) y ~8,5 (Mastcam IR) en la GDS MY34 (Viúdez-Moreiras 2025). No son el mismo canal: no mezclar.

## 3. Pendientes (borrador; ver 0.5 para el estado v0)

1. **Media anual y ciclo estacional de T (aire y suelo):** solo en Ruíz 2025 Fig. 6/7 y Martínez 2016 Fig. 4. Calcular desde REMS PDS (https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/) sobre MY31–33. Mientras tanto, 231,3 K es un punto medio provisional (A); T_base real de la media diaria queda por fijar.
2. **Presión media anual y rango limpio de cráter (MY31–33):** Ruíz Tabla A1 (no recuperada: el fetch devolvió solo el pie), o PDS. Los 700–925 Pa incluyen el ascenso. 812,5 Pa es solo un punto medio provisional (A).
3. **Amplitud diurna de presión:** "~60 Pa" es ambigua; verificar con Ruíz Fig. 10 o PDS.
4. **Irradiancia de superficie instantánea en banda total (W/m²):** no hay cifra verificada. Revisar Vicente-Retortillo et al. 2015 (doi:10.1051/swsc/2015035), Martínez et al. 2021 (doi:10.1029/2020JE006804; solo se vio resumen, que no da medias) o integrar UV+PAR+NIR. Hasta entonces usar la potencia media por sol derivada.
5. **UV irradiación diaria (J/m²/sol):** solo en figuras (Martínez 2016 Fig. 3; Viúdez-Moreiras 2025 Fig. 2C, 4). No tomo ninguna cifra de los ejes.
6. **Opacidad a 880 nm de referencia (clara y estación polvorienta):** solo 1,25 y 1,6 verificadas en texto.
7. **Q y época solar de referencia para T03:** elegir escenario (mín/medio/máx solar) y documentarlo como GAME_BALANCE; verificar Zeitlin et al. 2019 (Q revisada) en su fuente primaria, que no he abierto.
8. **Gómez-Elvira 2012/2014, Haberle 2014, Harri 2014:** solo se alcanzó el resumen (Haberle) o no se abrió el texto (timeout de doi.org); no se usó ninguna cifra de ellos. Hassler 2014 se leyó en el manuscrito de autor, no en la versión impresa.
9. **Fuera del alcance y sin datos:** vapor de agua/HR (Martínez 2016 §2.4 da HR máx ~70 % en Ls~93°, Ruíz Fig. 8–9 y texto: VMR máx hasta 60 ppm en MY≤33 y 120–140 ppm en MY34–35, valores de máximo diario nocturno), perclorato, hielo de agua y porosidad: sin fuente verificada para valores de línea base; no incluidos.
10. **ADR-011:** no existe en el repo; confirmar que el tick de 88 775 s y la conversión día→sol (×1,0275) son las que se quieren.

## 4. Registro de fuentes (acceso 2026-11-16, Europe/Rome)

| Fuente | DOI / URL | Qué se leyó |
|---|---|---|
| Ruíz et al. 2025, Remote Sens. 17(3):368 | 10.3390/rs17030368 · https://www.mdpi.com/2072-4292/17/3/368 | texto completo, Tablas 1, 2, A2, A3 (A1 vacía) |
| Martínez et al. 2016, Física de la Tierra 28:163-179 | 10.5209/rev_FITE.2016.v28.53902 · revistas.ucm.es/index.php/FITE/article/download/53902/49342 | texto completo |
| Vicente-Retortillo et al. 2016, FdlT 28:111-127 | 10.5209/rev_FITE.2016.v28.53900 · …/download/53900/49340 | texto completo |
| Hassler et al. 2014, Science 343 | 10.1126/science.1244797 | manuscrito de autor completo (Tablas 1–3, texto SEP) |
| Guo et al. 2021, A&A Rev. 29:8 | 10.1007/s00159-021-00136-5 (PDF vía copia abierta en inspirehep.net) | texto completo (secciones 2, 4.3, 7.3, Tabla 2) |
| Viúdez-Moreiras et al. 2025, PNAS 122(21) | 10.1073/pnas.2426611122 · https://www.pnas.org/doi/10.1073/pnas.2426611122 | texto completo, Tablas 1–2; SI Appendix no leído |
| NASA NSSDCA Mars Fact Sheet | https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html | página completa |
| Vasavada et al. 2017 Icarus 284 (inercia térmica, para contexto) | https://ntrs.nasa.gov/citations/20170003773 | solo resumen: TI aparente de roca madre ~350–550 J m⁻² K⁻¹ s⁻¹ᐟ² (no incluido en JSON) |
| REMS MODRDR (MSL-M-REMS-5-MODRDR-V1.0) | https://atmos.nmsu.edu/PDS/data/mslrem_1001/DATA/ | datos RMD de 318 soles de MY32 (3,31 GB), AAREADME, SIS, ERRATA, MODRDR6.FMT |
| Martínez et al. 2017, Space Sci. Rev. | 10.1007/s11214-017-0360-x | texto completo (acceso abierto), §2.5, 3.3, 3.4, 3.6, 4.3 |
| Leshin et al. 2013, Science 341:1238937 | 10.1126/science.1238937 | abstract y página |
| Sutter et al. 2017, JGR Planets | 10.1002/2016JE005225 | abstract (Crossref) |
| Glavin et al. 2013, JGR Planets | 10.1002/jgre.20144 | abstract (Crossref); cifra 0,3–0,5 wt % no verificada |
| Mitrofanov et al. 2014, JGR Planets | 10.1002/2013JE004553 | abstract (OpenAlex) |
| Harri et al. 2014, JGR Planets | 10.1002/2013JE004514 | abstract y Key Points (OpenAlex); inconsistencia 75 vs 140 ppm |
| Hamilton et al. 2014, JGR Planets | 10.1002/2014JE004618 | abstract (OpenAlex) |
| Lewis et al. 2019, Science 363:535 | 10.1126/science.aat0738 | solo resumen editorial; cifra vía abstract de Icarus 2021 (10.1016/j.icarus.2021.114539) |
| Hecht et al. 2009, Science 325:64 | 10.1126/science.1172466 | solo resumen editorial |
