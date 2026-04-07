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
f_ring = 200e6     # 100 MHz ringing frequency
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
    # Vectorized version: accepts scalars or numpy arrays for `y_value`, `mean`, `std_dev`.
    y = np.asarray(y_value)
    mean_a = np.asarray(mean)
    std_a = np.asarray(std_dev)

    # Avoid division by zero: mark zero/negative std as invalid
    std_safe = np.where(std_a == 0, np.nan, std_a)

    # Compute exponent with broadcasting; result shape will broadcast to (y, mean)
    with np.errstate(invalid='ignore', divide='ignore'):
        exponent = -0.5 * ((y - mean_a) / std_safe) ** 2
        intensity = np.exp(exponent)

    # Where mean is NaN or std was zero, set intensity to 0
    invalid_mask = np.isnan(mean_a) | (std_a == 0)
    if np.any(invalid_mask):
        # Broadcast mask to intensity shape
        try:
            mask_b = np.broadcast_to(invalid_mask, intensity.shape)
            intensity = np.where(mask_b, 0.0, intensity)
        except ValueError:
            # fallback: if shapes don't align, set all to zero where invalid_mask is True
            if np.all(invalid_mask):
                return np.zeros_like(intensity, dtype=float)

    # Clip to [0,1]
    intensity = np.clip(intensity, 0.0, 1.0)
    return intensity

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

# Parameters
SS = 1                    # supersampling factor
H_PIXELS = 400             # final height
W_PIXELS = len(bin_centers)
Hs = H_PIXELS * SS
Ws = W_PIXELS * SS

# high-res vertical coordinates
y_pixels_high = np.linspace(y_min, y_max, Hs)

# create high-res columns positions in bin units (float bin index)
# column_bin_pos ranges from 0..W_PIXELS-1 across Ws columns
column_bin_pos = (np.linspace(0, W_PIXELS, Ws, endpoint=False) + 0.5/SS)  # position in bin space

# neighbor kernel parameters
sigma_bins = 0.9          # horizontal smoothing width in bin units
truncate = 3.0            # truncate kernel at ±(truncate * sigma_bins) bins
k_width = int(np.ceil(truncate * sigma_bins))

# Precompute small set of bin indices relative offsets: -k_width..+k_width
offsets = np.arange(-k_width, k_width+1)

# Prepare high-res alpha array
alpha_high = np.zeros((Hs, Ws), dtype=float)

# For each offset compute contribution from bins shifted by that offset
# We'll vectorize by computing kernel weights for all columns and the small offset array
# Build bin center positions in bin index units (0..W-1)
bin_idx = np.arange(W_PIXELS)

# For each column compute which bins (by absolute index) fall into offset window
# We'll loop over offsets (small) instead of bins (large) to limit memory
for d in offsets:
    # For a column at position p (in bin units), the corresponding bin index contributing is round(p - d)
    # We compute weights = exp(-0.5 * ((p - (k))/sigma)^2) where k runs over actual bins,
    # but we approximate by taking k = floor(p) + d, i.e. local neighbor contributions.
    # A robust approach: compute distances from each column to all bins, but that's large.
    # Simpler: for each column p, consider bin k = floor(p) + d (single-bin contribution).
    k = (np.floor(column_bin_pos).astype(int) + d).clip(0, W_PIXELS-1)   # shape (Ws,)
    # horizontal weight (Gaussian) based on distance from column to that bin center
    dx = column_bin_pos - k
    wx = np.exp(-0.5 * (dx / sigma_bins)**2)
    # normalize per-column later (we'll accumulate then divide)
    # get means/stds for the selected bins (vector by columns)
    means_k = binned_means[k]          # shape (Ws,)
    stds_k = binned_stds[k] + 1e-9
    # compute vertical gaussian for all y pixels and broadcast: shape (Hs, Ws)
    # normal_alpha_lut(y_pixels_high[:,None], means_k[None,:], stds_k[None,:]) -> (Hs, Ws)
    contrib = normal_alpha_lut(y_pixels_high[:, None], means_k[None, :], stds_k[None, :]) * wx[None, :]
    alpha_high += contrib
# normalize horizontally so max stays near 1 (or normalize by sum of weights)
# compute normalization weights sum (per column) same procedure:
weight_sum = np.zeros((Ws,), dtype=float)
for d in offsets:
    k = (np.floor(column_bin_pos).astype(int) + d).clip(0, W_PIXELS-1)
    dx = column_bin_pos - k
    wx = np.exp(-0.5 * (dx / sigma_bins)**2)
    weight_sum += wx
alpha_high /= (weight_sum[None, :] + 1e-12)

# Now build high-res RGBA and downsample by block-averaging (if SS>1)
img_high = np.zeros((Hs, Ws, 4), dtype=float)
img_high[:, :, :3] = bin_rgb  # RGB
img_high[:, :, 3] = alpha_high

# Downsample to final HxW by averaging SS×SS blocks:
img = img_high.reshape(H_PIXELS, SS, W_PIXELS, SS, 4).mean(axis=(1, 3))

plt.show()