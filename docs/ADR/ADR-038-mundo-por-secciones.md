# ADR-038 — El mundo crece por secciones (M4)

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** El modo historia se juega en una sola partida sobre Marte real (ADR-036). La sección inicial mide 400 km. Daniel quiere que al avanzar se abran más secciones de Marte, de modo que el mundo jugable crezca de verdad.

## Decisiones

### Un mosaico de secciones contiguas

Las secciones forman un mosaico. Cada una tiene su casilla (`tile`): x crece hacia el este e y hacia el sur. Todas comparten la latitud de proyección de la sección inicial (`projection_lat` = 19°), así que sus bordes coinciden en metros locales. Los datos salen con la misma herramienta (`tools/planet/build_region.py`): relieve HRSC-MOLA y brillo del mosaico CTX MC-13.

| Casilla | Sección | Se abre con |
| --- | --- | --- |
| (0, 0) | Isidis occidental | inicio |
| (0, −1) | Nili Fossae norte | Era II |
| (1, 0) | Isidis norte | Era II |
| (0, 1) | Syrtis–Isidis | Era III |
| (−1, 0) | Syrtis Major norte | Era III |

### Cada sección es su propia simulación y su propio guardado

No se agranda la rejilla de una sola simulación: con 256² celdas por sección, cuatro secciones serían más de 260.000 celdas por sol. En su lugar:

- Solo corre la sección en la que está el rover.
- Al cruzar un borde con la sección vecina abierta, se guarda la actual y se abre la vecina. Se carga de su guardado o, si nunca se ha visitado, empieza como Marte sin vida.
- Al entrar, la vecina **se pone al día** con los soles que han pasado, porque el reloj del planeta es compartido. Lo que se sembró allí siguió viviendo mientras el rover no estaba.
- El rover conserva batería y desgaste, y aparece en el borde opuesto.
- Una sección bloqueada dice qué era la abre.

Cada guardado lleva el mapa del mundo: la sección activa, la era alcanzada y el guardado de cada sección (evento `section-world`). Al cargar, se recupera la sección activa y se sabe dónde están las demás.

## Consecuencias

- Ponerse al día cuesta el tiempo de simular los soles pendientes, y es más lento cuanta más vida hay en la sección.
- No hay intercambio entre secciones: la vida no se dispersa a través de los bordes. Eso pediría simular a la vez las franjas de borde.
- La era viene por ahora de `--era=N`, para pruebas. En M5 la dará el modo historia: cada capítulo de la campaña abre su era.
- Cada sección ocupa unos 7–8 MB de datos en el repositorio.
