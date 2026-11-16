# SPRINT 01 — Infraestructura ejecutable y prototipo CLI

Fuente: TDD v1.0 §18 (cinco jornadas), §18.1 (checklist), §19 (definición de terminado). Estado: **propuesta pendiente de visto bueno de Dani**.

**Meta:** que el prototipo CLI de §19 funcione de extremo a extremo (MarsBaseline, 64×64, 1000 ticks, validar, snapshot, guardar resultados, exit 0). **Godot no hace falta para el CLI**, así que las tareas de Godot son paralelas y no bloquean.

## Reglas del sprint

- Cada tarea es del tamaño de **un PR** y se revisa con [`PR_CHECKLIST.md`](PR_CHECKLIST.md).
- **Dueños sugeridos:** **MARTIS Motor** (código de motor/infra), **MARTIS Ciencia** (modelos, parámetros, clasificación `SCIENTIFIC_MEASURED / SCIENTIFIC_ESTIMATED / MODEL_ASSUMPTION / GAME_BALANCE / SPECULATIVE`), **MARTIS QA** (tests, revisión de PR, invariantes VAL-001..010). Otro agente revisa siempre el PR; QA revisa todos.
- Los criterios de aceptación son comandos o tests concretos. Los comandos asumen la raíz del repo.
- Sin datos científicos inventados: todo número del escenario lleva clasificación; sin fuente ⇒ `MODEL_ASSUMPTION`/`GAME_BALANCE`/`SPECULATIVE` y marcado como ilustrativo. Los objetivos de §7.4 no se tratan como mediciones.
- Convención del CLI propuesta: exit 0 = éxito; exit 2 = argumentos inválidos; exit 1 = violación de invariante o error de ejecución.

## Prerrequisitos

1. ADR-002 está aceptado: SDK 10.0.404, `rollForward` `latestPatch`, `allowPrerelease` false y TFM `net10.0` para librerías y CLI (bloquea S1-02).
2. H-01 y H-02 están cerrados en [ADR-000](ADR/ADR-000-reglas-de-arquitectura.md) (grafo y comandos como datos). H-03 (tipos de snapshot) sigue abierto en [TDD_HUECOS.md](TDD_HUECOS.md).
3. H-16: ids de escenario (`mars_baseline`) y salidas mínimas del prototipo.
4. El repo remoto lo crea Dani/lead cuando decida; hasta entonces todo es local.

## Camino crítico hacia el CLI

`S1-01 → S1-02 → S1-03 → S1-04 (CLI esqueleto) → S1-09..S1-13 (dominio) → S1-14 → S1-15 (validador) → S1-18 → S1-19 → S1-20 → S1-22 → S1-23 (CLI avanza ticks) → S1-25..S1-27 (escenario + ambiente) → S1-31 → S1-32 (CLI flujo completo §19)`.

Fuera del camino crítico (si el sprint se retrasa se mueven al Sprint 2, en este orden de sacrificio): S1-35 (benchmark), S1-08, S1-07, S1-06.

---

## Día 1 — Infraestructura

### S1-01 · Repositorio, README y .gitignore — *Motor*
Crear repo Git (local o remoto según Dani), `README.md` (qué es MARTIS, requisitos, comandos de build/test/CLI), `.gitignore` .NET/Godot, y dejar en el repo [docs/tdd/MARTIS_TDD_v1.0.txt](tdd/MARTIS_TDD_v1.0.txt), los ADR, [ROADMAP.md](ROADMAP.md), este sprint y [PR_CHECKLIST.md](PR_CHECKLIST.md).
**Aceptación:** `git status` limpio tras el primer commit; `git ls-files docs/ADR | wc -l` ≥ 12; el README contiene los comandos exactos de `dotnet build`, `dotnet test` y `dotnet run --project src/Martis.Cli`.

