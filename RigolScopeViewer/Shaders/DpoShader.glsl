uniform vec2 iResolution;
uniform vec2 iPan;        // Зміщення миші в пікселях під час drag
uniform float iZoomX;     // Зум по X під час drag (wheel)

uniform float iVoltsMin;  // Нижня межа екрану у вольтах
uniform float iVoltsMax;  // Верхня межа екрану у вольтах
uniform float iIntensity;

// Наша 1D текстура з даними бінів.
uniform shader iDataTexture; 

half4 main(vec2 fragCoord) {
    // 1. Fake Pan & Zoom (зміщуємо координати екрану)
    vec2 virtualCoord = fragCoord;
    virtualCoord.x = (virtualCoord.x - iPan.x) / iZoomX;
    
    // Отримуємо нормалізовані координати відносно оригінальної текстури
    vec2 uv = virtualCoord / iResolution;

    // Якщо ми витягнули графік за межі наявних даних - малюємо чорний фон
    if (uv.x < 0.0 || uv.x > 1.0) {
        return half4(0.0, 0.0, 0.0, 1.0);
    }

    // 2. Читаємо дані біна. 
    // eval() приймає координати в локальному просторі текстури (від 0 до Width)
    float sampleX = uv.x * iResolution.x;
    half4 binData = iDataTexture.eval(vec2(sampleX, 0.5));
    
    float mean = binData.r;
    float stdDev = binData.g;

    // Якщо даних немає (пустий бін), нічого не світиться
    if (stdDev <= 0.0) return half4(0.0, 0.0, 0.0, 1.0);

    // 3. Мапимо Y-піксель у Вольти
    // Skia має Y=0 зверху, тому інвертуємо UV по Y
    float vY = mix(iVoltsMin, iVoltsMax, 1.0 - uv.y);

    // 4. Математика DPO (Гауссіана)
    float diff = vY - mean;
    float exponent = -0.5 * (diff * diff) / (stdDev * stdDev);
    float alpha = exp(exponent) * iIntensity;
    
    alpha = clamp(alpha, 0.0, 1.0);

    // Колір фосфору (класичний осцилограф)
    vec3 color = vec3(1.0, 0.85, 0.1); 

    // Використовуємо alpha-premultiplied вивід для блендингу
    return half4(color * alpha, 1.0);
}