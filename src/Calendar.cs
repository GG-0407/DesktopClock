using System;

namespace DesktopClock
{
    /// <summary>
    /// 农历 / 干支 / 生肖 / 节日 / 二十四节气 计算（覆盖 1900-2100 年）。
    /// 农历采用经典查表法（1900-2100 年编码表），节气采用天文近似公式，零外部依赖。
    /// </summary>
    public static class ChineseCalendar
    {
        // 1900-2100 农历信息表。
        // 编码规则：bits 0-3 = 闰月月份（0 表示无闰月）；
        // bit 16 = 闰月 30 天；bit 15..4 分别对应正月..腊月，置位为 30 天。
        static readonly int[] lunarInfo = new int[] {
            0x04bd8,0x04ae0,0x0a570,0x054d5,0x0d260,0x0d950,0x16554,0x056a0,0x09ad0,0x055d2,
            0x04ae0,0x0a5b6,0x0a4d0,0x0d250,0x1d255,0x0b540,0x0d6a0,0x0ada2,0x095b0,0x14977,
            0x04970,0x0a4b0,0x0b4b5,0x06a50,0x06d40,0x1ab54,0x02b60,0x09570,0x052f2,0x04970,
            0x06566,0x0d4a0,0x0ea50,0x06e95,0x05ad0,0x02b60,0x186e3,0x092e0,0x1c8d7,0x0c950,
            0x0d4a0,0x1d8a6,0x0b550,0x056a0,0x1a5b4,0x025d0,0x092d0,0x0d2b2,0x0a950,0x0b557,
            0x06ca0,0x0b550,0x15355,0x04da0,0x0a5b0,0x14573,0x052b0,0x0a9a8,0x0e950,0x06aa0,
            0x0aea6,0x0ab50,0x04b60,0x0aae4,0x0a570,0x05260,0x0f263,0x0d950,0x05b57,0x056a0,
            0x096d0,0x04dd5,0x04ad0,0x0a4d0,0x0d4d4,0x0d250,0x0d558,0x0b540,0x0b6a0,0x195a6,
            0x095b0,0x049b0,0x0a974,0x0a4b0,0x0b27a,0x06a50,0x06d40,0x0af46,0x0ab60,0x09570,
            0x04af5,0x04970,0x064b0,0x074a3,0x0ea50,0x06b58,0x055c0,0x0ab60,0x096d5,0x092e0,
            0x0c960,0x0d954,0x0d4a0,0x0da50,0x07552,0x056a0,0x0abb7,0x025d0,0x092d0,0x0cab5,
            0x0a950,0x0b4a0,0x0baa4,0x0ad50,0x055d9,0x04ba0,0x0a5b0,0x15176,0x052b0,0x0a930,
            0x07954,0x06aa0,0x0ad50,0x05b52,0x04b60,0x0a6e6,0x0a4e0,0x0d260,0x0ea65,0x0d530,
            0x05aa0,0x076a3,0x096d0,0x04afb,0x04ad0,0x0a4d0,0x1d0b6,0x0d250,0x0d520,0x0dd45,
            0x0b5a0,0x056d0,0x055b2,0x049b0,0x0a577,0x0a4b0,0x0aa50,0x1b255,0x06d20,0x0ada0,
            0x14b63,0x09370,0x049f8,0x04970,0x064b0,0x168a6,0x0ea50,0x06b20,0x1a6c4,0x0aae0,
            0x0a2e0,0x0d2e3,0x0c960,0x0d557,0x0d4a0,0x0da50,0x05d55,0x056a0,0x0a6d0,0x055d4,
            0x052d0,0x0a9b8,0x0a950,0x0b4a0,0x0b6a6,0x0ad50,0x055a0,0x0aba4,0x0a5b0,0x052b0,
            0x0b273,0x06930,0x07337,0x06aa0,0x0ad50,0x14b55,0x04b60,0x0a570,0x054e4,0x0d160,
            0x0e968,0x0d520,0x0daa0,0x16aa6,0x056d0,0x04ae0,0x0a9d4,0x0a2d0,0x0d150,0x0f252,
            0x0d520
        };

        static readonly string[] tianGan = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        static readonly string[] diZhi   = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        static readonly string[] shengXiao = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };
        static readonly string[] lunarMonthName = { "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊" };
        static readonly string[] cn = { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };

        // 节气：sTermInfo[n] 为自 1900-01-06 02:05:00（1900 小寒）起的分钟偏移
        static readonly int[] sTermInfo = {
            0, 21208, 42467, 63836, 85337, 107014, 128867, 150921, 173149, 195551,
            218072, 240693, 263343, 285989, 308563, 331033, 353350, 375494, 397447,
            419210, 440795, 462224, 483532, 504758
        };
        static readonly string[] sTermName = {
            "小寒", "大寒", "立春", "雨水", "惊蛰", "春分", "清明", "谷雨",
            "立夏", "小满", "芒种", "夏至", "小暑", "大暑", "立秋", "处暑",
            "白露", "秋分", "寒露", "霜降", "立冬", "小雪", "大雪", "冬至"
        };

        // ---------- 农历基本量 ----------

