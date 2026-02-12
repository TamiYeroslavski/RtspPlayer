using System;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace RtspPlayer.Services
{
    /// <summary>
    /// שירות לניהול ניגון RTSP באמצעות LibVLC
    /// </summary>
    public class RtspService : IDisposable
    {
        private LibVLC? _libVlc;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private bool _disposed = false;

        public MediaPlayer? MediaPlayer => _mediaPlayer;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? ErrorOccurred;

        public RtspService()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // יצירת instance של LibVLC (מופעל debug logs לדיבוג)
                _libVlc = new LibVLC(enableDebugLogs: true);
                
                // חיבור ללוגים של LibVLC (אופציונלי - להסיר ב-production)
                _libVlc.Log += (sender, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[LibVLC] {e.Level}: {e.Message}");
                };

                // יצירת MediaPlayer
                _mediaPlayer = new MediaPlayer(_libVlc);

                // חיבור לאירועי שגיאה
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.Stopped += MediaPlayer_Stopped;
                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.Buffering += MediaPlayer_Buffering;
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"שגיאה באתחול: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// התחלת ניגון RTSP stream
        /// </summary>
        public async Task PlayAsync(string rtspUrl)
        {
            var mediaPlayer = _mediaPlayer;
            var libVlc = _libVlc;
            if (mediaPlayer == null || libVlc == null)
            {
                throw new InvalidOperationException("MediaPlayer לא אותחל");
            }

            try
            {
                // עצירת ניגון קודם אם יש
                mediaPlayer.Stop();
                
                // שחרור Media קודם אם יש
                _currentMedia?.Dispose();
                _currentMedia = null;

                // בדיקה אם זה RTSP או קובץ מקומי
                bool isRtsp = rtspUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase);
                
                if (isRtsp)
                {
                    OnStatusChanged("מתחבר...");
                }
                else
                {
                    OnStatusChanged("טוען קובץ...");
                }

                // יצירת Media מה-URL (שומרים אותו כדי שלא ישתחרר)
                _currentMedia = new Media(libVlc, rtspUrl, FromType.FromLocation);
                var currentMedia = _currentMedia;

                // הגדרת אפשרויות רק ל-RTSP
                if (isRtsp)
                {
                    // אפשרויות RTSP מותאמות לחיבור טוב יותר
                    _currentMedia.AddOption(":network-caching=1000");
                    _currentMedia.AddOption(":rtsp-tcp");  // שימוש ב-TCP במקום UDP (יציב יותר)
                    _currentMedia.AddOption(":rtsp-frame-buffer-size=500000");
                    _currentMedia.AddOption(":live-caching=1000");
                    _currentMedia.AddOption(":clock-jitter=0");
                    _currentMedia.AddOption(":clock-synchro=0");
                    _currentMedia.AddOption(":rtsp-timeout=5000");  // timeout של 5 שניות
                }
                else
                {
                    // אפשרויות לקבצים מקומיים
                    _currentMedia.AddOption(":file-caching=1000");
                }

                // ניגון (לא חוסם UI)
                var playResult = await Task.Run(() =>
                {
                    try
                    {
                        return mediaPlayer.Play(currentMedia);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in Play: {ex.Message}");
                        return false;
                    }
                });

                if (!playResult)
                {
                    OnErrorOccurred("לא ניתן להתחיל ניגון. בדוק שהכתובת תקינה.");
                    return;
                }

                // המתנה ארוכה יותר ל-RTSP כדי לאפשר חיבור
                int maxRetries = isRtsp ? 60 : 30;  // 6 שניות ל-RTSP, 3 שניות לקובץ
                int retries = 0;
                bool isPlaying = false;
                
                while (retries < maxRetries)
                {
                    var state = mediaPlayer.State;
                    
                    if (state == VLCState.Playing || state == VLCState.Buffering)
                    {
                        isPlaying = true;
                        break;
                    }
                    
                    if (state == VLCState.Error)
                    {
                        // בדיקה אם זה באמת שגיאה או רק חיבור איטי
                        await Task.Delay(200);
                        if (mediaPlayer.State == VLCState.Error)
                        {
                            OnErrorOccurred("לא ניתן להתחבר לזרם. בדוק שהכתובת תקינה והשרת פעיל.");
                            return;
                        }
                    }
                    
                    await Task.Delay(100);
                    retries++;
                    
                    // עדכון סטטוס במהלך המתנה
                    if (isRtsp && retries % 10 == 0)
                    {
                        OnStatusChanged($"מתחבר... ({retries * 100}ms)");
                    }
                }

                // בדיקה סופית
                {
                    var finalState = mediaPlayer.State;
                    if (finalState == VLCState.Error)
                    {
                        OnErrorOccurred("לא ניתן להתחבר לזרם. נסה כתובת אחרת או קובץ וידאו מקומי.");
                    }
                    else if (finalState == VLCState.Buffering)
                    {
                        // Buffering זה בסדר - זה אומר שהחיבור עובד
                        OnStatusChanged("טוען...");
                    }
                    else if (!isPlaying && finalState != VLCState.Playing)
                    {
                        // אם לא הגענו למצב Playing אחרי כל ההמתנה
                        OnErrorOccurred("החיבור איטי מדי או שהזרם לא זמין. נסה כתובת אחרת.");
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"שגיאה בניגון: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// עצירת ניגון ושחרור משאבים
        /// </summary>
        public void Stop()
        {
            try
            {
                _mediaPlayer?.Stop();
                
                // שחרור Media
                _currentMedia?.Dispose();
                _currentMedia = null;
                
                OnStatusChanged("עצור");
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"שגיאה בעת עצירה: {ex.Message}");
            }
        }

        #region Event Handlers

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            // בדיקה אם זה באמת שגיאה או רק חיבור איטי
            if (_mediaPlayer != null && _mediaPlayer.State == VLCState.Error)
            {
                OnErrorOccurred("שגיאה בזרם הווידאו. בדוק שהכתובת תקינה והשרת פעיל.");
            }
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            OnStatusChanged("הזרם הסתיים");
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            OnStatusChanged("עצור");
        }

        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            OnStatusChanged("מנגן");
        }

        private void MediaPlayer_Buffering(object? sender, MediaPlayerBufferingEventArgs e)
        {
            if (e.Cache < 100)
            {
                OnStatusChanged($"טוען... {e.Cache}%");
            }
        }

        #endregion

        #region Event Invokers

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnErrorOccurred(string errorMessage)
        {
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // ניתוק אירועים
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                        _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                        _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                        _mediaPlayer.Playing -= MediaPlayer_Playing;
                        _mediaPlayer.Buffering -= MediaPlayer_Buffering;
                    }

                    // עצירה ושחרור משאבים
                    _mediaPlayer?.Stop();
                    _currentMedia?.Dispose();
                    _mediaPlayer?.Dispose();
                    _libVlc?.Dispose();

                    _currentMedia = null;
                    _mediaPlayer = null;
                    _libVlc = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing RtspService: {ex.Message}");
                }

                _disposed = true;
            }
        }

        #endregion
    }
}
