shader_type spatial;

// Procedural Mars for the planetary view (V1): rust plains, dark basalt, bright highlands
// and polar caps from fbm noise on the sphere. Illustrative, not a map of real albedo.

float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
                   mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
               mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
                   mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 6; i++) {
        v += a * noise(p);
        p *= 2.03;
        a *= 0.5;
    }
    return v;
}

varying vec3 local_pos;

void vertex() {
    local_pos = VERTEX;
}

void fragment() {
    vec3 p = normalize(local_pos);
    float n = fbm(p * 3.0 + 4.0);
    float m = fbm(p * 9.0 + 11.0);
    vec3 rust = vec3(0.72, 0.33, 0.16);
    vec3 dark = vec3(0.32, 0.17, 0.11);
    vec3 bright = vec3(0.86, 0.6, 0.42);
    vec3 col = mix(dark, rust, smoothstep(0.35, 0.6, n));
    col = mix(col, bright, smoothstep(0.62, 0.8, n + m * 0.2));
    float cap = smoothstep(0.86, 0.93, abs(p.y) + (m - 0.5) * 0.08);
    col = mix(col, vec3(0.95, 0.94, 0.92), cap);
    ALBEDO = col;
    ROUGHNESS = 0.95;
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    EMISSION = vec3(1.0, 0.55, 0.35) * rim * 0.6;
}