### S1-02 · global.json y propiedades de build — *Motor*
`global.json` con el SDK de ADR-002 (versión vigente el día de la instalación), `Directory.Build.props` (TFM, `Nullable`, `TreatWarningsAsErrors`, análisis), `Directory.Packages.props` (gestión central de paquetes; versiones consultadas en NuGet ese día, no inventadas).
**Aceptación:** `dotnet --version` imprime la versión de `global.json`; `dotnet restore` exit 0; el repo no contiene ningún `<PackageReference Version=...>` fuera de `Directory.Packages.props`.

### S1-03 · MARTIS.sln y proyectos con referencias — *Motor*
Crear `MARTIS.sln` con `Martis.Domain`, `Martis.Simulation`, `Martis.Contracts`, `Martis.Persistence`, `Martis.Cli` y los proyectos de test de §1.2, con las referencias del grafo de ADR-000. **No** incluir `Martis.Game` (H-17). Un test de humo por proyecto de test.
**Aceptación:** `dotnet build MARTIS.sln -c Release` exit 0 y `dotnet test MARTIS.sln` exit 0; `Martis.Domain.csproj` no tiene `PackageReference` ni `ProjectReference`.

### S1-04 · Esqueleto de Martis.Cli — *Motor* (**el CLI llega primero**)
`Program.cs` con subcomando `simulate` y opciones `--scenario --seed --ticks --output`, analizador de argumentos propio (sin paquete nuevo), sin motor aún: valida argumentos, imprime la versión y sale.
**Aceptación:** `dotnet run --project src/Martis.Cli -- simulate --scenario mars_baseline --seed 12345 --ticks 1 --output /tmp/m1; echo $?` imprime `0`; `... simulate --ticks abc; echo $?` imprime `2` con un mensaje de uso; `--seed` acepta el rango completo `ulong`.

### S1-05 · Test de arquitectura (ADR-000) — *QA*
Test en `Martis.Integration.Tests` que parsea los `.csproj` y comprueba: aristas permitidas, ausencia de ciclos, `Martis.Domain` sin paquetes ni namespaces `Godot*`/`*Sqlite*`, TFM homogéneo. Más un chequeo de texto (`System.Random`, `GetHashCode`, `Parallel.`, `AsParallel`, `Task.Run`, `DateTime.Now`, `Guid.NewGuid`) sobre `src/Martis.Domain` y `src/Martis.Simulation`.
**Aceptación:** `dotnet test --filter Category=Architecture` pasa; en la descripción del PR hay evidencia de que añadir temporalmente `Domain → Simulation` hace fallar el test.

### S1-06 · CI mínima en Linux (opcional, H-23) — *Motor*
Workflow que ejecuta restore, build Release, test y el CLI de S1-04 en Ubuntu, sin Godot.
**Aceptación:** pipeline verde en el PR; ejecuta exactamente `dotnet build MARTIS.sln -c Release` y `dotnet test MARTIS.sln`.

### S1-07 · Spike Godot .NET + registro de versión (paralela, no bloquea) — *Motor*
Instalar la versión de ADR-001 (provisional 4.7.2 .NET), crear un proyecto mínimo en `src/Martis.Game` con un script C# que escribe en consola, y dejar en la nota de esta tarea la versión, `Godot.NET.Sdk`, el SDK .NET, el sistema operativo y la URL oficial de descarga.
**Aceptación:** `dotnet build src/Martis.Game` exit 0 en la máquina de desarrollo; la nota de la tarea lista versión de Godot, `Godot.NET.Sdk`, SDK .NET y sistema operativo.

### S1-08 · Spike de TFM Game ↔ librerías (paralela, no bloquea) — *Motor*
Comprobar y documentar si el proyecto Godot elegido puede referenciar `Martis.Contracts`/`Martis.Simulation` según el TFM de ADR-002 (pendiente de verificar en ADR-001/002).
**Aceptación:** el informe de esta tarea dice si compila o no, y el comando usado; si no compila, propuesta de multi-target (ADR-002, opción B). El TFM de Game sigue pendiente de este spike.

