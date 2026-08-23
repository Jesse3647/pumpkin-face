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
	vec3 toward_flame = candle_position - local_position;
	float distance_to_flame = max(length(toward_flame), 0.1);
	vec3 normal = normalize(local_normal);
	vec3 light_direction = toward_flame / distance_to_flame;
	float facing = max(dot(normal, light_direction), 0.0);
	float wrapped_facing = max((dot(normal, light_direction) + 0.28) / 1.28, 0.0);
	float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.30);
	float downward_facing = smoothstep(-0.08, 0.68, -normal.y);
	// Eye openings occupy roughly y=0.25..1.75 in model space. Preserve the
	// geometric facing term, then exaggerate that vertical range just enough
	// for the candle direction to survive projector contrast and slight blur.
	float aperture_height = smoothstep(0.24, 1.72, local_position.y);
	float direct = mix(pow(wrapped_facing, 1.2), pow(facing, 1.18), 0.70) *
		mix(0.055, 2.05, downward_facing) *
		mix(0.38, 1.42, aperture_height);
	float mouth_proximity = 1.0 - smoothstep(
		0.42,
		1.58,
		abs(local_position.y - candle_position.y));
	// Light escaping through the large mouth is mostly multi-bounce energy;
	// it remains visible even where the inner wall does not face the flame.
	float outward_flux = mouth_proximity * falloff * 0.72;
	// Approximate warm interreflection from the opposite cavity walls. The
	// upward component is deliberately soft so it fills shadows without
	// erasing the candle's clear bottom-to-top direction.
	float cavity_wrap = 0.11 + 0.10 * (1.0 - abs(normal.z)) + 0.055 * max(normal.y, 0.0);
	float bounce = falloff * (cavity_wrap + direct * 1.02) + outward_flux;
	float flutter = 0.93 + sin(animation_time * 5.7) * 0.055 + sin(animation_time * 11.9 + 1.3) * 0.027;
	float exposure = max(0.0, light_intensity * brightness * candle_brightness) * flutter;

	float rib = 0.5 + 0.5 * cos(UV.x * 62.831853);
	float mottling = hash21(floor(UV * vec2(55.0, 34.0)));
	vec3 dark_flesh = vec3(0.18, 0.022, 0.0014);
	vec3 cut_flesh = vec3(0.62, 0.115, 0.0065);
	vec3 flesh = mix(dark_flesh, cut_flesh, 0.28 + rib * 0.14 + mottling * 0.10);
	flesh = pow(max(flesh, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));

	vec3 bounced_candle = vec3(1.0, 0.285, 0.016) * bounce * exposure * 2.20;
	vec3 soft_sheen = vec3(1.0, 0.48, 0.075) *
		pow(max(wrapped_facing * falloff, 0.0), 2.6) * exposure * 0.18;
	vec3 color = flesh * (0.026 + bounce * exposure * 0.21) + bounced_candle + soft_sheen;
	color = color / (vec3(1.0) + color * 0.38);
	ALBEDO = color;
}
