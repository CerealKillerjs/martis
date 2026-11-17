# ADR-034 — Marte real: relieve de Jezero y PlanetDefinition (M1)

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** Daniel pidió mantener las celdas científicas pero hacer el mundo abierto sobre la superficie real de Marte, y después la de otros planetas. Eligió empezar por una región real abierta alrededor del cráter Jezero e ir ampliando al planeta entero. M1 es el primer paso: datos reales y un `PlanetDefinition` (ARC-003). M2 traerá la escala real con carga del terreno por trozos, y M3 las celdas ancladas al planeta y los lugares de interés de Jezero.

## Decisiones

### Datos públicos, preparados con una herramienta

`tools/planet/build_region.py` lee por HTTP solo la parte necesaria de cada fuente y escribe la región en `data/planets/mars/<región>/`:

| Fuente | Resolución | Licencia | Uso |
| --- | --- | --- | --- |
| Mars HRSC-MOLA Blended DEM Global v2 (USGS Astrogeology) | 200 m | dominio público | relieve base, sin huecos |
| Modelos de terreno CTX controlados (USGS, datos listos para análisis) | ~20 m | CC0-1.0 | detalle mezclado sobre la base donde existe |
| Mosaico CTX de las zonas de exploración humana, Jezero (USGS) | ~5 m | dominio público | brillo del suelo |

Cada franja CTX se corrige con su desfase vertical medio respecto a la base y se funde con bordes suaves.

Por región se guardan tres archivos:

- `height.png`: alturas en PNG de 16 bits, entre el mínimo y el máximo de la región.
- `albedo.jpg`: brillo del suelo.
- `region.json`: rejilla, límites, cobertura CTX, franjas usadas y fuentes.

En el repositorio solo entra la región preparada, de unos pocos MB. Los datos originales se descargan de nuevo con la herramienta.

### PlanetDefinition

`data/planets/mars.v1.json` es un catálogo de parámetros:

- El radio es el de la esfera Mars 2000 de los datos.
- La gravedad y la duración del sol son valores de referencia.
- Las regiones aparecen con su tamaño de celda. En Jezero, 375 m por celda dan 256 celdas en 96 km.

En `Martis.Simulation.Planets`:

- `PlanetDefinition` y `RegionDefinition` cargan esos datos.
- `RegionRelief` lee las alturas con un lector propio de PNG de 16 bits, porque el motor no depende de Godot. Da la altura en metros en cualquier punto local, la altura media de una celda y la latitud y longitud de cada punto.

### En el juego (paso intermedio)

- El terreno del mapa muestra Jezero real, escalado al tamaño actual del mapa con una exageración vertical de 2,5 para que el relieve se lea.
- El mosaico CTX da el brillo del suelo, con la paleta marciana del shader.
- `--procedural` vuelve al terreno inventado. Si faltan los datos, el juego también vuelve a él.
- La simulación todavía no usa la altura real. Eso llega en M3, con las celdas ancladas.

## Consecuencias

- El mundo ya es Marte real y la geometría de los datos es la del juego. M2 sustituye el escalado por metros reales y carga por trozos sin cambiar las fuentes.
- Otros planetas se añaden con otro `PlanetDefinition` y otras fuentes (por ejemplo LOLA para la Luna) usando la misma herramienta.
- Hasta M2, la exageración vertical y la escala reducida son una aproximación visual, no la escala real.
