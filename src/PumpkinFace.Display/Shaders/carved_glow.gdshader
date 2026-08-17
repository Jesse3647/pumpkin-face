shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.1, 2.0) = 1.0;
uniform float gamma : hint_range(0.4, 2.5) = 1.0;
uniform float halo_strength : hint_range(0.0, 1.0) = 0.18;
uniform vec2 feature_seed = vec2(0.0);
uniform vec4 glow_color : source_color = vec4(1.0, 0.39, 0.035, 1.0);

float hash21(vec2 p) {
	p = fract(p * vec2(127.1, 311.7));
	p += dot(p, p + 34.57);
	return fract(p.x * p.y);
}

float value_noise(vec2 p) {
	vec2 cell = floor(p);
	vec2 local = fract(p);
	local = local * local * (3.0 - 2.0 * local);
	float a = hash21(cell);
	float b = hash21(cell + vec2(1.0, 0.0));
	float c = hash21(cell + vec2(0.0, 1.0));
	float d = hash21(cell + vec2(1.0, 1.0));
	return mix(mix(a, b, local.x), mix(c, d, local.x), local.y);
}

void fragment() {
	vec2 local = UV;
	vec2 screen_domain = SCREEN_UV * vec2(10.0, 5.625);
	float noise = value_noise(
		local * 3.1 + screen_domain * 0.42 + feature_seed * 13.0);
	float flutter = 0.97;
	flutter += sin(animation_time * 5.7) * 0.022;
	flutter += sin(animation_time * 9.9 + 1.3) * 0.010;
	float center_distance = length((local - vec2(0.5, 0.56)) * vec2(0.76, 1.0));
	float soft_shape = 1.0 - smoothstep(0.18, 0.80, center_distance);
	float box_edge = min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y));
	float edge_feather = smoothstep(0.0, 0.22, box_edge);
	float low_spill = mix(0.86, 1.08, smoothstep(0.10, 0.92, local.y));
	float alpha = halo_strength * mix(0.88, 1.0, noise) *
		mix(0.26, 1.0, soft_shape) * edge_feather * low_spill;
	vec3 color = glow_color.rgb * max(0.0, light_intensity * brightness) * flutter;
	color = pow(max(color, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));
	COLOR = vec4(color, clamp(alpha, 0.0, 1.0));
}
