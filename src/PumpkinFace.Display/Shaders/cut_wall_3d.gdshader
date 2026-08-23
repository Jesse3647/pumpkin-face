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
	direct = pow(direct, 1.30) * mix(0.28, 1.0, downward_facing);
	float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.24);
	float mouth_proximity = 1.0 - smoothstep(
		0.42,
		1.58,
		abs(local_position.y - candle_position.y));
	float outward_flux = mouth_proximity * falloff * 0.34;
	float bounce = falloff * mix(0.018, 0.065, downward_facing) + outward_flux;
	float flutter = 0.93 + sin(animation_time * 5.7) * 0.055 + sin(animation_time * 11.9 + 1.3) * 0.027;
	float exposure = max(0.0, light_intensity * brightness * candle_brightness) * flutter;

	vec3 charred_flesh = vec3(0.035, 0.0045, 0.0005);
	vec3 candle_color = vec3(1.0, 0.30, 0.018);
	vec3 color = charred_flesh + candle_color * (bounce + direct * falloff * 1.55) * exposure;
	color = color / (vec3(1.0) + color * 0.42);
	color = pow(max(color, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));
	ALBEDO = color;
}
