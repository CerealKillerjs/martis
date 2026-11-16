# Diccionario de datos — T02

Tipos del modelo de dominio. Los identificadores `RegionId`, `CellId`, `PopulationId`, `OrganismId`, `GeneId` y `ReactionId` vienen de T01.

Las cifras que son una medición o una estimación llevan su fuente y su clasificación. El 88 775 s del sol es el redondeo de modelo de una medición citada abajo. Lo que no tiene fuente no es una medición. Las unidades que fija [ADR-011](ADR/ADR-011-unidades-del-modelo.md) siguen propuestas hasta que Lead acepte el ADR al fusionar.

VAL-001..005 significan solo lo que dice el TDD §10. Las reglas añadidas en este hito son DOM-VAL-001..014. La columna «se parece a» dice qué VAL del TDD recuerda, sin sustituirlo. La elección de unidad es `MODEL_ASSUMPTION`. La clasificación, la fuente y el ámbito van por valor en el JSON del escenario (TDD §10.2).

VAL-006, VAL-007 y VAL-008 no se comprueban: no hay consumo, balance elemental ni balance de energía.

## Tiempo

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| MartianTime.SecondsPerSol | int | s por sol | 88 775, una sola copia en `src/` | 1 tick = 1 sol. El entero es un redondeo de modelo (`MODEL_ASSUMPTION`) de una medición |

