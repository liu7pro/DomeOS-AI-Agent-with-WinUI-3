using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The Agent Orb itself: a soft sphere tinted with the two configured colours, wrapped in a
    /// glow. It breathes - slowly while idle, faster while the agent is thinking or running a tool.
    ///
    /// One instance drives the settings preview and another drives the floating window, so the
    /// preview always shows exactly what the desktop orb will look like.
    /// </summary>
    internal sealed class OrbVisual
    {
        private readonly Grid _root = new Grid();
        private readonly Ellipse _glow = new Ellipse();
        private readonly Ellipse _body = new Ellipse();
        private readonly RadialGradientBrush _glowBrush = new RadialGradientBrush();
        private readonly RadialGradientBrush _bodyBrush = new RadialGradientBrush();
        private readonly ScaleTransform _glowScale = new ScaleTransform();
        private readonly ScaleTransform _bodyScale = new ScaleTransform();

        private double _size;
        private double _phase;

        public OrbVisual(double size, Color primary, Color secondary)
        {
            _glow.RenderTransform = _glowScale;
            _body.RenderTransform = _bodyScale;

            _glow.Fill = _glowBrush;
            _body.Fill = _bodyBrush;

            Canvas.SetZIndex(_glow, 0);
            Canvas.SetZIndex(_body, 1);

            _root.Children.Add(_glow);
            _root.Children.Add(_body);
            _root.IsHitTestVisible = false;

            SetColors(primary, secondary);
            Resize(size);
        }

        public FrameworkElement Root => _root;

        /// <summary>True while the agent is working: the breathing speeds up.</summary>
        public bool Active { get; set; }

        public void Resize(double size)
        {
            _size = Math.Max(8, size);

            _body.Width = _size;
            _body.Height = _size;

            _glow.Width = _size * 1.5;
            _glow.Height = _size * 1.5;

            _root.Width = _size * 1.5;
            _root.Height = _size * 1.5;
        }

        public void SetColors(Color primary, Color secondary)
        {
            _bodyBrush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
            _bodyBrush.Center = new Point(0.36, 0.30);
            _bodyBrush.RadiusX = 0.72;
            _bodyBrush.RadiusY = 0.72;
            _bodyBrush.GradientStops.Clear();
            _bodyBrush.GradientStops.Add(new GradientStop { Offset = 0.00, Color = Mix(primary, Colors.White, 0.62) });
            _bodyBrush.GradientStops.Add(new GradientStop { Offset = 0.46, Color = primary });
            _bodyBrush.GradientStops.Add(new GradientStop { Offset = 1.00, Color = Mix(secondary, Colors.Black, 0.34) });

            _glowBrush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
            _glowBrush.Center = new Point(0.5, 0.5);
            _glowBrush.RadiusX = 0.5;
            _glowBrush.RadiusY = 0.5;
            _glowBrush.GradientStops.Clear();
            _glowBrush.GradientStops.Add(new GradientStop { Offset = 0.42, Color = WithAlpha(primary, 130) });
            _glowBrush.GradientStops.Add(new GradientStop { Offset = 1.00, Color = WithAlpha(primary, 0) });
        }

        /// <summary>One animation step; idle breathing is slow, working breathing is quick.</summary>
        public void Advance(double dtMs)
        {
            double period = Active ? 700 : 2200;
            _phase += dtMs / period;
            if (_phase > 1)
            {
                _phase -= Math.Floor(_phase);
            }

            double wave = 0.5 + 0.5 * Math.Sin(_phase * 2 * Math.PI);

            double body = 0.94 + 0.10 * wave;
            _bodyScale.ScaleX = body;
            _bodyScale.ScaleY = body;

            double glow = (Active ? 1.02 : 0.94) + 0.16 * wave;
            _glowScale.ScaleX = glow;
            _glowScale.ScaleY = glow;

            _glow.Opacity = (Active ? 0.55 : 0.32) + 0.35 * wave;
        }

        private static Color Mix(Color from, Color to, double k)
        {
            return Color.FromArgb(
                255,
                (byte)Math.Round(from.R + (to.R - from.R) * k),
                (byte)Math.Round(from.G + (to.G - from.G) * k),
                (byte)Math.Round(from.B + (to.B - from.B) * k));
        }

        private static Color WithAlpha(Color color, byte alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