        public static int LeapMonth(int y) { return lunarInfo[y - 1900] & 0xf; }
        public static int LeapDays(int y)  { return LeapMonth(y) != 0 ? ((lunarInfo[y - 1900] & 0x10000) != 0 ? 30 : 29) : 0; }
        public static int MonthDays(int y, int m) { return (lunarInfo[y - 1900] & (0x10000 >> m)) != 0 ? 30 : 29; }
        public static int YearDays(int y)
        {
            int sum = 348;
            for (int i = 0x8000; i > 0x8; i >>= 1)
                if ((lunarInfo[y - 1900] & i) != 0) sum++;
            return sum + LeapDays(y);   // 农历年天数必须包含闰月
        }

        // ---------- 公历 -> 农历 ----------

        public static void SolarToLunar(DateTime date, out int lYear, out int lMonth, out int lDay, out bool isLeap)
        {
            DateTime baseDate = new DateTime(1900, 1, 31);   // 1900 正月初一
            int offset = (int)(date.Date - baseDate).Days;
            int temp = 0, i;

            lYear = 1900; lMonth = 1; lDay = 1; isLeap = false;

            for (i = 1900; i < 2101 && offset > 0; i++)
            {
                temp = YearDays(i);
                offset -= temp;
            }
            if (offset < 0) { offset += temp; i--; }
            lYear = i;

            int leap = LeapMonth(lYear);
            for (i = 1; i < 13 && offset > 0; i++)
            {
                if (leap > 0 && i == leap + 1 && !isLeap)
                {
                    --i;
                    isLeap = true;
                    temp = LeapDays(lYear);
                }
                else
                {
                    temp = MonthDays(lYear, i);
                }
                if (isLeap && i == leap + 1) isLeap = false;
                offset -= temp;
            }
            if (offset == 0 && leap > 0 && i == leap + 1)
            {
                if (isLeap) isLeap = false;
                else { isLeap = true; --i; }
            }
            if (offset < 0) { offset += temp; --i; }
            lMonth = i;
            lDay = offset + 1;
        }

        // ---------- 显示 ----------

        public static string LunarDayName(int d)
        {
            switch (d)
            {
                case 10: return "初十";
                case 20: return "二十";
                case 30: return "三十";
                default:
                    if (d < 10) return "初" + cn[d];
                    if (d < 20) return "十" + cn[d - 10];
                    return "廿" + cn[d - 20];
            }
        }

        /// <summary>返回农历显示字符串（含干支、生肖），同时输出节日信息。</summary>
        public static string LunarToString(DateTime date, out bool hasFestival, out string festival)
        {
            int y, m, d;
            bool leap;
            SolarToLunar(date, out y, out m, out d, out leap);
            string ganzhi = tianGan[(y - 4) % 10] + diZhi[(y - 4) % 12] + shengXiao[(y - 4) % 12] + "年";
            string month = (leap ? "闰" : "") + lunarMonthName[m - 1] + "月";
            string day = LunarDayName(d);
            festival = GetFestival(date, y, m, d, leap);
            hasFestival = festival != null;
            return "农历" + ganzhi + " " + month + day;
        }

        // ---------- 节气 ----------

        /// <summary>第 n 个节气（0=小寒 … 23=冬至）的公历日期时间。</summary>
        public static DateTime SolarTerm(int year, int n)
        {
            double ms = 31556925974.7 * (year - 1900) + (double)sTermInfo[n] * 60000;
            return new DateTime(1900, 1, 6, 2, 5, 0).AddMilliseconds(ms);
        }

        /// <summary>若该日是节气则返回节气名，否则 null。</summary>
        public static string GetSolarTermName(DateTime date)
        {
            for (int n = 0; n < 24; n++)
            {
                DateTime t = SolarTerm(date.Year, n);
                if (t.Year == date.Year && t.Month == date.Month && t.Day == date.Day)
                    return sTermName[n];
            }
            return null;
        }

        // ---------- 节日 ----------

        public static string GetFestival(DateTime solar, int lYear, int lMonth, int lDay, bool isLeap)
        {
            if (isLeap) return null;
            int m = solar.Month, d = solar.Day;

            // 农历节日
            if (lMonth == 1 && lDay == 1)  return "春节";
            if (lMonth == 1 && lDay == 15) return "元宵节";
            if (lMonth == 5 && lDay == 5)  return "端午节";
            if (lMonth == 7 && lDay == 7)  return "七夕节";
            if (lMonth == 7 && lDay == 15) return "中元节";
            if (lMonth == 8 && lDay == 15) return "中秋节";
            if (lMonth == 9 && lDay == 9)  return "重阳节";
            if (lMonth == 12 && lDay == 8) return "腊八节";
            if (lMonth == 12 && lDay == MonthDays(lYear, 12)) return "除夕";

            // 公历节日
            if (m == 1 && d == 1)  return "元旦";
            if (m == 2 && d == 14) return "情人节";
            if (m == 3 && d == 8)  return "妇女节";
            if (m == 3 && d == 12) return "植树节";
            if (m == 4 && d == 1)  return "愚人节";
            if (m == 5 && d == 1)  return "劳动节";
            if (m == 5 && d == 4)  return "青年节";
            if (m == 6 && d == 1)  return "儿童节";
            if (m == 7 && d == 1)  return "建党节";
            if (m == 8 && d == 1)  return "建军节";
            if (m == 9 && d == 10) return "教师节";
            if (m == 10 && d == 1) return "国庆节";
            if (m == 12 && d == 24) return "平安夜";
            if (m == 12 && d == 25) return "圣诞节";

            // 清明（既是节气也是节日）
            if (GetSolarTermName(solar) == "清明") return "清明节";
            return null;
        }
    }
}
