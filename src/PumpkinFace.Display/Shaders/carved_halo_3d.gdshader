shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float candle_brightness : hint_range(0.1, 3.0) = 1.0;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.20;
uniform float outward_strength : hint_range(0.0, 3.0) = 1.0;
uniform vec3 candle_position = vec3(0.0, -1.46, -0.62);
uniform float feature_center_y = 0.0;
uniform float feature_half_height = 0.5;

varying vec3 local_position;

void vertex() {
	local_position = VERTEX;
}

void fragment() {
	// Ring UV.y runs from zero at the enlarged outer contour to one at the
	// exact cut edge. Fade outward so projected light lands only around holes.
	float fade = smoothstep(0.0, 0.92, UV.y);
	float relative_height = (local_position.y - feature_center_y) / max(feature_half_height, 0.05);
	float upper_edge = smoothstep(-0.52, 0.68, relative_height);
	float source_distance = length(candle_position - local_position);
	float source_falloff = 1.0 / (1.0 + source_distance * source_distance * 0.12);
	float directional_spill = mix(0.20, 1.0, upper_edge) *
		(0.72 + source_falloff * 0.72) * outward_strength;
	float flutter = 0.96 + sin(animation_time * 5.7) * 0.025 + sin(animation_time * 9.9 + 1.3) * 0.012;
	vec3 color = vec3(1.0, 0.36, 0.035) * max(0.0, light_intensity * brightness * candle_brightness) * flutter;
	ALBEDO = color;
	EMISSION = color * 1.35;
	ALPHA = fade * halo_strength * directional_spill;
}
