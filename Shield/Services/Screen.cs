﻿using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;
using Shield.Core;
using Shield.Core.Models;

namespace Shield.Services
{
    public class Screen
    {
        private MainPage mainPage;
        private int lastY = -1;
        private readonly FontFamily fixedFont = new FontFamily("Courier New");

        public Screen(MainPage mainPage)
        {
            this.mainPage = mainPage;
        }

        public async Task LogPrint(ScreenMessage lcdt)
        {
            if (lcdt.Action != null && lcdt.Action.ToUpperInvariant().Equals("CLEAR"))
            {
                mainPage.text.Text = "";
            }

            await
                mainPage.dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () => { mainPage.text.Text += lcdt.Message + "\r\n"; });
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct ArgbUnion
        {
            [FieldOffset(0)]
            public byte B;
            [FieldOffset(1)]
            public byte G;
            [FieldOffset(2)]
            public byte R;
            [FieldOffset(3)]
            public byte A;

            [FieldOffset(0)]
            public UInt32 Value;
        }

        private SolidColorBrush foreground = new SolidColorBrush(Color.FromArgb(0xFF,0xFF,0xFF,0xFF));
        private SolidColorBrush background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        private SolidColorBrush gray = new SolidColorBrush(Color.FromArgb(0xFF, 0x80, 0x80, 0x80));
        private int DefaultFontSize = 22;

