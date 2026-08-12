using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SmartAnalysis.UiTests;

/// <summary>
/// A single STA thread + <see cref="Dispatcher"/> hosting ONE WPF <see cref="System.Windows.Application"/>
/// with the design system merged. WPF's Application is a per-process singleton, so every WPF-dependent test
/// marshals its work onto this shared thread via <see cref="Invoke{T}"/> instead of creating its own.
/// </summary>
internal static class WpfTestHost
{
    private static Dispatcher? _dispatcher;
    private static readonly object Gate = new();

    /// <summary>Runs <paramref name="func"/> on the shared STA/Application thread and returns its result.</summary>
    public static T Invoke<T>(Func<T> func)
    {
        EnsureStarted();
        return _dispatcher!.Invoke(func);
    }

    private static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
            {
                return;
            }

            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                var app = new System.Windows.Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/SmartAnalysis.UI;component/DesignSystem/DesignSystem.xaml"),
                });
                _dispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();
        }
    }
}
