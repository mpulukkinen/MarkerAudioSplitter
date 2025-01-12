using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

public static class WavFileUtils
{
    public static async Task TrimWavFile(WaveFileReader inPath, string outPath, TimeSpan cutFromStart, TimeSpan cutFromEnd,
        CancellationToken cancel)
    {
        using (WaveFileWriter writer = new WaveFileWriter(outPath, inPath.WaveFormat))
        {
            float bytesPerMillisecond = inPath.WaveFormat.AverageBytesPerSecond / 1000f;

            long startPos = (long)(cutFromStart.TotalMilliseconds * bytesPerMillisecond);
            startPos = startPos - startPos % inPath.WaveFormat.BlockAlign;

            long endBytes = (long)(cutFromEnd.TotalMilliseconds * bytesPerMillisecond);
            endBytes = endBytes - endBytes % inPath.WaveFormat.BlockAlign;
            long endPos = inPath.Length - endBytes;

            if (startPos < 0)
            {
                return;
            }

            await TrimWavFile(inPath, writer, startPos, endPos, cancel);
        }
    }

    private static async Task TrimWavFile(WaveFileReader reader, WaveFileWriter writer, long startPos, long endPos, CancellationToken cancel)
    {
        reader.Position = startPos;

        var attemptedSize = 1024;
        var div = attemptedSize % reader.BlockAlign;

        attemptedSize -= div;

        byte[] buffer = new byte[attemptedSize];
        while (reader.Position < endPos)
        {
            int bytesRequired = (int)(endPos - reader.Position);
            bytesRequired -= bytesRequired % reader.BlockAlign;

            if (bytesRequired < reader.BlockAlign)
            {
                break;
            }

            int bytesToRead = Math.Min(bytesRequired, buffer.Length);

            int bytesRead = reader.Read(buffer, 0, bytesToRead);
            if (bytesRead > 0)
            {
                await writer.WriteAsync(buffer, 0, bytesRead);
            }

            if (cancel.IsCancellationRequested)
            {
                break;
            }
        }
    }
}