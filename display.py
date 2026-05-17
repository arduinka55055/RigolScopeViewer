import matplotlib.pyplot as plt
import numpy as np
from matplotlib.collections import LineCollection
from matplotlib.colors import Normalize
from scipy import signal as sp_signal
from scipy.ndimage import gaussian_filter1d
import os
import matplotlib.image as mimage

# --- Parameters for Simulation ---
N_samples = 100_000
t_total = 1e-6  # Total time (1 microseconds)
t = np.linspace(0, t_total, N_samples, endpoint=False)

# Signal parameters
V_amplitude = 3.3  # Volts
f_signal = 1e6     # 1 MHz square wave

# Ringing parameters (High Frequency Transient)
f_ring = 100e6     # 100 MHz ringing frequency
tau_damp = 10e-9    # ns damping constant
A_overshoot = 5  # V extra overshoot

# Reflection (Transmission Line Mismatch)
R_reflection = 0.4  # 40% reflection coefficient (R_c)
t_delay = 19e-9     # ns round-trip time (t_d)

# --- 1. Generate Ideal Square Wave ---
# Using scipy.signal.square for a clean, fast-edge meander
V_ideal = V_amplitude * sp_signal.square(2 * np.pi * f_signal * t)

# --- 2. Generate Ringing/Overshoot Transient ---
# A damped sinusoid that only contributes to the fast edges
V_ringing = A_overshoot * np.exp(-t / tau_damp) * np.sin(2 * np.pi * f_ring * t)

# Ringing logic (Simplified): Reset the damped sinusoid every time the ideal signal changes
# (In a real simulation, you'd integrate the RLC model, but this is a faster approximation)
V_transient = np.zeros_like(V_ideal)
for i in range(1, N_samples):
    if V_ideal[i] != V_ideal[i-1]:
        # Reset the ringing transient on every edge.
        # This is a huge simplification but captures the conceptual effect.
        # For this example, we'll keep the V_ringing as a continuous signal 
        # that fades quickly, and only let it affect the signal at the edges.

        # *** A simpler and more illustrative approach: Sum the components ***
        # The ringing *happens* at the edges. For a conceptual plot, let's just 
        # add the damped sinusoid that decays quickly. It's not perfectly synchronized,
        # but will illustrate the damped oscillation.

        # Resetting the ringing starts at t=0, so this component will mostly
        # influence the first rising edge, and then be near zero for subsequent edges.
        # For a better plot, the ringing must be restarted on *each* edge.
        pass

# A conceptually illustrative way to apply the ringing to edges:
# Find the indices where the signal flips (the edges)
edges = np.where(np.abs(np.diff(V_ideal)) > 0)[0] + 1
V_final = np.copy(V_ideal)

# Apply a new damped ringing component starting at each edge
for edge_index in edges:
    t_edge = t[edge_index:] - t[edge_index]
    # Create a fresh damped ringing waveform for this edge
    ringing_on_edge = A_overshoot * np.exp(-t_edge / tau_damp) * np.sin(2 * np.pi * f_ring * t_edge)
    
    # We add the ringing effect (the decay) to the step change.
    # The actual voltage at the edge would be V_ideal[edge_index] + ringing_on_edge[0]
    # Since V_ideal is a step, the transient *replaces* the step change for a brief period.
    # A simple addition:
    V_final[edge_index:] += ringing_on_edge * np.sign(V_ideal[edge_index] - V_ideal[edge_index-1])


# --- 3. Generate Reflection/Shoot-Through Transient ---
# This is the original signal, scaled and delayed.
# np.interp is used to handle the time shift (shift V_ideal to the right by t_delay)
# V_reflection(t) is V_ideal(t - t_d)
V_reflection = R_reflection * np.interp(t, t - t_delay, V_ideal, left=0, right=0)


# --- 4. Combine Final Signal with Noise (like in your provided code) ---
V_noise = np.random.normal(0, 0.1, N_samples) # Smaller noise for clarity
V_simulated = V_final + V_reflection + V_noise

# At this point, you would use a plotting library (like matplotlib) to plot t vs V_simulated
# The resulting plot would show a noisy square wave with:
# 1. An initial overshoot and decaying high-frequency oscillation (ringing) at each edge.
# 2. A smaller step (reflection) following the main edge by t_delay.


# --- 1. Generate large, noisy signal data ---

signal = V_simulated

# Ensure output directory for saved images
out_dir = os.path.join(os.getcwd(), "outputs")
os.makedirs(out_dir, exist_ok=True)

