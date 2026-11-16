# ADR-014 — Evolución v1: mutación, deriva y variantes (T06)

- **Estado:** Propuesto (pendiente de MARTIS Ciencia y MARTIS Lead), 2026-11-16.
- **Desarrolla:** [ADR-006](ADR-006-variantes-por-celda.md) y [ADR-009](ADR-009-integracion-genetica.md).
- **Fuente:** TDD v1.0 §4.1–4.4.

## Decisiones

### Mutación (`apply-mutations`, flujo Mutation)

Cada población con recién nacidos tiene una probabilidad `1 − exp(−event_rate · fracción de recién nacidos)` de un evento por tick. En un evento cada gen muta con probabilidad μ (`trait_probability`): `g′ = clamp(g + σ·z, 0, 1)`. Si el genoma resultante difiere del padre en más de `merge_tolerance` (diferencia máxima entre genes), funda una variante nueva:

- biomasa = `founder_fraction` · biomasa recién nacida, tomada del padre;
- la misma fracción de la reserva de energía pasa al hijo;
- edad 0; id = máximo + 1.

No se crea masa ni energía. Cada variante nueva es una fila de `mutations.csv` (`tick,cell,organism,parent_id,child_id,child_biomass_kg,changes`, con `changes` = `GEN:antes>después` separados por `;`).

`z` es una normal de Irwin–Hall (suma de 12 uniformes menos 6): media 0, varianza 1, colas cortadas en ±6. Solo usa sumas, así que da los mismos bits en todas las plataformas.

### Selección

La selección ya está en el modelo: el fenotipo de cada variante cambia su crecimiento (T04) y su mortalidad (T05). `pᵢ′ = pᵢwᵢ / Σpⱼwⱼ` es justo lo que resulta del crecimiento diferencial, así que no se aplica una segunda vez.

### Deriva (`apply-selection-and-drift`, flujo GeneticDrift)

En cada grupo (celda, organismo) con dos o más variantes se vuelven a muestrear las fracciones de biomasa con `Nₑ = drift.effective_size` ensayos, por binomiales secuenciales (multinomial). El binomial es exacto hasta 64 ensayos y normal redondeado por encima. La biomasa total del grupo no cambia. Una variante que queda por debajo de un individuo se extingue y su biomasa va a la necromasa.

### Variantes

Después de la deriva, las variantes a menos de `merge_tolerance` se fusionan en la de menor id. Si el grupo sigue por encima de `MaxVariantsPerSpeciesPerCell` (32 por defecto), la variante con menos biomasa (empate: id mayor) se fusiona con la genéticamente más cercana (empate: la primera en la lista) hasta cumplir el tope. La fusión suma biomasa, abundancia y reserva; la edad y la viabilidad se promedian por abundancia. La conservación de variantes raras «con importancia evolutiva» queda para más adelante.

### Versión

El pipeline con biología pasa a `pipeline-v3`.

## Consecuencias

- El id de población es `máximo + 1`, así que se puede reutilizar el id de una población extinta que tenía el máximo. Se corrige en T07 guardando el siguiente id en el snapshot.
- Todos los parámetros son `GAME_BALANCE` y no son tasas moleculares reales (TDD §4.2).