        public async void LcdPrint(ScreenMessage lcdt)
        {
            var isText = lcdt.Service.Equals("LCDT");
            FrameworkElement element = null;
            var expandToEdge = false;
            SolidColorBrush brush = null;

            if (lcdt.Action != null)
            {
                if (!string.IsNullOrWhiteSpace(lcdt.ARGB))
                {
                    if (lcdt.ARGB[0] == '#')
                    {
                        var hex = lcdt.ARGB.ToByteArray();
                        if (hex.Length > 3)
                        {
                            brush =
                                new SolidColorBrush(Color.FromArgb((hex[0] == 0 ? (byte) 255 : hex[0]), hex[1], hex[2],
                                    hex[3]));
                        }
                    }
                    else
                    {
                        UInt32 color;
                        if (UInt32.TryParse(lcdt.ARGB, out color))
                        {
                            var argb = new ArgbUnion {Value = color};
                            brush = new SolidColorBrush(Color.FromArgb(argb.A == 0 ? (byte) 255 : argb.A, argb.R, argb.G, argb.B));
                        }
                    }   
                }

                var action = lcdt.Action.ToUpperInvariant();
                switch (action)
                {
                    case "ORIENTATION":
                        {
                            var current = DisplayInformation.AutoRotationPreferences;
                            if (lcdt.Value.HasValue)
                            {
                                DisplayInformation.AutoRotationPreferences = (DisplayOrientations)lcdt.Value.Value;
                            }

                            await mainPage.SendResult(new ScreenResultMessage(lcdt) { ResultId = (int)current });

                            break;
                        }
                    case "ENABLE":
                        {
                            mainPage.sensors[lcdt.Service + ":" + lcdt.Message] = 1;
                            return;
                        }
                    case "DISABLE":
                        {
                            if (mainPage.sensors.ContainsKey(lcdt.Service + ":" + lcdt.Message))
                            {
                                mainPage.sensors.Remove(lcdt.Service + ":" + lcdt.Message);
                            }

                            return;
                        }
                    case "CLEAR":
                        {
                            if (lcdt.Y.HasValue)
                            {
                                RemoveLine(lcdt.Y.Value);
                            }
                            else if (lcdt.Pid.HasValue)
                            {
                                RemoveId(lcdt.Pid.Value);
                            }
                            else
                            {
                                mainPage.canvas.Children.Clear();

                                if (brush != null)
                                {
                                    mainPage.canvas.Background = brush;
                                }

                                lastY = -1;
                                mainPage.player.Stop();
                                mainPage.player.Source = null;
                            }

                            break;
                        }

                    case "BUTTON":
                        {
                            element = new Button
                            {
                                Content = lcdt.Message,
                                FontSize = lcdt.Size ?? DefaultFontSize,
                                Tag = lcdt.Tag
                            };

                            element.Tapped += async (s, a) => await mainPage.SendEvent(s, a, "tapped");
                            ((Button)element).Click += async (s, a) => await mainPage.SendEvent(s, a, "click");
                            element.PointerPressed += async (s, a) => await mainPage.SendEvent(s, a, "pressed");
                            element.PointerReleased += async (s, a) => await mainPage.SendEvent(s, a, "released");

                            break;
                        }

                    case "IMAGE":
                        {
                            var imageBitmap = new BitmapImage(new Uri(lcdt.Path, UriKind.Absolute));
                            //imageBitmap.CreateOptions = Windows.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;

                            if (lcdt.Width.HasValue)
                            {
                                imageBitmap.DecodePixelWidth = lcdt.Width.Value;
                            }

                            if (lcdt.Height.HasValue)
                            {
                                imageBitmap.DecodePixelHeight = lcdt.Height.Value;
                            }

                            element = new Image
                            {
                                Tag = lcdt.Tag
                            };

                            ((Image)element).Source = imageBitmap;

                            element.Tapped += async (s, a) => await mainPage.SendEvent(s, a, "tapped");
                            break;
                        }
                    case "LINE":
                    {
                        var line = new Line
                        {
                            X1 = lcdt.X.Value,
                            Y1 = lcdt.Y.Value,
                            X2 = lcdt.X2.Value,
                            Y2 = lcdt.Y2.Value,
                            StrokeThickness = lcdt.Width ?? 1,
                            Stroke = foreground
                        };

                        element = line;

                        break;
                    }

                    case "INPUT":
                        {
                            element = new TextBox
                            {
                                Text = lcdt.Message,
                                FontSize = lcdt.Size ?? DefaultFontSize,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = brush ?? foreground,
                                AcceptsReturn = lcdt.Multi ?? false
                            };

                            expandToEdge = true;

                            element.SetValue(Canvas.LeftProperty, lcdt.X);
                            element.SetValue(Canvas.TopProperty, lcdt.Y);

                            element.LostFocus += async (s, a) => await mainPage.SendEvent(s, a, "lostfocus", lcdt, ((TextBox)s).Text);
                            ((TextBox)element).TextChanged += async (s, a) => await mainPage.SendEvent(s, a, "changed", lcdt, ((TextBox)s).Text);

                            break;
                        }

                    case "RECTANGLE":
                        {
                            var rect = new Rectangle
                            {
                                Tag = lcdt.Tag,
                                Fill = brush ?? gray
                            };

                            if (lcdt.Width.HasValue)
                            {
                                rect.Width = lcdt.Width.Value;
                            }

                            if (lcdt.Height.HasValue)
                            {
                                rect.Height = lcdt.Height.Value;
                            }

                            element = rect;

                            element.Tapped += async (s, a) => await mainPage.SendEvent(s, a, "tapped", lcdt);
                            break;
                        }
                    case "TEXT":
                        {
                            element = new TextBlock
                            {
                                Text = lcdt.Message,
                                FontSize = lcdt.Size ?? DefaultFontSize,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = brush ?? foreground
                            };

                            expandToEdge = true;

                            element.SetValue(Canvas.LeftProperty, lcdt.X);
                            element.SetValue(Canvas.TopProperty, lcdt.Y);
                            break;
                        }

                    default:
                        break;
                }
            }

            if (element == null && isText && lcdt.Message != null)
            {
                var x = lcdt.X ?? 0;
                var y = lcdt.Y ?? lastY + 1;

                expandToEdge = true;

                element = new TextBlock
                {
                    Text = lcdt.Message,
                    FontSize = lcdt.Size ?? DefaultFontSize,
                    TextWrapping = TextWrapping.Wrap,
                    Tag = y.ToString()
                };

                var textblock = (TextBlock)element;

                textblock.FontFamily = fixedFont;

                if (lcdt.Foreground != null)
                {
                    textblock.Foreground = HexColorToBrush(lcdt.Foreground);
                }

                if (lcdt.HorizontalAlignment != null)
                {
                    if (lcdt.HorizontalAlignment.Equals("Center"))
                    {
                        textblock.TextAlignment = TextAlignment.Center;
                    }
                }

                element.SetValue(Canvas.LeftProperty, isText ? x * textblock.FontSize : x);
                element.SetValue(Canvas.TopProperty, isText ? y * textblock.FontSize : y);
            }
            else if (element != null && element.GetType() != typeof(Line))
            {
                element.SetValue(Canvas.LeftProperty, lcdt.X);
                element.SetValue(Canvas.TopProperty, lcdt.Y);
            }

            if (element != null)
            {
                var x = lcdt.X ?? 0;
                var y = lcdt.Y ?? lastY + 1;

                if (lcdt.HorizontalAlignment != null)
                {
                    if (lcdt.HorizontalAlignment.Equals("Center"))
                    {
                        element.HorizontalAlignment = HorizontalAlignment.Center;
                        element.Width = mainPage.canvas.Width;
                    }
                }

                if (lcdt.FlowDirection != null)
                {
                    if (lcdt.FlowDirection.Equals("RightToLeft"))
                    {
                        element.FlowDirection = FlowDirection.RightToLeft;
                    } else if (lcdt.FlowDirection.Equals("LeftToRight"))
                    {
                        element.FlowDirection = FlowDirection.LeftToRight;
                    }
                }

                if (lcdt.Width.HasValue)
                {
                    element.Width = lcdt.Width.Value;
                }
                else if (expandToEdge)
                {
                    element.Width = mainPage.canvas.ActualWidth;
                }

                if (lcdt.Height.HasValue)
                {
                    element.Height = lcdt.Height.Value;
                }

                //TODO: add optional/extra properties in a later version here.
                if (isText && x == 0)
                {
                    RemoveLine(y);
                }

                element.SetValue(RemoteIdProperty, lcdt.Id);

                mainPage.canvas.Children.Add(element);

                if (isText)
                {
                    lastY = y;
                }
            }
        }


