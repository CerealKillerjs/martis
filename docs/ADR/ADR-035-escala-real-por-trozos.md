# ADR-035 — Jezero a escala real, cargado por trozos (M2)

- **Estado:** Propuesto, 2026-11-17.
- **Contexto:** M1 (ADR-034) trajo el relieve real de Jezero, pero lo mostraba reducido sobre el mapa de celdas. Daniel quiere un mundo abierto sobre la superficie real. Eligió que el rover vaya a 20 m/s cuando se conduce y que los trayectos largos se hagan con órdenes G y viaje rápido.

## Decisiones

### Una unidad, un metro

`RealTerrain` (Godot) dibuja la región a escala real. El origen está en el centro de la región, +X apunta al este y +Z al sur. La altura es la del relieve real (una muestra cada ≈47 m), más unos metros de rugosidad procedural por debajo de esa resolución. Así el suelo junto al rover no es liso y no contradice los datos a la escala que estos pueden mostrar.

### Trozos con nivel de detalle

- El suelo se divide en trozos de 512 m. Se mantienen 7 anillos alrededor del rover (unos 7 km de radio).
- El nivel de detalle baja con la distancia: 129 vértices por lado en los dos primeros anillos (4 m entre vértices), luego 65, 33 y 17.
- Se construyen tres trozos por fotograma, siempre los más cercanos primero, y se liberan los que quedan fuera del alcance.
- Una falda de 6 m tapa las grietas entre trozos de distinto detalle.
- Una malla de toda la región, con 375 m entre vértices y 4 m más baja, llena el horizonte. Así el borde del cráter se ve a decenas de kilómetros.

Terrain3D no se puede descargar desde el entorno de desarrollo (las descargas de GitHub están bloqueadas), así que la carga por trozos es código propio. Cambiarla por el addon más adelante solo afecta a `RealTerrain`.

### Vista real al conducir

- Al conducir el rover (exploración), el juego oculta el mapa de celdas y muestra la vista real con cámara de persecución, sol, bruma ajustada a metros y el módulo de aterrizaje. `--procedural` la desactiva.
- Las celdas científicas siguen siendo la simulación: la posición del rover en metros se convierte a celdas de 375 m.
- `Rover.SpeedScale` convierte la velocidad de datos (celdas por segundo) en `jezero.rover_speed` = 20 m/s. Con una orden de movimiento (G) se multiplica por `jezero.travel_factor` = 30. Los dos son GAME_BALANCE en `data/planets/mars.v1.json`.

### Capturas

`--real-shot=archivo.png --at=x,y --look=grados --hour=0..1 [--hud]` coloca el rover en metros de la región, construye todos los trozos y guarda la imagen.

## Consecuencias

- Las estaciones, calefactores, colonias y lugares de interés todavía no aparecen en la vista real. Llegarán con M3, cuando las celdas queden ancladas al planeta y Valle Aurora se mueva a rasgos reales de Jezero.
- El detalle cercano es procedural. Más adelante pueden sustituirlo los DTM de HiRISE (1 m) donde existan.
- Por ahora el coste de memoria es de unos 225 trozos. La geometría se genera en el hilo principal, con un límite por fotograma.
