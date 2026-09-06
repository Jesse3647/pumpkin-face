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
varying vec3 local_position;
varying vec3 local_normal;
void vertex() {
    local_position = VERTEX;
    local_normal = NORMAL;
}
void fragment() {
    vec3 toward_flame = candle_position - local_position;
    float distance_to_flame = max(length(toward_flame), 0.1);
    vec3 normal = normalize(local_normal);
    float incidence = max(dot(normal, toward_flame / distance_to_flame), 0.0);
    float falloff = 1.0 / (1.0 + distance_to_flame * distance_to_flame * 0.22);
    // An overhang lights from below; its outer edge stays soot-dark. The sill
    // gets subdued bounce. Depth shading prevents a constant-width neon ring.
    float overhang = smoothstep(-0.12, 0.65, -normal.y);
    float edge = smoothstep(0.0, 0.18, UV.y);
    float seam = 1.0 - smoothstep(0.74, 1.0, UV.y) * 0.32;
    float fibers = sin(UV.x * 580.0 + sin(UV.y * 14.0) * 1.2) * 0.045;
    float grain = candle_noise(UV.x * 135.0) * 0.10;
    vec3 flesh = mix(vec3(0.73, 0.24, 0.035), vec3(1.0, 0.68, 0.27),
        0.42 + fibers + grain);
    float direct = incidence * falloff * mix(0.55, 1.35, overhang);
    float bounce = falloff * (0.09 + 0.16 * overhang);
    float energy = max(0.0, brightness * candle_brightness * light_intensity) * candle_flutter(animation_time);
    vec3 reflected = flesh * vec3(1.0, 0.76, 0.38) * (bounce + direct * 3.7);
    vec3 sheen = vec3(1.0, 0.78, 0.43) * pow(incidence, 7.0) * falloff * 0.18;
    vec3 saber = saber_irradiance(local_position, normal) * brightness * mix(0.5, 1.0, edge);
    ALBEDO = candle_output(((reflected + sheen) * energy * mix(0.13, 1.0, edge) + saber) * seam, gamma);
}
