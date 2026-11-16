# Diccionario de datos — T02

Tipos que introduce el modelo de dominio (S1-10..S1-14). Los identificadores tipados (`RegionId`, `CellId`, `PopulationId`, `OrganismId`, `GeneId`, `ReactionId`) ya estaban en T01 y no se redefinen aquí.

Ningún número de esta tabla es una medición marciana. El 0 con el que nacen las cantidades es el valor no asignado del tipo, no un estado inicial de Marte. Donde la unidad no está fijada, se escribe «pendiente de H-18» o «pendiente de MARTIS Ciencia». No hay factor de conversión ni coeficiente.

Los identificadores VAL-001..005 son los de TDD §10. `CELL-001` es un invariante estructural de este hito, no un VAL del TDD. VAL-006, VAL-007 y VAL-008 no se comprueban: no hay consumo, balance elemental ni balance de energía.

## EnvironmentState

Estado ambiental de una celda (TDD §2.2). Clase mutable. `CheckInvariants` rechaza el primer campo que no cumpla el rango.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| TemperatureKelvin | double | K | finito y ≥ 0 | VAL-001 |
| PressurePascal | double | Pa | finito y ≥ 0 | VAL-001 |
| UvRadiation | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |
| IonizingRadiation | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |
| SolarIrradiance | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |
| WaterIce | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |
| WaterVapor | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |
| SoilPorosity | double | pendiente de MARTIS Ciencia | finito y ≥ 0. Sin máximo declarado | VAL-001 |
| PerchlorateConcentration | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | VAL-001 |

## ResourcePool

Depósitos accesibles de una celda (TDD §2.3), no necesariamente la concentración química total. Clase mutable. La unidad de los nueve campos es **pendiente de H-18**.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| CarbonDioxide | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Water | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Oxygen | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Nitrogen | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Phosphorus | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Sulfur | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| Iron | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| OrganicCarbon | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |
| MineralCarbon | double | pendiente de H-18 | finito y ≥ 0 | VAL-001 |

Ninguna población resta de este depósito. La demanda se anota en `AllocationBuffer`.

## AllocationBuffer

Libro de demanda de una celda (TDD §12.3). Mismos nueve campos, mismo orden, misma unidad (**pendiente de H-18**) y el mismo rango que `ResourcePool`. Invariante VAL-001. Anotar una demanda no modifica el depósito. T02 no reparte ni consume el buffer.

## NamedQuantity

Par `(Name, Value)` con el nombre del campo y el valor actual. El orden de la lista `Quantities` es el orden de declaración del tipo dueño y el orden en que el validador recorre la celda.

## GeneExpression

Expresión normalizada de un gen (TDD §2.4). No es una tasa molecular medida.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| GeneId | GeneId | identificador de contenido | el de T01, incluida la cadena vacía | — |
| Expression | double | adimensional, peso de modelo | finito y en [0, 1]. −0 es válido | VAL-005 |

El constructor lanza `ArgumentOutOfRangeException` fuera de ese intervalo, incluido `NaN` y ±∞.

## Genome

Conjunto de `GeneExpression` sin genes repetidos. La enumeración sigue `GeneId.Value` con orden ordinal (`string.CompareOrdinal`), no el orden de inserción ni un orden por hash. Un genoma vacío es válido. `TryGetExpression` devuelve false si el gen no está; el 0 de salida en ese caso no es una expresión almacenada.

## Phenotype

Rasgos derivados (TDD §2.4). No se guarda en `Population`, `SimulationState` ni `OrganismDefinition`. Cada componente es finito y ≥ 0. No hay máximo: la escala biológica no está fijada (pendiente de MARTIS Ciencia).

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| UvResistance | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| ThermalTolerance | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| DesiccationTolerance | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| PhotosyntheticEfficiency | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| MaintenanceCost | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| ReproductionRate | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |
| BiofilmProduction | double | pendiente de MARTIS Ciencia | finito y ≥ 0 | cantidad no negativa |

`PhenotypeCalculator` copia la expresión del gen enlazado en `PhenotypeGeneBinding`, sin coeficiente. Un componente sin gen, o un gen que el genoma no tiene, aporta 0 (extremo inferior de VAL-005, no una constante medida). La modulación ambiental no está aplicada (pendiente de MARTIS Ciencia). `null` en el enlace significa «sin gen»; un `GeneId` vacío es un identificador real.

## Population

