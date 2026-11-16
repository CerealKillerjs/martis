# Diccionario de datos

Tipos del modelo de dominio. Los identificadores `RegionId`, `CellId`, `PopulationId`, `OrganismId`, `GeneId` y `ReactionId` vienen de T01.

Las cifras que son una medición o una estimación llevan su fuente y su clasificación. El 88 775 s del sol es el redondeo de modelo de una medición citada abajo. Lo que no tiene fuente no es una medición. Las unidades que fija [ADR-011](ADR/ADR-011-unidades-del-modelo.md) están aceptadas. T03a añade el cargador de parámetros, la configuración y el motor de ticks.

VAL-001..005 significan solo lo que dice el TDD §10. Las reglas añadidas en este hito son DOM-VAL-001..016. La columna «se parece a» dice qué VAL del TDD recuerda, sin sustituirlo. La elección de unidad es `MODEL_ASSUMPTION`. La clasificación, la fuente y el ámbito van por valor en el JSON del escenario (TDD §10.2).

VAL-006, VAL-007 y VAL-008 se comprueban en el paso `validate-conservation` del pipeline con biología (`pipeline-v3` desde T06; T04, [ADR-012](ADR/ADR-012-modelo-metabolico.md)).

## Tiempo

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| MartianTime.SecondsPerSol | int | s por sol | 88 775, una sola copia en `src/` | 1 tick = 1 sol. El entero es un redondeo de modelo (`MODEL_ASSUMPTION`) de una medición |

