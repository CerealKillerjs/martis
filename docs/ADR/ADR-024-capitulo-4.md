# ADR-024 — Capítulo 4 «Cadenas tróficas»: herbivoría y depredación (H6)

- **Estado:** Propuesto, 2026-11-17. Los valores son GAME_BALANCE y están pendientes de revisión con Ciencia.
- **Fuente:** el plan del modo historia ([ADR-019](ADR-019-modo-historia.md)).

## Decisiones

### Consumidores (`data/organisms/organisms.v1.json`)

| Organismo | Capacidades | Rasgos |
| --- | --- | --- |
| Herbívoro H01 | heterotrofía, respiración aerobia, pastoreo | Microfauna sintética de 5e-10 kg por individuo. También asimila carbono orgánico, como un descomponedor. Pierde poca agua (desecación 0,01). |
| Depredador P01 | heterotrofía, respiración aerobia, depredación | 1e-9 kg por individuo. Crece despacio. |

### Relaciones tróficas (`data/organisms/trophic.v1.json`, paso `update-ecology`)

Cada consumidor come de sus presas en su propia casilla con una respuesta de Holling tipo II:

`comido = mín(f·P′, i·C·P′/(K + P′)·Δt)`, con `P′ = máx(0, P − refugio)`.

| Relación | Presas | i (1/sol) | K (kg) | Eficiencia | Refugio |
| --- | --- | --- | --- | --- | --- |
| Pastoreo | fotótrofo, extremófilo, eucariota, multicelular | 0,30 | 0,5 | 0,30 | 0,3 kg |
| Depredación | herbívoro | 0,15 | 0,3 | 0,25 | 0,05 kg |

- **Reparto:** la pérdida se reparte entre las presas en proporción a su biomasa, y la ganancia entre los consumidores en proporción a la suya.
- **Masa:** lo que no se asimila pasa a biomasa muerta y lo recicla el paso siguiente. La masa de cada casilla se conserva, y la validación de conservación lo comprueba en cada tick.
- **Energía:** cada kilogramo asimilado suma 2·10⁵ J a la reserva y se registra como energía ganada.
- **Aleatoriedad:** el paso es determinista y no tira números aleatorios. Sin relaciones tróficas no hace nada.
- **Refugio:** sin él, los herbívoros se comían toda la presa de su casilla en 30 soles y después morían de hambre. Con él, la presa rebrota y la cadena se sostiene.
- **Orden:** el paso `update-ecology` aplica primero la alimentación y después las transiciones de [ADR-023](ADR-023-capitulo-3.md).

### Dispersión activa: aplazada

Mover biomasa entre casillas rompe la conservación casilla a casilla que valida el motor. Hace falta un registro de flujos entre casillas antes de añadirla. Mientras tanto, el jugador coloca a los consumidores.

### Economía y misiones

- Tecnologías: «Cría de herbívoros» (50 PI, requiere el cultivo de eucariotas) y «Cría de depredadores» (80 PI).
- Misiones en 256×256:

| Misión | Objetivos |
| --- | --- |
| 4.1 Herbívoros | herbívoros en una colonia y mantener 1 kg durante 60 soles |
| 4.2 Red trófica | herbívoros en 3 colonias y que los productores mantengan 0,8 kg durante 80 soles |
| 4.3 Depredadores | depredadores en una casilla y mantener 0,05 kg durante 60 soles |

Cada misión tiene un test que la completa con la investigación del capítulo 3.
