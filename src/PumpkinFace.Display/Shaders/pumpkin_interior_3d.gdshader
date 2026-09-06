shader_type spatial;
render_mode unshaded, cull_back;
#include "res://Shaders/candle_light.gdshaderinc"
#include "res://Shaders/lightsaber_light.gdshaderinc"

uniform float animation_time = 0.0;
uniform float light_intensity = 1.0;
uniform float brightness = 1.0;
uniform float gamma = 1.0;
uniform float candle_brightness = 1.0;
uniform vec3 candle_position = vec3(0.0, -1.46, -0.62);
uniform vec2 aperture_points[384];
uniform int aperture_counts[4];
uniform vec4 aperture_bounds[4];
varying vec3 local_position;
float cavity_occlusion(vec2 point, float camera_z) {
    float nearest = 1000.0;
    for (int feature = 0; feature < 4; feature++) {
        vec4 bounds = aperture_bounds[feature];
        if (point.x < bounds.x || point.y < bounds.y || point.x > bounds.z || point.y > bounds.w) { continue; }
        int count = aperture_counts[feature];
        for (int i = 0; i < 96; i++) {
            if (i >= count) { break; }
            vec2 a = aperture_points[feature * 96 + i];
            vec2 b = aperture_points[feature * 96 + (i + 1) % count];
            vec2 edge = b - a;
            float u = clamp(dot(point - a, edge) / max(dot(edge, edge), 0.01), 0.0, 1.0);
            nearest = min(nearest, length(point - a - edge * u));
        }
    }
    // Broad contact darkening behind the rim and every tooth, designed for
    // front projection. Fade the approximation out when inspecting in orbit.
    float frontal = pow(abs(camera_z), 12.0);
    return mix(1.0, mix(0.24, 1.0, smoothstep(0.0, 24.0, nearest)), frontal);
}
varying vec3 local_normal;
void vertex() {
    local_position = VERTEX;
    local_normal = NORMAL;
}
void fragment() {
    vec3 toward_flame = candle_position - local_position;
    float distance_to_flame = max(length(toward_flame), 0.1);
    vec3 normal = normalize(local_normal);
    float facing = max(dot(normal, toward_flame / distance_to_flame), 0.0);
    float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.25);
    float downward = smoothstep(-0.08, 0.68, -normal.y);
    float height = smoothstep(0.24, 1.72, local_position.y);
    float direct = pow(facing, 1.18) * mix(0.12, 2.0, downward) * mix(0.45, 1.4, height);
    float mouth = 1.0 - smoothstep(0.30, 1.55, abs(local_position.y - candle_position.y));
    float rib = 0.5 + 0.5 * cos(UV.x * 62.831853);
    float mottling = candle_noise(UV.x * 83.0 + sin(UV.y * 39.0)) * 0.055;
    float fibers = sin(local_position.x * 64.0 + sin(local_position.y * 9.0) * 2.0) * 0.010;
    float texture = 0.84 + rib * 0.08 + mottling + fibers;
    float bounce = falloff * (0.07 + direct * 1.35 + mouth * 0.92);
    float energy = max(0.0, brightness * candle_brightness * light_intensity) * candle_flutter(animation_time);
    // Dark amber in the recess, honey in the reflected pool, pale gold only
    // at the strongest reflection. No flat orange additive fill.
    float heat = smoothstep(0.08, 0.95, bounce);
    vec3 tint = mix(vec3(1.0, 0.22, 0.025), vec3(1.0, 0.70, 0.25), heat);
    float occlusion = cavity_occlusion(vec2(local_position.x, -local_position.y) * 150.0, INV_VIEW_MATRIX[2].z);
    vec3 candle = tint * bounce * energy * 4.5 * texture;
    vec3 saber = saber_irradiance(local_position, normal) * brightness * texture * 3.5;
    ALBEDO = candle_output((candle + saber) * occlusion, gamma);
}
