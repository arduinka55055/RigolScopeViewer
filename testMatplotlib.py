import time
import numpy as np
import matplotlib
matplotlib.use('Agg')  # headless backend
import matplotlib.pyplot as plt

def generate_signal(n, t0=-8e-4, tinc=1.6e-7):
    t = t0 + np.arange(n) * tinc
    f = 100.0
    y = 3.6 * np.sin(2 * np.pi * f * t)
    return y

def measure_matplotlib(data):
    fig, ax = plt.subplots(figsize=(8, 4))
    line, = ax.plot(data, linewidth=0.5)
    # прогрів
    fig.canvas.draw()
    plt.close(fig)
    
    # вимірювання (середнє за 10 рендерів)
    times = []
    for _ in range(10):
        start = time.perf_counter()
        fig.canvas.draw()
        times.append(time.perf_counter() - start)
    plt.close(fig)
    return np.mean(times) * 1000  # у мілісекундах

def main():
    point_counts = [10000, 100000, 1000000, 10000000]  # 10 млн може призвести до OOM
    print("Кількість точок\tMatplotlib (ms)")
    print("-------------------------------")
    for n in point_counts:
        print(f"Генерація {n} точок...", end=' ', flush=True)
        data = generate_signal(n)
        print("вимірювання...", flush=True)
        try:
            ms = measure_matplotlib(data)
            print(f"{n}\t\t{ms:.1f}")
        except Exception as e:
            print(f"{n}\t\tПомилка ({e})")

if __name__ == "__main__":
    main()