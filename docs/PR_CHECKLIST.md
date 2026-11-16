# Checklist de revisión de QA para cada PR

Uso: MARTIS QA copia esta lista en la revisión de **cada** PR y marca cada punto como ✅, ❌ o N/A (con motivo). Un PR con algún ❌ no se fusiona. Referencias: ADR-000 (reglas), TDD v1.0 §10 (VAL-001..010), §13 (pruebas).

**PR:** #___  **Tarea:** S1-__  **Autor/agente:** ___  **Revisor QA:** ___  **Fecha:** ___

## 0. Alcance y trazabilidad
- [ ] El PR corresponde a **una** tarea (ID `S1-nn` o issue) y su criterio de aceptación está en la descripción, con el comando ejecutado y su salida.
- [ ] Tamaño revisable (una sola preocupación); sin cambios ajenos a la tarea.
- [ ] Si toca una decisión DT/ADR, enlaza el ADR y no contradice su estado; si lo cambia, incluye ADR nuevo o actualización.

## 1. Reglas de arquitectura (ADR-000 R1–R2, R6)
- [ ] `Martis.Domain` sin `using Godot*`, sin `Microsoft.Data.Sqlite`/SQLite, sin `PackageReference`.
- [ ] Grafo de compilación, sin ciclos (el test `Category=Architecture` está en verde): Domain sin dependencias de proyecto; Contracts → Domain; Simulation → Domain y Contracts; Persistence → Domain y Contracts; Cli → Simulation y Persistence. `Martis.Game` no está en `MARTIS.sln`.
- [ ] Contracts no declara `Apply`. El test de arquitectura falla si aparece. Los comandos son datos; el handler que los aplica vive en Simulation (ADR-000).
- [ ] El motor (`Simulation`) no conoce escenas, nodos ni controles de Godot; Godot solo consume comandos, snapshots inmutables y eventos (nunca estado mutable).
- [ ] Sin individuos como objetos de simulación (solo poblaciones agregadas).
- [ ] Nada de acceso a disco/SQLite/red desde `Domain` ni lógica de dominio dentro de `Persistence`/`Game`.

## 2. Sin recursos gratis (ADR-000 R3)
- [ ] Describe de dónde sale la materia y la energía de cada nueva cantidad (biomasa, oxígeno, hijos…).
- [ ] Ningún consumo supera lo disponible (VAL-006); energía consumida ≤ disponible (VAL-008).
- [ ] Reacciones balanceadas por elemento (`A·S = 0`, VAL-007) con test; tolerancia `ε_abs + ε_rel·|M|` justificada.
- [ ] Recursos compartidos mediante buffers de asignación por celda; sin ventaja por orden de iteración.
- [ ] Capacidad de carga `K` no crea recursos.
- [ ] Cantidades físicas ≥ 0 y **finitas** (se comprueba `NaN` y `±∞`, no solo `< 0`).

## 3. Tests
- [ ] `dotnet build MARTIS.sln -c Release` y `dotnet test MARTIS.sln` exit 0 (adjuntar salida o enlace a CI).
- [ ] Hay tests nuevos para cada comportamiento nuevo; cada test **ejecuta código de `src/`** (no tautologías tipo `Math.Clamp` sobre literales; H-11).
- [ ] Casos de borde: 0, 1, negativos, `NaN`, `±∞`, vacío, tamaño máximo.
- [ ] Los fallos esperados son excepciones o violaciones **concretas** (mensaje con VAL-id, tick, celda/población cuando aplique).
- [ ] Invariantes VAL-001..010 afectados por el PR: tests actualizados. Si S1-34 ya añadió el registro de invariantes a `docs/`, ese registro también se actualiza.
- [ ] Sin tests dependientes de la hora, del orden de ejecución, de la cultura ni de ficheros fuera del repo.
- [ ] Tests de regresión con semilla y versión fijadas cuando se toca un sistema; si se cambia un resultado de referencia, el PR lo justifica explícitamente.
- [ ] Si el PR toca genoma, consumo, snapshot, abundancia o reacciones, hay tests de propiedad según §13 (genomas válidos, consumo limitado, snapshot recuperable, abundancia no negativa, reacciones elementales balanceadas). Si aún no hay herramienta de propiedades, hay ejemplos con semilla fija que cubren el mismo invariante.
- [ ] Si el PR toca un sistema o cita cifras de rendimiento, hay un benchmark BenchmarkDotNet (§13) con hardware, runtime, media, memoria asignada y GC. Las cifras de §7.4 (4.096 celdas, 10.000 poblaciones, 3 organismos, como máximo 32 variantes, 1.000 ticks en menos de 10 s, snapshot en menos de 1 s, memoria de simulación menor de 256 MB) van rotuladas como objetivo. No se optimiza sin esa medición.

