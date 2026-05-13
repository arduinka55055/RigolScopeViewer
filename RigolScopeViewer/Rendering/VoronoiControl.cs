using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RigolScopeViewer;

public class VoronoiControl : Control
{
    // 1. Стан нашого екрану (виносимо з Render)
    private double _panX = 0.0;
    private double _panY = 0.0;
    private double _zoomX = 1.0;
    private float _voltsMin = -4.0f;
    private float _voltsMax = 4.0f;
    private float _intensity = 2.0f;

    // 2. Змінні для логіки перетягування (Drag)
    private bool _isDragging = false;
    private Point? _lastMousePosition;

    // Прибираємо постійний цикл анімації (QueueNextFrame),
    // тепер ми рендеримо лише коли є зміни!

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(default, Bounds.Size);

        context.Custom(new DpoDrawOperation(
            bounds,
            _panX,
            _zoomX,
            _voltsMin,
            _voltsMax,
            _intensity
        ));
    }

    // --- ОБРОБКА ЖЕСТІВ ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);

        // Починаємо тягнути тільки якщо натиснута ліва кнопка
        if (point.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastMousePosition = point.Position;

            // "Захоплюємо" мишу. Навіть якщо курсор вийде за межі контрола,
            // ми все одно будемо отримувати події руху.
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging && _lastMousePosition.HasValue)
        {
            var currentPosition = e.GetPosition(this);

            // Рахуємо, на скільки пікселів зсунулась миша
            var deltaX = currentPosition.X - _lastMousePosition.Value.X;
            var deltaY = currentPosition.Y - _lastMousePosition.Value.Y;

            // Додаємо зсув до нашого глобального панорамування
            _panX += deltaX;
            _panY += deltaY;

            // Оновлюємо останню позицію
            _lastMousePosition = currentPosition;

            // КАЖЕМО AVALONIA ПЕРЕМАЛЮВАТИ КАДР!
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
            e.Pointer.Capture(null); // Відпускаємо мишу

            // ТУТ БУДЕ МАГІЯ:
            // Коли користувач відпустив мишу (Drop), текстура зміщена.
            // Саме тут ти маєш викликати подію для своєї ViewModel / IBinningEngine:
            // "Гей, юзер зсунув екран на _panX пікселів, дай мені нові дані!"
            // А коли нові дані прийдуть, ти обнулиш _panX = 0; і зробиш InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // e.Delta.Y > 0 означає скрол вгору (зум in)
        // e.Delta.Y < 0 означає скрол вниз (зум out)
        var zoomFactor = 1.15; // Крок зуму 15%

        if (e.Delta.Y > 0)
        {
            _zoomX *= zoomFactor;
        }
        else if (e.Delta.Y < 0)
        {
            _zoomX /= zoomFactor;
        }

        // Обмежуємо зум, щоб уникнути вильоту математики
        _zoomX = Math.Clamp(_zoomX, 0.01, 1000.0);

        InvalidateVisual(); // Перемалювати з новим зумом
    }
}
