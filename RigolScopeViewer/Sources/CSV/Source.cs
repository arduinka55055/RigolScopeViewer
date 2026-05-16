using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
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

    // Всі дані лежать тут. 1-й вимір - канал, 2-й вимір - точки
    private float[][]? _channelData;
    private WaveformMetadata[]? _metadata;

    public event EventHandler? DataReady;
    public int ChannelCount => _channelData?.Length ?? 0;

    public static bool SetupNeeded => true;

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

        // Після налаштування парсимо файл
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

        // Використовуємо списки як тимчасові буфери під час читання
        var tempBuffers = new List<List<float>>();
        var channelNames = new List<string>();

        using var reader = new StreamReader(_filePath);

        var isFirstRow = true;
        var firstTime = 0f;
        var secondTime = 0f;
        var rowIndex = 0;

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Швидкий спліт (швидше ніж LINQ)
            var parts = line.Split(',');

            if (isFirstRow && _config.HasHeaderRow)
            {
                var startIdx = _config.Mode == CsvImportMode.Timestamped ? 1 : 0;
                for (var i = startIdx; i < parts.Length; i++)
                {
                    channelNames.Add(parts[i].Trim());
                    tempBuffers.Add(new List<float>(100_000)); // Зарезервуємо пам'ять
                }
                isFirstRow = false;
                continue;
            }

            // Якщо хедерів немає, створюємо дефолтні імена при першому рядку
            if (isFirstRow && !_config.HasHeaderRow)
            {
                var startIdx = _config.Mode == CsvImportMode.Timestamped ? 1 : 0;
                for (var i = startIdx; i < parts.Length; i++)
                {
                    channelNames.Add($"Channel {i - startIdx + 1}");
                    tempBuffers.Add(new List<float>(100_000));
                }
            }

            var dataOffset = 0;

            if (_config.Mode == CsvImportMode.Timestamped)
            {
                // Парсимо час лише для перших двох рядків, щоб знайти SampleInterval
                if (rowIndex == 0 && float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var t1))
                    firstTime = t1;
                else if (rowIndex == 1 && float.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var t2))
                    secondTime = t2;

                dataOffset = 1;
            }

            // Парсимо напруги для всіх каналів
            for (var i = 0; i < tempBuffers.Count; i++)
            {
                if (i + dataOffset < parts.Length &&
                    float.TryParse(parts[i + dataOffset], NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                {
                    tempBuffers[i].Add(val);
                }
                else
                {
                    tempBuffers[i].Add(0f); // Fallback для битих рядків
                }
            }

            rowIndex++;
            isFirstRow = false;
        }

        // --- Перетворення тимчасових списків у швидкі масиви ---
        _channelData = new float[tempBuffers.Count][];
        _metadata = new WaveformMetadata[tempBuffers.Count];

        var calculatedInterval = _config.Mode == CsvImportMode.Timestamped && rowIndex > 1
            ? (secondTime - firstTime)
            : _config.ManualSampleInterval;

        if (calculatedInterval <= 0) calculatedInterval = 1e-6f; // Захист від ділення на нуль

        for (var i = 0; i < tempBuffers.Count; i++)
        {
            _channelData[i] = tempBuffers[i].ToArray(); // Конвертуємо в безперервний масив

            _metadata[i] = new WaveformMetadata
            {
                StartTime = firstTime,
                SampleInterval = calculatedInterval,
                TotalPoints = _channelData[i].Length,
                ChannelName = channelNames[i]
            };

            // Звільняємо пам'ять списку
            tempBuffers[i] = null;
        }
    }

    public WaveformMetadata GetMetadata(int channelIndex) => _metadata[channelIndex];

    public void ProcessChannelData(int channelIndex, TimeRange timeRange, DataProcessor processor, System.Threading.CancellationToken cancellationToken = default)
    {
        if (_channelData == null || channelIndex < 0 || channelIndex >= ChannelCount) return;

        var meta = _metadata[channelIndex];
        var data = _channelData[channelIndex];

        // Конвертуємо час у індекси масиву
        var startIndex = (int)((timeRange.Start - meta.StartTime) / meta.SampleInterval);
        var endIndex = (int)((timeRange.End - meta.StartTime) / meta.SampleInterval);

        startIndex = Math.Clamp(startIndex, 0, data.Length);
        endIndex = Math.Clamp(endIndex, startIndex, data.Length);

        // Магія Zero-Allocation! Віддаємо лише шматочок масиву через Span.
        ReadOnlySpan<float> slice = data.AsSpan();//(startIndex, endIndex - startIndex);

        processor(slice, meta, cancellationToken);
    }

    public void Start()
    {
        if (_channelData != null)
        {
            DataReady?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop() { }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _channelData = null;
        _metadata = null;
    }
}
