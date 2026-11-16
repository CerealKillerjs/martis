shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

// Bioluminescent colony domes. Per-instance colour from the MultiMesh: rgb = colour,
// a = vigour (0..1). Fresnel rim + slow breathing pulse; brighter at night.
uniform float night = 0.0;

varying float vigour;
varying float phase;

void vertex() {
    vigour = COLOR.a;
    phase = float(INSTANCE_ID) * 0.37;
    float breathe = 1.0 + sin(TIME * 1.3 + phase) * 0.04 * vigour;
    VERTEX *= breathe;
}

void fragment() {
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    vec3 base = COLOR.rgb;
    float veins = 0.5 + 0.5 * sin(UV.x * 40.0 + UV.y * 25.0 + TIME * 0.8 + phase);
    ALBEDO = base * 0.45;
    ROUGHNESS = 0.25;
    METALLIC = 0.0;
    SPECULAR = 0.7;
    float glow = (0.55 + 1.6 * night) * (0.4 + 0.6 * vigour);
    EMISSION = base * base * (fres * 1.4 + 0.3 + veins * 0.2) * glow;
    ALPHA = 0.55 + fres * 0.45;
}