## 4. Determinismo y reproducibilidad (ADR-000 R4)
- [ ] Sin `System.Random`, sin `GetHashCode()` para semillas/órdenes, sin `Guid.NewGuid()`, `DateTime.Now`/`UtcNow`, variables de entorno ni cultura en el estado.
- [ ] Cada fuente aleatoria usa el **flujo RNG** que le corresponde (Environment, Mutation, Reproduction, GeneticDrift, Events); no se añade consumo a un flujo ajeno.
- [ ] Sin `Parallel.*`, `AsParallel`, `Task.Run`, `Thread` en simulación (ADR-007).
- [ ] Colecciones con orden estable donde importa (no iterar `Dictionary`/`HashSet` sin ordenar; recorrido de celdas por índice ascendente).
- [ ] Orden de operaciones en reducciones de coma flotante fijo y documentado.
- [ ] Estado RNG, versión de motor, de datos y de algoritmo PRNG guardados donde corresponda (snapshot/`metadata.json`).
- [ ] Test de «misma semilla ⇒ mismo resultado» actualizado o añadido (VAL-010); se declara que la garantía es por plataforma/runtime (§6).
- [ ] Serialización con cultura invariante y `double` de ida y vuelta exacta.

## 5. Sin datos científicos inventados (ADR-000 R5)
- [ ] Ningún número nuevo del modelo (en código, JSON, tests, docs o comentarios) sin origen: o tiene fuente (URL/DOI, versión, fecha, página) o está clasificado como `MODEL_ASSUMPTION`, `GAME_BALANCE` o `SPECULATIVE` y marcado como ilustrativo.
- [ ] Los números de ejemplo del TDD (genoma A01, 10.000 → 9.900 + 100, 0,0001, 32…) no se usan como si fueran datos científicos.
- [ ] Una media planetaria no se presenta como medición regional.
- [ ] Los objetivos de rendimiento (§7.4) no se citan como resultados; los benchmarks adjuntan hardware, SO, runtime y configuración.
- [ ] Texto del PR/documentación distingue **ciencia establecida**, **supuestos de modelo** y **valores de balance**; la terraformación global no se presenta como viable con tecnología conocida.
- [ ] Las referencias nuevas están completas (el caso Jakosky & Edwards 2018 sigue pendiente de identificación bibliográfica, H-24).

## 6. Parámetros etiquetados
- [ ] Cada parámetro nuevo vive en `data/` (no en C#) con `id`, `value`, `unit` (con unidad temporal explícita para tasas), `classification`, `source`, `version`.
- [ ] `classification` ∈ {`SCIENTIFIC_MEASURED`, `SCIENTIFIC_ESTIMATED`, `MODEL_ASSUMPTION`, `GAME_BALANCE`, `SPECULATIVE`}.
- [ ] `SCIENTIFIC_MEASURED`/`SCIENTIFIC_ESTIMATED` ⇒ `source` obligatoria y comprobable; si no se puede comprobar, **no** se usa esa etiqueta.
- [ ] Probabilidades en [0,1] (VAL-004); genes en [0,1] (VAL-005).
- [ ] El test `Category=ParameterAudit` está en verde.

## 7. Calidad de código y datos
- [ ] Sin advertencias nuevas (`TreatWarningsAsErrors`); nullable respetado.
- [ ] Nombres con unidades donde aplica (`Kelvin`, `Pascal`, `Kg`, `J`, `Sols`); tipos fuertes para IDs.
- [ ] Sin dependencias NuGet nuevas sin ADR o justificación y sin versión fijada en `Directory.Packages.props`.
- [ ] Versiones de Godot/.NET/paquetes verificadas contra fuente oficial, o marcadas «pendiente de verificar»; nunca inventadas.
- [ ] Cambios de formato (snapshot, JSON, SQL) llevan versión de esquema y prueba de lectura/migración.
- [ ] Errores relevantes registran semilla, tick, celda, población, sistema y parámetros (§11).

## 8. Documentación
- [ ] README/docs actualizados si cambian comandos, estructura o decisiones.
- [ ] Huecos nuevos del TDD añadidos a `docs/TDD_HUECOS.md`.
- [ ] Mensajes de commit y descripción de PR en español, con el ID de tarea.

## Resultado
- [ ] **Aprobado** · [ ] **Cambios solicitados** (lista numerada) · [ ] **Bloqueado** (motivo y decisión necesaria de Dani)
