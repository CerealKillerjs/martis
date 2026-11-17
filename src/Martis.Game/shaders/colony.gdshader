shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

// Bioluminescent colony domes. Per-instance colour from the MultiMesh: rgb = colour,
// a = vigour (0..1). Fresnel rim + slow breathing pulse; brighter at night.
// INSTANCE_CUSTOM.x is the World3D clock when the colony of the cell appeared: new domes
// grow out of the ground with a small overshoot and a flash of light.
uniform float night = 0.0;
uniform float clock = 0.0;

varying float vigour;
varying float phase;
varying float fresh;

float ease_out_back(float t) {
    float c1 = 1.70158;
    float u = t - 1.0;
    return 1.0 + (c1 + 1.0) * u * u * u + c1 * u * u;
}

void vertex() {
    vigour = COLOR.a;
    phase = float(INSTANCE_ID) * 0.37;
    float age = clamp((clock - INSTANCE_CUSTOM.x) / 1.4, 0.0, 1.0);
    fresh = 1.0 - age;
    float grow = ease_out_back(age);
    float breathe = 1.0 + sin(TIME * 1.3 + phase) * 0.04 * vigour;
    VERTEX *= breathe * grow;
}

void fragment() {
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    vec3 base = COLOR.rgb;
    float veins = 0.5 + 0.5 * sin(UV.x * 40.0 + UV.y * 25.0 + TIME * 0.8 + phase);
    ALBEDO = base * 0.45;
    ROUGHNESS = 0.25;
    METALLIC = 0.0;
    SPECULAR = 0.7;
    float glow = (0.55 + 1.6 * night) * (0.4 + 0.6 * vigour) + fresh * fresh * 3.0;
    EMISSION = base * base * (fres * 1.4 + 0.3 + veins * 0.2) * glow;
    ALPHA = 0.55 + fres * 0.45;
}