---

## Día 2 — Dominio

### S1-09 · IDs tipados — *Motor*
`RegionId`, `CellId`, `PopulationId`, `OrganismId`, `GeneId`, `ReactionId` (`readonly record struct`, §2.1) en `Martis.Domain.Common`.
**Aceptación:** `dotnet test --filter FullyQualifiedName~Martis.Domain.Tests.Common` pasa: igualdad por valor, `ToString` estable, ningún ID se usa como semilla ni orden por `GetHashCode`.

### S1-10 · EnvironmentState y ResourcePool — *Motor*
Clases de §2.2/§2.3 con documentación de unidades en XML-doc (K, Pa, m, s, kg, J) y método de comprobación de invariantes.
**Aceptación:** tests: valores por defecto 0; comprobación rechaza negativos, `NaN` e `±∞` en cada campo (VAL-001/003, H-13). Las unidades de `ResourcePool` quedan marcadas «pendiente de H-18» en el XML-doc, sin inventar unidad.

### S1-11 · Genome y GeneExpression — *Motor*
`Genome` con expresiones en [0,1] (§2.4), rechazando `NaN`/`±∞`, con enumeración **ordenada por `GeneId`** (determinismo).
**Aceptación:** tests: −0,01, 1,01 y `NaN` lanzan `ArgumentOutOfRangeException`; 0 y 1 son válidos; el orden de enumeración no depende del orden de inserción (VAL-005).

### S1-12 · Population, OrganismDefinition y Phenotype — *Motor*
`Population` (§2.5), `OrganismDefinition` y `Phenotype` (§2.4) como tipos de dominio sin lógica de crecimiento.
**Aceptación:** tests: abundancia/biomasa negativas o no finitas rechazadas (VAL-002/003); `Phenotype` no se almacena como fuente de verdad (solo cálculo).

### S1-13 · SimulationState (mundo 64×64) — *Motor*
`SimulationState(width, height)` (§12.2), con `checked(width*height)`, `Tick`, arrays de `EnvironmentState` y `ResourcePool`, `Populations`, `Statistics`; `GridIndex` basado en dimensiones de configuración (H-26). Ubicado en `Martis.Domain` (H-02).
**Aceptación:** test: `new SimulationState(64, 64)` tiene 4.096 entornos y recursos; `GridIndex(63,63).ToLinearIndex() == 4095`; fuera de rango lanza excepción.

### S1-14 · SimulationValidator con violaciones estructuradas — *Motor*
Validador (H-15) que devuelve una lista de violaciones `{VAL-id, tick, celda, población, valor}` en vez de lanzar en la primera; cubre VAL-001..005 con `double.IsFinite`; los 9 recursos de `ResourcePool`.
**Aceptación:** test por regla con un caso válido y uno inválido; con dos violaciones simultáneas devuelve ambas; `dotnet test --filter FullyQualifiedName~Validation` pasa.

### S1-15 · Tests de invariantes VAL-001..005 — *QA*
Batería adicional independiente de S1-14: un test nominal y uno de borde por invariante, incluyendo `NaN`, `+∞`, `−0`, 0 exacto y 1 exacto para genes. Revisa que ningún test sea tautológico (H-11).
**Aceptación:** `dotnet test --filter Category=Invariants` pasa; cada test llama a código de `src/`, no solo a `Math.*`.

### S1-16 · Diccionario de parámetros y modelo científico inicial — *Ciencia*
Esta tarea añade en `docs/` el diccionario de parámetros (esquema `id/value/unit/classification/source/version`; `SCIENTIFIC_*` exige `source` con URL o DOI y fecha de consulta) y el modelo científico, con las 5 categorías, la advertencia «una media planetaria no sustituye una medición regional» y las 10 filas de §10.3 de [docs/tdd/MARTIS_TDD_v1.0.txt](tdd/MARTIS_TDD_v1.0.txt):

