using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RigolScopeViewer.Interfaces;
using RigolScopeViewer.Models;
using Microsoft.Extensions.Logging;

namespace RigolScopeViewer.Sources.CSV;

public class CsvWaveformSource : IWaveformSource
{
    private readonly string _filePath;
    private readonly IConfigManager _configManager;
    private readonly ILogger<CsvWaveformSource>? _logger;
    private CsvSourceConfig _config;

    private float[][]? _channelData;
    private WaveformMetadata[]? _metadata;

    public event EventHandler? DataReady;
    public int ChannelCount => _channelData?.Length ?? 0;

    public static bool SetupNeeded => true;

    public object? ControlPanelViewModel => null;

    public CsvWaveformSource(string filePath, IConfigManager configManager, ILogger<CsvWaveformSource>? logger = null)
    {
        _filePath = filePath;
        _configManager = configManager;
        _logger = logger;
        _config = _configManager.Load<CsvSourceConfig>("csv_config.json");
        _logger?.LogInformation("CsvWaveformSource initialized for file: {FilePath}", filePath);
    }

    public async Task<bool> RunSetupAsync()
    {
        var appLifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        var mainWindow = appLifetime?.MainWindow;

        var vm = new RigolScopeViewer.ViewModels.SetupWizardViewModel
        {
            ConfigObject = _config,
            CurrentFilePath = _filePath
        };

        // Підключаємо прев'ю CSV файлу (Head + Tail)
        vm.PreviewContent = PreviewerFactory.CreateCsvPreviewer(_filePath, (mode, interval) =>
        {
            // Оновлюємо Mode (Timestamped або IntervalBased)
            var modeProp = vm.Properties.FirstOrDefault(p => p.Name == nameof(CsvSourceConfig.Mode));
            if (modeProp != null)
            {
                modeProp.Value = mode;
            }

            // Якщо ми знайшли tInc в файлі, оновлюємо ManualSampleInterval
            if (interval.HasValue)
            {
                var intervalProp = vm.Properties.FirstOrDefault(p => p.Name == nameof(CsvSourceConfig.ManualSampleInterval));
                if (intervalProp != null)
                {
                    intervalProp.Value = interval.Value;
                }
            }

            _logger?.LogInformation("Auto-filled CSV config: Mode={Mode}, Interval={Interval}", mode, interval);
        });

        var dialog = new RigolScopeViewer.Views.SetupWizardWindow
        {
            DataContext = vm
        };

        bool result = false;
        if (mainWindow != null)
        {
            result = await dialog.ShowDialog<bool>(mainWindow);
        }
        else
        {
            dialog.Show();
            result = true;
        }

        if (!result) return false;

        _configManager.Save(_config, "csv_config.json");
        _logger?.LogDebug("CSV config saved");

        ParseFile();
        return true;
    }