Población agregada en una celda (TDD §2.5). No hay objetos individuo. La abundancia puede ser fraccionaria. El fenotipo no se almacena.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| Id | PopulationId | identificador de partida | el de T01 | — |
| OrganismId | OrganismId | identificador de contenido | el de T01 | — |
| CellIndex | int | índice lineal `y * width + x` | dentro de `[0, width * height)` cuando el mundo lo comprueba | CELL-001 |
| Genome | Genome | — | no nulo; expresiones según VAL-005 | VAL-005 |
| Abundance | double | recuento equivalente, adimensional | finito y ≥ 0. El 0 es legal | VAL-002 |
| BiomassKg | double | kg | finito y ≥ 0 | VAL-003 |
| EnergyReserveJ | double | J | finito y ≥ 0 | VAL-002. VAL-008 no se evalúa |
| AgeDistributionMeanDays | double | el nombre del TDD dice días. H-05 sigue abierto: un sol no es un día terrestre. Este tipo no guarda un factor de conversión | finito y ≥ 0 | VAL-002 |
| Viability | double | fracción del modelo | finito y en [0, 1]. Los extremos son VAL-004, no un umbral medido. Definición biológica pendiente de MARTIS Ciencia | VAL-004 |

## OrganismDefinition

Definición de contenido (TDD §2.4). No crece una población y no guarda un fenotipo.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| Id | OrganismId | identificador de contenido | el de T01 | — |
| Name | string | — | no nulo y no vacío | estructural |
| Category | string | — | no nulo y no vacío | estructural |
| BaseGenome | GenomeDefinition | — | no nulo | el genoma que envuelve |
| Metabolism | MetabolismDefinition | — | no nulo | — |
| Reproduction | ReproductionDefinition | — | no nulo | — |

`GenomeDefinition` guarda el `Genome` base y ningún double propio.

`MetabolismDefinition` guarda `ReactionId` en el orden del llamador, sin repetidos. No hay tasas, rendimientos ni estequiometría (pendiente de MARTIS Ciencia).

`ReproductionDefinition` no tiene campos. No hay tasa ni probabilidad (pendiente de MARTIS Ciencia).

## GridIndex

Coordenada de celda. La anchura y la altura las pasa quien construye el índice (H-26); el tipo no fija un tamaño de mapa.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| X | int | índice de columna | `0 ≤ X < Width` | fuera de rango lanza `ArgumentOutOfRangeException` |
| Y | int | índice de fila | `0 ≤ Y < Height` | fuera de rango lanza `ArgumentOutOfRangeException` |
| Width | int | número de columnas | > 0 | — |
| Height | int | número de filas | > 0 | — |

`ToLinearIndex` devuelve `Y * Width + X`. Si el producto no cabe en `int`, lanza `OverflowException`.

## SimulationState

Mundo en arrays lineales (TDD §12.2, H-02). `new SimulationState(width, height)` exige las dos dimensiones > 0 y calcula `checked(width * height)` antes de reservar.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| Width | int | columnas | > 0 | constructor |
| Height | int | filas | > 0 | constructor |
| CellCount | int | celdas | `Width * Height`, sin desbordar | — |
| Tick | long | paso de simulación | el valor que tenga el estado; el validador lo copia en cada violación | — |
| Environment | EnvironmentState[] | — | longitud `CellCount`, una instancia por celda | VAL-001 en cada campo |
| Resources | ResourcePool[] | — | longitud `CellCount`, una instancia por celda | VAL-001 en los 9 depósitos |
| AllocationBuffers | AllocationBuffer[] | — | longitud `CellCount`, alineado con `Resources` | VAL-001. No resta de `Resources` |
| Populations | List\<Population\> | — | sin entradas nulas. El orden de la lista es el orden de visita | VAL-002..005 y CELL-001 |
| Statistics | SimulationStatistics | — | no nulo | — |

La celda de índice `i` es `y * Width + x`. Añadir una población a la lista no mueve recursos.

`SimulationStatistics` no declara contadores ni valores medidos. Existe porque §12.2 lo pide en el estado.

## InvariantViolation

Resultado de `SimulationValidator.Validate`. No lanza en la primera violación: devuelve la lista completa, en este orden: celdas por índice lineal ascendente; dentro de la celda, ambiente, depósito y buffer, cada uno en orden de declaración; después las poblaciones en el orden de la lista; dentro de cada población, rango de celda, abundancia, biomasa, reserva de energía, media de edad, viabilidad y genes en orden de `GeneId`.

| Campo | Tipo | Unidad | Rango válido | Invariante |
| --- | --- | --- | --- | --- |
| InvariantId | string | — | no vacío. `VAL-001`..`VAL-005` o `CELL-001` | — |
| Tick | long | paso | el `Tick` del estado en el momento de la comprobación | — |
| CellIndex | int? | índice lineal | la celda afectada, o el `CellIndex` de la población aunque quede fuera del array. Null si no hay celda | — |
| Population | PopulationId? | identificador | la población afectada, o null | — |
| Value | double | la de la cantidad rota | el número que incumplió el rango, sin sustituirlo | — |
| Quantity | string | — | no vacío. Nombre del campo, el texto del `GeneId`, o `GeneId.Empty` si ese identificador no tiene texto | — |

`Validate(null)` lanza `ArgumentNullException`. Una ranura o una población nula lanza `InvalidOperationException`: es un grafo roto, no un VAL.
