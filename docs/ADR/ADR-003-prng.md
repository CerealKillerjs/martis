# ADR-003 — DT-003: Generador de números pseudoaleatorios (PRNG)

- **Estado:** Propuesto — pendiente de visto bueno de Dani.
- **Decisión TDD:** DT-003 «PRNG»; recomendación inicial: PCG32 (§6 admite «PCG32 o PCG64»).

## Contexto

La simulación debe reproducirse con la misma semilla, configuración, orden de comandos y versión de motor (§6, VAL-010). No se reparte `System.Random` por los sistemas. El estado interno del generador se guarda en snapshots (`RandomState(ulong State, ulong Increment)`, §6), lo que encaja con PCG32 (estado de 64 bits + incremento de 64 bits).

Referencia del algoritmo: implementación mínima de PCG32 de M. E. O'Neill (Apache 2.0), publicada en <https://www.pcg-random.org/download.html> (multiplicador `6364136223846793005`, salida XSH RR de 32 bits).

## Opciones

1. **PCG32** (estado 64 bits, 32 bits de salida). Simple, pequeño, `RandomState` ya previsto en el TDD. Para `NextUInt64`/`NextDouble` se combinan dos salidas de 32 bits (regla de combinación a fijar y versionar).
2. **PCG64** (estado 128 bits). Más margen de periodo; requiere `UInt128` o par de `ulong` en `RandomState`, y una implementación propia más larga de validar.
3. `System.Random`. Descartada por el TDD (§6) y por ADR-000 R4.

## Decisión recomendada

**PCG32**, identificador de algoritmo versionado `pcg32-v1` guardado en snapshots y `metadata.json`. Cinco flujos independientes (Environment, Mutation, Reproduction, GeneticDrift, Events). Mecanismo de derivación de cada flujo a partir de `WorldSeed` (distinto incremento por flujo y/o mezcla de bits de la semilla) **documentado y fijado en S1-20**; requisitos: determinista, sin `GetHashCode`, y probado con un test de aislamiento entre flujos. Las reglas de conversión (`NextDouble` en [0,1), `NextInt` sin sesgo de módulo, `NextBool(p)` con `p` en [0,1]) forman parte de `pcg32-v1`; cambiarlas implica `pcg32-v2`.

## Consecuencias

- Cambiar cualquier detalle del algoritmo invalida los resultados de referencia: obliga a subir la versión de algoritmo.
- Un flujo por dominio evita desplazar secuencias ajenas al añadir una llamada aleatoria.
- La uniformidad estadística no se promete aquí; se valida con tests simples (sanity), no con baterías de calidad PRNG.

## Cómo se verifica

- Test con **vectores de referencia** generados con la implementación oficial de PCG (citar versión y fuente en el test; no copiar valores de memoria).
- Misma semilla ⇒ misma secuencia (checklist 18.1); semillas distintas ⇒ secuencias distintas.
- Consumir N valores del flujo A no altera la secuencia del flujo B.
- `RandomState` guardado y restaurado reproduce la continuación exacta (VAL-009/010).
- `NextDouble` ∈ [0,1); `NextInt(min,max)` en rango; `NextBool` con probabilidad fuera de [0,1] lanza excepción (VAL-004).
