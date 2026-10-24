using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

// Windows.System also declares these two names, so the WinUI ones are pinned down here.
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace DomeOSAgentDev
{
    /// <summary>
    /// The RunDuck egg: the Chrome offline dinosaur, reproduced. The original drew pixel-art sprites
    /// onto a 600×150 canvas from bitmap maps; this paints the same maps straight into a
    /// WriteableBitmap, so the shapes, the physics constants and the spawn rhythm all match.
    ///
    /// Sprite maps are rows of [startColumn, endColumn] ranges; a row may hold more than one range.
    /// </summary>
    internal sealed class RunDuckGame
    {
        private const int CanvasW = 600;
        private const int CanvasH = 150;

        private const int GroundY = 127;
        private const int GroundPeriod = 14;
        private const int GroundDash = 9;

        private const double DinoX = 30;
        private const double DinoCell = 2.05;
        private const double DuckCell = 2.5;
        private const double DinoW = 20 * DinoCell;
        private const double DinoH = 23 * DinoCell;
        private const double DuckW = 20 * DuckCell;
        private const double DuckH = 12 * DuckCell;

        private const double Gravity = 0.62;
        private const double JumpVelocity = -9.4;
        private const double FastFall = 2.6;
        private const double SpeedMin = 6;
        private const double SpeedMax = 13;
        private const double SpeedRampDistance = 6000;

        private static readonly byte[] Background = { 247, 247, 247 };
        private static readonly byte[] Ink = { 83, 83, 83 };
        private static readonly byte[] CloudInk = { 200, 200, 200 };

        // ---- sprites

        private static readonly int[][] DinoBody =
        {
            new[] { 11, 15 },
            new[] { 10, 16 },
            new[] { 10, 18 },
            new[] { 10, 19 },
            new[] { 10, 14, 16, 19 },
            new[] { 10, 19 },
            new[] { 10, 17 },
            new[] { 10, 16 },
            new[] { 10, 17 },
            new[] { 10, 17 },
            new[] { 9, 16 },
            new[] { 2, 15 },
            new[] { 1, 16 },
            new[] { 1, 17 },
            new[] { 1, 18 },
            new[] { 2, 17 },
            new[] { 3, 15 },
            new[] { 4, 14 },
            new[] { 5, 12 },
        };

        private static readonly int[] LegsBoth = { 5, 6, 10, 11 };
        private static readonly int[] LegsBack = { 5, 6 };
        private static readonly int[] LegsFront = { 10, 11 };

        private static readonly int[][] DinoIdle = Combine(DinoBody, LegsBoth, LegsBoth, LegsBoth, LegsBoth);
        private static readonly int[][] DinoRunA = Combine(DinoBody, LegsBoth, LegsBack, LegsBack, LegsBack);
        private static readonly int[][] DinoRunB = Combine(DinoBody, LegsBoth, LegsFront, LegsFront, LegsFront);

        private static readonly int[][] DuckMap =
        {
            new[] { 12, 15 },
            new[] { 12, 16 },
            new[] { 12, 18 },
            new[] { 12, 15, 16, 19 },
            new[] { 12, 17 },
            new[] { 3, 16 },
            new[] { 2, 17 },
            new[] { 2, 17 },
            new[] { 3, 16 },
            new[] { 5, 7, 11, 13 },
            new[] { 5, 7, 11, 13 },
            new[] { 5, 7, 11, 13 },
        };

        private static readonly int[][] CactusMap =
        {
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 0, 0, 2, 4 },
            new[] { 0, 0, 2, 4 },
            new[] { 0, 0, 2, 4, 6, 6 },
            new[] { 0, 4, 6, 6 },
            new[] { 2, 4, 6, 6 },
            new[] { 2, 6 },
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 2, 4 },
            new[] { 2, 4 },
        };

        private static readonly int[][] CloudMap =
        {
            new[] { 3, 5 },
            new[] { 2, 7 },
            new[] { 1, 8 },
            new[] { 0, 8 },
        };

        private static int[][] Combine(int[][] body, params int[][] legs)
        {
            List<int[]> rows = new List<int[]>(body);
            rows.AddRange(legs);
            return rows.ToArray();
        }

        // ---- state

        private sealed class DuckState
        {
            public double Y = GroundY;
            public double Velocity;
            public bool Jumping;
            public bool Ducking;
        }

        private sealed class Obstacle
        {
            public double X;
            public double Cell;
            public int Count;
            public double UnitWidth;
            public double Width;
            public double Height;
        }

        private sealed class CloudState
        {
            public double X;
            public double Y;
        }

        private readonly Image _target;
        private readonly TextBlock _scoreText;
        private readonly StackPanel _gameOverPanel;
        private readonly DispatcherQueue _queue;

        private readonly WriteableBitmap _bitmap = new WriteableBitmap(CanvasW, CanvasH);
        private readonly byte[] _pixels = new byte[CanvasW * CanvasH * 4];
        private readonly List<Obstacle> _obstacles = new List<Obstacle>();
        private readonly List<CloudState> _clouds = new List<CloudState>();
        private readonly Stopwatch _clock = new Stopwatch();

        private DispatcherQueueTimer? _timer;
        private double _lastTime;
        private bool _open;

        private DuckState? _dino;
        private double _groundOffset;
        private double _speed = SpeedMin;
        private double _distance;
        private int _score;
        private int _best;
        private bool _gameOver;
        private int _legFrame;
        private double _legTimer;
        private double _spawnTimer;
        private double _cloudTimer;

        public RunDuckGame(Image target, TextBlock scoreText, StackPanel gameOverPanel, DispatcherQueue queue)
        {
            _target = target;
            _scoreText = scoreText;
            _gameOverPanel = gameOverPanel;
            _queue = queue;
        }

        public void Open()
        {
            if (_open)
            {
                return;
            }

            _open = true;
            _target.Source = _bitmap;

            Reset();
            Render();

            _clock.Restart();
            _lastTime = 0;

            _timer = _queue.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            _timer.IsRepeating = true;
            _timer.Tick += (DispatcherQueueTimer sender, object args) => Tick();
            _timer.Start();
        }

        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;

            if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }

            _clock.Stop();
            _dino = null;
        }

        public void HandleKeyDown(VirtualKey key)
        {
            if (!_open)
            {
                return;
            }

            if (key == VirtualKey.Space || key == VirtualKey.Up)
            {
                if (_gameOver)
                {
                    Reset();
                }
                else
                {
                    TryJump();
                }
            }
            else if (key == VirtualKey.Down && !_gameOver && _dino != null)
            {
                _dino.Ducking = true;
            }
        }

        public void HandleKeyUp(VirtualKey key)
        {
            if (_open && key == VirtualKey.Down && _dino != null)
            {
                _dino.Ducking = false;
            }
        }

        /// <summary>Space is not the only way in: the canvas is clickable too.</summary>
        public void Tap()
        {
            if (!_open)
            {
                return;
            }

            if (_gameOver)
            {
                Reset();
            }
            else
            {
                TryJump();
            }
        }

        // ------------------------------------------------------------------ simulation

        private void Reset()
        {
            _dino = new DuckState();
            _obstacles.Clear();
            _clouds.Clear();
            _clouds.Add(new CloudState { X = 430, Y = 44 });
            _groundOffset = 0;
            _speed = SpeedMin;
            _distance = 0;
            _score = 0;
            _gameOver = false;
            _legFrame = 0;
            _legTimer = 0;
            _spawnTimer = 260;
            _cloudTimer = 520;

            _gameOverPanel.Visibility = Visibility.Collapsed;
        }

        private void TryJump()
        {
            DuckState? dino = _dino;
            if (!_open || dino == null || _gameOver || dino.Jumping)
            {
                return;
            }

            dino.Jumping = true;
            dino.Ducking = false;
            dino.Velocity = JumpVelocity;
        }

        private void Tick()
        {
            if (!_open)
            {
                return;
            }

            double now = _clock.Elapsed.TotalMilliseconds;
            double dt = now - _lastTime;
            if (!(dt > 0))
            {
                dt = 16.667;
            }

            if (dt > 50)
            {
                dt = 50;
            }

            _lastTime = now;

            Update(dt);
            Render();
        }

        private void Update(double dt)
        {
            if (_gameOver || _dino == null)
            {
                return;
            }

            double f = dt / 16.667;
            DuckState dino = _dino;

            _speed = SpeedMin + (SpeedMax - SpeedMin) * Math.Min(1, _distance / SpeedRampDistance);
            _distance += _speed * f;
            _score = (int)Math.Floor(_distance / 8);
            _groundOffset = (_groundOffset + _speed * f) % GroundPeriod;

            if (dino.Jumping)
            {
                dino.Velocity += Gravity * (dino.Ducking ? FastFall : 1) * f;
                dino.Y += dino.Velocity * f;
                if (dino.Y >= GroundY)
                {
                    dino.Y = GroundY;
                    dino.Velocity = 0;
                    dino.Jumping = false;
                }
            }

            if (!dino.Jumping && !dino.Ducking)
            {
                _legTimer += dt;
                if (_legTimer >= 100)
                {
                    _legTimer = 0;
                    _legFrame ^= 1;
                }
            }

            foreach (Obstacle obstacle in _obstacles)
            {
                obstacle.X -= _speed * f;
            }

            while (_obstacles.Count > 0 && _obstacles[0].X + _obstacles[0].Width < -24)
            {
                _obstacles.RemoveAt(0);
            }

            _spawnTimer -= _speed * f;
            if (_spawnTimer <= 0)
            {
                SpawnObstacle();
            }

            foreach (CloudState cloud in _clouds)
            {
                cloud.X -= _speed * 0.35 * f;
            }

            while (_clouds.Count > 0 && _clouds[0].X < -60)
            {
                _clouds.RemoveAt(0);
            }

            _cloudTimer -= _speed * f;
            if (_cloudTimer <= 0)
            {
                SpawnCloud();
            }

            if (Collide())
            {
                _gameOver = true;
                dino.Jumping = false;
                dino.Velocity = 0;
                dino.Y = GroundY;

                if (_score > _best)
                {
                    _best = _score;
                }

                _gameOverPanel.Visibility = Visibility.Visible;
            }
        }

        private void SpawnObstacle()
        {
            bool large = Random.Shared.NextDouble() < 0.38;
            double cell = large ? 3.2 : 2.2;
            int maxCount = large ? 2 : 3;
            int count = 1 + (int)Math.Floor(Random.Shared.NextDouble() * maxCount);
            double unitWidth = Math.Round(7 * cell);

            _obstacles.Add(new Obstacle
            {
                X = CanvasW + 8,
                Cell = cell,
                Count = count,
                UnitWidth = unitWidth,
                Width = unitWidth * count,
                Height = Math.Round(15 * cell),
            });

            // spacing is set in pixel distance so the rhythm stays even as the speed rises
            _spawnTimer = (unitWidth * count) + _speed * (28 + Random.Shared.NextDouble() * 42);
        }

        private void SpawnCloud()
        {
            _clouds.Add(new CloudState
            {
                X = CanvasW + 10,
                Y = 24 + Random.Shared.NextDouble() * 46,
            });

            _cloudTimer = 260 + Random.Shared.NextDouble() * 620;
        }

        /// <summary>Hit boxes are a little tighter than the sprites, matching the original feel.</summary>
        private bool Collide()
        {
            DuckState? dino = _dino;
            if (dino == null)
            {
                return false;
            }

            bool ducking = dino.Ducking && !dino.Jumping;
            double height = ducking ? DuckH : DinoH;
            double width = ducking ? DuckW : DinoW;
            double left = DinoX + 6;
            double right = DinoX + width - 8;
            double top = dino.Y - height + 8;
            double bottom = dino.Y - 3;

            foreach (Obstacle obstacle in _obstacles)
            {
                double otherLeft = obstacle.X + 3;
                double otherRight = obstacle.X + obstacle.Width - 3;
                double otherTop = GroundY - obstacle.Height + 4;

                if (left < otherRight && right > otherLeft && top < GroundY && bottom > otherTop)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ painting

        private void Render()
        {
            ClearBackground();

            foreach (CloudState cloud in _clouds)
            {
                DrawBitmap(CloudMap, cloud.X, cloud.Y + 16, 4, CloudInk);
            }

            int gx = (int)Math.Round(-_groundOffset);
            while (gx < CanvasW)
            {
                if (gx + GroundDash > 0)
                {
                    Fill(gx, GroundY, GroundDash, 2, Ink);
                }

                gx += GroundPeriod;
            }

            foreach (Obstacle obstacle in _obstacles)
            {
                for (int c = 0; c < obstacle.Count; c++)
                {
                    DrawBitmap(CactusMap, obstacle.X + c * obstacle.UnitWidth, GroundY, obstacle.Cell, Ink);
                }
            }

            DrawDino();
            Present();

            string current = Pad5(_score);
            _scoreText.Text = _best > 0 ? "HI " + Pad5(_best) + "  " + current : current;
        }

        private void DrawDino()
        {
            DuckState? dino = _dino;
            if (dino == null)
            {
                return;
            }

            if (dino.Ducking && !dino.Jumping)
            {
                DrawBitmap(DuckMap, DinoX, dino.Y, DuckCell, Ink);
                return;
            }

            int[][] map = DinoIdle;
            if (!_gameOver && !dino.Jumping)
            {
                map = _legFrame != 0 ? DinoRunA : DinoRunB;
            }

            DrawBitmap(map, DinoX, dino.Y, DinoCell, Ink);
        }

        private void DrawBitmap(int[][] map, double left, double bottomY, double cell, byte[] color)
        {
            int rows = map.Length;
            double topF = bottomY - rows * cell;

            for (int r = 0; r < rows; r++)
            {
                int[] ranges = map[r];
                if (ranges == null)
                {
                    continue;
                }

                int ry = (int)Math.Round(topF + r * cell);
                int rh = (int)Math.Round(topF + (r + 1) * cell) - ry;

                for (int i = 0; i + 1 < ranges.Length; i += 2)
                {
                    int x0 = (int)Math.Round(left + ranges[i] * cell);
                    int x1 = (int)Math.Round(left + (ranges[i + 1] + 1) * cell);
                    Fill(x0, ry, x1 - x0, rh, color);
                }
            }
        }

        private void ClearBackground()
        {
            for (int i = 0; i < _pixels.Length; i += 4)
            {
                _pixels[i] = Background[0];
                _pixels[i + 1] = Background[1];
                _pixels[i + 2] = Background[2];
                _pixels[i + 3] = 255;
            }
        }

        /// <summary>Writes one rectangle of colour, clipped to the canvas.</summary>
        private void Fill(int x, int y, int width, int height, byte[] color)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int x0 = Math.Max(0, x);
            int y0 = Math.Max(0, y);
            int x1 = Math.Min(CanvasW, x + width);
            int y1 = Math.Min(CanvasH, y + height);

            for (int py = y0; py < y1; py++)
            {
                int row = py * CanvasW * 4;
                for (int px = x0; px < x1; px++)
                {
                    int offset = row + px * 4;
                    _pixels[offset] = color[2];
                    _pixels[offset + 1] = color[1];
                    _pixels[offset + 2] = color[0];
                    _pixels[offset + 3] = 255;
                }
            }
        }

        private void Present()
        {
            using (Stream stream = _bitmap.PixelBuffer.AsStream())
            {
                stream.Position = 0;
                stream.Write(_pixels, 0, _pixels.Length);
            }

            _bitmap.Invalidate();
        }

        private static string Pad5(int value)
        {
            string text = value.ToString();
            while (text.Length < 5)
            {
                text = "0" + text;
            }

            return text;
        }
    }
}
