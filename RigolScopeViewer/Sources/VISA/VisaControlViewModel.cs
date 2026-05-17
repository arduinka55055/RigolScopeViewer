using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
// using RigolScopeViewer.Interfaces; // Якщо ти створював IPluginViewModel, розкоментуй і додай його до класу

namespace RigolScopeViewer.Sources.VISA;

public partial class VisaControlViewModel(VisaWaveformSource source) : ObservableObject
{
    private readonly VisaWaveformSource _source = source;
    private CancellationTokenSource? _liveViewCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isError; // Прапорець для UI, щоб підсвічувати помилки

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        IsError = false;
        _liveViewCts = new CancellationTokenSource();

        try
        {
            // 1. Відкриваємо єдине постійне TCP-з'єднання
            _source.ConnectPersistent();

            // 2. Безкінечний цикл Live View
            while (!_liveViewCts.Token.IsCancellationRequested)
            {
                await _source.FetchDataAsync(_liveViewCts.Token);
                await Task.Delay(50, _liveViewCts.Token); // Обмеження ~20 FPS, щоб не покласти прилад
            }
        }
        catch (OperationCanceledException)
        {
            /* Нормальна зупинка через кнопку Stop */
        }
        catch (Exception)
        {
            // Якщо LXI завис, кабель випав або прилад перезавантажився
            IsError = true;
        }
        finally
        {
            // 3. Закриваємо з'єднання при зупинці, щоб звільнити ресурси мережі
            _source.DisconnectPersistent();
            IsRunning = false;
        }
    }
    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _liveViewCts?.Cancel();
    }
    private bool CanStop() => IsRunning;

    [RelayCommand]
    private async Task ResetConnectionAsync()
    {
        // Запам'ятовуємо, чи працював Live View до натискання Reset
        bool wasRunning = IsRunning;

        if (wasRunning)
        {
            Stop();
            // Обов'язково чекаємо, поки старий цикл гарантовано завершиться і звільнить сокет
            while (IsRunning) await Task.Delay(10);
        }

        try
        {
            // Викликаємо жорстке скидання
            await _source.ResetLxiAsync();
            IsError = false;

            // Якщо ми були в режимі Live View до збою, автоматично піднімаємо його назад
            if (wasRunning)
            {
                await StartAsync();
            }
        }
        catch
        {
            IsError = true; // Скидання не допомогло (можливо, прилад вимкнено фізично)
        }
    }
}