        public static Brush HexColorToBrush(string color)
        {
            color = color.Replace("#", "");
            if (color.Length > 5)
            {
                return new SolidColorBrush(ColorHelper.FromArgb(
                    color.Length > 7
                        ? byte.Parse(color.Substring(color.Length - 8, 2), NumberStyles.HexNumber)
                        : (byte)255,
                    byte.Parse(color.Substring(color.Length - 6, 2), NumberStyles.HexNumber),
                    byte.Parse(color.Substring(color.Length - 4, 2), NumberStyles.HexNumber),
                    byte.Parse(color.Substring(color.Length - 2, 2), NumberStyles.HexNumber)));
            }
            return null;
        }

        private void RemoveLine(int y)
        {
            var lines =
                mainPage.canvas.Children.Where(
                    t => t is TextBlock && ((TextBlock)t).Tag.Equals(y.ToString()));
            foreach (var line in lines)
            {
                mainPage.canvas.Children.Remove(line);
            }
        }

        private void RemoveId(int id)
        {
            var items =
                mainPage.canvas.Children.Where(e => ((int)e.GetValue(RemoteIdProperty)) == id);

            foreach (var item in items)
            {
                mainPage.canvas.Children.Remove(item);
            }
        }

        public static readonly DependencyProperty RemoteIdProperty = DependencyProperty.RegisterAttached("RemoteId",
            typeof(int), typeof(FrameworkElement), new PropertyMetadata(0));

    }
}