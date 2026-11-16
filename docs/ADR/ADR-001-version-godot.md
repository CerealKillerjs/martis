# ADR-001 — DT-001: Versión concreta de Godot 4 (.NET)

- **Estado:** Aceptado (Dani, 2026-11-16). Se aprueba la decisión recomendada.
- **Decisión TDD:** DT-001 «Versión concreta de Godot 4»; recomendación inicial: «Última estable compatible».
- **Fecha de verificación:** 2026-11-16

## Contexto

El TDD (§1.1, §17) exige fijar la versión exacta de Godot 4 con soporte .NET antes de empezar. Godot **no es necesario** para el prototipo CLI (§19); solo condiciona `Martis.Game` (T08) y la elección de TFM (ADR-002).

### Hechos verificados (fuentes oficiales, 2026-11-16)

| Hecho | Fuente |
|---|---|
| La última versión **estable** de Godot es **4.7.2-stable (18 de agosto de 2026)**. Le preceden 4.7.1-stable (14-jul-2026), 4.7-stable (18-jun-2026) y 4.6.3-stable (20-may-2026). | <https://godotengine.org/download/archive/> |
| **Godot 4.8 no es estable aún**: el archivo lo lista como «beta3» (9 de noviembre de 2026). No se ha verificado fecha de salida de 4.8 stable. | <https://godotengine.org/download/archive/> |
| 4.7.2 es versión de mantenimiento, con enlace de descarga .NET. El artículo dice que no hay incompatibilidades conocidas con Godot 4.7.1. | <https://godotengine.org/article/maintenance-release-godot-4-7-2/> |
| Los proyectos C# requieren la versión de Godot con .NET **y** el .NET SDK instalado aparte. La documentación estable (4.7) aún dice «Godot 4.5 requires .NET 8 or later» (texto sin actualizar a 4.7). | <https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html> |
| En el código fuente de la etiqueta `4.7.2-stable`, el TFM mínimo generado es `net8.0` (`net9.0` para exportar a Android). | <https://github.com/godotengine/godot/blob/4.7.2-stable/modules/mono/editor/GodotTools/GodotTools.ProjectEditor/ProjectGenerator.cs> |
| En desarrollo (rama que producirá 4.8) se fusionó el 26-sep-2026 «.NET: Upgrade packages and minimum TFM required to `net10.0`». Un revisor avisa de que forzará Visual Studio 2026 para abrir los proyectos C# generados por defecto (VS 2022 solo llega a .NET 9). | <https://github.com/godotengine/godot/pull/123738> |

## Opciones

1. **Godot 4.7.2 .NET** (última estable hoy). TFM `net8.0`. Estable y documentada; su TFM mínimo cae en un runtime fuera de soporte desde el 10-nov-2026 (ver ADR-002).
2. **Esperar a Godot 4.8 stable**. TFM mínimo `net10.0` (alineado con .NET 10 LTS). Hoy es beta; fecha no verificada; no se debe usar beta para fijar versión.
3. **Godot 4.6.3**. Estable pero superada; sin ventaja para MARTIS.

## Decisión recomendada

Opción 1 de forma **provisional**: usar **Godot 4.7.2-stable (.NET)** como versión de referencia para el spike de Día 1 (S1-07) y anotar versión y URL oficial en la nota de puesta en marcha de esa tarea. **La versión definitiva de `Martis.Game` se fija al iniciar T08**, reevaluando si 4.8 ya es estable. Mientras tanto no hay ninguna dependencia de Godot en el camino crítico (CLI, Domain, Simulation).

## Consecuencias

- El prototipo CLI no queda bloqueado por Godot.
- Si T08 arranca con 4.8 stable, el Game tendrá TFM `net10.0` y los proyectos del motor pueden compartirlo; si arranca con 4.7.x, las librerías referenciadas desde Game deben compilar a `net8.0` (ver ADR-002).
- El desarrollo principal es Windows (ADR-010): con 4.8 hará falta Visual Studio 2026 si se usa VS (según el comentario del PR); VS Code/Rider son alternativas documentadas en C# basics.

## Pendiente de verificar

- Fecha de lanzamiento de Godot 4.8 stable.
- Si Godot 4.7.2 acepta un proyecto con `TargetFramework` `net10.0` (la fuente solo confirma `net8.0` mínimo y `net9.0` para Android) → spike S1-08.
- Número exacto de la versión de `Godot.NET.Sdk` correspondiente (NuGet) → registrar en S1-07.

## Cómo se verifica

- La nota de puesta en marcha de S1-07 anota la versión exacta instalada y su origen (URL oficial de descarga).
- Spike S1-07/S1-08: `dotnet build src/Martis.Game` compila con la versión elegida y el editor abre el proyecto.
- Al iniciar T08 y en cada actualización de Godot: revisar de nuevo la página de archivo y las notas de versión.

## Nota de aprobación (2026-11-16)
Godot 4.7.2-stable (.NET). Se reevalúa en T08.
