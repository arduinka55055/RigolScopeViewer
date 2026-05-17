uniform vec2 iResolution;
uniform vec2 iPan;
uniform vec2 iZoom;
uniform float iVoltsMin;
uniform float iVoltsMax;
uniform float iIntensity;
uniform vec3 iColor;
uniform shader iDataTexture;

half4 main(vec2 fragCoord) {
    vec2 virtualCoord = fragCoord;
    virtualCoord.x = (virtualCoord.x - iPan.x) / iZoom.x;
    virtualCoord.y = (virtualCoord.y - iPan.y) / iZoom.y;

    vec2 uv = virtualCoord / iResolution;

    if(uv.x < 0.0 || uv.x > 1.0) {
        return half4(0.0);
    }

    // --- ФІКС СУБ-ПІКСЕЛЬНОГО ДРИФТУ ---
    // Тепер краї екрану ідеально співпадають з центрами крайніх пікселів текстури!
    float sampleX = mix(0.5, iResolution.x - 0.5, uv.x);
    float indexLeft = floor(sampleX - 0.5);
    float indexRight = indexLeft + 1.0;
    float fractX = fract(sampleX - 0.5);

    half4 dataLeft = iDataTexture.eval(vec2(indexLeft + 0.5, 0.5));
    half4 dataRight = iDataTexture.eval(vec2(indexRight + 0.5, 0.5));

    bool emptyLeft = dataLeft.g > dataLeft.b;
    bool emptyRight = dataRight.g > dataRight.b;

    if(emptyLeft && emptyRight) {
        return half4(0.0);
    }

    if(emptyLeft)
        dataLeft = dataRight;
    if(emptyRight)
        dataRight = dataLeft;

    float mean = mix(dataLeft.r, dataRight.r, fractX);
    float minV = mix(dataLeft.g, dataRight.g, fractX);
    float maxV = mix(dataLeft.b, dataRight.b, fractX);

    float vY = mix(iVoltsMin, iVoltsMax, 1.0 - uv.y);

    // Враховуємо ЗУМ, щоб товщина лінії (1.5 пікселя) завжди залишалася правильною на екрані
    float voltsPerPixel = abs(iVoltsMax - iVoltsMin) / (iResolution.y * max(iZoom.y, 0.0001));
    float minRadius = voltsPerPixel * 1.5;

    minV = min(minV, mean - minRadius);
    maxV = max(maxV, mean + minRadius);

    if(vY < minV || vY > maxV) {
        float distToEdge = max(minV - vY, vY - maxV);
        float edgeAlpha = max(0.0, 1.0 - (distToEdge / voltsPerPixel));

        if(edgeAlpha <= 0.0)
            return half4(0.0);

        float alpha = edgeAlpha * 0.3 * iIntensity;
        return half4(iColor * alpha, alpha);
    }

    float distToMean = abs(vY - mean);
    float maxDist = (vY >= mean) ? (maxV - mean) : (mean - minV);
    maxDist = max(maxDist, 0.0001);

    float normalizedDist = distToMean / maxDist;
    float glow = mix(1.0, 0.3, normalizedDist);

    float alphaInside = clamp(glow * iIntensity, 0.0, 1.0);
    return half4(iColor * alphaInside, alphaInside);
}