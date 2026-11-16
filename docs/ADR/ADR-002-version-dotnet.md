# ADR-002 — DT-002: Versión de .NET (SDK y TFM)

- **Estado:** Aceptado (Dani, 2026-11-16)
- **Decisión TDD:** DT-002 «Versión .NET»; recomendación inicial: «Fijar según Godot».
- **Fecha de verificación:** 2026-11-16

## Contexto

El TDD (§1.1) pide fijar el SDK en `global.json` y «fijar según Godot». Hoy ambas cosas tiran en direcciones distintas: Godot 4.7.x exige como mínimo `net8.0` (ADR-001), pero .NET 8 y .NET 9 salieron de soporte el 10-nov-2026.

### Hechos verificados (Microsoft, 2026-11-16)

| Versión | Tipo | Estado | Última | Fin de soporte |
|---|---|---|---|---|
| .NET 11.0 | STS | Active | 11.0.0 (10-nov-2026) | 14-nov-2028 |
| **.NET 10.0** | **LTS** | Active | **10.0.15** (10-nov-2026) | 14-nov-2028 |
| .NET 9.0 | — | Fuera de soporte | 9.0.23 | 10-nov-2026 |
| .NET 8.0 | — | Fuera de soporte | 8.0.34 | 10-nov-2026 |

Fuente: <https://dotnet.microsoft.com/en-us/download/dotnet>.

- SDK más reciente de .NET 10 publicado: **10.0.404** (incluye runtime 10.0.15; C# 14), con una banda paralela 10.0.115. Fuente: <https://dotnet.microsoft.com/en-us/download/dotnet/10.0>.
- Godot: requisitos y TFM según ADR-001 (docs C# basics: «.NET 8 or later»; fuente 4.7.2: mínimo `net8.0`; rama 4.8: mínimo `net10.0`).

## Opciones

- **A. SDK 10 (LTS) + libs/CLI en `net10.0`; Game decide en T08.** El prototipo y el motor nacen en un runtime con soporte hasta nov-2028. Si en T08 hay que usar Godot 4.7.x, las librerías referenciadas por Game requerirán compilar a `net8.0`.
- **B. SDK 10 + multi-target `net8.0;net10.0` en Domain/Simulation/Contracts/Persistence.** Compatible con Godot 4.7.x y 4.8, a costa de complejidad de build y de dos runtimes a probar (riesgo para determinismo, §6).
- **C. Todo en `net8.0`.** Coherente con Godot 4.7.x, pero usa un runtime ya sin soporte de seguridad. No recomendado.
- **D. .NET 11 (STS).** Es la «última», pero no hay evidencia verificada de que Godot lo soporte; no recomendado.

## Decisión recomendada

**Opción A, decidida.** `global.json` en `main` (`62d5257`) fija el SDK **10.0.404**, `rollForward` en **latestPatch** y `allowPrerelease` en **false** (banda 4xx, no la 1xx). `Martis.*` (excepto Game) usa `<TargetFramework>net10.0</TargetFramework>` en `Directory.Build.props`. El TFM del proyecto Game sigue pendiente del spike S1-08; si en T08 hace falta Godot 4.7.x, se reabre la opción B.

## Consecuencias

- Determinismo y resultados de referencia se fijan sobre un único runtime (.NET 10) y un SO/arquitectura concretos; cambiar de runtime exige regenerar y revisar los resultados de referencia (§6, §9.3).
- Versiones de paquetes (`Microsoft.Data.Sqlite`, xUnit, BenchmarkDotNet) **no verificadas**: se fijan en `Directory.Packages.props` consultando NuGet el día de uso; no hay números en este ADR.

## Pendiente de verificar

- Versiones de `Microsoft.Data.Sqlite`, xUnit y BenchmarkDotNet compatibles con .NET 10.
- Que Godot 4.7.2 pueda compilar y referenciar bibliotecas `net10.0` (S1-08). El SDK, `rollForward` y `allowPrerelease` ya están fijados arriba.

## Cómo se verifica

- `dotnet --version` en CI y en local coincide con `global.json`.
- `dotnet build MARTIS.sln -c Release` y `dotnet test` con exit 0 (S1-03).
- Test de arquitectura/props: todos los proyectos `src/` (excepto Game) tienen el mismo TFM.
- El runtime y la versión del SDK se escriben en `metadata.json` de cada ejecución del CLI.