1 tick = 1 sol. `SecondsPerSol` = 88 775 s es un redondeo de modelo (`MODEL_ASSUMPTION`) del día solar medio de Marte. La medición (`SCIENTIFIC_MEASURED`) es D. R. Williams, NASA NSSDCA, Mars Fact Sheet, tabla «Orbital parameters», fila «Length of day (hrs)» 24,6597 h ≈ 88 774,92 s (https://nssdc.gsfc.nasa.gov/planetary/factsheet/marsfact.html; consultado el 2026-11-16; la página no tiene fecha de actualización). Contraste: Allison & McEwen (2000), 88 775,244 s (NASA GISS Mars24, https://www.giss.nasa.gov/tools/mars24/help/notes.html). La diferencia es ≤ 0,25 s por sol (< 3·10⁻⁶). No es el periodo sideral (24,6229 h ≈ 88 642,4 s).

No hay `DeltaTimeDays` en el código. La edad de la población va en soles.

## EnvironmentState

| Campo | Tipo | Unidad | Base temporal | Rango válido | Id | Se parece a |
| --- | --- | --- | --- | --- | --- | --- |
| TemperatureKelvin | AssignedDouble | K. Se usa la temperatura del aire | Media sobre los 88 775 s del sol. REMS mide aire y suelo, y son distintos. La media esconde el ciclo diurno (`MODEL_ASSUMPTION`) | unset, o finito y > 0 | DOM-VAL-001 si está asignado y no cumple; DOM-VAL-009 si un escenario cargado lo deja unset | VAL-001, con cota estricta y sin ser un recurso |
| PressurePascal | AssignedDouble | Pa | Media sobre los 88 775 s del sol | unset, o finito y ≥ 0. El 0 asignado es un dato | DOM-VAL-002; DOM-VAL-009 si el escenario lo deja unset | VAL-001, sin ser un recurso |
| UvRadiation | double | W/m², banda 200–400 nm, mediodía local, horizontal | No es media del sol. La fluencia diaria en J/m² por sol está pendiente y no se acumula tick a tick | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| IonizingAbsorbedDoseGyPerSol | double | Gy por sol (Gy = J/kg), tejido o agua, no silicio. GCR en superficie. SEP fuera de v0 | Acumulada en el sol. No se suma al sievert | finito y ≥ 0 | DOM-VAL-013 | VAL-001, sin ser un recurso |
| IonizingEquivalentDoseSvPerSol | double | Sv por sol (Sv = Gy × Q, Q adimensional). Usar Sv en microorganismos es `MODEL_ASSUMPTION` | Acumulada en el sol. No se compara con el gray | finito y ≥ 0 | DOM-VAL-013 | VAL-001, sin ser un recurso |
| SolarIrradiance | double | W/m² en superficie, no en el tope de la atmósfera | Media sobre el sol. La energía del tick es G · SecondsPerSol, en J/m². Alternativa no usada: J/m² por sol | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| WaterIce | double | kg por celda, condicionado a H-07 | Stock físico. La biología no lo consume | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| WaterVapor | double | kg por celda, condicionado a H-07 | Stock físico. La biología no lo consume | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| SoilPorosity | double | fracción de volumen | El intervalo [0, 1] es la definición de la fracción, no un supuesto | finito y en [0, 1] | DOM-VAL-003 | VAL-005, sin ser un gen |
| PerchlorateMassKg | double | kg por celda, tras convertir una fracción másica | Especie pendiente: ClO₄⁻ o una sal de Ca/Mg. La convierte el cargador de T03 | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| Scope | EnvironmentScope o null | — | Ámbito principal, por celda | en un escenario cargado, uno de PlanetaryMean, Regional(SiteId), Lab, Game | DOM-VAL-010 si falta | ninguno de §10 |

Referencias de dosis (`SCIENTIFIC_ESTIMATED`), no copiadas al código: Hassler et al. 2014, Science 343:1244797, doi:10.1126/science.1244797, 0,210 ± 0,040 mGy/día ≈ 2,158·10⁻⁴ Gy/sol; Guo et al. 2021, doi:10.1007/s00159-021-00136-5, 0,49 ± 0,08 mSv/día ≈ 5,03·10⁻⁴ Sv/sol. El paso de día a sol multiplica por 88 775 / 86 400 y es `MODEL_ASSUMPTION` porque supone un día RAD de 24 h.

`AssignedDouble` por defecto es unset. Unset no es el número 0: `Set(0)` es otro valor. `CheckInvariants` no exige que temperatura y presión estén asignadas; `SimulationValidator` con `ValidationContext.LoadedScenario` sí. Un mundo sin escenario puede pasar el validador con las dos sin asignar. Ese mismo estado no es un escenario válido.

`SiteId` es un identificador de sitio, distinto de organismo, gen y reacción. `Regional` exige texto.

## ResourcePool

Masa en kg por celda. Cada depósito finito y ≥ 0 es **VAL-001** («ningún recurso negativo»). El 0 es una masa asignada, no un hueco.

| Campo | Tipo | Unidad | Rango | Notas |
| --- | --- | --- | --- | --- |
| CarbonDioxide, Oxygen, Nitrogen, Phosphorus, Sulfur, Iron, OrganicCarbon, MineralCarbon | double | kg por celda | finito y ≥ 0 | VAL-001 |
| Water | double | kg por celda | finito y ≥ 0 | Solo el líquido accesible para la biología. No es hielo ni vapor. VAL-001 |

La matriz A guarda recuentos de átomos por especie, adimensionales. La masa molar no se almacena: se deriva como `M_j = Σ_i A_ij · m_i`, con `m_i` de IUPAC/CIAAW (`SCIENTIFIC_MEASURED`, sin cifras en este diccionario), y `n_j = m_j / M_j`. La especie de cada depósito es `MODEL_ASSUMPTION` y está pendiente de Ciencia para T04 (véase la tabla del ADR-011). `BiomassKg` tiene pendiente decir si es masa seca o húmeda.

Hasta cerrar H-07, el dato de fuente es intensivo (kg/m² o fracción másica). El kg por celda lo obtiene el cargador de T03 con la geometría del escenario.

## AllocationBuffer

Misma lista y la misma unidad (kg por celda) que el depósito. Anota demanda y no resta. Un valor no finito o negativo es **DOM-VAL-005**, no VAL-001: no es el depósito.

## Agua por celda

`CellWaterBudget.RecordedTotalKg` (kg por celda) es el total contable. No es un cuarto stock. Tiene que coincidir con

`ResourcePool.Water + EnvironmentState.WaterIce + EnvironmentState.WaterVapor`

sumados en ese orden. Cada término y el total son finitos y ≥ 0. El error absoluto tiene que cumplir `ε ≤ MassBalanceAbsoluteToleranceKg + MassBalanceRelativeTolerance · |total|`. La relativa es `1e-9` y la absoluta es 0 kg; las dos son `MODEL_ASSUMPTION` y la absoluta sigue pendiente de una cifra distinta de cero. Si no cabe, **DOM-VAL-012**. Se parece a la tolerancia de TDD §10.1, no a VAL-007, que sigue sin implementarse.

Contar la misma masa en el líquido y en el hielo sin aumentar el total hace que el error no quepa. Si el total se infla para absorber ese doble conteo, esta regla no lo ve. El agua adsorbida o hidratada queda fuera de v0. La conservación entre ticks se comprueba en T04.

El depósito no tiene `WaterIce` ni `WaterVapor`. El ambiente no tiene `Water`.

## GeneExpression y Genome

| Campo | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| Expression | double | peso de modelo, adimensional | finito y en [0, 1]. −0 es válido | **VAL-005** |

El genoma no repite `GeneId` y se enumera en orden ordinal.

## Phenotype

No se guarda en la población ni en el estado. Cada componente es finito y ≥ 0. No hay máximo.

| Campo | Unidad | Clasificación |
| --- | --- | --- |
| MaintenanceCost | fracción de EnergyReserveJ por sol, en [0, 1] | MODEL_ASSUMPTION (la escala puede ser GAME_BALANCE). DOM-VAL-014 |
| ReproductionRate | factor de crecimiento por individuo y por sol, finito y ≥ 0, sin máximo | MODEL_ASSUMPTION (la escala puede ser GAME_BALANCE) |
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
| InvariantId | `VAL-001`, `VAL-002`, `VAL-003`, `VAL-005` o `DOM-VAL-001`..`DOM-VAL-014`. VAL-004 no lo emite este validador |
| Tick | el tick del estado |
| CellIndex | índice lineal |
| Population | la población, o null |
| Value | el número que falla. NaN cuando falta temperatura, presión o alcance en un escenario cargado: no es una medición NaN |
| Quantity | nombre del campo, el `GeneId`, o `GeneId.Empty` |

`Validate(state)` mira un mundo. `Validate(state, LoadedScenario)` exige temperatura, presión y alcance en cada celda.
