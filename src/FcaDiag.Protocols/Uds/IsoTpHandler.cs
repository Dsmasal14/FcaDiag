namespace FcaDiag.Protocols.Uds;

/// <summary>
/// ISO-TP (ISO 15765-2) transport protocol handler for multi-frame CAN messages
/// </summary>
public static class IsoTpHandler
{
    private const int SingleFrameMaxData = 7;
    private const int FirstFrameMaxData = 6;
    private const int ConsecutiveFrameMaxData = 7;

    /// <summary>
    /// Frame types in ISO-TP protocol
    /// </summary>
    public enum FrameType : byte
    {
        SingleFrame = 0x0,
        FirstFrame = 0x1,
        ConsecutiveFrame = 0x2,
        FlowControl = 0x3
    }

    /// <summary>
    /// Segment data into ISO-TP frames
    /// </summary>
    public static List<byte[]> SegmentData(byte[] data)
    {
        var frames = new List<byte[]>();

        if (data.Length <= SingleFrameMaxData)
        {
            // Single frame
            var frame = new byte[8];
            frame[0] = (byte)(((int)FrameType.SingleFrame << 4) | data.Length);
            Array.Copy(data, 0, frame, 1, data.Length);
            frames.Add(frame);
        }
        else
        {
            // First frame
            var firstFrame = new byte[8];
            firstFrame[0] = (byte)(((int)FrameType.FirstFrame << 4) | ((data.Length >> 8) & 0x0F));
            firstFrame[1] = (byte)(data.Length & 0xFF);
            Array.Copy(data, 0, firstFrame, 2, FirstFrameMaxData);
            frames.Add(firstFrame);

            // Consecutive frames
            int remaining = data.Length - FirstFrameMaxData;
            int offset = FirstFrameMaxData;
            byte sequenceNumber = 1;

            while (remaining > 0)
            {
                var cfFrame = new byte[8];
                cfFrame[0] = (byte)(((int)FrameType.ConsecutiveFrame << 4) | (sequenceNumber & 0x0F));

                int copyLength = Math.Min(remaining, ConsecutiveFrameMaxData);
                Array.Copy(data, offset, cfFrame, 1, copyLength);
                frames.Add(cfFrame);

                offset += copyLength;
                remaining -= copyLength;
                sequenceNumber = (byte)((sequenceNumber + 1) & 0x0F);
            }
        }

        return frames;
    }

    /// <summary>
    /// Reassemble ISO-TP frames into complete data
    /// </summary>
    public static byte[]? ReassembleData(List<byte[]> frames)
    {
        if (frames.Count == 0)
            return null;

        var firstFrame = frames[0];
        var frameType = (FrameType)(firstFrame[0] >> 4);

        if (frameType == FrameType.SingleFrame)
        {
            int length = firstFrame[0] & 0x0F;
            var data = new byte[length];
            Array.Copy(firstFrame, 1, data, 0, length);
            return data;
        }

        if (frameType == FrameType.FirstFrame)
        {
            int totalLength = ((firstFrame[0] & 0x0F) << 8) | firstFrame[1];
            var data = new byte[totalLength];

            // Copy first frame data
            int copied = Math.Min(FirstFrameMaxData, totalLength);
            Array.Copy(firstFrame, 2, data, 0, copied);

            // Copy consecutive frame data
            for (int i = 1; i < frames.Count && copied < totalLength; i++)
            {
                var cf = frames[i];
                int copyLength = Math.Min(ConsecutiveFrameMaxData, totalLength - copied);
                Array.Copy(cf, 1, data, copied, copyLength);
                copied += copyLength;
            }

            return data;
        }

        return null;
    }

    /// <summary>
    /// Create a flow control frame
    /// </summary>
    public static byte[] CreateFlowControl(byte status = 0, byte blockSize = 0, byte separationTime = 0)
    {
        return
        [
            (byte)(((int)FrameType.FlowControl << 4) | status),
            blockSize,
            separationTime,
            0, 0, 0, 0, 0
        ];
    }
}
