using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using LibVLCSharp.Shared;
using RtspPlayer.Helpers;
using RtspPlayer.Services;

namespace RtspPlayer
{
    public partial class MainWindow : Window
    {
        private readonly RtspService _rtspService;

        public MainWindow()
        {
            InitializeComponent();
            
            // יצירת שירות RTSP (Core.Initialize() כבר נקרא ב-App.xaml.cs)
            _rtspService = new RtspService();
            
            // חיבור לאירועים
            _rtspService.StatusChanged += OnStatusChanged;
            _rtspService.ErrorOccurred += OnErrorOccurred;
            
            // הגדרת MediaPlayer ל-VideoView
            VideoView.MediaPlayer = _rtspService.MediaPlayer;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "קבצי וידאו|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.m4v|כל הקבצים|*.*",
                Title = "בחר קובץ וידאו"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                RtspUrlComboBox.Text = openFileDialog.FileName;
            }
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            string url = RtspUrlComboBox.Text.Trim();

            // בדיקת תקינות URL
            if (string.IsNullOrWhiteSpace(url) || url.Contains("(בחר קובץ"))
            {
                System.Windows.MessageBox.Show("אנא בחר קובץ וידאו (כפתור 'עיון...') או הזן כתובת RTSP", 
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // המרת נתיב מקומי ל-file:// אם צריך
            if (File.Exists(url) && !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                url = "file:///" + url.Replace("\\", "/");
            }

            if (!Validation.IsValidRtspUrl(url))
            {
                System.Windows.MessageBox.Show("כתובת RTSP או קובץ לא תקין", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // עדכון UI
            PlayButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusTextBlock.Text = "מתחבר...";

            try
            {
                // התחלת ניגון (async)
                await _rtspService.PlayAsync(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "שגיאה";
                PlayButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _rtspService.Stop();
                StatusTextBlock.Text = "עצור";
                PlayButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"שגיאה בעת עצירה: {ex.Message}", "שגיאה", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnStatusChanged(object? sender, string status)
        {
            // עדכון UI מתוך UI thread
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = status;
            });
        }

        private void OnErrorOccurred(object? sender, string errorMessage)
        {
            // עדכון UI מתוך UI thread
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"שגיאה: {errorMessage}";
                
                // הודעה מפורטת יותר
                string detailedMessage = $"לא ניתן להתחבר לזרם או שאין וידאו.\n\n{errorMessage}\n\n" +
                    "טיפים:\n" +
                    "• נסה קובץ וידאו מקומי (כפתור 'עיון...')\n" +
                    "• בדוק שהכתובת RTSP תקינה\n" +
                    "• ודא שיש חיבור אינטרנט (אם זה RTSP חיצוני)\n" +
                    "• נסה לבדוק את הכתובת ב-VLC Media Player";
                
                MessageBox.Show(detailedMessage, 
                    "שגיאה", MessageBoxButton.OK, MessageBoxImage.Warning);
                PlayButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // ניקוי משאבים בעת סגירה
            try
            {
                _rtspService?.Stop();
                _rtspService?.Dispose();
            }
            catch (Exception ex)
            {
                // לוג שגיאה אם יש צורך
                System.Diagnostics.Debug.WriteLine($"Error disposing resources: {ex.Message}");
            }
        }
    }
}
