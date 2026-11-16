# ADR-010 — DT-010: Plataforma inicial

- **Estado:** Aceptado (Dani, 2026-11-16). Se aprueba la decisión recomendada.
- **Decisión TDD:** DT-010 «Plataforma inicial»; recomendación inicial: «Windows y Linux».
- **Fecha de verificación:** 2026-11-16

## Contexto

El TDD (§17, DT-010) recoge: «Godot 4 C# requiere distribución .NET y SDK compatible. La documentación consultada señala que proyectos C# de Godot 4 no pueden exportarse actualmente a Web. Recomendación: Windows como desarrollo principal, Linux para pruebas, escritorio como distribución inicial y no comprometer web todavía.»

### Verificación de la nota sobre Web — **sigue vigente** (docs oficiales 4.7, 2026-11-16)

- *C# basics*: «Projects written in C# using Godot 4 currently cannot be exported to the web platform. To use C# on the web platform, consider Godot 3 instead. Android and iOS platform support is available as of Godot 4.2, but is experimental and some limitations apply.» — <https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html>
- *Exporting for the Web*: «Projects written in C# using Godot 4 currently cannot be exported to the web. […] To use C# on web platforms, use Godot 3 instead.» — <https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html>
- *C# basics*: «As of Godot 4.0, exporting .NET projects is supported for desktop platforms (Linux, Windows and macOS). Other platforms will gain support in future 4.x releases.» (misma URL).

Nota: la documentación «stable» corresponde hoy a Godot 4.7; el aviso de Web no cambió respecto a lo recogido en el TDD. Godot 3 no es opción (el TDD fija Godot 4, §1.1).

## Opciones

1. **Windows (desarrollo principal) + Linux (pruebas/CI), distribución de escritorio; sin compromiso de Web.**
2. Añadir macOS como objetivo (la documentación lo soporta; el TDD no lo menciona).
3. Priorizar Web. No viable con C# en Godot 4 hoy.
4. Móvil (Android/iOS). Soporte experimental según la documentación; fuera de MVP.

## Decisión recomendada

Opción 1. La simulación y el CLI (`Martis.Simulation`, `Martis.Cli`) son multiplataforma por construcción (sin Godot) y se prueban en Linux y Windows; `Martis.Game` se exporta a escritorio. Web/móvil quedan fuera de alcance y se revisan si cambia la documentación oficial. macOS: decisión de Dani (H-12), no asumida.

## Consecuencias

- El CI en Linux ejecuta build+test+CLI sin necesitar Godot.
- Los resultados de referencia de reproducibilidad se declaran por plataforma/runtime (§6); no se promete igualdad entre Windows y Linux sin test que lo demuestre.
- Una versión web jugable no es posible con la arquitectura actual; si se quisiera, requeriría otra decisión (por ejemplo otro front-end) y un nuevo ADR.

## Cómo se verifica

- CI en Linux: `dotnet build` y `dotnet test` con exit 0; CLI de 1000 ticks con exit 0.
- Ejecución periódica en Windows (local o CI) del mismo CLI y comparación del `final_snapshot.json` entre plataformas como **observación documentada**, no como requisito hasta decidirlo.
- Al subir de versión de Godot: releer las dos páginas citadas y actualizar este ADR.
