using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Services; // Для RenderFrame

namespace RigolScopeViewer;

// Структура для передачі налаштувань масштабу у ViewModel
public readonly struct ViewportChangeParams(double panPercent, double zoomFactor, int screenWidthPx)
{
    public double PanPercent { get; } = panPercent; // Напр. -0.1 (зсув на 10% вліво)
    public double ZoomFactor { get; } = zoomFactor; // Напр. 0.8 (зум ін) або 1.25 (зум аут)
    public int ScreenWidthPx { get; } = screenWidthPx;
}

public class GPUScopeControl : Control
{
    // --- 1. ПРИВ'ЯЗКИ (BINDINGS) ---

    // Властивість для ВХІДНИХ ДАНИХ (від ViewModel до Контрола)
    public static readonly StyledProperty<RenderFrame?> FrameDataProperty =
        AvaloniaProperty.Register<GPUScopeControl, RenderFrame?>(nameof(FrameData));

    public RenderFrame? FrameData
    {
        get => GetValue(FrameDataProperty);
        set => SetValue(FrameDataProperty, value);
    }

    // Команда для ВИХІДНИХ ПОДІЙ (від Контрола до ViewModel)
    public static readonly StyledProperty<ICommand?> UpdateViewportCommandProperty =
        AvaloniaProperty.Register<GPUScopeControl, ICommand?>(nameof(UpdateViewportCommand));

    public ICommand? UpdateViewportCommand
    {
        get => GetValue(UpdateViewportCommandProperty);
        set => SetValue(UpdateViewportCommandProperty, value);
    }

    // --- 2. СТАН ЕКРАНУ ---
    private double _panX = 0.0;
    private double _zoomX = 1.0;
    private float _voltsMin = -4.0f;
    private float _voltsMax = 4.0f;
    private float _intensity = 2.0f;
    private bool _isDragging = false;
    private Point? _lastMousePosition;

    // --- 3. РЕАКЦІЯ НА НОВІ ДАНІ ---
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Якщо ViewModel прислала нам новий прорахований кадр:
        if (change.Property == FrameDataProperty)
        {
            // Скидаємо візуальний шейдерний зсув, бо нові дані вже відцентровані
            _panX = 0;
            _zoomX = 1.0;
            InvalidateVisual(); // Перемалювати екран
        }
    }

    // Кешуємо логер як поле класу, щоб не діставати його з DI на кожному кадрі (це повільно)
    private readonly ILogger<DpoDrawOperation>? _drawLogger;

    public GPUScopeControl()
    {
        _drawLogger = App.Services?.GetService<ILogger<DpoDrawOperation>>();
    }

    public override void Render(DrawingContext context)
    {
        if (FrameData == null) return; // Нічого не малюємо, якщо даних немає

        var bounds = new Rect(default, Bounds.Size);

        // Передаємо наші РЕАЛЬНІ дані (_FrameData) замість mock-даних
        context.Custom(new DpoDrawOperation(
            _drawLogger,
            bounds,
            FrameData, // Твій оновлений DpoDrawOperation має приймати RenderFrame
            _panX,
            _zoomX,
            _voltsMin,
            _voltsMax,
            _intensity
        ));
    }

    // --- 4. ОБРОБКА ЖЕСТІВ ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);

        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMousePosition = point.Position;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging && _lastMousePosition.HasValue)
        {
            var currentPosition = e.GetPosition(this);
            _panX += (currentPosition.X - _lastMousePosition.Value.X);
            _lastMousePosition = currentPosition;

            // Швидко малюємо зсув на відеокарті
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            _lastMousePosition = null;
            e.Pointer.Capture(null);

            // ТУТ ВІДБУВАЄТЬСЯ МАГІЯ
            // Рахуємо, на скільки екранів (відсотків) ми зсунули графік
            double panPercent = _panX / Bounds.Width;

            // Відправляємо запит у ViewModel на перерахунок математики
            var args = new ViewportChangeParams(-panPercent, 1.0, (int)Bounds.Width);

            if (UpdateViewportCommand?.CanExecute(args) == true)
            {
                UpdateViewportCommand.Execute(args);
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        double zoomFactor = e.Delta.Y > 0 ? 0.8 : 1.25;

        // Показуємо швидкий зум на відеокарті
        _zoomX *= zoomFactor;
        InvalidateVisual();

        // Відразу просимо ViewModel перерахувати реальні точки
        var args = new ViewportChangeParams(0.0, zoomFactor, (int)Bounds.Width);

        if (UpdateViewportCommand?.CanExecute(args) == true)
        {
            UpdateViewportCommand.Execute(args);
        }
    }
}
