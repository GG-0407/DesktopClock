using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopClock
{
    // ============================== 入口 ==============================

    static class Program
    {
        // 诊断模式：启动后直接打开设置窗口，转储透明度控件状态到 _diag.txt，然后退出。
        // 用法：DesktopClock.exe --diag  （不影响正常启动）
        static void RunDiag()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_diag.txt");
            try
            {
                StringBuilder log = new StringBuilder();
                Settings s = new Settings(); s.Load();
                SettingsForm f = new SettingsForm(s, null);
                f.Show();
                for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
                log.AppendLine("Form shown OK, W=" + f.Width + " H=" + f.Height);
                WalkControls(f.Controls, log);
                File.WriteAllText(p, log.ToString());
                f.Dispose();
            }
            catch (Exception ex) { File.WriteAllText(p, "EXCEPTION: " + ex); }
        }

        static void WalkControls(Control.ControlCollection cols, StringBuilder log)
        {
            foreach (Control c in cols)
            {
                if (c is AlphaBar)
                {
                    AlphaBar ab = (AlphaBar)c;
                    log.AppendLine("AlphaBar   Bounds=" + ab.Bounds + " Visible=" + ab.Visible
                                 + " Enabled=" + ab.Enabled + " Value=" + ab.Value
                                 + " HandleCreated=" + ab.IsHandleCreated);
                }
                else if (c is TrackBar)
                    log.AppendLine("TrackBar   Bounds=" + c.Bounds + " Visible=" + c.Visible
                                 + " Value=" + ((TrackBar)c).Value);
                else if (c.GetType().FullName.IndexOf("AlphaBar") >= 0)
                    log.AppendLine("UnknownAlphaBar  Bounds=" + c.Bounds + " Visible=" + c.Visible);
                if (c.HasChildren) WalkControls(c.Controls, log);
            }
        }

        [STAThread]
        static void Main()
        {
            string[] cli = Environment.GetCommandLineArgs();
            if (Array.IndexOf(cli, "--diag") >= 0)
            {
                RunDiag();
                return;
            }

            // 注：不声明 DPI 感知。若声明，分层窗口在 125% 缩放下会被系统二次缩放导致文字发灰。
            // 保持非感知，让系统统一按 96 DPI 虚拟化处理（已验证正常）。

            string crashLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                try { File.WriteAllText(crashLog, e.Exception.ToString()); } catch { }
                Environment.Exit(1);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { File.WriteAllText(crashLog, (e.ExceptionObject ?? new object()).ToString()); } catch { }
                Environment.Exit(1);
            };

            bool createdNew;
            using (var mutex = new Mutex(true, "Local\\DesktopClock_Singleton", out createdNew))
            {
                if (!createdNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ClockForm());
            }
        }
    }

    // ============================== INI 读写 ==============================

    public static class Ini
    {
        static readonly string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopClock.ini");

        public static string Get(string key, string def)
        {
            if (!File.Exists(path)) return def;
            try
            {
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("[") || s.IndexOf('=') < 0) continue;
                    int eq = s.IndexOf('=');
                    if (s.Substring(0, eq).Trim() == key) return s.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return def;
        }

        public static void Set(string key, string value)
        {
            try
            {
                List<string> lines = new List<string>();
                if (File.Exists(path)) lines.AddRange(File.ReadAllLines(path, Encoding.UTF8));
                int idx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string s = lines[i].Trim();
                    if (s.Length == 0 || s.StartsWith(";") || s.StartsWith("[") || s.IndexOf('=') < 0) continue;
                    int eq = s.IndexOf('=');
                    if (s.Substring(0, eq).Trim() == key) { idx = i; break; }
                }
                if (idx >= 0) lines[idx] = key + "=" + value;
                else
                {
                    if (lines.Count == 0) lines.Add("[Settings]");
                    lines.Add(key + "=" + value);
                }
                File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ============================== 配置 ==============================

    public class Settings
    {
        public string Font        = "微软雅黑 Light";   // 时间字体
        public string InfoFont    = "微软雅黑 Light";   // 信息字体
        public int    TimeSize    = 64;
        public int    TextColor   = 0xF2F2F2;
        public int    AccentColor = 0xE8B56D;
        public int    BgColor     = 0x101010;
        public int    BgAlpha     = 70;                // 0 = 全透明
        public bool   ShowSeconds = true;
        public bool   AlwaysOnTop = true;
        public bool   ClickThrough = false;
        public bool   AutoStart   = false;
        public bool   Locked      = false;
        public string Position    = "remember";
        public int    PosX = -1, PosY = -1;
        public bool   ShowWeather = true;
        public string CityName    = "北京";
        public string CityCode    = "101010100";

        public void Load()
        {
            Font        = Ini.Get("Font", Font);
            InfoFont    = Ini.Get("InfoFont", InfoFont);
            TimeSize    = ToInt(Ini.Get("TimeSize", TimeSize.ToString()));
            TextColor   = ParseHex(Ini.Get("TextColor", "F2F2F2"));
            AccentColor = ParseHex(Ini.Get("AccentColor", "E8B56D"));
            BgColor     = ParseHex(Ini.Get("BgColor", "101010"));
            BgAlpha     = ToInt(Ini.Get("BgAlpha", "70"));
            ShowSeconds = ToInt(Ini.Get("ShowSeconds", "1")) != 0;
            AlwaysOnTop = ToInt(Ini.Get("AlwaysOnTop", "1")) != 0;
            ClickThrough = ToInt(Ini.Get("ClickThrough", "0")) != 0;
            AutoStart   = ToInt(Ini.Get("AutoStart", "0")) != 0;
            Locked      = ToInt(Ini.Get("Locked", "0")) != 0;
            Position    = Ini.Get("Position", "remember");
            PosX        = ToInt(Ini.Get("PosX", "-1"));
            PosY        = ToInt(Ini.Get("PosY", "-1"));
            ShowWeather = ToInt(Ini.Get("ShowWeather", "1")) != 0;
            CityName    = Ini.Get("CityName", "北京");
            CityCode    = Ini.Get("CityCode", "101010100");
            if (TimeSize < 28 || TimeSize > 240) TimeSize = 64;
            if (BgAlpha < 0 || BgAlpha > 255) BgAlpha = 70;
        }

        public void Save()
        {
            Ini.Set("Font", Font);
            Ini.Set("InfoFont", InfoFont);
            Ini.Set("TimeSize", TimeSize.ToString());
            Ini.Set("TextColor", TextColor.ToString("X6"));
            Ini.Set("AccentColor", AccentColor.ToString("X6"));
            Ini.Set("BgColor", BgColor.ToString("X6"));
            Ini.Set("BgAlpha", BgAlpha.ToString());
            Ini.Set("ShowSeconds", ShowSeconds ? "1" : "0");
            Ini.Set("AlwaysOnTop", AlwaysOnTop ? "1" : "0");
            Ini.Set("ClickThrough", ClickThrough ? "1" : "0");
            Ini.Set("AutoStart", AutoStart ? "1" : "0");
            Ini.Set("Locked", Locked ? "1" : "0");
            Ini.Set("Position", Position);
            Ini.Set("PosX", PosX.ToString());
            Ini.Set("PosY", PosY.ToString());
            Ini.Set("ShowWeather", ShowWeather ? "1" : "0");
            Ini.Set("CityName", CityName);
            Ini.Set("CityCode", CityCode);
        }

        static int ParseHex(string s) { int v = 0; try { v = int.Parse(s.Trim(), NumberStyles.HexNumber); } catch { } return v; }
        static int ToInt(string s) { int v = 0; try { v = int.Parse(s.Trim()); } catch { } return v; }
    }

    public static class Colors
    {
        public static Color FromHex(int hex) { return Color.FromArgb(255, (hex >> 16) & 0xFF, (hex >> 8) & 0xFF, hex & 0xFF); }
        public static int ToHex(Color c) { return (c.R << 16) | (c.G << 8) | c.B; }
        public static Color Lighten(Color c, float f)
        {
            int r = (int)(c.R + (255 - c.R) * f);
            int g = (int)(c.G + (255 - c.G) * f);
            int b = (int)(c.B + (255 - c.B) * f);
            return Color.FromArgb(c.A, r, g, b);
        }
    }

    // ============================== 天气服务（中国天气网，免 key） ==============================

    public class CityResult { public string Code; public string Display; }

    public class WeatherInfo
    {
        public string City, Weather, Temp, High, Low, Wind, Humidity, Display;
        public bool Ok;
    }

    public static class WeatherService
    {
        static string Get(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 8000;
            req.ReadWriteTimeout = 8000;
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            req.Referer = "http://www.weather.com.cn/";
            req.Accept = "*/*";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (System.IO.Stream st = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                st.CopyTo(ms);
                return Decode(ms.ToArray(), resp.ContentType);
            }
        }

        static string Decode(byte[] data, string ct)
        {
            string enc = "utf-8";
            if (!string.IsNullOrEmpty(ct))
            {
                string l = ct.ToLowerInvariant();
                if (l.Contains("gb2312") || l.Contains("gbk") || l.Contains("gb18030")) enc = "gbk";
            }
            if (enc == "utf-8")
            {
                try
                {
                    string s = new UTF8Encoding(false, true).GetString(data);
                    if (!s.Contains("�")) return s;
                }
                catch { }
            }
            return Encoding.GetEncoding(936).GetString(data);
        }

        static string JGet(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static WeatherInfo Fetch(string code, string name)
        {
            WeatherInfo info = new WeatherInfo();
            if (string.IsNullOrEmpty(code)) return info;
            try
            {
                // 实时：d1.weather.com.cn/sk_2d（纯 http，规避 TLS 指纹屏蔽）
                // 预报：d1.weather.com.cn/dingzhi
                string rt = Get("http://d1.weather.com.cn/sk_2d/" + code + ".html");
                string fc = Get("http://d1.weather.com.cn/dingzhi/" + code + ".html");

                string city = JGet(rt, "cityname");
                if (string.IsNullOrEmpty(city)) city = JGet(fc, "cityname");
                string weather = JGet(fc, "weather");
                string temp = JGet(rt, "temp");       // 实时气温
                string h = JGet(fc, "temp");          // 预报最高
                string l = JGet(fc, "tempn");         // 预报最低
                string wind = (JGet(rt, "WD") ?? "") + (JGet(rt, "WS") ?? "");
                if (string.IsNullOrEmpty(wind)) wind = (JGet(fc, "wd") ?? "") + (JGet(fc, "ws") ?? "");
                string humidity = JGet(rt, "SD");     // 湿度

                if (string.IsNullOrEmpty(weather) && string.IsNullOrEmpty(temp)) return info;

                info.City = string.IsNullOrEmpty(city) ? name : city;
                info.Weather = weather;
                info.Temp = TrimTemp(temp);
                info.High = TrimTemp(h);
                info.Low = TrimTemp(l);
                info.Wind = wind;
                info.Humidity = humidity;
                BuildDisplay(info);
                info.Ok = true;
            }
            catch { }
            return info;
        }

        static string TrimTemp(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace("℃", "").Replace("°", "").Replace("C", "").Trim();
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return ((int)Math.Round(d)).ToString();
            return s;
        }

        static void BuildDisplay(WeatherInfo w)
        {
            List<string> parts = new List<string>();
            parts.Add(w.City);
            if (!string.IsNullOrEmpty(w.Weather)) parts.Add(w.Weather);
            if (!string.IsNullOrEmpty(w.Temp)) parts.Add(w.Temp + "℃");
            if (!string.IsNullOrEmpty(w.High) && !string.IsNullOrEmpty(w.Low)) parts.Add(w.High + "~" + w.Low + "℃");
            if (!string.IsNullOrEmpty(w.Wind)) parts.Add(w.Wind);
            if (!string.IsNullOrEmpty(w.Humidity)) parts.Add("湿度" + w.Humidity);
            w.Display = string.Join("  ", parts.ToArray());
        }

        public static List<CityResult> Search(string name)
        {
            List<CityResult> list = new List<CityResult>();
            if (string.IsNullOrEmpty(name)) return list;
            try
            {
                string url = "http://toy1.weather.com.cn/search?cityname=" + Uri.EscapeDataString(name);
                string json = Get(url);
                HashSet<string> seen = new HashSet<string>();
                List<CityResult> exact = new List<CityResult>();
                List<CityResult> startsWith = new List<CityResult>();
                List<CityResult> rest = new List<CityResult>();
                foreach (Match m in Regex.Matches(json, "\"([0-9]{6,12})~([^~]*)~([^~]*)~([^~]*)~([^~]*)~"))
                {
                    string code = m.Groups[1].Value;
                    string pinyin = m.Groups[2].Value;
                    string nameCn = m.Groups[3].Value;
                    string nameCn2 = m.Groups[5].Value;
                    if (seen.Contains(code)) continue;
                    seen.Add(code);
                    if (code.Length > 9) continue;   // 跳过过长后缀码
                    string disp = string.IsNullOrEmpty(nameCn2) ? nameCn : nameCn2;
                    CityResult r = new CityResult();
                    r.Code = code;
                    r.Display = disp;
                    if (disp == name) exact.Add(r);
                    else if (disp.StartsWith(name, StringComparison.Ordinal)) startsWith.Add(r);
                    else rest.Add(r);
                }
                list.AddRange(exact);
                list.AddRange(startsWith);
                list.AddRange(rest);
                if (list.Count > 30) list.RemoveRange(30, list.Count - 30);
            }
            catch { }
            return list;
        }

        // 常用城市列表（名称, 城市码）
        public static readonly string[][] MajorCities = new string[][] {
            new string[]{"北京","101010100"}, new string[]{"上海","101020100"}, new string[]{"天津","101030100"}, new string[]{"重庆","101040100"},
            new string[]{"广州","101280101"}, new string[]{"深圳","101280601"}, new string[]{"珠海","101280701"}, new string[]{"东莞","101281601"}, new string[]{"佛山","101280800"},
            new string[]{"杭州","101210101"}, new string[]{"南京","101190101"}, new string[]{"苏州","101190401"}, new string[]{"无锡","101190201"}, new string[]{"宁波","101210401"}, new string[]{"温州","101210701"},
            new string[]{"武汉","101200101"}, new string[]{"长沙","101250101"}, new string[]{"成都","101270101"}, new string[]{"西安","101110101"},
            new string[]{"济南","101120101"}, new string[]{"青岛","101120201"}, new string[]{"郑州","101180101"}, new string[]{"石家庄","101090101"}, new string[]{"太原","101100101"},
            new string[]{"沈阳","101070101"}, new string[]{"大连","101070201"}, new string[]{"长春","101060101"}, new string[]{"哈尔滨","101050101"},
            new string[]{"福州","101230101"}, new string[]{"厦门","101230201"}, new string[]{"合肥","101220101"}, new string[]{"南昌","101240101"},
            new string[]{"南宁","101300101"}, new string[]{"桂林","101300501"}, new string[]{"海口","101310101"}, new string[]{"三亚","101310201"},
            new string[]{"贵阳","101260101"}, new string[]{"昆明","101290101"}, new string[]{"兰州","101160101"}, new string[]{"西宁","101150101"},
            new string[]{"银川","101170101"}, new string[]{"乌鲁木齐","101130101"}, new string[]{"呼和浩特","101080101"}, new string[]{"拉萨","101140101"},
            new string[]{"香港","101320101"}, new string[]{"澳门","101330101"}, new string[]{"台北","101340101"}
        };
    }

    // ============================== 渲染器 ==============================

    public class FontChoice
    {
        public string Display;
        public string Family;
        public override string ToString() { return Display; }
    }

    static class ClockRenderer
    {
        static readonly string[] fontCandidates = new string[] {
            "微软雅黑 Light", "微软雅黑", "微软雅黑 UI", "等线 Light", "等线",
            "华文行楷", "华文新魏", "华文楷体", "华文隶书", "华文琥珀", "华文中宋", "华文细黑", "华文彩云",
            "隶书", "楷体", "宋体", "黑体", "仿宋", "幼圆",
            "方正舒体", "方正姚体", "方正魏碑", "方正小标宋", "方正准圆",
            "汉仪粗宋", "汉仪楷体", "汉仪行楷", "汉仪大黑", "汉仪颜楷",
            "Segoe UI", "Segoe UI Light", "Consolas", "Arial", "Georgia"
        };

        static PrivateFontCollection bundledFonts = new PrivateFontCollection();
        static InstalledFontCollection installedFonts;
        static Dictionary<string, FontFamily> familyMap = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);

        static ClockRenderer()
        {
            try
            {
                installedFonts = new InstalledFontCollection();
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, "*.ttf"))
                    {
                        try { bundledFonts.AddFontFile(f); }
                        catch { /* 单个字体损坏不影响其他字体 */ }
                    }
                }
                foreach (FontFamily f in bundledFonts.Families)
                    if (!familyMap.ContainsKey(f.Name)) familyMap.Add(f.Name, f);
                foreach (FontFamily f in installedFonts.Families)
                    if (!familyMap.ContainsKey(f.Name)) familyMap.Add(f.Name, f);
            }
            catch { }
        }

        public static FontFamily GetFamily(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                FontFamily ff;
                if (familyMap.TryGetValue(name, out ff)) return ff;
            }
            foreach (KeyValuePair<string, FontFamily> kv in familyMap)
                if (kv.Key.IndexOf("微软雅黑", StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            foreach (KeyValuePair<string, FontFamily> kv in familyMap) return kv.Value;
            return null;
        }

        static string FriendlyName(string fam)
        {
            if (fam.IndexOf("WenKai", StringComparison.OrdinalIgnoreCase) >= 0 || fam.IndexOf("LXGW", StringComparison.OrdinalIgnoreCase) >= 0)
                return "霞鹜文楷（楷体风）";
            if (fam.IndexOf("KuaiLe", StringComparison.OrdinalIgnoreCase) >= 0)
                return "站酷快乐体（可爱）";
            if (fam.IndexOf("QingKe", StringComparison.OrdinalIgnoreCase) >= 0)
                return "站酷庆科黄油体（个性）";
            return fam;
        }

        public static List<FontChoice> FontChoices()
        {
            List<FontChoice> list = new List<FontChoice>();
            foreach (FontFamily f in bundledFonts.Families)
            {
                FontChoice fc = new FontChoice();
                fc.Display = FriendlyName(f.Name);
                fc.Family = f.Name;
                list.Add(fc);
            }
            foreach (string n in InstalledCandidates())
            {
                FontChoice fc = new FontChoice();
                fc.Display = n;
                fc.Family = n;
                list.Add(fc);
            }
            return list;
        }

        public static string FamilyOf(string display)
        {
            foreach (FontChoice fc in FontChoices())
                if (fc.Display == display) return fc.Family;
            return display;
        }

        public static List<string> InstalledCandidates()
        {
            List<string> ordered = new List<string>();
            List<string> installed = new List<string>();
            try
            {
                using (InstalledFontCollection fc = new InstalledFontCollection())
                {
                    foreach (FontFamily f in fc.Families)
                        foreach (string cand in fontCandidates)
                            if (string.Equals(f.Name, cand, StringComparison.OrdinalIgnoreCase)) { installed.Add(f.Name); break; }
                }
            }
            catch { }
            foreach (string cand in fontCandidates)
                foreach (string n in installed)
                    if (string.Equals(n, cand, StringComparison.OrdinalIgnoreCase) && !ordered.Contains(n)) ordered.Add(n);
            foreach (string n in installed)
                if (!ordered.Contains(n)) ordered.Add(n);
            return ordered;
        }

        public static Bitmap Render(Settings cfg, DateTime now, string weatherLine, out int w, out int h)
        {
            FontFamily famT = GetFamily(cfg.Font);
            FontFamily famI = GetFamily(cfg.InfoFont);
            if (famT == null) famT = GetFamily("微软雅黑");
            if (famI == null) famI = GetFamily("微软雅黑");
            float ts = cfg.TimeSize;
            float secSize = Math.Max(10f, ts * 0.42f);
            float infoSize = Math.Max(10f, ts * 0.26f);
            float weaSize = Math.Max(9f, ts * 0.20f);

            using (Font fTime = new Font(famT, ts, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font fSec = new Font(famT, secSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font fInfo = new Font(famI, infoSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font fInfoB = new Font(famI, infoSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font fWea = new Font(famI, weaSize, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                string timeMain = now.ToString("HH:mm");
                string timeSec = cfg.ShowSeconds ? now.ToString("ss") : null;
                string dateLine = string.Format("{0}年{1}月{2}日 星期{3}", now.Year, now.Month, now.Day, "日一二三四五六".Substring((int)now.DayOfWeek, 1));
                string festival;
                bool hasFes;
                string lunarLine = ChineseCalendar.LunarToString(now, out hasFes, out festival);
                string accent = hasFes ? festival : ChineseCalendar.GetSolarTermName(now);

                // 测量
                float wT, hT, wS = 0, wD, hD, wL, hL, wH = 0, hH = 0, wW = 0, hW = 0;
                using (Bitmap m = new Bitmap(1, 1))
                using (Graphics gm = Graphics.FromImage(m))
                {
                    SizeF s = gm.MeasureString(timeMain, fTime); wT = s.Width; hT = s.Height;
                    if (timeSec != null) { s = gm.MeasureString(timeSec, fSec); wS = s.Width; }
                    s = gm.MeasureString(dateLine, fInfo); wD = s.Width; hD = s.Height;
                    s = gm.MeasureString(lunarLine, fInfo); wL = s.Width; hL = s.Height;
                    if (accent != null) { s = gm.MeasureString(accent, fInfoB); wH = s.Width; hH = s.Height; }
                    if (weatherLine != null) { s = gm.MeasureString(weatherLine, fWea); wW = s.Width; hW = s.Height; }
                }

                float wTime = wT + (timeSec != null ? ts * 0.12f + wS : 0);
                float contentW = Math.Max(wTime, Math.Max(wD, wL));
                if (accent != null) contentW = Math.Max(contentW, wH);
                if (weatherLine != null) contentW = Math.Max(contentW, wW);

                float pad = ts * 0.40f;
                float gap = ts * 0.13f;
                float padY = infoSize * 0.25f;

                // 内容高度（与下方绘制顺序严格一致，避免行重叠）
                float contentH = hT + (gap * 0.45f + 2f + gap * 0.60f) + hD + gap + hL + gap;
                if (accent != null) contentH += hH + padY * 0.4f + gap;
                if (weatherLine != null) contentH += hW;

                w = (int)Math.Ceiling(contentW) + (int)(pad * 2);
                h = (int)Math.Ceiling(contentH) + (int)(pad * 2);

                Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    g.Clear(Color.FromArgb(0, 0, 0, 0));

                    float cx = w / 2f;
                    float y = pad;
                    Color textC = Colors.FromHex(cfg.TextColor);
                    Color accentC = Colors.FromHex(cfg.AccentColor);

                    // 背景
                    if (cfg.BgAlpha > 0)
                    {
                        using (SolidBrush bb = new SolidBrush(Color.FromArgb(cfg.BgAlpha, Colors.FromHex(cfg.BgColor))))
                            FillRoundRect(g, 0, 0, w, h, ts * 0.16f, bb);
                    }

                    // 时间（居中 + 渐变 + 阴影）
                    float tx = cx - wTime / 2f;
                    using (SolidBrush sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                        g.DrawString(timeMain, fTime, sh, tx + 1, y + 2);
                    using (LinearGradientBrush lg = new LinearGradientBrush(
                        new RectangleF(0, y, 10, hT), textC, Colors.Lighten(textC, 0.35f), 90f))
                        g.DrawString(timeMain, fTime, lg, tx, y);
                    if (timeSec != null)
                    {
                        float sx = tx + wT + ts * 0.12f;
                        float sy = y + (hT - fSec.Height) * 0.88f;
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(170, textC)))
                            g.DrawString(timeSec, fSec, sb, sx, sy);
                    }
                    y += hT + gap * 0.45f;

                    // 时间下划线（强调色，居中）
                    float ulw = Math.Min(wTime, contentW * 0.60f);
                    using (Pen up = new Pen(Color.FromArgb(190, accentC), 2f))
                        g.DrawLine(up, cx - ulw / 2f, y, cx + ulw / 2f, y);
                    y += 2f + gap * 0.60f;

                    // 公历
                    using (SolidBrush db = new SolidBrush(Color.FromArgb(205, textC)))
                        g.DrawString(dateLine, fInfo, db, cx - wD / 2f, y);
                    y += hD + gap;

                    // 农历
                    using (SolidBrush lb = new SolidBrush(Color.FromArgb(205, textC)))
                        g.DrawString(lunarLine, fInfo, lb, cx - wL / 2f, y);
                    y += hL + gap;

                    // 节日 / 节气徽章
                    if (accent != null)
                    {
                        float padX = infoSize * 0.8f;
                        RectangleF pill = new RectangleF(cx - (wH + padX * 2) / 2f, y - padY, wH + padX * 2, hH + padY * 2);
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(45, accentC)))
                            FillRoundRect(g, pill.X, pill.Y, pill.Width, pill.Height, pill.Height / 2f, pb);
                        using (SolidBrush ab = new SolidBrush(accentC))
                            g.DrawString(accent, fInfoB, ab, cx - wH / 2f, pill.Y + padY * 0.6f);
                        y += hH + padY * 0.4f + gap;
                    }

                    // 天气
                    if (weatherLine != null)
                    {
                        using (SolidBrush wb = new SolidBrush(Color.FromArgb(190, textC)))
                            g.DrawString(weatherLine, fWea, wb, cx - wW / 2f, y);
                    }
                }
                return bmp;
            }
        }

        static void FillRoundRect(Graphics g, float x, float y, float w, float h, float r, Brush brush)
        {
            if (r < 1) { g.FillRectangle(brush, x, y, w, h); return; }
            using (GraphicsPath path = new GraphicsPath())
            {
                float d = r * 2;
                path.AddArc(x, y, d, d, 180, 90);
                path.AddArc(x + w - d, y, d, d, 270, 90);
                path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
                path.AddArc(x, y + h - d, d, d, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }
    }

    // ============================== 主时钟窗口 ==============================

    public class ClockForm : Form
    {
        const int WS_EX_LAYERED     = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW  = 0x00000080;
        const int WS_EX_NOACTIVATE  = 0x08000000;
        const int GWL_EXSTYLE       = -20;
        const int AC_SRC_OVER       = 0x00;
        const int AC_SRC_ALPHA      = 0x01;
        const int ULW_ALPHA         = 0x02;
        const int SWP_NOZORDER      = 0x0004;
        const int SWP_NOACTIVATE    = 0x0010;
        const int SWP_NOOWNERZORDER = 0x0200;
        const int WM_DPICHANGED     = 0x02E0;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);

        static IntPtr GetWindowLongPtr(IntPtr h, int n)
        { return IntPtr.Size == 8 ? GetWindowLongPtr64(h, n) : GetWindowLong32(h, n); }
        static void SetWindowLongPtr(IntPtr h, int n, IntPtr v)
        { if (IntPtr.Size == 8) SetWindowLongPtr64(h, n, v); else SetWindowLong32(h, n, v.ToInt32()); }
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        // ---- 状态 ----
        Settings cfg;
        ContextMenuStrip menu;
        NotifyIcon trayIcon;
        IntPtr trayHicon;
        Icon trayIconObj;
        System.Windows.Forms.Timer timer;
        System.Threading.Timer weatherTimer;
        int weatherBusy;
        WeatherInfo weather;

        Bitmap bmp;
        int winW, winH;
        bool dragging;
        Point dragStart;

        ToolStripMenuItem mLock, mTop, mThru, mWeather, mAuto;

        public ClockForm()
        {
            cfg = new Settings();
            cfg.Load();

            Text = "桌面时钟";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = cfg.AlwaysOnTop;
            StartPosition = FormStartPosition.Manual;
            Width = 400;
            Height = 140;

            if (cfg.AutoStart) SetAutoStart(true);

            CreateTrayIcon();
            BuildMenus();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 200;
            timer.Tick += OnTimerTick;
            timer.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                if (cfg != null && cfg.ClickThrough) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Render();
            ApplyPosition();
            if (cfg.ShowWeather) TriggerWeather();
        }

        void OnTimerTick(object s, EventArgs e)
        {
            Render();
            // 秒走数对齐：让刷新点稳定落在“每个整秒 + 20ms”上。
            // 目标点固定，Windows 计时器的小误差会自动收敛，不会积累漂移。
            // 靠近边界（目标不足 20ms）时把剩余推到下一整秒，避免 1 秒内双刷，
            // 也避免旧逻辑（贴边时 +1500ms 会跳过下一个整秒边界）导致每个数字停留约 2 秒。
            int ms = DateTime.Now.Millisecond;
            int target = 20 - ms;
            if (target <= 0) target += 1000;
            if (target < 20) target += 1000;
            timer.Interval = target;
        }

        // ---------- 托盘图标 ----------

        Icon MakeTrayIcon()
        {
            using (Bitmap b = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Pen p = new Pen(Color.White, 3f))
                        g.DrawEllipse(p, 2, 2, 28, 28);
                    using (Pen p = new Pen(Color.White, 2.5f))
                    {
                        g.DrawLine(p, 16, 16, 16, 8);
                        g.DrawLine(p, 16, 16, 22, 16);
                    }
                    using (SolidBrush cb = new SolidBrush(Color.FromArgb(230, 232, 181, 109)))
                        g.FillEllipse(cb, 14, 14, 4, 4);
                }
                trayHicon = b.GetHicon();
            }
            using (Icon temp = Icon.FromHandle(trayHicon))
            {
                return (Icon)temp.Clone();
            }
        }

        void CreateTrayIcon()
        {
            trayIconObj = MakeTrayIcon();
            trayIcon = new NotifyIcon();
            trayIcon.Icon = trayIconObj;
            trayIcon.Text = "桌面时钟";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { ShowSettings(); };
        }

        // ---------- 右键菜单 ----------

        void BuildMenus()
        {
            menu = new ContextMenuStrip();
            ToolStripMenuItem mSettings = new ToolStripMenuItem("设置…");
            mLock   = new ToolStripMenuItem("锁定位置");
            mTop    = new ToolStripMenuItem("置顶");
            mThru   = new ToolStripMenuItem("鼠标穿透");
            mWeather = new ToolStripMenuItem("显示天气");
            mAuto   = new ToolStripMenuItem("开机自启");
            ToolStripMenuItem mExit = new ToolStripMenuItem("退出");

            mLock.Checked = cfg.Locked;
            mTop.Checked = cfg.AlwaysOnTop;
            mThru.Checked = cfg.ClickThrough;
            mWeather.Checked = cfg.ShowWeather;
            mAuto.Checked = cfg.AutoStart;

            mSettings.Click += delegate { ShowSettings(); };
            // 注意：未设 CheckOnClick 时点击不会自动翻转勾选，必须在这里手动 !Checked，
            // 否则读取到的仍是旧值，开关会完全失效（这就是之前托盘菜单点了没反应的原因）。
            mLock.Click += delegate { mLock.Checked = !mLock.Checked; cfg.Locked = mLock.Checked; cfg.Save(); };
            mTop.Click += delegate { mTop.Checked = !mTop.Checked; cfg.AlwaysOnTop = mTop.Checked; TopMost = cfg.AlwaysOnTop; cfg.Save(); };
            mThru.Click += delegate { mThru.Checked = !mThru.Checked; cfg.ClickThrough = mThru.Checked; ApplyClickThrough(cfg.ClickThrough); cfg.Save(); };
            mWeather.Click += delegate
            {
                mWeather.Checked = !mWeather.Checked;
                cfg.ShowWeather = mWeather.Checked;
                cfg.Save();
                weather = null;
                Render();
                if (cfg.ShowWeather) TriggerWeather();
            };
            mAuto.Click += delegate { mAuto.Checked = !mAuto.Checked; cfg.AutoStart = mAuto.Checked; SetAutoStart(cfg.AutoStart); cfg.Save(); };
            mExit.Click += delegate { Close(); };

            menu.Items.Add(mSettings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mLock);
            menu.Items.Add(mTop);
            menu.Items.Add(mThru);
            menu.Items.Add(mWeather);
            menu.Items.Add(mAuto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(mExit);

            trayIcon.ContextMenuStrip = menu;
        }

        void SyncMenuChecks()
        {
            if (mLock != null) mLock.Checked = cfg.Locked;
            if (mTop != null) mTop.Checked = cfg.AlwaysOnTop;
            if (mThru != null) mThru.Checked = cfg.ClickThrough;
            if (mWeather != null) mWeather.Checked = cfg.ShowWeather;
            if (mAuto != null) mAuto.Checked = cfg.AutoStart;
        }

        // ---------- 天气 ----------

        void TriggerWeather()
        {
            if (weatherTimer == null)
                weatherTimer = new System.Threading.Timer(WeatherTick, null, Timeout.Infinite, 1800000);
            weatherTimer.Change(0, 1800000);
        }

        void WeatherTick(object state)
        {
            if (Interlocked.Exchange(ref weatherBusy, 1) == 1) return;
            try
            {
                WeatherInfo wi = WeatherService.Fetch(cfg.CityCode, cfg.CityName);
                if (wi.Ok && IsHandleCreated)
                {
                    BeginInvoke(new Action(delegate { weather = wi; Render(); }));
                }
            }
            catch { }
            finally { Interlocked.Exchange(ref weatherBusy, 0); }
        }

        void SetCity(string name, string code)
        {
            cfg.CityName = name;
            cfg.CityCode = code;
            weather = null;
            Render();
            if (cfg.ShowWeather) TriggerWeather();
        }

        // ---------- 渲染 ----------

        void Render()
        {
            if (!IsHandleCreated) return;
            string wl = (cfg.ShowWeather && weather != null) ? weather.Display : null;
            int nw, nh;
            Bitmap nb = ClockRenderer.Render(cfg, DateTime.Now, wl, out nw, out nh);

            if (nw != winW || nh != winH)
            {
                winW = nw; winH = nh;
                SetWindowPos(Handle, IntPtr.Zero, Left, Top, winW, winH,
                    (uint)(SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER));
            }
            if (bmp != null) bmp.Dispose();
            bmp = nb;
            UpdateLayered();
        }

        void UpdateLayered()
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = SelectObject(memDc, hBitmap);

            POINT pos = new POINT(); pos.X = Left; pos.Y = Top;
            POINT src = new POINT();
            SIZE size = new SIZE(); size.cx = winW; size.cy = winH;
            BLENDFUNCTION blend = new BLENDFUNCTION();
            blend.BlendOp = AC_SRC_OVER;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = AC_SRC_ALPHA;

            UpdateLayeredWindow(Handle, screenDc, ref pos, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);

            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        // ---------- 位置 ----------

        void ApplyPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int m = 24;
            int x, y;
            string p = cfg.Position;
            if (p == "remember" && cfg.PosX >= 0 && cfg.PosY >= 0)
            {
                Location = new Point(cfg.PosX, cfg.PosY);
                return;
            }
            switch (p)
            {
                case "lt": x = wa.Left + m; y = wa.Top + m; break;
                case "tc": x = wa.Left + (wa.Width - Width) / 2; y = wa.Top + m; break;
                case "rt": x = wa.Right - Width - m; y = wa.Top + m; break;
                case "lb": x = wa.Left + m; y = wa.Bottom - Height - m; break;
                case "bc": x = wa.Left + (wa.Width - Width) / 2; y = wa.Bottom - Height - m; break;
                case "rb": x = wa.Right - Width - m; y = wa.Bottom - Height - m; break;
                case "center":
                    x = wa.Left + (wa.Width - Width) / 2;
                    y = wa.Top + (wa.Height - Height) / 2;
                    break;
                default:
                    x = cfg.PosX >= 0 ? cfg.PosX : wa.Left + (wa.Width - Width) / 2;
                    y = cfg.PosY >= 0 ? cfg.PosY : wa.Top + (wa.Height - Height) / 2;
                    break;
            }
            Location = new Point(x, y);
        }

        // ---------- 鼠标 ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (cfg.Locked)
            {
                base.OnMouseDown(e);
                return;
            }
            if (e.Button == MouseButtons.Left && !cfg.ClickThrough)
            {
                dragging = true;
                dragStart = e.Location;
                Cursor = Cursors.SizeAll;
                Capture = true;
            }
            else if (e.Button == MouseButtons.Right && !cfg.ClickThrough)
            {
                menu.Show(this, e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                Point p = PointToScreen(e.Location);
                Location = new Point(p.X - dragStart.X, p.Y - dragStart.Y);
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging && e.Button == MouseButtons.Left)
            {
                dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                cfg.Position = "remember";
                cfg.PosX = Left;
                cfg.PosY = Top;
                cfg.Save();
            }
            base.OnMouseUp(e);
        }

        // ---------- 设置 ----------

        void ShowSettings()
        {
            using (SettingsForm f = new SettingsForm(cfg, this))
            {
                f.ShowDialog(this);
            }
        }

        public void ApplyLive(Settings s)
        {
            cfg = s;
            TopMost = cfg.AlwaysOnTop;
            ApplyClickThrough(cfg.ClickThrough);
            SyncMenuChecks();
            Render();
            ApplyPosition();
        }

        public void ApplyAndSave(Settings s, bool cityChanged)
        {
            cfg = s;
            TopMost = cfg.AlwaysOnTop;
            ApplyClickThrough(cfg.ClickThrough);
            SetAutoStart(cfg.AutoStart);
            cfg.Save();
            SyncMenuChecks();
            Render();
            ApplyPosition();
            if (cityChanged) SetCity(cfg.CityName, cfg.CityCode);
        }

        void ApplyClickThrough(bool on)
        {
            if (!IsHandleCreated) return;
            IntPtr ex = GetWindowLongPtr(Handle, GWL_EXSTYLE);
            long v = ex.ToInt64();
            if (on) v |= WS_EX_TRANSPARENT; else v &= ~((long)WS_EX_TRANSPARENT);
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(v));
        }

        static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key == null) return;
                    if (on)
                        key.SetValue("DesktopClock", "\"" + Application.ExecutablePath + "\"");
                    else
                        key.DeleteValue("DesktopClock", false);
                }
            }
            catch { }
        }

        // ---------- 收尾 ----------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cfg.Position == "remember")
            {
                cfg.PosX = Left;
                cfg.PosY = Top;
            }
            cfg.Save();
            if (trayIcon != null) trayIcon.Dispose();
            if (trayIconObj != null) trayIconObj.Dispose();
            if (trayHicon != IntPtr.Zero) { try { DestroyIcon(trayHicon); } catch { } trayHicon = IntPtr.Zero; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (weatherTimer != null) { try { weatherTimer.Dispose(); } catch { } weatherTimer = null; }
                if (bmp != null) bmp.Dispose();
                if (timer != null) timer.Dispose();
                if (menu != null) menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ============================== 设置窗口 ==============================

    public class SettingsForm : Form
    {
        Settings work;
        ClockForm clock;

        // 外观
        ComboBox presetCombo, timeFontCombo, infoFontCombo;
        NumericUpDown sizeNum;
        CheckBox secChk;
        Button textBtn, accentBtn, bgBtn;
        AlphaBar alphaBar;
        Label textSw, accentSw, bgSw;
        Label alphaLabel;

        // 布局
        ComboBox posCombo;
        CheckBox lockChk, topChk, thruChk;

        // 天气
        CheckBox weaChk;
        Label curCityLabel;
        ComboBox cityCombo;
        TextBox nameBox, codeBox, searchBox;
        ListBox resultList;
        List<CityResult> results;

        // 常规
        CheckBox autoChk;

        // Tab 页引用（OnShown 时要按实际宽度重排控件，构造时 pg.Width 还是默认值 200）
        TabPage pgLook, pgLayout, pgWeather;

        bool suppress;

        static readonly string[] presets = {
            "简洁  微软雅黑 Light", "经典  微软雅黑", "楷书  楷体", "黑体  黑体",
            "仿宋  仿宋", "等线  等线", "快乐  站酷快乐体（可爱）"
        };
        static readonly string[] posKeys = { "remember", "lt", "tc", "rt", "lb", "bc", "rb", "center" };
        static readonly string[] posNames = { "记住当前位置", "左上角", "顶部居中", "右上角", "左下角", "底部居中", "右下角", "屏幕中央" };

        public SettingsForm(Settings current, ClockForm owner)
        {
            clock = owner;
            work = current;

            Text = "桌面时钟 · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 620);

            TabControl tabs = new TabControl();
            tabs.Location = new Point(12, 12);
            tabs.Size = new Size(536, 512);
            Controls.Add(tabs);

            // ---- 外观 ----
            pgLook = new TabPage("外观");
            tabs.TabPages.Add(pgLook);
            BuildLookTab(pgLook);

            // ---- 布局 ----
            pgLayout = new TabPage("布局");
            tabs.TabPages.Add(pgLayout);
            BuildLayoutTab(pgLayout);

            // ---- 天气 ----
            pgWeather = new TabPage("天气");
            tabs.TabPages.Add(pgWeather);
            BuildWeatherTab(pgWeather);

            // ---- 常规 ----
            TabPage pgGeneral = new TabPage("常规");
            tabs.TabPages.Add(pgGeneral);
            BuildGeneralTab(pgGeneral);

            // 底部按钮
            Button reset = new Button(); reset.Text = "恢复默认"; reset.Width = 88; reset.Height = 30;
            reset.Location = new Point(16, 548);
            reset.Click += delegate { ResetDefaults(); };
            Controls.Add(reset);

            Button save = new Button(); save.Text = "保存"; save.Width = 88; save.Height = 30;
            save.Location = new Point(560 - 88 - 16 - 100, 548);
            save.Click += delegate { OnSave(); };
            Controls.Add(save);
            Button cancel = new Button(); cancel.Text = "取消"; cancel.Width = 88; cancel.Height = 30;
            cancel.Location = new Point(560 - 88 - 16, 548);
            cancel.Click += delegate { OnCancel(); };
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        // 窗体显示后 TabPage 才有真实宽度，重排所有依赖宽度的控件。
        // 构造阶段 pg.Width 还是默认值 200，导致 cw=56、透明度条 Width 被压成 0（渲染不出来）。
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            const int cx = 120;
            if (pgLook != null)
            {
                int lw = pgLook.ClientSize.Width - cx - 24;
                if (lw < 40) lw = 300;
                presetCombo.Width = lw;
                timeFontCombo.Width = lw;
                infoFontCombo.Width = lw;
                alphaBar.Width = lw - 90;
                if (alphaBar.Width < 120) alphaBar.Width = 120;
                alphaLabel.Location = new Point(cx + alphaBar.Width + 10, 304);
            }
            if (pgLayout != null)
                posCombo.Width = pgLayout.ClientSize.Width - cx - 24;
            if (pgWeather != null)
            {
                int ww = pgWeather.ClientSize.Width - cx - 24;
                if (ww < 40) ww = 300;
                cityCombo.Width = ww;
                codeBox.Width = ww - 120;
                resultList.Width = ww;
            }
        }

        // ---------- 外观页 ----------

        void BuildLookTab(TabPage pg)
        {
            int lx = 16, cx = 120, cw = pg.Width - cx - 24;

            AddLabel(pg, "字体预设", lx, 22);
            presetCombo = new ComboBox(); presetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            presetCombo.Location = new Point(cx, 18); presetCombo.Width = cw;
            foreach (string p in presets) presetCombo.Items.Add(p);
            presetCombo.SelectedIndex = 0;
            presetCombo.SelectedIndexChanged += delegate
            {
                if (suppress || presetCombo.SelectedIndex < 0) return;
                string sel = (string)presetCombo.SelectedItem;
                int sp = sel.IndexOf("  ");
                if (sp > 0)
                {
                    string timeF = sel.Substring(sp + 2).Trim();
                    SetFontSelection(timeFontCombo, ClockRenderer.FamilyOf(timeF));
                    SetFontSelection(infoFontCombo, ClockRenderer.FamilyOf(timeF));
                }
                OnLive();
            };
            pg.Controls.Add(presetCombo);

            AddLabel(pg, "时间字体", lx, 58);
            timeFontCombo = MakeFontCombo(pg, cx, 54, cw);
            AddLabel(pg, "信息字体", lx, 94);
            infoFontCombo = MakeFontCombo(pg, cx, 90, cw);

            AddLabel(pg, "时间字号", lx, 132);
            sizeNum = new NumericUpDown();
            sizeNum.Minimum = 28; sizeNum.Maximum = 240; sizeNum.Increment = 4;
            sizeNum.Value = work.TimeSize;
            sizeNum.Location = new Point(cx, 128);
            sizeNum.Width = 100;
            sizeNum.ValueChanged += delegate { OnLive(); };
            pg.Controls.Add(sizeNum);

            AddLabel(pg, "显示秒", lx, 166);
            secChk = new CheckBox(); secChk.Checked = work.ShowSeconds; secChk.AutoSize = true;
            secChk.Location = new Point(cx, 164);
            secChk.CheckedChanged += delegate { OnLive(); };
            pg.Controls.Add(secChk);

            AddLabel(pg, "文字颜色", lx, 200);
            textSw = MakeSwatch(pg, cx + 116, 198, Colors.FromHex(work.TextColor));
            textBtn = MakeColorButton(pg, cx, 196, textSw);
            AddLabel(pg, "强调色", lx, 236);
            accentSw = MakeSwatch(pg, cx + 116, 234, Colors.FromHex(work.AccentColor));
            accentBtn = MakeColorButton(pg, cx, 232, accentSw);
            AddLabel(pg, "背景颜色", lx, 272);
            bgSw = MakeSwatch(pg, cx + 116, 270, Colors.FromHex(work.BgColor));
            bgBtn = MakeColorButton(pg, cx, 268, bgSw);

            AddLabel(pg, "背景透明度", lx, 308);
            alphaBar = new AlphaBar();
            alphaBar.Value = work.BgAlpha;
            alphaBar.SetBaseColor(Colors.FromHex(work.BgColor));
            alphaBar.Location = new Point(cx, 304);
            alphaBar.Width = cw - 90;
            alphaBar.Height = 26;
            alphaBar.ValueChanged += delegate(int v) { alphaLabel.Text = v.ToString(); OnLive(); };
            pg.Controls.Add(alphaBar);
            alphaLabel = new Label();
            alphaLabel.Text = alphaBar.Value.ToString();
            alphaLabel.Width = 40;
            alphaLabel.TextAlign = ContentAlignment.MiddleLeft;
            alphaLabel.Location = new Point(cx + alphaBar.Width + 10, 304);
            alphaLabel.AutoSize = false;
            pg.Controls.Add(alphaLabel);

            AddLabel(pg, "提示：改完即实时生效，保存后写入配置文件。", 16, 344);
        }

        // ---------- 布局页 ----------

        void BuildLayoutTab(TabPage pg)
        {
            int lx = 16, cx = 120;

            AddLabel(pg, "位置", lx, 22);
            posCombo = new ComboBox(); posCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            posCombo.Location = new Point(cx, 18); posCombo.Width = pg.Width - cx - 24;
            for (int i = 0; i < posNames.Length; i++) posCombo.Items.Add(posNames[i]);
            int idx = Array.IndexOf(posKeys, work.Position);
            posCombo.SelectedIndex = idx >= 0 ? idx : 0;
            posCombo.SelectedIndexChanged += delegate { OnLive(); };
            pg.Controls.Add(posCombo);

            AddLabel(pg, "锁定位置", lx, 58);
            lockChk = new CheckBox(); lockChk.Text = "锁定后不能拖动（防止误拖）"; lockChk.Checked = work.Locked; lockChk.AutoSize = true;
            lockChk.Location = new Point(cx, 56);
            lockChk.CheckedChanged += delegate { OnLive(); };
            pg.Controls.Add(lockChk);

            AddLabel(pg, "置顶", lx, 92);
            topChk = new CheckBox(); topChk.Text = "始终显示在最前面"; topChk.Checked = work.AlwaysOnTop; topChk.AutoSize = true;
            topChk.Location = new Point(cx, 90);
            topChk.CheckedChanged += delegate { OnLive(); };
            pg.Controls.Add(topChk);

            AddLabel(pg, "鼠标穿透", lx, 126);
            thruChk = new CheckBox(); thruChk.Text = "时钟不拦截任何鼠标点击（通过托盘图标恢复）"; thruChk.Checked = work.ClickThrough; thruChk.AutoSize = true;
            thruChk.Location = new Point(cx, 124);
            thruChk.CheckedChanged += delegate { OnLive(); };
            pg.Controls.Add(thruChk);

            AddLabel(pg, "提示：鼠标穿透开启后，请用任务栏右下角的托盘图标（时钟图标）右键操作。", 16, 180);
        }

        // ---------- 天气页 ----------

        void BuildWeatherTab(TabPage pg)
        {
            int lx = 16, cx = 120, cw = pg.Width - cx - 24;

            AddLabel(pg, "显示天气", lx, 22);
            weaChk = new CheckBox(); weaChk.Text = "在时钟中显示当前城市天气"; weaChk.Checked = work.ShowWeather; weaChk.AutoSize = true;
            weaChk.Location = new Point(cx, 20);
            weaChk.CheckedChanged += delegate { OnLive(); };
            pg.Controls.Add(weaChk);

            AddLabel(pg, "当前城市", lx, 58);
            curCityLabel = new Label();
            curCityLabel.Text = work.CityName;
            curCityLabel.AutoSize = true;
            curCityLabel.Location = new Point(cx, 58);
            curCityLabel.ForeColor = Color.FromArgb(0, 90, 180);
            pg.Controls.Add(curCityLabel);

            AddLabel(pg, "常用城市", lx, 92);
            cityCombo = new ComboBox();
            cityCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            cityCombo.Location = new Point(cx, 88);
            cityCombo.Width = cw;
            // 注意顺序：先设 AutoCompleteSource 再设 AutoCompleteMode，否则抛 NotSupportedException
            cityCombo.AutoCompleteSource = AutoCompleteSource.ListItems;
            cityCombo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            foreach (string[] c in WeatherService.MajorCities)
            {
                if (!cityCombo.Items.Contains(c[0])) cityCombo.Items.Add(c[0]);
            }
            int sel = cityCombo.Items.IndexOf(work.CityName);
            cityCombo.SelectedIndex = sel >= 0 ? sel : -1;
            cityCombo.SelectedIndexChanged += delegate
            {
                if (suppress || cityCombo.SelectedIndex < 0) return;
                foreach (string[] c in WeatherService.MajorCities)
                {
                    if (c[0] == (string)cityCombo.SelectedItem)
                    {
                        nameBox.Text = c[0];
                        codeBox.Text = c[1];
                        curCityLabel.Text = c[0];
                        break;
                    }
                }
            };
            pg.Controls.Add(cityCombo);

            AddLabel(pg, "城市名", lx, 130);
            nameBox = new TextBox();
            nameBox.Text = work.CityName;
            nameBox.Location = new Point(cx, 126);
            nameBox.Width = 150;
            pg.Controls.Add(nameBox);

            AddLabel(pg, "城市码", lx + 180, 130);
            codeBox = new TextBox();
            codeBox.Text = work.CityCode;
            codeBox.Location = new Point(lx + 240, 126);
            codeBox.Width = cw - 120;
            pg.Controls.Add(codeBox);

            Button applyBtn = new Button(); applyBtn.Text = "应用到时钟"; applyBtn.Width = 100;
            applyBtn.Location = new Point(cx, 158);
            applyBtn.Click += delegate { ApplyCity(); };
            pg.Controls.Add(applyBtn);

            // 搜索区域
            int searchTop = 200;
            AddLabel(pg, "搜索城市", lx, searchTop + 30);
            searchBox = new TextBox();
            searchBox.Location = new Point(cx, searchTop + 26);
            searchBox.Width = 180;
            searchBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) DoSearch(); };
            pg.Controls.Add(searchBox);
            Button searchBtn = new Button(); searchBtn.Text = "搜索"; searchBtn.Width = 64;
            searchBtn.Location = new Point(cx + 186, searchTop + 25);
            searchBtn.Click += delegate { DoSearch(); };
            pg.Controls.Add(searchBtn);

            resultList = new ListBox();
            resultList.Location = new Point(cx, searchTop + 58);
            resultList.Width = cw;
            resultList.Height = 110;
            resultList.SelectedIndexChanged += delegate
            {
                if (suppress || resultList.SelectedIndex < 0 || results == null) return;
                CityResult r = results[resultList.SelectedIndex];
                if (r == null) return;
                nameBox.Text = r.Display;
                codeBox.Text = r.Code;
                curCityLabel.Text = r.Display;
                ApplyCity();
            };
            pg.Controls.Add(resultList);

            AddLabel(pg, "从“常用城市”下拉选择，或搜索城市名后点选结果，也可手动输入城市名与城市码。", 16, searchTop + 178);
            AddLabel(pg, "城市码示例：北京=101010100，上海=101020100，广州=101280101。", 16, searchTop + 200);
            AddLabel(pg, "天气数据来自中国天气网（免费），每 30 分钟自动刷新。", 16, searchTop + 222);
        }

        void DoSearch()
        {
            string name = searchBox.Text.Trim();
            if (name.Length == 0) return;
            try { Cursor = Cursors.WaitCursor; }
            catch { }
            results = WeatherService.Search(name);
            suppress = true;
            resultList.Items.Clear();
            if (results.Count == 0) resultList.Items.Add("（未找到该城市，请检查名称）");
            foreach (CityResult r in results) resultList.Items.Add(r.Display + "  [" + r.Code + "]");
            suppress = false;
            try { Cursor = Cursors.Default; }
            catch { }
        }

        void ApplyCity()
        {
            string nm = nameBox.Text.Trim();
            string cd = codeBox.Text.Trim();
            if (nm.Length == 0 || cd.Length == 0) return;
            work.CityName = nm;
            work.CityCode = cd;
            curCityLabel.Text = nm;
            OnLive();
        }

        // ---------- 常规页 ----------

        void BuildGeneralTab(TabPage pg)
        {
            int lx = 16, cx = 120;

            AddLabel(pg, "开机自启", lx, 22);
            autoChk = new CheckBox(); autoChk.Text = "开机时自动启动"; autoChk.Checked = work.AutoStart; autoChk.AutoSize = true;
            autoChk.Location = new Point(cx, 20);
            pg.Controls.Add(autoChk);

            AddLabel(pg, "版本", lx, 60);
            AddLabel(pg, "桌面时钟 v2.0", cx, 60);

            AddLabel(pg, "说明", lx, 96);
            AddLabel(pg, "· 左键拖动移动时钟（锁定后不可拖动）", cx, 96);
            AddLabel(pg, "· 右键点击时钟或托盘图标打开菜单", cx, 118);
            AddLabel(pg, "· 托盘图标 = 右下角系统托盘的小时钟图标", cx, 140);
            AddLabel(pg, "· 所有设置保存在 DesktopClock.ini", cx, 162);
        }

        // ---------- 控件辅助 ----------

        ComboBox MakeFontCombo(TabPage pg, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Location = new Point(x, y);
            c.Width = w;
            foreach (FontChoice fc in ClockRenderer.FontChoices()) c.Items.Add(fc);
            SetFontSelection(c, work.Font);
            c.SelectedIndexChanged += delegate { if (!suppress) OnLive(); };
            pg.Controls.Add(c);
            return c;
        }

        void SetFontSelection(ComboBox c, string family)
        {
            suppress = true;
            int idx = -1;
            for (int i = 0; i < c.Items.Count; i++)
            {
                FontChoice fc = (FontChoice)c.Items[i];
                if (string.Equals(fc.Family, family, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
            }
            c.SelectedIndex = idx;
            suppress = false;
        }

        void AddLabel(TabPage pg, string t, int x, int y)
        {
            Label l = new Label(); l.Text = t; l.AutoSize = true; l.Location = new Point(x, y);
            pg.Controls.Add(l);
        }

        Label MakeSwatch(TabPage pg, int x, int y, Color c)
        {
            Label sw = new Label();
            sw.BorderStyle = BorderStyle.FixedSingle;
            sw.Size = new Size(46, 22);
            sw.Location = new Point(x, y);
            sw.BackColor = c;
            pg.Controls.Add(sw);
            return sw;
        }

        Button MakeColorButton(TabPage pg, int x, int y, Label swatch)
        {
            Button b = new Button();
            b.Text = "选择颜色…";
            b.Width = 100;
            b.Location = new Point(x, y);
            b.Click += delegate
            {
                using (ColorDialog dlg = new ColorDialog())
                {
                    dlg.Color = swatch.BackColor;
                    dlg.FullOpen = true;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        swatch.BackColor = dlg.Color;
                        OnLive();
                    }
                }
            };
            pg.Controls.Add(b);
            return b;
        }

        // ---------- 逻辑 ----------

        void ReadControls()
        {
            if (timeFontCombo.SelectedItem != null) work.Font = ((FontChoice)timeFontCombo.SelectedItem).Family;
            if (infoFontCombo.SelectedItem != null) work.InfoFont = ((FontChoice)infoFontCombo.SelectedItem).Family;
            work.TimeSize = (int)sizeNum.Value;
            work.ShowSeconds = secChk.Checked;
            work.TextColor = Colors.ToHex(textSw.BackColor);
            work.AccentColor = Colors.ToHex(accentSw.BackColor);
            work.BgColor = Colors.ToHex(bgSw.BackColor);
            work.BgAlpha = alphaBar.Value;
            work.Locked = lockChk.Checked;
            work.AlwaysOnTop = topChk.Checked;
            work.ClickThrough = thruChk.Checked;
            work.ShowWeather = weaChk.Checked;
            work.AutoStart = autoChk.Checked;
            if (nameBox != null && codeBox != null)
            {
                string nm = nameBox.Text.Trim();
                string cd = codeBox.Text.Trim();
                if (nm.Length > 0) work.CityName = nm;
                if (cd.Length > 0) work.CityCode = cd;
            }
            if (posCombo.SelectedIndex >= 0) work.Position = posKeys[posCombo.SelectedIndex];
        }

        void OnLive()
        {
            ReadControls();
            if (alphaBar != null) alphaBar.SetBaseColor(Colors.FromHex(work.BgColor));
            clock.ApplyLive(work);
        }

        void OnSave()
        {
            ReadControls();
            bool cityChanged = false;
            if (work.CityCode != Ini.Get("CityCode", "101010100") || work.CityName != Ini.Get("CityName", "北京"))
                cityChanged = true;
            clock.ApplyAndSave(work, cityChanged);
            DialogResult = DialogResult.OK;
            Close();
        }

        void OnCancel()
        {
            // 恢复
            Settings restored = new Settings();
            restored.Load();
            clock.ApplyLive(restored);
            DialogResult = DialogResult.Cancel;
            Close();
        }

        void ResetDefaults()
        {
            Settings d = new Settings();
            suppress = true;
            presetCombo.SelectedIndex = 0;
            SetFontSelection(timeFontCombo, "微软雅黑 Light");
            SetFontSelection(infoFontCombo, "微软雅黑 Light");
            sizeNum.Value = 64;
            secChk.Checked = true;
            textSw.BackColor = Colors.FromHex(0xF2F2F2);
            accentSw.BackColor = Colors.FromHex(0xE8B56D);
            bgSw.BackColor = Colors.FromHex(0x101010);
            alphaBar.Value = 70;
            alphaLabel.Text = "70";
            lockChk.Checked = false;
            topChk.Checked = true;
            thruChk.Checked = false;
            weaChk.Checked = true;
            autoChk.Checked = false;
            posCombo.SelectedIndex = 0;
            suppress = false;
            OnLive();
        }
    }

    // ============================== 自定义透明度条 ==============================
    // 用 GDI+ 直接绘制：渐变底（背景色从透明到不透明）+ 可见滑块。
    // 点击任意位置即跳值、按住拖动实时调节，比系统 TrackBar 直观得多
    //（系统 TrackBar 在 Win11 上 TickStyle.None 时轨道几乎不可见，且点轨道不跳值）。
    class AlphaBar : Control
    {
        int _value;
        bool dragging;
        Color baseColor = Color.FromArgb(30, 30, 30);

        public int Value
        {
            get { return _value; }
            set { if (_value != value) { _value = value; Invalidate(); } }
        }
        public event Action<int> ValueChanged;

        public AlphaBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public void SetBaseColor(Color c) { baseColor = c; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f;

            // 渐变轨道：左全透明 → 右不透明（背景色实时跟随）
            int tw = Math.Max(1, Width - 14);
            RectangleF track = new RectangleF(7, cy - 7, tw, 14);
            using (LinearGradientBrush lgb = new LinearGradientBrush(
                new RectangleF(track.X, track.Y, track.Width, track.Height),
                Color.FromArgb(0, baseColor), Color.FromArgb(255, baseColor),
                LinearGradientMode.Horizontal))
                g.FillRectangle(lgb, track.X, track.Y, track.Width, track.Height);
            using (Pen bp = new Pen(Color.FromArgb(130, 0, 0, 0)))
                g.DrawRectangle(bp, track.X, track.Y, track.Width, track.Height);

            // 滑块位置（两端留 7px 边距）
            float x = 7 + _value / 255f * tw;
            using (SolidBrush sb = new SolidBrush(Color.White))
                g.FillEllipse(sb, x - 7, cy - 7, 14, 14);
            using (SolidBrush mk = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                g.FillRectangle(mk, x - 1, cy - 4, 2, 8);
            using (Pen op = new Pen(Color.FromArgb(150, 0, 0, 0)))
                g.DrawEllipse(op, x - 7, cy - 7, 14, 14);
        }

        void SetValueFromX(int mx)
        {
            int tw = Width - 14;
            if (tw <= 0) return;
            int v = (int)Math.Round((mx - 7) / (double)tw * 255);
            if (v < 0) v = 0; if (v > 255) v = 255;
            if (v != _value)
            {
                _value = v;
                if (ValueChanged != null) ValueChanged(v);
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { dragging = true; SetValueFromX(e.X); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetValueFromX(e.X);
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }
    }
}
