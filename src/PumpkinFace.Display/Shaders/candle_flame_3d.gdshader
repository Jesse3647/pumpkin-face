shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float gamma : hint_range(0.25, 4.0) = 1.0;
uniform float candle_brightness : hint_range(0.1, 3.0) = 1.0;
uniform float flame_layer : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float height_fade = smoothstep(0.0, 0.12, UV.y) * (1.0 - smoothstep(0.78, 1.0, UV.y));
	float flutter = 0.94 + sin(animation_time * 5.7) * 0.045 + sin(animation_time * 11.9 + 1.3) * 0.024;
	vec3 ember = vec3(1.0, 0.12, 0.006);
	vec3 gold = vec3(1.0, 0.54, 0.055);
	vec3 hot = vec3(1.0, 0.88, 0.50);
	vec3 color = mix(ember, gold, smoothstep(0.05, 0.72, UV.y));
	color = mix(color, hot, flame_layer * 0.82 + smoothstep(0.58, 0.92, UV.y) * 0.18);
	color *= max(0.15, light_intensity * brightness * candle_brightness) * flutter;
	color = pow(max(color, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));
	ALBEDO = color;
	EMISSION = color * mix(2.8, 4.2, flame_layer);
	ALPHA = height_fade * mix(0.60, 0.78, flame_layer);
}
