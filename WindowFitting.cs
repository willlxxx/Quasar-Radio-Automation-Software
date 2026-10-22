using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Serilog;

namespace quasar_radio
{
    /// <summary>
    /// Ajusta una ventana a el area util del monitor en el que cae.
    ///
    /// Existe porque <see cref="WindowStartupLocation.CenterScreen"/> usa la pantalla
    /// entera, no el area util: con la barra de tareas delante, una consola de 900 px
    /// de alto en un panel de 1600x900 deja la barra de titulo fuera del monitor.
    /// Tampoco sabe nada de monitores con origen negativo (80,-20), que es el caso
    /// real del panel del estudio.
    /// </summary>
    public static class WindowFitting
    {
        private static readonly ILogger _log = Log.ForContext(typeof(WindowFitting));
        /// <summary>
        /// Encoge la ventana si no cabe y la empuja lo minimo necesario para que quede
        /// dentro del area util. Si ya estaba bien puesta, no la mueve: la geometria que
        /// guardo <see cref="ModuleNavigator"/> debe respetarse tal cual.
        ///
        /// Se llama en <c>OnSourceInitialized</c>: cuando el HWND ya existe y Windows
        /// ya le asigno posicion, pero antes de que el usuario la vea pintada.
        /// </summary>
        public static void FitToWorkArea(Window window)
        {
            if (window == null) return;

            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;

                if (!TryGetWorkAreaDip(hwnd, window, out var work)) return;

                // Maximizada se gestiona sola; aqui solo importa la ventana normal.
                var resized = false;
                if (window.WindowState == WindowState.Normal)
                {
                    if (window.Width > work.Width) { window.Width = Math.Floor(work.Width); resized = true; }
                    if (window.Height > work.Height) { window.Height = Math.Floor(work.Height); resized = true; }
                }

                var w = window.WindowState == WindowState.Normal ? window.Width : work.Width;
                var h = window.WindowState == WindowState.Normal ? window.Height : work.Height;

                // Franja tolerada: con el borde superior mas arriba del area util la barra
                // de titulo (y con ella Mover/Minimizar/Cerrar) queda inalcanzable.
                var minLeft = work.X;
                var maxLeft = work.X + work.Width - Math.Min(w, work.Width);
                var minTop = work.Y;
                var maxTop = work.Y + work.Height - Math.Min(h, work.Height);

                var left = Math.Clamp(window.Left, minLeft, Math.Max(minLeft, maxLeft));
                var top = Math.Clamp(window.Top, minTop, Math.Max(minTop, maxTop));

                var moved = Math.Abs(left - window.Left) > 0.5 || Math.Abs(top - window.Top) > 0.5;
                if (!moved && !resized) return;

                window.WindowStartupLocation = WindowStartupLocation.Manual;
                if (moved)
                {
                    window.Left = left;
                    window.Top = top;
                }

                _log.Information(
                    "Ventana {Type} encajada en el area util {Work}: {Left},{Top} {W}x{H}",
                    window.GetType().Name, work, window.Left, window.Top, w, h);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "No se pudo ajustar la ventana al monitor");
            }
        }

        /// <summary>Enrolla <see cref="FitToWorkArea"/> en el evento SourceInitialized.</summary>
        public static void FitWhenReady(this Window window)
        {
            if (window == null) return;

            if (window.IsInitialized)
            {
                FitToWorkArea(window);
                return;
            }

            window.SourceInitialized += (_, _) => FitToWorkArea(window);
        }

        // ------------------------------------------------------------------ Win32

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const int MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        /// <summary>
        /// Area util del monitor mas cercano al HWND, pasada a unidades independientes
        /// del dispositivo (las que usan <see cref="Window.Left"/> y cia).
        /// </summary>
        private static bool TryGetWorkAreaDip(IntPtr hwnd, Window window, out Rect work)
        {
            work = Rect.Empty;

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) return false;

            var dpi = VisualTreeHelper.GetDpi(window);
            var sx = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
            var sy = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

            var x = info.rcWork.Left / sx;
            var y = info.rcWork.Top / sy;
            var width = (info.rcWork.Right - info.rcWork.Left) / sx;
            var height = (info.rcWork.Bottom - info.rcWork.Top) / sy;
            if (width <= 0 || height <= 0) return false;

            work = new Rect(x, y, width, height);
            return true;
        }
    }
}
