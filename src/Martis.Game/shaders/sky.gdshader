shader_type sky;

// Martian sky: butterscotch by day, blue halo around a low sun (Mars sunsets are blue),
// stars and a dim dusty horizon by night. LIGHT0 is the sun.
uniform float night = 0.0;
uniform vec3 day_top : source_color = vec3(0.36, 0.24, 0.17);
uniform vec3 day_horizon : source_color = vec3(0.86, 0.62, 0.43);
uniform vec3 halo : source_color = vec3(0.45, 0.62, 0.95);
uniform vec3 night_top : source_color = vec3(0.01, 0.012, 0.03);
uniform vec3 night_horizon : source_color = vec3(0.09, 0.06, 0.07);
uniform vec3 ground : source_color = vec3(0.18, 0.1, 0.07);

float hash(vec3 p) {
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

void sky() {
    vec3 dir = normalize(EYEDIR);
    float h = dir.y;
    vec3 sun_dir = LIGHT0_ENABLED ? LIGHT0_DIRECTION : vec3(0.0, 1.0, 0.0);
    float sd = dot(dir, sun_dir);
    float sun_height = sun_dir.y;

    float up = pow(clamp(h, 0.0, 1.0), 0.45);
    vec3 day = mix(day_horizon, day_top, up);
    float low_sun = 1.0 - smoothstep(0.05, 0.5, sun_height);
    day += halo * exp(-(1.0 - sd) * 14.0) * (0.35 + 1.4 * low_sun);
    day += vec3(1.0, 0.9, 0.75) * exp(-(1.0 - sd) * 300.0) * 0.8;
    float disk = smoothstep(0.99955, 0.9998, sd);
    day += vec3(1.6, 1.5, 1.35) * disk * 6.0;

    vec3 nightc = mix(night_horizon, night_top, up);
    vec3 cell = floor(dir * 420.0);
    float star = step(0.9965, hash(cell)) * smoothstep(0.0, 0.25, h);
    float twinkle = 0.6 + 0.4 * sin(TIME * 3.0 + hash(cell + 3.0) * 40.0);
    nightc += vec3(0.9, 0.92, 1.0) * star * twinkle * 1.8;
    // Two small moons drifting: Phobos and Deimos as faint dots.
    vec3 phobos = normalize(vec3(cos(TIME * 0.02), 0.45, sin(TIME * 0.02)));
    nightc += vec3(0.8, 0.75, 0.7) * smoothstep(0.99985, 0.9999, dot(dir, phobos)) * 2.0;

    vec3 col = mix(day, nightc, night);
    col = mix(ground * (1.0 - night * 0.8), col, smoothstep(-0.08, 0.02, h));
    COLOR = col;
}
