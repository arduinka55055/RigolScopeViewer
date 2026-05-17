# RigolScopeViewer - Agent Instructions

RigolScopeViewer is a high-performance oscilloscope waveform viewer for Rigol instruments, built with .NET, Avalonia, and GPU-accelerated rendering.

## Quick Reference

| Item | Value |
|------|-------|
| **Language** | C# 12 |
| **Framework** | .NET 10.0 |
| **Target Platforms** | Windows, macOS, Linux (Cross-platform) |
| **Solution Structure** | 2 projects: Core library + Desktop app |
| **Build Command** | `dotnet build RigolScopeViewer.sln` |
| **Dev Watch Mode** | `dotnet watch run --project RigolScopeViewer.Desktop/RigolScopeViewer.Desktop.csproj` |
| **UI Framework** | Avalonia 11.3.2 with Skia rendering |
| **Pattern** | MVVM with CommunityToolkit.Mvvm, DI via MS.Extensions.DependencyInjection |

## Architecture

### Layer Stack
```text
┌─────────────────────────────────┐
│  Desktop App (Avalonia UI/MVVM) │
├─────────────────────────────────┤
│  Rendering (Skia + GPU Shaders) │
├─────────────────────────────────┤
│  Data Pipeline (Task.Run + TPL) │
├─────────────────────────────────┤
│  PluginManager (Dyn. DLL Load)  │
├─────────────────────────────────┤
│  Data Sources (Built-in + .dll) │
├─────────────────────────────────┤
│  Contracts (Shared Interfaces)  │
└─────────────────────────────────┘

```

### Data Flow: File → Render

```
1. User opens file → MainViewModel.OpenFile()
   ↓
2. Factory selects IWaveformSource (RigolBinSource or CsvWaveformSource)
   ↓
3. Source.SetupNeeded? → RunSetupAsync() (Avalonia Wizard)
   ↓
4. User pans/zooms → MainViewModel.RequestNewFrameAsync()
   ↓
5. [Background Thread] Task.Run → Source.ProcessChannelData()
   ↓
6. [Background Thread] OscilloscopePipeline.ProcessFrame() 
   └─ DpoBinningEngine.Resample() → Rents ArrayPool<ColumnStats>
   └─ Returns RenderFrame (IDisposable)
   ↓
7. [UI Thread] DpoDrawOperation.Render() 
   └─ RenderFrame.GetValidSpan().CopyTo(SKBitmap.GetPixelSpan()) → Instant GPU Upload
   └─ RenderFrame.Dispose() → Returns memory to ArrayPool
```

### Core Interfaces (extend these to add features)

| Interface | Location | Purpose |
|-----------|----------|---------|
| `IWaveformSource` | `Interfaces/IWaveformSource.cs` | Load multi-channel data from file format |
| `IResampler<T>` | `Interfaces/IResampler.cs` | Generic binning/resampling (T: unmanaged) |
| `IConfigManager` | `Interfaces/IConfigManager.cs` | JSON config persistence in AppData |

## Key Conventions

### 1. Performance-Critical Path: Zero-Copy Span<T>
- **Never** use `float[]` in hot paths; use `ReadOnlySpan<float>` / `Span<T>`
- **Always** use `ArrayPool<T>` for reusable buffers → return via `RenderFrame.Dispose()`
- **Use** `stackalloc` for temp buffers < 1KB
- Profile with `ILogger<T>` already available (injected via DI)

**Example**:
```csharp
// ❌ Don't do this
public ColumnStats[] ProcessData(float[] data) { ... }

// ✅ Do this
public void Resample(ReadOnlySpan<float> source, Span<ColumnStats> destination) { ... }
```

### 2. MVVM & Observability
- View models inherit `ViewModelBase` (Reactive command support)
- **Observable properties** use `[ObservableProperty]` attribute (MVVM Toolkit)
- State changes → call `RequestNewFrameAsync()` to trigger re-render

### 3. Dependency Injection
- Register services in `DependencyInjection.AddRigolScopeViewerServices()`
- Prefer constructor injection; avoid `ServiceLocator` pattern
- All services registered as **Singleton** (assumes thread-safe immutable services)

### 4. GPU Rendering via Skia
- Shaders are embedded GLSL files in `Shaders/` (loaded at app startup)
- No immediate-mode drawing; use `DrawOperation` pattern
- Viewport transformations (pan/zoom) passed as uniforms to shader

### 5. Data Loading: IWaveformSource Extension
Create new format support:
1. Inherit `IWaveformSource`
2. Implement: `RunSetupAsync()` (parse metadata), `ProcessChannelData()` (load data)
3. Return `float[][]` (channels × sample points)
4. Register factory in `MainViewModel.OpenFile()`

## Common Development Tasks

### Add a New Data Format Loader

**File**: `RigolScopeViewer/Sources/YourFormat/Source.cs`

```csharp
public class YourFormatSource : IWaveformSource
{
    public async Task RunSetupAsync(string filePath, IProgress<string>? progress = null)
    {
        // Parse file header, extract metadata
        // Populate Channels, WaveformMetadata
    }
    
    public async ValueTask<float[]> ProcessChannelData(
        int channelIndex, 
        float startTime, 
        float endTime,
        IResampler<ColumnStats>? processor = null)
    {
        // Load channel data for time range
        // Call processor.Resample() if provided
        return data;
    }
}
```

Register in `MainViewModel`:
```csharp
private IWaveformSource GetSourceForFile(string path)
{
    return Path.GetExtension(path) switch
    {
        ".yourformat" => new YourFormatSource(),
        _ => throw new NotSupportedException()
    };
}
```

### Optimize Rendering Performance

