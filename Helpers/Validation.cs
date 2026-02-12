using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RtspPlayer.Helpers
{
    /// <summary>
    /// כלי עזר לבדיקת תקינות כתובות RTSP וקבצי וידאו
    /// </summary>
    public static class Validation
    {
        private static readonly Regex RtspUrlPattern = new Regex(
            @"^rtsp://.+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// בודק אם כתובת RTSP או קובץ וידאו תקין
        /// </summary>
        /// <param name="url">כתובת RTSP או נתיב לקובץ וידאו</param>
        /// <returns>true אם הכתובת תקינה, אחרת false</returns>
        public static bool IsValidRtspUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            // תמיכה בקבצי וידאו מקומיים (file:// או נתיב ישיר)
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase) || 
                System.IO.File.Exists(url))
            {
                // בדיקת סיומת קובץ וידאו
                string extension = System.IO.Path.GetExtension(url).ToLower();
                string[] videoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
                if (videoExtensions.Contains(extension))
                {
                    return true;
                }
            }

            // בדיקה בסיסית שהכתובת מתחילה ב-rtsp://
            if (!RtspUrlPattern.IsMatch(url))
            {
                return false;
            }

            // בדיקה שהכתובת היא URI תקין
            try
            {
                var uri = new Uri(url);
                return uri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
