using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace UwpMandelbrot {
    sealed partial class Mandelbrot : UserControl {
        WriteableBitmap _bmp;
        Complex _from = new Complex(-1.5, -1), _to = new Complex(1, 1);

        public Mandelbrot() {
            this.InitializeComponent();

            Loaded += async delegate {
                await Init();
                CreateBitmap();
                await RunAsync(_from, _to);
            };

            SizeChanged += async (s, e) => {
                if (_bmp != null) {
                    CreateBitmap();
                    await RunAsync(_from, _to);
                }
            };
        }

        void CreateBitmap() {
            _bmp = new WriteableBitmap((int)ActualWidth, (int)ActualHeight);
            _image.Source = _bmp;
        }

        static Color[] _rainbow;


        async static Task Init() {
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Data/nice.xml"));
            using (var stm = await file.OpenReadAsync()) {
                _rainbow = ColorGradientPersist.Read(stm.AsStreamForRead()).GenerateColors(512);
            }
        }

        int MandelbrotColor(Complex c) {
            int color = _rainbow.Length;

            Complex z = Complex.Zero;
            while (z.Real * z.Real + z.Imaginary * z.Imaginary <= 4 && color > 0) {
                z = z * z + c;
                color--;
            }
            return color == 0 ? Colors.Black.ToInt() : _rainbow[color].ToInt();
        }

        public async Task Reset(Complex? from = null, Complex? to = null) {
            await RunAsync(from ?? new Complex(-1.5, -1), to ?? new Complex(1, 1));
        }

        bool _isSelecting;
        Point _start;

        private void OnMoved(object sender, PointerRoutedEventArgs e) {
            if (_isSelecting) {
                var pt = e.GetCurrentPoint(_image).Position;
                _rect.Rect = new Rect(Math.Min(_start.X, pt.X), Math.Min(_start.Y, pt.Y), Math.Abs(pt.X - _start.X), Math.Abs(pt.Y - _start.Y));
            }
        }

        private async void OnReleased(object sender, PointerRoutedEventArgs e) {
            if (_isSelecting) {
                _isSelecting = false;
                _selection.Visibility = Visibility.Collapsed;
                var rc = _rect.Rect;
                if (rc.Width < 10 && rc.Height < 10)
                    return;

                _image.ReleasePointerCapture(e.Pointer);
                double newWidth = rc.Width * (_to.Real - _from.Real) / _image.ActualWidth;
                double newHeight = rc.Height * (_to.Imaginary - _from.Imaginary) / _image.ActualHeight;
                double deltax = rc.X * (_to.Real - _from.Real) / _image.ActualWidth;
                double deltay = rc.Y * (_to.Imaginary - _from.Imaginary) / _image.ActualHeight;
                _from = _from + new Complex(deltax, deltay);
                _to = _from + new Complex(newWidth, newHeight);
                await RunAsync(_from, _to);
            }
        }

        private void OnPressed(object sender, PointerRoutedEventArgs e) {
            bool mouse = e.Pointer.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse;
            var pointer = e.GetCurrentPoint(_image);
            if (mouse && !pointer.Properties.IsLeftButtonPressed)
                return;

            var pt = pointer.Position;
            _start = pt;
            _selection.Visibility = Visibility.Visible;
            _rect.Rect = new Rect(pt.X, pt.Y, 0, 0);
            _isSelecting = true;
            _image.CapturePointer(e.Pointer);

        }

        public async Task RunAsync(Complex from, Complex to) {
            _from = from; _to = to;
            int width = _bmp.PixelWidth, height = _bmp.PixelHeight;
            double deltax = (to.Real - from.Real) / width;
            double deltay = (to.Imaginary - from.Imaginary) / height;
            byte[] bytes = new byte[width * 4];
            var buffer = _bmp.PixelBuffer.AsStream();
            for (int y = 0; y < height; y++) {
                await Task.Run(() => {
                    Parallel.For(0, width, x => {
                        int pixel = MandelbrotColor(from + new Complex(x * deltax, y * deltay));
                        int pos = x * 4;
                        bytes[pos] = (byte)(pixel & 0xff);
                        bytes[pos + 1] = (byte)((pixel >> 8) & 0xff);
                        bytes[pos + 2] = (byte)((pixel >> 16) & 0xff);
                        bytes[pos + 3] = 255;
                    });
                    buffer.Write(bytes, 0, bytes.Length);
                });
                _bmp.Invalidate();
            }
            await UpdateTile();
        }

        int imageid = 0;

        private async Task UpdateTile() {
            // save image
            if (imageid > 0) {
                try {
                    await (await ApplicationData.Current.LocalFolder.GetFileAsync(string.Format("tile{0}.jpg", imageid))).DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch { }
            }
            imageid++;
            var name = string.Format("tile{0}.jpg", imageid);
            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
            var dpi = DisplayInformation.GetForCurrentView().LogicalDpi;
            using (var stm = await file.OpenAsync(FileAccessMode.ReadWrite)) {
                var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stm);
                enc.IsThumbnailGenerated = true;
                enc.GeneratedThumbnailHeight = enc.GeneratedThumbnailWidth = 150;
                enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)_bmp.PixelWidth, (uint)_bmp.PixelHeight, dpi, dpi, _bmp.PixelBuffer.ToArray());
                await enc.FlushAsync();
            }

            var template = TileUpdateManager.GetTemplateContent(TileTemplateType.TileSquare150x150Image);
            var image = template.GetElementsByTagName("image").First();
            var src = template.CreateAttribute("src");
            src.Value = "ms-appdata:///local/" + name;
            image.Attributes.SetNamedItem(src);
            var tile = new TileNotification(template);
            TileUpdateManager.CreateTileUpdaterForApplication().Update(tile);
        }
    }
}

