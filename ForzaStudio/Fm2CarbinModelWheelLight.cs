namespace ForzaStudio
{
    using System;

    // FM2 wheel lighting (car body header: 4 rows x 16 floats, one spherical harmonics vector per wheel, bands 0-3, the
    // D3DX basis of the vertex lighting). User rule 2026-11-22 "dalla base va tolto TUTTO": a ported car must not keep the
    // template's. Study 2026-11-23 (E:\DEV\fm4dev\PortToFM2 wlfit, wlmap, wlmean; docs\PORT_TO_FM2.md):
    //   - the vertex baker's light at the wheel centres (sky / free down / sent back / ground, fitted on 25 cars) is no
    //     better than FM2's mean (band 0 rms 0.22, spread 0.19);
    //   - FM4's 4 values per wheel (header 0x004, bands 0-1) give FM2's 16 no better than the mean (79 cars both games
    //     have, leave one car out: rms 0.156 vs 0.158).
    // So a ported car gets FM2's mean over its 310 car models (user 2026-11-23: "ok media FM2"): no car's own values.
    // Row order = FM2's (the same as FM4's: rows 0 / 3 and 1 / 2 the two sides, 0 / 1 front).
    public partial class Fm2CarbinModel
    {
        private static readonly float[] Fm2MeanWheelLighting =
        {
            2.4012f, 0.2859f, 0.0292f, -1.2092f, 0.1782f, -0.0009f, 0.1695f, -0.0425f, -0.5718f, 0.0026f, -0.0189f, -0.0264f, 0.0005f, -0.2009f, 0.0074f, -0.0284f,
            2.3975f, 0.2949f, 0.0416f, 1.2146f, -0.1831f, -0.0392f, 0.1397f, 0.0251f, -0.5565f, 0.0283f, -0.0111f, -0.0317f, -0.0047f, 0.1883f, -0.0065f, 0.0563f,
            2.3907f, 0.2580f, -0.0748f, 1.2065f, -0.1676f, -0.0360f, 0.1236f, -0.0008f, -0.5807f, 0.0297f, -0.0257f, -0.0494f, -0.0778f, 0.1785f, -0.0108f, 0.0491f,
            2.3893f, 0.2441f, -0.0884f, -1.2006f, 0.1588f, -0.0034f, 0.1516f, -0.0024f, -0.5975f, -0.0010f, -0.0026f, -0.0444f, -0.0744f, -0.1820f, -0.0023f, -0.0192f,
        };

        // The car body's wheel lighting set to FM2's mean (big-endian floats, as stored).
        private void SetMeanWheelLighting()
        {
            byte[] data = new byte[Fm2MeanWheelLighting.Length * 4];
            for (int i = 0; i < Fm2MeanWheelLighting.Length; i++)
            {
                PutFloat(data, i * 4, Fm2MeanWheelLighting[i]);
            }
            this.WheelLighting = data;
        }
    }
}
