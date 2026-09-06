shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
#include "res://Shaders/candle_light.gdshaderinc"
#include "res://Shaders/lightsaber_light.gdshaderinc"

uniform float animation_time = 0.0;
uniform float light_intensity : hint_range(0.0, 2.0) = 1.0;
uniform float brightness : hint_range(0.0, 2.0) = 1.0;
uniform float gamma : hint_range(0.25, 4.0) = 1.0;
uniform float candle_brightness : hint_range(0.1, 3.0) = 1.0;
uniform vec3 candle_position = vec3(0.0, -1.46, -0.62);
uniform vec2 left_eye_points[96];
uniform vec2 right_eye_points[96];
uniform vec2 nose_points[96];
uniform vec2 mouth_points[96];
uniform int left_eye_count = 0;
uniform int right_eye_count = 0;
uniform int nose_count = 0;
uniform int mouth_count = 0;

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
	if (local_position.z > 0.0) {
		vec2 design_point = vec2(local_position.x, -local_position.y) * 150.0;
		bool inside = false;
		for (int index = 0; index < 96; index++) {
			if (index >= left_eye_count) { break; }
			int previous = (index + left_eye_count - 1) % left_eye_count;
			vec2 a = left_eye_points[index];
			vec2 b = left_eye_points[previous];
			if ((a.y > design_point.y) != (b.y > design_point.y) &&
				design_point.x < (b.x - a.x) * (design_point.y - a.y) / (b.y - a.y) + a.x) {
				inside = !inside;
			}
		}
		if (inside) { discard; }

		inside = false;
		for (int index = 0; index < 96; index++) {
			if (index >= right_eye_count) { break; }
			int previous = (index + right_eye_count - 1) % right_eye_count;
			vec2 a = right_eye_points[index];
			vec2 b = right_eye_points[previous];
			if ((a.y > design_point.y) != (b.y > design_point.y) &&
				design_point.x < (b.x - a.x) * (design_point.y - a.y) / (b.y - a.y) + a.x) {
				inside = !inside;
			}
		}
		if (inside) { discard; }

		inside = false;
		for (int index = 0; index < 96; index++) {
			if (index >= nose_count) { break; }
			int previous = (index + nose_count - 1) % nose_count;
			vec2 a = nose_points[index];
			vec2 b = nose_points[previous];
			if ((a.y > design_point.y) != (b.y > design_point.y) &&
				design_point.x < (b.x - a.x) * (design_point.y - a.y) / (b.y - a.y) + a.x) {
				inside = !inside;
			}
		}
		if (inside) { discard; }

		inside = false;
		for (int index = 0; index < 96; index++) {
			if (index >= mouth_count) { break; }
			int previous = (index + mouth_count - 1) % mouth_count;
			vec2 a = mouth_points[index];
			vec2 b = mouth_points[previous];
			if ((a.y > design_point.y) != (b.y > design_point.y) &&
				design_point.x < (b.x - a.x) * (design_point.y - a.y) / (b.y - a.y) + a.x) {
				inside = !inside;
			}
		}
		if (inside) { discard; }
	}

	// The shell normally projects black while still occluding the candle.
	// An active saber reveals only the reflected red light on the curved skin.
	ALBEDO = vec3(0.0);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	// Feather before the silhouette so projection remains a local wash of light.
	float front = smoothstep(0.8, 2.9, local_position.z);
	float face_area = exp(-pow(length(local_position.xy / vec2(3.7, 2.8)), 4.0));
	float skin = 0.84 + 0.10 * hash21(local_position.xy * 18.0);
	EMISSION = candle_output(saber_irradiance(local_position, local_normal) * brightness * front * face_area * skin, gamma);
}
