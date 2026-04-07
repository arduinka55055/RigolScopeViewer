# Rigol Scope Viewer — AI Development Instructions

## Quick Start

**Project**: Oscilloscope waveform viewer for Rigol digital scopes  
**Framework**: Avalonia (WPF-like MVVM for cross-platform desktop)  
**Runtime**: .NET 8.0 (C# 12)  
**Build**: `dotnet build RigolScopeViewer.sln`  
**Run**: `dotnet run --project RigolScopeViewer.Desktop`

## Project Architecture

### Structure
```
RigolScopeViewer/          # Core library (.NET 8.0 Class Library)
├── Models/                 # Data models: Waveform, ScopeSettings
├── ViewModels/             # MVVM binding layer (MainViewModel, ChannelViewModel)
├── Views/                  # Avalonia XAML UI components
└── Services/               # File loaders (CSV, Rigol binary format)

RigolScopeViewer.Desktop/  # WinExe entry point
└── Program.cs              # AppBuilder configuration
```

### Key Design Patterns

**MVVM Architecture**
- **Models**: Immutable domain objects (`Waveform`, `ScopeSettings`)
- **ViewModels**: Inherit from `ViewModelBase`, expose `INotifyPropertyChanged` and `ICommand`
- **Views**: XAML controls bind to ViewModels; no code-behind logic (except event handlers)

**Waveform Rendering Optimization**
- **Mipmaps**: Multi-level downsampling stored in `Waveform.Mipmaps` (list of min/max pairs)
- **Mipmap Selection**: `OscilloscopeControl.SelectMipmap()` chooses appropriate level based on pixel resolution
- **Benefits**: Efficient rendering regardless of zoom level (prevents oversampling/aliasing)

**File Format Support**
- **CSV**: Simple time-value columns (first column = time, rest = channels)
- **Rigol Binary**: Custom format with header metadata, supports analog/digital data per channel
- **Extensibility**: Implement `IWaveformLoader` interface to add formats

## Development Guidelines

### Naming & Conventions
- **Namespaces**: `RigolScopeViewer.*` (no .Desktop in library classes)
- **Classes**: PascalCase, suffixed with type (`ViewModel`, `Loader`, `Control`)
- **Properties**: PascalCase with backing fields (`_privateField`)
- **MVVM Pattern**: ViewModel properties trigger `OnPropertyChanged()` for UI updates
- **Enums**: PascalCase members (e.g., `WaveformType.Analog`, `IncrementType.Logarithmic`)

### Window/Control Lifecycle
1. XAML initialization via `InitializeComponent()` (auto-generated)
2. DataContext assignment in code-behind constructor
3. Property bindings activate INotifyPropertyChanged chain
4. Resource cleanup: Dispose patterns for streams, SkiaSharp surfaces

### Color Scheme
- **Auto-assigned by channel index**: Yellow (CH1), Cyan (CH2), Magenta (CH3), Blue (CH4), Lime, Orange
- Location: `RigolBinLoader.GetChannelColor()` and `CsvLoader.GetChannelColor()`

### UI Controls
- **OscilloscopeControl**: Custom Skia-based rendering (analog/digital waveforms, triggers, cursors)
- **UnitNumericUpDown**: Logarithmic or linear stepping for time/voltage scales
- **RelayCommand**: MVVM Toolkit command wrapper for Open, ZoomIn, ZoomOut

## Common Tasks

### Adding a New File Format Loader
1. Create `Services/MyFormatLoader.cs` inheriting `IWaveformLoader`
2. Implement `Load(string fileName) → List<Waveform>`
3. Return list of `Waveform` objects with `TimeData`, `AnalogData`, and `Color`
4. Register in `MainViewModel.OpenFile()` switch statement

### Modifying Waveform Rendering
- **Rendering logic**: `OscilloscopeControl.OnRender()` (Skia-based, not Avalonia canvas)
- **Mipmap calculation**: `Waveform.BuildMipmaps()` (called on first zoom)
- **Performance tip**: Avoid rebuilding mipmaps; cache result in `Waveform.Mipmaps`

### Adding Channel Controls
- Add property to `ChannelViewModel` (triggers `_updateCallback` for redraw)
- Bind in XAML under channel items panel
- Call `MainViewModel.UpdateWaveforms()` on property change

## Common Pitfalls

1. **Mipmap cache invalidation**: If `Scale` or `VoltageOffset` changes, cached mipmaps become invalid
   - *Solution*: Clear `Waveform.Mipmaps` when model data changes
2. **Namespace mismatch**: Some files incorrectly use `OscilloscopeViewer.Models` instead of `RigolScopeViewer.*`
   - *Fix*: Correct namespace declarations; avoid mixing
3. **Thread safety**: File dialogs must run on UI thread; use `async/await` properly
4. **Avalonia version**: Locked to 11.3.2; breaking changes in 12.x may require migration

## Testing & Debugging

**Demo Data**
- MainViewModel constructor creates demo sine waves (CH1: 1kHz, CH2: 500Hz) for testing
- Load CSV/binary files via File > Open menu

**Debug Visualization**
- `Debug.AttachDevTools()` in MainWindow.cs enabled for DEBUG builds
- Avalonia DevTools: Inspect visual tree, probe property values

**Build Configurations**
- **Debug**: Debugging symbols, DevTools enabled, validation active
- **Release**: Optimized binaries, published to `RigolScopeViewer.Desktop/bin/Release/net8.0/publish/`

## Key Files Reference

| File | Purpose |
|------|---------|
| [RigolScopeViewer.Desktop/Program.cs](../RigolScopeViewer.Desktop/Program.cs) | Entry point; Avalonia AppBuilder config |
| [RigolScopeViewer/Views/OscilloscopeControl.axaml.cs](../RigolScopeViewer/Views/OscilloscopeControl.axaml.cs) | Rendering engine (Skia-based waveform drawing) |
| [RigolScopeViewer/Models/Waveform.cs](../RigolScopeViewer/Models/Waveform.cs) | Waveform data + mipmap generation |
| [RigolScopeViewer/ViewModels/MainViewModel.cs](../RigolScopeViewer/ViewModels/MainViewModel.cs) | Application state, file loading, channel management |
| [RigolScopeViewer/Services/RigolBinLoader.cs](../RigolScopeViewer/Services/RigolBinLoader.cs) | Rigol binary format parser |
| [RigolScopeViewer/Services/CsvLoader.cs](../RigolScopeViewer/Services/CsvLoader.cs) | CSV format loader |

## IDE Setup

- **Editor**: VS Code with C# Dev Kit (Omnisharp-based IntelliSense)
- **Build**: CTRL+SHIFT+B → Run `dotnet build` task  
- **Debug**: F5 → Launches RigolScopeViewer.Desktop with debugger
- **Restore**: `dotnet restore` if NuGet packages missing

## Resources

- [Avalonia Documentation](https://docs.avaloniaui.net/) — Cross-platform UI framework
- [MVVM Toolkit](https://learn.microsoft.com/en-us/windows/communitytoolkit/mvvm/mvvm_introduction) — INotifyPropertyChanged helpers
- [SkiaSharp](https://github.com/mono/SkiaSharp) — Hardware-accelerated graphics backend
- [Rigol DS1000Z Documentation](https://www.rigolna.com/) — Oscilloscope binary format reference

---

**Last Updated**: 2026-04-06  
**Maintainers**: Rigol Scope Viewer Team