# Save a simple "original" time-series plot (downsampled for performance)
ds = max(1, N_samples // 2000)
t_ds = t[::ds]
signal_ds = signal[::ds]
fig_orig, ax_orig = plt.subplots(figsize=(12, 6))
ax_orig.plot(t_ds, signal_ds, color='cyan', linewidth=0.6)
ax_orig.set_xlabel("Time (s)")
ax_orig.set_ylabel("Signal Amplitude")
ax_orig.set_title("Original Time-Series Signal (downsampled)")
ax_orig.grid(True, alpha=0.5, linestyle=':')
orig_path = os.path.join(out_dir, "original_signal.png")
fig_orig.savefig(orig_path, dpi=200, bbox_inches='tight')
plt.close(fig_orig)

# --- 2. Configuration & Binning Statistics (Same as before) ---
N_BINS_X = 5000

x_bins_1d = np.linspace(t.min(), t.max(), N_BINS_X)
bin_centers = (x_bins_1d[:-1] + x_bins_1d[1:]) / 2.0

binned_means = []
binned_mins = []
binned_maxs = []
binned_stds = []

for i in range(N_BINS_X - 1):
    mask = (t >= x_bins_1d[i]) & (t < x_bins_1d[i+1])
    if np.any(mask):
        y_values = signal[mask]
        binned_means.append(np.mean(y_values))
        binned_mins.append(np.min(y_values))
        binned_maxs.append(np.max(y_values))
        binned_stds.append(np.std(y_values))
    else:
        binned_means.append(np.nan)
        binned_mins.append(np.nan)
        binned_maxs.append(np.nan)
        binned_stds.append(np.nan)

# Convert lists to arrays for easier math
binned_means = np.array(binned_means)
binned_mins = np.array(binned_mins)
binned_maxs = np.array(binned_maxs)
binned_stds = np.array(binned_stds)

# --- 3. Normal Color LUT Curve Function (Gaussian Alpha Mapping) ---

def normal_alpha_lut(y_value, mean, std_dev):
    """
    Calculates the alpha/intensity using a Gaussian PDF centered at the mean.
    More frequent values near the mean get higher alpha.
    """
    # Vectorized version: accepts scalars or numpy arrays for `y_value`.
    y = np.asarray(y_value)
    if np.isnan(mean) or std_dev == 0:
        return np.zeros_like(y, dtype=float)

    exponent = -0.5 * ((y - mean) / std_dev) ** 2
    intensity = np.exp(exponent)
    return np.clip(intensity, 0.0, 1.0)


# --- 4 & 5. Render bins directly onto a pixel grid (RGBA image) ---
plt.style.use('dark_background')
fig, ax = plt.subplots(figsize=(12, 6))

# Image pixel dimensions: width = number of bins, height = vertical pixels
H_PIXELS = 1000
W_PIXELS = len(bin_centers)

# Build vertical pixel centers that map to the plotted Y range
y_min = signal.min() - 0.5
y_max = signal.max() + 0.5
y_pixels = np.linspace(y_min, y_max, H_PIXELS)

# Prepare an empty RGBA image (H, W, 4)
img = np.zeros((H_PIXELS, W_PIXELS, 4), dtype=float)

# Chosen color for bins (yellow) as RGB; alpha will be per-pixel
bin_rgb = np.array([1.0, 1.0, 0.0])

for i in range(W_PIXELS):
    mean = binned_means[i]
    stddev = binned_stds[i]
    if np.isnan(mean) or stddev == 0:
        continue

    alphas = normal_alpha_lut(y_pixels, mean, stddev + 1e-6)
    # Assign RGB and alpha to the i-th column (vertical)
    img[:, i, :3] = bin_rgb
    img[:, i, 3] = alphas

# --- Horizontal smoothing across neighboring bins to reduce staircasing ---
# Sigma is in bin units; try 1.0 for gentle smoothing, increase for wider blending.
smoothing_sigma_bins = 2.0
if smoothing_sigma_bins > 0:
    # gaussian_filter1d works with pixels; our axis=1 is W_PIXELS (bins)
    img[:, :, 3] = gaussian_filter1d(img[:, :, 3], sigma=smoothing_sigma_bins, axis=1, mode='constant')

# Save an anti-aliased / smoothed pixel-grid as well
pixel_aa_path = os.path.join(out_dir, "pixel_grid_aa.png")
img_to_save_aa = np.flipud(img)
mimage.imsave(pixel_aa_path, img_to_save_aa)


# Display image aligned to axes; `interpolation='nearest'` keeps pixel alignment
ax.imshow(img, origin='lower', extent=(t.min(), t.max(), y_min, y_max),
          aspect='auto', interpolation='nearest')

# Save the pixel-grid image to disk before showing (pixel-perfect)
pixel_path = os.path.join(out_dir, "pixel_grid.png")
# `img` is float in [0,1], shape (H_PIXELS, W_PIXELS, 4). To save a pixel-perfect PNG
# write the array directly using `imsave`. Flip vertically to match `origin='lower'`.
img_to_save = np.flipud(img)
# Ensure values in [0,1] remain as floats for imsave
mimage.imsave(pixel_path, img_to_save)

# --- 6. Final Plot Setup ---
ax.set_xlim(t.min(), t.max())
ax.set_ylim(signal.min() - 0.5, signal.max() + 0.5)
ax.set_xlabel("Time (s)")
ax.set_ylabel("Signal Amplitude")
ax.set_title("Advanced Binned Gradient Rendering with Normal LUT")
ax.grid(True, alpha=0.5, linestyle=':')
plt.show()