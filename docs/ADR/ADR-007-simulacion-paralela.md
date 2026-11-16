# ADR-007 — DT-007: Simulación paralela

- **Estado:** Propuesto — pendiente de visto bueno de Dani.
- **Decisión TDD:** DT-007 «Simulación paralela»; recomendación inicial: «No en primera versión».

## Contexto

El determinismo exige evitar paralelismo no determinista; el multithreading se incorporará solo después de demostrar que no altera resultados (§6). Los sectores 16×16 preparan una futura paralelización (§7.2). El TDD también prevé que Godot no se bloquee por la simulación: primero ejecución síncrona, después un *worker*, sin acceder nunca a nodos Godot desde él, con cola de comandos y snapshot inmutable (§8). Riesgo explícito: no implementar «multithreading masivo» ni job systems personalizados al inicio (§15).

## Opciones

1. **Simulación estrictamente secuencial** dentro del tick.
2. Paralelismo por sector con reducción determinista (orden fijo de fusión de resultados).
3. Paralelismo libre (`Parallel.For`, PLINQ). Incompatible con ADR-000 R4.

## Decisión recomendada

Opción 1 en la primera versión. **Aclaración:** ejecutar *toda* la simulación secuencial en un hilo trabajador (para no bloquear la UI de Godot) no es paralelismo de simulación y es compatible con esta decisión; está permitido a partir de T08 siempre que haya una única escritora del estado. Reabrir con un ADR nuevo solo cuando una medición (T10) demuestre un cuello de botella y exista un test que pruebe resultados idénticos con y sin paralelismo.

## Consecuencias

- Resultados reproducibles más fáciles de razonar y depurar.
- El techo de rendimiento queda limitado a un núcleo hasta la reapertura; los objetivos de §7.4 son objetivos, no mediciones, y se revisan con datos.

## Cómo se verifica

- Búsqueda en CI de `Parallel.`, `AsParallel`, `Task.Run`, `Thread` en `src/Martis.Simulation` y `src/Martis.Domain` (con lista blanca justificada en comentarios de PR).
- Test de reproducibilidad (VAL-010) ejecutado dos veces, idéntico bit a bit.
- Si en el futuro se paraleliza: test de equivalencia secuencial vs. paralelo con varias semillas.