**Identify bottleneck**:
1. Resample too slow? → Optimize `DpoBinningEngine.Resample()`
2. GPU too slow? → Check `DpoShader.glsl` or GPU texture upload
3. File load too slow? → Profile `IWaveformSource.ProcessChannelData()`

**GPU Compute Migration** (TODO):
- File: `RigolScopeViewer/Services/Samplers/DpoBinningEngineComputeGPU.cs`
- Goal: Move resampling loop to GPU via ComputeSharp
- Strategy: Parallelize per-bin statistics across GPU threads

### Add UI Controls or Settings

**Observable Property**:
```csharp
[ObservableProperty]
private double zoomFactor = 1.0;
```

**Bind in XAML**:
```xml
<TextBlock Text="{Binding ZoomFactor}" />
```

**Trigger re-render**:
```csharp
partial void OnZoomFactorChanged(double value)
{
    RequestNewFrameAsync();
}
```

### Save/Load Configuration

**Define config class** (JSON-serializable):
```csharp
public class ScopeConfig
{
    public double TimePerDivision { get; set; }
    public string? LastOpenedFile { get; set; }
}
```

**Use ConfigManager**:
```csharp
var config = _configManager.Load<ScopeConfig>("scope_config.json");
// ... modify ...
_configManager.Save("scope_config.json", config);
```

Location: `%APPDATA%/RigolScopeViewer/`

## Development Workflow

### Setup & Build
```powershell
# Restore NuGet packages
dotnet restore

# Full build (Debug)
dotnet build RigolScopeViewer.sln

# Full build (Release)
dotnet build -c Release RigolScopeViewer.sln
```

### Debug & Development
```powershell
# Watch mode (recommended for dev)
dotnet watch run --project RigolScopeViewer.Desktop/RigolScopeViewer.Desktop.csproj

# Release publish (self-contained)
dotnet publish RigolScopeViewer.Desktop -c Release -p:PublishProfile=FolderProfile
```

## Code Style

- **Nullability**: `#nullable enable` (enforced project-wide)
- **Indentation**: 4 spaces (enforced via .editorconfig)
- **Encoding**: UTF-8 with BOM
- **Naming**: PascalCase (classes, methods), camelCase (fields, locals)
- **Using Statements**: Sort alphabetically (Roslyn default)

## Performance Hotspots & Considerations

| Component | Concern | Strategy |
|-----------|---------|----------|
| DpoBinningEngine | Resampling millions of samples | Span<T>, stackalloc, ArrayPool |
| GPU Shader | Fragment math per pixel | Precompute mean/stddev in CPU |
| File Load | Parsing large .bin files | Async/await, progress reporting |
| Viewport Change | Panning/zooming lag | Lock step on RequestNewFrameAsync() |

## Current State & TODOs

**Mature**:
- ✅ Rigol .bin format loader
- ✅ CSV loader with configurable parsing
- ✅ CPU resampling pipeline (zero-copy)
- ✅ Skia/GLSL GPU rendering
- ✅ MVVM + DI architecture

**In Progress / TODO**:
- 🔄 CSV import wizard UI (Avalonia dialog)
- 🔄 GPU compute resampling (ComputeSharp integration)
- ⚠️ Float vs. Double precision trade-off study
- ⚠️ SharedMemoryReader (interface exists, stub only)

## Key Files Reference

**Entry Point & Infrastructure**:
- [Program.cs](RigolScopeViewer.Desktop/Program.cs) – Startup, DI setup
- [Services/DependencyInjection.cs](RigolScopeViewer/Services/DependencyInjection.cs) – Service registration

**Core Business Logic**:
- [ViewModels/MainViewModel.cs](RigolScopeViewer/ViewModels/MainViewModel.cs) – Orchestration
- [Services/OscilloscopePipeline.cs](RigolScopeViewer/Services/OscilloscopePipeline.cs) – Data flow
- [Services/Samplers/DpoBinningEngine.cs](RigolScopeViewer/Services/Samplers/DpoBinningEngine.cs) – Performance-critical resampling

**Data Sources**:
- [Interfaces/IWaveformSource.cs](RigolScopeViewer/Interfaces/IWaveformSource.cs) – Pluggable loader interface
- [Sources/RigolBinSource.cs](RigolScopeViewer/Sources/RigolBinSource.cs) – Binary format
- [Sources/CSV/Source.cs](RigolScopeViewer/Sources/CSV/Source.cs) – CSV format

**Rendering**:
- [Rendering/DPODrawOperation.cs](RigolScopeViewer/Rendering/DPODrawOperation.cs) – GPU rendering operations
- [Shaders/DpoShader.glsl](RigolScopeViewer/Shaders/DpoShader.glsl) – Fragment shader (Gaussian rendering)
- [Views/OscilloscopeControl.axaml.cs](RigolScopeViewer/Views/OscilloscopeControl.axaml.cs) – Control logic

## Gotchas & Common Issues

1. **Span<T> across threads**: Span<T> cannot safely cross thread boundaries. If data needs threading, use array + `RenderFrame.IDisposable` pooling.
2. **GPU shader recompilation**: Shaders are embedded; changes require app restart. Profile with test harness if iterating on shader.
3. **ArrayPool double-return**: If returning RenderFrame from async, ensure caller disposes to return buffers.
4. **Observable property side effects**: Setting `ZoomFactor` triggers `OnZoomFactorChanged()` which calls async `RequestNewFrameAsync()`. Avoid re-entrancy.
5. **CSV config persistence**: CsvWaveformSource config lives in user's AppData; clearing cache loses user's column mappings.

---

**Last Updated**: May 2026  
**Maintainers**: Agent instructions for RigolScopeViewer development
