# ADR-008 — DT-008: Método de difusión

- **Estado:** Propuesto — pendiente de visto bueno de Dani.
- **Decisión TDD:** DT-008 «Difusión»; recomendación inicial: «Diferencias finitas».

## Contexto

§7.3: difusión discreta `C_i^(t+1) = C_i^t + D·Δt·Σ_{j∈N(i)}(C_j^t − C_i^t)`. Para rejilla uniforme y esquema explícito de cuatro vecinos, condición **orientativa** de estabilidad `D·Δt/Δx² ≤ 1/4`; si no se cumple, subpasos u otro integrador. En el MVP puede limitarse a agua superficial conceptual y algunos nutrientes. Pendientes científicos (§10.3): «Difusión de gases — modelo físico y condiciones locales».

Huecos propios de esta decisión: no se define el tamaño de celda Δx (H-07), ni las condiciones de contorno (borde cerrado, periódico…) (H-08), y Δt = 1 sol (~88.775 s) puede ser grande frente a la escala de difusión real.

## Opciones

1. **Diferencias finitas explícitas, 4 vecinos**, con comprobación de estabilidad y subpasos.
2. Esquema implícito. Más estable, más complejo; no necesario en MVP.
3. Sin difusión en el MVP (solo procesos por celda). Reduce alcance, pero pierde distribución espacial (ARC-004).

## Decisión recomendada

Opción 1, aplicada solo a las variables que el hito T03 incluya (agua superficial conceptual y nutrientes seleccionados). Formulación **en flujos simétricos entre celdas vecinas** para conservar la cantidad total (ADR-000 R3). Si `D·Δt/Δx² > 1/4` el motor usa subpasos (o falla de forma explícita), nunca avanza silenciosamente. Condiciones de contorno propuestas: borde cerrado (sin flujo) hasta decisión de Dani. `D`, `Δx` y su clasificación son parámetros etiquetados; mientras no haya fuente científica son `MODEL_ASSUMPTION` o `GAME_BALANCE`, nunca `SCIENTIFIC_*`.

## Consecuencias

- Sin Δx definido no se puede calibrar `D` físicamente (bloquea cierre científico de T03).
- Los subpasos aumentan el coste del tick.
- La estabilidad del esquema explícito es condición necesaria, no garantía de realismo.

## Cómo se verifica

- Test: la suma total de la magnitud difundida se conserva dentro de `ε_abs + ε_rel·|M|` (VAL-007 análogo para campos).
- Test: ninguna celda queda negativa tras la difusión (VAL-001).
- Test: con parámetros que violan `D·Δt/Δx² ≤ 1/4` se activan subpasos (o se lanza error) y el resultado es estable.
- Test: campo uniforme permanece uniforme; un pico se ensancha de forma monótona (comprobación cualitativa; solución analítica de comparación a proponer por MARTIS Ciencia).
- Test de simetría e independencia del orden de celdas (determinismo).