1 tick = 1 sol. `SecondsPerSol` = 88 775 s es un redondeo de modelo (`MODEL_ASSUMPTION`) del día solar medio de Marte. La medición (`SCIENTIFIC_MEASURED`) es D. R. Williams, NASA NSSDCA, Mars Fact Sheet, tabla «Orbital parameters», fila «Length of day (hrs)» 24,6597 h ≈ 88 774,92 s (https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html; consultado el 2026-11-16; la página no tiene fecha de actualización). Contraste: Allison & McEwen (2000), Planet. Space Sci. 48, 215–235, doi:10.1016/S0032-0633(99)00092-6, 88 775,244 s (NASA GISS Mars24, https://www.giss.nasa.gov/tools/mars24/help/notes.html). El redondeo 88 775 s difiere ≤ 0,25 s de cada fuente (0,08 s de NSSDCA; 0,244 s de Allison & McEwen); entre las dos fuentes hay 0,32 s. No es el periodo sideral (24,6229 h ≈ 88 642,4 s).

No hay `DeltaTimeDays` en el código. La edad de la población va en soles.

## EnvironmentState

| Campo | Tipo | Unidad | Base temporal | Rango válido | Id | Se parece a |
| --- | --- | --- | --- | --- | --- | --- |
| TemperatureKelvin | AssignedDouble | K. Se usa la temperatura del aire | Media sobre los 88 775 s del sol. REMS mide aire y suelo, y son distintos. La media esconde el ciclo diurno (`MODEL_ASSUMPTION`) | unset, o finito y > 0 | DOM-VAL-001 si está asignado y no cumple; DOM-VAL-009 si un escenario cargado lo deja unset | VAL-001, con cota estricta y sin ser un recurso |
| PressurePascal | AssignedDouble | Pa | Media sobre los 88 775 s del sol | unset, o finito y ≥ 0. El 0 asignado es un dato | DOM-VAL-002; DOM-VAL-009 si el escenario lo deja unset | VAL-001, sin ser un recurso |
| UvRadiation | AssignedDouble | W/m², banda 200–400 nm, mediodía local, horizontal | No es media del sol. La fluencia diaria en J/m² por sol está pendiente y no se acumula tick a tick | unset, o finito y ≥ 0. En MarsBaseline v0 queda unset | DOM-VAL-004 si está asignado | VAL-001 |
| IonizingAbsorbedDoseGyPerSol | double | Gy por sol (Gy = J/kg), tejido o agua, no silicio. GCR en superficie. SEP fuera de v0 | Acumulada en el sol. No se suma al sievert | finito y ≥ 0 | DOM-VAL-013 | VAL-001, sin ser un recurso |
| IonizingEquivalentDoseSvPerSol | double | Sv por sol (Sv = Gy × Q, Q adimensional). Usar Sv en microorganismos es `MODEL_ASSUMPTION` | Acumulada en el sol. No se compara con el gray | finito y ≥ 0 | DOM-VAL-013 | VAL-001, sin ser un recurso |
| SolarIrradiance | double | W/m² en superficie, no en el tope de la atmósfera | Media sobre el sol. La energía del tick es G · SecondsPerSol, en J/m². Alternativa no usada: J/m² por sol | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| WaterIce | double | kg por celda, condicionado a H-07 | Stock físico. Depósito GAME_BALANCE. No se deriva del wt % | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| WaterVapor | AssignedDouble | kg por celda, condicionado a H-07 | Stock físico. La biología no lo consume | unset, o finito y ≥ 0. En MarsBaseline v0 queda unset. Unset no entra en la suma como medición | DOM-VAL-004 si está asignado | VAL-001 |
| SoilPorosity | AssignedDouble | fracción de volumen | El intervalo [0, 1] es la definición de la fracción, no un supuesto | unset, o finito y en [0, 1]. En MarsBaseline v0 queda unset | DOM-VAL-003 si está asignado | VAL-005, sin ser un gen |
| PerchlorateMassKg | AssignedDouble | kg por celda | Sin asignar hasta H-07. El wt % no se convierte a kg | unset, o finito y ≥ 0 | DOM-VAL-004 si está asignado | VAL-001 |
| PerchlorateWtPercent | double | wt % ClO4 | Fracción másica. No es PerchlorateMassKg | finito y en [0, 100] | DOM-VAL-016 | ninguno de §10 |
| WaterIceWtPercent | double | wt % | Fracción másica. No es WaterIce en kg | finito y en [0, 100] | DOM-VAL-016 | ninguno de §10 |
| Scope | EnvironmentScope o null | — | Ámbito principal, por celda. MarsBaseline lo toma de metadata.scope_id | en un escenario cargado, uno de PlanetaryMean, Regional(SiteId), Lab, Game | DOM-VAL-010 si falta | ninguno de §10 |

Referencias de dosis, no copiadas al código. La clasificación `SCIENTIFIC_ESTIMATED` es la del valor por sol derivado; el valor nativo de Hassler es `SCIENTIFIC_MEASURED`. Hassler et al. 2014, Science 343:1244797, doi:10.1126/science.1244797, 0,210 ± 0,040 mGy/día ≈ 2,158·10⁻⁴ Gy/sol; Guo et al. 2021, doi:10.1007/s00159-021-00136-5, 0,49 ± 0,08 mSv/día ≈ 5,03·10⁻⁴ Sv/sol. El paso de día a sol multiplica por 88 775 / 86 400 y es `MODEL_ASSUMPTION` porque supone un día RAD de 24 h.

`AssignedDouble` por defecto es unset. Unset no es el número 0: `Set(0)` es otro valor. `CheckInvariants` no exige que un `AssignedDouble` esté asignado. `SimulationValidator` con `ValidationContext.LoadedScenario` sí exige temperatura, presión y alcance. UV, vapor, porosidad y `PerchlorateMassKg` pueden quedar unset en un escenario cargado: MarsBaseline v0 los deja así. Un mundo sin escenario puede pasar el validador con esos campos sin asignar. Ese mismo estado no es un escenario válido.

`SiteId` es un identificador de sitio, distinto de organismo, gen y reacción. `Regional` exige texto.

## ResourcePool

Masa en kg por celda. Cada depósito finito y ≥ 0 es **VAL-001** («ningún recurso negativo»). El 0 es una masa asignada, no un hueco.

| Campo | Tipo | Unidad | Rango | Notas |
| --- | --- | --- | --- | --- |
| CarbonDioxide, Oxygen, Nitrogen, Phosphorus, Sulfur, Iron, OrganicCarbon, MineralCarbon | double | kg por celda | finito y ≥ 0 | VAL-001 |
| Water | double | kg por celda | finito y ≥ 0 | Solo el líquido accesible para la biología. No es hielo ni vapor. VAL-001 |

La matriz A guarda recuentos de átomos por especie, adimensionales. La masa molar no se almacena: `M_j` va en kg/mol y se deriva como `M_j = Σ_i A_ij · m_i`. Las masas atómicas de IUPAC/CIAAW (`SCIENTIFIC_MEASURED`) se publican en g/mol y se multiplican por 1e-3; este diccionario no copia esas cifras. `n_j = m_j / M_j`, con `m_j` en kg. La especie de cada depósito es `MODEL_ASSUMPTION` y está pendiente de Ciencia para T04 (véase la tabla del ADR-011). `BiomassKg` tiene pendiente decir si es masa seca o húmeda.

Hasta cerrar H-07, el dato de fuente es intensivo (kg/m² o fracción másica). El kg por celda lo obtiene el cargador de T03 con la geometría del escenario.

## AllocationBuffer

Misma lista y la misma unidad (kg por celda) que el depósito. Anota demanda y no resta. Un valor no finito o negativo es **DOM-VAL-005**, no VAL-001: no es el depósito.

## Agua por celda

`CellWaterBudget.RecordedTotalKg` (kg por celda) es el total contable. No es un cuarto stock. Tiene que coincidir con

`ResourcePool.Water + EnvironmentState.WaterIce + EnvironmentState.WaterVapor`

sumados en ese orden. Cada término y el total son finitos y ≥ 0. El error absoluto tiene que cumplir `ε ≤ MassBalanceAbsoluteToleranceKg + MassBalanceRelativeTolerance · |total|`. La relativa es `1e-9` y la absoluta es `1e-12` kg. Las dos son `MODEL_ASSUMPTION`. La absoluta está por encima del residuo de redondeo (≈ ε_mach · masa máxima de la celda; con stocks ≲ 10³ kg, ~10⁻¹³ kg) y por debajo de cualquier masa de agua con sentido; se revisa con H-07. El total de la comparación es el total registrado, no una masa esperada distinta. Si no cabe, **DOM-VAL-012**. Se parece a la tolerancia de TDD §10.1, no a VAL-007, que sigue sin implementarse.

Contar la misma masa en el líquido y en el hielo sin aumentar el total hace que el error no quepa. Si el total se infla para absorber ese doble conteo, esta regla no lo ve. El agua adsorbida o hidratada queda fuera de v0. La conservación entre ticks se comprueba en T04.

El depósito no tiene `WaterIce` ni `WaterVapor`. El ambiente no tiene `Water`.

## GeneExpression y Genome

| Campo | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| Expression | double | peso de modelo, adimensional | finito y en [0, 1]. −0 es válido | **VAL-005** |

El genoma no repite `GeneId` y se enumera en orden ordinal.

## Phenotype

No se guarda en la población ni en el estado. Cada componente es finito y ≥ 0.

| Campo | Unidad | Clasificación |
| --- | --- | --- |
| MaintenanceCost | fracción de EnergyReserveJ por sol (1 tick), en [0, 1]. Al ser una fracción de la reserva, el coste tiende a 0 cuando la reserva se agota, y v0 no modela mortalidad por agotamiento de la reserva de energía (`MODEL_ASSUMPTION`) | MODEL_ASSUMPTION (la escala puede ser GAME_BALANCE). DOM-VAL-014. Lo lanza el constructor de Phenotype; SimulationValidator no lo emite |
| ReproductionRate | tasa per cápita por sol (1 tick), en [0, 1]. 0 significa que no se reproduce. No es un factor multiplicativo. El techo ≤ 1 es una decisión de diseño | MODEL_ASSUMPTION (la escala puede ser GAME_BALANCE). DOM-VAL-015. Lo lanza el constructor de Phenotype; SimulationValidator no lo emite. Se calibran en T05/T06 |
| UvResistance, ThermalTolerance, DesiccationTolerance, PhotosyntheticEfficiency, BiofilmProduction | pendiente de MARTIS Ciencia | pendiente de MARTIS Ciencia |

El calculador copia la expresión del gen enlazado, sin coeficiente. Sin gen, el componente vale 0 (extremo de VAL-005, no una constante medida).

## Population

| Campo | Tipo | Unidad | Rango | Id | Se parece a |
| --- | --- | --- | --- | --- | --- |
| Abundance | double | recuento equivalente | finito y ≥ 0. El 0 es legal | **VAL-002** | es el VAL-002 |
| BiomassKg | double | kg | finito y ≥ 0 | **VAL-003** | es el VAL-003 |
| EnergyReserveJ | double | J | finito y ≥ 0 | DOM-VAL-006 | VAL-002, pero el TDD no llama recurso ni población a la energía |
| AgeDistributionMeanSols | double | sol | finito y ≥ 0 | DOM-VAL-007 | VAL-002, pero no es una población negativa |
| Viability | double | fracción del modelo | finito y en [0, 1] | DOM-VAL-008 | VAL-004. El TDD no dice que este campo sea una probabilidad. Definición biológica pendiente de MARTIS Ciencia |
| CellIndex | int | índice `y * width + x` | dentro de la rejilla | DOM-VAL-011 | ninguno de §10 |
| Genome | Genome | — | expresiones VAL-005 | VAL-005 | — |

## GridIndex y SimulationState

Índice lineal `y * width + x`. Las dimensiones las pasa quien construye el estado. El tipo no contiene el tamaño de ejemplo.

H-07: el prototipo habla de una rejilla de 64×64 celdas. Eso es un tamaño de prueba, no una constante del índice. El tamaño físico (metros por celda) no está fijado. No hay difusión.

`WaterBudgets` tiene una entrada por celda, alineada con `Resources` y `Environment`.

## InvariantViolation

| Campo | Significado |
| --- | --- |
| InvariantId | `VAL-001`, `VAL-002`, `VAL-003`, `VAL-005` o `DOM-VAL-001`..`DOM-VAL-016`. VAL-004 no lo emite este validador. DOM-VAL-014 y DOM-VAL-015 solo los lanza el constructor de `Phenotype`; `SimulationValidator` no los emite |
| Tick | el tick del estado |
| CellIndex | índice lineal |
| Population | la población, o null |
| Value | el número que falla. NaN cuando falta temperatura, presión o alcance en un escenario cargado: no es una medición NaN |
| Quantity | nombre del campo, el `GeneId`, o `GeneId.Empty` |

`Validate(state)` mira un mundo. `Validate(state, LoadedScenario)` exige temperatura, presión y alcance en cada celda.

## Parámetros (T03a)

El cargador genérico lee un documento con `metadata`, `location` y `parameters`. No abre un escenario y no escribe celdas. Cada entrada es un valor con su propia clasificación, su ámbito y su fuente. Dos entradas pueden compartir `field`.

| Nombre | Tipo | Qué guarda |
| --- | --- | --- |
| ParameterDefinition | clase | Una entrada: id único, field, valor, unit, classification, scope, version, status, used_in_v0, source, range, uncertainty, note, rationale, derived_from, conversion |
| ParameterLiteral | número, bool, texto, intervalo [min, max] o null | El JSON tal cual. Null no se convierte en 0 |
| ParameterClassification | SCIENTIFIC_MEASURED, SCIENTIFIC_ESTIMATED, MODEL_ASSUMPTION, GAME_BALANCE, SPECULATIVE, UNCLASSIFIED_PENDING | Las cinco primeras son las de ADR-000 R5. UNCLASSIFIED_PENDING solo se admite con status PENDING y value null, y ningún sistema lo recibe |
| ParameterScope | planetary_mean, regional:\<site\>, analog:\<nombre\> | No son los ámbitos de celda Lab y Game. `NoteRegionalCellUse(id, sitio)` avisa y no reescribe el valor si es planetary_mean, si es regional:A en una celda de B, o si es analog: en una celda regional (R5-07) |
| ParameterSource | citation, accessed, doi?, url?, where? | SCIENTIFIC_* exige al menos una fuente con doi o url. GAME_BALANCE puede llevar source null, como `mutation_rate` en el TDD §10.2 |
| ParameterCatalog | documento cargado | `RequireNumber`, `RequireBool`, `RequireString` y `RequireRange` entregan el valor con clasificación, ámbito, fuente, `derived_from`, `conversion`, `uncertainty`, `range`, `note` y `rationale` (R6-03). PENDING, HISTORICAL, SUPERADO y used_in_v0 false se conservan y fallan al leerse, nombrando el id. Cualquier otro status es texto. No hay valor por defecto |
| version | decimal | El JSON puede traer un entero (`1`) o un texto (`"0.1"`). Se guarda el número parseado y tiene que ser > 0 |
| conversion | objeto | Obligatorio con `derived_from` y prohibido sin él. Lleva `expression`, `factor` (finito y > 0), `factor_classification` (`MODEL_ASSUMPTION`) y `assumptions` (array de textos no vacío). El cargador no multiplica el factor |
| uncertainty | texto o objeto | Un texto se guarda tal cual. Un objeto se guarda como JSON canónico (claves ordenadas, tokens de número copiados) y no se interpreta |
| range | [min, max] | Intervalo cerrado. min > max se rechaza. Un número o un intervalo fuera de ese rango se rechaza. Los extremos son válidos. Un value de texto, bool o null con range numérico se rechaza (R6-01), salvo un PENDING o UNCLASSIFIED_PENDING cuyo value es null: ese rango no se comprueba al cargar y la entrada falla al leerse (R6-02) |

`derived_from` apunta al id del valor nativo. El derivado guarda su propio número. Nunca es `SCIENTIFIC_MEASURED`. Puede ser `SCIENTIFIC_ESTIMATED` (con DOI o URL) solo si la nativa es `SCIENTIFIC_*`. Si la nativa no es `SCIENTIFIC_*`, el derivado tampoco. La conversión es `MODEL_ASSUMPTION` en el factor, no en la etiqueta del derivado. El cargador no calcula el número. La geometría de la celda (H-07) sigue fuera: este tipo no pasa kg/m² a kg por celda. Un valor que no es `SCIENTIFIC_*` y no trae fuente necesita `rationale` o `note`.

## Configuración, azar y motor (T03a)

| Nombre | Tipo | Qué guarda |
| --- | --- | --- |
| SimulationConfiguration | record | MapWidth, MapHeight, DeltaTimeSols, WorldSeed, MaxVariantsPerSpeciesPerCell, EnableValidation. No existe DeltaTimeDays. `Validate` rechaza dimensiones ≤ 0, Δt no finito o ≤ 0, y un tope de variantes ≤ 0 |
| MaxVariantsPerSpeciesPerCell | int | Tope por especie y celda (ADR-006). El defecto 32 es GAME_BALANCE de rendimiento, no un dato de Marte |
| IRandomSource | interfaz | NextUInt64, NextDouble en [0, 1), NextInt sin sesgo de módulo, NextBool(p) con p en [0, 1] |
| RandomState | record | State e Increment del PCG, los dos ulong |
| Pcg32 | clase | Algoritmo `pcg32-v1`. La salida de 32 bits es la oficial. NextUInt64 junta dos salidas: la primera en los bits bajos |
| RngStreams | clase | Environment, Mutation, Reproduction, GeneticDrift, Events. Cada flujo sale de `WorldSeed` con una secuencia fija 1..5. El estado se exporta y se restaura |
| ISimulationCommand | interfaz en Contracts | Dato, sin Apply. El kind identifica al handler |
| ICommandHandler | interfaz en Simulation | Aplica un comando sobre SimulationState |
| ISimulationSystem | interfaz | Un paso del tick. Recibe el contexto y Δt en soles |
| SimulationContext | clase | Estado, RngStreams, configuración y Δt en soles |
| SimulationPipeline | pipeline-v0 | Quince pasos de §5.4, en orden fijo. apply-commands vacía la cola. CreateV0 pone EnvironmentSystem en update-environment: T constante, D = 0 y S = 0, sin sorteo. El resto siguen en stub |
| SimulationEngine | clase | Advance(steps). Advance(0) no corre sistemas. Un paso negativo lanza ArgumentOutOfRangeException. EnableValidation true valida al final del tick; false no valida. Si el tick lanza, el mundo, el PRNG y la cola vuelven al estado de antes del tick. ApplyCommand solo encola |
| PrototypeWorld | fábrica | Mundo vacío 64×64 para el CLI. No lee un escenario |
| MarsBaselineScenario | cargador | Lee el JSON de Ciencia con v0_field_map y scope_id, y los depósitos GAME_BALANCE en kg. No convierte wt % a kg. Los ids fuera del mapa no se entregan |
| ScenarioRunSnapshot | copia | Entorno, depósitos y PRNG. El hash es SHA-256 del texto canónico. La ida y vuelta restaura entorno y PRNG |
| SimulationSnapshot | vista en Contracts | Celdas, poblaciones y estadísticas copiadas. No sigue los cambios posteriores del mundo |

El CLI `simulate` acepta `--scenario`, `--seed`, `--ticks` y `--output`, crea ese mundo vacío, avanza los ticks e imprime el tick final. No escribe resultados y no abre `data/scenarios`.

## Metabolismo (T04)

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| ChemicalFormula | Domain.Chemistry | recuentos de átomos | símbolos de elemento con recuento > 0 | se deriva la masa molar, no se guarda |
| StoichiometricTerm.Moles | decimal | mol por unidad de avance | > 0 | `A·S = 0` por elemento (VAL-007, al cargar) |
| ReactionDefinition.CarbonMoles | decimal | mol C por unidad de avance | > 0 | pasa el avance en mol C al avance de la reacción |
| PopulationIntroduction / IntroducePopulationCommand | Simulation / Contracts | kg de biomasa seca | finito y > 0, organismo conocido, celda en la rejilla | la masa entra como flujo externo en `mass_balance.csv` |
| mass_balance.csv | archivo | kg, mol, J | una fila por intervalo de historia | `max_element_error_kg ≤ 1e-12 + 1e-9·|M|` |
| VAL-006 | invariante | kg | consumido ≤ depósito al empezar el tick | por celda y depósito |
| VAL-007 | invariante | kg | masa de cada elemento conservada | por celda, contando entradas externas |
| VAL-008 | invariante | J | gasto ≤ reserva inicial + ganancia | por población |

## Poblaciones (T05)

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| mᵢ (término de mortalidad) | double | probabilidad por tick | [0, 1] | `1 − exp(−r·estrés·Δt)`; fuera de rango es un error |
| m (mortalidad combinada) | double | probabilidad por tick | [0, 1] | `1 − Π(1 − mᵢ)` |
| Population.Viability | double | adimensional | [0, 1] | supervivencia del último tick, `1 − m` (DOM-VAL-008) |
| Population.AgeDistributionMeanSols | double | sol | ≥ 0 | supervivientes +Δt; recién nacidos entran con 0 |
| K | double | kg por celda | > 0 | `min(K_space, K_C, K_N, K_P)`; no crea recursos |
| necromasa | double (por tick) | kg por celda | ≥ 0 | se recicla en el mismo tick; VAL-007 la cuenta |
| extinción | regla | individuos | `N < 1` | la población se quita y su biomasa es necromasa |

## Evolución (T06)

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| mutation.trait_probability (μ) | double | probabilidad por gen y evento | [0, 1] | μ = 0 ⇒ ninguna variante nueva |
| mutation.sigma (σ) | double | expresión génica | ≥ 0 | `g′ = clamp(g + σz, 0, 1)` (VAL-005) |
| drift.effective_size (Nₑ) | double | individuos | ≥ 1 | separado de la abundancia agregada |
| variant.merge_tolerance | double | expresión génica | [0, 1] | variantes más cercanas se fusionan |
| MaxVariantsPerSpeciesPerCell | int | variantes | ≥ 1 (32) | nunca se supera tras `apply-selection-and-drift` (ADR-006) |
| mutations.csv | archivo | — | una fila por variante nueva | misma semilla ⇒ mismas filas (VAL-010) |
