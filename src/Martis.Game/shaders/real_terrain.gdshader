shader_type spatial;
render_mode cull_disabled;

// Real-scale ground of a region (M2, ADR-035): metres, origin at the region centre.
uniform sampler2D albedo_map : filter_linear_mipmap, repeat_disable, hint_default_white;
uniform float region_size = 96000.0;

varying vec3 world_pos;
varying vec3 world_normal;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) {
        v += a * noise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return v;
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    vec2 uv = world_pos.xz / region_size + 0.5;
    float dist = length(world_pos - CAMERA_POSITION_WORLD);

    // Orbital brightness with local contrast (strip seams fade, landforms stay).
    float g = texture(albedo_map, uv).r;
    float g_low = textureLod(albedo_map, uv, 7.0).r;
    float b = clamp(0.5 + (g - g_low) * 2.4 + (g_low - 0.45) * 0.3, 0.0, 1.0);

    vec3 dark = vec3(0.17, 0.08, 0.05);
    vec3 bright = vec3(0.68, 0.38, 0.21);
    vec3 basalt = vec3(0.20, 0.13, 0.11);
    vec3 dust = vec3(0.78, 0.55, 0.36);
    vec3 albedo = mix(dark, bright, smoothstep(0.1, 0.9, b));
    albedo = mix(albedo, basalt, smoothstep(0.42, 0.15, b) * 0.45);

    // Close-up grain the orbital image cannot resolve: pebbles, ripples, steep rock.
    float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
    float grain = fbm(world_pos.xz * 0.9);
    float fine = fbm(world_pos.xz * 6.0);
    float near = 1.0 - smoothstep(60.0, 900.0, dist);
    albedo = mix(albedo, basalt * 0.9, smoothstep(0.25, 0.6, slope) * 0.6);
    albedo = mix(albedo, dust, smoothstep(0.62, 0.85, grain) * 0.25 * near);
    albedo *= mix(1.0, 0.82 + 0.36 * fine, near);

    // Wind ripples as a normal perturbation close to the camera.
    float r0 = sin(world_pos.x * 1.7 + fbm(world_pos.xz * 0.2) * 8.0);
    float rx = sin((world_pos.x + 0.05) * 1.7 + fbm((world_pos.xz + vec2(0.05, 0.0)) * 0.2) * 8.0);
    vec3 bump = normalize(vec3((r0 - rx) / 0.05 * 0.04 * near, 0.0, 1.0));
    NORMAL_MAP = bump * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.0;

    ALBEDO = albedo;
    ROUGHNESS = 0.95;
    SPECULAR = 0.15;
}
