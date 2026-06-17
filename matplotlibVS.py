import matplotlib.pyplot as plt
import pandas as pd
import numpy as np
import io
import time
import re

# --- НАЛАШТУВАННЯ ---
# False - читає твій CSV з новим форматом
# True - генерує 5 мільйонів точок для стрес-тесту
STRESS_TEST_MODE = False 

csv_data = open('f:\\dac\\RigolDS0.csv', 'r').read()

if not STRESS_TEST_MODE:
    print("Аналіз заголовка та завантаження даних...")
    
    # 1. Витягуємо перший рядок для пошуку t0 та tInc
    lines = csv_data.strip().split('\n')
    header = lines[0]
    
    # Використовуємо регулярні вирази для парсингу значень з плаваючою комою
    t0_match = re.search(r't0\s*=\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)', header)
    tInc_match = re.search(r'tInc\s*=\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)', header)
    
    t0 = float(t0_match.group(1)) if t0_match else 0.0
    tInc = float(tInc_match.group(1)) if tInc_match else 1e-6
    
    print(f"Знайдено параметри: t0 = {t0}, tInc = {tInc}")

    # 2. Читаємо матрицю даних
    # Пропускаємо перший рядок (skiprows=1), бо там "брудний" заголовок.
    # Беремо лише перші 4 колонки (usecols=[0,1,2,3]), щоб проігнорувати порожні коми в кінці рядків.
    df = pd.read_csv(io.StringIO(csv_data), skiprows=1, header=None, 
                     usecols=[0, 1, 2, 3], names=['CH1V', 'CH2V', 'CH3V', 'CH4V'])
    
    # 3. Математично генеруємо вісь часу для кожної точки
    df['Time(s)'] = t0 + df.index * tInc

else:
    print("Генерація 5 000 000 точок. Пристебніться...")
    points = 5_000_000
    t = np.linspace(-0.005, 0.005, points)
    
    ch1 = 3.6 + np.sin(t * 100000) * 0.5 + np.random.normal(0, 0.1, points)
    ch2 = 3.1 + np.cos(t * 150000) * 0.4 + np.random.normal(0, 0.1, points)
    ch3 = 4.0 + np.sin(t * 50000) * 0.8
    ch4 = 0.8 + np.cos(t * 20000) * 0.2
    
    df = pd.DataFrame({
        'Time(s)': t,
        'CH1V': ch1, 'CH2V': ch2, 'CH3V': ch3, 'CH4V': ch4
    })

print("Дані готові. Починаємо рендеринг (CPU-bound)...")
start_time = time.time()

# Налаштування стилю під осцилограф
plt.style.use('dark_background')
fig, ax = plt.subplots(figsize=(12, 6))
fig.patch.set_facecolor('#1e1e1e')
ax.set_facecolor('#121212')

# Малюємо всі 4 канали
ax.plot(df['Time(s)'], df['CH1V'], color='#ffff00', label='CH1', linewidth=0.5, alpha=0.8)
ax.plot(df['Time(s)'], df['CH2V'], color='#00ffff', label='CH2', linewidth=0.5, alpha=0.8)
ax.plot(df['Time(s)'], df['CH3V'], color='#ff00ff', label='CH3', linewidth=0.5, alpha=0.8)
ax.plot(df['Time(s)'], df['CH4V'], color='#00ff00', label='CH4', linewidth=0.5, alpha=0.8)

ax.set_title(f'Matplotlib Rendering Test ({len(df):,} точок)', color='white', pad=15)
ax.set_xlabel('Time (s)', color='white')
ax.set_ylabel('Voltage (V)', color='white')
ax.grid(True, color='#333333', linestyle='--')
ax.legend(loc='upper right')

render_time = time.time() - start_time
print(f"Первинна підготовка графіків зайняла: {render_time:.2f} секунд")
print("Спробуйте запанорамувати графік (Pan) або зробити Zoom. Інтерфейс має зависнути.")

plt.show()