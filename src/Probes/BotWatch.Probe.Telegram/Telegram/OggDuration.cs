using System.Buffers.Binary;

namespace BotWatch.Probe.Telegram.Telegram;

// Telegram просит длительность голосового. берём её из последней страницы ogg:
// там позиция в сэмплах, а у opus она всегда в 48 кГц, какой бы ни была исходная частота
internal static class OggDuration
{
    private const int OpusSampleRate = 48000;
    private const int PageHeaderSize = 27;

    public static int Seconds(byte[] ogg)
    {
        for (var i = ogg.Length - PageHeaderSize; i >= 0; i--)
        {
            if (ogg[i] != 'O' || ogg[i + 1] != 'g' || ogg[i + 2] != 'g' || ogg[i + 3] != 'S')
            {
                continue;
            }

            var granule = BinaryPrimitives.ReadInt64LittleEndian(ogg.AsSpan(i + 6, 8));
            return granule > 0 ? (int)Math.Ceiling(granule / (double)OpusSampleRate) : 1;
        }

        return 1;
    }
}
