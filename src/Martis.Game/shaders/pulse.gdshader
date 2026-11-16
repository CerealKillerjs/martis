shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;

// Expanding ring for events (mutation, deploy, extinction).
uniform vec4 tint : source_color = vec4(1.0, 0.8, 0.3, 1.0);
uniform float age = 0.0; // 0..1

void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float r = length(p);
    float ring = smoothstep(0.08, 0.0, abs(r - age * 0.95)) * (1.0 - age);
    ALBEDO = tint.rgb * ring * 3.0;
    ALPHA = ring;
}
