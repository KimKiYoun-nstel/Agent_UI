using System;
using System.Text;

namespace Agent.UI.Wpf.Services
{
    /// <summary>
    /// 전역 로거 유틸리티
    /// 레벨: Info(0) < Trace(1) < Debug(2) (숫자가 클수록 상세)
    /// 외부 서비스는 기존 Action<string> 콜백을 사용하므로 AcceptExternal을 통해 수신 가능
    /// </summary>
    public static class Logger
    {
        public enum Level : int { Info = 0, Trace = 1, Debug = 2 }

        /// <summary>현재 허용되는 최대 레벨(verbosity). 기본은 Info</summary>
        public static Level CurrentLevel { get; set; } = Level.Info;

        /// <summary>UI 또는 외부로 로그 문자열을 전달하는 콜백(예: MainViewModel.Log)</summary>
        public static Action<string>? Sink;

        /// <summary>외부 애셈블리/서비스가 기존 Action<string> 형태로 로그를 전달할 때 사용</summary>
        public static void AcceptExternal(string s) => Info(s);

        public static void Info(string s)
        {
            if ((int)CurrentLevel >= (int)Level.Info)
            {
                try { Sink?.Invoke($"[INFO] {s}"); } catch { }
            }
        }

        public static void Trace(string s)
        {
            if ((int)CurrentLevel >= (int)Level.Trace)
            {
                try { Sink?.Invoke($"[TRACE] {s}"); } catch { }
            }
        }

        public static void Debug(string s)
        {
            if ((int)CurrentLevel >= (int)Level.Debug)
            {
                try { Sink?.Invoke($"[DEBUG] {s}"); } catch { }
            }
        }

        /// <summary>
        /// 주어진 문자열을 UTF8 바이트 기준으로 16KB(=16384)로 트렁케이트합니다.
        /// 트렁케이트된 경우 "...(truncated)"를 덧붙입니다.
        /// </summary>
        public static string TruncateUtf8(string s, int maxBytes = 16 * 1024)
        {
            if (s == null) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length <= maxBytes) return s;
            // 잘라서 유효한 UTF8로 복원
            var truncated = new byte[maxBytes];
            Array.Copy(bytes, 0, truncated, 0, maxBytes);
            // ensure no partial multibyte sequence at end
            int validLen = maxBytes;
            while (validLen > 0)
            {
                try { var str = Encoding.UTF8.GetString(truncated, 0, validLen); return str + "...(truncated)"; }
                catch { validLen--; }
            }
            return "...(truncated)";
        }

        /// <summary>
        /// 바이트 배열을 최대 maxBytes 바이트로 잘라 base64로 변환해 반환. 잘렸다면 표시 추가.
        /// </summary>
        public static string ByteArrayToBase64Truncated(ReadOnlySpan<byte> src, int maxBytes = 16 * 1024)
        {
            if (src.Length <= maxBytes) return Convert.ToBase64String(src.ToArray());
            var arr = new byte[maxBytes];
            src.Slice(0, maxBytes).CopyTo(arr);
            return Convert.ToBase64String(arr) + $"...(truncated {src.Length - maxBytes} bytes)";
        }
    }
}
