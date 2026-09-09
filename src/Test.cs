using System;

/// <summary>
/// 农历/节气算法校验程序（控制台）。
/// 用法: calcheck.exe
/// 对若干高置信度的已知日期做断言，并打印若干参考日期的换算结果。
/// </summary>
public class Test
{
    static int errors = 0, passed = 0;

    static void Check(string solar, int ly, int lm, int ld, bool leap, string name)
    {
        DateTime d = DateTime.Parse(solar);
        int y, m, dd;
        bool lp;
        DesktopClock.ChineseCalendar.SolarToLunar(d, out y, out m, out dd, out lp);
        bool ok = (y == ly && m == lm && dd == ld && lp == leap);
        if (ok) passed++;
        else errors++;
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + solar + " -> 农历" + y + "/" + m + "/" + dd
            + (lp ? "(闰)" : "") + "  期望 " + ly + "/" + lm + "/" + ld + (leap ? "(闰)" : "") + "  " + name);
    }

    static void Print(string solar, string name)
    {
        DateTime d = DateTime.Parse(solar);
        bool hasFes; string fes;
        string s = DesktopClock.ChineseCalendar.LunarToString(d, out hasFes, out fes);
        string term = DesktopClock.ChineseCalendar.GetSolarTermName(d);
        Console.WriteLine("      " + name + "  " + solar + "  =>  " + s + (hasFes ? "  【" + fes + "】" : "")
            + (term != null ? "  (节气:" + term + ")" : ""));
    }

    public static void Main()
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        Console.WriteLine("== 农历换算断言（高置信锚点）==");

        // 各年正月初一（春节）
        Check("1900-01-31", 1900, 1, 1, false, "1900 春节");
        Check("2000-02-05", 2000, 1, 1, false, "2000 春节");
        Check("2020-01-25", 2020, 1, 1, false, "2020 春节");
        Check("2023-01-22", 2023, 1, 1, false, "2023 春节");
        Check("2024-02-10", 2024, 1, 1, false, "2024 春节");
        Check("2025-01-29", 2025, 1, 1, false, "2025 春节");
        Check("2026-02-17", 2026, 1, 1, false, "2026 春节");

        // 中秋节（八月十五）
        Check("2023-09-29", 2023, 8, 15, false, "2023 中秋");
        Check("2024-09-17", 2024, 8, 15, false, "2024 中秋");
        Check("2025-10-06", 2025, 8, 15, false, "2025 中秋");

        // 除夕（腊月最后一天）
        Check("2024-02-09", 2023, 12, 30, false, "2024 除夕");

        // 闰月（2020 闰四月初一）
        Check("2020-05-23", 2020, 4, 1, true, "2020 闰四月初一");

        Console.WriteLine("== 节气锚点 ==");
        DateTime lichun = DesktopClock.ChineseCalendar.SolarTerm(2026, 2);   // 立春
        DateTime liqiu  = DesktopClock.ChineseCalendar.SolarTerm(2026, 14);  // 立秋
        DateTime dongzhi = DesktopClock.ChineseCalendar.SolarTerm(2026, 23); // 冬至
        Console.WriteLine("      2026 立春 = " + lichun.ToString("yyyy-MM-dd HH:mm") + "  (期望 02-04)");
        Console.WriteLine("      2026 立秋 = " + liqiu.ToString("yyyy-MM-dd HH:mm"));
        Console.WriteLine("      2026 冬至 = " + dongzhi.ToString("yyyy-MM-dd HH:mm"));
        if (lichun.Day != 4) { errors++; Console.WriteLine("[FAIL] 2026 立春非 2 月 4 日"); }
        else passed++;

        Console.WriteLine("== 参考输出 ==");
        Print("2026-08-09", "今天");
        Print("2026-02-17", "2026春节");
        Print("2026-09-25", "中秋前");
        Print("2026-10-01", "国庆");
        Print("2026-04-05", "清明附近");
        Print("2024-02-10", "2024春节");
        Print("2033-01-01", "2033元旦");

        Console.WriteLine();
        Console.WriteLine("通过 " + passed + " / " + (passed + errors));
        Console.WriteLine(errors == 0 ? "== 全部通过 ==" : "== 存在失败，请检查! ==");
    }
}
