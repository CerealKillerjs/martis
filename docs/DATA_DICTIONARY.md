# Diccionario de datos — T02

Tipos del modelo de dominio. Los identificadores `RegionId`, `CellId`, `PopulationId`, `OrganismId`, `GeneId` y `ReactionId` vienen de T01.

Ningún número de aquí es una medición marciana. Las unidades que fija [ADR-011](ADR/ADR-011-unidades-del-modelo.md) están propuestas: las valida MARTIS Ciencia. Lo que no tiene cifra en esa decisión lleva `TODO: pendiente MARTIS Ciencia` y no se inventa.

VAL-001..005 significan solo lo que dice el TDD §10. Las reglas añadidas en este hito son DOM-VAL-001..012. La columna «se parece a» dice qué VAL del TDD recuerda, sin sustituirlo.

VAL-006, VAL-007 y VAL-008 no se comprueban: no hay consumo, balance elemental ni balance de energía.

## Tiempo

| Nombre | Tipo | Unidad | Rango | Invariante |
| --- | --- | --- | --- | --- |
| MartianTime.SecondsPerSol | int | s por sol | 88 775, una sola copia en el código | 1 tick = 1 sol. Cita NSSDC pendiente de MARTIS Ciencia |

No hay `DeltaTimeDays` en el código. La edad de la población va en soles.

## EnvironmentState

| Campo | Tipo | Unidad | Clasificación | Rango válido | Id | Se parece a |
| --- | --- | --- | --- | --- | --- | --- |
| TemperatureKelvin | AssignedDouble | K | unidad del TDD §2.2 | unset, o finito y > 0 | DOM-VAL-001 si está asignado y no cumple; DOM-VAL-009 si un escenario cargado lo deja unset | VAL-001, con cota estricta y sin ser un recurso |
| PressurePascal | AssignedDouble | Pa | unidad del TDD §2.2 | unset, o finito y ≥ 0. El 0 asignado es un dato | DOM-VAL-002; DOM-VAL-009 si el escenario lo deja unset | VAL-001, sin ser un recurso |
| UvRadiation | double | W/m² en superficie | MODEL_ASSUMPTION | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| IonizingRadiation | double | W/m² en superficie | MODEL_ASSUMPTION | finito y ≥ 0. Unidad dosimétrica pendiente de MARTIS Ciencia | DOM-VAL-004 | VAL-001 |
| SolarIrradiance | double | W/m² en superficie, no en el tope de la atmósfera | MODEL_ASSUMPTION | finito y ≥ 0 | DOM-VAL-004 | VAL-001 |
| WaterIce | double | kg por celda | MODEL_ASSUMPTION | finito y ≥ 0. Reserva física; la biología no la consume | DOM-VAL-004 | VAL-001 |
| WaterVapor | double | kg por celda | MODEL_ASSUMPTION | finito y ≥ 0. Reserva física; la biología no la consume | DOM-VAL-004 | VAL-001 |
| SoilPorosity | double | fracción adimensional | MODEL_ASSUMPTION | finito y en [0, 1] | DOM-VAL-003 | VAL-005, sin ser un gen |
| PerchlorateConcentration | double | kg de perclorato por celda | MODEL_ASSUMPTION | finito y ≥ 0. No es una concentración molar | DOM-VAL-004 | VAL-001 |
| Scope | EnvironmentScope o null | — | — | en un escenario cargado, uno de PlanetaryMean, Regional(SiteId), Lab, Game | DOM-VAL-010 si falta | ninguno de §10 |

`AssignedDouble` por defecto es unset. Unset no es el número 0: `Set(0)` es otro valor. `CheckInvariants` no exige que temperatura y presión estén asignadas; `SimulationValidator` con `ValidationContext.LoadedScenario` sí. Un mundo sin escenario puede pasar el validador con las dos sin asignar. Ese mismo estado no es un escenario válido.

`SiteId` es un identificador de sitio, distinto de organismo, gen y reacción. `Regional` exige texto.

## ResourcePool

Masa en kg por celda. Cada depósito finito y ≥ 0 es **VAL-001** («ningún recurso negativo»). El 0 es una masa asignada, no un hueco.

| Campo | Tipo | Unidad | Rango | Notas |
| --- | --- | --- | --- | --- |
| CarbonDioxide, Oxygen, Nitrogen, Phosphorus, Sulfur, Iron, OrganicCarbon, MineralCarbon | double | kg por celda | finito y ≥ 0 | VAL-001 |
| Water | double | kg por celda | finito y ≥ 0 | Solo el líquido accesible para la biología. No es hielo ni vapor. VAL-001 |

La matriz A (`ElementalCompositionMatrix`) guardará las masas molares, clasificación `MODEL_ASSUMPTION`. Todavía no tiene números. TODO: pendiente MARTIS Ciencia.

## AllocationBuffer

Misma lista y la misma unidad (kg por celda) que el depósito. Anota demanda y no resta. Un valor no finito o negativo es **DOM-VAL-005**, no VAL-001: no es el depósito.

## Agua por celda

`CellWaterBudget.RecordedTotalKg` (kg por celda) es el total contable. No es un cuarto stock. Tiene que coincidir con

`ResourcePool.Water + EnvironmentState.WaterIce + EnvironmentState.WaterVapor`

sumados en ese orden. Cada término y el total son finitos y ≥ 0. Si el total no cuadra, **DOM-VAL-012**. Se parece a una conservación de masa, no a un VAL del §10: VAL-007 sigue sin implementarse.

Contar la misma masa en el líquido y en el hielo sin aumentar el total registrado hace que la suma no cuadre: el validador lo ve. Pasar masa de un compartimento a otro, conservándola, llega en T03/T04 y no está en este código.

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
| MaintenanceCost | adimensional por tick | MODEL_ASSUMPTION |
| ReproductionRate | adimensional por tick | MODEL_ASSUMPTION |
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
| InvariantId | `VAL-001`, `VAL-002`, `VAL-003`, `VAL-005` o `DOM-VAL-001`..`DOM-VAL-012`. VAL-004 no lo emite este validador |
| Tick | el tick del estado |
| CellIndex | índice lineal |
| Population | la población, o null |
| Value | el número que falla. NaN cuando falta temperatura, presión o alcance en un escenario cargado: no es una medición NaN |
| Quantity | nombre del campo, el `GeneId`, o `GeneId.Empty` |

`Validate(state)` mira un mundo. `Validate(state, LoadedScenario)` exige temperatura, presión y alcance en cada celda.
