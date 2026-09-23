using GeniaProxy.Services;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfClipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace GeniaProxy
{
    public sealed partial class ShareProfileWindow : Window
    {
        private readonly ProfileQrData qrData;
        private readonly BitmapImage qrImage;

        public ShareProfileWindow(ProfileQrData qrData)
        {
            ArgumentNullException.ThrowIfNull(qrData);
            InitializeComponent();

            this.qrData = qrData;
            qrImage = CreateBitmap(qrData.PngBytes);
            QrImage.Source = qrImage;
            ProfileTitleText.Text = qrData.ProfileName;
            PayloadInformationText.Text =
                $"Мобильная ссылка {qrData.DisplayFormat} · {qrData.PayloadBytes} байт";
        }

        private void CopyQrButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                WpfClipboard.SetImage(qrImage);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось скопировать QR-код.", ex);
            }
        }

        private void CopyLinkButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                WpfClipboard.SetText(qrData.Payload);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось скопировать ссылку профиля.", ex);
            }
        }

        private void SaveQrButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog = new WpfSaveFileDialog
            {
                Title = "Сохранить QR-код профиля",
                Filter = "PNG (*.png)|*.png",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = "GeniaProxy-" +
                    qrData.ProfileName + "-QR.png"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                File.WriteAllBytes(dialog.FileName, qrData.PngBytes);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить QR-код.", ex);
            }
        }

        private void ShowError(string message, Exception exception)
        {
            WpfMessageBox.Show(
                this,
                message + "\n\n" + exception.Message,
                "GeniaProxy",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        private static BitmapImage CreateBitmap(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
