using System;
using RigolScopeViewer.Models;

namespace RigolScopeViewer;

static class GodObject
{
    // Цей клас - тимчасовий "Бог-Об'єкт", який містить посилання на всі важливі сервіси та дані.
    // говнокод, замінити на DI

    //give in array float for channel
    public static Func<float[]>? ChannelDataReady;
    public static WaveformMetadata WaveMetadata { get; set; }
}
