using NAudio.Wave;
using System;
using System.Threading.Tasks;

public static class WavFileUtils
{
    public static async Task TrimWavFile(WaveFileReader inPath, string outPath, TimeSpan cutFromStart, TimeSpan cutFromEnd)
    {
        using (WaveFileWriter writer = new WaveFileWriter(outPath, inPath.WaveFormat))
        {
            float bytesPerMillisecond = inPath.WaveFormat.AverageBytesPerSecond / 1000f;

            int startPos = (int)(cutFromStart.TotalMilliseconds * bytesPerMillisecond);
            startPos = startPos - startPos % inPath.WaveFormat.BlockAlign;

            int endBytes = (int)(cutFromEnd.TotalMilliseconds * bytesPerMillisecond);
            endBytes = endBytes - endBytes % inPath.WaveFormat.BlockAlign;
            int endPos = (int)inPath.Length - endBytes;

            await TrimWavFile(inPath, writer, startPos, endPos);
        }
    }

    private static async Task TrimWavFile(WaveFileReader reader, WaveFileWriter writer, int startPos, int endPos)
    {
        reader.Position = startPos;
        byte[] buffer = new byte[1024];
        while (reader.Position < endPos)
        {
            int bytesRequired = (int)(endPos - reader.Position);
            if (bytesRequired > 0)
            {
                int bytesToRead = Math.Min(bytesRequired, buffer.Length);
                int bytesRead = reader.Read(buffer, 0, bytesToRead);
                if (bytesRead > 0)
                {
                    await writer.WriteAsync(buffer, 0, bytesRead);
                }
            }
        }
    }
}
