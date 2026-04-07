using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RigolScopeViewer;

public class VoronoiControl : Control
{
    private static readonly Stopwatch St = Stopwatch.StartNew();
    private bool _isRunning;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Start the animation loop when the control appears
        _isRunning = true;
        QueueNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // STOP the loop when the control is removed (prevents memory leaks/ghost CPU usage)
        _isRunning = false;
    }

    private void QueueNextFrame()
    {
        if (!_isRunning) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
        {
            // Syncs the redraw exactly to the monitor's refresh rate
            topLevel.RequestAnimationFrame(_ =>
            {
                if (!_isRunning) return;
                InvalidateVisual(); // Trigger Render()
                QueueNextFrame();   // Loop
            });
        }
    }

    public override void Render(DrawingContext context)
    {
        // 1. Задаємо тестові параметри
        var bounds = new Rect(default, Bounds.Size); // Розмір поточного контрола
        double panX = 0.0;       // Мишка нічого не тягне, зсув нульовий
        double zoomX = 1.0;      // Нормальний масштаб 1:1
        float voltsMin = -4.0f;  // Нижня межа екрану (наприклад, -4 Вольта)
        float voltsMax = 4.0f;   // Верхня межа екрану (+4 Вольта)
        float intensity = 2.0f;  // Множник яскравості "фосфору" (2.0 дасть гарне світіння)

        // 2. Передаємо їх у наш новий рендерер
        context.Custom(new DpoDrawOperation(
            bounds,
            panX,
            zoomX,
            voltsMin,
            voltsMax,
            intensity
        ));

        // 3. Залишаємо цикл перемальовки, щоб перевірити FPS 
        // (Але в реальному житті роби InvalidateVisual лише коли прийшли нові дані або юзер зробив Drag/Zoom!)
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}