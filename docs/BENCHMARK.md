# Rendimiento medido (S1-35, T10)

Estos números son **medidos**, no objetivos cumplidos. No hay equipo de referencia documentado (H-20), así que no se dice que §7.4 se cumpla ni que se incumpla. Fecha: 2026-11-16. Commit: rama `claude/t10-benchmarks`.

## Equipo

| | |
| --- | --- |
| CPU | Intel Xeon 2,10 GHz, 4 núcleos lógicos (contenedor en la nube, compartido) |
| SO | Ubuntu 24.04.5 LTS, x64 |
| Runtime | .NET 10.0.15, RyuJIT x86-64-v4, GC de estación de trabajo concurrente |
| SDK | 10.0.115 (la CI usa 10.0.404) |
| BenchmarkDotNet | 0.15.8, trabajo `ShortRun` (3 iteraciones de calentamiento y 3 medidas, 1 proceso) |

Con `ShortRun` el error es grande (columna *Error*). Sirve para ver el orden de magnitud, no para comparar cambios pequeños.

## Cómo se reproduce

```bash
dotnet run -c Release --project benchmarks/Martis.Benchmarks -- --filter '*' --job short   # BenchmarkDotNet
dotnet run -c Release --project benchmarks/Martis.Benchmarks -- --profile 1000             # tiempo por paso del pipeline
```

## Simulación completa (`FullSimulation`)

| Mundo | Ticks | Media | Asignado |
| --- | ---: | ---: | ---: |
| Vacío, pipeline-v0 (prototipo de §19) | 100 | 0,32 s | 428 MB |
| Vacío, pipeline-v0 | 1000 | 3,16 s | 4,28 GB |
| FIRST COLONY (A01 diseñado + D01 en una celda), pipeline-v3 | 100 | 0,60 s | 708 MB |
| FIRST COLONY, pipeline-v3 | 1000 | 6,05 s | 7,08 GB |

## Carga de §7.4 (`StressWorld` y `--profile`)

Hay 4096 celdas, 3 organismos y 10 000 poblaciones al empezar: A01 y D01 en todas las celdas y E01 en las primeras 1808. El tope es de 32 variantes por especie y celda.

| Ticks | Media | Asignado |
| ---: | ---: | ---: |
| 10 | 0,28 s | 205 MB |
| 100 | 5,35 s | 3,28 GB |
| 1000 (`--profile`) | 175 s | 71,2 GB; 890 GC gen0 y 412 gen2; memoria de trabajo máxima 314 MB |

En 1000 ticks las mutaciones llevan las poblaciones de 10 000 a 79 647. Ese número está por encima de la carga de §7.4, y el coste por tick crece con él: unos 110 ms/tick en los primeros 100 ticks y unos 175 ms/tick de media en los 1000.

Tiempo por paso (`--profile 1000`):

| Paso | Tiempo | % |
| --- | ---: | ---: |
| apply-selection-and-drift | 41,7 s | 23,8 |
| calculate-metabolism | 34,4 s | 19,7 |
| calculate-mortality | 34,2 s | 19,5 |
| update-biomass-and-energy | 25,3 s | 14,4 |
| allocate-shared-resources | 9,3 s | 5,3 |
| calculate-resource-availability | 7,4 s | 4,2 |
| validate-conservation | 5,8 s | 3,3 |
| apply-mutations | 4,8 s | 2,7 |
| generate-reproduction | 2,7 s | 1,5 |
| resto (entorno, comandos, reciclado, estadísticas) | 0,5 s | 0,3 |

## Snapshots (`Snapshots`, carga de §7.4 tras 20 ticks)

| Operación | Media | Asignado |
| --- | ---: | ---: |
| `StateSnapshot.Capture` | 7,9 ms | 7,5 MB |
| `Restore` | 16,9 ms | 11,3 MB |
| Escribir binario MRTS | 22,4 ms | 31,1 MB |
| Leer binario MRTS (con SHA-256) | 20,4 ms | 17,0 MB |
| Escribir JSON canónico | 84,8 ms | 171,8 MB |

A los 1000 ticks (79 647 poblaciones), capturar y escribir el binario tarda 188 ms y ocupa 20,2 MB.

## Comparación con §7.4 (orientativa, sin equipo de referencia)

| Objetivo §7.4 | Medido aquí | Lectura |
| --- | --- | --- |
| 1000 ticks < 10 s con 10 000 poblaciones | 175 s, pero con 10 000 → 79 647 poblaciones | lejos del objetivo. Con 10 000 estables serían ~110 ms/tick, unos 110 s |
| Snapshot < 1 s | 8 ms de captura y 22 ms de escritura binaria (10 000 a 30 000 poblaciones); 188 ms con 80 000 | dentro |
| Memoria < 256 MB | 314 MB de memoria de trabajo máxima con 80 000 poblaciones | por encima con esa carga |
| FIRST COLONY real (1 celda viva) | 6 ms/tick | el juego va sobrado a 8 soles/s |

## Cambios de T10 en este PR

- Los parámetros de población (T05) y de evolución (T06) se leen una vez por organismo (`MetabolismWorkspace.PopulationParametersOf` y `EvolutionParametersOf`). Antes se leían por población y tick.
- `SimulationState.AllocatePopulationId` solo recorre las poblaciones añadidas desde la llamada anterior. Antes recorría todas en cada mutación, lo que era O(N²) por tick. Con la carga de §7.4, `apply-mutations` bajó del 36 % al 2,7 % del tiempo.
- Los resultados no cambian: la suite completa y las pruebas de reproducibilidad bit a bit siguen en verde.

## Siguientes pasos recomendados (no hechos)

1. **Presupuesto de poblaciones.** 32 variantes × 3 especies × 4096 celdas permite 393 216 poblaciones. §7.4 piensa en 10 000. Hace falta decidir un tope global o un umbral de fusión mayor (H-29).
2. **Asignaciones por tick.** El mundo vacío asigna unos 4 MB por tick: `StateCheckpoint.Capture`, que copia el mundo para poder deshacer el tick, y las listas del validador. Reutilizar los búferes bajaría el GC (712 colecciones gen2 en 1000 ticks del mundo vacío).
3. **`SelectionDriftSystem.Groups`.** Usa un `SortedDictionary` de tuplas que se reconstruye cada tick. Un índice por celda que se mantenga entre ticks lo evitaría.
4. **Metabolismo, mortalidad y biomasa** iteran por población con búsquedas por id de organismo (cadena). Usar un índice entero de organismo y arrays por población ayudaría.
5. **Paralelismo por celdas** (ADR-007). Solo cuando todo lo anterior esté medido y sin romper VAL-010.
