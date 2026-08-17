shader_type canvas_item;
render_mode unshaded;

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.1, 2.0) = 1.0;
uniform float gamma : hint_range(0.4, 2.5) = 1.0;
uniform vec2 feature_seed = vec2(0.0);
uniform vec4 ember_color : source_color = vec4(1.0, 0.20, 0.015, 1.0);
uniform vec4 flame_color : source_color = vec4(1.0, 0.77, 0.16, 1.0);
uniform vec4 hot_color : source_color = vec4(1.0, 0.96, 0.62, 1.0);

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
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

float fbm(vec2 p) {
	float result = 0.0;
	float amplitude = 0.55;
	for (int octave = 0; octave < 4; octave++) {
		result += value_noise(p) * amplitude;
		p = p * 2.03 + vec2(17.13, 9.71);
		amplitude *= 0.48;
	}
	return result;
}

void fragment() {
	vec2 local = UV;
	// One low candle drives every opening. Feature seeds move only the static
	// mottling, so eyes and mouth brighten together instead of twinkling as
	// unrelated vector fills.
	vec2 screen_domain = SCREEN_UV * vec2(10.0, 5.625);
	float global_flutter = 0.968;
	global_flutter += sin(animation_time * 5.7) * 0.024;
	global_flutter += sin(animation_time * 9.9 + 1.3) * 0.012;

	vec2 flame_origin = vec2(
		0.50 + sin(animation_time * 0.83) * 0.035,
		0.91 + sin(animation_time * 1.17 + 0.6) * 0.018);
	vec2 flame_domain = vec2(local.x * 3.5, local.y * 2.8 - animation_time * 0.11);
	flame_domain += screen_domain * vec2(0.34, 0.48) + feature_seed * 7.0;
	float broad_flame = fbm(flame_domain);
	float fine_flame = fbm(flame_domain * 2.35 + vec2(animation_time * 0.07, 0.0));

	float source_distance = length((local - flame_origin) * vec2(0.76, 1.18));
	float source_heat = 1.0 - smoothstep(0.06, 0.78, source_distance);
	float above_source = smoothstep(-0.06, 0.78, flame_origin.y - local.y);
	float plume_center = flame_origin.x + sin(local.y * 8.0 - animation_time * 1.6) * 0.055;
	float plume = (1.0 - smoothstep(0.08, 0.44, abs(local.x - plume_center))) * above_source;
	float heat = clamp(
		0.30 + broad_flame * 0.28 + fine_flame * 0.12 +
		source_heat * 0.55 + plume * 0.20,
		0.0,
		1.0);

	vec3 glow = mix(ember_color.rgb, flame_color.rgb, smoothstep(0.16, 0.68, heat));
	glow = mix(glow, hot_color.rgb, smoothstep(0.50, 0.94, heat) * (0.40 + source_heat * 0.60));

	float box_edge = min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y));
	float edge_occlusion = mix(0.76, 1.0, smoothstep(0.0, 0.18, box_edge));
	float upper_soot = mix(0.84, 1.0, smoothstep(0.0, 0.28, local.y));
	float exposure = max(0.0, light_intensity * brightness);
	float candle_exposure = mix(0.72, 1.20, smoothstep(0.12, 0.92, heat));
	glow *= exposure * edge_occlusion * upper_soot * candle_exposure * global_flutter * 1.18;
	// Preserve cream-hot cores and amber midtones without clipping the whole
	// cavity to a flat white card.
	glow /= vec3(1.0) + glow * 0.060;
	glow = min(glow, vec3(1.0, 0.89, 0.52));
	glow = pow(max(glow, vec3(0.0)), vec3(1.0 / max(gamma, 0.05)));

	float organic_alpha = 0.965 + (broad_flame - 0.5) * 0.018;
	COLOR = vec4(glow, clamp(organic_alpha, 0.0, 1.0));
}
