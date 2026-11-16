# ADR-005 — DT-005: Modelo espacial

- **Estado:** Aceptado (Dani, 2026-11-16). Se aprueba la decisión recomendada.
- **Decisión TDD:** DT-005 «Modelo espacial»; recomendación inicial: «Arrays lineales».

## Contexto

MVP: cuadrícula 2D de 64 × 64 = 4.096 celdas (§7). Índice lineal `index = y·width + x` (§7.1). Almacenamiento principal mediante arrays y estructuras compactas, sin diccionarios complejos para recorrer todas las celdas cada tick. Preparación de sectores de 16 × 16 (16 sectores) sin exigir multithreading (§7.2). El tamaño físico de celda (m por celda) **no está fijado** (§7) y condiciona difusión y modelo ambiental (hueco H-07).

## Opciones

1. **Arrays lineales (índice `y·W + x`)** con acceso por `GridIndex`. Orden de recorrido fijo y determinista.
2. Diccionario/`Dictionary<CellId, Cell>`. Rechazada por rendimiento y por orden de iteración no garantizado.
3. Quadtree/regiones jerárquicas. Excesivo para 64×64; fuera de MVP.

## Decisión recomendada

**Arrays lineales**, recorridos siempre por índice ascendente. Primera versión con arrays de clases por claridad (§12.2); migración a arrays de estructuras compactas (SoA) **solo si lo justifican benchmarks** (T10). Sectores 16×16 como vista derivada del índice, sin lógica de threading. Las dimensiones se leen de `SimulationConfiguration` (no se codifican 64 en la lógica; `GridIndex` de §7.1 usa constantes solo en el ejemplo del TDD).

## Consecuencias

- Rendimiento predecible y recorrido determinista.
- Mapas mayores o dimensiones no cuadradas requieren que `GridIndex` deje de usar constantes.
- La decisión de metros por celda sigue abierta y bloquea la calibración de difusión (ADR-008).

## Cómo se verifica

- Tests de `GridIndex`/índice lineal: ida y vuelta (x,y)↔índice para toda la rejilla; fuera de rango lanza excepción.
- Test: mundo 64×64 tiene exactamente 4.096 celdas y sectores 16×16 cubren cada celda una sola vez.
- Revisión de PR: sin `Dictionary`/`HashSet` en bucles por celda del tick (PR_CHECKLIST).
- Benchmarks de `EnvironmentUpdate` y `FullSimulation` (medidos, no objetivos) antes de cambiar la estructura.
