using System.Globalization;
using Godot;

namespace Martis.Game;

/// <summary>Global balances shown by the planetary view.</summary>
public sealed record PlanetaryData(
    long Sol,
    double BiomassKg,
    int Populations,
    double LivingFraction,
    double MeanTemperatureK,
    double MinTemperatureK,
    double MaxTemperatureK,
    double WaterKg,
    double MeanDoseMSvPerSol,
    double? ResearchPoints,
    int Orders,
    int FailedOrders,
    double? Intelligence,
    double EstimatedFraction,
    double MeasuredFraction,
    int Stations,
    int WorkingStations,
    double RoverWear,
    int DiscoveredPlaces,
    int Places,
    int Heaters = 0,
    int WorkingHeaters = 0,
    double MeanWarmingK = 0,
    int WarmedCells = 0);

/// <summary>A section of the planet as the planetary view draws it (M3, ADR-036).</summary>
public sealed record SectionMark(string Name, double Lat, double Lon, double SizeM, bool Open, string Unlock);

/// <summary>
/// The planetary view (V1, Documento Maestro v2.0 §3.1): a turning globe of Mars with the
/// study region marked, beside the region's global balances. It reads the same state as
/// every other mode; nothing here changes the simulation.
/// </summary>
public partial class PlanetaryPanel : Control
{
    private Label _data = null!;
    private Label _sections = null!;
    private Node3D _globe = null!;
    private Node3D? _sectionMarks;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(new ColorRect { Color = new Color(0.01f, 0.01f, 0.03f, 0.92f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.SetAnchorsPreset(LayoutPreset.LeftWide);
        container.AnchorRight = 0.6f;
        container.OffsetTop = 100;
        container.OffsetBottom = -20;
        AddChild(container);
        var viewport = new SubViewport { TransparentBg = true, Msaa3D = Viewport.Msaa.Msaa4X, OwnWorld3D = true };
        container.AddChild(viewport);
        viewport.AddChild(new Camera3D { Position = new Vector3(0, 0, 3.9f), Fov = 38, Current = true });
        var sun = new DirectionalLight3D { LightEnergy = 0.95f, LightColor = new Color(1f, 0.93f, 0.85f) };
        sun.RotationDegrees = new Vector3(-20, 40, 0);
        viewport.AddChild(sun);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.25f, 0.2f, 0.3f),
                AmbientLightEnergy = 0.15f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        _globe = new Node3D { RotationDegrees = new Vector3(18, 0, 0) };
        viewport.AddChild(_globe);
        _globe.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 128, Rings = 64 },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/globe.gdshader") },
        });
        _globe.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1.035f, Height = 2.07f, RadialSegments = 64, Rings = 32 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.6f, 0.4f, 0.08f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Front,
            },
        });

        // The arrival site in Jezero, 18.4° N, 77.5° E, marked by a beacon.
        var marker = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Black, EmissionEnabled = true, Emission = new Color(0.4f, 1f, 0.7f), EmissionEnergyMultiplier = 6f },
        };
        var lat = Mathf.DegToRad(18.4f);
        var lon = Mathf.DegToRad(77.5f);
        marker.Position = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon)) * 1.01f;
        _globe.AddChild(marker);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.RightWide);
        panel.AnchorLeft = 0.6f;
        panel.OffsetLeft = 10;
        panel.OffsetRight = -24;
        panel.OffsetTop = 110;
        panel.OffsetBottom = -30;
        var style = new StyleBoxFlat { BgColor = new Color(0.05f, 0.06f, 0.09f, 0.85f), BorderColor = new Color(0.45f, 1f, 0.7f, 0.3f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(18);
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);
        var title = new Label { Text = "MARTE · VISTA PLANETARIA" };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", new Color(0.45f, 1f, 0.7f));
        column.AddChild(title);
        var subtitle = new Label { Text = "Región de estudio · balances agregados", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        subtitle.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        column.AddChild(subtitle);
        _data = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _data.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(_data);
        _sections = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _sections.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(_sections);
        var note = new Label
        {
            Text = "Los balances del planeta entero (clima global, atmósfera) llegarán con la fase E. Esc vuelve al modo anterior.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        note.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.45f));
        column.AddChild(note);
    }

    /// <summary>
    /// The planet's sections (M3, ADR-036): open ones glow green on the globe, locked ones are
    /// dim amber outlines, each listed with what opens it.
    /// </summary>
    public void SetSections(System.Collections.Generic.IReadOnlyList<SectionMark> sections, double radiusM)
    {
        _sectionMarks?.QueueFree();
        _sectionMarks = new Node3D { Name = "Sections" };
        _globe.AddChild(_sectionMarks);
        var lines = new System.Collections.Generic.List<string> { "SECCIONES DE MARTE" };
        foreach (var section in sections)
        {
            var lat = Mathf.DegToRad((float)section.Lat);
            var lon = Mathf.DegToRad((float)section.Lon);
            var normal = new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
            var side = (float)(section.SizeM / radiusM); // angular size on a unit globe
            var colour = section.Open ? new Color(0.4f, 1f, 0.7f, 0.55f) : new Color(1f, 0.7f, 0.35f, 0.22f);
            var patch = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(side, side) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = colour,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    EmissionEnabled = section.Open,
                    Emission = new Color(0.4f, 1f, 0.7f),
                    EmissionEnergyMultiplier = 1.5f,
                },
            };
            _sectionMarks.AddChild(patch);
            var east = new Vector3(Mathf.Cos(lon), 0, -Mathf.Sin(lon));
            var north = normal.Cross(east).Normalized();
            patch.Transform = new Transform3D(new Basis(east, normal, -north), normal * 1.012f);
            lines.Add(section.Open
                ? $"● {section.Name} · abierta"
                : $"○ {section.Name} · se abre con {section.Unlock}");
        }

        _sections.Text = string.Join('\n', lines);
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            _globe.RotateY((float)delta * 0.12f);
        }
    }

    public void Show(PlanetaryData data)
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new System.Collections.Generic.List<string>
        {
            string.Create(c, $"Sol {data.Sol}"),
            string.Create(c, $"Biomasa total  {Hud.Mass(data.BiomassKg)}"),
            string.Create(c, $"Poblaciones  {data.Populations}"),
            string.Create(c, $"Superficie con vida  {data.LivingFraction * 100:0.0} %"),
            string.Create(c, $"Temperatura media  {data.MeanTemperatureK:0} K ({data.MeanTemperatureK - 273.15:0} °C)"),
            string.Create(c, $"   mínima {data.MinTemperatureK:0} K · máxima {data.MaxTemperatureK:0} K"),
            string.Create(c, $"Agua accesible  {Hud.Mass(data.WaterKg)}"),
            string.Create(c, $"Dosis media  {data.MeanDoseMSvPerSol:0.00} mSv/sol"),
            string.Create(c, $"Conocimiento  {data.EstimatedFraction * 100:0} % estimado · {data.MeasuredFraction * 100:0.#} % medido"),
            string.Create(c, $"Estaciones  {data.WorkingStations}/{data.Stations} activas · desgaste del rover {data.RoverWear * 100:0} %"),
            string.Create(c, $"Calentamiento local  +{data.MeanWarmingK:0.0} K en {data.WarmedCells} celdas · calefactores {data.WorkingHeaters}/{data.Heaters}"),
            string.Create(c, $"Lugares de interés  {data.DiscoveredPlaces}/{data.Places} descubiertos"),
            string.Create(c, $"Órdenes  {data.Orders} ({data.FailedOrders} fallidas)"),
        };
        if (data.ResearchPoints is { } points)
        {
            lines.Add(string.Create(c, $"Investigación  {points:0} PI"));
        }

        if (data.Intelligence is { } intelligence)
        {
            lines.Add(string.Create(c, $"Índice de inteligencia  {intelligence:0}"));
        }

        _data.Text = string.Join('\n', lines);
    }
}
