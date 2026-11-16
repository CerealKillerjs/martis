# ADR-009 — DT-009: Integración genética (herencia)

- **Estado:** Aceptado (Dani, 2026-11-16). Se aprueba la decisión recomendada.
- **Decisión TDD:** DT-009 «Integración genética»; recomendación inicial: «Herencia clonal».

## Contexto

El genoma es funcional (expresiones en [0,1] por gen), no una cadena de ADN (§2.4). MVP: herencia clonal con mutación; recombinación y transferencia horizontal se dejan para después (§4.1). Mutación: probabilidad μ por rasgo; si ocurre `g' = g + δ`, `δ ~ Normal(0, σ²)`, `g' = clamp(g', 0, 1)`; μ y σ son parámetros de juego, **no** tasas moleculares reales (§4.2). Selección `w_i = s_i·r_i`, `p_i' = p_i·w_i / Σ p_j·w_j` (§4.3). Deriva por muestreo binomial o multinomial secuencial, con tamaño efectivo como parámetro separado de la abundancia.

Observaciones del TDD (huecos): el ejemplo JSON `mutation_rate` usa unidad `per_reproduction_event` mientras §4.2 define μ **por rasgo** (H-09); el test de ejemplo de §13 (`Mutation_MustRemainWithinGeneBounds`) solo prueba `Math.Clamp`, no código de MARTIS (H-11); `clamp` acumula probabilidad en los extremos 0 y 1 (sesgo de modelización a documentar).

## Opciones

1. **Herencia clonal con mutación gaussiana acotada** (MVP).
2. Recombinación sexual/parasexual entre variantes. Fuera de MVP.
3. Transferencia horizontal de genes. Fuera de MVP.

## Decisión recomendada

Opción 1. Parámetros μ, σ y tamaño efectivo etiquetados `GAME_BALANCE` (o `MODEL_ASSUMPTION`), sin presentar valores como tasas biológicas medidas. La unidad de μ se fija explícitamente (por rasgo y por evento reproductivo) y se alinea el ejemplo JSON. Mutación y deriva usan sus propios flujos RNG (ADR-003). Un descendiente nuevo solo existe si hay recursos (ADR-000 R3).

## Consecuencias

- Evolución fácil de razonar y reproducir; modelo sencillo que no representa recombinación real.
- El tope de variantes (ADR-006) interactúa con la mutación: debe haber un orden determinista de creación/fusión.
- Ampliar a recombinación implicará cambiar el contrato del genoma y la serialización (nuevo ADR).

## Cómo se verifica

- Tests sobre el código real (`MutationSystem`/función de mutación), no sobre `Math.Clamp`: todos los genes en [0,1] tras mutar (VAL-005), incluidos valores extremos 0, 1, NaN y δ grandes.
- Con μ=0 el hijo es copia exacta del padre; con μ=1 todos los rasgos se evalúan.
- Misma semilla ⇒ misma lista de variantes y frecuencias (VAL-010); el flujo Mutation no se desplaza si cambia el uso del flujo Events.
- Propiedad: abundancia total tras reproducción ≤ límite impuesto por recursos.
- Test estadístico de sanidad de la distribución de δ con tolerancia explícita (no se afirma significado biológico).