1. Temperatura regional — datos de estaciones y modelos climáticos.
2. Radiación UV — mediciones instrumentales y modelos espectrales.
3. Radiación ionizante — datos RAD y modelos de transporte.
4. Agua accesible — cartografía de hielo y propiedades del suelo.
5. Percloratos — mediciones locales y caracterización química.
6. Nutrientes disponibles — análisis mineralógico.
7. Difusión de gases — modelo físico y condiciones locales.
8. Presión regional — altitud y datos atmosféricos.
9. Pérdidas atmosféricas — modelos de escape atmosférico.
10. Tasas metabólicas — bibliografía experimental y calibración.

**Aceptación:** los dos documentos están en `docs/` y un revisor QA confirma que no contienen valores numéricos de Marte sin fuente enlazada; la lista de §10.3 tiene estas 10 filas.

### S1-17 · ParameterDefinition + cargador JSON — *Motor*
Registro `ParameterDefinition` y cargador/validador JSON (BCL) que rechaza: sin `classification`, clasificación desconocida, `SCIENTIFIC_*` sin `source`, `unit` vacía, `version` no positiva.
**Aceptación:** `dotnet test --filter FullyQualifiedName~Parameters` pasa con un caso de rechazo por cada regla y uno válido (el ejemplo `mutation_rate` de §10.2 cargado como `GAME_BALANCE`).

---

## Día 3 — Motor

### S1-18 · SimulationConfiguration — *Motor*
Record de §12.1 con `Validate()`; unidad de tiempo definida (H-05) y `MaxVariantsPerSpeciesPerCell` (H-06); rechaza `NaN`.
**Aceptación:** tests: dimensiones ≤ 0, Δt ≤ 0 o no finito y límite ≤ 0 lanzan `InvalidOperationException`; configuración válida pasa.

### S1-19 · PCG32 e IRandomSource — *Motor*
`IRandomSource`, `RandomState(ulong State, ulong Increment)` y PCG32 `pcg32-v1` (ADR-003) con `NextUInt64`, `NextDouble` en [0,1), `NextInt` sin sesgo de módulo, `NextBool(p)`.
**Aceptación:** tests con **vectores de referencia de la implementación oficial de PCG** (versión y fuente citadas en el test); misma semilla ⇒ misma secuencia; `NextBool(-0.1)` y `NextBool(1.1)` lanzan excepción (VAL-004).

### S1-20 · Cinco flujos RNG independientes — *Motor*
`RngStreams` con Environment, Mutation, Reproduction, GeneticDrift y Events, derivación determinista desde `WorldSeed` documentada en ADR-003 (H-04); estado exportable/restaurable.
**Aceptación:** test: consumir 1000 valores del flujo A no cambia los 100 siguientes del flujo B; guardar `RandomState` de cada flujo y restaurarlo reproduce la continuación exacta.

### S1-21 · Tests de PRNG (QA) — *QA*
Revisión y tests adversariales: semillas 0, 1, `ulong.MaxValue`; semillas distintas ⇒ secuencias distintas; límites de `NextInt`; sanidad estadística con tolerancia explícita (sin afirmar calidad PRNG); búsqueda de `System.Random`/`GetHashCode` en `src/` (S1-05).
**Aceptación:** `dotnet test --filter Category=Random` pasa; el PR documenta la tolerancia usada y su justificación.

### S1-22 · ISimulationSystem, SimulationContext, ISimulationEngine y SimulationEngine — *Motor*
Contratos de §5.1–5.5 con `RngStreams` en el contexto. Los comandos son datos: un handler en Martis.Simulation aplica el comando sobre `SimulationState` (ADR-000). Registro **ordenado y fijo** de sistemas (H-28) y `Advance(steps)`; sistemas stub para los pasos de §5.4 (sin lógica).
**Aceptación:** tests: `Advance(0)` no cambia el tick; `Advance(-1)` lanza `ArgumentOutOfRangeException`; tras `Advance(1000)` el tick es 1000; el orden de ejecución de los sistemas coincide con el registro (sistema de prueba que anota su paso).

