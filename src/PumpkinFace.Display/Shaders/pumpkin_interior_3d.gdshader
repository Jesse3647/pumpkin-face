shader_type spatial;
render_mode cull_back, diffuse_burley, specular_disabled;

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
	vec3 toward_flame = candle_position - local_position;
	float distance_to_flame = max(length(toward_flame), 0.1);
	float facing = max(dot(normalize(local_normal), toward_flame / distance_to_flame), 0.0);
	float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.30);
	float bounce = falloff * (0.16 + facing * 0.84);
	float flutter = 0.93 + sin(animation_time * 5.7) * 0.055 + sin(animation_time * 11.9 + 1.3) * 0.027;
	float exposure = max(0.0, light_intensity * brightness * candle_brightness) * flutter;

	float rib = 0.5 + 0.5 * cos(UV.x * 62.831853);
	float mottling = hash21(floor(UV * vec2(55.0, 34.0)));
	vec3 dark_flesh = vec3(0.24, 0.031, 0.0018);
	vec3 cut_flesh = vec3(0.69, 0.135, 0.008);
	vec3 flesh = mix(dark_flesh, cut_flesh, 0.30 + rib * 0.12 + mottling * 0.08);
	flesh = pow(max(flesh, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));

	vec3 bounced_candle = vec3(1.0, 0.34, 0.032) * bounce * exposure;
	ALBEDO = flesh;
	ROUGHNESS = 0.96;
	EMISSION = bounced_candle * 0.60;
}