    private void ParseFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogWarning("CSV file not found: {FilePath}", _filePath);
            return;
        }

        _logger?.LogDebug("Parsing CSV file: {FilePath}", _filePath);

        // Визначаємо кількість рядків, щоб відразу алокувати масиви без List<float>
        int totalRows = CountLines(_filePath) - 1; // -1 для хедера
        if (totalRows <= 0) return;

        var channelNames = new List<string>();

        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        using var reader = new StreamReader(fs);

        var firstLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(firstLine)) return;

        bool isIntervalFormat = false;
        bool isTimestampFormat = false;
        int dataStartIndex = 0;

        float t0 = 0f;
        float tInc = 0f;

        // --- 1. Аналіз першого рядка (алокації тут не страшні, бо це лише 1 раз) ---
        var headerParts = firstLine.Split(',');

        var t0Part = Array.Find(headerParts, p => p.Trim().StartsWith("t0", StringComparison.OrdinalIgnoreCase));
        var tIncPart = Array.Find(headerParts, p => p.Trim().StartsWith("tInc", StringComparison.OrdinalIgnoreCase));

        if (t0Part != null && tIncPart != null)
        {
            isIntervalFormat = true;
            dataStartIndex = 0;

            float.TryParse(t0Part.Split('=')[1], NumberStyles.Any, CultureInfo.InvariantCulture, out t0);
            float.TryParse(tIncPart.Split('=')[1], NumberStyles.Any, CultureInfo.InvariantCulture, out tInc);

            for (int i = 0; i < headerParts.Length; i++)
            {
                var colName = headerParts[i].Trim();
                if (colName.StartsWith("t0", StringComparison.OrdinalIgnoreCase)) break;
                if (!string.IsNullOrWhiteSpace(colName)) channelNames.Add(colName);
            }
        }
        else if (headerParts[0].Trim().StartsWith("Time", StringComparison.OrdinalIgnoreCase))
        {
            isTimestampFormat = true;
            dataStartIndex = 1;

            for (int i = 1; i < headerParts.Length; i++)
            {
                var colName = headerParts[i].Trim();
                if (!string.IsNullOrWhiteSpace(colName)) channelNames.Add(colName);
            }
        }
        else
        {
            isTimestampFormat = _config.Mode == CsvImportMode.Timestamped;
            dataStartIndex = isTimestampFormat ? 1 : 0;

            for (var i = dataStartIndex; i < headerParts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(headerParts[i]))
                    channelNames.Add($"Channel {i - dataStartIndex + 1}");
            }

            // Якщо це не хедер, нам треба врахувати цей рядок.
            // Щоб не ускладнювати Span-парсинг, просто закриваємо і відкриваємо файл наново.
            reader.BaseStream.Position = 0;
            reader.DiscardBufferedData();
            totalRows++; // Враховуємо перший рядок як дані
        }

        // --- 2. Алокація фінальних масивів (Zero-Allocation під час читання) ---
        int channelCount = channelNames.Count;
        _channelData = new float[channelCount][];
        for (int i = 0; i < channelCount; i++)
        {
            _channelData[i] = new float[totalRows];
        }

        // --- 3. Швидкий парсинг даних ---
        var firstTime = 0f;
        var secondTime = 0f;
        var rowIndex = 0;

        while (rowIndex < totalRows && !reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            ReadOnlySpan<char> lineSpan = line.AsSpan();
            int currentPartIdx = 0;
            int startIdx = 0;

            for (int i = 0; i <= lineSpan.Length; i++)
            {
                // Шукаємо кому або кінець рядка
                if (i == lineSpan.Length || lineSpan[i] == ',')
                {
                    var chunk = lineSpan.Slice(startIdx, i - startIdx).Trim();

                    // Якщо це мітка часу (Timestamp format)
                    if (isTimestampFormat && currentPartIdx == 0)
                    {
                        if (rowIndex == 0 && float.TryParse(chunk, NumberStyles.Any, CultureInfo.InvariantCulture, out var t1))
                            firstTime = t1;
                        else if (rowIndex == 1 && float.TryParse(chunk, NumberStyles.Any, CultureInfo.InvariantCulture, out var t2))
                            secondTime = t2;
                    }
                    else
                    {
                        // Парсимо значення каналу
                        int channelIdx = currentPartIdx - dataStartIndex;

                        if (channelIdx >= 0 && channelIdx < channelCount)
                        {
                            if (chunk.Length > 0 && float.TryParse(chunk, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                            {
                                _channelData[channelIdx][rowIndex] = val;
                            }
                            else
                            {
                                _channelData[channelIdx][rowIndex] = 0f;
                            }
                        }
                    }

                    currentPartIdx++;
                    startIdx = i + 1; // Рухаємося за кому
                }
            }

            rowIndex++;
        }

        // Якщо фактичних рядків виявилося менше ніж totalRows, обрізаємо масиви (рідкісний кейс, зазвичай через пусті рядки в кінці)
        if (rowIndex < totalRows)
        {
            for (int i = 0; i < channelCount; i++)
            {
                Array.Resize(ref _channelData[i], rowIndex);
            }
        }

        // --- 4. Формування метаданих ---
        _metadata = new WaveformMetadata[channelCount];

        float calculatedInterval = _config.ManualSampleInterval;
        float startTime = 0f;

        if (isIntervalFormat)
        {
            calculatedInterval = tInc;
            startTime = t0;
        }
        else if (isTimestampFormat && rowIndex > 1)
        {
            calculatedInterval = (secondTime - firstTime);
            startTime = firstTime;
        }

        if (calculatedInterval <= 0) calculatedInterval = 1e-6f;

        for (var i = 0; i < channelCount; i++)
        {
            _metadata[i] = new WaveformMetadata
            {
                StartTime = startTime,
                SampleInterval = calculatedInterval,
                TotalPoints = _channelData[i].Length,
                ChannelName = channelNames[i]
            };
        }
    }

    // Хелпер для швидкого підрахунку рядків у файлі (читаємо чанками байтів, а не стрінгів)
    private static int CountLines(string path)
    {
        int count = 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536);
        int byteRead;
        while ((byteRead = fs.ReadByte()) != -1)
        {
            if (byteRead == '\n') count++;
        }
        return count;
    }

    public WaveformMetadata GetMetadata(int channelIndex) => _metadata[channelIndex];

    public void ProcessChannelData(int channelIndex, TimeRange timeRange, DataProcessor processor, CancellationToken cancellationToken = default)
    {
        if (_channelData == null || channelIndex < 0 || channelIndex >= ChannelCount) return;

        var meta = _metadata[channelIndex];
        var data = _channelData[channelIndex];

        var startIndex = (int)((timeRange.Start - meta.StartTime) / meta.SampleInterval);
        var endIndex = (int)((timeRange.End - meta.StartTime) / meta.SampleInterval);

        startIndex = Math.Clamp(startIndex, 0, data.Length);
        endIndex = Math.Clamp(endIndex, startIndex, data.Length);

        // Zero-Allocation Slice
        ReadOnlySpan<float> slice = data.AsSpan(startIndex, endIndex - startIndex);
        processor(slice, meta, cancellationToken);
    }

    public void Start()
    {
        if (_channelData != null) DataReady?.Invoke(this, EventArgs.Empty);
    }

    public void Stop() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _channelData = null;
        _metadata = null;
    }
}
