shader_type spatial;
render_mode unshaded, cull_back;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float gamma : hint_range(0.25, 4.0) = 1.0;
uniform float candle_brightness : hint_range(0.1, 3.0) = 1.0;
uniform vec3 candle_position = vec3(0.0, -1.46, -0.62);

varying vec3 local_position;
varying vec3 local_normal;

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

void vertex() {
	local_position = VERTEX;
	local_normal = NORMAL;
}

void fragment() {
	vec3 normal = normalize(local_normal);
	vec3 toward_flame = candle_position - local_position;
	float distance_to_flame = max(length(toward_flame), 0.1);
	vec3 light_direction = toward_flame / distance_to_flame;

	// The upper cut surface faces downward toward the candle. The lower sill
	// faces upward and receives only restrained bounced light.
	float direct = max(dot(normal, light_direction), 0.0);
	float downward_facing = smoothstep(-0.10, 0.72, -normal.y);
	// Freshly cut pumpkin flesh is pale, damp and rough. Use a broad wrapped
	// response in addition to the real OmniLight so it reads as reflected
	// candlelight rather than a flat emissive orange strip.
	float wrapped_direct = max((dot(normal, light_direction) + 0.34) / 1.34, 0.0);
	direct = mix(pow(wrapped_direct, 1.12), pow(direct, 1.25), 0.62) *
		mix(0.36, 1.0, downward_facing);
	float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.24);
	float mouth_proximity = 1.0 - smoothstep(
		0.42,
		1.58,
		abs(local_position.y - candle_position.y));
	float outward_flux = mouth_proximity * falloff * 0.40;
	float cavity_fill = falloff * (0.15 + 0.16 * (1.0 - abs(normal.z)));
	float bounce = cavity_fill + falloff * mix(0.050, 0.135, downward_facing) + outward_flux;
	float flutter = 0.93 + sin(animation_time * 5.7) * 0.055 + sin(animation_time * 11.9 + 1.3) * 0.027;
	float exposure = max(0.0, light_intensity * brightness * candle_brightness) * flutter;

	float fibers = hash21(floor(vec2(UV.x * 84.0, UV.y * 13.0)));
	float depth_darken = mix(1.0, 0.72, smoothstep(0.0, 1.0, UV.y));
	vec3 pale_flesh = mix(
		vec3(0.50, 0.075, 0.003),
		vec3(0.82, 0.20, 0.016),
		0.30 + fibers * 0.16) * depth_darken;
	vec3 candle_color = vec3(1.0, 0.285, 0.014);
	vec3 broad_reflection = candle_color * (bounce + direct * falloff * 1.34) * exposure * 1.18;
	vec3 damp_highlight = vec3(1.0, 0.58, 0.13) *
		pow(max(direct * falloff, 0.0), 2.35) * exposure * 0.32;
	vec3 color = pale_flesh * (0.055 + bounce * exposure * 0.20) +
		broad_reflection + damp_highlight;
	color = color / (vec3(1.0) + color * 0.48);
	color = pow(max(color, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));
	ALBEDO = color;
}
