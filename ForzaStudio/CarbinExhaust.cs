using System;
using System.Collections.Generic;

namespace ForzaStudio
{
    // The exhaust locators of a main car carbin (FM4 layout, studied 2026-10-20/21, fm4dev\FM3_FM4\tools exhrec):
    // the header holds the format block [0x10, 0x10, 1, N] at 0x154 followed by N records of 140 bytes. Record: word 0 = 2
    // (type), word 2 = -1, a 4x4 matrix at +0x0C whose translation (+0x3C, +0x40, +0x44) is the pipe tip, a second 4x4
    // at +0x4C (translation ~ 0, -0.02, 1.0 .. 1.6). The backfire flames start at the tip and follow the matrix axes.
    // N is part of the header length formula (CarbinModel.HeadLength), so a car can have any number of pipes.
    // Game names (default.xex, 2026-11-07): 0x154 = CBaseCarModelData version 0x10, ShaderVersion 16, the Emitters
    // container (version 1, count N); record = CCarEmitterData: version 2, type (0 on every DLC car), parent (-1),
    // location matrix (+0x0C), location2 matrix (+0x4C). See CarbinModel.HeaderLayout.
    public partial class CarbinModel
    {
        public const int ExhaustBlock = 0x154;
        public const int ExhaustRecordSize = 140;

        // Offsets inside a record.
        private const int RecordMatrix = 0x0C;
        private const int RecordTip = 0x3C;

        public bool HasExhaustLocators
        {
            get
            {
                return this.Head != null && this.Head.Length >= ExhaustBlock + 16
                    && ReadU32(this.Head, ExhaustBlock) == 0x10 && ReadU32(this.Head, ExhaustBlock + 4) == 0x10 && ReadU32(this.Head, ExhaustBlock + 8) == 1;
            }
        }

        public int ExhaustCount
        {
            get { return this.HasExhaustLocators ? (int) ReadU32(this.Head, ExhaustBlock + 12) : 0; }
        }

        // The pipe tip of record i (model axes).
        public float[] GetExhaustTip(int index)
        {
            int at = ExhaustBlock + 16 + (index * ExhaustRecordSize) + RecordTip;
            return new float[] { ReadHeadFloat(at), ReadHeadFloat(at + 4), ReadHeadFloat(at + 8) };
        }

        public void SetExhaustTip(int index, float x, float y, float z)
        {
            int at = ExhaustBlock + 16 + (index * ExhaustRecordSize) + RecordTip;
            WriteHeadFloat(at, x);
            WriteHeadFloat(at + 4, y);
            WriteHeadFloat(at + 8, z);
        }

        // The tip list becomes tips (x, y, z): the first records are reused (their rotation / second matrix stay), extra
        // ones are copies of the first record, surplus ones are removed. With no record at all a standard one is made
        // (identity rotation, second matrix translation 0, -0.02, 1.0). The header grows or shrinks by 140 bytes per pipe.
        public void SetExhaustTips(List<float[]> tips)
        {
            if (!this.HasExhaustLocators)
            {
                throw new InvalidOperationException("This carbin has no FM4 exhaust locator block.");
            }
            int old = this.ExhaustCount;
            int start = ExhaustBlock + 16;
            byte[] template = new byte[ExhaustRecordSize];
            if (old > 0)
            {
                Buffer.BlockCopy(this.Head, start, template, 0, ExhaustRecordSize);
            }
            else
            {
                WriteBigEndian(template, 0, 2);
                WriteBigEndian(template, 8, 0xFFFFFFFF);
                // identity 4x4 at +0x0C and +0x4C
                foreach (int matrix in new int[] { 0x0C, 0x4C })
                {
                    for (int d = 0; d < 4; d++)
                    {
                        WriteBigEndian(template, matrix + (d * 16) + (d * 4), BitConverter.ToUInt32(BitConverter.GetBytes(1.0f), 0));
                    }
                }
                WriteBigEndian(template, 0x4C + 0x30 + 8, BitConverter.ToUInt32(BitConverter.GetBytes(1.0f), 0));
                WriteBigEndian(template, 0x4C + 0x30 + 4, BitConverter.ToUInt32(BitConverter.GetBytes(-0.02f), 0));
            }
            List<byte[]> records = new List<byte[]>();
            for (int i = 0; i < tips.Count; i++)
            {
                byte[] record = new byte[ExhaustRecordSize];
                Buffer.BlockCopy((i < old) ? this.Head : template, (i < old) ? start + (i * ExhaustRecordSize) : 0, record, 0, ExhaustRecordSize);
                records.Add(record);
            }
            int tail = start + (old * ExhaustRecordSize);
            byte[] head = new byte[start + (records.Count * ExhaustRecordSize) + (this.Head.Length - tail)];
            Buffer.BlockCopy(this.Head, 0, head, 0, start);
            for (int i = 0; i < records.Count; i++)
            {
                Buffer.BlockCopy(records[i], 0, head, start + (i * ExhaustRecordSize), ExhaustRecordSize);
            }
            Buffer.BlockCopy(this.Head, tail, head, start + (records.Count * ExhaustRecordSize), this.Head.Length - tail);
            WriteBigEndian(head, ExhaustBlock + 12, (uint) records.Count);
            this.Head = head;
            for (int i = 0; i < tips.Count; i++)
            {
                this.SetExhaustTip(i, tips[i][0], tips[i][1], tips[i][2]);
            }
        }

        private float ReadHeadFloat(int offset)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(ReadU32(this.Head, offset)), 0);
        }

        private void WriteHeadFloat(int offset, float value)
        {
            WriteBigEndian(this.Head, offset, BitConverter.ToUInt32(BitConverter.GetBytes(value), 0));
        }

        private static void WriteBigEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte) (value >> 24);
            data[offset + 1] = (byte) (value >> 16);
            data[offset + 2] = (byte) (value >> 8);
            data[offset + 3] = (byte) value;
        }
    }
}
