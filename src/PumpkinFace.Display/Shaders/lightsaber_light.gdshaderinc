// A vertical emitter just beyond the left side of the face. Sample its nearest
// point so reflected light wraps along the blade rather than forming a dot.
uniform float saber_strength = 0.0;
uniform vec3 saber_position = vec3(-4.4, 0.0, 5.0);

vec3 saber_irradiance(vec3 position, vec3 surface_normal) {
    vec3 nearest = vec3(saber_position.x, clamp(position.y, -2.6, 2.9), saber_position.z);
    vec3 toward = nearest - position;
    float distance_squared = max(dot(toward, toward), 0.01);
    float incidence = max(dot(normalize(surface_normal), toward * inversesqrt(distance_squared)), 0.0);
    float falloff = 1.0 / (1.0 + distance_squared * 0.38);
    return vec3(1.0, 0.006, 0.018) * saber_strength * 5.0 * falloff * (0.10 + 0.90 * incidence);
}
