// Continuous, deterministic noise: slow lean plus short, non-periodic flutter.
// Every material uses the same source so the openings never twinkle separately.
float candle_noise(float t) {
    float i = floor(t);
    float f = fract(t);
    f = f * f * (3.0 - 2.0 * f);
    return mix(fract(sin(i * 127.1 + 7.3) * 43758.5453),
        fract(sin((i + 1.0) * 127.1 + 7.3) * 43758.5453), f);
}
uniform float candle_flicker = 1.0;
float candle_flutter(float t) {
    return candle_flicker;
}
// Smooth highlight shoulder retains amber shadows and pale gold highlights.
vec3 candle_output(vec3 radiance, float display_gamma) {
    vec3 mapped = vec3(1.0) - exp(-max(radiance, vec3(0.0)));
    return pow(mapped, vec3(1.0 / max(display_gamma, 0.05)));
}
