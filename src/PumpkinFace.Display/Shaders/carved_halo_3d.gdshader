shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float candle_brightness : hint_range(0.1, 3.0) = 1.0;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.20;

void fragment() {
	// Ring UV.y runs from zero at the enlarged outer contour to one at the
	// exact cut edge. Fade outward so projected light lands only around holes.
	float fade = smoothstep(0.0, 0.92, UV.y);
	float flutter = 0.96 + sin(animation_time * 5.7) * 0.025 + sin(animation_time * 9.9 + 1.3) * 0.012;
	vec3 color = vec3(1.0, 0.36, 0.035) * max(0.0, light_intensity * brightness * candle_brightness) * flutter;
	ALBEDO = color;
	EMISSION = color * 1.35;
	ALPHA = fade * halo_strength;
}
