using System;
using System.Collections.Generic;
using System.IO;
using ForzaStudio;

static class RoundTrip
{
    static int Main(string[] args)
    {
        int ok = 0, bad = 0, model = 0, header = 0;
        foreach (string root in args)
        {
            foreach (string f in Directory.GetFiles(root, "*.bgf", SearchOption.AllDirectories))
            {
                byte[] d = File.ReadAllBytes(f);
                AnarkBgf b = AnarkBgf.Read(d);
                if (b.TailEditable)
                {
                    model++;
                }
                byte[] w = b.Write();
                if (!Same(d, w))
                {
                    bad++;
                    Console.WriteLine("DIFF write " + f);
                    continue;
                }
                byte[] before = (byte[]) b.Header.Clone();
                b.UpdateHeader();
                bool same = true;
                foreach (int o in new int[] { 0x1C, 0x24, 0x28, 0x2C, 0x30, 0x34, 0x3C, 0x44, 0x48, 0x4C, 0x50, 0x54, 0x5C, 0x60, 0x64, 0x68, 0x70, 0x74, 0x78, 0x7C, 0x80 })
                {
                    for (int k = 0; k < 4; k++)
                    {
                        if (before[o + k] != b.Header[o + k])
                        {
                            if (same)
                            {
                                Console.WriteLine("HEADER 0x" + o.ToString("X2") + " " + f);
                            }
                            same = false;
                        }
                    }
                }
                if (same)
                {
                    header++;
                }
                ok++;
            }
        }
        Console.WriteLine("{0} written byte for byte, {1} different, {2} with the tail model, {3} headers recomputed identical", ok, bad, model, header);
        return bad;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }
}
