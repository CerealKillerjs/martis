# ADR-023 — Capítulo 3 «Cooperación»: endosimbiosis y multicelularidad (H5)

- **Estado:** Propuesto, 2026-11-17. Daniel eligió el modelo «fusión en la misma casilla» ese día. Los valores son GAME_BALANCE y están pendientes de revisión con Ciencia.
- **Fuente:** el plan del modo historia ([ADR-019](ADR-019-modo-historia.md)).

## Decisiones

### Dos organismos nuevos (`data/organisms/organisms.v1.json`)

| Organismo | Capacidades | Rasgos |
| --- | --- | --- |
| Eucariota K01 | fotoautotrofía y respiración aerobia | Fija carbono y además respira materia orgánica, lo que devuelve agua y CO₂ a la casilla. Es más grande (1e-13 kg por individuo) y crece más despacio. |
| Multicelular M01 | además, multicelularidad | Es mucho más grande (1e-9 kg), resiste mejor la radiación y crece aún más despacio. |

Sus parámetros parten de los del fotótrofo A01 y del descomponedor D01, y van marcados como organismos sintéticos.

### Transiciones (`data/organisms/transitions.v1.json`, paso `update-ecology`)

| Transición | Socios en la misma casilla | Condición | Probabilidad | Fundador |
| --- | --- | --- | --- | --- |
| Endosimbiosis | descomponedor D01 y fotótrofo A01 | ≥ 0,2 kg cada uno | 3 % por sol | 10 % de cada socio |
| Multicelularidad | eucariota K01 | ≥ 1 kg y biopelícula ≥ 0,80 | 3 % por sol | 20 % de la población |

- **Masa:** la nueva población sale de la biomasa y de la reserva de energía de sus socios. Como todos los organismos comparten la composición de la biomasa, la masa se conserva.
- **Aleatoriedad:** las tiradas usan el flujo Events de ADR-003, que ningún otro paso usaba. Hay una tirada por cada par (casilla, regla) que cumple la condición, en orden de casilla y de regla.
- **Mundos sin transiciones:** el paso no hace nada y no tira, así que la partida es idéntica bit a bit a la de antes. Las transiciones se activan desde el capítulo 3 (`WorldOptions.Transitions`).
- **Presupuesto:** respetan el presupuesto de poblaciones de ADR-021.

### Economía

| Tecnología | Coste | Requiere | Efecto |
| --- | --- | --- | --- |
| Cultivo de eucariotas | 40 PI | Cepas extremófilas | permite desplegar el Eucariota K01 |
| Cultivo multicelular | 70 PI | Cultivo de eucariotas | permite desplegar el Multicelular M01 |

La naturaleza no necesita la tecnología: la fusión ocurre igual sin ella.

### Misiones

| Misión | Objetivos |
| --- | --- |
| 3.1 Simbiosis | juntar un descomponedor con fotótrofos, presenciar la endosimbiosis y mantener 0,5 kg de eucariotas durante 40 soles |
| 3.2 Células complejas | eucariotas en 4 casillas y mantener 5 kg durante 60 soles |
| 3.3 Multicelularidad | diseñar un eucariota con biopelícula ≥ 0,80, presenciar el salto multicelular y mantener 1 kg durante 60 soles |

Cada misión tiene un test que la completa con la investigación que se tiene al terminar el capítulo 2.
