shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Per-cell data from GameSession.View(), 64×64, linear filtered (ADR-017).
// R = phototroph biomass (0..1, log scale), G = decomposer biomass (0..1, log scale),
// B = hue of the dominant variant, A = value of the selected overlay (0..1).
uniform sampler2D cell_data : filter_linear, repeat_disable;
// R = phototroph halo, G = decomposer halo (log biomass spread for display only).
uniform sampler2D glow_data : filter_linear, repeat_disable;
uniform int overlay_mode = 0;          // 0 none, 1 temperature, 2 water, 3 radiation, 4 biomass
uniform float overlay_strength = 0.92;
uniform float world_size = 128.0;
uniform float night = 0.0;             // 0 day, 1 deep night
uniform vec2 highlight_cell = vec2(-1.0);
uniform float cells = 64.0;

varying vec3 world_pos;
varying vec3 world_normal;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
    return v;
}

// Cellular pattern for microbial mats.
float cellular(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    float d = 1.0;
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 o = vec2(float(x), float(y));
            vec2 r = o + vec2(hash(i + o), hash(i + o + 17.0)) - f;
            d = min(d, dot(r, r));
        }
    }
    return sqrt(d);
}

vec3 hsv2rgb(vec3 c) {
    vec3 p = abs(fract(c.xxx + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * mix(vec3(1.0), clamp(p - 1.0, 0.0, 1.0), c.y);
}

vec3 ramp(int mode, float t) {
    if (mode == 1) { // temperature: deep blue -> amber -> white-hot
        return mix(mix(vec3(0.05, 0.12, 0.45), vec3(0.95, 0.45, 0.1), smoothstep(0.0, 0.6, t)), vec3(1.0, 0.95, 0.8), smoothstep(0.6, 1.0, t));
    }
    if (mode == 2) { // water: dry tan -> cyan
        return mix(vec3(0.30, 0.18, 0.10), vec3(0.04, 0.40, 0.75), t);
    }
    if (mode == 3) { // radiation: violet glow
        return mix(vec3(0.1, 0.0, 0.2), vec3(0.9, 0.2, 1.0), t);
    }
    return mix(vec3(0.02, 0.1, 0.05), vec3(0.3, 1.0, 0.5), t); // biomass
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec2 uv = world_pos.xz / world_size + 0.5;
    vec4 data = texture(cell_data, uv);
    vec4 halo = texture(glow_data, uv);

    // Regolith: rust, ochre and basalt by height, slope and noise.
    float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
    float n = fbm(world_pos.xz * 0.15);
    float fine = fbm(world_pos.xz * 2.5);
    vec3 rust = vec3(0.55, 0.22, 0.10);
    vec3 ochre = vec3(0.70, 0.42, 0.22);
    vec3 basalt = vec3(0.24, 0.15, 0.12);
    vec3 dust = vec3(0.78, 0.55, 0.36);
    vec3 albedo = mix(rust, ochre, smoothstep(0.35, 0.75, n));
    albedo = mix(albedo, basalt, smoothstep(0.25, 0.6, slope));
    albedo = mix(albedo, dust, smoothstep(0.55, 0.9, world_pos.y * 0.12 + n * 0.4) * 0.5);
    albedo *= 0.85 + 0.3 * fine;

    vec3 emission = vec3(0.0);

    // Microbial mats: phototrophs teal-green, decomposers violet, hue from the variant.
    float mat_pattern = smoothstep(0.55, 0.1, cellular(world_pos.xz * 1.6 + vec2(n * 3.0)));
    float photo = max(data.r, halo.r * 0.85);
    float decomp = max(data.g, halo.g * 0.8);
    if (photo + decomp > 0.001) {
        vec3 variant = hsv2rgb(vec3(0.27 + data.b * 0.2, 0.8, 1.0));
        vec3 photo_col = mix(vec3(0.05, 0.85, 0.45), variant, 0.45);
        vec3 decomp_col = vec3(0.7, 0.25, 0.95);
        float patch = smoothstep(0.35, 0.65, fbm(world_pos.xz * 0.6 + 7.0));
        float cover_p = smoothstep(0.05, 0.55, photo) * mix(0.25, 1.0, mat_pattern) * mix(0.55, 1.0, patch);
        float cover_d = smoothstep(0.05, 0.55, decomp) * mix(0.2, 0.8, 1.0 - mat_pattern) * mix(0.5, 1.0, 1.0 - patch);
        albedo = mix(albedo, photo_col * 0.35, cover_p * 0.8);
        albedo = mix(albedo, decomp_col * 0.3, cover_d * 0.6);
        float glow = (0.6 + 0.4 * sin(TIME * 1.5 + n * 12.0)) * (0.35 + 0.65 * night);
        emission += photo_col * cover_p * glow * 1.6 + decomp_col * cover_d * glow * 1.2;
    }

    if (overlay_mode > 0) {
        vec3 o = ramp(overlay_mode, data.a);
        // Cell grid lines make the 64×64 model visible under the overlay.
        vec2 g = abs(fract(uv * cells) - 0.5);
        float line = smoothstep(0.47, 0.5, max(g.x, g.y));
        // Emissive so the data reads the same by day and by night; relief kept by a fixed shade.
        float shade = 0.45 + 0.55 * clamp(dot(world_normal, normalize(vec3(0.4, 1.0, 0.3))), 0.0, 1.0);
        albedo *= 1.0 - overlay_strength;
        emission = mix(emission, o * shade * 0.35, overlay_strength) + vec3(line * 0.05);
    }

    if (highlight_cell.x >= 0.0) {
        vec2 c = floor(uv * cells);
        if (c == highlight_cell) {
            vec2 g = abs(fract(uv * cells) - 0.5);
            float edge = smoothstep(0.38, 0.5, max(g.x, g.y));
            emission += vec3(1.0, 0.85, 0.4) * (edge * 2.0 + 0.15) * (0.7 + 0.3 * sin(TIME * 4.0));
        }
    }

    // Micro relief: wind ripples and pebbles as a normal perturbation.
    vec2 q = world_pos.xz;
    float e = 0.05;
    float r0 = fbm(q * 3.0) + sin(q.x * 6.0 + fbm(q * 0.8) * 6.0) * 0.08;
    float rx = fbm((q + vec2(e, 0.0)) * 3.0) + sin((q.x + e) * 6.0 + fbm((q + vec2(e, 0.0)) * 0.8) * 6.0) * 0.08;
    float rz = fbm((q + vec2(0.0, e)) * 3.0) + sin(q.x * 6.0 + fbm((q + vec2(0.0, e)) * 0.8) * 6.0) * 0.08;
    vec3 bump = normalize(vec3((r0 - rx) / e * 0.25, (r0 - rz) / e * 0.25, 1.0));
    NORMAL_MAP = bump * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    ALBEDO = albedo;
    ROUGHNESS = 0.92 - fine * 0.1;
    SPECULAR = overlay_mode > 0 ? 0.0 : 0.2;
    EMISSION = emission;
}
