using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;

namespace RigolScopeViewer.Sources.CSV;

public static partial class PreviewerFactory
{
    public static Control CreateCsvPreviewer(string filePath, Action<CsvImportMode, float?>? onFormatDetected = null)
    {
        var textBlock = new TextBlock
        {
            FontFamily = FontFamily.Parse("Consolas, Courier New, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Brushes.LightGray,
            Margin = new Avalonia.Thickness(5)
        };

        var scrollViewer = new ScrollViewer
        {
            Content = textBlock,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Height = 250,
            Background = new SolidColorBrush(Color.Parse("#1e1e1e")),
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Brushes.Gray,
            Margin = new Avalonia.Thickness(10)
        };

        var container = new StackPanel { Spacing = 10 };
        container.Children.Add(new TextBlock { Text = "CSV File Preview (Auto-detecting format...)", FontWeight = FontWeight.Bold });
        container.Children.Add(scrollViewer);

        // Читаємо файл у бекграунді, щоб не фрізити UI
        Task.Run(() =>
        {
            var result = GenerateCsvHeadTail(filePath, 8);

            // Повертаємось у UI-потік для безпечного оновлення контролів та виклику колбеку
            Dispatcher.UIThread.Post(() =>
            {
                textBlock.Text = result.PreviewText;

                if (result.DetectedMode.HasValue)
                {
                    // Викликаємо колбек на UI-потоці! Thread-safe.
                    onFormatDetected?.Invoke(result.DetectedMode.Value, result.DetectedInterval);
                }
            });
        });

        return container;
    }

    private static (string PreviewText, CsvImportMode? DetectedMode, float? DetectedInterval) GenerateCsvHeadTail(string path, int linesCount)
    {
        if (!File.Exists(path)) return ("File not found.", null, null);

        CsvImportMode? mode = null;
        float? interval = null;

        try
        {
            var head = new List<string>(linesCount);
            var tail = new Queue<string>(linesCount);
            int totalLines = 0;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);

            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                // Логіка автодетекції на першому рядку (Header)
                if (totalLines == 0)
                {
                    if (line.Contains("t0") && line.Contains("tInc"))
                    {
                        mode = CsvImportMode.IntervalBased;

                        // Парсимо tInc
                        var parts = line.Split(',');
                        var tIncPart = Array.Find(parts, p => p.Trim().StartsWith("tInc", StringComparison.OrdinalIgnoreCase));
                        if (tIncPart != null)
                        {
                            var tIncStr = tIncPart.Split('=')[1];
                            if (float.TryParse(tIncStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedInc))
                            {
                                interval = parsedInc;
                            }
                        }
                    }
                    else if (line.StartsWith("Time", StringComparison.OrdinalIgnoreCase))
                    {
                        mode = CsvImportMode.Timestamped;
                    }
                }

                if (totalLines < linesCount) head.Add(line);

                tail.Enqueue(line);
                if (tail.Count > linesCount) tail.Dequeue();

                totalLines++;
            }

            string preview;
            if (totalLines <= linesCount * 2)
            {
                preview = string.Join("\n", head.Concat(tail.Skip(Math.Max(0, totalLines - linesCount))).Distinct());
            }
            else
            {
                var hiddenCount = totalLines - (linesCount * 2);
                preview = string.Join("\n", head) +
                          $"\n\n  ... [ {hiddenCount:N0} lines hidden ] ...\n\n" +
                          string.Join("\n", tail);
            }

            return (preview, mode, interval);
        }
        catch (Exception ex)
        {
            return ($"Error generating preview:\n{ex.Message}", null, null);
        }
    }
}
