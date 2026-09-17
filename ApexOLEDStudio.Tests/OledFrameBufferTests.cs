using ApexOLEDStudio.Core.Drawing;
using Xunit;

namespace ApexOLEDStudio.Tests;

public class OledFrameBufferTests
{
    [Fact]
    public void Clear_SetsAllPixelsToFalse()
    {
        var buffer = new OledFrameBuffer();
        buffer.SetPixel(10, 10, true);
        buffer.Clear(false);

        Assert.False(buffer.GetPixel(10, 10));
    }

    [Fact]
    public void SetPixel_SetsCorrectCoordinates()
    {
        var buffer = new OledFrameBuffer();
        buffer.SetPixel(0, 0, true);
        buffer.SetPixel(127, 39, true);

        Assert.True(buffer.GetPixel(0, 0));
        Assert.True(buffer.GetPixel(127, 39));
        Assert.False(buffer.GetPixel(1, 0));
        Assert.False(buffer.GetPixel(0, 1));
    }

    [Fact]
    public void ToRawApexBytes_PacksRowMajorMsbCorrectly()
    {
        var buffer = new OledFrameBuffer();
        // Pixel (0,0) is MSB of first byte -> 0x80
        buffer.SetPixel(0, 0, true);

        // Pixel (7,0) is LSB of first byte -> 0x01
        buffer.SetPixel(7, 0, true);

        // Pixel (0,1) is MSB of 17th byte (index 16) -> 0x80
        buffer.SetPixel(0, 1, true);

        byte[] raw = buffer.ToRawApexBytes();

        Assert.Equal(640, raw.Length);
        Assert.Equal(0x81, raw[0]); // 0x80 | 0x01
        Assert.Equal(0x80, raw[16]);
    }

    [Fact]
    public void ToApexHidPacket_BuildsValid643BytePacket()
    {
        var buffer = new OledFrameBuffer();
        buffer.SetPixel(0, 0, true);

        byte[] packet = buffer.ToApexHidPacket();

        Assert.Equal(643, packet.Length);
        Assert.Equal(0x00, packet[0]); // Windows Report ID
        Assert.Equal(0x61, packet[1]); // Apex OLED command
        Assert.Equal(0x80, packet[2]); // First data byte
        Assert.Equal(0x00, packet[642]); // Padding
    }

    [Fact]
    public void DrawText_RendersAsciiCharacters()
    {
        var buffer = new OledFrameBuffer();
        int nextX = buffer.DrawText(0, 0, "CPU: 50%");

        Assert.True(nextX > 0);
        // At least some pixels must be turned on
        bool anyPixelOn = false;
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 50; x++)
            {
                if (buffer.GetPixel(x, y))
                {
                    anyPixelOn = true;
                    break;
                }
            }
        }
        Assert.True(anyPixelOn);
    }
}