### S1-23 · CLI ejecuta el motor — *Motor* (**el CLI llega primero**)
`simulate` crea estado vacío 64×64, `SimulationEngine` con sistemas stub, avanza `--ticks`, imprime el tick final y sale con 0.
**Aceptación:** `dotnet run --project src/Martis.Cli -- simulate --scenario mars_baseline --seed 12345 --ticks 1000 --output /tmp/m2; echo $?` imprime el tick 1000 y luego `0`.

### S1-24 · Test de determinismo del motor — *QA*
Sistema de prueba que modifica estado con el flujo Environment; dos motores con la misma semilla tras 1000 ticks tienen estados idénticos **bit a bit** (`BitConverter.DoubleToInt64Bits`), con otra semilla difieren (VAL-010).
**Aceptación:** `dotnet test --filter Category=Determinism` pasa y se ejecuta dos veces seguidas con el mismo resultado.

---

## Día 4 — Entorno

### S1-25 · Parámetros de MarsBaseline con clasificación — *Ciencia*
`data/scenarios/mars_baseline.json` con los parámetros iniciales (temperatura, presión, radiación UV e ionizante, irradiancia solar, agua/hielo/vapor, porosidad, perclorato, recursos, amplitudes/periodos del modelo diurno y estacional de §3.2). Cada parámetro con `unit`, `classification` y `source`/`version`; **cada valor con fuente enlazable (p. ej. NASA NSSDC Mars Fact Sheet, a verificar y citar con fecha) o clasificado como `MODEL_ASSUMPTION`/`GAME_BALANCE`/`SPECULATIVE` y marcado ilustrativo**; ningún valor presentado como medición regional si es media planetaria (§10.3). Tamaño de celda (H-07) como supuesto etiquetado.
**Aceptación:** el cargador de S1-17 acepta el fichero; QA ejecuta la auditoría de S1-30; una lista en el modelo científico de S1-16 enlaza cada valor `SCIENTIFIC_*` a su fuente.

