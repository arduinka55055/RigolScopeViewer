// Voronoi + Triangle Mask Shader
uniform float iTime;
uniform vec2 iResolution;

// Simple hash for Voronoi
vec2 hash(vec2 p) {
    p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
    return fract(sin(p) * 43758.5453);
}

half4 main(vec2 fragCoord) {
    vec2 uv = fragCoord / iResolution.xy;
    
    // Voronoi Logic
    vec2 p = uv * 8.0;
    vec2 i_p = floor(p);
    vec2 f_p = fract(p);
    float minDist = 1.0;

    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            vec2 neighbor = vec2(float(x), float(y));
            vec2 point = hash(i_p + neighbor);
            point = 0.5 + 0.5 * sin(iTime + 6.2831 * point);
            float dist = length(neighbor + point - f_p);
            minDist = min(minDist, dist);
        }
    }

    // Output color based on Voronoi distance
    return half4(vec3(1.0 - minDist) * vec3(0.2, 0.5, 0.9), 1.0);
}