using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RigolScopeViewer.Services;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Reflection;

namespace RigolScopeViewer;

public partial class OscilloscopeControl : UserControl
{
    public OscilloscopeControl()
    {
        InitializeComponent();
    }
}
