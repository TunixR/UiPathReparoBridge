// PROTOTYPE for ReparoHome#49: throwaway. probe run <script> <outDir> | probe scale [percent]
using System;
using System.Runtime.InteropServices;

static class Program
{
    static int Main(string[] a)
    {
        if (a[0] == "run") { Reparo.UiPathBridge.ProbeRunner.Run(a[1], a[2], "ProbeCli (no DPI manifest)"); return 0; }
        if (a[0] == "scale") return Scale.Run(a.Length > 1 ? int.Parse(a[1]) : 0);
        return 2;
    }
}

/// Display scaling of every active source, live (no sign-out), through DisplayConfig's undocumented DPI packets
/// (the ones Settings > Display uses: type -3 get, -4 set, values relative to the recommended scale).
static class Scale
{
    static readonly int[] Steps = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };
    [StructLayout(LayoutKind.Sequential)] struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct HEADER { public int type; public int size; public LUID adapterId; public uint id; }
    [StructLayout(LayoutKind.Sequential)] struct GET { public HEADER h; public int min, cur, max; }
    [StructLayout(LayoutKind.Sequential)] struct SET { public HEADER h; public int scale; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_SOURCE { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PATH_TARGET { public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology, rotation, scaling; public uint rrNum, rrDen; public int scanLineOrdering; public bool targetAvailable; public uint statusFlags; }
    [StructLayout(LayoutKind.Sequential)] struct PATH { public PATH_SOURCE src; public PATH_TARGET tgt; public uint flags; }
    [StructLayout(LayoutKind.Sequential, Size = 64)] struct MODE { public int infoType; public uint id; public LUID adapterId; }
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint np, out uint nm);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH[] p, ref uint nm, [Out] MODE[] m, IntPtr topo);
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref GET g);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SET s);

    public static int Run(int percent)
    {
        GetDisplayConfigBufferSizes(2 /*ONLY_ACTIVE_PATHS*/, out uint np, out uint nm);
        var paths = new PATH[np]; var modes = new MODE[nm];
        int rc = QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
        if (rc != 0) { Console.WriteLine($"QueryDisplayConfig {rc}"); return 1; }
        for (int i = 0; i < np; i++)
        {
            var g = new GET { h = { type = -3, size = Marshal.SizeOf<GET>(), adapterId = paths[i].src.adapterId, id = paths[i].src.id } };
            rc = DisplayConfigGetDeviceInfo(ref g);
            int rec = Math.Abs(g.min), now = Steps[Math.Clamp(rec + g.cur, 0, Steps.Length - 1)];
            Console.WriteLine($"source {paths[i].src.id}: rc {rc}, scale {now}% (recommended {Steps[rec]}%, range {Steps[rec + g.min]}..{Steps[Math.Min(rec + g.max, Steps.Length - 1)]})");
            if (percent > 0)
            {
                var s = new SET { h = { type = -4, size = Marshal.SizeOf<SET>(), adapterId = paths[i].src.adapterId, id = paths[i].src.id }, scale = Array.IndexOf(Steps, percent) - rec };
                Console.WriteLine($"  set {percent}%: rc {DisplayConfigSetDeviceInfo(ref s)}");
            }
        }
        return 0;
    }
}
