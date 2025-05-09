using Vint.Core.Server.Game.Protocol.Codecs.Buffer;

namespace Vint.Core.Server.Game.Protocol.Codecs.Impl;

public class DateTimeCodec : Codec {
    public override void Encode(ProtocolBuffer buffer, object value) =>
        buffer.Writer.Write(new DateTimeOffset((DateTime)value).ToUnixTimeMilliseconds());

    public override object Decode(ProtocolBuffer buffer) =>
        DateTimeOffset.FromUnixTimeMilliseconds(buffer.Reader.ReadInt64()).UtcDateTime;
}