### S1-26 · Escenario MarsBaseline: carga e inicialización — *Motor*
Cargador de escenario y `MarsBaseline` inicializan temperatura, presión, radiación y recursos de las 4.096 celdas **solo desde el JSON** (ningún valor numérico de modelo en C#).
**Aceptación:** test: tras cargar, las 4.096 celdas tienen los valores del fichero y el validador reporta 0 violaciones; borrar un parámetro obligatorio del JSON hace fallar la carga con mensaje claro.

### S1-27 · EnvironmentSystem v0 — *Motor*
Sistema de ambiente mínimo. La v0 usa `T(x,y,t) = T_base(x,y) + D(t) + ε` y omite `S(t)` de la fórmula de §3.2. ε es opcional y, si se usa, sale del flujo Environment. Parámetros del escenario; sin difusión (H-07/H-08).
**Aceptación:** tests: con amplitud y σ = 0 el estado permanece constante tras 100 ticks; con σ > 0 y la misma semilla el resultado es idéntico bit a bit; el validador no reporta violaciones.

### S1-28 · SimulationSnapshot (vista) y estadísticas — *Motor*
`CellSnapshot`, `PopulationSnapshot`, `SimulationSnapshot` de §8.1 en `Martis.Contracts`, `CreateSnapshot()` y `SimulationStatistics` mínima (H-03).
**Aceptación:** test: snapshot de un mundo 64×64 tiene 4.096 `CellSnapshot` y `Tick` coherente; el snapshot es inmutable (no refleja cambios posteriores del estado).

### S1-29 · Integración: 100 ticks de MarsBaseline — *QA*
Test de integración (Día 4 del TDD): cargar escenario, 100 ticks, validar, snapshot.
**Aceptación:** `dotnet test --filter Category=Integration` pasa; el test falla si se inyecta una temperatura `NaN` y el fallo menciona VAL, tick y celda.

### S1-30 · Auditoría de parámetros (ADR-000 R5) — *QA*
Test automático sobre `data/**/*.json`: todo parámetro tiene `classification` válida; todo `SCIENTIFIC_*` tiene `source` con URL/DOI; ningún `SCIENTIFIC_*` con `source: null`.
**Aceptación:** `dotnet test --filter Category=ParameterAudit` pasa y falla con un JSON de prueba que viola cada regla.

---

## Día 5 — Validación

### S1-31 · Snapshot completo de estado, serialización y restauración — *Motor*
`StateSnapshot` completo (estado, poblaciones, genomas, RNG, versión de motor, algoritmo PRNG; H-03/H-04) y serialización JSON canónica (orden estable, cultura invariante, ida y vuelta exacta de `double`) para `final_snapshot.json`; `Restore` restaura estado **y** RNG.
**Aceptación:** test: serializar → deserializar → serializar produce bytes idénticos; `Restore(CreateSnapshot())` ⇒ estado idéntico (VAL-009); continuar 100 ticks desde el restaurado == continuar desde el original (VAL-010).

### S1-32 · CLI: flujo completo de §19 — *Motor*
`simulate` ejecuta: cargar `mars_baseline` → inicializar 64×64 → ambiente → RNG → 1000 ticks → validar → snapshot → guardar resultados (`metadata.json` con semilla, ticks, escenario, versión de motor/datos/SDK, algoritmo PRNG; `final_snapshot.json`; `resource_history.csv` cada N ticks; `population_history.csv` con cabecera; `simulation.log`) → exit 0. Si el validador falla: exit 1 y log con semilla, tick, celda, sistema.
**Aceptación:** `dotnet run --project src/Martis.Cli -- simulate --scenario mars_baseline --seed 12345 --ticks 1000 --output results/experiment_001; echo $?` imprime `0` y existen los 5 ficheros; `final_snapshot.json` contiene `"tick": 1000` (o el campo equivalente definido); un test con estado corrupto inyectado devuelve exit 1.

### S1-33 · Reproducibilidad de extremo a extremo — *QA*
Ejecutar el CLI dos veces con la misma semilla y comparar la salida; con otra semilla (escenario de prueba con σ > 0 en el ambiente) comprobar que difiere.
**Aceptación:** `sha256sum results/a/* results/b/*` coincide fichero a fichero para la misma semilla (misma plataforma y runtime, §6); test automatizado equivalente en `Martis.Integration.Tests`; el PR documenta explícitamente la limitación entre plataformas.

### S1-34 · Matriz de invariantes VAL-001..010 — *QA*
El registro de invariantes de esta tarea, en `docs/`: por cada VAL, estado (implementado/probado/pendiente), test que lo cubre y por qué no aplica aún (VAL-006/007/008 hasta T04).
**Aceptación:** las 10 filas existen; los enlaces a test son nombres reales ejecutables; VAL-001..005, 009, 010 marcados como cubiertos solo si su test está en verde.

### S1-35 · Benchmark inicial (menor prioridad) — *Motor*
`benchmarks/Martis.Benchmarks` con BenchmarkDotNet (paquete y versión consultados en NuGet) para `FullSimulation` de 1000 ticks y `SnapshotCreation`.
**Aceptación:** `dotnet run -c Release --project benchmarks/Martis.Benchmarks` genera el informe; el informe de esta tarea, en `docs/`, registra hardware, SO, runtime y los números **medidos** sin tratarlos aún como el objetivo de §7.4 (el modelo actual no es el modelo completo, H-20).

### S1-36 · Cierre del sprint — *QA*
Recorrer el checklist 18.1 y la definición de terminado §19 sobre un **clon limpio**.
**Aceptación:** `git clone <repo> && cd <repo> && dotnet build MARTIS.sln -c Release && dotnet test MARTIS.sln` exit 0, luego el comando de S1-32 exit 0; tabla de checklist 18.1 completada con enlaces a PR; lista de huecos nuevos añadida a `TDD_HUECOS.md`.

---

## Checklist de aceptación 18.1 → tareas

| # | Ítem del TDD (§18.1) | Tareas | Evidencia de cierre |
|---|---|---|---|
| 1 | Crear repositorio MARTIS — Git inicial y README | S1-01 | `git ls-files`, README con comandos |
| 2 | Configurar Godot .NET y SDK — editor y compilación C# funcionales | S1-02 (SDK), S1-07 (Godot), S1-08 (TFM) | `dotnet --version`; nota de puesta en marcha de S1-07; `dotnet build src/Martis.Game` (S1-07, paralela) |
| 3 | Crear MARTIS.sln — solución con proyectos independientes | S1-03, S1-05, S1-06 | `dotnet build MARTIS.sln`; test de arquitectura |
| 4 | Implementar Martis.Domain — entidades básicas y validaciones | S1-09, S1-10, S1-11, S1-12, S1-14, S1-15, S1-16, S1-17 | tests de dominio e invariantes |
| 5 | Implementar SimulationState — mundo 64×64 | S1-13 | test 4.096 celdas |
| 6 | Implementar SimulationEngine — avance mediante ticks | S1-18, S1-22, S1-23, S1-24 | test de ticks y determinismo; CLI avanza ticks |
| 7 | Implementar PRNG — misma semilla, misma secuencia | S1-19, S1-20, S1-21 | vectores de referencia; test de flujos |
| 8 | Implementar escenario MarsBaseline — configuración ambiental inicial | S1-25, S1-26, S1-27, S1-29, S1-30 | `mars_baseline.json` auditado; test de 100 ticks |
| 9 | Crear SimulationSnapshot — captura del estado | S1-28, S1-31 | round-trip VAL-009 |
| 10 | Crear pruebas automatizadas — build y tests satisfactorios | S1-05, S1-15, S1-21, S1-24, S1-29, S1-30, S1-33, S1-34, S1-36 (+ S1-06 CI) | `dotnet test MARTIS.sln` exit 0 |

## Mapa de días del TDD → tareas

| Día (§18) | Tareas del TDD | Tareas |
|---|---|---|
| 1 — Infraestructura | Crear Git; Godot .NET y SDK; sln; proyectos; referencias; README; .gitignore; compilación | S1-01..S1-08 |
| 2 — Dominio | IDs; EnvironmentState; ResourcePool; Population; Genome; SimulationState; validaciones | S1-09..S1-17 |
| 3 — Motor | SimulationConfiguration; Context; ISimulationSystem; ISimulationEngine; SimulationEngine; ticks; PRNG | S1-18..S1-24 |
| 4 — Entorno | Región 64×64; temperatura, presión, radiación y recursos; **100 ticks** (el prototipo exige 1000 en Día 5, H-16); snapshot | S1-25..S1-30 |
| 5 — Validación | Tests; invariantes; reproducibilidad; serialización; CLI básico; benchmark inicial | S1-31..S1-36 |

## Cobertura de invariantes en el sprint

| VAL | Tarea que lo cubre | Nota |
|---|---|---|
| 001, 002, 003, 005 | S1-10, S1-11, S1-12, S1-14, S1-15 | incluye NaN/∞ |
| 004 | S1-19, S1-21, S1-18 | probabilidades en [0,1] |
| 006, 007, 008 | — | requieren metabolismo (T04); estado «pendiente» en S1-34 |
| 009 | S1-31 | round-trip |
| 010 | S1-24, S1-33 | misma plataforma/runtime |
