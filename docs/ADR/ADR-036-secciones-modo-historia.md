# ADR-036 — Secciones de Marte y sección inicial del modo historia (M3)

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** Daniel decidió que la campaña sea el modo historia: lineal, en una sola partida y sobre un único terreno persistente, desde los microbios hasta una civilización. Quiere el mapa lo más grande posible desde el principio y que se desbloqueen más secciones de Marte al avanzar. Eligió empezar con una sección grande de 400 km con celdas de unos 1,5 km. M1 (ADR-034) y M2 (ADR-035) ya traían el relieve real de Jezero y la escala real.

## Decisiones

### El planeta se divide en secciones

`data/planets/mars.v1.json` lista las secciones del planeta. Cada una tiene:

- centro y lado;
- la era que la abre;
- su región de datos, cuando ya está preparada;
- sus insertos de detalle;
- su lugar de llegada;
- su archivo de lugares de interés.

| Sección | Centro | Se abre con |
| --- | --- | --- |
| Isidis occidental (inicial) | 19,0° N, 78,5° E | inicio del modo historia |
| Nili Fossae norte | 25,5° N, 77,0° E | Era II · Ecosistemas |
| Llanura de Isidis | 13,5° N, 86,0° E | Era II · Ecosistemas |
| Syrtis Major | 12,0° N, 71,5° E | Era III · Inteligencia |
| Utopia sur | 30,0° N, 90,0° E | Era IV · Expansión |

La vista planetaria las dibuja sobre el globo: las abiertas en verde y las bloqueadas en ámbar, con lo que las abre.

### La sección inicial: Isidis occidental, 400 km

`tools/planet/build_region.py --region isidis_west` la prepara con dos fuentes:

- **Relieve:** HRSC-MOLA (200 m), con una muestra cada 195 m.
- **Brillo:** el mosaico global CTX MC-13 (100 m, USGS). Sus muestras int16 son en realidad sin signo; las franjas vacías se rellenan con una pirámide de medias.

La sección cubre Jezero, Nili Fossae, el borde de Syrtis Major y el oeste de Isidis. Tiene 256 celdas científicas de 1.562,5 m (`isidis_west.cell`, GAME_BALANCE). Sin `--grid`, el juego usa la sección entera.

### Jezero como inserto de detalle

`RegionRelief.WithInsert` funde una región más detallada sobre otra, situada por su centro en la esfera del planeta. Dentro del inserto manda el detalle, y en un borde de 3 km se mezcla con la base. Jezero (≈47 m por muestra) queda así dentro de la sección. La vista real usa también su imagen CTX de 5 m para el contraste local dentro del cráter.

### Lugares reales y llegada

`data/expedition/isidis_west.v1.json` sitúa los lugares de interés por latitud y longitud aproximadas:

- el delta occidental de Jezero;
- los carbonatos de su margen;
- el cañón de Neretva Vallis;
- las arcillas hidratadas de Nili Fossae;
- un meteorito metálico, marcado como hipotético.

Los textos dicen qué es real y qué es de juego. El rover de una partida nueva llega al lugar de aterrizaje de Perseverance.

## Consecuencias

- Abrir una sección todavía no amplía la simulación. La rejilla científica es de un solo tamaño por partida, así que crecer hacia secciones vecinas pide simular varias regiones a la vez. Eso es el siguiente paso, junto con preparar los datos de esas secciones con la misma herramienta.
- La campaña por capítulos con mapas distintos (PR #28) se adapta después para jugarse como misiones seguidas en esta misma partida.
- Celdas de 1,5 km agregan más terreno por celda que en Jezero (375 m). Los parámetros ecológicos son por celda y no cambian. El tamaño de la celda solo cambia cuánto suelo representa cada una.
